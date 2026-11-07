using Sms.Core.Psg;

namespace Sms.Core.Tests.Psg;

// Timing reference: SMS Power SN76489 / IDocs/PSGNotes.txt,
// "How the SN76489 makes sound". These test a software-selected initial phase,
// not physical power-on, oscillator frequency, or machine access ordering.
public sealed class PsgToneClockTests
{
    [Fact]
    public void MaxAdvanceFromZeroCounterWithNonzeroPhaseMatchesLiteralBoundary()
    {
        var clock = new PsgToneClock(period: 7, counter: 0, outputHigh: false, dividerPhase: 15);
        clock.WritePeriod(3);

        Assert.Equal(384_307_168_202_282_326UL, clock.AdvanceInputTicks(ulong.MaxValue));
        Assert.Equal(new PsgToneClockSnapshot(3, 3, false, 14), clock.Inspect());
    }

    [Theory]
    [InlineData(2, 0, false, 0)]
    [InlineData(1023, 1023, true, 15)]
    [InlineData(2, 1023, true, 7)]
    public void InitializationAndInspectionAreExplicit(int period, int counter, bool high, int phase)
    {
        var clock = new PsgToneClock(period, counter, high, phase);
        var expected = new PsgToneClockSnapshot(period, counter, high, phase);
        Assert.Equal(expected, clock.Inspect());
        Assert.Equal(0UL, clock.AdvanceInputTicks(0));
        Assert.Equal(expected, clock.Inspect());
    }

