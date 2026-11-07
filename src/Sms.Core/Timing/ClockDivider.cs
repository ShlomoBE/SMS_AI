namespace Sms.Core.Timing;

/// <summary>
/// Counts whole device ticks from an integer number of source ticks, retaining phase.
/// Does not define the phase of any SMS component at hardware power-on.
/// </summary>
public sealed class ClockDivider
{
    public ClockDivider(uint divisor, uint initialPhase = 0)
    {
        ArgumentOutOfRangeException.ThrowIfZero(divisor);
        if (initialPhase >= divisor)
            throw new ArgumentOutOfRangeException(nameof(initialPhase));
        Divisor = divisor;
        Phase = initialPhase;
    }

    public uint Divisor { get; }
    public uint Phase { get; private set; }

    /// <summary>Returns completed device ticks; phase is the remaining source ticks.</summary>
    public ulong Advance(ulong sourceTicks)
    {
        // Divide first so adding the previous phase cannot overflow ulong.
        ulong completed = sourceTicks / Divisor;
        ulong remainder = sourceTicks % Divisor + Phase;
        completed += remainder / Divisor;
        Phase = (uint)(remainder % Divisor);
        return completed;
    }
}
