using System.Collections.Concurrent;
using ZsDicSharp;

namespace RSTBSharp.Tests;

/// <summary>Checks the tables and calculators against a game dump's vanilla resource size table.</summary>
public class CorpusTests
{
    [BotwCorpusFact]
    public void BotwTableRoundTrips()
    {
        byte[] data = Corpus.BotwTable(Corpus.BotwRoot!);
        ResourceSizeTable table = ResourceSizeTable.FromBinary(data);

        Assert.Equal(RstbFormat.Rstb, table.Format);
        Assert.Equal(data, table.ToBinary());
        Assert.Equal(data, ResourceSizeTable.FromYaml(table.ToYaml()).ToBinary());
    }

    [TotkCorpusFact]
    public void TotkTableRoundTrips()
    {
        using ZsDic zs = ZsDic.FromRomfs(Corpus.TotkRoot!);
        byte[] data = Corpus.TotkTable(Corpus.TotkRoot!, zs);
        ResourceSizeTable table = ResourceSizeTable.FromBinary(data);

        Assert.Equal(RstbFormat.Restbl, table.Format);
        Assert.Equal(data, table.ToBinary());
        Assert.Equal(data, ResourceSizeTable.FromYaml(table.ToYaml()).ToBinary());
    }

    [BotwCorpusFact]
    public void BotwCalculatorReproducesVanilla()
    {
        string root = Corpus.BotwRoot!;
        ResourceSizeTable table = ResourceSizeTable.FromBinary(Corpus.BotwTable(root));
        bool wiiU = table.BigEndian;
        int checkedCount = 0, exact = 0;
        ConcurrentBag<string> under = [];

        Corpus.ForEachBotw(root, (name, data) =>
        {
            string canonical = BotwSizeCalculator.CanonicalName(name);
            uint size = BotwSizeCalculator.Calculate(name, data, wiiU, estimate: false);
            if (size == 0 || !table.TryGetSize(canonical, out uint vanilla)) return;
            Interlocked.Increment(ref checkedCount);
            if (size == vanilla) Interlocked.Increment(ref exact);
            else if (size < vanilla) under.Add($"{canonical}: {size} < {vanilla}");
        });

        // A handful of vanilla entries (navmeshes, a few maps) are larger than their type's constant.
        Assert.True(under.Count < checkedCount / 500, string.Join('\n', under.Take(50)));
        Assert.True(exact > checkedCount * 0.95, $"{exact} of {checkedCount} exact");
    }

    [TotkCorpusFact]
    public void TotkCalculatorNeverUnderestimatesVanilla()
    {
        string root = Corpus.TotkRoot!;
        using ZsDic zs = ZsDic.FromRomfs(root);
        ResourceSizeTable table = ResourceSizeTable.FromBinary(Corpus.TotkTable(root, zs));
        int checkedCount = 0, exact = 0;
        ConcurrentBag<string> under = [];

        Corpus.ForEachTotk(root, zs, (path, data) =>
        {
            uint size = TotkSizeCalculator.Calculate(path, data);
            if (size == 0 || !table.TryGetSize(TotkSizeCalculator.CanonicalName(path), out uint vanilla)) return;
            Interlocked.Increment(ref checkedCount);
            if (size == vanilla) Interlocked.Increment(ref exact);
            else if (size < vanilla) under.Add($"{path}: {size} < {vanilla}");
        });

        Assert.Empty(under);
        Assert.True(exact > checkedCount / 4, $"{exact} of {checkedCount} exact");
    }

    [OotCorpusFact]
    public void OotTableRoundTrips()
    {
        using ZsDic zs = ZsDic.FromRomfs(Corpus.OotRoot!);
        byte[] data = Corpus.OotTable(Corpus.OotRoot!, zs);
        ResourceSizeTable table = ResourceSizeTable.FromBinary(data);

        Assert.Equal(RstbFormat.Restbl, table.Format);
        Assert.Equal(data, table.ToBinary());
    }

    [OotCorpusFact]
    public void OotCalculatorNeverUnderestimatesVanilla()
    {
        string root = Corpus.OotRoot!;
        using ZsDic zs = ZsDic.FromRomfs(root);
        ResourceSizeTable table = ResourceSizeTable.FromBinary(Corpus.OotTable(root, zs));
        int checkedCount = 0, exact = 0;
        ConcurrentBag<string> under = [];

        Corpus.ForEachOot(root, zs, (path, data) =>
        {
            uint size = OotSizeCalculator.Calculate(path, data, margin: false);
            if (size == 0 || !table.TryGetSize(OotSizeCalculator.CanonicalName(path), out uint vanilla)) return;
            Interlocked.Increment(ref checkedCount);
            if (size == vanilla) Interlocked.Increment(ref exact);
            else if (size < vanilla) under.Add($"{path}: {size} < {vanilla}");
        });

        Assert.True(under.IsEmpty, string.Join('\n', under.Take(50)));
        Assert.True(exact > checkedCount * 0.9, $"{exact} of {checkedCount} exact");
    }
}
