using Sms.Core.Vdp;

namespace Sms.Core.Tests.Vdp;

public sealed class VdpPortsTests
{
    private static VdpPorts Fixture(int address = 0x1234, int code = 1, bool pending = false,
        byte buffer = 0xa5, byte status = 0xf7, bool line = true)
    {
        var vram = new byte[0x4000];
        vram[0] = 0x17;
        vram[0x3fff] = 0x29;
        vram[0x1234] = 0x38;
        vram[0x1235] = 0x49;
        vram[0x1256] = 0x6b;
        return VdpPorts.CreateDeterministicFixture(vram, new byte[32], new byte[11],
            address, code, pending, buffer, status, line);
    }

    private static void Command(VdpPorts vdp, int address, int code)
    {
        vdp.WriteControl((byte)address);
        vdp.WriteControl((byte)((code << 6) | (address >> 8)));
    }

    [Fact]
    public void FixtureOwnsCopiesAndSnapshotIsDetachedAndReadOnly()
    {
        var vram = new byte[0x4000];
        var cram = new byte[32];
        var registers = new byte[11];
        var vdp = VdpPorts.CreateDeterministicFixture(vram, cram, registers, 0, 1, false, 5, 0, false);
        var before = vdp.Inspect();
        vram[0] = 1; cram[0] = 2; registers[0] = 3;
        Assert.Equal(0, vdp.Inspect().Vram[0]);
        Assert.Equal(0, vdp.Inspect().Cram[0]);
        Assert.Equal(0, vdp.Inspect().Registers[0]);
        vdp.WriteData(0xff);
        Assert.Equal(0, before.Vram[0]);
        Assert.Throws<NotSupportedException>(() => ((IList<byte>)before.Vram)[0] = 1);
        Assert.Throws<NotSupportedException>(() => ((IList<byte>)before.Cram)[0] = 1);
        Assert.Throws<NotSupportedException>(() => ((IList<byte>)before.Registers)[0] = 1);
    }

    [Fact]
    public void FixtureRejectsInvalidSizesAndState()
    {
        var v = new byte[0x4000]; var c = new byte[32]; var r = new byte[11];
        Assert.Throws<ArgumentException>(() => VdpPorts.CreateDeterministicFixture(new byte[1], c, r, 0, 0, false, 0, 0, false));
        Assert.Throws<ArgumentException>(() => VdpPorts.CreateDeterministicFixture(v, new byte[1], r, 0, 0, false, 0, 0, false));
        Assert.Throws<ArgumentException>(() => VdpPorts.CreateDeterministicFixture(v, c, new byte[1], 0, 0, false, 0, 0, false));
        Assert.Throws<ArgumentOutOfRangeException>(() => VdpPorts.CreateDeterministicFixture(v, c, r, 0x4000, 0, false, 0, 0, false));
        Assert.Throws<ArgumentOutOfRangeException>(() => VdpPorts.CreateDeterministicFixture(v, c, r, 0, 4, false, 0, 0, false));
        Assert.Throws<ArgumentOutOfRangeException>(() => VdpPorts.CreateDeterministicFixture(v, c, r, -1, 0, false, 0, 0, false));
        Assert.Throws<ArgumentOutOfRangeException>(() => VdpPorts.CreateDeterministicFixture(v, c, r, 0, -1, false, 0, 0, false));
        Assert.Throws<ArgumentNullException>(() => VdpPorts.CreateDeterministicFixture(null!, c, r, 0, 0, false, 0, 0, false));
        Assert.Throws<ArgumentNullException>(() => VdpPorts.CreateDeterministicFixture(v, null!, r, 0, 0, false, 0, 0, false));
        Assert.Throws<ArgumentNullException>(() => VdpPorts.CreateDeterministicFixture(v, c, null!, 0, 0, false, 0, 0, false));
        c[0] = 0x40;
        Assert.Throws<ArgumentException>(() => VdpPorts.CreateDeterministicFixture(v, c, r, 0, 0, false, 0, 0, false));
    }

