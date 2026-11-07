using System;
using System.IO;
using Sms.Core.Audio;
using Sms.Core.Cartridge;
using Sms.Core.State;
using Sms.Core.System;
using Sms.Core.Vdp;
using Xunit;

namespace Sms.Core.Tests.State;

public class SmsStateTests
{
    private static byte[] CreateDummyRom(int bankCount = 2, byte fillValue = 0x00)
    {
        byte[] rom = new byte[bankCount * SegaMapper.BankSize];
        rom.AsSpan().Fill(fillValue);
        return rom;
    }

    [Fact]
    public void FullRoundTrip_SaveAndRestoreState_MatchesAllComponents()
    {
        byte[] rom = CreateDummyRom(4, 0x00);
        var mapper = new SegaMapper(rom, SegaMapperProfile.Sega3155235Sram8KiB, 0x55);
        var original = new SmsSystem(mapper, videoStandard: VideoStandard.Pal, audioMode: SmsAudioMode.ForceFm);

        // 1. CPU State
        original.Cpu.ProgramCounter = 0x1234;
        original.Cpu.StackPointer = 0xDFFF;
        original.Cpu.A = 0x11;
        original.Cpu.F = 0x22;
        original.Cpu.B = 0x33;
        original.Cpu.C = 0x44;
        original.Cpu.D = 0x55;
        original.Cpu.E = 0x66;
        original.Cpu.H = 0x77;
        original.Cpu.L = 0x88;
        original.Cpu.IX = 0x99AA;
        original.Cpu.IY = 0xBBCC;
        original.Cpu.APrime = 0xA1;
        original.Cpu.FPrime = 0xF1;
        original.Cpu.BPrime = 0xB1;
        original.Cpu.CPrime = 0xC1;
        original.Cpu.DPrime = 0xD1;
        original.Cpu.EPrime = 0xE1;
        original.Cpu.HPrime = 0x71;
        original.Cpu.LPrime = 0x81;
        original.Cpu.I = 0x02;
        original.Cpu.R = 0x7F;
        original.Cpu.IFF1 = true;
        original.Cpu.IFF2 = true;
        original.Cpu.InterruptMode = 1;
        original.Cpu.PendingEi = true;
        original.Cpu.IsHalted = false;

        // 2. RAM State
        for (int i = 0; i < original.Ram.Length; i++)
        {
            original.Ram[i] = (byte)(i & 0xFF);
        }

        // 3. Cartridge Mapper State & SRAM
        original.Bus.WriteMemory(0xFFFC, 0x08); // Enable SRAM
        original.Bus.WriteMemory(0xFFFD, 0x02);
        original.Bus.WriteMemory(0xFFFE, 0x03);
        original.Bus.WriteMemory(0xFFFF, 0x01);
        original.Bus.WriteMemory(0x8000, 0xAB);
        original.Bus.WriteMemory(0x8001, 0xCD);

        // 4. VDP State
        original.Vdp.WriteControl(0x04);
        original.Vdp.WriteControl(0x80); // Register 0
        original.Vdp.WriteControl(0x20);
        original.Vdp.WriteControl(0x81); // Register 1
        original.Vdp.WriteControl(0x00);
        original.Vdp.WriteControl(0x40); // VRAM Write Address 0x0000
        original.Vdp.WriteData(0xFE);
        original.Vdp.WriteData(0xED);
        original.Vdp.InjectFrameInterrupt();
        original.Vdp.InjectLineInterrupt();

        // 5. PSG State
        original.AudioMixer.Psg.WritePort(0x80); // Latch Tone 0 low
        original.AudioMixer.Psg.WritePort(0x0F); // Tone 0 high
        original.AudioMixer.Psg.WritePort(0x92); // Attenuation channel 0 = 2
        original.AudioMixer.Psg.WritePort(0xE4); // Noise control

        // 6. YM2413 FM State
        original.AudioMixer.Fm.WritePortAddress(0x10);
        original.AudioMixer.Fm.WritePortData(0x50);
        original.AudioMixer.Fm.WritePortAddress(0x20);
        original.AudioMixer.Fm.WritePortData(0x11);
        original.AudioMixer.Fm.WriteAudioControl(0x01);

        // 7. Scheduler State
        original.Scheduler.Advance(1000);

        // 8. System Bus State
        original.Bus.MemoryControl = 0x04;
        original.Bus.CurrentScanline = 120;
        original.Bus.CurrentHCounter = 0x45;

        // Save state
        byte[] stateData = SmsStateSerializer.SaveState(original);
        Assert.NotNull(stateData);
        Assert.True(stateData.Length > 0);

        // Create target system with same mapper profile
        var targetMapper = new SegaMapper(rom, SegaMapperProfile.Sega3155235Sram8KiB, 0x00);
        var restored = new SmsSystem(targetMapper);

        // Restore state
        bool success = SmsStateSerializer.TryRestoreState(restored, stateData, out string? errorMessage);
        Assert.True(success, errorMessage);
        Assert.Null(errorMessage);

        // Assert CPU
        Assert.Equal(original.Cpu.ProgramCounter, restored.Cpu.ProgramCounter);
        Assert.Equal(original.Cpu.StackPointer, restored.Cpu.StackPointer);
        Assert.Equal(original.Cpu.A, restored.Cpu.A);
        Assert.Equal(original.Cpu.F, restored.Cpu.F);
        Assert.Equal(original.Cpu.B, restored.Cpu.B);
        Assert.Equal(original.Cpu.C, restored.Cpu.C);
        Assert.Equal(original.Cpu.D, restored.Cpu.D);
        Assert.Equal(original.Cpu.E, restored.Cpu.E);
        Assert.Equal(original.Cpu.H, restored.Cpu.H);
        Assert.Equal(original.Cpu.L, restored.Cpu.L);
        Assert.Equal(original.Cpu.IX, restored.Cpu.IX);
        Assert.Equal(original.Cpu.IY, restored.Cpu.IY);
        Assert.Equal(original.Cpu.APrime, restored.Cpu.APrime);
        Assert.Equal(original.Cpu.FPrime, restored.Cpu.FPrime);
        Assert.Equal(original.Cpu.BPrime, restored.Cpu.BPrime);
        Assert.Equal(original.Cpu.CPrime, restored.Cpu.CPrime);
        Assert.Equal(original.Cpu.DPrime, restored.Cpu.DPrime);
        Assert.Equal(original.Cpu.EPrime, restored.Cpu.EPrime);
        Assert.Equal(original.Cpu.HPrime, restored.Cpu.HPrime);
        Assert.Equal(original.Cpu.LPrime, restored.Cpu.LPrime);
        Assert.Equal(original.Cpu.I, restored.Cpu.I);
        Assert.Equal(original.Cpu.R, restored.Cpu.R);
        Assert.Equal(original.Cpu.IFF1, restored.Cpu.IFF1);
        Assert.Equal(original.Cpu.IFF2, restored.Cpu.IFF2);
        Assert.Equal(original.Cpu.InterruptMode, restored.Cpu.InterruptMode);
        Assert.Equal(original.Cpu.PendingEi, restored.Cpu.PendingEi);
        Assert.Equal(original.Cpu.IsHalted, restored.Cpu.IsHalted);
        Assert.Equal(original.Cpu.IsFaulted, restored.Cpu.IsFaulted);

        // Assert RAM
        Assert.Equal(original.Ram, restored.Ram);

        // Assert Mapper & SRAM
        Assert.NotNull(restored.Mapper);
        Assert.Equal(original.Mapper!.Registers, restored.Mapper.Registers);
        Assert.Equal(original.Mapper.ExportSram(), restored.Mapper.ExportSram());

        // Assert VDP
        var origVdp = original.Vdp.Inspect();
        var restVdp = restored.Vdp.Inspect();
        Assert.Equal(origVdp.Vram, restVdp.Vram);
        Assert.Equal(origVdp.Cram, restVdp.Cram);
        Assert.Equal(origVdp.Registers, restVdp.Registers);
        Assert.Equal(origVdp.Address, restVdp.Address);
        Assert.Equal(origVdp.Code, restVdp.Code);
        Assert.Equal(origVdp.SecondControlByte, restVdp.SecondControlByte);
        Assert.Equal(origVdp.ReadBuffer, restVdp.ReadBuffer);
        Assert.Equal(origVdp.Status, restVdp.Status);
        Assert.Equal(origVdp.LinePending, restVdp.LinePending);

        // Assert PSG
        var origPsg = original.AudioMixer.Psg;
        var restPsg = restored.AudioMixer.Psg;
        for (int ch = 0; ch < 3; ch++) Assert.Equal(origPsg.GetTonePeriod(ch), restPsg.GetTonePeriod(ch));
        for (int ch = 0; ch < 4; ch++) Assert.Equal(origPsg.GetAttenuation(ch), restPsg.GetAttenuation(ch));
        Assert.Equal(origPsg.NoiseControl, restPsg.NoiseControl);
        Assert.Equal(origPsg.Lfsr, restPsg.Lfsr);
        Assert.Equal(origPsg.LatchedRegister, restPsg.LatchedRegister);

        // Assert FM
        var origFm = original.AudioMixer.Fm;
        var restFm = restored.AudioMixer.Fm;
        Assert.Equal(origFm.LatchedAddress, restFm.LatchedAddress);
        Assert.Equal(origFm.AudioControl, restFm.AudioControl);
        for (int ch = 0; ch < 9; ch++)
        {
            Assert.Equal(origFm.GetFNumber(ch), restFm.GetFNumber(ch));
            Assert.Equal(origFm.GetBlock(ch), restFm.GetBlock(ch));
            Assert.Equal(origFm.GetKeyOn(ch), restFm.GetKeyOn(ch));
            Assert.Equal(origFm.GetSustain(ch), restFm.GetSustain(ch));
            Assert.Equal(origFm.GetPatchIndex(ch), restFm.GetPatchIndex(ch));
            Assert.Equal(origFm.GetVolume(ch), restFm.GetVolume(ch));
        }

        // Assert Scheduler
        Assert.Equal(original.Scheduler.MasterTicks, restored.Scheduler.MasterTicks);
        Assert.Equal(original.Scheduler.CpuPhase, restored.Scheduler.CpuPhase);
        Assert.Equal(original.Scheduler.VdpPhase, restored.Scheduler.VdpPhase);
        Assert.Equal(original.Scheduler.PsgPhase, restored.Scheduler.PsgPhase);

        // Assert Bus
        Assert.Equal(original.Bus.MemoryControl, restored.Bus.MemoryControl);
        Assert.Equal(original.VideoStandard, restored.VideoStandard);
        Assert.Equal(original.AudioMode, restored.AudioMode);
    }

