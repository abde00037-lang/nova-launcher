using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;

namespace Nova.Core;

internal static class SafeArchive
{
    private static readonly uint[] CrcTable = BuildCrcTable();
    private static uint[] BuildCrcTable()
    {
        var table = new uint[256];
        for (uint i = 0; i < table.Length; i++)
        {
            uint value = i;
            for (int bit = 0; bit < 8; bit++) value = (value & 1) != 0 ? 0xEDB88320u ^ (value >> 1) : value >> 1;
            table[i] = value;
        }
        return table;
    }

    internal static int Extract(ZipArchive archive, string root, string prefix, ArchiveLimits limits, ArchiveBudget budget, string[]? skip = null, CancellationToken ct = default)
    {
        var selected = Validate(archive, root, prefix, limits, skip);
        // The entire list is checked before the first directory or output file is created.
        foreach (var item in selected)
        {
            ct.ThrowIfCancellationRequested();
            string destination = SafePath.ResolveWithin(root, item.Relative);
            budget.BeginFile();
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            SafePath.ResolveWithin(root, item.Relative);
            string temporary = destination + ".nova-" + Guid.NewGuid().ToString("N") + ".part";
            try
            {
                using (var input = item.Entry.Open())
                using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    byte[] buffer = new byte[81920];
                    long fileBytes = 0;
                    uint crc = 0xffffffffu;
                    int n;
                    while ((n = input.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        ct.ThrowIfCancellationRequested();
                        fileBytes += n;
                        budget.AddBytes(n, fileBytes);
                        for (int i = 0; i < n; i++) crc = CrcTable[(crc ^ buffer[i]) & 0xff] ^ (crc >> 8);
                        output.Write(buffer, 0, n);
                    }
                    if (fileBytes != item.Entry.Length || ~crc != item.Entry.Crc32)
                        throw new InvalidDataException("Archive entry is corrupt or its length is inaccurate.");
                }
                SafePath.ResolveWithin(root, item.Relative);
                File.Move(temporary, destination, overwrite: true);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
        return selected.Count;
    }

    internal static List<(ZipArchiveEntry Entry, string Relative)> Validate(ZipArchive archive, string root, string prefix, ArchiveLimits limits, string[]? skip = null)
    {
        limits.ValidateArchive(archive);
        var selected = new List<(ZipArchiveEntry Entry, string Relative)>();
        HashSet<string>? skipped = skip == null ? null : new HashSet<string>(skip, StringComparer.OrdinalIgnoreCase);
        foreach (var entry in archive.Entries)
        {
            if (!entry.FullName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ||
                (skipped != null && skipped.Contains(entry.FullName))) continue;
            string rel = entry.FullName.Substring(prefix.Length);
            if (rel.Length == 0) continue;
            bool directory = rel.EndsWith('/') || rel.EndsWith('\\');
            string checkedRel = directory ? rel.TrimEnd('/', '\\') : rel;
            if (checkedRel.Length == 0) continue;
            // Unix symlink metadata, never follow links or extract them as files.
            if (((entry.ExternalAttributes >> 16) & 0xF000) == 0xA000)
                throw new InvalidDataException("Archive symlinks are not permitted.");
            SafePath.ResolveWithin(root, checkedRel);
            if (!directory) selected.Add((entry, checkedRel));
        }
        return selected;
    }
}
