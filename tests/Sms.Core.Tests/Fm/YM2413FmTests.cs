using Sms.Core.Fm;
using Xunit;

namespace Sms.Core.Tests.Fm;

public class YM2413FmTests
{
    [Fact]
    public void PortF0_LatchesRegisterAddress()
    {
        var fm = new YM2413Fm();
        Assert.Equal(0, fm.LatchedAddress);

        fm.WritePortAddress(0x12);
        Assert.Equal(0x12, fm.LatchedAddress);

        fm.WritePortAddress(0x35);
        Assert.Equal(0x35, fm.LatchedAddress);
    }

    [Fact]
    public void PortF1_WritesDataToLatchedRegister()
    {
        var fm = new YM2413Fm();
        fm.WritePortAddress(0x10); // Channel 0 F-Number Low
        fm.WritePortData(0xAB);

        Assert.Equal(0xAB, fm.ReadRegister(0x10));
        Assert.Equal(0xAB, fm.GetFNumber(0));
    }

    [Fact]
    public void PortF2_UpdatesAudioControlFlags()
    {
        var fm = new YM2413Fm();

        Assert.False(fm.FmEnabled);
        Assert.False(fm.PsgMuted);
        Assert.Equal(0, fm.AudioControl);

        // Enable FM (Bit 0)
        fm.WriteAudioControl(0x01);
        Assert.True(fm.FmEnabled);
        Assert.False(fm.PsgMuted);
        Assert.Equal(0x01, fm.AudioControl);

        // Mute PSG (Bit 1) and keep FM enabled
        fm.WriteAudioControl(0x03);
        Assert.True(fm.FmEnabled);
        Assert.True(fm.PsgMuted);
        Assert.Equal(0x03, fm.AudioControl);

        // Disable FM and un-mute PSG
        fm.WriteAudioControl(0x00);
        Assert.False(fm.FmEnabled);
        Assert.False(fm.PsgMuted);
    }

    [Fact]
    public void Reset_ClearsAllRegistersAndState()
    {
        var fm = new YM2413Fm();
        fm.WritePortAddress(0x20);
        fm.WritePortData(0x1F);
        fm.WriteAudioControl(0x03);

        fm.Reset();

        Assert.Equal(0, fm.LatchedAddress);
        Assert.Equal(0, fm.AudioControl);
        Assert.False(fm.FmEnabled);
        Assert.False(fm.PsgMuted);
        Assert.Equal(0, fm.ReadRegister(0x20));
        Assert.Equal(0, fm.GetFNumber(0));
        Assert.False(fm.GetKeyOn(0));
    }

    [Fact]
    public void BuiltInRomPatches_All15InstrumentsCanBeSelected()
    {
        var fm = new YM2413Fm();

        string[] expectedNames = new string[]
        {
            "Custom", "Violin", "Guitar", "Piano", "Flute", "Clarinet",
            "Oboe", "Trumpet", "Organ", "Horn", "Synthesizer", "Harpsichord",
            "Vibraphone", "Synth Bass", "Acoustic Bass", "Electric Guitar"
        };

        for (int p = 0; p <= 15; p++)
        {
            fm.WritePortAddress(0x30); // Channel 0 patch & volume
            byte regVal = (byte)((p << 4) | 0x05); // Patch p, Volume 5
            fm.WritePortData(regVal);

            Assert.Equal(p, fm.GetPatchIndex(0));
            Assert.Equal(5, fm.GetVolume(0));

            OpllInstrumentPatch patch = fm.GetChannelPatch(0);
            Assert.Equal(expectedNames[p], patch.Name);
        }
    }

    [Fact]
    public void CustomPatchRegisters_00to07_UpdateCustomInstrumentParameters()
    {
        var fm = new YM2413Fm();

        // Write custom patch parameters to $00..$07
        byte[] customParams = new byte[] { 0x71, 0x61, 0x1E, 0x17, 0xD0, 0x78, 0x00, 0x17 };
        for (byte reg = 0; reg < 8; reg++)
        {
            fm.WritePortAddress(reg);
            fm.WritePortData(customParams[reg]);
        }

        // Select custom patch (0) on Channel 0
        fm.WritePortAddress(0x30);
        fm.WritePortData(0x00); // Patch 0, Vol 0

        OpllInstrumentPatch customPatch = fm.GetChannelPatch(0);
        Assert.Equal("Custom", customPatch.Name);
        Assert.Equal(1, customPatch.Modulator.Multiplier);
        Assert.True(customPatch.Modulator.Ksr);
        Assert.True(customPatch.Modulator.EgType);
        Assert.True(customPatch.Modulator.Vibrato);
        Assert.False(customPatch.Modulator.Tremolo);
    }

