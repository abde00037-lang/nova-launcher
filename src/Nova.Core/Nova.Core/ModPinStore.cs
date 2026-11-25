using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace Nova.Core;

/// <summary>Per-instance, filename-based pins. Pins affect Nova's updater, not external tools.</summary>
public static class ModPinStore
{
    private static string CanonicalName(string filename)
    {
        if (string.IsNullOrWhiteSpace(filename) || Path.GetFileName(filename) != filename ||
            filename.Contains('\\') || !ModManager.IsModFile(filename))
            throw new InvalidDataException("Invalid mod filename for pin.");
        return ModManager.DisplayName(filename).ToUpperInvariant();
    }

    private static string MarkerPath(string modsDirectory, string filename)
    {
        string key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(CanonicalName(filename))));
        return SafePath.ResolveWithin(modsDirectory, Path.Combine(".nova-mod-pins", key + ".pin"));
    }

    public static bool IsPinned(string modsDirectory, string filename)
    {
        string marker = MarkerPath(modsDirectory, filename);
        try { _ = File.GetAttributes(marker); return true; }
        catch (FileNotFoundException) { return false; }
        catch (DirectoryNotFoundException) { return false; }
    }

    public static void SetPinned(string modsDirectory, string filename, bool pinned)
    {
        Directory.CreateDirectory(modsDirectory);
        using FileStream updateLock = ModUpdater.AcquireModLock(modsDirectory);
        string mod = SafePath.ResolveWithin(modsDirectory, filename);
        if (!File.Exists(mod)) throw new FileNotFoundException("The mod no longer exists.", filename);
        string marker = MarkerPath(modsDirectory, filename);
        if (pinned)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(marker)!);
            SafePath.RejectLinks(modsDirectory, marker);
            // Empty marker is enough: existence is the pin and a corrupt marker still blocks updates.
            using var file = new FileStream(marker, FileMode.OpenOrCreate, FileAccess.Write, FileShare.None);
        }
        else if (File.Exists(marker)) File.Delete(marker);
    }
}