    [Fact]
    public void DeterministicContinuation_Stepping100FramesBeforeVsAfterRestore_ProducesIdenticalFrameBuffers()
    {
        byte[] rom = CreateDummyRom(2, 0x00); // NOP instructions
        var sys1 = new SmsSystem(rom);

        // Run sys1 for 10 initial frames
        for (int f = 0; f < 10; f++)
        {
            sys1.StepFrame();
        }

        // Save state at frame 10
        byte[] stateData = SmsStateSerializer.SaveState(sys1);

        // Advance sys1 by 100 more frames
        for (int f = 0; f < 100; f++)
        {
            sys1.StepFrame();
        }

        uint[] frameBufferBeforeSave = (uint[])sys1.FrameBuffer.Clone();

        // Create sys2 and restore state at frame 10
        var sys2 = new SmsSystem(rom);
        bool restored = SmsStateSerializer.TryRestoreState(sys2, stateData, out string? error);
        Assert.True(restored, error);

        // Advance sys2 by 100 frames
        for (int f = 0; f < 100; f++)
        {
            sys2.StepFrame();
        }

        uint[] frameBufferAfterRestore = sys2.FrameBuffer;

        // Assert that both systems produced 100% identical frame buffers after 100 frames
        Assert.Equal(frameBufferBeforeSave, frameBufferAfterRestore);
    }

