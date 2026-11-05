using System.Buffers.Binary;
using SarcSharp;
using Yaz0Sharp;
using ZsDicSharp;
using ZstdSharp;

namespace RSTBSharp.Tests;

/// <summary>Game dumps for the corpus tests, found through BOTW_ROMFS and TOTK_ROMFS; the tests are skipped without them.</summary>
internal static class Corpus
{
    internal const string BotwEnv = "BOTW_ROMFS";
    internal const string TotkEnv = "TOTK_ROMFS";
    internal const string OotEnv = "OOT_ROMFS";

    internal static string? BotwRoot => Root(BotwEnv);
    internal static string? TotkRoot => Root(TotkEnv);
    internal static string? OotRoot => Root(OotEnv);

    private static string? Root(string env)
        => Environment.GetEnvironmentVariable(env) is { } root && Directory.Exists(root) ? root : null;

    internal static byte[] BotwTable(string root)
        => Yaz0.DecompressFile(Path.Combine(root, "System", "Resource", "ResourceSizeTable.product.srsizetable"));

    internal static byte[] TotkTable(string root, ZsDic zs)
        => zs.Decompress(File.ReadAllBytes(Directory.GetFiles(Path.Combine(root, "System", "Resource"), "*.rsizetable.zs").Single()), ZsDictionary.None);

    // Visits every BotW file by its name on disk or in its pack, decompressed, with packs opened at any depth.
    internal static void ForEachBotw(string root, Action<string, byte[]> visit)
        => ForEachFile(root, (path, data) => OpenBotw(path, data, 0, visit));

    private static void OpenBotw(string name, byte[] data, int depth, Action<string, byte[]> visit)
    {
        data = Yaz0.DecompressIfNeeded(data);
        visit(name, data);
        if (data.AsSpan().StartsWith("SARC"u8) && depth < 4)
            foreach (SarcEntry entry in SarcFile.FromBinary(data).Entries) OpenBotw(entry.Name, entry.Data, depth + 1, visit);
    }

    // Visits every TotK file and every file in a .pack by its romfs path, with .zs removed except from .ta.zs.
    internal static void ForEachTotk(string root, ZsDic zs, Action<string, byte[]> visit)
        => ForEachFile(root, (path, data) => OpenTotk(path, data, zs, visit));

    internal static byte[] OotTable(string root)
        => new Decompressor().Unwrap(File.ReadAllBytes(Directory.GetFiles(Path.Combine(root, "System", "Resource"), "*.rsizetable.zs").Single())).ToArray();

    // Visits every OoT file and every file in a .pack by its romfs path, decompressed.
    internal static void ForEachOot(string root, Action<string, byte[]> visit)
    {
        OotZstd zstd = new(root);
        ForEachFile(root, (path, data) => OpenOot(path, data, zstd, visit));
    }

    private static void OpenOot(string path, byte[] data, OotZstd zstd, Action<string, byte[]> visit)
    {
        data = zstd.Decompress(data);
        visit(path, data);
        if (OotSizeCalculator.CanonicalName(path).EndsWith(".pack", StringComparison.Ordinal))
            foreach (SarcEntry entry in SarcFile.FromBinary(data).Entries) OpenOot(entry.Name, entry.Data, zstd, visit);
    }

    private static void ForEachFile(string root, Action<string, byte[]> visit)
        => Parallel.ForEach(Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories), path =>
            visit(Path.GetRelativePath(root, path), File.ReadAllBytes(path)));

    private static void OpenTotk(string path, byte[] data, ZsDic zs, Action<string, byte[]> visit)
    {
        if (ZsDic.IsCompressed(data) && !path.EndsWith(".ta.zs", StringComparison.Ordinal)) data = zs.Decompress(data, path);
        visit(path, data);
        if (TotkSizeCalculator.CanonicalName(path).EndsWith(".pack", StringComparison.Ordinal))
            foreach (SarcEntry entry in SarcFile.FromBinary(data).Entries) OpenTotk(entry.Name, entry.Data, zs, visit);
    }
}

/// <summary>
/// Picks OoT dictionaries by the ID in each zstd frame. ZsDic goes by file name and doesn't load the bkprb.zsdic this game adds.
/// </summary>
internal sealed class OotZstd
{
    private readonly Dictionary<uint, byte[]> _dictionaries = [];
    private readonly ThreadLocal<Dictionary<uint, Decompressor>> _decompressors = new(() => []);

    public OotZstd(string root)
    {
        byte[] pack = new Decompressor().Unwrap(File.ReadAllBytes(Path.Combine(root, "Pack", "ZsDic.pack.zs"))).ToArray();
        foreach (SarcEntry entry in SarcFile.FromBinary(pack).Entries)
            _dictionaries[BinaryPrimitives.ReadUInt32LittleEndian(entry.Data.AsSpan(4))] = entry.Data;
    }

    public byte[] Decompress(byte[] data)
    {
        if (!ZsDic.IsCompressed(data)) return data;
        uint id = DictionaryId(data);
        Dictionary<uint, Decompressor> cache = _decompressors.Value!;
        if (!cache.TryGetValue(id, out Decompressor? decompressor))
        {
            decompressor = new Decompressor();
            if (id != 0) decompressor.LoadDictionary(_dictionaries[id]);
            cache[id] = decompressor;
        }

        return decompressor.Unwrap(data).ToArray();
    }

    private static uint DictionaryId(byte[] data)
    {
        byte descriptor = data[4];
        int pos = (descriptor & 0x20) != 0 ? 5 : 6;
        return (descriptor & 3) switch
        {
            1 => data[pos],
            2 => BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(pos)),
            3 => BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(pos)),
            _ => 0,
        };
    }
}

/// <summary>A fact that needs an OoT dump.</summary>
public sealed class OotCorpusFactAttribute : FactAttribute
{
    public OotCorpusFactAttribute()
    {
        if (Corpus.OotRoot is null) Skip = $"Set {Corpus.OotEnv} to an OoT romfs root to run the corpus tests.";
    }
}

/// <summary>A fact that needs a BotW dump.</summary>
public sealed class BotwCorpusFactAttribute : FactAttribute
{
    public BotwCorpusFactAttribute()
    {
        if (Corpus.BotwRoot is null) Skip = $"Set {Corpus.BotwEnv} to a BotW romfs root to run the corpus tests.";
    }
}

/// <summary>A fact that needs a TotK dump.</summary>
public sealed class TotkCorpusFactAttribute : FactAttribute
{
    public TotkCorpusFactAttribute()
    {
        if (Corpus.TotkRoot is null) Skip = $"Set {Corpus.TotkEnv} to a TotK romfs root to run the corpus tests.";
    }
}
