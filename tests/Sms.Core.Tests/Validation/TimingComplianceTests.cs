using System;
using Sms.Core.Cartridge;
using Sms.Core.Input;
using Sms.Core.System;
using Sms.Core.Timing;
using Xunit;

namespace Sms.Core.Tests.Validation;

public sealed class TimingComplianceTests
{
    private static byte[] CreateDummyRom()
    {
        byte[] rom = new byte[32768];
        rom.AsSpan().Fill(0x00);
        return rom;
    }

    [Fact]
    public void MasterScheduler_ClockDomainDivisors_PreservedAcrossStepFrame()
    {
        var sys = new SmsSystem(CreateDummyRom(), videoStandard: VideoStandard.Ntsc);
        sys.StepFrame();

        // NTSC = 262 lines * 684 master ticks/line = 179,208 master ticks per frame (228 CPU T-states/line)
        ulong expectedMasterTicks = 262u * 684u;
        Assert.Equal(expectedMasterTicks, sys.Scheduler.MasterTicks);
    }

    [Fact]
    public void VCounter_Progression_NtscAndPalTables()
    {
        var sysNtsc = new SmsSystem(CreateDummyRom(), videoStandard: VideoStandard.Ntsc);

        sysNtsc.Bus.CurrentScanline = 100;
        Assert.Equal(100, sysNtsc.Bus.ReadIo(0x7E));

        sysNtsc.Bus.CurrentScanline = 230; // 230 - 6 = 224 = 0xE0
        Assert.Equal(0xE0, sysNtsc.Bus.ReadIo(0x7E));

        var sysPal = new SmsSystem(CreateDummyRom(), videoStandard: VideoStandard.Pal);
        sysPal.Bus.CurrentScanline = 250; // 250 - 57 = 193 = 0xC1
        Assert.Equal(0xC1, sysPal.Bus.ReadIo(0x7E));
    }

    [Fact]
    public void PauseButton_TriggersNmi_ExecutesVector0x0066()
    {
        var sys = new SmsSystem(CreateDummyRom());
        sys.Cpu.ProgramCounter = 0x0100;

        // Press Pause button
        sys.SetPauseState(true);

        // Stepping instruction processes NMI
        sys.StepInstruction();

        Assert.Equal(0x0066, sys.Cpu.ProgramCounter);
    }
}
