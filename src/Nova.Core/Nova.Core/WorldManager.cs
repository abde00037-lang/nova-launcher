using System;
using System.IO;
using System.Security.Cryptography;

namespace Nova.Core;

public static class WorldManager
{
    // worldFolder is the actual direct child folder of saves, not the display name.
    public static string ResolveWorld(string gameDirectory, string worldFolder)
    {
        if (string.IsNullOrWhiteSpace(worldFolder) || worldFolder != Path.GetFileName(worldFolder) || worldFolder.Contains('\\') || worldFolder.Contains('/'))
            throw new ArgumentException("Invalid world folder.");
        string world = SafePath.ResolveWithin(gameDirectory, Path.Combine("saves", worldFolder));
        string level = SafePath.ResolveWithin(world, "level.dat");
        if (!Directory.Exists(world) || !File.Exists(level)) throw new DirectoryNotFoundException("World or level.dat not found.");
        return world;
    }

    public static string ReadDisplayName(string gameDirectory, string worldFolder)
    {
        string world = ResolveWorld(gameDirectory, worldFolder);
        return WorldNbt.Read(SafePath.ResolveWithin(world, "level.dat")).Name;
    }

    private static FileStream OperationLock(string gameDirectory)
    {
        string path = SafePath.ResolveWithin(gameDirectory, ".nova-world-operation.lock");
        try { return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
        catch (IOException ex) { throw new IOException("Another Nova world operation is in progress.", ex); }
    }

    private static FileStream? LockSession(string world)
    {
        string path = SafePath.ResolveWithin(world, "session.lock");
        if (!File.Exists(path)) return null;
        FileStream? stream = null;
        try
        {
            stream = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite | FileShare.Delete);
            stream.Lock(0, 1);
            return stream;
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
        {
            stream?.Dispose();
            throw new IOException("The world may be open in Minecraft. Close the game before changing it.", ex);
        }
    }

    private static string Sha256(string file)
    {
        using var stream = File.OpenRead(file);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    // Keeps a verified copy of the original level.dat; does not rename its directory or touch region files.
    public static string Rename(string gameDirectory, string worldFolder, string newName)
    {
        using FileStream operation = OperationLock(gameDirectory);
        string world = ResolveWorld(gameDirectory, worldFolder);
        using FileStream? session = LockSession(world);
        string original = SafePath.ResolveWithin(world, "level.dat");
        string before = Sha256(original);
        var parsed = WorldNbt.Read(original);
        if (Sha256(original) != before) throw new IOException("World metadata changed; rename aborted.");
        byte[] replacement = WorldNbt.ReplaceName(parsed, newName);
        string backupDir = SafePath.ResolveWithin(gameDirectory, Path.Combine(".nova-world-metadata-backups", Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(backupDir);
        string backup = SafePath.ResolveWithin(backupDir, "level.dat");
        File.Copy(original, backup, false);
        if (Sha256(backup) != before || Sha256(original) != before) throw new IOException("Could not verify the world metadata backup.");
        string temp = SafePath.ResolveWithin(world, ".nova-level-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            WorldNbt.WriteGzip(temp, replacement);
            if (WorldNbt.Read(temp).Name != newName || Sha256(original) != before) throw new IOException("World metadata changed; rename aborted.");
            SafePath.RejectLinks(gameDirectory, original);
            File.Move(temp, original, overwrite: true);
            return backup;
        }
        finally { try { if (File.Exists(temp)) File.Delete(temp); } catch { } }
    }

    // Non-destructive deletion: moves the whole world into a private trash folder on the same volume.
    public static string MoveToTrash(string gameDirectory, string worldFolder)
    {
        using FileStream operation = OperationLock(gameDirectory);
        string world = ResolveWorld(gameDirectory, worldFolder);
        using FileStream? session = LockSession(world);
        string container = SafePath.ResolveWithin(gameDirectory, Path.Combine(".nova-world-trash", Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(container);
        string target = SafePath.ResolveWithin(container, worldFolder);
        SafePath.RejectLinks(gameDirectory, world);
        // Windows refuses to move a directory while our own lock handle is open.
        // Release only after the lock check; callers must keep Minecraft closed.
        session?.Dispose();
        try { Directory.Move(world, target); }
        catch { try { Directory.Delete(container, false); } catch { } throw; }
        return target;
    }
}
