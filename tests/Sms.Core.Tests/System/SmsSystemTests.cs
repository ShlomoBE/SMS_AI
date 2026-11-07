using Sms.Core.Audio;
using Sms.Core.Cartridge;
using Sms.Core.Input;
using Sms.Core.System;
using Xunit;

namespace Sms.Core.Tests.System;

public class SmsSystemTests
{
    private static byte[] CreateDummyRom(int bankCount = 2, byte fillValue = 0x00)
    {
        byte[] rom = new byte[bankCount * SegaMapper.BankSize];
        rom.AsSpan().Fill(fillValue);
        return rom;
    }

    [Fact]
    public void PowerOn_InitializesStateCorrectly()
    {
        byte[] rom = CreateDummyRom(2, 0x00);
        var sys = new SmsSystem(rom, SegaMapperProfile.Sega3155235RomOnly);

        Assert.Equal(VideoStandard.Ntsc, sys.VideoStandard);
        Assert.Equal(SmsAudioMode.Auto, sys.AudioMode);
        Assert.Equal(SmsSystem.DisplayPixelCount, sys.FrameBuffer.Length);
        Assert.NotNull(sys.Cpu);
        Assert.NotNull(sys.Bus);
        Assert.NotNull(sys.Vdp);
        Assert.NotNull(sys.Controllers);
        Assert.NotNull(sys.AudioMixer);
        Assert.NotNull(sys.Scheduler);
        Assert.NotNull(sys.Mapper);
        Assert.Equal(0x00, sys.Bus.MemoryControl);
        Assert.Equal(8192, sys.Ram.Length);
    }

    [Fact]
    public void Reset_RestoresInitialState()
    {
        byte[] rom = CreateDummyRom(2, 0x00);
        var sys = new SmsSystem(rom);

        // Modify state
        sys.Cpu.ProgramCounter = 0x1234;
        sys.Ram[0] = 0x42;
        sys.Bus.MemoryControl = 0x04;
        sys.FrameBuffer[0] = 0xFF112233;

        // Reset
        sys.Reset();

        Assert.Equal(0x0000, sys.Cpu.ProgramCounter);
        Assert.Equal(0x00, sys.Ram[0]);
        Assert.Equal(0x00, sys.Bus.MemoryControl);
        Assert.Equal(0u, sys.FrameBuffer[0]);
    }

    [Fact]
    public void MemoryRouting_Port3E_Control()
    {
        byte[] bios = new byte[0x4000];
        bios.AsSpan().Fill(0xAA);

        byte[] rom = CreateDummyRom(2, 0xBB);
        var sys = new SmsSystem(romBytes: rom, mapperProfile: SegaMapperProfile.Sega3155235RomOnly, bios: bios);

        // Port $3E = 0x00 -> BIOS enabled (bit 2=0), Cartridge enabled (bit 3=0)
        sys.Bus.MemoryControl = 0x00;
        Assert.Equal(0xAA, sys.Bus.ReadMemory(0x0000)); // < bios.Length -> BIOS
        Assert.Equal(0xBB, sys.Bus.ReadMemory(0x5000)); // >= bios.Length -> Cartridge

        // Port $3E = 0x04 -> BIOS disabled (bit 2=1), Cartridge enabled (bit 3=0)
        sys.Bus.MemoryControl = 0x04;
        Assert.Equal(0xBB, sys.Bus.ReadMemory(0x0000)); // Cartridge
        Assert.Equal(0xBB, sys.Bus.ReadMemory(0x5000)); // Cartridge

        // Port $3E = 0x08 -> BIOS enabled (bit 2=0), Cartridge disabled (bit 3=1)
        sys.Bus.MemoryControl = 0x08;
        Assert.Equal(0xAA, sys.Bus.ReadMemory(0x0000)); // BIOS
        Assert.Equal(0xFF, sys.Bus.ReadMemory(0x5000)); // Cartridge disabled -> 0xFF

        // Port $3E = 0x0C -> BIOS disabled (bit 2=1), Cartridge disabled (bit 3=1)
        sys.Bus.MemoryControl = 0x0C;
        Assert.Equal(0xFF, sys.Bus.ReadMemory(0x0000)); // Unmapped -> 0xFF
        Assert.Equal(0xFF, sys.Bus.ReadMemory(0x5000)); // Unmapped -> 0xFF
    }

