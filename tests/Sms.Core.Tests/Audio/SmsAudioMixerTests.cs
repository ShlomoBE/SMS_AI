using System;
using Sms.Core.Audio;
using Sms.Core.Fm;
using Sms.Core.Psg;
using Xunit;

namespace Sms.Core.Tests.Audio;

public sealed class SmsAudioMixerTests
{
    [Fact]
    public void SeparateOutputGainsScalePsgAndFmIndependently()
    {
        static float[] Render(float psgGain, float fmGain)
        {
            var mixer = new SmsAudioMixer { PsgGain = psgGain, FmGain = fmGain };
            mixer.WritePortF2(1);
            mixer.WritePort7F(0x8A); mixer.WritePort7F(0); mixer.WritePort7F(0x90);
            mixer.WritePortF0(0x30); mixer.WritePortF1(0x10);
            mixer.WritePortF0(0x10); mixer.WritePortF1(0xAC);
            mixer.WritePortF0(0x20); mixer.WritePortF1(0x19);
            var result = new float[1024];
            mixer.GeneratePcmSamples(result, result.Length, 44100, 249344);
            return result;
        }

        var psg = Render(1, 0);
        var fm = Render(0, 1);
        var combined = Render(1, 2);
        Assert.Contains(psg, sample => sample != 0);
        Assert.Contains(fm, sample => sample != 0);
        for (int i = 0; i < combined.Length; i++)
            Assert.Equal(Math.Clamp(psg[i] + 2 * fm[i], -1f, 1f), combined[i], 5);
    }

    [Fact]
    public void DefaultConstructorInitializesSubEnginesAndAutoMode()
    {
        var mixer = new SmsAudioMixer();

        Assert.Equal(SmsAudioMode.Auto, mixer.Mode);
        Assert.NotNull(mixer.Psg);
        Assert.NotNull(mixer.Fm);
    }

    [Fact]
    public void CustomConstructorUsesProvidedSubEngines()
    {
        var customPsg = new SN76489Psg();
        var customFm = new YM2413Fm();

        var mixer = new SmsAudioMixer(customPsg, customFm);

        Assert.Same(customPsg, mixer.Psg);
        Assert.Same(customFm, mixer.Fm);
    }

    [Fact]
    public void WritePort7FRoutesToPsgSubEngine()
    {
        var mixer = new SmsAudioMixer();

        // Write Tone 0 Period low nibble (0x8F -> latch Tone 0, low 4 bits = 15)
        mixer.WritePort7F(0x8F);
        Assert.Equal(0, mixer.Psg.LatchedRegister);
        Assert.Equal(0x00F, mixer.Psg.GetTonePeriod(0));

        // Write Tone 0 Volume (0x95 -> latch Tone 0 volume, attenuation = 5)
        mixer.WritePort7F(0x95);
        Assert.Equal(1, mixer.Psg.LatchedRegister);
        Assert.Equal(5, mixer.Psg.GetAttenuation(0));
    }

    [Fact]
    public void WritePortF0RoutesAddressToFmSubEngine()
    {
        var mixer = new SmsAudioMixer();

        mixer.WritePortF0(0x12);
        Assert.Equal(0x12, mixer.Fm.LatchedAddress);

        mixer.WritePortF0(0x35);
        Assert.Equal(0x35, mixer.Fm.LatchedAddress);
    }

    [Fact]
    public void WritePortF1RoutesDataToFmSubEngine()
    {
        var mixer = new SmsAudioMixer();

        // Latch address 0x10 (Channel 0 F-Number Low)
        mixer.WritePortF0(0x10);
        mixer.WritePortF1(0xAA);
        Assert.Equal(0xAA, mixer.Fm.GetFNumber(0));

        // Latch address 0x30 (Channel 0 Patch & Volume)
        mixer.WritePortF0(0x30);
        mixer.WritePortF1(0x47); // Patch 4, Volume 7
        Assert.Equal(4, mixer.Fm.GetPatchIndex(0));
        Assert.Equal(7, mixer.Fm.GetVolume(0));
    }