    [Fact]
    public void FirstControlWriteImmediatelyChangesLowAddressOnly()
    {
        var vdp = Fixture();
        vdp.WriteControl(0xfe);
        var state = vdp.Inspect();
        Assert.Equal(0x12fe, state.Address);
        Assert.Equal(1, state.Code);
        Assert.Equal(0xa5, state.ReadBuffer);
        Assert.True(state.SecondControlByte);
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)]
    public void CommandThenDataReadUsesBufferForEveryCode(int code)
    {
        var vdp = Fixture();
        Command(vdp, 0x1234, code);
        var command = vdp.Inspect();
        Assert.Equal(code, command.Code);
        Assert.False(command.SecondControlByte);
        Assert.Equal(code == 0 ? 0x1235 : 0x1234, command.Address);
        Assert.Equal(code == 0 ? 0x38 : 0xa5, command.ReadBuffer);
        Assert.Equal(code == 0 ? 0x38 : 0xa5, vdp.ReadData());
        Assert.Equal(code == 0 ? 0x49 : 0x38, vdp.Inspect().ReadBuffer);
        Assert.Equal(code == 0 ? 0x1236 : 0x1235, vdp.Inspect().Address);
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)]
    public void DataWriteUsesCodeAndCopiesFullByteToBuffer(int code)
    {
        var vdp = Fixture(0x3fff, code);
        vdp.WriteData(0xe6);
        var state = vdp.Inspect();
        Assert.Equal(0, state.Address);
        Assert.Equal(0xe6, state.ReadBuffer);
        Assert.Equal(code == 3 ? 0x29 : 0xe6, state.Vram[0x3fff]);
        Assert.Equal(code == 3 ? 0x26 : 0, state.Cram[31]);
        Assert.Equal(0xe6, vdp.ReadData());
        Assert.Equal(0x17, vdp.Inspect().ReadBuffer);
        Assert.Equal(1, vdp.Inspect().Address);
    }

    [Fact]
    public void PrefetchAndReadWrapIndependently()
    {
        var vdp = Fixture();
        Command(vdp, 0x3fff, 0);
        Assert.Equal(0, vdp.Inspect().Address);
        Assert.Equal(0x29, vdp.ReadData());
        Assert.Equal(0x17, vdp.ReadData());
        Assert.Equal(2, vdp.Inspect().Address);
        vdp = Fixture(0x3fff);
        Assert.Equal(0xa5, vdp.ReadData());
        Assert.Equal(0, vdp.Inspect().Address);
        Assert.Equal(0x29, vdp.ReadData());
    }

    [Fact]
    public void WriteAfterReadCommandUsesPostPrefetchAddressAcrossWrap()
    {
        var vdp = Fixture();
        Command(vdp, 0x3fff, 0);
        Assert.Equal(0, vdp.Inspect().Address);
        Assert.Equal(0x29, vdp.Inspect().ReadBuffer);
        vdp.WriteData(0xe7);
        var state = vdp.Inspect();
        Assert.Equal(0xe7, state.Vram[0]);
        Assert.Equal(0x29, state.Vram[0x3fff]);
        Assert.Equal(1, state.Address);
        Assert.Equal(0xe7, state.ReadBuffer);
    }

    [Fact]
    public void AllRegisterSelectorsAndIgnoredBitsStillUpdateCommandAddressAndCode()
    {
        for (int high = 0x80; high <= 0xbf; high++)
        {
            var vdp = Fixture();
            vdp.WriteControl(0x5a);
            vdp.WriteControl((byte)high);
            var s = vdp.Inspect();
            Assert.Equal(((high & 0x3f) << 8) | 0x5a, s.Address);
            Assert.Equal(2, s.Code);
            Assert.Equal(0xa5, s.ReadBuffer);
            for (int i = 0; i < 11; i++) Assert.Equal((high & 15) == i ? 0x5a : 0, s.Registers[i]);
            vdp.WriteData(0x9b);
            Assert.Equal(0x9b, vdp.Inspect().Vram[s.Address]);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(10)]
    [InlineData(11)]
    [InlineData(15)]
    public void RegisterCommandChangesOnlyValidSelectedRegister(int selector)
    {
        byte[] registers = Enumerable.Range(0, 11).Select(i => (byte)(0x90 + i)).ToArray();
        var vdp = VdpPorts.CreateDeterministicFixture(new byte[0x4000], new byte[32], registers, 0, 0, false, 0, 0, false);
        vdp.WriteControl(0x5a);
        vdp.WriteControl((byte)(0x80 | selector));
        var snapshot = vdp.Inspect();
        for (int i = 0; i < registers.Length; i++)
            Assert.Equal(i == selector ? (byte)0x5a : registers[i], snapshot.Registers[i]);
    }

    [Fact]
    public void CramAliasesEvery32BytesWithoutWrappingAddressEarly()
    {
        var vdp = Fixture(0x3fdf, 3);
        vdp.WriteData(0xff);
        Assert.Equal(0x3fe0, vdp.Inspect().Address);
        vdp.WriteData(0xc1);
        Assert.Equal(0x3f, vdp.Inspect().Cram[31]);
        Assert.Equal(1, vdp.Inspect().Cram[0]);
        Assert.Equal(0xc1, vdp.Inspect().ReadBuffer);
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)]
    public void DataOrStatusAccessCancelsPartialCommandButKeepsFirstByteEffects(int access)
    {
        var vdp = Fixture();
        vdp.WriteControl(0x56);
        if (access == 0) Assert.Equal(0xa5, vdp.ReadData());
        if (access == 1) vdp.WriteData(0x76);
        if (access == 2) Assert.Equal(0xf7, vdp.ReadStatus());
        var s = vdp.Inspect();
        Assert.False(s.SecondControlByte);
        Assert.Equal(access == 2 ? 0x1256 : 0x1257, s.Address);
        Assert.Equal(1, s.Code);
        if (access == 0) Assert.Equal(0x6b, s.ReadBuffer);
        if (access == 1) Assert.Equal(0x76, s.Vram[0x1256]);
        vdp.WriteControl(0x88);
        Assert.True(vdp.Inspect().SecondControlByte);
        Assert.Equal(0x1288, vdp.Inspect().Address);
        Assert.Equal(1, vdp.Inspect().Code);
    }

    [Fact]
    public void StatusReadClearsOnlySeededHighFlagsAndLinePending()
    {
        var vdp = Fixture(pending: true);
        Assert.Equal(0xf7, vdp.ReadStatus());
        var s = vdp.Inspect();
        Assert.Equal(0x17, s.Status);
        Assert.False(s.LinePending);
        Assert.False(s.SecondControlByte);
        Assert.Equal(0x1234, s.Address);
        Assert.Equal(0xa5, s.ReadBuffer);
        Assert.Equal(0x17, vdp.ReadStatus());
    }
}