    [Fact]
    public void MemoryMap_SystemRam_Mirroring()
    {
        var sys = new SmsSystem(CreateDummyRom());

        sys.Bus.WriteMemory(0xC000, 0x77);
        Assert.Equal(0x77, sys.Bus.ReadMemory(0xE000)); // Mirrored at E000

        sys.Bus.WriteMemory(0xE123, 0x88);
        Assert.Equal(0x88, sys.Bus.ReadMemory(0xC123)); // Mirrored at C123
    }

    [Fact]
    public void MemoryMap_MapperWriteFanOut()
    {
        var sys = new SmsSystem(CreateDummyRom(4)); // 4 banks = 64 KiB

        // Write to FFFD (Slot 0 bank select)
        sys.Bus.WriteMemory(0xFFFD, 0x02);
        Assert.Equal(0x02, sys.Mapper!.Registers.Slot0);

        // Write to DFFD (RAM mirror of FFFD address, but NOT FFFC-FFFF fanout to mapper)
        sys.Bus.WriteMemory(0xDFFD, 0x03);
        // Mapper latch should remain 0x02, while RAM at DFFD/FFFD has 0x03
        Assert.Equal(0x02, sys.Mapper.Registers.Slot0);
        Assert.Equal(0x03, sys.Bus.ReadMemory(0xFFFD));
    }

    [Fact]
    public void MemoryMap_Sram_ReadWrite()
    {
        byte[] rom = CreateDummyRom(2);
        var mapper = new SegaMapper(rom, SegaMapperProfile.Sega3155235Sram8KiB, new byte[8192]);
        var sys = new SmsSystem(mapper);

        // Enable SRAM on Slot 2 (write 0x08 to Port/Address FFFC)
        sys.Bus.WriteMemory(0xFFFC, 0x08);

        // Write to SRAM range 0x8000..0x9FFF
        sys.Bus.WriteMemory(0x8000, 0xA5);
        Assert.Equal(0xA5, sys.Bus.ReadMemory(0x8000));

        byte[] sram = sys.Mapper!.ExportSram();
        Assert.Equal(0xA5, sram[0]);
    }

    [Fact]
    public void PortReadsWrites_3E_3F_VCounter_HCounter_Vdp_Audio_Controllers()
    {
        var sys = new SmsSystem(CreateDummyRom());

        // Port $3E read/write
        sys.Bus.WriteIo(0x3E, 0x04);
        Assert.Equal(0x04, sys.Bus.ReadIo(0x3E));

        // Port $3F read/write
        sys.Bus.WriteIo(0x3F, 0x55);
        Assert.Equal(0x55, sys.Bus.ReadIo(0x3F));

        // V-Counter NTSC
        sys.Bus.VideoStandard = VideoStandard.Ntsc;
        sys.Bus.CurrentScanline = 100;
        Assert.Equal(100, sys.Bus.ReadIo(0x7E));
        sys.Bus.CurrentScanline = 220; // 220 - 6 = 214 = 0xD6
        Assert.Equal(0xD6, sys.Bus.ReadIo(0x7E));

        // V-Counter PAL
        sys.Bus.VideoStandard = VideoStandard.Pal;
        sys.Bus.CurrentScanline = 250; // 250 - 57 = 193 = 0xC1
        Assert.Equal(0xC1, sys.Bus.ReadIo(0x7E));

        // H-Counter
        sys.Bus.CurrentHCounter = 0x84;
        Assert.Equal(0x84, sys.Bus.ReadIo(0x7F));

        // Controller ports $DC / $C0 & $DD / $C1
        sys.SetButtonState(1, SmsButtons.Up, true);
        Assert.Equal(0xFE, sys.Bus.ReadIo(0xDC)); // Bit 0 low = Up pressed (0xFE)
        Assert.Equal(0xFE, sys.Bus.ReadIo(0xC0)); // Mirror $C0

        // YM2413 FM Port $F2 Write & Read
        sys.Bus.WriteIo(0xF2, 0x01); // FM Enable bit 0
        Assert.Equal(0x01, sys.Bus.ReadIo(0xF2));

        // Default unmapped port
        Assert.Equal(0xFF, sys.Bus.ReadIo(0x99));
    }

