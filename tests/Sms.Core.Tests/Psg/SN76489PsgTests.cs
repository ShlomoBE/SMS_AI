using Sms.Core.Psg;
using Sms.Core.Timing;

namespace Sms.Core.Tests.Psg;

public sealed class SN76489PsgTests
{
    [Fact]
    public void LatchByteSetsTargetRegisterAndLowDataNibble()
    {
        var psg = new SN76489Psg();

        // Target Reg 0 (Tone0 Freq): Write 0x8F (1000 1111) -> Reg 0, low 4 bits = 0x0F
        psg.WritePort(0x8F);
        Assert.Equal(0, psg.LatchedRegister);
        Assert.Equal(0x00F, psg.GetTonePeriod(0));

        // Target Reg 1 (Tone0 Volume): Write 0x9A (1001 1010) -> Reg 1, attenuation = 10
        psg.WritePort(0x9A);
        Assert.Equal(1, psg.LatchedRegister);
        Assert.Equal(10, psg.GetAttenuation(0));

        // Target Reg 2 (Tone1 Freq): Write 0xA5 (1010 0101) -> Reg 2, low 4 bits = 0x05
        psg.WritePort(0xA5);
        Assert.Equal(2, psg.LatchedRegister);
        Assert.Equal(0x005, psg.GetTonePeriod(1));

        // Target Reg 3 (Tone1 Volume): Write 0xB3 (1011 0011) -> Reg 3, attenuation = 3
        psg.WritePort(0xB3);
        Assert.Equal(3, psg.LatchedRegister);
        Assert.Equal(3, psg.GetAttenuation(1));

        // Target Reg 4 (Tone2 Freq): Write 0xC7 (1100 0111) -> Reg 4, low 4 bits = 0x07
        psg.WritePort(0xC7);
        Assert.Equal(4, psg.LatchedRegister);
        Assert.Equal(0x007, psg.GetTonePeriod(2));

        // Target Reg 5 (Tone2 Volume): Write 0xD2 (1101 0010) -> Reg 5, attenuation = 2
        psg.WritePort(0xD2);
        Assert.Equal(5, psg.LatchedRegister);
        Assert.Equal(2, psg.GetAttenuation(2));

        // Target Reg 6 (Noise Control): Write 0xE4 (1110 0100) -> Reg 6, noise control = 4
        psg.WritePort(0xE4);
        Assert.Equal(6, psg.LatchedRegister);
        Assert.Equal(4, psg.NoiseControl);

        // Target Reg 7 (Noise Volume): Write 0xF1 (1111 0001) -> Reg 7, attenuation = 1
        psg.WritePort(0xF1);
        Assert.Equal(7, psg.LatchedRegister);
        Assert.Equal(1, psg.GetAttenuation(3));
    }

    [Fact]
    public void DataByteUpdatesHighSixBitsForToneRegistersAndPreservesLowNibble()
    {
        var psg = new SN76489Psg();

        // Tone 0: Write low nibble 0x0C (Latch 0x8C)
        psg.WritePort(0x8C);
        Assert.Equal(0x00C, psg.GetTonePeriod(0));

        // Write high 6 bits 0x3F (Data 0x3F -> bits 5-0)
        psg.WritePort(0x3F);
        Assert.Equal(0x3FC, psg.GetTonePeriod(0));

        // Write another low nibble 0x03 (Latch 0x83) -> preserves high bits 0x3F0
        psg.WritePort(0x83);
        Assert.Equal(0x3F3, psg.GetTonePeriod(0));

        // Write high 6 bits 0x01 (Data 0x01) -> preserves low bits 0x03
        psg.WritePort(0x01);
        Assert.Equal(0x013, psg.GetTonePeriod(0));
    }

    [Fact]
    public void DataByteIsIgnoredWhenLatchedToVolumeOrNoiseControl()
    {
        var psg = new SN76489Psg();

        // Latch Tone 0 Volume (Reg 1), attenuation = 5
        psg.WritePort(0x95);
        Assert.Equal(5, psg.GetAttenuation(0));

        // Write Data byte 0x2A -> should be ignored for volume
        psg.WritePort(0x2A);
        Assert.Equal(5, psg.GetAttenuation(0));

        // Latch Noise Control (Reg 6), control = 6
        psg.WritePort(0xE6);
        Assert.Equal(6, psg.NoiseControl);

        // Write Data byte 0x15 -> should be ignored for noise control
        psg.WritePort(0x15);
        Assert.Equal(6, psg.NoiseControl);
    }

