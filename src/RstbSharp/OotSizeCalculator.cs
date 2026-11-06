using System.Text;
using System.Text.Json;
using static System.Buffers.Binary.BinaryPrimitives;

namespace RSTBSharp;

/// <summary>
/// Ocarina of Time RESTBL values, derived by fitting the 1.1.1 table to its romfs. Most types are exact; bfres, asb, bphsc
/// and bkprcb are upper bounds and bgyml uses a cost model fitted per type. The game's code only informed the bfres bound.
/// </summary>
public static class OotSizeCalculator
{
    // Cross-validation on files the fit hadn't seen needed up to 12.6 KB for bgyml and 25 KB for bfres.
    private const uint EstimateMargin = 16 * 1024;
    private const uint BfresMargin = 64 * 1024;

    private const string StaticVfx = "Effect/static.Nin_Ounce_NVN2.vsetb.byml";
    private const string TabledVoice = "Sound/Resource/Stream/NV_EUes_Custom_Rauru_Dm_SageRoom_0005_0008_Sp00.bwav";

    private static readonly HashSet<string> LargeBars =
    [
        "BaseEnvSound", "BaseNoise", "BGM_Static", "EnvBird", "EnvGrassInsect", "ExpressionSound", "FieldEnvSound", "Foliage",
        "Land_Kakariko_BuildingParts_A_43", "Obj_FireTemple_FireWallTrap_A_01", "Ocarina", "ReactionField", "ReactionHit",
    ];

    private static readonly Lazy<GymlModel> Model = new(GymlModel.Load);

    // 0 means the file gets no entry. The path is as on disk or in its pack, and data has its .zs compression removed.
    // The margin covers the types that are only estimated, and is what files the fit never saw should use. Leave it off to
    // compare against vanilla, whose unchanged files should keep their own values anyway.
    public static uint Calculate(string path, ReadOnlySpan<byte> data, bool margin = true)
    {
        string name = CanonicalName(path);
        string ext = Path.GetExtension(name).TrimStart('.');
        if (name is "Pack/ZsDic.pack" || ext is "webm" or "mc" or "zsdic" or "rsizetable" || (ext is "bwav" && name != TabledVoice)) return 0;

        long extra = margin ? ext switch
        {
            "bfres" => BfresMargin,
            "bgyml" or "asb" or "bphsc" or "bkprcb" => EstimateMargin,
            _ => 0,
        } : 0;

        return (uint)Math.Clamp(Align(data.Length, 32) + Offset(name, ext, data, margin) + extra, 0, uint.MaxValue);
    }

    // The name a romfs path goes by in the RESTBL: .zs dropped.
    public static string CanonicalName(string path)
    {
        path = path.Replace('\\', '/');
        return path.EndsWith(".zs", StringComparison.Ordinal) ? path[..^3] : path;
    }

    private static long Offset(string name, string ext, ReadOnlySpan<byte> data, bool margin) => ext switch
    {
        "bcul" => 312 + 32L * ReadUInt32LittleEndian(data[0x10..]),
        "bbox" => 312 + 40L * ReadUInt32LittleEndian(data[0x4..]),
        "ainb" => 376L + TotkSizeCalculator.Exb(data, ReadInt32LittleEndian(data[0x44..])),
        "bkprb" => Probe(data),
        "bkprcb" => ProbeBound(data.Length),
        "bkres" => BakeResource(data),
        "asb" => Asb(data),
        "bphsc" => StaticCompound(data),
        "bfres" => Bfres(data),
        "bgyml" => Model.Value.Offset(name, data),
        "byml" => name.EndsWith(".casset.byml", StringComparison.Ordinal) ? 0x208
            : name == StaticVfx ? 0x1028
            : name.StartsWith("RSDB/ActorInfo.", StringComparison.Ordinal) ? 0xbf8
            : 0x108,
        "sarc" => name.EndsWith(".release.sarc", StringComparison.Ordinal) ? 0x1058 : 0x138,
        "bntx" => name.StartsWith("UI/", StringComparison.Ordinal) ? 0x2048 : 0x108,
        // Nothing found says which bars need the larger value, so a file that isn't vanilla gets it to be safe.
        "bars" => margin || LargeBars.Contains(Path.GetFileNameWithoutExtension(name)) ? 0x268 : 0x248,
        "bwav" => 0x315e8,
        "bfsha" => 0x100,
        "baev" or "bfevfl" => 0x110,
        "bphhf" => 0x128,
        "bphnm" or "bklight" => 0x130,
        "pack" or "genvb" => 0x138,
        "bphsh" => 0x170,
        "bpheb" => 0x1a0,
        "blarc" => 0x1030,
        _ => 0x108,
    };

