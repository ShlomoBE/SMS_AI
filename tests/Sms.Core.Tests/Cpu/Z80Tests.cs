using Sms.Core.Cpu;

namespace Sms.Core.Tests.Cpu;

public sealed class Z80Tests
{
    [Theory]
    [InlineData(0x0000, 0x00, 0x0001, 0x01)]
    [InlineData(0xFFFF, 0x7F, 0x0000, 0x00)]
    [InlineData(0x1234, 0xFF, 0x1235, 0x80)]
    public void NopAdvancesPcAndRefreshAndCostsFourTStates(
        ushort pc, byte refresh, ushort expectedPc, byte expectedRefresh)
    {
        var bus = new RecordingBus();
        var cpu = new Z80(bus) { ProgramCounter = pc, Refresh = refresh };

        int cycles = cpu.Step();

        Assert.Equal(4, cycles);
        Assert.Equal(expectedPc, cpu.ProgramCounter);
        Assert.Equal(expectedRefresh, cpu.Refresh);
        Assert.Equal(new[] { pc }, bus.ReadAddresses);
    }

    [Fact]
    public void UnsupportedOpcodeFailsExplicitlyWithoutAdvancingCpuState()
    {
        var bus = new RecordingBus();
        bus.Memory[0x1234] = 0xED;
        bus.Memory[0x1235] = 0x00;
        var cpu = new Z80(bus) { ProgramCounter = 0x1234, Refresh = 0x99 };

        var error = Assert.Throws<NotSupportedException>(() => cpu.Step());

        Assert.Contains("0x00", error.Message);
        Assert.Equal(0x1236, cpu.ProgramCounter);
        Assert.Equal(0x9B, cpu.Refresh);
        Assert.Equal(new ushort[] { 0x1234, 0x1235 }, bus.ReadAddresses);
        Assert.True(cpu.IsFaulted);
        Assert.Throws<InvalidOperationException>(() => cpu.Step());
        Assert.Equal(2, bus.ReadAddresses.Count);
    }

    [Fact]
    public void UnsupportedEdSubOpcodeFaultsWithoutRetryingFetch()
    {
        var bus = new RecordingBus();
        bus.Memory[0xFFFF] = 0xED;
        bus.Memory[0x0000] = 0x00;
        var cpu = new Z80(bus) { ProgramCounter = 0xFFFF };

        var error = Assert.Throws<NotSupportedException>(() => cpu.Step());
        Assert.Contains("0x00", error.Message);
        Assert.Throws<InvalidOperationException>(() => cpu.Step());
        Assert.Equal(new ushort[] { 0xFFFF, 0x0000 }, bus.ReadAddresses);
    }

    [Fact]
    public void FailedFetchFaultsCpuBeforeRetryCanRepeatBusSideEffects()
    {
        var bus = new RecordingBus { ThrowOnFetch = true };
        var cpu = new Z80(bus);

        Assert.Throws<InvalidOperationException>(() => cpu.Step());
        Assert.True(cpu.IsFaulted);
        Assert.Throws<InvalidOperationException>(() => cpu.Step());
        Assert.Single(bus.ReadAddresses);
    }

    [Fact]
    public void NopIncrementsLowSevenRefreshBitsForEveryRefreshValue()
    {
        for (int initial = 0; initial <= byte.MaxValue; initial++)
        {
            var bus = new RecordingBus();
            var cpu = new Z80(bus) { Refresh = (byte)initial };

            Assert.Equal(4, cpu.Step());

            int expectedLowBits = (initial & 0x7F) == 0x7F ? 0 : (initial & 0x7F) + 1;
            Assert.Equal((byte)((initial & 0x80) | expectedLowBits), cpu.Refresh);
            Assert.Equal(new ushort[] { 0 }, bus.ReadAddresses);
        }
    }

    [Fact]
    public void ConsecutiveNopsFetchAcrossAddressAndRefreshWrap()
    {
        var bus = new RecordingBus();
        var cpu = new Z80(bus) { ProgramCounter = 0xffff, Refresh = 0xfe };

        Assert.Equal(4, cpu.Step());
        Assert.Equal(4, cpu.Step());
        Assert.Equal(1, cpu.ProgramCounter);
        Assert.Equal(0x80, cpu.Refresh);
        Assert.Equal(new ushort[] { 0xffff, 0x0000 }, bus.ReadAddresses);
    }

    [Fact]
    public void NopPreservesAllFlagValues()
    {
        var bus = new RecordingBus();
        var cpu = new Z80(bus);
        for (int flags = 0; flags <= byte.MaxValue; flags++)
        {
            cpu.ProgramCounter = 0;
            cpu.F = (byte)flags;
            Assert.Equal(4, cpu.Step());
            Assert.Equal((byte)flags, cpu.F);
        }
    }

    [Fact]
    public void NullBusIsRejected() => Assert.Throws<ArgumentNullException>(() => new Z80(null!));

    private sealed class RecordingBus : IZ80Bus
    {
        public byte[] Memory { get; } = new byte[65536];
        public List<ushort> ReadAddresses { get; } = [];
        public bool ThrowOnFetch { get; init; }
        public byte FetchOpcode(ushort address)
        {
            ReadAddresses.Add(address);
            if (ThrowOnFetch) throw new InvalidOperationException("Fetch failed");
            return Memory[address];
        }
        public byte ReadMemory(ushort address) => throw new InvalidOperationException("Unexpected data read");
        public void WriteMemory(ushort address, byte value) => throw new InvalidOperationException("Unexpected memory write");
        public byte ReadIo(ushort port) => throw new InvalidOperationException("Unexpected I/O read");
        public void WriteIo(ushort port, byte value) => throw new InvalidOperationException("Unexpected I/O write");
        public byte AcknowledgeInterrupt() => throw new InvalidOperationException("Unexpected interrupt acknowledge");
    }
}
