using static System.Buffers.Binary.BinaryPrimitives;

namespace RSTBSharp;

/// <summary>TotK RESTBL values after ArchLeaders' TotK-RstbCalculator, which reproduces the 1.2.1 table for most types and overestimates the rest.</summary>
public static class TotkSizeCalculator
{
    // 0 means the file gets no entry. The path is as on disk, and data has its .zs compression removed, except for
    // .ta.zs and .mc files which are taken as stored.
    public static uint Calculate(string path, ReadOnlySpan<byte> data)
    {
        string canonical = CanonicalName(path);
        string ext = path.EndsWith(".mc", StringComparison.Ordinal) ? ".mc"
            : canonical.EndsWith(".casset.byml", StringComparison.Ordinal) ? ".casset.byml"
            : Path.GetExtension(canonical);
        if (canonical is "Pack/ZsDic.pack" || ext is ".rsizetable" or ".bwav" or ".webm") return 0;

        uint size = ((uint)data.Length + 31) & ~31u;
        return canonical switch
        {
            // Vanilla files that need more than their extension's rule gives.
            "Event/EventFlow/Dm_ED_0004.bfevfl" => size + 0x1e0,
            "Effect/Animal_Crow_A.Nin_NX_NVN.esetb.byml" => size + 0x1080,
            "UI/LayoutArchive/Common.Product.110.Nin_NX_NVN.blarc" => size + 0x2b60,
            "Effect/static.Nin_NX_NVN.esetb.byml"
                or "Effect/static.Product.110.Nin_NX_NVN.esetb.byml"
                or "Lib/agl/agl_resource.Nin_NX_NVN.release.sarc"
                or "Lib/gsys/gsys_resource.Nin_NX_NVN.release.sarc"
                or "Lib/Terrain/tera_resource.Nin_NX_NVN.release.sarc"
                or "Shader/ApplicationPackage.Nin_NX_NVN.release.sarc" => size + 0x1000,
            _ => ext switch
            {
                ".ainb" => size + 392 + Exb(data, ReadInt32LittleEndian(data[0x44..])),
                ".asb" => size + 552 + 40 * ReadUInt32LittleEndian(data[0x10..]) + Exb(data, ReadInt32LittleEndian(data[0x60..])),
                ".bstar" => size + 0x120 + ReadUInt32LittleEndian(data[0x8..]) * 8,
                ".mc" => (size + MeshCodec(data) + 1500) * 4,
                ".bgyml" => (size + 1000) * 8,
                ".bkres" => (size + 2000) * 4,
                ".baev" or ".bfevfl" or ".bphnm" => size + 0x120,
                ".bars" => size + 0x240,
                ".bphsh" => size + 0x170,
                ".casset.byml" => size + 0x1c0,
                ".genvb" or ".pack" or ".sarc" => size + 0x180,
                ".baatarc" or ".bagst" or ".bcul" or ".beco" or ".belnk" or ".bfarc" or ".bfsha" or ".bhtmp" or ".blal"
                    or ".blarc" or ".blwp" or ".bnsh" or ".bntx" or ".bphcl" or ".bphhb" or ".bslnk" or ".byml" or ".cai"
                    or ".chunk" or ".crbin" or ".cutinfo" or ".dpi" or ".jpg" or ".png" or ".quad" or ".tscb" or ".txtg"
                    or ".txt" or ".vsts" or ".wbr" or ".zs" => size + 0x100,
                _ => (size + 1500) * 4,
            },
        };
    }

    // The name a romfs path goes by in the RESTBL: .zs and .mc dropped, but not .ta.zs.
    public static string CanonicalName(string path)
    {
        path = path.Replace('\\', '/');
        return (path.EndsWith(".zs", StringComparison.Ordinal) && !path.EndsWith(".ta.zs", StringComparison.Ordinal))
            || path.EndsWith(".mc", StringComparison.Ordinal)
            ? path[..^3]
            : path;
    }

    // The EXB section's signature table, which AINB and ASB both carry.
    internal static uint Exb(ReadOnlySpan<byte> data, int offset)
    {
        if (offset == 0) return 0;
        uint signatures = ReadUInt32LittleEndian(data[(offset + ReadInt32LittleEndian(data[(offset + 0x20)..]))..]);
        return 16 + (signatures + 1) / 2 * 8;
    }

    private static uint MeshCodec(ReadOnlySpan<byte> data)
    {
        int flags = ReadInt32LittleEndian(data[8..]);
        int decompressed = (flags >> 5) << (flags & 0xf);
        return (uint)((decompressed * 1.15 + 0x1000) * 4);
    }
}