    private static long Align(long size, long alignment) => (size + alignment - 1) / alignment * alignment;

    // Probe data costs 16 bytes for each 9 it stores, less a constant for the grid size.
    private static long Probe(ReadOnlySpan<byte> data)
    {
        long constant = ReadUInt32LittleEndian(data[0x10..]) switch
        {
            8 => -169696,
            16 => -1373920,
            32 => -11007712,
            _ => 0,
        };
        return constant == 0 ? ProbeBound(data.Length) : Math.Max(16L * data.Length + constant, 0) / 9;
    }

    // The same shape as Probe, with a constant that was only pinned down as being negative.
    private static long ProbeBound(long size) => Align(16 * size / 9 + 1, 8);

    private static long BakeResource(ReadOnlySpan<byte> data)
    {
        long models = 0, materials = 0;
        if (BymlReader.Read(SarcEntry(data, "bkdat.byaml"u8)) is Dictionary<string, object?> root
            && root.GetValueOrDefault("DataElements") is List<object?> elements)
        {
            foreach (object? element in elements)
            {
                if (element is not Dictionary<string, object?> e || e.GetValueOrDefault("ModelElements") is not List<object?> list) continue;
                models += list.Count;
                foreach (object? model in list)
                    if (model is Dictionary<string, object?> m && m.GetValueOrDefault("MaterialElements") is List<object?> mats) materials += mats.Count;
            }
        }

        return 8760 + Align(84 * models + 56 * materials, 16);
    }

    private static ReadOnlySpan<byte> SarcEntry(ReadOnlySpan<byte> sarc, ReadOnlySpan<byte> name)
    {
        if (!sarc.StartsWith("SARC"u8)) throw new InvalidDataException("Not a SARC: expected SARC magic.");
        int sfat = ReadUInt16LittleEndian(sarc[4..]);
        int dataStart = ReadInt32LittleEndian(sarc[0xc..]);
        int count = ReadUInt16LittleEndian(sarc[(sfat + 6)..]);
        int names = sfat + 12 + count * 16 + 8;
        for (int i = 0; i < count; i++)
        {
            int node = sfat + 12 + i * 16;
            ReadOnlySpan<byte> entry = sarc[(names + (ReadInt32LittleEndian(sarc[(node + 4)..]) & 0xffffff) * 4)..];
            if (entry[..entry.IndexOf((byte)0)].SequenceEqual(name))
                return sarc[(dataStart + ReadInt32LittleEndian(sarc[(node + 8)..]))..(dataStart + ReadInt32LittleEndian(sarc[(node + 12)..]))];
        }

        throw new InvalidDataException($"SARC has no {Encoding.UTF8.GetString(name)}.");
    }

    // Exactly 544 + 40 * c14 for most files. The rest need something unidentified, which the second form bounds.
    private static long Asb(ReadOnlySpan<byte> data)
    {
        long c0C = ReadUInt32LittleEndian(data[0x0c..]), c14 = ReadUInt32LittleEndian(data[0x14..]);
        long c24 = ReadUInt32LittleEndian(data[0x24..]), c4C = ReadUInt32LittleEndian(data[0x4c..]);
        return Math.Max(544 + 40 * c14, 858 + 39 * c14 + 7 * c0C + 132 * c24 + 29 * c4C);
    }

