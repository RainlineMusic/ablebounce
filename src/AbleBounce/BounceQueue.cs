using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace AbleBounce;

public sealed class BounceQueue(BridgeClient bridge, ExportProfile profile, Action<string> report, Action<int, string> jobState)
{
    private readonly ExportDriver _driver = new(profile, bridge);
    private string? _transaction;
    public bool NeedsRestore => _transaction != null;
    private static readonly string[] FileNames = ["", "MASTER", "INSTR_MASTER", "INSTR_MIX", "MIX",
        "BACK_MIX", "BACK_MASTER", "", "Multitrack", "Selected_Tracks_DRY"];

    public async Task<string> Run(SessionInfo source, Dictionary<string, string> roles, int[] jobs, int bits, string output, CancellationToken ct, string? trackTitle = null, string[]? dryKeys = null)
    {
        profile.Validate(source);
        var dry_keys = dryKeys ?? [];
        if (jobs.Contains(9) && dry_keys.Length == 0) throw new InvalidOperationException("Tick at least one track in the DRY column.");
        var current = await bridge.Scan(ct);
        if (current.Path != source.Path || !current.Tracks.Select(t => (t.Key, t.Name, t.Parent)).SequenceEqual(source.Tracks.Select(t => (t.Key, t.Name, t.Parent))))
            throw new InvalidOperationException("The Set changed after scanning. Connect again to review its track roles.");
        if (current.Transaction != null) throw new InvalidOperationException("Restore the unfinished current Set before starting a new batch.");
        await bridge.Call("preflight", new { jobs, roles, dry_keys }, ct);
        var title = Storage.SafeName(string.IsNullOrWhiteSpace(trackTitle)
            ? (string.IsNullOrWhiteSpace(source.Path) ? source.Name : Path.GetFileNameWithoutExtension(source.Path)) : trackTitle.Trim());
        var root = Path.Combine(Path.GetFullPath(output), title + "_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + "_" + Guid.NewGuid().ToString("N")[..5]);
        Directory.CreateDirectory(root);
        report("Exporting from the current open Set. No project files will be saved.");
        var begun = await bridge.Call("begin", new { jobs, roles, dry_keys }, ct);
        _transaction = begun.GetProperty("transaction").GetString() ?? throw new IOException("Live did not create a transaction.");
        var delivered = new List<object>();
        WaveInfo? range = null;
        Exception? originalError = null;
        try
        {
            Storage.Write("last-session.json", new { source = source.Path, output = root, transaction = _transaction, state = "running" });
            for (int i = 0; i < jobs.Length; i++)
            {
                ct.ThrowIfCancellationRequested();
                int job = jobs[i]; jobState(job, "Preparing");
                report($"Preparing {i + 1}/{jobs.Length}: {FileNames[job].Replace('_', ' ')}");
                await bridge.Call("prepare", new { job, transaction = _transaction }, ct, timeoutSeconds: 30);
                var staging = Path.Combine(root, "Staging", job.ToString("D2"));
                Directory.CreateDirectory(staging);
                jobState(job, "Rendering");
                int expectedFiles = job is 8 or 9 ? source.Tracks.Count(t => t.Audio) : 1;
                var files = await _driver.Render(job, bits, staging, expectedFiles, report, ct);
                foreach (var f in files)
                {
                    var wave = WaveFile.Inspect(f, bits);
                    if (range == null) range = wave;
                    else if (range.Frames != wave.Frames || range.SampleRate != wave.SampleRate || range.Bits != wave.Bits)
                        throw new IOException("The export range or sample rate changed between variants. The partial batch was kept for inspection.");
                }
                var finalFiles = Deliver(job, files, root, source, title, dry_keys);
                delivered.Add(new { variant = job, name = FileNames[job], files = finalFiles.Select(f => Path.GetRelativePath(root, f)).ToArray(),
                    frames = range!.Frames, sample_rate = range.SampleRate });
                await bridge.Call("restore", new { transaction = _transaction, final = false }, ct);
                jobState(job, "Done");
                File.WriteAllText(Path.Combine(root, "manifest.json"), JsonSerializer.Serialize(new
                {
                    application = "glucose 0.2.2", live_version = source.Version, source_set = source.Path, track_title = title,
                    bit_depth = range!.Bits, roles,
                    range = "Reused Live Export dialog start/length; sample rate set in Live", deliveries = delivered,
                    multitrack_note = "Tracks are pre-parent-bus, post-fader. Groups/Returns are alternatives; do not sum every group with all its children.",
                    dry_note = "Selected dry tracks bypass audio effects, sends, parent and master processing."
                }, Storage.Json));
            }
        }
        catch (Exception ex) { originalError = ex; throw; }
        finally
        {
            try
            {
                report("Restoring the current Set…");
                await _driver.EnsureStopped();
                await bridge.Call("restore", new { transaction = _transaction, final = true }, timeoutSeconds: 30);
                _transaction = null;
                Storage.Write("last-session.json", new { source = source.Path, output = root, state = originalError == null ? "complete" : "stopped_and_restored" });
                report(originalError == null ? "Batch complete. Current Set restored." : "Batch stopped. Current Set restored; completed files kept.");
            }
            catch (Exception restoreError)
            {
                Storage.Write("last-session.json", new { source = source.Path, output = root, transaction = _transaction, state = "restore_required", error = restoreError.Message });
                throw new InvalidOperationException("Restoration needs attention: " + restoreError.Message + " Do not save the modified current Set. Cancel its render/dialog and use Restore Set. Original error: " + (originalError?.Message ?? "none"), restoreError);
            }
        }
        return root;
    }

    private static List<string> Deliver(int job, List<string> files, string root, SessionInfo source, string title, string[] dryKeys)
    {
        var result = new List<string>();
        var staging = Path.GetDirectoryName(files[0])!;
        if (job != 8 && job != 9)
        {
            var folder = Path.Combine(root, job == 9 ? "Dry_Vocal" : "Mixes");
            Directory.CreateDirectory(folder);
            string final = Path.Combine(folder, $"{title}_{FileNames[job]}.wav");
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
                if (job == 9 && !dryKeys.Contains(track.Key)) continue;
                var folder = job == 9 ? Path.Combine(root, "Multitrack", "Dry Tracks") : Path.Combine(root, "Multitrack", isReturn ? "Returns" : track.Group ? "Groups" : "Tracks");
                var number = isReturn ? "R" + (index + 1).ToString("D2") : (index + 1).ToString("D3");
                pending.Add((file, Path.Combine(folder, title + "_" + number + "_" + Storage.SafeName(track.Name) + ".wav")));
            }
            var missing = source.Tracks.Where(t => t.Audio && !t.Master && (job != 9 || dryKeys.Contains(t.Key)) && !mapped.ContainsKey(t.Key)).Select(t => t.Name).ToArray();
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

