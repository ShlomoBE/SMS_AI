using Sms.Core.Cartridge;

namespace Sms.Core.Tests.Cartridge;

public sealed class SegaMapperTests
{
    private const SegaMapperProfile Profile = SegaMapperProfile.Sega3155235RomOnly;

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(4)]
    [InlineData(8)]
    [InlineData(16)]
    [InlineData(32)]
    public void EveryAddressMatchesInitialMapAndDrivenWindow(int banks)
    {
        var mapper = Create(banks);
        Assert.Equal(banks * SegaMapper.BankSize, mapper.RomSize);
        Assert.Equal(Profile, mapper.Profile);
        Assert.Equal(new SegaMapperRegisters(0, 0, 1, 2), mapper.Registers);

        for (int address = 0; address <= ushort.MaxValue; address++)
        {
            bool driven = mapper.TryRead((ushort)address, out byte value);
            Assert.Equal(address < 0xc000, driven);
            if (driven)
            {
                int bank = (address / SegaMapper.BankSize) & (banks - 1);
                Assert.Equal(Pattern(bank, address & 0x3fff), value);
            }
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(4)]
    [InlineData(8)]
    [InlineData(16)]
    [InlineData(32)]
    public void EveryBankRegisterValueWrapsAndPreservesOtherSlots(int banks)
    {
        var mapper = Create(banks);
        int[] offsets = [0, 1, 0x3fe, 0x3ff, 0x400, 0x401, 0x1fff, 0x2000, 0x3ffe, 0x3fff];
        for (int slot = 0; slot < 3; slot++)
        {
            for (int selected = 0; selected <= byte.MaxValue; selected++)
            {
                mapper.ResetToPowerOnProfile();
                Assert.Equal(MapperWriteResult.RegisterUpdated,
                    mapper.ObserveWrite((ushort)(0xfffd + slot), (byte)selected));
                var expectedRegisters = slot switch
                {
                    0 => new SegaMapperRegisters(0, (byte)selected, 1, 2),
                    1 => new SegaMapperRegisters(0, 0, (byte)selected, 2),
                    _ => new SegaMapperRegisters(0, 0, 1, (byte)selected)
                };
                Assert.Equal(expectedRegisters, mapper.Registers);

                for (int readSlot = 0; readSlot < 3; readSlot++)
                {
                    foreach (int offset in offsets)
                    {
                        int address = readSlot * SegaMapper.BankSize + offset;
                        int bank = (readSlot == slot ? selected : readSlot) & (banks - 1);
                        if (address < 0x400)
                            bank = 0;
                        AssertRead(mapper, address, Pattern(bank, offset));
                    }
                }
            }
        }
    }

    [Fact]
    public void FixedFirstKilobyteAndLastFifteenKilobytesKeepExactOffsets()
    {
        var mapper = Create(32);
        mapper.ObserveWrite(0xfffd, 31);
        mapper.ObserveWrite(0xfffe, 30);
        mapper.ObserveWrite(0xffff, 29);
        for (int address = 0; address < 0xc000; address++)
        {
            int bank = address switch
            {
                < 0x400 => 0,
                < 0x4000 => 31,
                < 0x8000 => 30,
                _ => 29
            };
            AssertRead(mapper, address, Pattern(bank, address & 0x3fff));
        }
    }

    [Fact]
    public void OnlyExactRegisterAddressesObserveWritesAndRomCannotBeWritten()
    {
        var mapper = Create(4);
        for (int address = 0; address < 0xfffc; address++)
            Assert.Equal(MapperWriteResult.Ignored, mapper.ObserveWrite((ushort)address, 0xa5));

        Assert.Equal(new SegaMapperRegisters(0, 0, 1, 2), mapper.Registers);
        for (int address = 0; address < 0xc000; address++)
            AssertRead(mapper, address, Pattern(address / 0x4000, address & 0x3fff));
    }

    [Fact]
    public void RomInputIsAnImmutableCopyIncludingAfterBankSwitchAndReset()
    {
        byte[] source = SyntheticRom(4);
        var mapper = new SegaMapper(source, Profile);
        Array.Fill(source, (byte)0xff);
        mapper.ObserveWrite(0xfffd, 3);
        mapper.ObserveWrite(0xfffe, 3);
        mapper.ObserveWrite(0xffff, 3);
        for (int address = 0; address < 0xc000; address++)
            AssertRead(mapper, address, Pattern(address < 0x400 ? 0 : 3, address & 0x3fff));

        mapper.ResetToPowerOnProfile();
        for (int address = 0; address < 0xc000; address++)
            AssertRead(mapper, address, Pattern(address / 0x4000, address & 0x3fff));
    }

    [Fact]
    public void PowerOnProfileResetRestoresAllLatchesAndIsRepeatable()
    {
        var mapper = Create(4);
        for (int repetition = 0; repetition < 3; repetition++)
        {
            mapper.ObserveWrite(0xfffc, 0x64);
            mapper.ObserveWrite(0xfffd, 3);
            mapper.ObserveWrite(0xfffe, 2);
            mapper.ObserveWrite(0xffff, 1);
            mapper.ResetToPowerOnProfile();
            Assert.Equal(new SegaMapperRegisters(0, 0, 1, 2), mapper.Registers);
            AssertRead(mapper, 0x400, Pattern(0, 0x400));
            AssertRead(mapper, 0x4000, Pattern(1, 0));
            AssertRead(mapper, 0x8000, Pattern(2, 0));
        }
    }

    [Fact]
    public void EveryControlValueEitherLatchesInertBitsOrExplicitlyReportsDeferredModes()
    {
        var mapper = Create(4);
        mapper.ObserveWrite(0xfffd, 3);
        byte[] allowed = [0x00, 0x04, 0x20, 0x24, 0x40, 0x44, 0x60, 0x64];
        for (int value = 0; value <= byte.MaxValue; value++)
        {
            SegaMapperRegisters before = mapper.Registers;
            MapperWriteResult result = mapper.ObserveWrite(0xfffc, (byte)value);
            if (!allowed.Contains((byte)value))
            {
                Assert.Equal(MapperWriteResult.UnsupportedControlValue, result);
                Assert.Equal(before, mapper.Registers);
            }
            else
            {
                Assert.Equal(MapperWriteResult.RegisterUpdated, result);
                Assert.Equal(before with { Control = (byte)value }, mapper.Registers);
            }
            AssertRead(mapper, 0x400, Pattern(3, 0x400));
            AssertRead(mapper, 0x4000, Pattern(1, 0));
            AssertRead(mapper, 0x8000, Pattern(2, 0));
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(0x2000)]
    [InlineData(0x3fff)]
    [InlineData(0x4001)]
    [InlineData(0x4200)] // A copier header is not silently stripped.
    [InlineData(0xc000)] // Three complete banks are still unsupported.
    [InlineData(0x14000)]
    [InlineData(0x7ffff)]
    [InlineData(0x80001)]
    [InlineData(0x100000)] // Beyond this chip profile's 32 banks.
    public void UnsupportedRomSizesAreRejected(int length)
    {
        Assert.Throws<ArgumentException>(() => new SegaMapper(new byte[length], Profile));
    }

    [Fact]
    public void InvalidProfileIsRejectedRatherThanGuessed()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new SegaMapper(SyntheticRom(1), (SegaMapperProfile)123));
    }

    [Fact]
    public void TestBusFansOutRegisterWritesAndRamAliasesDoNotChangeMapper()
    {
        // Charles MacDonald, IDocs/General.txt section 2; SMS Power Mappers,
        // ROM mapping: RAM aliases can be overwritten independently of latches.
        var bus = new FanOutHarness(Create(4));
        byte[] values = [0x64, 3, 2, 1];
        for (int register = 0; register < 4; register++)
        {
            ushort address = (ushort)(0xfffc + register);
            Assert.Equal(MapperWriteResult.RegisterUpdated, bus.Write(address, values[register]));
            Assert.Equal(values[register], bus.Read(address));
            Assert.Equal(values[register], bus.Read((ushort)(address - 0x2000)));
            Assert.False(bus.Mapper.TryRead(address, out _));
        }
        Assert.Equal(new SegaMapperRegisters(0x64, 3, 2, 1), bus.Mapper.Registers);

        for (int register = 0; register < 4; register++)
        {
            Assert.Equal(MapperWriteResult.Ignored, bus.Write((ushort)(0xdffc + register), 0xaa));
            Assert.Equal(0xaa, bus.Read((ushort)(0xfffc + register)));
        }
        Assert.Equal(new SegaMapperRegisters(0x64, 3, 2, 1), bus.Mapper.Registers);
        AssertRead(bus.Mapper, 0x400, Pattern(3, 0x400));
        AssertRead(bus.Mapper, 0x4000, Pattern(2, 0));
        AssertRead(bus.Mapper, 0x8000, Pattern(1, 0));
    }

    [Fact]
    public void TestBusMirrorsEveryWorkRamByteWithoutAliasingMapperRegisters()
    {
        var bus = new FanOutHarness(Create(4));
        for (int offset = 0; offset < 0x2000; offset++)
        {
            byte value = (byte)(offset ^ (offset >> 8));
            Assert.Equal(MapperWriteResult.Ignored, bus.Write((ushort)(0xc000 + offset), value));
            Assert.Equal(value, bus.Read((ushort)(0xe000 + offset)));
        }
        Assert.Equal(new SegaMapperRegisters(0, 0, 1, 2), bus.Mapper.Registers);

        for (int offset = 0; offset < 0x1ffc; offset++)
        {
            byte value = (byte)~offset;
            Assert.Equal(MapperWriteResult.Ignored, bus.Write((ushort)(0xe000 + offset), value));
            Assert.Equal(value, bus.Read((ushort)(0xc000 + offset)));
        }
    }

    private static SegaMapper Create(int banks) => new(SyntheticRom(banks), Profile);

    // SMS Power Mappers, RAM mapping: explicit 8 KiB wiring mirrors both
    // halves of slot 2 and both bit-2 selections; not two independent banks.
    [Fact]
    public void SramEveryByteMirrorsBothDirectionsAndBothBankSelections()
    {
        var mapper = CreateSram(0x5a);
        Assert.Equal(SegaMapperProfile.Sega3155235Sram8KiB, mapper.Profile);
        Assert.Equal(SegaMapper.Sram8KiBSize, mapper.SramSize);
        foreach (byte control in new byte[] { 0x08, 0x0c })
        {
            Assert.Equal(MapperWriteResult.RegisterUpdated, mapper.ObserveWrite(0xfffc, control));
            for (int offset = 0; offset < SegaMapper.Sram8KiBSize; offset++)
            {
                byte value = Pattern(control, offset);
                Assert.Equal(MapperWriteResult.CartridgeRamWritten,
                    mapper.ObserveWrite((ushort)(0x8000 + offset), value));
                AssertRead(mapper, 0xa000 + offset, value);
                Assert.Equal(MapperWriteResult.CartridgeRamWritten,
                    mapper.ObserveWrite((ushort)(0xa000 + offset), (byte)~value));
                AssertRead(mapper, 0x8000 + offset, (byte)~value);
            }
            mapper.ObserveWrite(0xfffc, (byte)(control ^ 4));
            for (int offset = 0; offset < SegaMapper.Sram8KiBSize; offset++)
                AssertRead(mapper, 0x8000 + offset, (byte)~Pattern(control, offset));
        }
    }

    [Fact]
    public void SramOverlayBoundariesDisabledWritesHiddenRomBankAndReset()
    {
        var mapper = CreateSram(0x5a);
        for (int address = 0; address < 0xfffc; address++)
            Assert.Equal(MapperWriteResult.Ignored, mapper.ObserveWrite((ushort)address, 0xff));
        Assert.All(mapper.ExportSram(), value => Assert.Equal((byte)0x5a, value));

        mapper.ObserveWrite(0xfffc, 0x08);
        for (int address = 0; address <= ushort.MaxValue; address++)
        {
            if (address is >= 0x8000 and < 0xc000)
                AssertRead(mapper, address, 0x5a);
            else if (address < 0x8000)
                AssertRead(mapper, address, Pattern(address / 0x4000, address & 0x3fff));
            else
                Assert.False(mapper.TryRead((ushort)address, out _));
        }
        Assert.Equal(MapperWriteResult.Ignored, mapper.ObserveWrite(0x7fff, 0xaa));
        Assert.Equal(MapperWriteResult.Ignored, mapper.ObserveWrite(0xc000, 0xaa));
        mapper.ObserveWrite(0x8000, 0x11);
        mapper.ObserveWrite(0xbfff, 0x22);
        mapper.ObserveWrite(0xffff, 3);
        AssertRead(mapper, 0x8000, 0x11);
        AssertRead(mapper, 0x9fff, 0x22);
        mapper.ObserveWrite(0xfffc, 0);
        AssertRead(mapper, 0x8000, Pattern(3, 0));
        AssertRead(mapper, 0xbfff, Pattern(3, 0x3fff));
        Assert.Equal(MapperWriteResult.Ignored, mapper.ObserveWrite(0x8000, 0xff));
        Assert.Equal(MapperWriteResult.Ignored, mapper.ObserveWrite(0xbfff, 0xff));
        mapper.ObserveWrite(0xfffc, 0x0c);
        AssertRead(mapper, 0xa000, 0x11);
        AssertRead(mapper, 0xbfff, 0x22);

        byte[] beforeReset = mapper.ExportSram();
        mapper.ResetToPowerOnProfile();
        mapper.ResetToPowerOnProfile();
        Assert.Equal(new SegaMapperRegisters(0, 0, 1, 2), mapper.Registers);
        Assert.Equal(beforeReset, mapper.ExportSram());
        AssertRead(mapper, 0x8000, Pattern(2, 0));
        mapper.ObserveWrite(0xfffc, 8);
        AssertRead(mapper, 0xa000, 0x11);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(4)]
    [InlineData(8)]
    [InlineData(16)]
    [InlineData(32)]
    public void SramProfileRestoresHiddenRomBankForEachSupportedRomSize(int banks)
    {
        var mapper = new SegaMapper(SyntheticRom(banks), SegaMapperProfile.Sega3155235Sram8KiB, (byte)0x5a);
        mapper.ObserveWrite(0xffff, 0xfe);
        int selectedBank = 0xfe & (banks - 1);
        AssertRead(mapper, 0x8000, Pattern(selectedBank, 0));
        mapper.ObserveWrite(0xfffc, 0x08);
        AssertRead(mapper, 0x8000, 0x5a);
        mapper.ObserveWrite(0xffff, 0xff);
        mapper.ObserveWrite(0xfffc, 0);
        AssertRead(mapper, 0x8000, Pattern(0xff & (banks - 1), 0));
        AssertRead(mapper, 0xbfff, Pattern(0xff & (banks - 1), 0x3fff));
    }

    [Fact]
    public void SramAllControlValuesHaveExplicitSupportedOrAtomicUnsupportedResult()
    {
        var mapper = CreateSram(0x5a);
        mapper.ObserveWrite(0xfffd, 3);
        for (int control = 0; control <= byte.MaxValue; control++)
        {
            var before = mapper.Registers;
            var result = mapper.ObserveWrite(0xfffc, (byte)control);
            if ((control & 0x93) != 0)
            {
                Assert.Equal(MapperWriteResult.UnsupportedControlValue, result);
                Assert.Equal(before, mapper.Registers);
            }
            else
            {
                Assert.Equal(MapperWriteResult.RegisterUpdated, result);
                Assert.Equal(before with { Control = (byte)control }, mapper.Registers);
            }
            AssertRead(mapper, 0x400, Pattern(3, 0x400));
            AssertRead(mapper, 0x4000, Pattern(1, 0));
            AssertRead(mapper, 0x8000, (mapper.Registers.Control & 8) != 0 ? (byte)0x5a : Pattern(2, 0));
            Assert.All(mapper.ExportSram(), value => Assert.Equal((byte)0x5a, value));
        }
    }

    [Fact]
    public void SramConstructionExportAndImportAreDefensiveCopies()
    {
        byte[] initial = Enumerable.Range(0, SegaMapper.Sram8KiBSize).Select(i => Pattern(7, i)).ToArray();
        byte[] expected = initial.ToArray();
        var mapper = new SegaMapper(SyntheticRom(4), SegaMapperProfile.Sega3155235Sram8KiB, initial);
        Array.Fill(initial, (byte)0);
        Assert.Equal(expected, mapper.ExportSram());
        byte[] exported = mapper.ExportSram();
        Array.Fill(exported, (byte)0xff);
        Assert.Equal(expected, mapper.ExportSram());
        mapper.ObserveWrite(0xfffc, 0x0c);
        var registers = mapper.Registers;
        mapper.ImportSram(exported);
        Array.Fill(exported, (byte)0);
        Assert.Equal(registers, mapper.Registers);
        Assert.All(mapper.ExportSram(), value => Assert.Equal((byte)0xff, value));
        AssertRead(mapper, 0x8000, 0xff);
        AssertRead(mapper, 0xbfff, 0xff);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(8191)]
    [InlineData(8193)]
    [InlineData(16384)]
    [InlineData(32768)]
    public void SramInvalidLengthsRejectConstructionAndImportWithoutMutation(int length)
    {
        Assert.Throws<ArgumentException>(() =>
            new SegaMapper(SyntheticRom(4), SegaMapperProfile.Sega3155235Sram8KiB, new byte[length]));
        var mapper = CreateSram(0x5a);
        mapper.ObserveWrite(0xfffc, 8);
        var before = mapper.Registers;
        Assert.Throws<ArgumentException>(() => mapper.ImportSram(new byte[length]));
        Assert.Equal(before, mapper.Registers);
        Assert.All(mapper.ExportSram(), value => Assert.Equal((byte)0x5a, value));
    }

    [Fact]
    public void SramRequiresExplicitInitializationAndCannotBeAddedToRomOnlyProfile()
    {
        Assert.Throws<ArgumentException>(() => new SegaMapper(SyntheticRom(4), SegaMapperProfile.Sega3155235Sram8KiB));
        Assert.Throws<ArgumentException>(() => new SegaMapper(SyntheticRom(4), Profile, new byte[8192]));
        Assert.Throws<ArgumentException>(() => new SegaMapper(SyntheticRom(4), Profile, (byte)0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new SegaMapper(SyntheticRom(4), (SegaMapperProfile)123, (byte)0));
        var mapper = Create(4);
        Assert.Equal(0, mapper.SramSize);
        Assert.Empty(mapper.ExportSram());
        Assert.Throws<InvalidOperationException>(() => mapper.ImportSram(Array.Empty<byte>()));
    }

    private static SegaMapper CreateSram(byte initial) =>
        new(SyntheticRom(4), SegaMapperProfile.Sega3155235Sram8KiB, initial);

    private static byte[] SyntheticRom(int banks)
    {
        var bytes = new byte[banks * SegaMapper.BankSize];
        for (int bank = 0; bank < banks; bank++)
            for (int offset = 0; offset < SegaMapper.BankSize; offset++)
                bytes[bank * SegaMapper.BankSize + offset] = Pattern(bank, offset);
        return bytes;
    }

    private static byte Pattern(int bank, int offset) =>
        (byte)((bank * 7) ^ offset ^ (offset >> 8));

    private static void AssertRead(SegaMapper mapper, int address, byte expected)
    {
        Assert.True(mapper.TryRead((ushort)address, out byte actual));
        Assert.Equal(expected, actual);
    }

    // Deliberately test-only: no shared bus API or production RAM implementation.
    private sealed class FanOutHarness(SegaMapper mapper)
    {
        private readonly byte[] ram = new byte[0x2000];
        public SegaMapper Mapper { get; } = mapper;

        public MapperWriteResult Write(ushort address, byte value)
        {
            if (address >= 0xc000)
                ram[address & 0x1fff] = value;
            return Mapper.ObserveWrite(address, value);
        }

        public byte Read(ushort address) => address >= 0xc000
            ? ram[address & 0x1fff]
            : Mapper.TryRead(address, out byte value)
                ? value
                : throw new InvalidOperationException("Undriven test read.");
    }
}