    private static long StaticCompound(ReadOnlySpan<byte> data)
    {
        long sections = 0;
        for (int pos = 0x10; pos < 0x200 && pos + 4 <= data.Length; pos += 4)
            if (ReadUInt32LittleEndian(data[pos..]) != 0) sections++;
        return 12 + 1942 * sections;
    }

    // Sized from the counts gsys::ModelResource reads at load. Part of a model's real cost depends on its shader archive, so
    // this is an upper bound: the counts and minimums come from the game's code, and the amounts above them from fitting.
    private static long Bfres(ReadOnlySpan<byte> data)
    {
        long result = 0x19b0;

        int models = ReadUInt16LittleEndian(data[0xdc..]);
        int modelArray = Pointer(data, 0x28);
        for (int i = 0; i < models; i++)
            result += BfresModel(data, modelArray + i * 0x78);

        int animations = ReadUInt16LittleEndian(data[0xe2..]);
        int animationArray = Pointer(data, 0x58);
        for (int i = 0; i < animations; i++)
            result += 32 + Align(4L * ReadUInt16LittleEndian(data[(animationArray + i * 0x50 + 0x4c)..]), 8);

        result += 328L * ReadUInt16LittleEndian(data[0xe4..]) + 48L * ReadUInt16LittleEndian(data[0xe6..]);
        result += 32L * (ReadUInt16LittleEndian(data[0xe8..]) + ReadUInt16LittleEndian(data[0xea..]));
        result += 256L * ReadUInt16LittleEndian(data[0xec..]);
        return Align(result, 8);
    }

    private static long BfresModel(ReadOnlySpan<byte> data, int model)
    {
        long result = 4538 + Align(4L * ReadUInt16LittleEndian(data[(Pointer(data, model + 0x18) + 0x38)..]), 8);

        int materials = ReadUInt16LittleEndian(data[(model + 0x6c)..]);
        int materialArray = Pointer(data, model + 0x38);
        for (int i = 0; i < materials; i++)
            result += 4689 + 60L * data[materialArray + i * 0xb0 + 0xa2];

        int shapes = ReadUInt16LittleEndian(data[(model + 0x6a)..]);
        int shapeArray = Pointer(data, model + 0x28);
        for (int i = 0; i < shapes; i++)
        {
            int shape = shapeArray + i * 0x60;
            result += 5882;
            if (!IsSharedShape(data, shapeArray, i)) result += 0x750 + 0x270L * ShapeLods(data, shape);
        }

        return result;
    }

    // The game gives a shape no setup of its own when an earlier one has its material and pointer at 0x10 but differs at 0x54.
    private static bool IsSharedShape(ReadOnlySpan<byte> data, int shapeArray, int index)
    {
        int shape = shapeArray + index * 0x60;
        if (index == 0 || data[shape + 0x5a] != 0) return false;

        for (int i = 0; i < index; i++)
        {
            int other = shapeArray + i * 0x60;
            if (ReadUInt16LittleEndian(data[(shape + 0x52)..]) != ReadUInt16LittleEndian(data[(other + 0x52)..])) continue;
            return ReadUInt64LittleEndian(data[(shape + 0x10)..]) == ReadUInt64LittleEndian(data[(other + 0x10)..])
                && ReadUInt16LittleEndian(data[(shape + 0x54)..]) != ReadUInt16LittleEndian(data[(other + 0x54)..]);
        }

        return false;
    }

    private static int ShapeLods(ReadOnlySpan<byte> data, int shape)
    {
        int index = data[shape + 0x5b];
        return (data[shape + 4] & 0x10) == 0 ? index : data[Pointer(data, shape + 0x18) + index * 0x38 + 0x45];
    }

    private static int Pointer(ReadOnlySpan<byte> data, int position) => checked((int)ReadUInt64LittleEndian(data[position..]));

    /// <summary>What each bgyml type costs: a base, plus an amount per array element or map entry at each path in the file.</summary>
    private sealed class GymlModel
    {
        private const int MinimumUnseenCost = 64;

