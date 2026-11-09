using Sms.Core.Fm;
using Xunit;

namespace Sms.Core.Tests.Fm;

public class YM2413SnapshotTests
{
    private static void Write(YM2413Fm fm, byte address, byte value)
    { fm.WritePortAddress(address); fm.WritePortData(value); }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SnapshotResumesCustomPatchAndRhythmWithPendingWrites(bool rhythm)
    {
        var source = new YM2413Fm();
        source.WriteAudioControl(3);
        byte[] patch = { 0x71, 0x61, 0x1e, 0x17, 0xd0, 0x78, 0, 0x17 };
        for (byte i = 0; i < patch.Length; i++) Write(source, i, patch[i]);
        Write(source, 0x30, 0); Write(source, 0x10, 0xac); Write(source, 0x20, 0x19);
        if (rhythm)
        {
            Write(source, 0x16, 0x80); Write(source, 0x26, 0x09);
            Write(source, 0x17, 0x70); Write(source, 0x27, 0x09);
            Write(source, 0x18, 0x60); Write(source, 0x28, 0x09);
            Write(source, 0x0e, 0x3f);
        }
        source.GeneratePcmSamples(new float[101], 101, 44100, 23001);
        // Wave selection changes but its deferred slot update has not yet committed.
        Write(source, 0x03, 0x08);
        source.WritePortAddress(0x05);
        var state = source.CaptureSnapshot();
        state = global::System.Text.Json.JsonSerializer.Deserialize<YM2413Snapshot>(
            global::System.Text.Json.JsonSerializer.Serialize(state))!;
        Assert.True(YM2413Fm.ValidateSnapshot(state));
        var restored = new YM2413Fm();
        restored.RestoreSnapshot(state);
        Assert.Equal(3, restored.AudioControl);
        Assert.Equal(0x05, restored.LatchedAddress);
        source.WritePortData(0xf2); restored.WritePortData(0xf2);
        Assert.Equal(source.ReadRegister(5), restored.ReadRegister(5));
        var heldA = new float[1]; var heldB = new float[1];
        source.GeneratePcmSamples(heldA, 1, 44100, 0);
        restored.GeneratePcmSamples(heldB, 1, 44100, 0);
        Assert.Equal(heldA, heldB);
        var expected = new float[500]; var actual = new float[500];
        source.GeneratePcmSamples(expected, expected.Length, 44100, 121901);
        restored.GeneratePcmSamples(actual, actual.Length, 44100, 121901);
        Assert.Equal(expected, actual);
        Assert.Contains(expected, value => value != 0);
        Assert.Equal(source.CaptureSnapshot().MasterTickRemainder, restored.CaptureSnapshot().MasterTickRemainder);
    }

    [Fact]
    public void InvalidAndVersionOneSnapshotsDoNotMutateDevice()
    {
        var fm = new YM2413Fm(); fm.WriteAudioControl(1); Write(fm, 0x30, 0x10);
        var original = fm.CaptureSnapshot();
        var bad = original with { Version = 1 };
        Assert.False(YM2413Fm.ValidateSnapshot(bad));
        Assert.Throws<ArgumentException>(() => fm.RestoreSnapshot(bad));
        var badSlots = (YM2413SlotState[])original.Slots.Clone();
        badSlots[0] = badSlots[0] with { Patch = 38 };
        Assert.Throws<ArgumentException>(() => fm.RestoreSnapshot(original with { Slots = badSlots }));
        Assert.Equal(original.Address, fm.LatchedAddress);
        Assert.Equal(original.AudioControl, fm.AudioControl);
        Assert.Equal(original.Registers, fm.CaptureSnapshot().Registers);
    }

    [Fact]
    public void CapturedArraysAreIndependentOfDeviceAndRestore()
    {
        var fm = new YM2413Fm(); Write(fm, 0x10, 42);
        var state = fm.CaptureSnapshot();
        var other = new YM2413Fm(); other.RestoreSnapshot(state);
        state.Registers[0x10] = 77;
        Assert.Equal(42, fm.ReadRegister(0x10));
        Assert.Equal(42, other.ReadRegister(0x10));
    }
}
