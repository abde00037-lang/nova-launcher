using System;
using System.IO;
using System.Linq;

namespace Nova.Core;

/// <summary>Validates untrusted archive and manifest paths using Windows-compatible rules.</summary>
public static class SafePath
{
    public static string ResolveWithin(string root, string relativePath)
    {
        if (string.IsNullOrWhiteSpace(root)) throw new ArgumentException("A root directory is required.", nameof(root));
        if (string.IsNullOrWhiteSpace(relativePath)) throw new InvalidDataException("Empty archive path.");
        // Apply Windows path rules even on another OS so tests and archives behave consistently.
        string name = relativePath.Replace('\\', '/');
        if (name.StartsWith('/') || Path.IsPathRooted(name) || name.IndexOf(':') >= 0 || name.IndexOf('\0') >= 0)
            throw new InvalidDataException("Absolute or device paths are not permitted: " + relativePath);
        string[] parts = name.Split('/');
        foreach (string part in parts)
        {
            if (part.Length == 0 || part == "." || part == ".." || part.EndsWith(' ') || part.EndsWith('.') ||
                part.Any(c => c < 32 || c == '<' || c == '>' || c == '"' || c == '|' || c == '?' || c == '*'))
                throw new InvalidDataException("Unsafe archive path: " + relativePath);
            string device = part.Split('.')[0].ToUpperInvariant();
            if (device == "CON" || device == "PRN" || device == "AUX" || device == "NUL" ||
                (device.Length == 4 && (device.StartsWith("COM") || device.StartsWith("LPT")) && "123456789¹²³".IndexOf(device[3]) >= 0))
                throw new InvalidDataException("Reserved Windows path: " + relativePath);
        }
        string rootFull = Path.GetFullPath(root);
        string combined = Path.GetFullPath(Path.Combine(rootFull, Path.Combine(parts)));
        string boundary = Path.TrimEndingDirectorySeparator(rootFull) + Path.DirectorySeparatorChar;
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (!combined.StartsWith(boundary, comparison)) throw new InvalidDataException("Path escapes its destination: " + relativePath);
        RejectLinks(rootFull, combined);
        return combined;
    }

    // Existing links/reparse points below the chosen root could redirect writes outside it.
    public static void RejectLinks(string root, string destination)
    {
        string rootFull = Path.GetFullPath(root);
        string destFull = Path.GetFullPath(destination);
        string boundary = Path.TrimEndingDirectorySeparator(rootFull) + Path.DirectorySeparatorChar;
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (!destFull.Equals(rootFull, comparison) && !destFull.StartsWith(boundary, comparison))
            throw new InvalidDataException("Destination is outside the root.");
        CheckExisting(rootFull);
        string remainder = Path.GetRelativePath(rootFull, destFull);
        string current = rootFull;
        if (remainder == ".") return;
        foreach (string part in remainder.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
        {
            current = Path.Combine(current, part);
            CheckExisting(current);
        }
    }

    private static void CheckExisting(string path)
    {
        try
        {
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("Links and reparse points are not permitted in an extraction path: " + path);
        }
        catch (FileNotFoundException) { }
        catch (DirectoryNotFoundException) { }
    }
}