    [Fact]
    public void VolumeAttenuationLookupTableMatchesLogarithmic2dBStepsAndMuteAt15()
    {
        Assert.Equal(1.0f, SN76489Psg.GetVolumeTableEntry(0), precision: 5);
        Assert.Equal(0.0f, SN76489Psg.GetVolumeTableEntry(15), precision: 5);

        // Step 1 (-2dB)
        float step1 = SN76489Psg.GetVolumeTableEntry(1);
        Assert.True(step1 < 1.0f);
        Assert.Equal(MathF.Pow(10.0f, -0.1f), step1, precision: 5);

        // Verify strictly monotonic decreasing sequence from 0 to 14, and 15 is 0
        for (byte i = 0; i < 14; i++)
        {
            float current = SN76489Psg.GetVolumeTableEntry(i);
            float next = SN76489Psg.GetVolumeTableEntry((byte)(i + 1));
            Assert.True(current > next, $"Volume at {i} ({current}) should be greater than volume at {i+1} ({next})");
        }
        Assert.Equal(0.0f, SN76489Psg.GetVolumeTableEntry(15));
    }

    [Fact]
    public void ToneChannelSquareWaveTogglesEvery16TimesPeriodPsgInputTicks()
    {
        var psg = new SN76489Psg();

        // Set Tone 0 Period = 4, Volume = 0 (max volume)
        psg.WritePort(0x84); // Latch Tone 0 Freq Low = 4
        psg.WritePort(0x00); // Data Tone 0 High = 0 -> Period = 4
        psg.WritePort(0x90); // Latch Tone 0 Volume = 0 (max volume)

        bool initialPolarity = psg.GetToneOutputHigh(0);

        // Advancing 3 internal ticks (48 input ticks) should not toggle yet
        psg.AdvanceClockTicks(48);
        Assert.Equal(initialPolarity, psg.GetToneOutputHigh(0));

        // 4th internal tick (total 64 input ticks) causes a toggle
        psg.AdvanceClockTicks(16);
        Assert.NotEqual(initialPolarity, psg.GetToneOutputHigh(0));

        // Advance another 4 internal ticks (64 input ticks) -> toggles back to initial polarity
        psg.AdvanceClockTicks(64);
        Assert.Equal(initialPolarity, psg.GetToneOutputHigh(0));
    }

    [Fact]
    public void ToneChannelPeriod0And1OutputConstantDC()
    {
        var psg = new SN76489Psg();

        // Tone 0: Period = 0, Volume = 0 (max volume)
        psg.WritePort(0x80);
        psg.WritePort(0x00);
        psg.WritePort(0x90);

        // Unmute only Tone 0
        float samplePeriod0 = psg.GetMixedOutputSample();
        psg.AdvanceClockTicks(500);
        float samplePeriod0AfterTicks = psg.GetMixedOutputSample();

        // Output wave should remain +1.0f (DC)
        Assert.Equal(samplePeriod0, samplePeriod0AfterTicks);
        // Mixed output = (1.0f * 1.0f) / 4.0f = 0.25f
        Assert.Equal(0.25f, samplePeriod0, precision: 4);

        // Tone 0: Period = 1
        psg.WritePort(0x81);
        psg.WritePort(0x00);

        float samplePeriod1 = psg.GetMixedOutputSample();
        psg.AdvanceClockTicks(500);
        float samplePeriod1AfterTicks = psg.GetMixedOutputSample();

        Assert.Equal(samplePeriod1, samplePeriod1AfterTicks);
        Assert.Equal(0.25f, samplePeriod1, precision: 4);
    }

    [Fact]
    public void WritingFrequencyRegisterDoesNotResetRunningCounterImmediately()
    {
        var psg = new SN76489Psg();

        // Set Tone 0 Period = 10 (160 input ticks per half-period)
        psg.WritePort(0x8A); // Latch 10
        psg.WritePort(0x00);

        // Advance 5 internal ticks (80 input ticks) -> counter is now 5
        psg.AdvanceClockTicks(80);
        int counterBeforeWrite = psg.GetToneCounter(0);

        // Change period to 20
        psg.WritePort(0x84);
        psg.WritePort(0x01); // 0x014 = 20

        // Running counter should preserve its current countdown value (5)
        Assert.Equal(counterBeforeWrite, psg.GetToneCounter(0));
    }

    [Fact]
    public void WritingNoiseControlReseedsLfsrTo0x8000()
    {
        var psg = new SN76489Psg();
        Assert.Equal(0x8000, psg.Lfsr);

        // Write Noise Control (Reg 6), Periodic Noise, Rate 0
        psg.WritePort(0xE0);
        Assert.Equal(0x8000, psg.Lfsr);

        // Advance clock ticks to cause LFSR shifts
        psg.AdvanceClockTicks(160);
        Assert.NotEqual(0x8000, psg.Lfsr);

        // Rewrite Noise Control -> should reseed LFSR to 0x8000
        psg.WritePort(0xE4); // White noise, Rate 0
        Assert.Equal(0x8000, psg.Lfsr);
    }

