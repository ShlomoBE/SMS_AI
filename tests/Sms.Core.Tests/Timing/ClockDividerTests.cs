using Sms.Core.Timing;

namespace Sms.Core.Tests.Timing;

public sealed class ClockDividerTests
{
    [Fact]
    public void RejectsInvalidDivisorAndPhase()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ClockDivider(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ClockDivider(3, 3));
    }

    [Theory]
    [InlineData(0, 0, 0)]
    [InlineData(1, 0, 1)]
    [InlineData(2, 0, 2)]
    [InlineData(3, 1, 0)]
    [InlineData(10, 3, 1)]
    public void DividesSourceTicksAndPreservesRemainder(ulong input, ulong expectedTicks, uint expectedPhase)
    {
        var divider = new ClockDivider(3);
        Assert.Equal(expectedTicks, divider.Advance(input));
        Assert.Equal(expectedPhase, divider.Phase);
    }

    [Fact]
    public void ChunkingDoesNotChangeCountOrPhase()
    {
        var whole = new ClockDivider(3, 2);
        var chunked = new ClockDivider(3, 2);
        ulong total = 0;
        foreach (uint amount in new uint[] { 0, 1, 2, 12, 77, 1, 113 })
            total += chunked.Advance(amount);
        Assert.Equal(whole.Advance(206), total);
        Assert.Equal(whole.Phase, chunked.Phase);
    }

    [Fact]
    public void VariedPartitionsMatchOneAdvanceAcrossPhases()
    {
        for (uint divisor = 1; divisor <= 17; divisor++)
        for (uint phase = 0; phase < divisor; phase++)
        {
            var whole = new ClockDivider(divisor, phase);
            var chunked = new ClockDivider(divisor, phase);
            ulong total = 0;
            ulong accumulated = 0;
            for (uint i = 0; i < 101; i++)
            {
                uint amount = (i * 37 + divisor) % 23;
                total += amount;
                accumulated += chunked.Advance(amount);
            }
            Assert.Equal(whole.Advance(total), accumulated);
            Assert.Equal(whole.Phase, chunked.Phase);
        }
    }

    [Fact]
    public void LargeAdvancesDoNotOverflowWhenAddingPhase()
    {
        var divider = new ClockDivider(3, 2);
        Assert.Equal(6_148_914_691_236_517_205UL, divider.Advance(ulong.MaxValue));
        Assert.Equal(2U, divider.Phase);
    }

    [Fact]
    public void ExtremeInputsMatchWideIntegerReference()
    {
        ulong[] counts = [0, 1, uint.MaxValue, ulong.MaxValue - 1, ulong.MaxValue];
        uint[] divisors = [1, 2, 3, uint.MaxValue];
        foreach (uint divisor in divisors)
        foreach (uint phase in new uint[] { 0, divisor - 1 })
        foreach (ulong ticks in counts)
        {
            var divider = new ClockDivider(divisor, phase);
            var reference = (System.Numerics.BigInteger)ticks + phase;
            Assert.Equal((ulong)(reference / divisor), divider.Advance(ticks));
            Assert.Equal((uint)(reference % divisor), divider.Phase);
        }
    }

    [Fact]
    public void NtscFrameTickRatioIsConsistentWithDocumentedHypothesis()
    {
        // IDocs/VDP.txt §11: 342 dots/line, 262 lines/frame; docs/hardware-assumptions.md
        // records the master/2 dot and master/3 CPU ratios as a model-dependent hypothesis.
        const ulong frameMasterTicks = 342UL * 2 * 262;
        Assert.Equal(59_736UL, new ClockDivider(3).Advance(frameMasterTicks));
        Assert.Equal(89_604UL, new ClockDivider(2).Advance(frameMasterTicks));
    }
}