        private sealed record Type(long Base, Dictionary<string, long> Costs, HashSet<string> Known, Dictionary<string, long> Constants, HashSet<string> Maps)
        {
            public long UnseenCost { get; } = Math.Max(Costs.Values.DefaultIfEmpty(0).Max(), MinimumUnseenCost);
        }

        private readonly Dictionary<string, Type> _types = [];
        private long _unknownBase, _unknownPerByte;

        public static GymlModel Load()
        {
            using Stream stream = typeof(GymlModel).Assembly.GetManifestResourceStream("RSTBSharp.OotBgymlModel.json")
                ?? throw new InvalidOperationException("The bgyml model is missing from the assembly.");
            using JsonDocument json = JsonDocument.Parse(stream);

            GymlModel model = new();
            foreach (JsonProperty type in json.RootElement.EnumerateObject())
            {
                JsonElement v = type.Value;
                if (type.Name == "__unknown__")
                {
                    model._unknownBase = v.GetProperty("base").GetInt64();
                    model._unknownPerByte = v.GetProperty("perbyte").GetInt64();
                    continue;
                }

                model._types[type.Name] = new Type(v.GetProperty("base").GetInt64(), Numbers(v.GetProperty("costs")),
                    Strings(v.GetProperty("known")), Numbers(v.GetProperty("constants")), Strings(v.GetProperty("maps")));
            }

            return model;
        }

        private static Dictionary<string, long> Numbers(JsonElement e) => e.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetInt64());

        private static HashSet<string> Strings(JsonElement e) => e.EnumerateArray().Select(s => s.GetString()!).ToHashSet();

        public long Offset(string name, ReadOnlySpan<byte> data)
        {
            string[] parts = Path.GetFileName(name).Split('.');
            if (parts.Length < 2 || !_types.TryGetValue(parts[^2], out Type? type))
                return Align(_unknownBase + _unknownPerByte * data.Length, 8);

            Dictionary<string, long> features = [];
            Walk(BymlReader.Read(data), "", features, type.Maps);

            long result = type.Base;
            foreach ((string feature, long count) in features)
            {
                if (type.Costs.TryGetValue(feature, out long cost)) result += cost * count;
                else if (feature.EndsWith("{n}", StringComparison.Ordinal) || type.Known.Contains(feature)) continue;
                // Beyond anything the fit saw, so assume the type's dearest element.
                else result += type.UnseenCost * Math.Max(0, count - type.Constants.GetValueOrDefault(feature));
            }

            return Align(result, 8);
        }

        private static void Walk(object? node, string path, Dictionary<string, long> features, HashSet<string> maps)
        {
            switch (node)
            {
                case BymlHashMap hash:
                    Elements(hash.Values, path + "#", features, maps);
                    break;
                case List<object?> array:
                    Elements(array, path + "[]", features, maps);
                    break;
                case Dictionary<string, object?> dictionary:
                    if (path.Length > 0 && !path.EndsWith(']') && !path[(path.LastIndexOf('.') + 1)..].Contains('@'))
                        Add(features, path + "{n}", dictionary.Count);
                    foreach ((string key, object? value) in dictionary)
                    {
                        if (value is not (BymlHashMap or List<object?> or Dictionary<string, object?>)) continue;
                        string tag = Tag(value);
                        if (tag.Length > 0) Add(features, path + ".*" + tag, 1);
                        Walk(value, path + "." + (maps.Contains(path) ? "*" : key) + tag, features, maps);
                    }

                    break;
            }
        }

        private static void Elements(List<object?> values, string path, Dictionary<string, long> features, HashSet<string> maps)
        {
            Add(features, path, values.Count);
            foreach (object? value in values)
            {
                string tag = Tag(value);
                if (tag.Length > 0) Add(features, path + tag, 1);
                Walk(value, path + tag, features, maps);
            }
        }

        private static string Tag(object? value)
            => value is Dictionary<string, object?> d && d.GetValueOrDefault("$type") is string type ? "@" + type : "";

        private static void Add(Dictionary<string, long> features, string key, long count)
            => features[key] = features.GetValueOrDefault(key) + count;
    }
}
