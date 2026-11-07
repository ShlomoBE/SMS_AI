using Sms.Core.Psg;

namespace Sms.Core.Tests.Psg;

public sealed class PsgRegisterTests
{
    [Fact]
    public void FixtureInitializationIsExplicitAndDeterministic()
    {
        var expected = new PsgRegisterSnapshot(0, 0, 0, 15, 15, 15, 15,
            0, PsgRegister.Tone0, 0x8000, 0);
        Assert.Equal(expected, PsgRegisters.CreateDeterministicFixture().Inspect());
        Assert.Equal(expected, PsgRegisters.CreateDeterministicFixture().Inspect());
    }

    [Fact]
    public void EveryLatchByteSelectsAndUpdatesOnlyItsRegister()
    {
        for (int value = 0x80; value <= 0xff; value++)
        {
            var psg = SeedAllRegisters();
            var before = psg.Inspect();
            int selected = (value >> 4) & 7;
            psg.Write((byte)value);
            var after = psg.Inspect();

            Assert.Equal((PsgRegister)selected, after.LatchedRegister);
            for (int register = 0; register < 8; register++)
            {
                int expected = RegisterValue(before, register);
                if (register == selected)
                {
                    expected = register == 6 ? value & 7
                        : (register & 1) != 0 ? value & 15
                        : (expected & 0x3f0) | (value & 15);
                }
                Assert.Equal(expected, RegisterValue(after, register));
            }
            Assert.Equal(before.NoiseReseedCount + (selected == 6 ? 1UL : 0UL),
                after.NoiseReseedCount);
        }
    }

    [Fact]
    public void EveryDataByteHonorsEachPersistentLatchAndMasksUnusedBits()
    {
        for (int selected = 0; selected < 8; selected++)
        {
            var psg = SeedAllRegisters();
            psg.Write((byte)(0x80 | (selected << 4) | 0x0b));
            for (int value = 0; value < 0x80; value++)
            {
                var before = psg.Inspect();
                psg.Write((byte)value);
                var after = psg.Inspect();
                Assert.Equal((PsgRegister)selected, after.LatchedRegister);
                for (int register = 0; register < 8; register++)
                {
                    int expected = RegisterValue(before, register);
                    if (register == selected)
                    {
                        expected = register == 6 ? value & 7
                            : (register & 1) != 0 ? value & 15
                            : ((value & 0x3f) << 4) | (expected & 15);
                    }
                    Assert.Equal(expected, RegisterValue(after, register));
                }
                Assert.Equal(before.NoiseReseedCount + (selected == 6 ? 1UL : 0UL),
                    after.NoiseReseedCount);
            }
        }
    }

    [Fact]
    public void ReferenceWriteExamplesUpdateToneVolumeAndNoiseImmediately()
    {
        var psg = PsgRegisters.CreateDeterministicFixture();
        psg.Write(0x8e);
        Assert.Equal(14, psg.Inspect().Tone0);
        psg.Write(0x0f);
        Assert.Equal(0xfe, psg.Inspect().Tone0);
        psg.Write(0xbf);
        Assert.Equal(15, psg.Inspect().Attenuation1);
        psg.Write(0xdf);
        Assert.Equal(15, psg.Inspect().Attenuation2);
        psg.Write(0x00);
        Assert.Equal(0, psg.Inspect().Attenuation2);
        psg.Write(0xe5);
        Assert.Equal(5, psg.Inspect().Noise);
        psg.Write(0x04);
        Assert.Equal(4, psg.Inspect().Noise);
        Assert.Equal(PsgRegister.Noise, psg.Inspect().LatchedRegister);
    }

    [Fact]
    public void RepeatedNoiseWritesRecordReseedEventsEvenForUnchangedSettings()
    {
        var psg = PsgRegisters.CreateDeterministicFixture();
        foreach (byte value in new byte[] { 0xe5, 0xe5, 0x05, 0x45, 0xed })
        {
            ulong previousCount = psg.Inspect().NoiseReseedCount;
            psg.Write(value);
            Assert.Equal(5, psg.Inspect().Noise);
            Assert.Equal(0x8000, psg.Inspect().NoiseShiftRegister);
            Assert.Equal(previousCount + 1, psg.Inspect().NoiseReseedCount);
        }
        // The diagnostic count observes writes; no LFSR evolution is claimed.
    }

    [Fact]
    public void SnapshotIsDetachedAndInspectionHasNoSideEffects()
    {
        var psg = PsgRegisters.CreateDeterministicFixture();
        var before = psg.Inspect();
        Assert.Equal(before, psg.Inspect());
        psg.Write(0x8f);
        Assert.Equal(0, before.Tone0);
        Assert.Equal(15, psg.Inspect().Tone0);
        var editedCopy = before with { Tone0 = 123 };
        Assert.Equal(123, editedCopy.Tone0);
        Assert.Equal(15, psg.Inspect().Tone0);
    }

    [Fact]
    public void IdenticalWriteSequencesHaveIdenticalInspectionOutputs()
    {
        var first = PsgRegisters.CreateDeterministicFixture();
        var second = PsgRegisters.CreateDeterministicFixture();
        for (int value = 0; value <= 255; value++)
        {
            first.Write((byte)value);
            second.Write((byte)value);
            Assert.Equal(first.Inspect(), second.Inspect());
        }
    }

    private static PsgRegisters SeedAllRegisters()
    {
        var psg = PsgRegisters.CreateDeterministicFixture();
        for (int register = 0; register < 8; register++)
        {
            psg.Write((byte)(0x80 | (register << 4) | 5));
            if ((register & 1) == 0 && register != 6)
            {
                psg.Write(0x2a);
            }
        }
        return psg;
    }

    private static int RegisterValue(PsgRegisterSnapshot state, int register) => register switch
    {
        0 => state.Tone0,
        1 => state.Attenuation0,
        2 => state.Tone1,
        3 => state.Attenuation1,
        4 => state.Tone2,
        5 => state.Attenuation2,
        6 => state.Noise,
        7 => state.Attenuation3,
        _ => throw new ArgumentOutOfRangeException(nameof(register))
    };
}
