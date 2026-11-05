namespace RSTBSharp.Tests;

/// <summary>The BotW and TotK size rules on hand-made inputs.</summary>
public class CalculatorTests
{
    [Fact]
    public void BotwSwitchAlignsTo32()
    {
        Assert.Equal(32u + 0x1c0, BotwSizeCalculator.Calculate("System/Version.txt", new byte[5], wiiU: false));
        Assert.Equal(64u + 0x200, BotwSizeCalculator.Calculate("Actor/Pack/A.sbactorpack", new byte[33], wiiU: false));
    }

    [Fact]
    public void BotwWiiUAlignsLooseFilesTo64AndCompressedTo32()
    {
        Assert.Equal(64u + 0x140, BotwSizeCalculator.Calculate("System/Version.txt", new byte[5], wiiU: true));
        Assert.Equal(64u + 0x180, BotwSizeCalculator.Calculate("Actor/Pack/A.sbactorpack", new byte[33], wiiU: true));
        Assert.Equal(64u + 0x180, BotwSizeCalculator.Calculate("Actor/Pack/A.bactorpack", 33, wiiU: true, compressed: true));
    }

    [Fact]
    public void BotwCanonicalNamesDropTheCompressionPrefix()
    {
        Assert.Equal("Actor/Pack/A.bactorpack", BotwSizeCalculator.CanonicalName("Actor/Pack/A.sbactorpack"));
        Assert.Equal("Layout/A.sarc", BotwSizeCalculator.CanonicalName("Layout/A.sarc"));
        Assert.Equal("Layout/A.sarc", BotwSizeCalculator.CanonicalName("Layout/A.ssarc"));
        Assert.Equal("Game/A.stats", BotwSizeCalculator.CanonicalName("Game/A.stats"));
        Assert.Equal("Dir.sx/NoExtension", BotwSizeCalculator.CanonicalName("Dir.sx/NoExtension"));
    }

    [Fact]
    public void BotwReadsTheSizeFromAYaz0Header()
    {
        byte[] yaz0 = new byte[16];
        "Yaz0"u8.CopyTo(yaz0);
        yaz0[7] = 100;

        Assert.Equal(128u + 0x120, BotwSizeCalculator.Calculate("Map/A.mubin", yaz0, wiiU: true));
    }

    [Fact]
    public void BotwContentDependentTypesNeedAnEstimate()
    {
        Assert.Equal(0u, BotwSizeCalculator.Calculate("Model/A.sbfres", new byte[1000], wiiU: false, estimate: false));
        Assert.NotEqual(0u, BotwSizeCalculator.Calculate("Model/A.sbfres", new byte[1000], wiiU: false));
        Assert.Equal(0u, BotwSizeCalculator.Calculate("Actor/ModelList/A.bmodellist", new byte[1000], wiiU: false));
    }

    [Fact]
    public void TotkUsesThePathOnDisk()
    {
        Assert.Equal("Pack/Actor/A.pack", TotkSizeCalculator.CanonicalName(@"Pack\Actor\A.pack.zs"));
        Assert.Equal("Model/A.bfres", TotkSizeCalculator.CanonicalName("Model/A.bfres.mc"));
        Assert.Equal("Startup/A.ta.zs", TotkSizeCalculator.CanonicalName("Startup/A.ta.zs"));

        Assert.Equal(64u + 0x180, TotkSizeCalculator.Calculate("Pack/Actor/A.pack.zs", new byte[33]));
        Assert.Equal(64u + 0x1c0, TotkSizeCalculator.Calculate("Sound/A.casset.byml.zs", new byte[33]));
        Assert.Equal((64u + 1500) * 4, TotkSizeCalculator.Calculate("Foo/A.unknown", new byte[33]));
        Assert.Equal(0u, TotkSizeCalculator.Calculate("Pack/ZsDic.pack.zs", new byte[33]));
    }

    [Fact]
    public void OotConstantsFollowTheName()
    {
        Assert.Equal("Pack/Actor/A.pack", OotSizeCalculator.CanonicalName(@"Pack\Actor\A.pack.zs"));
        Assert.Equal(64u + 0x108, OotSizeCalculator.Calculate("Foo/A.unknown", new byte[33]));
        Assert.Equal(64u + 0x138, OotSizeCalculator.Calculate("Pack/Actor/A.pack.zs", new byte[33]));
        Assert.Equal(64u + 0x208, OotSizeCalculator.Calculate("Sound/A.casset.byml.zs", new byte[33]));
        Assert.Equal(64u + 0x108, OotSizeCalculator.Calculate("Effect/A.Nin_Ounce_NVN2.vsetb.byml.zs", new byte[33]));
        Assert.Equal(64u + 0x2048, OotSizeCalculator.Calculate("UI/Tex/A.bntx.zs", new byte[33]));
        Assert.Equal(64u + 0x108, OotSizeCalculator.Calculate("Tex/A.bntx.zs", new byte[33]));
    }

