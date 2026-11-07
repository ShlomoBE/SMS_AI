using Sms.Core.Cartridge;
using Sms.Core.Memory;

namespace Sms.Core.Tests.Memory;

// Synthetic ROM/RAM only. Hardware basis: Charles MacDonald,
// IDocs/General.txt section 2, especially "Register mirroring".
public sealed class SegaMemoryBusTests
{
    [Fact]
    public void ConstructorsRejectNullMapper()
    {
        Assert.Throws<ArgumentNullException>(() => new SegaMemoryBus(null!, (byte)0));
        Assert.Throws<ArgumentNullException>(() => new SegaMemoryBus(null!, new byte[SegaMemoryBus.RamSize]));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(8191)]
    [InlineData(8193)]
    public void InitialImageMustBeExactlyEightKiB(int length)
    {
        Assert.Throws<ArgumentException>(() => new SegaMemoryBus(CreateMapper(), new byte[length]));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(90)]
    [InlineData(255)]
    public void ExplicitFillInitializesBothRamWindows(int fill)
    {
        var bus = new SegaMemoryBus(CreateMapper(), (byte)fill);
        for (int address = 0xc000; address <= 0xffff; address++)
            Assert.Equal((byte)fill, bus.ReadMemory((ushort)address));
    }

    [Fact]
    public void InitialImageIsCopiedAndNotConfusedWithMapperLatches()
    {
        var initialRam = new byte[SegaMemoryBus.RamSize];
        for (int i = 0; i < initialRam.Length; i++)
            initialRam[i] = (byte)(i ^ (i >> 8));
        var mapper = CreateMapper();
        var before = mapper.Registers;
        var bus = new SegaMemoryBus(mapper, initialRam);
        for (int i = 0; i < initialRam.Length; i++)
        {
            Assert.Equal(initialRam[i], bus.ReadMemory((ushort)(0xc000 + i)));
            Assert.Equal(initialRam[i], bus.ReadMemory((ushort)(0xe000 + i)));
        }
        byte original = initialRam[0];
        initialRam[0] ^= 0xff;
        Assert.Equal(original, bus.ReadMemory(0xc000));
        bus.WriteMemory(0xc001, 0x55);
        Assert.Equal((byte)1, initialRam[1]);
        Assert.Equal(before, mapper.Registers);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ConstructionPreservesExistingMapperState(bool useImage)
    {
        var mapper = CreateMapper();
        mapper.ObserveWrite(0xfffc, 0x64);
        mapper.ObserveWrite(0xfffd, 3);
        mapper.ObserveWrite(0xfffe, 2);
        mapper.ObserveWrite(0xffff, 1);
        var before = mapper.Registers;
        var bus = useImage ? new SegaMemoryBus(mapper, new byte[SegaMemoryBus.RamSize]) : new SegaMemoryBus(mapper, (byte)0);
        Assert.Equal(before, mapper.Registers);
        Assert.Equal(RomValue(3, 0x400), bus.ReadMemory(0x0400));
        Assert.Equal(RomValue(2, 0), bus.ReadMemory(0x4000));
        Assert.Equal(RomValue(1, 0), bus.ReadMemory(0x8000));
    }

    [Theory]
    [InlineData(0x0000, 0x00)]
    [InlineData(0x03ff, 0xfc)]
    [InlineData(0x0400, 0x04)]
    [InlineData(0x3fff, 0xc0)]
    [InlineData(0x4000, 0x25)]
    [InlineData(0x7fff, 0xe5)]
    [InlineData(0x8000, 0x4a)]
    [InlineData(0xbfff, 0x8a)]
    public void CartridgeBoundaryReadsMatchSyntheticRom(int address, int expected)
    {
        var bus = new SegaMemoryBus(CreateMapper(), (byte)0x71);
        Assert.Equal((byte)expected, bus.ReadMemory((ushort)address));
        Assert.Equal((byte)0x71, bus.ReadMemory(0xc000));
    }

    [Fact]
    public void EveryRamByteMirrorsInBothDirectionsWithoutSelectingMapperThroughLowerWindow()
    {
        var mapper = CreateMapper();
        var before = mapper.Registers;
        var bus = new SegaMemoryBus(mapper, (byte)0);
        for (int i = 0; i < SegaMemoryBus.RamSize; i++)
        {
            byte value = (byte)(i ^ (i >> 8));
            Assert.Equal(MapperWriteResult.Ignored, bus.WriteMemory((ushort)(0xc000 + i), value));
            Assert.Equal(value, bus.ReadMemory((ushort)(0xe000 + i)));
        }
        Assert.Equal(before, mapper.Registers);

        // Exclude only the four actual mapper addresses from this direction.
        for (int i = 0; i < SegaMemoryBus.RamSize - 4; i++)
        {
            byte value = (byte)~(i ^ (i >> 8));
            Assert.Equal(MapperWriteResult.Ignored, bus.WriteMemory((ushort)(0xe000 + i), value));
            Assert.Equal(value, bus.ReadMemory((ushort)(0xc000 + i)));
        }
        Assert.Equal(before, mapper.Registers);
    }

    [Theory]
    [InlineData(0xfffc, 0x04)]
    [InlineData(0xfffc, 0x20)]
    [InlineData(0xfffc, 0x40)]
    [InlineData(0xfffc, 0x64)]
    [InlineData(0xfffd, 0x03)]
    [InlineData(0xfffe, 0x03)]
    [InlineData(0xffff, 0x03)]
    public void ExactRegisterWritesReachBothRamAndSelectedMapperLatch(int address, int value)
    {
        var mapper = CreateMapper();
        var bus = new SegaMemoryBus(mapper, (byte)0xa5);
        Assert.Equal(MapperWriteResult.RegisterUpdated, bus.WriteMemory((ushort)address, (byte)value));
        Assert.Equal((byte)value, bus.ReadMemory((ushort)address));
        Assert.Equal((byte)value, bus.ReadMemory((ushort)(address - 0x2000)));
        var expected = address switch
        {
            0xfffc => new SegaMapperRegisters((byte)value, 0, 1, 2),
            0xfffd => new SegaMapperRegisters(0, (byte)value, 1, 2),
            0xfffe => new SegaMapperRegisters(0, 0, (byte)value, 2),
            _ => new SegaMapperRegisters(0, 0, 1, (byte)value)
        };
        Assert.Equal(expected, mapper.Registers);

        // Lower RAM alias changes readback, not the write-only mapper latch.
        Assert.Equal(MapperWriteResult.Ignored, bus.WriteMemory((ushort)(address - 0x2000), 0x7e));
        Assert.Equal((byte)0x7e, bus.ReadMemory((ushort)address));
        Assert.Equal(expected, mapper.Registers);
    }

    [Theory]
    [InlineData(0x01)]
    [InlineData(0x02)]
    [InlineData(0x08)]
    [InlineData(0x10)]
    [InlineData(0x80)]
    [InlineData(0xff)]
    public void UnsupportedControlIsSurfacedAfterRamReceivesWrite(int control)
    {
        var mapper = CreateMapper();
        var bus = new SegaMemoryBus(mapper, (byte)0xa5);
        bus.WriteMemory(0xfffc, 0x04);
        var before = mapper.Registers;
        byte romBefore = bus.ReadMemory(0x8000);
        Assert.Equal(MapperWriteResult.UnsupportedControlValue, bus.WriteMemory(0xfffc, (byte)control));
        Assert.Equal((byte)control, bus.ReadMemory(0xfffc));
        Assert.Equal((byte)control, bus.ReadMemory(0xdffc));
        Assert.Equal(before, mapper.Registers);
        Assert.Equal(romBefore, bus.ReadMemory(0x8000));
    }

    [Fact]
    public void AllNonRegisterWritesLeaveMapperUnchangedAndRomReadOnly()
    {
        var mapper = CreateMapper();
        var before = mapper.Registers;
        var bus = new SegaMemoryBus(mapper, (byte)0);
        for (int address = 0; address < 0xfffc; address++)
        {
            byte romBefore = address < 0xc000 ? bus.ReadMemory((ushort)address) : (byte)0;
            Assert.Equal(MapperWriteResult.Ignored, bus.WriteMemory((ushort)address, 0xff));
            if (address < 0xc000)
                Assert.Equal(romBefore, bus.ReadMemory((ushort)address));
        }
        Assert.Equal(before, mapper.Registers);
    }

    [Fact]
    public void BankWritesAffectInjectedMapperAndReadsButKeepFirstKiBFixed()
    {
        var mapper = CreateMapper();
        var bus = new SegaMemoryBus(mapper, (byte)0);
        byte fixedBefore = bus.ReadMemory(0x03ff);
        foreach (ushort address in new ushort[] { 0xfffd, 0xfffe, 0xffff })
            Assert.Equal(MapperWriteResult.RegisterUpdated, bus.WriteMemory(address, 3));
        Assert.Equal(fixedBefore, bus.ReadMemory(0x03ff));
        foreach (ushort address in new ushort[] { 0x0400, 0x3fff, 0x4000, 0x7fff, 0x8000, 0xbfff })
        {
            int offset = address & 0x3fff;
            Assert.Equal(RomValue(3, offset), bus.ReadMemory(address));
        }
        Assert.Equal(new SegaMapperRegisters(0, 3, 3, 3), mapper.Registers);
    }

    private static byte RomValue(int bank, int offset) => (byte)((bank * 0x25) ^ offset ^ (offset >> 8));

    [Fact]
    public void SramBusRoutesEntireOverlayAndMirrorsWithoutChangingWorkRam()
    {
        var mapper = CreateMapper(SegaMapperProfile.Sega3155235Sram8KiB);
        var bus = new SegaMemoryBus(mapper, (byte)0x71);
        Assert.Equal(MapperWriteResult.Ignored, bus.WriteMemory(0x8000, 0xff));
        Assert.Equal(MapperWriteResult.RegisterUpdated, bus.WriteMemory(0xfffc, 8));
        for (int offset = 0; offset < 0x2000; offset++)
        {
            byte value = (byte)(offset ^ (offset >> 8));
            Assert.Equal(MapperWriteResult.CartridgeRamWritten, bus.WriteMemory((ushort)(0x8000 + offset), value));
            Assert.Equal(value, bus.ReadMemory((ushort)(0xa000 + offset)));
            Assert.Equal(MapperWriteResult.CartridgeRamWritten, bus.WriteMemory((ushort)(0xa000 + offset), (byte)~value));
            Assert.Equal((byte)~value, bus.ReadMemory((ushort)(0x8000 + offset)));
        }
        Assert.Equal(MapperWriteResult.Ignored, bus.WriteMemory(0x7fff, 0xff));
        Assert.Equal(RomValue(1, 0x3fff), bus.ReadMemory(0x7fff));
        Assert.Equal((byte)0x71, bus.ReadMemory(0xc000));
        Assert.Equal(MapperWriteResult.Ignored, bus.WriteMemory(0xc000, 0x33));
        Assert.Equal((byte)0x33, bus.ReadMemory(0xe000));
        Assert.Equal((byte)0xff, bus.ReadMemory(0xa000));

        Assert.Equal(MapperWriteResult.RegisterUpdated, bus.WriteMemory(0xffff, 3));
        Assert.Equal((byte)3, bus.ReadMemory(0xdfff));
        Assert.Equal((byte)3, bus.ReadMemory(0xffff));
        bus.WriteMemory(0xfffc, 0x0c);
        Assert.Equal((byte)0xff, bus.ReadMemory(0x8000));
        bus.WriteMemory(0xfffc, 0);
        Assert.Equal(RomValue(3, 0), bus.ReadMemory(0x8000));
        Assert.Equal(MapperWriteResult.Ignored, bus.WriteMemory(0xa000, 0));
        bus.WriteMemory(0xfffc, 8);
        Assert.Equal((byte)0xff, bus.ReadMemory(0x8000));
    }

    [Fact]
    public void SramBusAllControlValuesPreserveFanOutAndRamMirrorsNeverSelectLatches()
    {
        var mapper = CreateMapper(SegaMapperProfile.Sega3155235Sram8KiB);
        var bus = new SegaMemoryBus(mapper, (byte)0x71);
        for (int control = 0; control <= byte.MaxValue; control++)
        {
            var before = mapper.Registers;
            bool supported = (control & 0x93) == 0;
            Assert.Equal(supported ? MapperWriteResult.RegisterUpdated : MapperWriteResult.UnsupportedControlValue,
                bus.WriteMemory(0xfffc, (byte)control));
            Assert.Equal((byte)control, bus.ReadMemory(0xfffc));
            Assert.Equal((byte)control, bus.ReadMemory(0xdffc));
            Assert.Equal(supported ? before with { Control = (byte)control } : before, mapper.Registers);
            var after = mapper.Registers;
            Assert.Equal(MapperWriteResult.Ignored, bus.WriteMemory(0xdffc, 0xaa));
            Assert.Equal((byte)0xaa, bus.ReadMemory(0xfffc));
            Assert.Equal(after, mapper.Registers);
        }
        bus.WriteMemory(0xfffc, 8);
        byte[] values = [8, 3, 2, 1];
        for (int register = 0; register < 4; register++)
        {
            Assert.Equal(MapperWriteResult.RegisterUpdated, bus.WriteMemory((ushort)(0xfffc + register), values[register]));
            Assert.Equal(values[register], bus.ReadMemory((ushort)(0xdffc + register)));
            Assert.Equal(MapperWriteResult.Ignored, bus.WriteMemory((ushort)(0xdffc + register), 0xaa));
            Assert.Equal((byte)0xaa, bus.ReadMemory((ushort)(0xfffc + register)));
        }
        Assert.Equal(new SegaMapperRegisters(8, 3, 2, 1), mapper.Registers);
        Assert.All(mapper.ExportSram(), value => Assert.Equal((byte)0x5a, value));
    }

    private static SegaMapper CreateMapper(SegaMapperProfile profile = SegaMapperProfile.Sega3155235RomOnly)
    {
        var rom = new byte[4 * SegaMapper.BankSize];
        for (int bank = 0; bank < 4; bank++)
            for (int offset = 0; offset < SegaMapper.BankSize; offset++)
                rom[bank * SegaMapper.BankSize + offset] = RomValue(bank, offset);
        return profile == SegaMapperProfile.Sega3155235RomOnly
            ? new SegaMapper(rom, profile)
            : new SegaMapper(rom, profile, (byte)0x5a);
    }
}
