using Sms.Core.Vdp;

namespace Sms.Core.Tests.Vdp;

// IDocs/VDP.txt §§4, 12: flags are latched and interrupt enables gate the level.
// Collision bit 5 follows §4; §10 contradicts it and needs hardware verification.
public sealed class VdpInterruptTests
{
    private static VdpPorts Fixture(byte status = 0, bool line = false, byte r0 = 0, byte r1 = 0,
        bool secondControlByte = false)
    {
        var registers = new byte[11];
        registers[0] = r0;
        registers[1] = r1;
        return VdpPorts.CreateDeterministicFixture(new byte[0x4000], new byte[32], registers,
            0x1234, 1, secondControlByte, 0xa5, status, line);
    }

    private static void WriteRegister(VdpPorts vdp, byte index, byte value)
    {
        vdp.WriteControl(value);
        vdp.WriteControl((byte)(0x80 | index));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PendingBeforeEnableSurvivesDisableAndCanBeReenabled(bool line)
    {
        var vdp = Fixture(status: 0x13);
        if (line) vdp.InjectLineInterrupt(); else vdp.InjectFrameInterrupt();
        Assert.False(vdp.IrqAsserted);
        Assert.Equal(line ? 0x13 : 0x93, vdp.Inspect().Status);
        Assert.Equal(line, vdp.Inspect().LinePending);

        byte register = line ? (byte)0 : (byte)1;
        byte enable = line ? (byte)0x10 : (byte)0x20;
        WriteRegister(vdp, register, enable);
        Assert.True(vdp.IrqAsserted);
        WriteRegister(vdp, register, 0);
        Assert.False(vdp.IrqAsserted);
        WriteRegister(vdp, register, enable);
        Assert.True(vdp.IrqAsserted);
        Assert.Equal(line ? 0x13 : 0x93, vdp.ReadStatus());
        Assert.False(vdp.IrqAsserted);
    }

    [Fact]
    public void CombinedSourcesAreIndependentlyGatedAndStatusReadClearsBoth()
    {
        var vdp = Fixture(r0: 0x10, r1: 0x20);
        vdp.InjectFrameInterrupt();
        vdp.InjectLineInterrupt();
        Assert.True(vdp.IrqAsserted);
        WriteRegister(vdp, 1, 0);
        Assert.True(vdp.IrqAsserted);
        WriteRegister(vdp, 0, 0);
        Assert.False(vdp.IrqAsserted);
        WriteRegister(vdp, 1, 0x20);
        Assert.True(vdp.IrqAsserted);
        Assert.Equal(0x80, vdp.ReadStatus());
        Assert.False(vdp.IrqAsserted);
        WriteRegister(vdp, 0, 0x10);
        Assert.False(vdp.IrqAsserted);
        Assert.False(vdp.Inspect().LinePending);
    }

    [Fact]
    public void SpriteEventsLatchTheirOwnBitsButDoNotRaiseIrq()
    {
        var vdp = Fixture(status: 0x15, r0: 0x10, r1: 0x20);
        vdp.InjectSpriteOverflow();
        vdp.InjectSpriteOverflow();
        Assert.Equal(0x55, vdp.Inspect().Status);
        Assert.False(vdp.IrqAsserted);
        vdp.InjectSpriteCollision();
        vdp.InjectSpriteCollision();
        Assert.Equal(0x75, vdp.Inspect().Status);
        Assert.False(vdp.IrqAsserted);
        Assert.Equal(0x75, vdp.ReadStatus());
        Assert.Equal(0x15, vdp.ReadStatus());
    }

    [Fact]
    public void SeededStateImmediatelyDrivesLevelAndStatusReadCancelsCommand()
    {
        var vdp = Fixture(status: 0xfb, line: true, r0: 0x10, r1: 0x20,
            secondControlByte: true);
        Assert.True(vdp.IrqAsserted);
        Assert.True(vdp.Inspect().SecondControlByte);
        Assert.Equal(0xfb, vdp.ReadStatus());
        var after = vdp.Inspect();
        Assert.Equal(0x1b, after.Status);
        Assert.False(after.LinePending);
        Assert.False(after.SecondControlByte);
        Assert.Equal(0x1234, after.Address);
        Assert.Equal(0xa5, after.ReadBuffer);
        Assert.False(vdp.IrqAsserted);
        vdp.WriteControl(0x66);
        Assert.True(vdp.Inspect().SecondControlByte);
        Assert.Equal(0x1266, vdp.Inspect().Address);
    }

    [Fact]
    public void StatusReadClearsDisabledPendingAndLaterEnableCannotReviveIt()
    {
        var vdp = Fixture(status: 0x80, line: true);
        Assert.False(vdp.IrqAsserted);
        Assert.Equal(0x80, vdp.ReadStatus());
        WriteRegister(vdp, 0, 0x10);
        WriteRegister(vdp, 1, 0x20);
        Assert.False(vdp.IrqAsserted);
    }

    [Theory]
    [InlineData(0x80, false, 0, 0x20, true)]
    [InlineData(0x80, false, 0x10, 0, false)]
    [InlineData(0, true, 0x10, 0, true)]
    [InlineData(0, true, 0, 0x20, false)]
    public void SeededPendingUsesOnlyItsOwnEnable(byte status, bool line, byte r0, byte r1, bool expected)
    {
        var vdp = Fixture(status, line, r0, r1);
        Assert.Equal(expected, vdp.IrqAsserted);
        vdp.ReadData();
        vdp.WriteData(0x42);
        WriteRegister(vdp, 2, 0xff);
        Assert.Equal(expected, vdp.IrqAsserted);
        Assert.Equal(status, vdp.Inspect().Status);
        Assert.Equal(line, vdp.Inspect().LinePending);
    }

    [Fact]
    public void EveryPendingAndEnableCombinationUsesIndependentLevelGates()
    {
        foreach (bool frame in new[] { false, true })
        foreach (bool line in new[] { false, true })
        foreach (bool frameEnabled in new[] { false, true })
        foreach (bool lineEnabled in new[] { false, true })
        {
            var vdp = Fixture(status: frame ? (byte)0x80 : (byte)0,
                line: line, r0: lineEnabled ? (byte)0x10 : (byte)0,
                r1: frameEnabled ? (byte)0x20 : (byte)0);
            Assert.Equal((frame && frameEnabled) || (line && lineEnabled), vdp.IrqAsserted);
            vdp.ReadStatus();
            Assert.False(vdp.IrqAsserted);
            vdp.InjectFrameInterrupt();
            vdp.InjectLineInterrupt();
            Assert.Equal(frameEnabled || lineEnabled, vdp.IrqAsserted);
        }
    }
}
