using Sms.Core.Timing;

namespace Sms.Core.Psg;

/// <summary>
/// SN76489 Programmable Sound Generator (PSG) implementation for Sega Master System.
/// Handles register latching/writes, 3 tone channels, 1 noise channel with LFSR,
/// clock advancement, and PCM sample buffer generation.
/// </summary>
public sealed class SN76489Psg
{
    private static readonly float[] VolumeTable = BuildVolumeTable();

    private readonly ushort[] tonePeriod = new ushort[3];
    private readonly int[] toneCounter = new int[3];
    private readonly bool[] toneOutputHigh = new bool[3] { true, true, true };
    private readonly byte[] attenuation = new byte[4] { 15, 15, 15, 15 };

    private byte latchedRegister;
    private byte noiseControl;
    private ushort lfsr = 0x8000;
    private int noiseCounter = 1;
    private int dividerPhase;

    /// <summary>
    /// Gets the current 10-bit frequency period (0..1023) for the specified tone channel (0..2).
    /// </summary>
    public ushort GetTonePeriod(int channel)
    {
        if (channel is < 0 or > 2) throw new ArgumentOutOfRangeException(nameof(channel));
        return tonePeriod[channel];
    }

    /// <summary>
    /// Gets the current 4-bit attenuation (0..15) for the specified channel (0..3).
    /// </summary>
    public byte GetAttenuation(int channel)
    {
        if (channel is < 0 or > 3) throw new ArgumentOutOfRangeException(nameof(channel));
        return attenuation[channel];
    }

    /// <summary>Gets the current 3-bit noise control register value (bit 2 = type, bits 1-0 = shift rate).</summary>
    public byte NoiseControl => noiseControl;

    /// <summary>Gets the current 16-bit LFSR value.</summary>
    public ushort Lfsr => lfsr;

    /// <summary>Gets the currently latched register (0..7).</summary>
    public byte LatchedRegister => latchedRegister;

    /// <summary>Gets the output polarity (+1.0f high / -1.0f low) for the specified tone channel (0..2).</summary>
    public bool GetToneOutputHigh(int channel)
    {
        if (channel is < 0 or > 2) throw new ArgumentOutOfRangeException(nameof(channel));
        return toneOutputHigh[channel];
    }

    /// <summary>Gets the current running counter for the specified tone channel (0..2).</summary>
    public int GetToneCounter(int channel)
    {
        if (channel is < 0 or > 2) throw new ArgumentOutOfRangeException(nameof(channel));
        return toneCounter[channel];
    }

    /// <summary>Gets the 2dB step logarithmic attenuation volume entry (0..15).</summary>
    public static float GetVolumeTableEntry(byte attenuationIndex)
    {
        return VolumeTable[attenuationIndex & 15];
    }

    /// <summary>
    /// Writes a byte to PSG port $7F following the SN76489 register latch/data protocol.
    /// </summary>
    public void WritePort(byte data)
    {
        bool isLatch = (data & 0x80) != 0;
        if (isLatch)
        {
            latchedRegister = (byte)((data >> 4) & 7);
            byte lowData = (byte)(data & 0x0F);
            int channel = latchedRegister >> 1;

            if ((latchedRegister & 1) != 0)
            {
                // Registers 1, 3, 5, 7: Attenuation
                attenuation[channel] = lowData;
            }
            else if (latchedRegister == 6)
            {
                // Register 6: Noise Control
                noiseControl = (byte)(data & 0x07);
                lfsr = 0x8000;
                noiseCounter = GetNoiseRatePeriod(noiseControl & 3);
            }
            else
            {
                // Registers 0, 2, 4: Tone Frequency Low 4 bits
                tonePeriod[channel] = (ushort)((tonePeriod[channel] & 0x3F0) | lowData);
                if (toneCounter[channel] <= 0) toneCounter[channel] = tonePeriod[channel];
            }
        }
        else
        {
            // Data byte (Bit 7 = 0)
            if (latchedRegister == 0 || latchedRegister == 2 || latchedRegister == 4)
            {
                int channel = latchedRegister >> 1;
                byte highData = (byte)(data & 0x3F);
                tonePeriod[channel] = (ushort)((tonePeriod[channel] & 0x00F) | (highData << 4));
                if (toneCounter[channel] <= 0) toneCounter[channel] = tonePeriod[channel];
            }
            // Ignored for volume (1, 3, 5, 7) or noise control (6)
        }
    }

    /// <summary>
    /// Alias for WritePort for compatibility with generic write interfaces.
    /// </summary>
    public void Write(byte data) => WritePort(data);

    /// <summary>
    /// Advances emulated PSG time by the specified number of PSG input clock ticks.
    /// </summary>
    public void AdvanceClockTicks(ulong psgInputTicks)
    {
        if (psgInputTicks == 0) return;

        int phaseSum = dividerPhase + (int)(psgInputTicks % 16);
        ulong internalTicks = psgInputTicks / 16 + (ulong)(phaseSum / 16);
        dividerPhase = phaseSum % 16;

        for (ulong i = 0; i < internalTicks; i++)
        {
            StepInternalTick();
        }
    }