    [Theory]
    [InlineData("Pack/ZsDic.pack.zs")]
    [InlineData("Sound/Resource/Stream/A.bwav")]
    [InlineData("Movie/A.webm")]
    [InlineData("Model/A.bfres.mc")]
    public void OotSkipsFilesWithoutEntries(string path)
        => Assert.Equal(0u, OotSizeCalculator.Calculate(path, new byte[33]));

    [Fact]
    public void OotCountsRecordsFromTheHeader()
    {
        byte[] bcul = new byte[36];
        bcul[0x10] = 2;
        Assert.Equal(64u + 312 + 2 * 32, OotSizeCalculator.Calculate("PreComputedCulling/A.bcul", bcul));

        byte[] bbox = new byte[52];
        bbox[4] = 1;
        Assert.Equal(64u + 312 + 40, OotSizeCalculator.Calculate("PreComputedCulling/Boxes.bbox", bbox));

        byte[] bkprb = new byte[900_000];
        bkprb[0x10] = 16;
        Assert.Equal(900_000u + (16 * 900_000 - 1373920) / 9, OotSizeCalculator.Calculate("Bake/A.bkprb", bkprb));
    }

    [Fact]
    public void OotMarginOnlyCoversEstimates()
    {
        byte[] empty = Byml();
        Assert.Equal(32u + 0x13f8, OotSizeCalculator.Calculate("A.game__drop__DropTable.bgyml", empty, margin: false));
        Assert.Equal(32u + 0x13f8 + 16 * 1024, OotSizeCalculator.Calculate("A.game__drop__DropTable.bgyml", empty));
        Assert.Equal(64u + 0x138, OotSizeCalculator.Calculate("Pack/A.pack", new byte[33], margin: true));
        Assert.Equal(64u + 0x248, OotSizeCalculator.Calculate("Sound/Resource/A.bars", new byte[33], margin: false));
        Assert.Equal(64u + 0x268, OotSizeCalculator.Calculate("Sound/Resource/A.bars", new byte[33]));
        Assert.Equal(64u + 0x268, OotSizeCalculator.Calculate("Sound/Resource/Ocarina.bars", new byte[33], margin: false));
    }

    [Fact]
    public void OotChargesBgymlPerElement()
    {
        byte[] known = Byml("SubTableList", 2);
        Assert.Equal(Aligned(known) + 0x13f8 + 2 * 8, OotSizeCalculator.Calculate("A.game__drop__DropTable.bgyml", known, margin: false));

        // A field the type has never had is charged the type's dearest known element, at least 64 bytes.
        byte[] unseen = Byml("NewList", 2);
        Assert.Equal(Aligned(unseen) + 0x13f8 + 2 * 64, OotSizeCalculator.Calculate("A.game__drop__DropTable.bgyml", unseen, margin: false));
    }

    [Fact]
    public void OotUnknownBgymlTypeIsBounded()
    {
        byte[] data = Byml();
        Assert.Equal(32u + 8856 + 9 * 16, OotSizeCalculator.Calculate("A.game__mod__NewType.bgyml", data, margin: false));
    }

    private static uint Aligned(byte[] data) => (uint)(data.Length + 31) & ~31u;

    // A little endian BYML holding nothing, or one array of integers under a key.
    private static byte[] Byml(string? key = null, int elements = 0)
    {
        byte[] head = [(byte)'Y', (byte)'B', 7, 0];
        if (key is null) return [.. head, .. new byte[12]];

        const int root = 0x10, array = root + 12;
        int keys = array + 4 + ((elements + 3) & ~3) + 4 * elements;
        byte[] data = new byte[keys + 12 + key.Length + 1];
        head.CopyTo(data, 0);
        BitConverter.TryWriteBytes(data.AsSpan(4), keys);
        BitConverter.TryWriteBytes(data.AsSpan(12), root);

        data[root] = 0xc1;
        data[root + 1] = 1;
        data[root + 7] = 0xc0;
        BitConverter.TryWriteBytes(data.AsSpan(root + 8), array);

        data[array] = 0xc0;
        data[array + 1] = (byte)elements;
        data.AsSpan(array + 4, elements).Fill(0xd1);

        data[keys] = 0xc2;
        data[keys + 1] = 1;
        BitConverter.TryWriteBytes(data.AsSpan(keys + 4), 12);
        BitConverter.TryWriteBytes(data.AsSpan(keys + 8), 12 + key.Length + 1);
        System.Text.Encoding.UTF8.GetBytes(key).CopyTo(data, keys + 12);
        return data;
    }
}
