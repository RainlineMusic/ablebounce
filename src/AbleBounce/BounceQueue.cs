using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace AbleBounce;

public sealed class BounceQueue(BridgeClient bridge, ExportProfile profile, Action<string> report, Action<int, string> jobState)
{
    private readonly ExportDriver _driver = new(profile, bridge);
    private string? _transaction;
    public bool NeedsRestore => _transaction != null;
    private static readonly string[] FileNames = ["", "Full_Mix", "Instrumental", "Instrumental_No_Master", "Full_Mix_No_Master",
        "Backing_Mix_No_Master", "Backing_Mix", "Full_Mix_Lead_Minus_6dB", "Multitrack", "Dry_Lead_Vocal"];

    public async Task<string> Run(SessionInfo source, Dictionary<string, string> roles, int[] jobs, int bits, string output, CancellationToken ct)
    {
        profile.Validate(source);
        var current = await bridge.Scan(ct);
        if (current.Path != source.Path || !current.Tracks.Select(t => (t.Key, t.Name, t.Parent)).SequenceEqual(source.Tracks.Select(t => (t.Key, t.Name, t.Parent))))
            throw new InvalidOperationException("The Set changed after scanning. Connect again to review its track roles.");
        if (current.Transaction != null) throw new InvalidOperationException("Restore the unfinished working Set before starting a new batch.");
        await bridge.Call("preflight", new { jobs, roles }, ct);
        var root = Path.Combine(Path.GetFullPath(output), Storage.SafeName(Path.GetFileNameWithoutExtension(source.Path)) + "_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + "_" + Guid.NewGuid().ToString("N")[..5]);
        Directory.CreateDirectory(root);
        report("Creating a separate working Set in the same project folder…");
        string working = await _driver.CreateWorkingCopy(source, ct);
        var workingScan = await bridge.Scan(ct);
        if (!workingScan.Tracks.Select(t => (t.Key, t.Name, t.Parent)).SequenceEqual(source.Tracks.Select(t => (t.Key, t.Name, t.Parent))))
            throw new InvalidOperationException("The Set structure changed while creating the working copy. Connect again and review roles.");
        var begun = await bridge.Call("begin", new { jobs, roles }, ct);
        _transaction = begun.GetProperty("transaction").GetString() ?? throw new IOException("Live did not create a transaction.");
        Storage.Write("last-session.json", new { source = source.Path, working, output = root, transaction = _transaction, state = "running" });
        var delivered = new List<object>();
        WaveInfo? range = null;
        Exception? originalError = null;
        try
        {
            for (int i = 0; i < jobs.Length; i++)
            {
                ct.ThrowIfCancellationRequested();
                int job = jobs[i]; jobState(job, "Preparing");
                report($"Preparing {i + 1}/{jobs.Length}: {FileNames[job].Replace('_', ' ')}");
                await bridge.Call("prepare", new { job, transaction = _transaction }, ct, timeoutSeconds: 30);
                var staging = Path.Combine(root, "Staging", job.ToString("D2"));
                Directory.CreateDirectory(staging);
                jobState(job, "Rendering");
                int expectedFiles = job == 8 ? source.Tracks.Count(t => t.Audio) : 1;
                var files = await _driver.Render(job, bits, staging, expectedFiles, report, ct);
                foreach (var f in files)
                {
                    var wave = WaveFile.Inspect(f, bits);
                    if (range == null) range = wave;
                    else if (range.Frames != wave.Frames || range.SampleRate != wave.SampleRate)
                        throw new IOException("The export range or sample rate changed between variants. The partial batch was kept for inspection.");
                }
                var finalFiles = Deliver(job, files, root, source);
                delivered.Add(new { variant = job, name = FileNames[job], files = finalFiles.Select(f => Path.GetRelativePath(root, f)).ToArray(),
                    frames = range!.Frames, sample_rate = range.SampleRate });
                await bridge.Call("restore", new { transaction = _transaction, final = false }, ct);
                jobState(job, "Done");
                File.WriteAllText(Path.Combine(root, "manifest.json"), JsonSerializer.Serialize(new
                {
                    application = "AbleBounce 0.1.1", live_version = source.Version, source_set = source.Path,
                    working_set = working, bit_depth = bits, roles,
                    range = "Reused Live Export dialog start/length; sample rate set in Live", deliveries = delivered,
                    multitrack_note = "Tracks are pre-parent-bus, post-fader. Groups/Returns are alternatives; do not sum every group with all its children.",
                    lead_trim_note = "A final Utility trims each selected lead root by -6 dB, including its own sends; descendant sends bypassing a selected group retain their original level."
                }, Storage.Json));
            }
        }
        catch (Exception ex) { originalError = ex; throw; }
        finally
        {
            try
            {
                report("Restoring the working Set…");
                await _driver.EnsureStopped();
                await bridge.Call("restore", new { transaction = _transaction, final = true }, timeoutSeconds: 30);
                _transaction = null;
                Storage.Write("last-session.json", new { source = source.Path, working, output = root, state = originalError == null ? "complete" : "stopped_and_restored" });
                report(originalError == null ? "Batch complete. Working Set restored; original Set untouched." : "Batch stopped. Working Set restored; completed files kept.");
            }
            catch (Exception restoreError)
            {
                Storage.Write("last-session.json", new { source = source.Path, working, output = root, transaction = _transaction, state = "restore_required", error = restoreError.Message });
                throw new InvalidOperationException("Restoration needs attention: " + restoreError.Message + " Do not save the modified working Set. Cancel its render/dialog and use Restore working Set. Original error: " + (originalError?.Message ?? "none"), restoreError);
            }
        }
        return root;
    }

    private static List<string> Deliver(int job, List<string> files, string root, SessionInfo source)
    {
        var result = new List<string>();
        var staging = Path.GetDirectoryName(files[0])!;
        if (job != 8)
        {
            var folder = Path.Combine(root, job == 9 ? "Dry_Vocal" : "Mixes");
            Directory.CreateDirectory(folder);
            string final = Path.Combine(folder, $"{job:D2}_{FileNames[job]}.wav");
            File.Move(files.Single(), final); result.Add(final);
        }
        else
        {
            var mapped = new Dictionary<string, string>();
            var pending = new List<(string Source, string Target)>();
            var returns = source.Tracks.Where(t => t.Return).ToArray();
            var tracks = source.Tracks.Where(t => !t.Return && !t.Master).ToArray();
            int unlabelled = 0;
            foreach (var file in files)
            {
                var match = Regex.Match(Path.GetFileName(file), @"__AB_([TR])(\d{4})__");
                if (!match.Success) { unlabelled++; continue; } // native Main reference
                int index = int.Parse(match.Groups[2].Value) - 1;
                bool isReturn = match.Groups[1].Value == "R";
                var list = isReturn ? returns : tracks;
                if (index < 0 || index >= list.Length) throw new IOException("Unexpected exported track identifier.");
                var track = list[index];
                if (!mapped.TryAdd(track.Key, file)) throw new IOException("Duplicate exported track identifier: " + track.Name);
                var folder = Path.Combine(root, "Multitrack", isReturn ? "Returns" : track.Group ? "Groups" : "Tracks");
                var number = isReturn ? "R" + (index + 1).ToString("D2") : (index + 1).ToString("D3");
                pending.Add((file, Path.Combine(folder, number + "_" + Storage.SafeName(track.Name) + ".wav")));
            }
            var missing = source.Tracks.Where(t => t.Audio && !t.Master && !mapped.ContainsKey(t.Key)).Select(t => t.Name).ToArray();
            if (missing.Length > 0 || unlabelled > 1)
                throw new IOException("Multitrack export could not be matched safely. Missing: " + string.Join(", ", missing) + ". Staging files were retained.");
            // Validate all identifiers before moving any file.
            foreach (var item in pending)
            { Directory.CreateDirectory(Path.GetDirectoryName(item.Target)!); File.Move(item.Source, item.Target); result.Add(item.Target); }
            foreach (var file in Directory.GetFiles(staging, "*.wav")) File.Delete(file); // unused native Main reference only
        }
        if (!Directory.EnumerateFileSystemEntries(staging).Any()) Directory.Delete(staging);
        return result;
    }
}
