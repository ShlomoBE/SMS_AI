using System.Numerics;
using Sms.Core.Timing;

namespace Sms.Core.Tests.Timing;

public sealed class MasterSchedulerTests
{
    [Fact]
    public void DomainConstantsAreCorrect()
    {
        Assert.Equal(10_738_580UL, MasterScheduler.NtscMasterClockHz);
        Assert.Equal(10_640_850UL, MasterScheduler.PalMasterClockHz);
        Assert.Equal(3U, MasterScheduler.CpuDivisor);
        Assert.Equal(2U, MasterScheduler.VdpDivisor);
        Assert.Equal(3U, MasterScheduler.PsgDivisor);
    }

    [Fact]
    public void InitialStateDefaultsToZero()
    {
        var scheduler = new MasterScheduler();

        Assert.Equal(0UL, scheduler.MasterTicks);
        Assert.Equal(0U, scheduler.CpuPhase);
        Assert.Equal(0U, scheduler.VdpPhase);
        Assert.Equal(0U, scheduler.PsgPhase);
        Assert.Equal(3U, scheduler.CpuDivider.Divisor);
        Assert.Equal(2U, scheduler.VdpDivider.Divisor);
        Assert.Equal(3U, scheduler.PsgDivider.Divisor);

        var snapshot = scheduler.GetSnapshot();
        Assert.Equal(new MasterSchedulerSnapshot(0, 0, 0, 0), snapshot);
        Assert.Equal(snapshot, scheduler.Snapshot);
    }

    [Fact]
    public void ConstructorWithInitialValuesAndSnapshotWorks()
    {
        var snapshot = new MasterSchedulerSnapshot(1000UL, 2U, 1U, 2U);
        var schedulerFromSnapshot = new MasterScheduler(snapshot);

        Assert.Equal(1000UL, schedulerFromSnapshot.MasterTicks);
        Assert.Equal(2U, schedulerFromSnapshot.CpuPhase);
        Assert.Equal(1U, schedulerFromSnapshot.VdpPhase);
        Assert.Equal(2U, schedulerFromSnapshot.PsgPhase);
        Assert.Equal(snapshot, schedulerFromSnapshot.GetSnapshot());

        var schedulerDirect = new MasterScheduler(1000UL, 2U, 1U, 2U);
        Assert.Equal(snapshot, schedulerDirect.GetSnapshot());
    }

