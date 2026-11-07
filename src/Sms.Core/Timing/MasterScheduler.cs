namespace Sms.Core.Timing;

/// <summary>
/// Snapshot of the MasterScheduler state for deterministic serialization and fixture creation.
/// </summary>
public readonly record struct MasterSchedulerSnapshot(
    ulong MasterTicks,
    uint CpuPhase,
    uint VdpPhase,
    uint PsgPhase);

/// <summary>
/// Ticks completed across system clock domains for a given advance step.
/// </summary>
public readonly record struct MasterSchedulerAdvanceResult(
    ulong CpuTicks,
    ulong VdpTicks,
    ulong PsgTicks)
{
    public ulong Cpu => CpuTicks;
    public ulong Vdp => VdpTicks;
    public ulong Psg => PsgTicks;

    public static MasterSchedulerAdvanceResult operator +(MasterSchedulerAdvanceResult left, MasterSchedulerAdvanceResult right)
    {
        return new MasterSchedulerAdvanceResult(
            left.CpuTicks + right.CpuTicks,
            left.VdpTicks + right.VdpTicks,
            left.PsgTicks + right.PsgTicks);
    }
}

/// <summary>
/// Deterministic master system scheduler for SMS clock domains.
/// Advances MasterTicks and derives domain ticks (CPU, VDP, PSG) using clock dividers while preserving phase.
/// </summary>
public sealed class MasterScheduler
{
    public const ulong NtscMasterClockHz = 10_738_580;
    public const ulong PalMasterClockHz = 10_640_850;

    public const uint CpuDivisor = 3;
    public const uint VdpDivisor = 2;
    public const uint PsgDivisor = 3;

    public ulong MasterTicks { get; private set; }

    public ClockDivider CpuDivider { get; }
    public ClockDivider VdpDivider { get; }
    public ClockDivider PsgDivider { get; }

    public uint CpuPhase => CpuDivider.Phase;
    public uint VdpPhase => VdpDivider.Phase;
    public uint PsgPhase => PsgDivider.Phase;

    public MasterScheduler(ulong initialMasterTicks = 0, uint cpuPhase = 0, uint vdpPhase = 0, uint psgPhase = 0)
    {
        if (cpuPhase >= CpuDivisor)
            throw new ArgumentOutOfRangeException(nameof(cpuPhase), $"cpuPhase must be less than CpuDivisor ({CpuDivisor}).");
        if (vdpPhase >= VdpDivisor)
            throw new ArgumentOutOfRangeException(nameof(vdpPhase), $"vdpPhase must be less than VdpDivisor ({VdpDivisor}).");
        if (psgPhase >= PsgDivisor)
            throw new ArgumentOutOfRangeException(nameof(psgPhase), $"psgPhase must be less than PsgDivisor ({PsgDivisor}).");

        MasterTicks = initialMasterTicks;
        CpuDivider = new ClockDivider(CpuDivisor, cpuPhase);
        VdpDivider = new ClockDivider(VdpDivisor, vdpPhase);
        PsgDivider = new ClockDivider(PsgDivisor, psgPhase);
    }

    public MasterScheduler(MasterSchedulerSnapshot snapshot)
        : this(snapshot.MasterTicks, snapshot.CpuPhase, snapshot.VdpPhase, snapshot.PsgPhase)
    {
    }

    public MasterSchedulerSnapshot GetSnapshot() => new(MasterTicks, CpuPhase, VdpPhase, PsgPhase);

    public MasterSchedulerSnapshot Snapshot => GetSnapshot();

    /// <summary>
    /// Advances total MasterTicks by specified masterTicks, calculating and returning
    /// completed ticks for CPU, VDP, and PSG clock domains while preserving phase.
    /// </summary>
    public MasterSchedulerAdvanceResult Advance(ulong masterTicks)
    {
        ArgumentOutOfRangeException.ThrowIfZero(masterTicks);

        MasterTicks += masterTicks;

        ulong cpuTicks = CpuDivider.Advance(masterTicks);
        ulong vdpTicks = VdpDivider.Advance(masterTicks);
        ulong psgTicks = PsgDivider.Advance(masterTicks);

        return new MasterSchedulerAdvanceResult(cpuTicks, vdpTicks, psgTicks);
    }

    /// <summary>
    /// Convenience method advancing master ticks by tStates * CpuDivisor (3 master ticks per T-state).
    /// </summary>
    public MasterSchedulerAdvanceResult AdvanceCpuTStates(int tStates)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(tStates);

        ulong masterTicks = (ulong)tStates * CpuDivisor;
        return Advance(masterTicks);
    }
}
