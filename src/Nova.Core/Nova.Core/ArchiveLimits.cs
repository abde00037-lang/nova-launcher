using System;
using System.IO;
using System.IO.Compression;

namespace Nova.Core;

/// <summary>Configurable limits for untrusted archive extraction.</summary>
public sealed class ArchiveLimits
{
    public int MaxEntries { get; init; } = 100_000;
    public long MaxFileBytes { get; init; } = 2L * 1024 * 1024 * 1024;
    public long MaxTotalBytes { get; init; } = 16L * 1024 * 1024 * 1024;
    public long MaxManifestBytes { get; init; } = 32L * 1024 * 1024;
    public static ArchiveLimits Default => new ArchiveLimits
    {
        MaxEntries = checked((int)ReadLimit("NOVA_ARCHIVE_MAX_ENTRIES", 100_000)),
        MaxFileBytes = ReadLimit("NOVA_ARCHIVE_MAX_FILE_BYTES", 2L * 1024 * 1024 * 1024),
        MaxTotalBytes = ReadLimit("NOVA_ARCHIVE_MAX_TOTAL_BYTES", 16L * 1024 * 1024 * 1024),
        MaxManifestBytes = ReadLimit("NOVA_ARCHIVE_MAX_MANIFEST_BYTES", 32L * 1024 * 1024)
    };
    private static long ReadLimit(string name, long fallback)
    {
        string? value = Environment.GetEnvironmentVariable(name);
        if (string.IsNullOrEmpty(value)) return fallback;
        if (!long.TryParse(value, out long number) || number <= 0) throw new InvalidDataException("Invalid archive limit: " + name);
        return number;
    }

    public void ValidateArchive(ZipArchive archive)
    {
        if (MaxEntries < 1 || MaxFileBytes < 1 || MaxTotalBytes < 1 || MaxManifestBytes < 1)
            throw new ArgumentOutOfRangeException(nameof(archive), "Archive limits must be positive.");
        if (archive.Entries.Count > MaxEntries) throw new InvalidDataException("Archive has too many entries.");
        long total = 0;
        foreach (ZipArchiveEntry entry in archive.Entries)
        {
            long size = entry.Length;
            if (size < 0 || size > MaxFileBytes || size > MaxTotalBytes - total)
                throw new InvalidDataException("Archive exceeds extraction size limits.");
            total += size;
        }
    }
}

/// <summary>Counts actual decompressed bytes; metadata alone is never trusted.</summary>
internal sealed class ArchiveBudget
{
    private readonly ArchiveLimits limits;
    private long total;
    private int entries;
    internal ArchiveBudget(ArchiveLimits limits) => this.limits = limits;
    internal void BeginFile()
    {
        if (++entries > limits.MaxEntries) throw new InvalidDataException("Too many extracted files.");
    }
    internal void AddBytes(long count, long fileTotal)
    {
        if (fileTotal > limits.MaxFileBytes || count > limits.MaxTotalBytes - total)
            throw new InvalidDataException("Extraction exceeded size limits.");
        total += count;
    }
}
