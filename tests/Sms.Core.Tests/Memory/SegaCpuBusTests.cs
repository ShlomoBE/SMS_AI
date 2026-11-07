using Sms.Core.Cartridge;
using Sms.Core.Cpu;
using Sms.Core.Memory;

namespace Sms.Core.Tests.Memory;

public sealed class SegaCpuBusTests
{
    [Fact]
    public void CpuCanExecuteSyntheticProgramAndWriteMirroredRam()
    {
        byte[] rom = new byte[SegaMapper.BankSize];
        // LD HL,$C123; LD A,$5A; LD (HL),A; NOP.
        byte[] program = [0x21, 0x23, 0xc1, 0x3e, 0x5a, 0x77, 0x00];
        program.CopyTo(rom, 0);
        var memory = new SegaMemoryBus(new SegaMapper(rom, SegaMapperProfile.Sega3155235RomOnly), 0);
        var cpu = new Z80(new SegaCpuBus(memory));

        Assert.Equal(10, cpu.Step());
        Assert.Equal(7, cpu.Step());
        Assert.Equal(7, cpu.Step());
        Assert.Equal(4, cpu.Step());
        Assert.Equal(0x5a, memory.ReadMemory(0xc123));
        Assert.Equal(0x5a, memory.ReadMemory(0xe123));
        Assert.Equal(7, cpu.ProgramCounter);
        Assert.Equal(4, cpu.Refresh);
    }

    [Fact]
    public void UnsupportedMapperControlThrowsAfterRamSideEffect()
    {
        var mapper = new SegaMapper(new byte[SegaMapper.BankSize], SegaMapperProfile.Sega3155235RomOnly);
        var memory = new SegaMemoryBus(mapper, 0);
        var bus = new SegaCpuBus(memory);
        Assert.Throws<NotSupportedException>(() => bus.WriteMemory(0xfffc, 0x08));
        Assert.Equal(0x08, memory.ReadMemory(0xfffc));
        Assert.Equal(0, mapper.Registers.Control);
    }

    [Fact]
    public void CpuFaultsAfterUnsupportedMapperWriteWithoutUndoingRam()
    {
        byte[] rom = new byte[SegaMapper.BankSize];
        byte[] program = [0x21, 0xfc, 0xff, 0x3e, 0x08, 0x77];
        program.CopyTo(rom, 0);
        var mapper = new SegaMapper(rom, SegaMapperProfile.Sega3155235RomOnly);
        var memory = new SegaMemoryBus(mapper, 0);
        var cpu = new Z80(new SegaCpuBus(memory));
        cpu.Step();
        cpu.Step();

        Assert.Throws<NotSupportedException>(() => cpu.Step());
        Assert.True(cpu.IsFaulted);
        Assert.Equal(0x08, memory.ReadMemory(0xdffc));
        Assert.Equal(0, mapper.Registers.Control);
        Assert.Throws<InvalidOperationException>(() => cpu.Step());
    }

    [Fact]
    public void SuccessfulBankWriteChangesOpcodeFetchAndMirrorsRegisterWrite()
    {
        byte[] rom = new byte[4 * SegaMapper.BankSize];
        rom[3 * SegaMapper.BankSize] = 0x76;
        var memory = new SegaMemoryBus(new SegaMapper(rom, SegaMapperProfile.Sega3155235RomOnly), 0);
        var bus = new SegaCpuBus(memory);

        bus.WriteMemory(0xfffe, 3);

        Assert.Equal(3, bus.ReadMemory(0xdffe));
        Assert.Equal(0x76, bus.FetchOpcode(0x4000));
    }

    [Fact]
    public void SramProfileCanBeSelectedWrittenAndReadThroughAdapter()
    {
        var mapper = new SegaMapper(new byte[SegaMapper.BankSize], SegaMapperProfile.Sega3155235Sram8KiB, (byte)0);
        var memory = new SegaMemoryBus(mapper, 0);
        var bus = new SegaCpuBus(memory);

        bus.WriteMemory(0xfffc, 8);
        bus.WriteMemory(0x8000, 0xa5);

        Assert.Equal(8, bus.ReadMemory(0xdffc));
        Assert.Equal(0xa5, bus.ReadMemory(0xa000));
        Assert.Equal(0xa5, bus.FetchOpcode(0x8000));
    }

    [Fact]
    public void UnimplementedDeviceOperationsFailExplicitly()
    {
        var memory = new SegaMemoryBus(new SegaMapper(new byte[SegaMapper.BankSize], SegaMapperProfile.Sega3155235RomOnly), 0);
        var bus = new SegaCpuBus(memory);
        Assert.Throws<ArgumentNullException>(() => new SegaCpuBus(null!));
        Assert.Throws<NotSupportedException>(() => bus.ReadIo(0x1234));
        Assert.Throws<NotSupportedException>(() => bus.WriteIo(0x1234, 0x56));
        Assert.Throws<NotSupportedException>(() => bus.AcknowledgeInterrupt());
    }
}
