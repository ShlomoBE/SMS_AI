using System.Reflection;
using Sms.Core.Fm;
using Xunit;

namespace Sms.Core.Tests.Fm;

// emu2413 a1d5120903b75f93d3eb7690e34a29c0b4add903, OPLL_new(3600000, 50000),
// OPLL_writeReg followed by OPLL_calc. Native rate disables upstream SRC.
public class YM2413ReferenceTests
{
    private static void Write(YM2413Fm fm, byte address, byte data)
    { fm.WritePortAddress(address); fm.WritePortData(data); }

    private static short[] Native(YM2413Fm fm, int count)
    {
        var calc = typeof(YM2413Fm).GetMethod("CalcOneSample", BindingFlags.Instance | BindingFlags.NonPublic)!;
        return Enumerable.Range(0, count).Select(_ => (short)calc.Invoke(fm, null)!).ToArray();
    }

    [Fact]
    public void ViolinKeyOn_FirstNativeSamplesMatchUpstream()
    {
        var fm = new YM2413Fm();
        Write(fm, 0x30, 0x10); Write(fm, 0x10, 0xac); Write(fm, 0x20, 0x19);
        Assert.Equal(new short[32], Native(fm, 32));
    }

    [Fact]
    public void RhythmKeyOn_FirstNativeSamplesMatchUpstream()
    {
        var fm = new YM2413Fm();
        Write(fm, 0x36, 0); Write(fm, 0x37, 0); Write(fm, 0x38, 0);
        Write(fm, 0x16, 0xac); Write(fm, 0x17, 0x80); Write(fm, 0x18, 0x60);
        Write(fm, 0x26, 0x09); Write(fm, 0x27, 0x09); Write(fm, 0x28, 0x09); Write(fm, 0x0e, 0x3f);
        short[] expected = { 0,1702,3250,4620,5740,6672,7470,7514,7002,5898,4836,1744,-3852,-1782,-6136,-1566,-1172,2080,1188,6050,-788,5778,-3096,3230,3118,2606,1684,520,-7086,-2182,-9938,-4252 };
        Assert.Equal(expected, Native(fm, expected.Length));
    }

    [Fact]
    public void CustomPatch_KeyOnAndReleaseMatchUpstream()
    {
        var fm = new YM2413Fm();
        byte[] patch = { 0x21, 0x21, 0x3f, 0, 0xf1, 0xf1, 0xf1, 0xf1 };
        for (byte i = 0; i < patch.Length; i++) Write(fm, i, patch[i]);
        Write(fm, 0x30, 0); Write(fm, 0x10, 0x20); Write(fm, 0x20, 0x19);
        short[] attack = { 0,-119,-231,-343,-478,-586,-718,-822,-946,-1044,-1160,-1251,-1339,-1440,-1516,-1605,-1672,-1736,-1803,-1853,-1898,-1935,-1972,-2004,-2020,-2037,-2042,-2042,-2037,-2026,-2004,-1983 };
        Assert.Equal(attack, Native(fm, 32));
        Native(fm, 224);
        Write(fm, 0x20, 0x09);
        short[] release = { -2031,-2015,-1999,-1972,-1935,-1898,-1853,-1803,-1750,-1704,-1636,-1566,-1492,-1433,-1350,-1261 };
        Assert.Equal(release, Native(fm, release.Length));
    }
}