    [Fact]
    public void InvalidConstructorPhasesThrow()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new MasterScheduler(0, cpuPhase: 3));
        Assert.Throws<ArgumentOutOfRangeException>(() => new MasterScheduler(0, vdpPhase: 2));
        Assert.Throws<ArgumentOutOfRangeException>(() => new MasterScheduler(0, psgPhase: 3));

        Assert.Throws<ArgumentOutOfRangeException>(() => new MasterScheduler(new MasterSchedulerSnapshot(0, 3, 0, 0)));
        Assert.Throws<ArgumentOutOfRangeException>(() => new MasterScheduler(new MasterSchedulerSnapshot(0, 0, 2, 0)));
        Assert.Throws<ArgumentOutOfRangeException>(() => new MasterScheduler(new MasterSchedulerSnapshot(0, 0, 0, 3)));
    }

    [Fact]
    public void InvalidAdvanceParametersThrow()
    {
        var scheduler = new MasterScheduler();

        Assert.Throws<ArgumentOutOfRangeException>(() => scheduler.Advance(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => scheduler.AdvanceCpuTStates(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => scheduler.AdvanceCpuTStates(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => scheduler.AdvanceCpuTStates(-100));
    }

    [Fact]
    public void AdvanceCpuTStatesScalesByCpuDivisor()
    {
        var s1 = new MasterScheduler();
        var result1 = s1.AdvanceCpuTStates(1); // 3 master ticks
        Assert.Equal(3UL, s1.MasterTicks);
        Assert.Equal(1UL, result1.CpuTicks);
        Assert.Equal(1UL, result1.VdpTicks);
        Assert.Equal(1UL, result1.PsgTicks);
        Assert.Equal(0U, s1.CpuPhase);
        Assert.Equal(1U, s1.VdpPhase);
        Assert.Equal(0U, s1.PsgPhase);

        var s2 = new MasterScheduler();
        var result2 = s2.AdvanceCpuTStates(4); // 12 master ticks
        Assert.Equal(12UL, s2.MasterTicks);
        Assert.Equal(4UL, result2.CpuTicks);
        Assert.Equal(6UL, result2.VdpTicks);
        Assert.Equal(4UL, result2.PsgTicks);
        Assert.Equal(0U, s2.CpuPhase);
        Assert.Equal(0U, s2.VdpPhase);
        Assert.Equal(0U, s2.PsgPhase);
    }

    [Fact]
    public void PartitionInvarianceAdvance10EqualsAdvance3PlusAdvance7()
    {
        var sDirect = new MasterScheduler();
        var directResult = sDirect.Advance(10);

        var sChunked = new MasterScheduler();
        var r3 = sChunked.Advance(3);
        var r7 = sChunked.Advance(7);
        var chunkedResult = r3 + r7;

        Assert.Equal(directResult, chunkedResult);
        Assert.Equal(sDirect.MasterTicks, sChunked.MasterTicks);
        Assert.Equal(sDirect.CpuPhase, sChunked.CpuPhase);
        Assert.Equal(sDirect.VdpPhase, sChunked.VdpPhase);
        Assert.Equal(sDirect.PsgPhase, sChunked.PsgPhase);
        Assert.Equal(sDirect.GetSnapshot(), sChunked.GetSnapshot());
    }

    [Fact]
    public void PartitionInvarianceAcrossArbitraryChunkingsAndInitialPhases()
    {
        uint[][] phaseCombinations = [
            [0, 0, 0],
            [1, 0, 2],
            [2, 1, 1],
            [2, 1, 2]
        ];

        foreach (var phases in phaseCombinations)
        {
            var direct = new MasterScheduler(100, phases[0], phases[1], phases[2]);
            var chunked = new MasterScheduler(100, phases[0], phases[1], phases[2]);

            ulong totalMasterTicks = 0;
            MasterSchedulerAdvanceResult accumulatedResult = default;

            ulong[] chunks = [1, 2, 5, 12, 1, 33, 7, 40];
            foreach (var chunk in chunks)
            {
                totalMasterTicks += chunk;
                accumulatedResult += chunked.Advance(chunk);
            }

            var directResult = direct.Advance(totalMasterTicks);

            Assert.Equal(directResult, accumulatedResult);
            Assert.Equal(direct.MasterTicks, chunked.MasterTicks);
            Assert.Equal(direct.GetSnapshot(), chunked.GetSnapshot());
        }
    }

    [Fact]
    public void PhaseRetentionAcrossSingleTickBoundaries()
    {
        var scheduler = new MasterScheduler();

        // Tick 1
        var r1 = scheduler.Advance(1);
        Assert.Equal(1UL, scheduler.MasterTicks);
        Assert.Equal(new MasterSchedulerAdvanceResult(0, 0, 0), r1);
        Assert.Equal(1U, scheduler.CpuPhase);
        Assert.Equal(1U, scheduler.VdpPhase);
        Assert.Equal(1U, scheduler.PsgPhase);

        // Tick 2
        var r2 = scheduler.Advance(1);
        Assert.Equal(2UL, scheduler.MasterTicks);
        Assert.Equal(new MasterSchedulerAdvanceResult(0, 1, 0), r2); // VDP completes 1 tick at tick 2
        Assert.Equal(2U, scheduler.CpuPhase);
        Assert.Equal(0U, scheduler.VdpPhase);
        Assert.Equal(2U, scheduler.PsgPhase);

        // Tick 3
        var r3 = scheduler.Advance(1);
        Assert.Equal(3UL, scheduler.MasterTicks);
        Assert.Equal(new MasterSchedulerAdvanceResult(1, 0, 1), r3); // CPU & PSG complete 1 tick at tick 3
        Assert.Equal(0U, scheduler.CpuPhase);
        Assert.Equal(1U, scheduler.VdpPhase);
        Assert.Equal(0U, scheduler.PsgPhase);
    }

    [Fact]
    public void LargeAdvanceValuesUpToUlongMaxMatchBigIntegerReference()
    {
        ulong[] advanceValues = [
            uint.MaxValue,
            ulong.MaxValue / 2,
            ulong.MaxValue - 1,
            ulong.MaxValue
        ];

        foreach (var phaseCpu in new uint[] { 0, 2 })
        foreach (var phaseVdp in new uint[] { 0, 1 })
        foreach (var phasePsg in new uint[] { 0, 2 })
        foreach (var ticks in advanceValues)
        {
            var scheduler = new MasterScheduler(0, phaseCpu, phaseVdp, phasePsg);
            var result = scheduler.Advance(ticks);

            Assert.Equal(ticks, scheduler.MasterTicks);

            BigInteger expectedCpu = (BigInteger)ticks + phaseCpu;
            BigInteger expectedVdp = (BigInteger)ticks + phaseVdp;
            BigInteger expectedPsg = (BigInteger)ticks + phasePsg;

            Assert.Equal((ulong)(expectedCpu / 3), result.CpuTicks);
            Assert.Equal((ulong)(expectedVdp / 2), result.VdpTicks);
            Assert.Equal((ulong)(expectedPsg / 3), result.PsgTicks);

            Assert.Equal((uint)(expectedCpu % 3), scheduler.CpuPhase);
            Assert.Equal((uint)(expectedVdp % 2), scheduler.VdpPhase);
            Assert.Equal((uint)(expectedPsg % 3), scheduler.PsgPhase);
        }
    }
}