    [Fact]
    public void WritePortF2RoutesAudioControlToFmSubEngine()
    {
        var mixer = new SmsAudioMixer();

        // Write 0x01: FM Enabled = true, PSG Muted = false
        mixer.WritePortF2(0x01);
        Assert.Equal(0x01, mixer.Fm.AudioControl);
        Assert.True(mixer.Fm.FmEnabled);
        Assert.False(mixer.Fm.PsgMuted);

        // Write 0x02: FM Enabled = false, PSG Muted = true
        mixer.WritePortF2(0x02);
        Assert.Equal(0x02, mixer.Fm.AudioControl);
        Assert.False(mixer.Fm.FmEnabled);
        Assert.True(mixer.Fm.PsgMuted);

        // Write 0x03: FM Enabled = true, PSG Muted = true
        mixer.WritePortF2(0x03);
        Assert.Equal(0x03, mixer.Fm.AudioControl);
        Assert.True(mixer.Fm.FmEnabled);
        Assert.True(mixer.Fm.PsgMuted);
    }

    [Fact]
    public void ModeTransitionsAndUpdateStateCorrectly()
    {
        var mixer = new SmsAudioMixer();

        Assert.Equal(SmsAudioMode.Auto, mixer.Mode);

        mixer.Mode = SmsAudioMode.ForcePsg;
        Assert.Equal(SmsAudioMode.ForcePsg, mixer.Mode);

        mixer.Mode = SmsAudioMode.ForceFm;
        Assert.Equal(SmsAudioMode.ForceFm, mixer.Mode);

        mixer.Mode = SmsAudioMode.Auto;
        Assert.Equal(SmsAudioMode.Auto, mixer.Mode);
    }

    [Fact]
    public void AudioControlEvaluationUnderAutoMode()
    {
        var mixer = new SmsAudioMixer { Mode = SmsAudioMode.Auto };

        // Port $F2 = 0x00 (FM disabled, PSG not muted)
        mixer.WritePortF2(0x00);
        Assert.True(mixer.IsPsgEnabled);
        Assert.False(mixer.IsFmEnabled);

        // Port $F2 = 0x01 (FM enabled, PSG not muted)
        mixer.WritePortF2(0x01);
        Assert.True(mixer.IsPsgEnabled);
        Assert.True(mixer.IsFmEnabled);

        // Port $F2 = 0x02 (FM disabled, PSG muted)
        mixer.WritePortF2(0x02);
        Assert.False(mixer.IsPsgEnabled);
        Assert.False(mixer.IsFmEnabled);

        // Port $F2 = 0x03 (FM enabled, PSG muted)
        mixer.WritePortF2(0x03);
        Assert.False(mixer.IsPsgEnabled);
        Assert.True(mixer.IsFmEnabled);
    }

    [Fact]
    public void AudioControlEvaluationUnderForcePsgMode()
    {
        var mixer = new SmsAudioMixer { Mode = SmsAudioMode.ForcePsg };

        // Regardless of Port $F2 value, PSG is always enabled and FM is disabled
        byte[] controlValues = [0x00, 0x01, 0x02, 0x03, 0xFF];
        foreach (byte ctrl in controlValues)
        {
            mixer.WritePortF2(ctrl);
            Assert.True(mixer.IsPsgEnabled);
            Assert.False(mixer.IsFmEnabled);
        }
    }

    [Fact]
    public void AudioControlEvaluationUnderForceFmMode()
    {
        var mixer = new SmsAudioMixer { Mode = SmsAudioMode.ForceFm };

        // FM is always enabled under ForceFm
        // PSG is muted UNLESS $F2 has PsgMuted == false (i.e. bit 1 is 0)

        // Port $F2 = 0x00 (PsgMuted = false) -> both enabled
        mixer.WritePortF2(0x00);
        Assert.True(mixer.IsPsgEnabled);
        Assert.True(mixer.IsFmEnabled);

        // Port $F2 = 0x01 (PsgMuted = false) -> both enabled
        mixer.WritePortF2(0x01);
        Assert.True(mixer.IsPsgEnabled);
        Assert.True(mixer.IsFmEnabled);

        // Port $F2 = 0x02 (PsgMuted = true) -> PSG muted, FM enabled
        mixer.WritePortF2(0x02);
        Assert.False(mixer.IsPsgEnabled);
        Assert.True(mixer.IsFmEnabled);

        // Port $F2 = 0x03 (PsgMuted = true) -> PSG muted, FM enabled
        mixer.WritePortF2(0x03);
        Assert.False(mixer.IsPsgEnabled);
        Assert.True(mixer.IsFmEnabled);
    }