    [Fact]
    public void ErrorHandling_InvalidMagicHeader_ReturnsFalse_SystemUnchanged()
    {
        var sys = new SmsSystem(CreateDummyRom());
        sys.Cpu.ProgramCounter = 0x1234;

        byte[] invalidMagicState = SmsStateSerializer.SaveState(sys);
        // Overwrite magic bytes "SMSSTATE" with "BADMAGIC"
        invalidMagicState[0] = (byte)'B';
        invalidMagicState[1] = (byte)'A';
        invalidMagicState[2] = (byte)'D';

        var targetSys = new SmsSystem(CreateDummyRom());
        targetSys.Cpu.ProgramCounter = 0x5555;

        bool success = SmsStateSerializer.TryRestoreState(targetSys, invalidMagicState, out string? errorMessage);

        Assert.False(success);
        Assert.NotNull(errorMessage);
        Assert.Contains("magic", errorMessage, StringComparison.OrdinalIgnoreCase);
        // Verify system state was unchanged
        Assert.Equal((ushort)0x5555, targetSys.Cpu.ProgramCounter);
    }

    [Fact]
    public void ErrorHandling_VersionMismatch_ReturnsFalse_SystemUnchanged()
    {
        var sys = new SmsSystem(CreateDummyRom());
        sys.Cpu.ProgramCounter = 0x1234;

        byte[] versionMismatchState = SmsStateSerializer.SaveState(sys);
        // Version is at bytes 8..11 (uint32). Set to version 99
        versionMismatchState[8] = 99;
        versionMismatchState[9] = 0;
        versionMismatchState[10] = 0;
        versionMismatchState[11] = 0;

        var targetSys = new SmsSystem(CreateDummyRom());
        targetSys.Cpu.ProgramCounter = 0x5555;

        bool success = SmsStateSerializer.TryRestoreState(targetSys, versionMismatchState, out string? errorMessage);

        Assert.False(success);
        Assert.NotNull(errorMessage);
        Assert.Contains("version", errorMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Equal((ushort)0x5555, targetSys.Cpu.ProgramCounter);
    }

    [Fact]
    public void ErrorHandling_CorruptedOrTruncatedPayload_ReturnsFalse_SystemUnchanged()
    {
        var sys = new SmsSystem(CreateDummyRom());
        byte[] validState = SmsStateSerializer.SaveState(sys);

        // Truncate payload to 20 bytes (incomplete)
        byte[] truncatedState = validState[..20];

        var targetSys = new SmsSystem(CreateDummyRom());
        targetSys.Cpu.ProgramCounter = 0x7777;

        bool success = SmsStateSerializer.TryRestoreState(targetSys, truncatedState, out string? errorMessage);

        Assert.False(success);
        Assert.NotNull(errorMessage);
        Assert.Equal((ushort)0x7777, targetSys.Cpu.ProgramCounter);
    }

    [Fact]
    public void FileIo_SaveAndLoadStateFromFile_RoundTrip()
    {
        var sysOriginal = new SmsSystem(CreateDummyRom());
        sysOriginal.Cpu.ProgramCounter = 0x2468;
        sysOriginal.Ram[10] = 0x99;

        string tempFile = Path.Combine(Path.GetTempPath(), $"sms_test_state_{Guid.NewGuid():N}.state");

        try
        {
            SmsStateSerializer.SaveStateToFile(sysOriginal, tempFile);
            Assert.True(File.Exists(tempFile));

            var sysRestored = new SmsSystem(CreateDummyRom());
            SmsStateSerializer.LoadStateFromFile(sysRestored, tempFile);

            Assert.Equal((ushort)0x2468, sysRestored.Cpu.ProgramCounter);
            Assert.Equal(0x99, sysRestored.Ram[10]);
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }
        }
    }