    /// <summary>
    /// Returns the current mixed mono PCM sample in [-1.0f .. 1.0f].
    /// </summary>
    public float GetMixedOutputSample()
    {
        float sum = 0.0f;

        for (int i = 0; i < 3; i++)
        {
            float vol = VolumeTable[attenuation[i]];
            if (vol > 0.0f)
            {
                float wave = (tonePeriod[i] <= 1) ? 1.0f : (toneOutputHigh[i] ? 1.0f : -1.0f);
                sum += wave * vol;
            }
        }

        float noiseVol = VolumeTable[attenuation[3]];
        if (noiseVol > 0.0f)
        {
            float noiseWave = (lfsr & 1) != 0 ? 1.0f : -1.0f;
            sum += noiseWave * noiseVol;
        }

        return sum / 4.0f;
    }

    /// <summary>
    /// Generates normalized mono float PCM samples into outputBuffer over the specified master ticks duration.
    /// </summary>
    public void GeneratePcmSamples(float[] outputBuffer, int sampleCount, double sampleRateHz, ulong masterTicksToAdvance)
    {
        ArgumentNullException.ThrowIfNull(outputBuffer);
        if (sampleCount < 0 || sampleCount > outputBuffer.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(sampleCount));
        }
        if (sampleCount == 0) return;

        ulong totalPsgTicks;
        if (masterTicksToAdvance > 0)
        {
            totalPsgTicks = masterTicksToAdvance / MasterScheduler.PsgDivisor;
        }
        else if (sampleRateHz > 0)
        {
            double totalSeconds = sampleCount / sampleRateHz;
            totalPsgTicks = (ulong)Math.Round(totalSeconds * (MasterScheduler.NtscMasterClockHz / (double)MasterScheduler.PsgDivisor));
        }
        else
        {
            totalPsgTicks = 0;
        }

        ulong accumulatedTicks = 0;

        for (int i = 0; i < sampleCount; i++)
        {
            ulong targetTicks = totalPsgTicks > 0
                ? (ulong)(((ulong)(i + 1) * totalPsgTicks) / (ulong)sampleCount)
                : 0;

            ulong ticksToAdvance = targetTicks - accumulatedTicks;
            if (ticksToAdvance > 0)
            {
                AdvanceClockTicks(ticksToAdvance);
                accumulatedTicks = targetTicks;
            }

            outputBuffer[i] = GetMixedOutputSample();
        }

        if (totalPsgTicks > accumulatedTicks)
        {
            AdvanceClockTicks(totalPsgTicks - accumulatedTicks);
        }
    }

    private void StepInternalTick()
    {
        bool tone2Reloaded = false;

        for (int ch = 0; ch < 3; ch++)
        {
            ushort period = tonePeriod[ch];
            if (period >= 2)
            {
                if (toneCounter[ch] > 1)
                {
                    toneCounter[ch]--;
                }
                else
                {
                    toneCounter[ch] = period;
                    toneOutputHigh[ch] = !toneOutputHigh[ch];
                    if (ch == 2) tone2Reloaded = true;
                }
            }
            else
            {
                if (toneCounter[ch] > 1)
                {
                    toneCounter[ch]--;
                }
                else
                {
                    toneCounter[ch] = Math.Max((int)period, 1);
                    if (ch == 2) tone2Reloaded = true;
                }
            }
        }

        int shiftRate = noiseControl & 0x03;
        if (shiftRate == 3)
        {
            if (tone2Reloaded)
            {
                ShiftLfsr();
            }
        }
        else
        {
            int noisePeriod = GetNoiseRatePeriod(shiftRate);
            if (noiseCounter > 1)
            {
                noiseCounter--;
            }
            else
            {
                noiseCounter = noisePeriod;
                ShiftLfsr();
            }
        }
    }

    private void ShiftLfsr()
    {
        bool isWhiteNoise = (noiseControl & 0x04) != 0;
        int bit0 = lfsr & 1;
        int bit3 = (lfsr >> 3) & 1;
        int feedbackBit = isWhiteNoise ? (bit0 ^ bit3) : bit0;
        lfsr = (ushort)((lfsr >> 1) | (feedbackBit << 15));
    }

    private static int GetNoiseRatePeriod(int shiftRate)
    {
        return shiftRate switch
        {
            0 => 1,
            1 => 2,
            2 => 4,
            _ => 1
        };
    }

    private static float[] BuildVolumeTable()
    {
        float[] table = new float[16];
        for (int i = 0; i < 15; i++)
        {
            table[i] = MathF.Pow(10.0f, -0.1f * i);
        }
        table[15] = 0.0f;
        return table;
    }
}
