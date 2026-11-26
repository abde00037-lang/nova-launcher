// Shared by the .NET Framework standalone installers (C# 5 compatible).
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;

internal static class SafeInstallerZip
{
    static long Limit(string name, long fallback)
    {
        string raw = Environment.GetEnvironmentVariable(name);
        if (string.IsNullOrEmpty(raw)) return fallback;
        long value;
        if (!long.TryParse(raw, out value) || value <= 0) throw new InvalidDataException("Invalid archive limit: " + name);
        return value;
    }
    internal static long DownloadLimit() { return Limit("NOVA_ARCHIVE_MAX_FILE_BYTES", 2L * 1024 * 1024 * 1024); }
    internal static string PathWithin(string root, string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) throw new InvalidDataException("Empty ZIP path.");
        string name = raw.Replace('\\', '/');
        if (name.StartsWith("/", StringComparison.Ordinal) || name.IndexOf(':') >= 0 || name.IndexOf('\0') >= 0 || Path.IsPathRooted(name))
            throw new InvalidDataException("Unsafe ZIP path: " + raw);
        string[] parts = name.Split('/');
        foreach (string part in parts)
        {
            if (part.Length == 0 || part == "." || part == ".." || part.EndsWith(".") || part.EndsWith(" ") || part.IndexOfAny(new[] { '<', '>', '"', '|', '?', '*' }) >= 0)
                throw new InvalidDataException("Unsafe ZIP path: " + raw);
            foreach (char c in part) if (c < 32) throw new InvalidDataException("Unsafe ZIP path: " + raw);
            string device = part.Split('.')[0].ToUpperInvariant();
            if (device == "CON" || device == "NUL" || device == "PRN" || device == "AUX" ||
                (device.Length == 4 && (device.StartsWith("COM") || device.StartsWith("LPT")) && "123456789¹²³".IndexOf(device[3]) >= 0))
                throw new InvalidDataException("Reserved ZIP path: " + raw);
        }
        string fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string result = Path.GetFullPath(Path.Combine(fullRoot, name.Replace('/', Path.DirectorySeparatorChar)));
        if (!result.StartsWith(fullRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("ZIP path escapes destination.");
        RejectLinks(fullRoot);
        string current = fullRoot;
        foreach (string part in parts) { current = Path.Combine(current, part); RejectLinks(current); }
        return result;
    }
    static void RejectLinks(string path)
    {
        try { if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("Reparse point in destination: " + path); }
        catch (FileNotFoundException) { } catch (DirectoryNotFoundException) { }
    }
    internal static void Extract(ZipArchive archive, string destination, Func<string, string> map)
    {
        long maxFiles = Limit("NOVA_ARCHIVE_MAX_ENTRIES", 100000);
        long maxFile = Limit("NOVA_ARCHIVE_MAX_FILE_BYTES", 2L * 1024 * 1024 * 1024);
        long maxTotal = Limit("NOVA_ARCHIVE_MAX_TOTAL_BYTES", 16L * 1024 * 1024 * 1024);
        if (archive.Entries.Count > maxFiles) throw new InvalidDataException("Too many ZIP entries.");
        long total = 0;
        var entries = new List<KeyValuePair<ZipArchiveEntry, string>>();
        foreach (ZipArchiveEntry entry in archive.Entries)
        {
            string raw = entry.FullName.TrimEnd('/', '\\');
            if (raw.Length == 0) continue;
            PathWithin(destination, raw);
            if (((entry.ExternalAttributes >> 16) & 0xF000) == 0xA000) throw new InvalidDataException("ZIP symlinks are not allowed.");
            if (entry.Length < 0 || entry.Length > maxFile || entry.Length > maxTotal - total) throw new InvalidDataException("ZIP size limit exceeded.");
            total += entry.Length;
            if (entry.FullName.EndsWith("/") || entry.FullName.EndsWith("\\")) continue;
            string mapped = map(raw);
            PathWithin(destination, mapped);
            entries.Add(new KeyValuePair<ZipArchiveEntry, string>(entry, mapped));
        }
        total = 0;
        foreach (var item in entries)
        {
            string target = PathWithin(destination, item.Value);
            Directory.CreateDirectory(Path.GetDirectoryName(target));
            PathWithin(destination, item.Value);
            string temp = target + "." + Guid.NewGuid().ToString("N") + ".part";
            try
            {
                long fileTotal = 0;
                using (Stream input = item.Key.Open()) using (Stream output = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    byte[] bytes = new byte[81920]; int count;
                    while ((count = input.Read(bytes, 0, bytes.Length)) > 0)
                    {
                        fileTotal += count;
                        if (fileTotal > maxFile || count > maxTotal - total) throw new InvalidDataException("ZIP extraction size limit exceeded.");
                        total += count; output.Write(bytes, 0, count);
                    }
                    if (fileTotal != item.Key.Length) throw new InvalidDataException("ZIP entry length mismatch.");
                }
                PathWithin(destination, item.Value);
                if (File.Exists(target)) File.Delete(target);
                File.Move(temp, target);
            }
            finally { if (File.Exists(temp)) File.Delete(temp); }
        }
    }
    internal static void RemoveDeprecatedAiData(string targetRoot)
    {
        string aiDirectory = PathWithin(targetRoot, "ai");
        if (File.Exists(aiDirectory) && !Directory.Exists(aiDirectory))
        {
            File.Delete(aiDirectory);
            return;
        }
        if (!Directory.Exists(aiDirectory)) return;

        // Delete only the two known AI data files. Do not recursively traverse arbitrary user content.
        foreach (string name in new[] { "providers.json", "keys.dat" })
        {
            string file = PathWithin(targetRoot, "ai/" + name);
            if (File.Exists(file)) File.Delete(file);
        }
        if (Directory.GetFileSystemEntries(aiDirectory).Length == 0) Directory.Delete(aiDirectory, false);
    }

    internal static void CopyFiles(string source, string target)
    {
        foreach (string file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
        {
            string rel = file.Substring(source.TrimEnd(Path.DirectorySeparatorChar).Length).TrimStart(Path.DirectorySeparatorChar);
            string dest = PathWithin(target, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(dest));
            PathWithin(target, rel);
            File.Copy(file, dest, true);
        }
    }
}
