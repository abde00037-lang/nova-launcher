namespace Nova.Core;

public sealed record StoreFile(string FileName, string Url, string? Sha1, long Size);
