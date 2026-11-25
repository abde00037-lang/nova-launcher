using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Nova.Core;

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
}