    [Fact]
    public void SramPersistence_SaveAndLoadSramFromFile_RoundTrip()
    {
        byte[] rom = CreateDummyRom(2);
        var mapper1 = new SegaMapper(rom, SegaMapperProfile.Sega3155235Sram8KiB, 0x00);
        var sys1 = new SmsSystem(mapper1);

        // Write test data to SRAM
        sys1.Bus.WriteMemory(0xFFFC, 0x08); // Enable SRAM
        sys1.Bus.WriteMemory(0x8000, 0x12);
        sys1.Bus.WriteMemory(0x8001, 0x34);
        sys1.Bus.WriteMemory(0x9FFF, 0x56);

        string tempSramFile = Path.Combine(Path.GetTempPath(), $"sms_test_sram_{Guid.NewGuid():N}.sav");

        try
        {
            SmsStateSerializer.SaveSramToFile(sys1, tempSramFile);
            Assert.True(File.Exists(tempSramFile));
            Assert.Equal(8192, new FileInfo(tempSramFile).Length);

            var mapper2 = new SegaMapper(rom, SegaMapperProfile.Sega3155235Sram8KiB, 0x00);
            var sys2 = new SmsSystem(mapper2);

            SmsStateSerializer.LoadSramFromFile(sys2, tempSramFile);

            sys2.Bus.WriteMemory(0xFFFC, 0x08); // Enable SRAM
            Assert.Equal(0x12, sys2.Bus.ReadMemory(0x8000));
            Assert.Equal(0x34, sys2.Bus.ReadMemory(0x8001));
            Assert.Equal(0x56, sys2.Bus.ReadMemory(0x9FFF));
        }
        finally
        {
            if (File.Exists(tempSramFile))
            {
                File.Delete(tempSramFile);
            }
        }
    }

    [Fact]
    public void SramPersistence_NoSramCartridge_ThrowsInvalidOperationException()
    {
        byte[] rom = CreateDummyRom(2);
        var mapperNoSram = new SegaMapper(rom, SegaMapperProfile.Sega3155235RomOnly);
        var sys = new SmsSystem(mapperNoSram);

        string tempSramFile = Path.Combine(Path.GetTempPath(), $"sms_no_sram_{Guid.NewGuid():N}.sav");

        Assert.Throws<InvalidOperationException>(() => SmsStateSerializer.SaveSramToFile(sys, tempSramFile));
        Assert.Throws<InvalidOperationException>(() => SmsStateSerializer.LoadSramFromFile(sys, tempSramFile));
    }
}
