using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Nova.Core;

public sealed record ModSnapshotInfo(string Id, DateTimeOffset CreatedUtc, int FileCount);

/// <summary>A verified copy of the instance's top-level mod files, not a world or full instance backup.</summary>
public static class ModSnapshot
{
    private sealed record Entry(string FileName, string Sha256);
    private sealed record Manifest(DateTimeOffset CreatedUtc, List<Entry> Files);

    private static async Task<string> Hash(string path, CancellationToken ct)
    {
        await using FileStream stream = File.OpenRead(path);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, ct).ConfigureAwait(false));
    }

    /// <summary>Lists well-formed snapshot manifests. File contents are verified when a snapshot is restored.</summary>
    public static async Task<IReadOnlyList<ModSnapshotInfo>> ListAsync(string modsDirectory, CancellationToken ct = default)
    {
        string root = SafePath.ResolveWithin(modsDirectory, ".nova-mod-snapshots");
        if (!Directory.Exists(root)) return Array.Empty<ModSnapshotInfo>();
        var result = new List<ModSnapshotInfo>();
        foreach (string directory in Directory.EnumerateDirectories(root))
        {
            ct.ThrowIfCancellationRequested();
            string id = Path.GetFileName(directory);
            if (id.Length != 32 || !Guid.TryParseExact(id, "N", out _)) continue;
            try
            {
                SafePath.RejectLinks(modsDirectory, directory);
                string manifestPath = SafePath.ResolveWithin(directory, "manifest.json");
                if (!File.Exists(manifestPath)) continue;
                Manifest? manifest = JsonSerializer.Deserialize<Manifest>(await File.ReadAllTextAsync(manifestPath, ct).ConfigureAwait(false));
                if (manifest?.Files == null || manifest.Files.Any(entry =>
                    string.IsNullOrWhiteSpace(entry.FileName) || Path.GetFileName(entry.FileName) != entry.FileName ||
                    !ModManager.IsModFile(entry.FileName) || entry.Sha256.Length != 64 ||
                    !entry.Sha256.All(Uri.IsHexDigit))) continue;
                result.Add(new ModSnapshotInfo(id, manifest.CreatedUtc, manifest.Files.Count));
            }
            catch (OperationCanceledException) { throw; }
            catch { /* Ignore incomplete, corrupt, or linked snapshot folders; restore verifies independently. */ }
        }
        return result.OrderByDescending(snapshot => snapshot.CreatedUtc).ToArray();
    }

    public static async Task<string> CreateAsync(string modsDirectory, CancellationToken ct = default)
    {
        Directory.CreateDirectory(modsDirectory);
        using FileStream updateLock = ModUpdater.AcquireModLock(modsDirectory);
        string root = SafePath.ResolveWithin(modsDirectory, ".nova-mod-snapshots");
        Directory.CreateDirectory(root);
        string id = Guid.NewGuid().ToString("N");
        string staging = SafePath.ResolveWithin(root, ".partial-" + id);
        string target = SafePath.ResolveWithin(root, id);
        Directory.CreateDirectory(staging);
        try
        {
            string filesFolder = SafePath.ResolveWithin(staging, "files");
            Directory.CreateDirectory(filesFolder);
            string[] names = Directory.EnumerateFiles(modsDirectory).Select(Path.GetFileName)
                .Where(n => n != null && ModManager.IsModFile(n)).Select(n => n!).OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToArray();
            var entries = new List<Entry>();
            foreach (string name in names)
            {
                ct.ThrowIfCancellationRequested();
                string source = SafePath.ResolveWithin(modsDirectory, name);
                string destination = SafePath.ResolveWithin(filesFolder, name);
                File.Copy(source, destination, false);
                string copied = await Hash(destination, ct).ConfigureAwait(false);
                if (!string.Equals(copied, await Hash(source, ct).ConfigureAwait(false), StringComparison.Ordinal))
                    throw new IOException("A mod changed while the snapshot was being created: " + name);
                entries.Add(new Entry(name, copied));
            }
            // Fail if the mod list or any file changed during the copy.
            string[] current = Directory.EnumerateFiles(modsDirectory).Select(Path.GetFileName)
                .Where(n => n != null && ModManager.IsModFile(n)).Select(n => n!).OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToArray();
            if (!names.SequenceEqual(current, StringComparer.OrdinalIgnoreCase)) throw new IOException("The mod list changed during snapshot creation.");
            foreach (Entry entry in entries)
                if (!string.Equals(entry.Sha256, await Hash(SafePath.ResolveWithin(modsDirectory, entry.FileName), ct).ConfigureAwait(false), StringComparison.Ordinal))
                    throw new IOException("A mod changed during snapshot creation: " + entry.FileName);
            await File.WriteAllTextAsync(SafePath.ResolveWithin(staging, "manifest.json"), JsonSerializer.Serialize(new Manifest(DateTimeOffset.UtcNow, entries)), ct).ConfigureAwait(false);
            SafePath.RejectLinks(modsDirectory, staging);
            Directory.Move(staging, target);
            return target;
        }
        catch
        {
            try { if (Directory.Exists(staging)) Directory.Delete(staging, true); } catch { }
            throw;
        }
    }

    /// <summary>Copies verified snapshot files back over the mods folder and disables mod files added after the snapshot. Nothing is deleted.</summary>
    public static async Task<string> RestoreAsync(string modsDirectory, string snapshotId, CancellationToken ct = default)
    {
        if (snapshotId.Length != 32 || !Guid.TryParseExact(snapshotId, "N", out _)) throw new ArgumentException("Snapshot id must be the 32-char folder name under .nova-mod-snapshots.");
        string root = SafePath.ResolveWithin(modsDirectory, ".nova-mod-snapshots");
        string target = SafePath.ResolveWithin(root, snapshotId);
        string manifestPath = SafePath.ResolveWithin(target, "manifest.json");
        if (!File.Exists(manifestPath)) throw new FileNotFoundException("Unknown mod snapshot " + snapshotId + ".", manifestPath);
        Manifest manifest = JsonSerializer.Deserialize<Manifest>(await File.ReadAllTextAsync(manifestPath, ct).ConfigureAwait(false))
            ?? throw new InvalidDataException("Invalid snapshot manifest.");
        using FileStream operationLock = ModUpdater.AcquireModLock(modsDirectory);
        int restored = 0;
        foreach (Entry entry in manifest.Files)
        {
            ct.ThrowIfCancellationRequested();
            string source = SafePath.ResolveWithin(SafePath.ResolveWithin(target, "files"), entry.FileName);
            string copied = await Hash(source, ct).ConfigureAwait(false);
            if (!string.Equals(copied, entry.Sha256, StringComparison.Ordinal)) throw new IOException("Snapshot file failed verification: " + entry.FileName);
            string destination = SafePath.ResolveWithin(modsDirectory, entry.FileName);
            File.Copy(source, destination, true);
            restored++;
        }
        HashSet<string> bases = new HashSet<string>(manifest.Files.Select(f => ModManager.DisplayName(f.FileName)), StringComparer.OrdinalIgnoreCase);
        int disabled = 0;
        foreach (string current in Directory.EnumerateFiles(modsDirectory))
        {
            string name = Path.GetFileName(current);
            if (!ModManager.IsModFile(name) || name.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase)) continue;
            if (bases.Contains(ModManager.DisplayName(name))) continue;
            File.Move(current, SafePath.ResolveWithin(modsDirectory, name + ".disabled"));
            disabled++;
        }
        return "restored " + restored + " files and disabled " + disabled + " newer mod files from snapshot " + snapshotId;
    }
}
