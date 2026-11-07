namespace Sms.Core.Psg;

/// <summary>A detached inspection value, not a hardware read or save-state format.</summary>
public readonly record struct PsgToneClockSnapshot(
    int Period, int Counter, bool OutputHigh, int DividerPhase);

/// <summary>
/// Deterministic single-tone-channel timing subset, in PSG INPUT clocks (not master ticks).
/// Reference: https://www.smspower.org/Development/SN76489 and Maxim's
/// IDocs/PSGNotes.txt, "How the SN76489 makes sound": input /16, decrement a nonzero
/// counter, then reload and invert polarity when zero. Register writes select the
/// next reload value; they do not reload the running counter.
/// No noise, attenuation, waveform samples, bus timing, or physical power-on claims.
/// </summary>
public sealed class PsgToneClock
{
    private int period;
    private int counter;
    private bool outputHigh;
    private int dividerPhase;

    /// <summary>
    /// All state is caller-selected, not an assertion about hardware reset.
    /// Period must be 2..1023: zero/one behavior is deliberately unsupported pending
    /// revision-specific validation. Counter is independently 0..1023 (it may exceed
    /// the current period after a write). A zero counter reloads/toggles on the next
    /// INTERNAL tick, not immediately. Phase 0..15 counts input ticks since the last
    /// internal tick; phase 15 causes an internal tick on the next input tick.
    /// </summary>
    public PsgToneClock(int period, int counter, bool outputHigh, int dividerPhase)
    {
        ValidatePeriod(period);
        if (counter is < 0 or > 1023)
            throw new ArgumentOutOfRangeException(nameof(counter));
        if (dividerPhase is < 0 or > 15)
            throw new ArgumentOutOfRangeException(nameof(dividerPhase));

        this.period = period;
        this.counter = counter;
        this.outputHigh = outputHigh;
        this.dividerPhase = dividerPhase;
    }

    /// <summary>
    /// Immediately updates only the next reload value; preserves count, output and
    /// divider phase. Invalid values throw without changing state. Caller serializes
    /// writes and advances; no ordering at shared machine timestamps is implied.
    /// </summary>
    public void WritePeriod(int value)
    {
        ValidatePeriod(value);
        period = value;
    }

    /// <summary>
    /// Consumes exactly this many integer PSG input ticks, including an internal tick
    /// on the final input tick if the divider expires there. Returns the number of
    /// polarity changes during this call, not audio samples or a timestamped event
    /// stream. Zero is a no-op. All ulong inputs are supported without time overflow;
    /// there is no cumulative timestamp. Runtime is independent of elapsed ticks.
    /// </summary>
    public ulong AdvanceInputTicks(ulong inputTicks)
    {
        // Divide before adding phase so even ulong.MaxValue is safe.
        int phaseSum = dividerPhase + (int)(inputTicks % 16);
        ulong internalTicks = inputTicks / 16 + (ulong)(phaseSum / 16);
        dividerPhase = phaseSum % 16;

        ulong untilToggle = (ulong)Math.Max(counter, 1);
        if (internalTicks < untilToggle)
        {
            counter -= (int)internalTicks;
            return 0;
        }

        ulong remaining = internalTicks - untilToggle;
        ulong toggles = 1 + remaining / (ulong)period;
        counter = period - (int)(remaining % (ulong)period);
        if ((toggles & 1) != 0)
            outputHigh = !outputHigh;
        return toggles;
    }

    /// <summary>Side-effect-free; use under the machine owner's synchronization.</summary>
    public PsgToneClockSnapshot Inspect() => new(period, counter, outputHigh, dividerPhase);

    private static void ValidatePeriod(int value)
    {
        if (value is < 2 or > 1023)
            throw new ArgumentOutOfRangeException(nameof(value),
                "Only ten-bit tone periods 2..1023 are supported; zero/one policies remain unresolved.");
    }
}