    [Fact]
    public void FNumberAndBlock_Calculation_CorrectFor9BitFNumAndOctave()
    {
        var fm = new YM2413Fm();

        // Channel 3 F-Num Low = 0x80
        fm.WritePortAddress(0x13);
        fm.WritePortData(0x80);

        // Channel 3 F-Num High = 1, Block = 4, Key-On = 0, Sustain = 1
        // Reg $23: Bit 0 = FNum High (1), Bits 3..1 = Block (4 = 100b -> 1000b = 0x08), Bit 5 = Sustain (0x20)
        byte reg23 = (byte)(1 | (4 << 1) | 0x20); // 0x01 | 0x08 | 0x20 = 0x29
        fm.WritePortAddress(0x23);
        fm.WritePortData(reg23);

        Assert.Equal(0x180, fm.GetFNumber(3)); // 0x100 | 0x80 = 0x180
        Assert.Equal(4, fm.GetBlock(3));
        Assert.True(fm.GetSustain(3));
        Assert.False(fm.GetKeyOn(3));
    }

    [Fact]
    public void KeyOn_KeyOff_Transitions_UpdateState()
    {
        var fm = new YM2413Fm();

        // Select Violin patch on channel 1
        fm.WritePortAddress(0x31);
        fm.WritePortData(0x10); // Patch 1, Vol 0

        // Set F-Number and Key-On on channel 1
        fm.WritePortAddress(0x11);
        fm.WritePortData(0x50);

        fm.WritePortAddress(0x21);
        fm.WritePortData(0x10); // Key-On = 1

        Assert.True(fm.GetKeyOn(1));

        // Key-Off channel 1
        fm.WritePortAddress(0x21);
        fm.WritePortData(0x00); // Key-On = 0

        Assert.False(fm.GetKeyOn(1));
    }

    [Fact]
    public void RhythmModeToggle_AndInstrumentTriggers_Register0E()
    {
        var fm = new YM2413Fm();

        Assert.False(fm.RhythmEnable);

        // Write to $0E: Rhythm Enable (Bit 5), BD (Bit 4), SD (Bit 3), HH (Bit 0)
        byte rData = (byte)(0x20 | 0x10 | 0x08 | 0x01);
        fm.WritePortAddress(0x0E);
        fm.WritePortData(rData);

        Assert.True(fm.RhythmEnable);
        Assert.True(fm.RhythmBassDrumOn);
        Assert.True(fm.RhythmSnareDrumOn);
        Assert.False(fm.RhythmTomTomOn);
        Assert.False(fm.RhythmTopCymbalOn);
        Assert.True(fm.RhythmHiHatOn);
    }

    [Fact]
    public void GeneratePcmSamples_DisabledFm_OutputsSilence()
    {
        var fm = new YM2413Fm();
        // FM disabled by default (AudioControl bit 0 = 0)

        float[] buffer = new float[100];
        for (int i = 0; i < buffer.Length; i++) buffer[i] = 99.0f;

        fm.GeneratePcmSamples(buffer, buffer.Length, 44100.0, 1000);

        for (int i = 0; i < buffer.Length; i++)
        {
            Assert.Equal(0.0f, buffer[i]);
        }
    }

    [Fact]
    public void GeneratePcmSamples_EnabledFmWithKeyOn_OutputsAudioData()
    {
        var fm = new YM2413Fm();

        // Enable FM via Port $F2
        fm.WriteAudioControl(0x01);

        // Setup Channel 0 with Violin patch, note F-Num, Block 4, Key-On
        fm.WritePortAddress(0x30);
        fm.WritePortData(0x10); // Patch 1 (Violin), Volume 0 (Max)

        fm.WritePortAddress(0x10);
        fm.WritePortData(0xAC); // F-Num low

        fm.WritePortAddress(0x20);
        fm.WritePortData(0x19); // KeyOn=1, Block=4, FNum High=1

        float[] buffer = new float[256];
        fm.GeneratePcmSamples(buffer, buffer.Length, 44100.0, 5000);

        bool containsNonZero = false;
        for (int i = 0; i < buffer.Length; i++)
        {
            Assert.InRange(buffer[i], -1.0f, 1.0f);
            if (buffer[i] != 0.0f)
            {
                containsNonZero = true;
            }
        }

        Assert.True(containsNonZero);
    }
}
