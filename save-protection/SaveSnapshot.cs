using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace LocalGameHost;

// Original portable snapshot codec. No platform APIs or game code.
// Only primary save files; callers control when to commit and restore.
internal static class SaveSnapshot
{
    internal const int MaxEncoded = 256 * 1024;
    internal const int MaxRaw = 2 * 1024 * 1024;
    private const int MaxFiles = 64;
    private const string Prefix = "CS1:";
    private static readonly Regex Name = new(@"\A[A-Za-z0-9_-]{1,64}\.celeste\z", RegexOptions.CultureInvariant);
    private static void CheckName(string name)
    {
        if (!Name.IsMatch(name)) throw new InvalidDataException("Unsupported save name");
    }

    internal static string Encode(IReadOnlyDictionary<string, byte[]> files)
    {
        if (files.Count > MaxFiles) throw new InvalidDataException("Too many save files");
        using var raw = new MemoryStream();
        using (var writer = new BinaryWriter(raw, Encoding.UTF8, true))
        {
            writer.Write(files.Count);
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var pair in files.OrderBy(p => p.Key, StringComparer.Ordinal))
            {
                CheckName(pair.Key);
                if (!names.Add(pair.Key) || pair.Value.Length == 0 ||
                    pair.Value.Length > MaxRaw || raw.Length + pair.Value.Length + 100 > MaxRaw)
                    throw new InvalidDataException("Invalid or oversized save snapshot");
                writer.Write(pair.Key);
                writer.Write(pair.Value.Length);
                writer.Write(pair.Value);
            }
        }
        using var packed = new MemoryStream();
        using (var gzip = new GZipStream(packed, CompressionLevel.Optimal, true))
        {
            raw.Position = 0;
            raw.CopyTo(gzip);
        }
        var bytes = packed.ToArray();
        var encoded = Prefix + Convert.ToHexString(SHA256.HashData(bytes)) + ":" + Convert.ToBase64String(bytes);
        if (encoded.Length > MaxEncoded) throw new InvalidDataException("Save snapshot exceeds preferences budget");
        return encoded;
    }

    internal static Dictionary<string, byte[]> Decode(string encoded)
    {
        if (encoded.Length > MaxEncoded || encoded.Length < 70 ||
            !encoded.StartsWith(Prefix, StringComparison.Ordinal) || encoded[68] != ':')
            throw new InvalidDataException("Unknown or oversized snapshot");
        var bytes = Convert.FromBase64String(encoded[69..]);
        if (!String.Equals(Convert.ToHexString(SHA256.HashData(bytes)), encoded.Substring(4, 64), StringComparison.Ordinal))
            throw new InvalidDataException("Snapshot checksum mismatch");
        using var packed = new MemoryStream(bytes);
        using var gzip = new GZipStream(packed, CompressionMode.Decompress);
        using var raw = new MemoryStream();
        var buffer = new byte[8192];
        int count;
        while ((count = gzip.Read(buffer, 0, buffer.Length)) != 0)
        {
            if (raw.Length + count > MaxRaw) throw new InvalidDataException("Inflated snapshot exceeds limit");
            raw.Write(buffer, 0, count);
        }
        raw.Position = 0;
        using var reader = new BinaryReader(raw, Encoding.UTF8, true);
        int entries = reader.ReadInt32();
        if (entries < 0 || entries > MaxFiles) throw new InvalidDataException("Invalid file count");
        var files = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < entries; i++)
        {
            var name = reader.ReadString();
            CheckName(name);
            int length = reader.ReadInt32();
            if (length <= 0 || length > raw.Length - raw.Position)
                throw new InvalidDataException("Invalid file length");
            if (!files.TryAdd(name, reader.ReadBytes(length)))
                throw new InvalidDataException("Duplicate save name");
        }
        if (raw.Position != raw.Length) throw new InvalidDataException("Trailing snapshot data");
        return files;
    }
}
