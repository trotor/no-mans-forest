using Nmf.Sim.Core;

namespace Nmf.Sim.Tests.Core;

public class RngTests
{
    [Fact]
    public void Seed42Stream54_MatchesPcg32ReferenceOutput()
    {
        // Reference values from the pcg-c demo (pcg32_srandom_r(42, 54)).
        var rng = new Rng(42, 54);
        uint[] expected = [0xa15c02b7, 0x7b47f409, 0xba1d3330, 0x83d2f293, 0xbfa4784b, 0xcbed606e];
        foreach (var value in expected)
            Assert.Equal(value, rng.NextUInt());
    }

    [Fact]
    public void NextInt_Seed42_MatchesGoldenValues()
    {
        var rng = new Rng(42);
        Assert.Equal(3, rng.NextInt(10));
        Assert.Equal(7, rng.NextInt(10));
        Assert.Equal(4, rng.NextInt(10));
    }

    [Fact]
    public void SameSeed_ProducesSameSequence()
    {
        var a = new Rng(1234);
        var b = new Rng(1234);
        for (int i = 0; i < 100; i++)
            Assert.Equal(a.NextUInt(), b.NextUInt());
    }

    [Fact]
    public void DifferentSeeds_ProduceDifferentSequences()
    {
        var a = new Rng(1);
        var b = new Rng(2);
        var sa = Enumerable.Range(0, 8).Select(_ => a.NextUInt()).ToArray();
        var sb = Enumerable.Range(0, 8).Select(_ => b.NextUInt()).ToArray();
        Assert.NotEqual(sa, sb);
    }

    [Fact]
    public void NextInt_StaysWithinBound()
    {
        var rng = new Rng(7);
        for (int i = 0; i < 10_000; i++)
            Assert.InRange(rng.NextInt(7), 0, 6);
    }

    [Fact]
    public void NextInt_MinMax_SupportsNegativeRanges()
    {
        var rng = new Rng(7);
        for (int i = 0; i < 10_000; i++)
            Assert.InRange(rng.NextInt(-5, 5), -5, 4);
    }

    [Fact]
    public void NextInt_IsRoughlyUniform()
    {
        var rng = new Rng(99);
        var buckets = new int[4];
        for (int i = 0; i < 40_000; i++)
            buckets[rng.NextInt(4)]++;
        Assert.All(buckets, count => Assert.InRange(count, 9_000, 11_000));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    public void NextInt_NonPositiveBound_Throws(int bound)
    {
        var rng = new Rng(1);
        Assert.Throws<ArgumentOutOfRangeException>(() => rng.NextInt(bound));
    }

    [Fact]
    public void NextInt_EmptyRange_Throws()
    {
        var rng = new Rng(1);
        Assert.Throws<ArgumentOutOfRangeException>(() => rng.NextInt(5, 5));
    }

    [Fact]
    public void State_RoundTripsThroughFromState()
    {
        var rng = new Rng(5);
        for (int i = 0; i < 5; i++) rng.NextUInt();
        var saved = rng.State;
        var expected = new[] { rng.NextUInt(), rng.NextUInt(), rng.NextUInt() };

        var restored = Rng.FromState(saved);
        Assert.Equal(expected, new[] { restored.NextUInt(), restored.NextUInt(), restored.NextUInt() });
    }

    [Fact]
    public void FromState_EvenIncrement_Throws()
    {
        Assert.Throws<ArgumentException>(() => Rng.FromState(new RngState(1, 2)));
    }

    [Fact]
    public void Chance_ZeroNeverAndThousandAlways()
    {
        var rng = new Rng(3);
        for (int i = 0; i < 1000; i++)
        {
            Assert.False(rng.Chance(0));
            Assert.True(rng.Chance(1000));
        }
    }

    [Fact]
    public void Chance_AlwaysConsumesOneDraw()
    {
        var a = new Rng(11);
        var b = new Rng(11);
        a.Chance(0);
        b.NextInt(1000);
        Assert.Equal(a.State, b.State);
    }
}