    [Fact]
    public void PeriodicNoiseRepeatsEvery16Shifts()
    {
        var psg = new SN76489Psg();

        // Write Periodic Noise, Rate 0 (16 input ticks per shift)
        psg.WritePort(0xE0);
        Assert.Equal(0x8000, psg.Lfsr);

        // Shift 16 times (16 * 16 = 256 input ticks)
        psg.AdvanceClockTicks(256);
        Assert.Equal(0x8000, psg.Lfsr);
    }

    [Fact]
    public void WhiteNoiseUsesTappedFeedbackBit0XorBit3()
    {
        var psg = new SN76489Psg();

        // Write White Noise, Rate 0
        psg.WritePort(0xE4);
        ushort seed = psg.Lfsr;
        Assert.Equal(0x8000, seed);

        // Advance 16 shifts (256 input ticks)
        psg.AdvanceClockTicks(256);
        ushort lfsrAfter16Shifts = psg.Lfsr;

        // White noise should diverge from periodic noise (0x8000) after 16 shifts
        Assert.NotEqual(0x8000, lfsrAfter16Shifts);
    }

    [Fact]
    public void ShiftRate3IsDrivenByTone2Period()
    {
        var psg = new SN76489Psg();

        // Tone 2 Period = 4 (64 input ticks per half-period)
        psg.WritePort(0xC4);
        psg.WritePort(0x00);

        // Noise Control: White noise, Shift Rate 3 (Tone 2 driven)
        psg.WritePort(0xE7);
        ushort initialLfsr = psg.Lfsr;

        // Advancing 48 input ticks (< 64 ticks for Tone 2 reload) -> LFSR should not shift
        psg.AdvanceClockTicks(48);
        Assert.Equal(initialLfsr, psg.Lfsr);

        // Advancing past 64 ticks -> Tone 2 counter reloads and shifts LFSR
        psg.AdvanceClockTicks(16);
        Assert.NotEqual(initialLfsr, psg.Lfsr);
    }

    [Fact]
    public void GeneratePcmSamplesFillsBufferWithNormalizedFloatSamples()
    {
        var psg = new SN76489Psg();

        // Set Tone 0 Period = 10, Volume = 0 (max volume)
        psg.WritePort(0x8A);
        psg.WritePort(0x00);
        psg.WritePort(0x90);

        float[] buffer = new float[735]; // ~1 frame at 44100Hz / 60fps
        ulong masterTicksForFrame = 178977; // 59659 PSG ticks * 3 master ticks

        psg.GeneratePcmSamples(buffer, buffer.Length, 44100.0, masterTicksForFrame);

        for (int i = 0; i < buffer.Length; i++)
        {
            Assert.InRange(buffer[i], -1.0f, 1.0f);
        }
    }

    [Fact]
    public void MutedPsgGeneratesAllZerosPcmOutput()
    {
        var psg = new SN76489Psg(); // All attenuations default to 15 (muted)

        float[] buffer = new float[100];
        psg.GeneratePcmSamples(buffer, buffer.Length, 44100.0, 30000);

        for (int i = 0; i < buffer.Length; i++)
        {
            Assert.Equal(0.0f, buffer[i]);
        }
    }

    [Fact]
    public void GeneratePcmSamplesIsDeterministicAcrossIdenticalInstances()
    {
        var psg1 = new SN76489Psg();
        var psg2 = new SN76489Psg();

        byte[] writes = [0x8A, 0x00, 0x90, 0xA5, 0x00, 0xB2, 0xE4, 0xF1];
        foreach (byte b in writes)
        {
            psg1.WritePort(b);
            psg2.WritePort(b);
        }

        float[] buffer1 = new float[500];
        float[] buffer2 = new float[500];

        psg1.GeneratePcmSamples(buffer1, buffer1.Length, 48000.0, 100000);
        psg2.GeneratePcmSamples(buffer2, buffer2.Length, 48000.0, 100000);

        Assert.Equal(buffer1, buffer2);
    }

    [Fact]
    public void GeneratePcmSamplesThrowsOnInvalidParameters()
    {
        var psg = new SN76489Psg();

        Assert.Throws<ArgumentNullException>(() => psg.GeneratePcmSamples(null!, 10, 44100.0, 1000));
        Assert.Throws<ArgumentOutOfRangeException>(() => psg.GeneratePcmSamples(new float[10], -1, 44100.0, 1000));
        Assert.Throws<ArgumentOutOfRangeException>(() => psg.GeneratePcmSamples(new float[10], 11, 44100.0, 1000));
    }
}
