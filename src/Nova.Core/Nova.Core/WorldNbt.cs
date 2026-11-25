using System;
using System.Buffers.Binary;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace Nova.Core;

// Reads and replaces only Data.LevelName. All unrelated NBT bytes remain untouched.
internal static class WorldNbt
{
    private const int MaxBytes = 64 * 1024 * 1024;
    private const int MaxDepth = 128;
    internal sealed record Parsed(byte[] Data, string Name, int NameStart, int NameEnd);

    internal static Parsed Read(string path)
    {
        using var file = File.OpenRead(path);
        using var zip = new GZipStream(file, CompressionMode.Decompress);
        using var data = new MemoryStream();
        var buffer = new byte[81920];
        int n;
        while ((n = zip.Read(buffer, 0, buffer.Length)) > 0)
        {
            if (data.Length + n > MaxBytes) throw new InvalidDataException("World metadata is too large.");
            data.Write(buffer, 0, n);
        }
        var parser = new Parser(data.ToArray());
        return parser.Parse();
    }

    internal static byte[] ReplaceName(Parsed parsed, string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 120) throw new ArgumentException("World name must contain 1 to 120 characters.");
        foreach (char c in name) if (char.IsControl(c)) throw new ArgumentException("World name cannot contain control characters.");
        for (int i = 0; i < name.Length; i++)
        {
            if (char.IsHighSurrogate(name[i])) { if (i + 1 >= name.Length || !char.IsLowSurrogate(name[++i])) throw new ArgumentException("Invalid Unicode in world name."); }
            else if (char.IsLowSurrogate(name[i])) throw new ArgumentException("Invalid Unicode in world name.");
        }
        byte[] encoded = EncodeModifiedUtf8(name);
        if (encoded.Length > ushort.MaxValue) throw new ArgumentException("World name is too long.");
        byte[] result = new byte[parsed.Data.Length - (parsed.NameEnd - parsed.NameStart) + encoded.Length + 2];
        Buffer.BlockCopy(parsed.Data, 0, result, 0, parsed.NameStart);
        BinaryPrimitives.WriteUInt16BigEndian(result.AsSpan(parsed.NameStart, 2), (ushort)encoded.Length);
        Buffer.BlockCopy(encoded, 0, result, parsed.NameStart + 2, encoded.Length);
        Buffer.BlockCopy(parsed.Data, parsed.NameEnd, result, parsed.NameStart + 2 + encoded.Length, parsed.Data.Length - parsed.NameEnd);
        return result;
    }

    internal static void WriteGzip(string path, byte[] data)
    {
        using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        using var zip = new GZipStream(file, CompressionLevel.Optimal);
        zip.Write(data, 0, data.Length);
    }

    private static byte[] EncodeModifiedUtf8(string s)
    {
        using var b = new MemoryStream();
        foreach (char c in s)
        {
            if (c >= 1 && c <= 0x7f) b.WriteByte((byte)c);
            else if (c <= 0x7ff) { b.WriteByte((byte)(0xc0 | (c >> 6))); b.WriteByte((byte)(0x80 | (c & 0x3f))); }
            else { b.WriteByte((byte)(0xe0 | (c >> 12))); b.WriteByte((byte)(0x80 | ((c >> 6) & 0x3f))); b.WriteByte((byte)(0x80 | (c & 0x3f))); }
        }
        return b.ToArray();
    }

    private sealed class Parser
    {
        private readonly byte[] data;
        private int pos;
        private string? worldName;
        private int start, end;
        internal Parser(byte[] data) { this.data = data; }
        private void Need(int count) { if (count < 0 || count > data.Length - pos) throw new InvalidDataException("Truncated or invalid NBT."); }
        private byte Byte() { Need(1); return data[pos++]; }
        private int Int() { Need(4); int v = BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(pos, 4)); pos += 4; return v; }
        private string Text()
        {
            Need(2); int len = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(pos, 2)); pos += 2; Need(len);
            int limit = pos + len; var b = new StringBuilder(len);
            while (pos < limit)
            {
                int a = data[pos++];
                if (a >= 1 && a <= 0x7f) b.Append((char)a);
                else if ((a & 0xe0) == 0xc0)
                {
                    if (pos >= limit || (data[pos] & 0xc0) != 0x80) throw new InvalidDataException("Invalid NBT modified UTF-8.");
                    int c = ((a & 0x1f) << 6) | (data[pos++] & 0x3f);
                    if (c != 0 && c < 0x80) throw new InvalidDataException("Invalid NBT modified UTF-8.");
                    b.Append((char)c);
                }
                else if ((a & 0xf0) == 0xe0)
                {
                    if (pos + 1 >= limit || (data[pos] & 0xc0) != 0x80 || (data[pos + 1] & 0xc0) != 0x80) throw new InvalidDataException("Invalid NBT modified UTF-8.");
                    int c = ((a & 15) << 12) | ((data[pos] & 63) << 6) | (data[pos + 1] & 63); pos += 2;
                    if (c < 0x800) throw new InvalidDataException("Invalid NBT modified UTF-8.");
                    b.Append((char)c);
                }
                else throw new InvalidDataException("Invalid NBT modified UTF-8.");
            }
            return b.ToString();
        }
        internal Parsed Parse()
        {
            if (Byte() != 10) throw new InvalidDataException("NBT root is not a compound.");
            Text(); Compound(0, 0);
            if (pos != data.Length || worldName == null) throw new InvalidDataException("NBT lacks a unique Data.LevelName or has trailing data.");
            return new Parsed(data, worldName, start, end);
        }
        private void Compound(int depth, int mode)
        {
            if (depth > MaxDepth) throw new InvalidDataException("NBT nesting is too deep.");
            while (true)
            {
                int type = Byte(); if (type == 0) return;
                string name = Text();
                if (mode == 0 && type == 10 && name == "Data") { Compound(depth + 1, 1); continue; }
                if (mode == 1 && type == 8 && name == "LevelName")
                {
                    if (worldName != null) throw new InvalidDataException("Duplicate world name.");
                    start = pos; worldName = Text(); end = pos; continue;
                }
                Skip(type, depth + 1);
            }
        }
        private void Skip(int type, int depth)
        {
            if (depth > MaxDepth) throw new InvalidDataException("NBT nesting is too deep.");
            switch (type)
            {
                case 1: Need(1); pos++; break;
                case 2: Need(2); pos += 2; break;
                case 3: case 5: Need(4); pos += 4; break;
                case 4: case 6: Need(8); pos += 8; break;
                case 7: { int n = Int(); Need(n); pos += n; break; }
                case 8: Text(); break;
                case 9:
                    { int itemType = Byte(); int n = Int(); if (n < 0 || n > data.Length - pos || (itemType == 0 && n != 0) || itemType > 12) throw new InvalidDataException("Invalid NBT list."); for (int i = 0; i < n; i++) Skip(itemType, depth + 1); break; }
                case 10: Compound(depth + 1, 2); break;
                case 11: case 12:
                    { int n = Int(); int size = type == 11 ? 4 : 8; if (n < 0 || n > (data.Length - pos) / size) throw new InvalidDataException("Invalid NBT array."); pos += n * size; break; }
                default: throw new InvalidDataException("Unknown NBT tag.");
            }
        }
    }
}