    [Theory]
    [InlineData(int.MinValue)]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(1024)]
    [InlineData(int.MaxValue)]
    public void UnsupportedPeriodsAreRejectedWithoutMutation(int period)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new PsgToneClock(period, 5, true, 3));
        var clock = new PsgToneClock(7, 5, true, 3);
        var before = clock.Inspect();
        Assert.Throws<ArgumentOutOfRangeException>(() => clock.WritePeriod(period));
        Assert.Equal(before, clock.Inspect());
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(1024, 0)]
    [InlineData(1, -1)]
    [InlineData(1, 16)]
    public void InvalidCounterOrPhaseIsRejected(int counter, int phase)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new PsgToneClock(2, counter, false, phase));
    }

    [Fact]
    public void EveryDividerPhaseExpiresOnExactlyTheSixteenthAccumulatedInputTick()
    {
        for (int phase = 0; phase < 16; phase++)
        {
            var clock = new PsgToneClock(3, 1, false, phase);
            Assert.Equal(0UL, clock.AdvanceInputTicks((ulong)(15 - phase)));
            Assert.Equal(new PsgToneClockSnapshot(3, 1, false, 15), clock.Inspect());
            Assert.Equal(1UL, clock.AdvanceInputTicks(1));
            Assert.Equal(new PsgToneClockSnapshot(3, 3, true, 0), clock.Inspect());
        }
    }

    [Fact]
    public void AllTenBitCountersDecrementAndReloadAtTheirExactBoundary()
    {
        for (int counter = 0; counter <= 1023; counter++)
        {
            var clock = new PsgToneClock(1023, counter, true, 0);
            int ticksUntilReload = Math.Max(counter, 1);
            Assert.Equal(0UL, clock.AdvanceInputTicks((ulong)((ticksUntilReload - 1) * 16)));
            Assert.Equal(counter == 0 ? 0 : 1, clock.Inspect().Counter);
            Assert.True(clock.Inspect().OutputHigh);
            Assert.Equal(1UL, clock.AdvanceInputTicks(16));
            Assert.Equal(new PsgToneClockSnapshot(1023, 1023, false, 0), clock.Inspect());
        }
    }

    [Fact]
    public void EverySupportedPeriodHasTwoReloadsPerFullCycle()
    {
        for (int period = 2; period <= 1023; period++)
        {
            var clock = new PsgToneClock(period, period, false, 0);
            Assert.Equal(0UL, clock.AdvanceInputTicks((ulong)(period * 16 - 1)));
            Assert.Equal(new PsgToneClockSnapshot(period, 1, false, 15), clock.Inspect());
            Assert.Equal(1UL, clock.AdvanceInputTicks(1));
            Assert.Equal(new PsgToneClockSnapshot(period, period, true, 0), clock.Inspect());
            Assert.Equal(1UL, clock.AdvanceInputTicks((ulong)(period * 16)));
            Assert.Equal(new PsgToneClockSnapshot(period, period, false, 0), clock.Inspect());
        }
    }

    [Theory]
    [InlineData(2)]
    [InlineData(1023)]
    public void PeriodWritePreservesRunningCountOutputAndPartialDivider(int newPeriod)
    {
        var clock = new PsgToneClock(7, 5, false, 11);
        clock.WritePeriod(newPeriod);
        Assert.Equal(new PsgToneClockSnapshot(newPeriod, 5, false, 11), clock.Inspect());
        Assert.Equal(0UL, clock.AdvanceInputTicks(68));
        Assert.Equal(new PsgToneClockSnapshot(newPeriod, 1, false, 15), clock.Inspect());
        Assert.Equal(1UL, clock.AdvanceInputTicks(1));
        Assert.Equal(new PsgToneClockSnapshot(newPeriod, newPeriod, true, 0), clock.Inspect());
    }

    [Fact]
    public void LastWriteBeforeReloadWinsAndWriteAfterReloadDoesNotUndoIt()
    {
        var clock = new PsgToneClock(7, 1, false, 15);
        clock.WritePeriod(3);
        clock.WritePeriod(9);
        Assert.Equal(1UL, clock.AdvanceInputTicks(1));
        clock.WritePeriod(2);
        Assert.Equal(new PsgToneClockSnapshot(2, 9, true, 0), clock.Inspect());
        Assert.Equal(1UL, clock.AdvanceInputTicks(9 * 16));
        Assert.Equal(new PsgToneClockSnapshot(2, 2, false, 0), clock.Inspect());
    }

    [Fact]
    public void BulkAdvancementMatchesIndependentInputTickModelWithWrites()
    {
        var random = new Random(76489);
        for (int scenario = 0; scenario < 64; scenario++)
        {
            int period = random.Next(2, 1024);
            int counter = random.Next(1024);
            int phase = random.Next(16);
            bool high = (scenario & 1) != 0;
            var clock = new PsgToneClock(period, counter, high, phase);
            for (int operation = 0; operation < 32; operation++)
            {
                period = random.Next(2, 1024);
                clock.WritePeriod(period);
                int ticks = random.Next(4096);
                ulong toggles = 0;
                for (int tick = 0; tick < ticks; tick++)
                {
                    phase++;
                    if (phase != 16) continue;
                    phase = 0;
                    if (counter > 0) counter--;
                    if (counter != 0) continue;
                    counter = period;
                    high = !high;
                    toggles++;
                }
                Assert.Equal(toggles, clock.AdvanceInputTicks((ulong)ticks));
                Assert.Equal(new PsgToneClockSnapshot(period, counter, high, phase), clock.Inspect());
            }
        }
    }

    [Theory]
    [InlineData(2, 0)]
    [InlineData(2, 15)]
    [InlineData(1023, 0)]
    [InlineData(1023, 15)]
    public void MaximumAdvanceIsOverflowSafeAndPartitionInvariant(int period, int phase)
    {
        var bulk = new PsgToneClock(period, 1023, false, phase);
        var split = new PsgToneClock(period, 1023, false, phase);
        ulong bulkToggles = bulk.AdvanceInputTicks(ulong.MaxValue);
        ulong splitToggles = split.AdvanceInputTicks(ulong.MaxValue - 12345);
        splitToggles += split.AdvanceInputTicks(12345);
        Assert.Equal(bulkToggles, splitToggles);
        Assert.Equal(bulk.Inspect(), split.Inspect());

        // ulong.MaxValue input ticks = 2^60-1 whole /16 ticks plus 15 input ticks.
        ulong internalTicks = (1UL << 60) - 1 + (phase == 0 ? 0UL : 1UL);
        ulong remaining = internalTicks - 1023;
        ulong expectedToggles = 1 + remaining / (ulong)period;
        Assert.Equal(expectedToggles, bulkToggles);
        Assert.Equal(new PsgToneClockSnapshot(period,
            period - (int)(remaining % (ulong)period), (expectedToggles & 1) != 0,
            (phase + 15) % 16), bulk.Inspect());
    }

    [Fact]
    public void ArbitraryPartitionsPreserveCountsOutputAndPhaseAcrossWrites()
    {
        var bulk = new PsgToneClock(17, 23, true, 9);
        var split = new PsgToneClock(17, 23, true, 9);
        var random = new Random(16);
        foreach (int period in new[] { 2, 1023, 17, 3, 254 })
        {
            bulk.WritePeriod(period);
            split.WritePeriod(period);
            const ulong total = 100003;
            ulong expectedToggles = bulk.AdvanceInputTicks(total);
            ulong actualToggles = 0;
            ulong left = total;
            while (left > 0)
            {
                ulong chunk = Math.Min(left, (ulong)random.Next(1, 128));
                actualToggles += split.AdvanceInputTicks(chunk);
                left -= chunk;
            }
            Assert.Equal(expectedToggles, actualToggles);
            Assert.Equal(bulk.Inspect(), split.Inspect());
        }
    }
}