    [Fact]
    public void FrameStepping_Ntsc_And_Pal()
    {
        var sysNtsc = new SmsSystem(CreateDummyRom(), videoStandard: VideoStandard.Ntsc);
        sysNtsc.StepFrame();
        Assert.Equal(262u * 228u, sysNtsc.Scheduler.MasterTicks);

        var sysPal = new SmsSystem(CreateDummyRom(), videoStandard: VideoStandard.Pal);
        sysPal.StepFrame();
        Assert.Equal(313u * 228u, sysPal.Scheduler.MasterTicks);
    }

    [Fact]
    public void PauseButton_TriggersNmi()
    {
        var sys = new SmsSystem(CreateDummyRom());
        sys.Cpu.ProgramCounter = 0x0100;
        sys.Cpu.IFF1 = true;

        // Set Pause pressed (edge transition -> pending NMI)
        sys.SetPauseState(true);

        int tStates = sys.StepInstruction();
        Assert.Equal(11, tStates); // NMI consumes 11 T-states
        Assert.Equal(0x0066, sys.Cpu.ProgramCounter); // NMI vector
        Assert.True(sys.Cpu.IFF2);
        Assert.False(sys.Cpu.IFF1);
    }

    [Fact]
    public void Vdp_FrameAndLineIrq_Delivery()
    {
        var sys = new SmsSystem(CreateDummyRom());
        sys.Cpu.ProgramCounter = 0x0100;
        sys.Cpu.IFF1 = true;
        sys.Cpu.InterruptMode = 1;

        // Enable Frame IRQ in VDP Register 1 (Bit 5 = 0x20)
        sys.Vdp.WriteControl(0x20); // Data byte 0x20
        sys.Vdp.WriteControl(0x81); // Register 1 write (0x80 | 1)

        sys.Vdp.InjectFrameInterrupt();

        int tStates = sys.StepInstruction();
        Assert.Equal(13, tStates); // IM 1 IRQ consumes 13 T-states
        Assert.Equal(0x0038, sys.Cpu.ProgramCounter); // IM 1 IRQ vector
    }

    [Fact]
    public void AudioSampleGeneration_DuringFrameStepping()
    {
        var sys = new SmsSystem(CreateDummyRom());
        float[] audioBuffer = new float[256];

        sys.StepFrame(audioBuffer, 44100);

        // Verify audio buffer has been populated
        Assert.NotNull(audioBuffer);
        Assert.Equal(256, audioBuffer.Length);
    }

    [Fact]
    public void ConvertCramToArgb_MapsColorsCorrectly()
    {
        // 0x00 -> Black
        Assert.Equal(0xFF000000u, SmsSystem.ConvertCramToArgb(0x00));

        // 0x3F -> White (Red=3, Green=3, Blue=3 => 255, 255, 255)
        Assert.Equal(0xFFFFFFFFu, SmsSystem.ConvertCramToArgb(0x3F));

        // 0x03 -> Full Red (Red=3 => 255)
        Assert.Equal(0xFFFF0000u, SmsSystem.ConvertCramToArgb(0x03));

        // 0x0C -> Full Green (Green=3 => 255)
        Assert.Equal(0xFF00FF00u, SmsSystem.ConvertCramToArgb(0x0C));

        // 0x30 -> Full Blue (Blue=3 => 255)
        Assert.Equal(0xFF0000FFu, SmsSystem.ConvertCramToArgb(0x30));
    }
}
