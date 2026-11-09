using Sms.Core.Fm;
using Xunit;

namespace Sms.Core.Tests.Fm;

public class YM2413TimingTests
{
    private static YM2413Fm Tone(bool enabled)
    {
        var fm = new YM2413Fm();
        fm.WriteAudioControl(enabled ? (byte)1 : (byte)0);
        void Write(byte address, byte data) { fm.WritePortAddress(address); fm.WritePortData(data); }
        Write(0x30, 0x10); Write(0x10, 0x80); Write(0x20, 0x19);
        return fm;
    }

    [Fact]
    public void DividedAdvancesProduceSameSamples()
    {
        var a = Tone(true); var b = Tone(true);
        var whole = new float[1000]; var first = new float[400]; var second = new float[600];
        a.GeneratePcmSamples(whole, 1000, 44100, 216000);
        b.GeneratePcmSamples(first, 400, 44100, 86400);
        b.GeneratePcmSamples(second, 600, 44100, 129600);
        Assert.Equal(whole, first.Concat(second).ToArray());
    }

    [Fact]
    public void MutingDoesNotStopChipClock()
    {
        var a = Tone(true); var b = Tone(false);
        a.GeneratePcmSamples(new float[100], 100, 44100, 21600);
        var muted = new float[100];
        b.GeneratePcmSamples(muted, 100, 44100, 21600);
        Assert.All(muted, sample => Assert.Equal(0, sample));
        b.WriteAudioControl(1);
        var x = new float[100]; var y = new float[100];
        a.GeneratePcmSamples(x, 100, 44100, 21600);
        b.GeneratePcmSamples(y, 100, 44100, 21600);
        Assert.Equal(x, y);
    }
}
