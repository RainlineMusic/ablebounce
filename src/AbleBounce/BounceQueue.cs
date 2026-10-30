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
            var passes = jobs.SelectMany(job => job == 9
                ? dry_keys.Select(key => (Job: job, DryKey: (string?)key))
                : new[] { (Job: job, DryKey: (string?)null) }).ToArray();
            for (int i = 0; i < passes.Length; i++)
            {
                ct.ThrowIfCancellationRequested();
                int job = passes[i].Job; var dryTrack = passes[i].DryKey; jobState(job, "Preparing");
                report($"Preparing {i + 1}/{passes.Length}: {FileNames[job].Replace('_', ' ')}");
                await bridge.Call("prepare", new { job, dry_track = dryTrack, transaction = _transaction }, ct, timeoutSeconds: 30);
                var staging = Path.Combine(root, "Staging", job.ToString("D2"), i.ToString("D3"));
                Directory.CreateDirectory(staging);
                jobState(job, "Rendering");
                int expectedFiles = job == 8 ? source.Tracks.Count(t => t.Audio) : 1;
                var files = await _driver.Render(job == 9 ? 1 : job, bits, staging, expectedFiles, report, ct);
                foreach (var f in files)
                {
                    var wave = WaveFile.Inspect(f, bits);
                    if (range == null) range = wave;
                    else if (range.Frames != wave.Frames || range.SampleRate != wave.SampleRate || range.Bits != wave.Bits)
                        throw new IOException("The export range or sample rate changed between variants. The partial batch was kept for inspection.");
                }
                var finalFiles = Deliver(job, files, root, source, title, dryTrack == null ? dry_keys : [dryTrack]);
                delivered.Add(new { variant = job, name = FileNames[job], files = finalFiles.Select(f => Path.GetRelativePath(root, f)).ToArray(),
                    frames = range!.Frames, sample_rate = range.SampleRate });
                await bridge.Call("restore", new { transaction = _transaction, final = false }, ct);
                jobState(job, i + 1 < passes.Length && passes[i + 1].Job == job ? "Rendering" : "Done");
                File.WriteAllText(Path.Combine(root, "manifest.json"), JsonSerializer.Serialize(new
                {
                    application = "glucose 0.2.8", live_version = source.Version, source_set = source.Path, track_title = title,
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
        // Only a fully delivered, restored batch may discard its temporary files.
        var stagingRoot = Path.Combine(root, "Staging");
        if (Directory.Exists(stagingRoot)) Directory.Delete(stagingRoot, recursive: true);
        return root;
    }

    internal static TrackInfo? MatchNativeTrack(string file, SessionInfo source)
    {
        static string Canonical(string name) => new(name.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
        var name = Path.GetFileNameWithoutExtension(file);
        if (!name.StartsWith("glucose", StringComparison.OrdinalIgnoreCase))
            throw new IOException("Unexpected native export filename: " + name);
        var remainder = name[7..].TrimStart(' ', '_', '-', '.');
        var key = Canonical(remainder);
        var tracks = source.Tracks.Where(t => !t.Master && !t.Return).ToArray();
        var returns = source.Tracks.Where(t => t.Return).ToArray();
        // Live expands # to the project track number. Prefer that explicit
        // native number before name-only matching (duplicate names are normal).
        var numbered = Regex.Match(remainder, @"^(\d+)[ ._\-]+(.+)$");
        if (numbered.Success && int.TryParse(numbered.Groups[1].Value, out int number) && number > 0 && number <= tracks.Length)
        {
            var track = tracks[number - 1];
            var original = track.Name.Replace("#", "").Trim(' ', '-', '_', '.');
            if (key == Canonical(track.Name.Replace("#", number.ToString()))
                || key == Canonical(track.Name) || Canonical(numbered.Groups[2].Value) == Canonical(original)) return track;
        }
        var matches = new List<TrackInfo>();
        foreach (var track in source.Tracks.Where(t => !t.Master))
        {
            int index = Array.FindIndex(track.Return ? returns : tracks, t => t.Key == track.Key) + 1;
            if (key == Canonical(track.Name) || key == Canonical(track.Name.Replace("#", track.Return && index <= 26 ? ((char)('A' + index - 1)).ToString() : index.ToString())) || (!track.Return &&
                (key == Canonical(index + "-" + track.Name) || key == Canonical(index.ToString("D3") + "-" + track.Name) || key == Canonical(index.ToString("D2") + "-" + track.Name)))
                || (track.Return && index <= 26 && key == Canonical(((char)('A' + index - 1)).ToString() + "-" + track.Name)))
                matches.Add(track);
        }
        bool main = key.Length == 0 || key == "main" || key == "master"
            || source.Tracks.Any(t => t.Master && Canonical(t.Name) == key);
        if (matches.Count == 1 && !main) return matches[0];
        if (matches.Count == 0 && main) return null;
        throw new IOException("Cannot uniquely match native export file to a track: " + name + ". Staging files were kept.");
    }

    internal static string TrackFileLabel(string name, int ordinal)
    {
        var clean = name.Replace("#", "").Trim(' ', '_', '-', '.');
        var number = Regex.Match(clean, @"^(\d+)[ ._\-]+(.+)$");
        if (number.Success && int.TryParse(number.Groups[1].Value, out var parsed) && parsed == ordinal)
            clean = number.Groups[2].Value;
        return Storage.SafeName(clean);
    }

    internal static List<string> Deliver(int job, List<string> files, string root, SessionInfo source, string title, string[] dryKeys)
    {
        var staging = Path.GetDirectoryName(files[0])!;
        var planned = new List<(string Source, string Target)>();
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var tracks = source.Tracks.Where(t => !t.Return && !t.Master).ToArray();
        var returns = source.Tracks.Where(t => t.Return).ToArray();
        string Target(string folder, string stem)
        {
            var path = Path.Combine(folder, stem + ".wav"); int duplicate = 2;
            while (File.Exists(path) || !used.Add(path)) path = Path.Combine(folder, stem + "_" + duplicate++ + ".wav");
            return path;
        }
        if (job == 9)
        {
            var track = source.Tracks.Single(t => t.Key == dryKeys.Single());
            int ordinal = Array.FindIndex(tracks, t => t.Key == track.Key) + 1;
            var folder = Path.Combine(root, "Multitrack", "Dry Tracks");
            planned.Add((files.Single(), Target(folder, $"{title}_{ordinal:D3}_{TrackFileLabel(track.Name, ordinal)}")));
        }
        else if (job == 8)
        {
            foreach (var file in files.OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase))
            {
                TrackInfo? track;
                try { track = MatchNativeTrack(file, source); }
                catch (IOException)
                {
                    // Name-only duplicates can still be categorised when every
                    // candidate belongs to the same kind. Never guess group vs track.
                    static string Canonical(string name) => new(name.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
                    var native = Path.GetFileNameWithoutExtension(file);
                    var label = native.StartsWith("glucose", StringComparison.OrdinalIgnoreCase) ? native[7..].Trim(' ', '-', '_', '.') : native;
                    var candidates = source.Tracks.Where(t => !t.Master && Canonical(t.Name) == Canonical(label)).ToArray();
                    if (candidates.Length < 2 || candidates.Select(t => (t.Group, t.Return)).Distinct().Count() != 1) throw;
                    track = candidates[0];
                }
                if (track == null)
                {
                    planned.Add((file, Target(Path.Combine(root, "Multitrack", "References"), title + "_MAIN")));
                    continue;
                }
                int ordinal = Array.FindIndex(track.Return ? returns : tracks, t => t.Key == track.Key) + 1;
                var folder = Path.Combine(root, "Multitrack", track.Return ? "Returns" : track.Group ? "Groups" : "Tracks");
                var prefix = track.Return ? "R" + ordinal.ToString("D2") : ordinal.ToString("D3");
                planned.Add((file, Target(folder, title + "_" + prefix + "_" + TrackFileLabel(track.Name, ordinal))));
            }
        }
        else planned.Add((files.Single(), Target(Path.Combine(root, "Mixes"), title + "_" + FileNames[job])));
        // Resolve every category and collision before moving the first file.
        foreach (var item in planned)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(item.Target)!);
            File.Move(item.Source, item.Target);
        }
        if (!Directory.EnumerateFileSystemEntries(staging).Any()) Directory.Delete(staging);
        return planned.Select(p => p.Target).ToList();
    }
}