    [Fact]
    public void GeneratePcmSamplesInPsgOnlyActiveStateUsesFullGainWeighting()
    {
        var psg = new SN76489Psg();
        var fm = new YM2413Fm();
        var mixer = new SmsAudioMixer(psg, fm) { Mode = SmsAudioMode.ForcePsg };

        // Enable sound on PSG (Tone 0 Period = 10, Volume = 0 [max])
        psg.WritePort(0x8A);
        psg.WritePort(0x00);
        psg.WritePort(0x90);

        float[] mixerBuffer = new float[100];
        float[] psgDirectBuffer = new float[100];

        // Clone PSG state to get expected reference buffer
        var psgRef = new SN76489Psg();
        psgRef.WritePort(0x8A);
        psgRef.WritePort(0x00);
        psgRef.WritePort(0x90);
        psgRef.GeneratePcmSamples(psgDirectBuffer, psgDirectBuffer.Length, 44100.0, 30000);

        mixer.GeneratePcmSamples(mixerBuffer, mixerBuffer.Length, 44100.0, 30000);

        // Under single source active (PSG only), weight = 1.0f
        for (int i = 0; i < mixerBuffer.Length; i++)
        {
            Assert.Equal(psgDirectBuffer[i], mixerBuffer[i], precision: 5);
            Assert.InRange(mixerBuffer[i], -1.0f, 1.0f);
        }
    }

    [Fact]
    public void GeneratePcmSamplesInFmOnlyActiveStateUsesFullGainWeighting()
    {
        var psg = new SN76489Psg();
        var fm = new YM2413Fm();
        var mixer = new SmsAudioMixer(psg, fm) { Mode = SmsAudioMode.Auto };

        // Set Port $F2 = 0x03 (FM enabled, PSG muted) -> FM only active
        mixer.WritePortF2(0x03);

        // Setup FM Channel 0 note
        mixer.WritePortF0(0x10); mixer.WritePortF1(0x80); // F-Number low
        mixer.WritePortF0(0x20); mixer.WritePortF1(0x10); // KeyOn, Block 0, F-Number high = 0
        mixer.WritePortF0(0x30); mixer.WritePortF1(0x30); // Instrument 3, Volume 0

        float[] mixerBuffer = new float[100];
        mixer.GeneratePcmSamples(mixerBuffer, mixerBuffer.Length, 44100.0, 30000);

        for (int i = 0; i < mixerBuffer.Length; i++)
        {
            Assert.InRange(mixerBuffer[i], -1.0f, 1.0f);
        }

        // FM output should contain non-zero samples after KeyOn and clock advance
        bool hasNonZeroSample = false;
        for (int i = 0; i < mixerBuffer.Length; i++)
        {
            if (Math.Abs(mixerBuffer[i]) > 0.0001f)
            {
                hasNonZeroSample = true;
                break;
            }
        }
        Assert.True(hasNonZeroSample, "FM-only buffer should produce non-zero PCM audio samples");
    }

