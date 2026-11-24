using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;

namespace Nova.Core;

public static class NativeExtractor
{
    public static int ExtractAll(IEnumerable<string> nativeJars, string targetDirectory, bool clean = true, ArchiveLimits? limits = null)
    {
        limits ??= ArchiveLimits.Default;
        string target = Path.GetFullPath(targetDirectory);
        string parent = Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(target)) ?? throw new InvalidDataException("Invalid native target.");
        if (Path.GetPathRoot(target) == target) throw new InvalidDataException("Cannot extract natives to a filesystem root.");
        SafePath.RejectLinks(target, target);
        string staging = target + ".nova-stage-" + Guid.NewGuid().ToString("N");
        string backup = target + ".nova-old-" + Guid.NewGuid().ToString("N");
        List<ZipArchive> archives = new List<ZipArchive>();
        bool movedOld = false;
        try
        {
            // Validate every input before deleting or creating anything in targetDirectory.
            long totalEntries = 0;
            foreach (string jar in nativeJars.Where(File.Exists))
            {
                ZipArchive zip = ZipFile.OpenRead(jar);
                archives.Add(zip);
                totalEntries += zip.Entries.Count;
                if (totalEntries > limits.MaxEntries) throw new InvalidDataException("Native archives contain too many entries.");
                SafeArchive.Validate(zip, staging, "", limits, zip.Entries.Where(e => e.FullName.StartsWith("META-INF/", StringComparison.OrdinalIgnoreCase)).Select(e => e.FullName).ToArray());
            }
            var budget = new ArchiveBudget(limits);
            Directory.CreateDirectory(parent);
            Directory.CreateDirectory(staging);
            int count = 0;
            foreach (ZipArchive zip in archives)
            {
                string[] skip = zip.Entries.Where(e => e.FullName.StartsWith("META-INF/", StringComparison.OrdinalIgnoreCase)).Select(e => e.FullName).ToArray();
                count += SafeArchive.Extract(zip, staging, "", limits, budget, skip);
            }
            SafePath.RejectLinks(target, target);
            if (clean)
            {
                if (Directory.Exists(target)) { Directory.Move(target, backup); movedOld = true; }
                try { Directory.Move(staging, target); }
                catch { if (movedOld) { Directory.Move(backup, target); movedOld = false; } throw; }
                if (movedOld) { Directory.Delete(backup, true); movedOld = false; }
            }
            else
            {
                foreach (string file in Directory.EnumerateFiles(staging, "*", SearchOption.AllDirectories))
                {
                    string relative = Path.GetRelativePath(staging, file);
                    string dest = SafePath.ResolveWithin(target, relative);
                    Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                    SafePath.ResolveWithin(target, relative);
                    File.Move(file, dest, true);
                }
            }
            return count;
        }
        finally
        {
            foreach (ZipArchive zip in archives) zip.Dispose();
            if (Directory.Exists(staging)) Directory.Delete(staging, true);
            // If an old directory cannot be restored, retain it instead of deleting user files.
        }
    }
}
