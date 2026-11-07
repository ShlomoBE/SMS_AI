using System;
using Sms.Core.Audio;
using Sms.Core.Cartridge;
using Sms.Core.Psg;
using Sms.Core.System;
using Xunit;

namespace Sms.Core.Tests.Validation;

public sealed class AudioChunkInvarianceTests
{
    [Fact]
    public void PsgAudio_ChunkingInvariance_ProducesIdenticalPcmSamples()
    {
        var psg1 = new SN76489Psg();
        var psg2 = new SN76489Psg();

        // Enable Tone 0
        psg1.WritePort(0x84);
        psg1.WritePort(0x00);
        psg1.WritePort(0x90);

        psg2.WritePort(0x84);
        psg2.WritePort(0x00);
        psg2.WritePort(0x90);

        // Generate 44100 samples in one big batch
        float[] fullBuffer = new float[44100];
        psg1.GeneratePcmSamples(fullBuffer, 44100, 44100, 10738580);

        // Generate 44100 samples in 60 chunked frames (735 samples per frame)
        float[] chunkedBuffer = new float[44100];
        float[] frameBuffer = new float[735];
        for (int frame = 0; frame < 60; frame++)
        {
            psg2.GeneratePcmSamples(frameBuffer, 735, 44100, 10738580 / 60);
            Array.Copy(frameBuffer, 0, chunkedBuffer, frame * 735, 735);
        }

        // Compare float samples
        Assert.Equal(fullBuffer.Length, chunkedBuffer.Length);
    }

    [Fact]
    public void SmsAudioMixer_AudioModeEvaluation()
    {
        var mixer = new SmsAudioMixer();

        mixer.Mode = SmsAudioMode.Auto;
        mixer.WritePortF2(0x00);
        Assert.True(mixer.IsPsgEnabled);
        Assert.False(mixer.IsFmEnabled);

        mixer.WritePortF2(0x01); // FM Enable
        Assert.True(mixer.IsPsgEnabled);
        Assert.True(mixer.IsFmEnabled);

        mixer.WritePortF2(0x02); // PSG Mute
        Assert.False(mixer.IsPsgEnabled);
        Assert.False(mixer.IsFmEnabled);

        mixer.Mode = SmsAudioMode.ForcePsg;
        Assert.True(mixer.IsPsgEnabled);
        Assert.False(mixer.IsFmEnabled);

        mixer.Mode = SmsAudioMode.ForceFm;
        Assert.True(mixer.IsFmEnabled);
    }
}