    [Fact]
    public void GeneratePcmSamplesInCombinedPsgAndFmStateMixesWithHalfGainWeighting()
    {
        var psg = new SN76489Psg();
        var fm = new YM2413Fm();
        var mixer = new SmsAudioMixer(psg, fm) { Mode = SmsAudioMode.Auto };

        // Port $F2 = 0x01 (FM enabled, PSG not muted) -> Both active
        mixer.WritePortF2(0x01);

        // Setup PSG Tone 0
        psg.WritePort(0x8A);
        psg.WritePort(0x00);
        psg.WritePort(0x90);

        // Setup FM Channel 0 note
        mixer.WritePortF0(0x10); mixer.WritePortF1(0x80);
        mixer.WritePortF0(0x20); mixer.WritePortF1(0x10);
        mixer.WritePortF0(0x30); mixer.WritePortF1(0x30);

        float[] mixerBuffer = new float[100];
        mixer.GeneratePcmSamples(mixerBuffer, mixerBuffer.Length, 44100.0, 30000);

        // Create references for PSG-only and FM-only outputs to verify 0.5f + 0.5f weighting
        var psgRef = new SN76489Psg();
        psgRef.WritePort(0x8A); psgRef.WritePort(0x00); psgRef.WritePort(0x90);
        float[] psgRefBuf = new float[100];
        psgRef.GeneratePcmSamples(psgRefBuf, psgRefBuf.Length, 44100.0, 30000);

        var fmRef = new YM2413Fm();
        fmRef.WriteAudioControl(0x01);
        fmRef.WritePortAddress(0x10); fmRef.WritePortData(0x80);
        fmRef.WritePortAddress(0x20); fmRef.WritePortData(0x10);
        fmRef.WritePortAddress(0x30); fmRef.WritePortData(0x30);
        float[] fmRefBuf = new float[100];
        fmRef.GeneratePcmSamples(fmRefBuf, fmRefBuf.Length, 44100.0, 30000);

        for (int i = 0; i < mixerBuffer.Length; i++)
        {
            float expected = Math.Clamp(psgRefBuf[i] * 0.5f + fmRefBuf[i] * 0.5f, -1.0f, 1.0f);
            Assert.Equal(expected, mixerBuffer[i], precision: 4);
        }
    }

    [Fact]
    public void GeneratePcmSamplesInNeitherActiveStateOutputsSilence()
    {
        var psg = new SN76489Psg();
        var fm = new YM2413Fm();
        var mixer = new SmsAudioMixer(psg, fm) { Mode = SmsAudioMode.Auto };

        // Port $F2 = 0x02 (FM disabled, PSG muted) -> Neither active
        mixer.WritePortF2(0x02);

        // Unmute PSG in PSG sub-engine directly to ensure mixer mute overrides it
        psg.WritePort(0x8A);
        psg.WritePort(0x00);
        psg.WritePort(0x90);

        float[] mixerBuffer = new float[100];
        mixer.GeneratePcmSamples(mixerBuffer, mixerBuffer.Length, 44100.0, 30000);

        for (int i = 0; i < mixerBuffer.Length; i++)
        {
            Assert.Equal(0.0f, mixerBuffer[i]);
        }
    }

    [Fact]
    public void GeneratePcmSamplesValidatesArguments()
    {
        var mixer = new SmsAudioMixer();

        Assert.Throws<ArgumentNullException>(() => mixer.GeneratePcmSamples(null!, 10, 44100.0, 1000));
        Assert.Throws<ArgumentOutOfRangeException>(() => mixer.GeneratePcmSamples(new float[10], -1, 44100.0, 1000));
        Assert.Throws<ArgumentOutOfRangeException>(() => mixer.GeneratePcmSamples(new float[10], 11, 44100.0, 1000));

        // Sample count 0 should return cleanly without error
        float[] emptyBuf = new float[10];
        mixer.GeneratePcmSamples(emptyBuf, 0, 44100.0, 1000);
    }

    [Fact]
    public void ScratchBuffersReallocateDynamicallyForLargeSampleCounts()
    {
        var mixer = new SmsAudioMixer();
        mixer.WritePort7F(0x8A); mixer.WritePort7F(0x00); mixer.WritePort7F(0x90);

        // First call with small count
        float[] smallBuf = new float[64];
        mixer.GeneratePcmSamples(smallBuf, smallBuf.Length, 44100.0, 10000);

        // Second call with larger count (e.g. 4096 samples)
        float[] largeBuf = new float[4096];
        mixer.GeneratePcmSamples(largeBuf, largeBuf.Length, 44100.0, 500000);

        for (int i = 0; i < largeBuf.Length; i++)
        {
            Assert.InRange(largeBuf[i], -1.0f, 1.0f);
        }
    }
}
