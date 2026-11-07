using System;
using System.IO;
using Sms.Core.Cartridge;
using Sms.Core.System;
using Xunit;

namespace Sms.Core.Tests.Validation;

public sealed class BiosExecutionTests
{
    private static readonly string RomsDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "Roms");

    [Theory]
    [InlineData("[BIOS] Alex Kidd in Miracle World (USA, Europe, Brazil) (En).sms")]
    [InlineData("Action Fighter (USA, Europe, Brazil) (En) (Rev 1).sms")]
    [InlineData("Double Dragon (World).sms")]
    public void LoadAndExecute_LocalRom_Runs300FramesWithoutErrors(string romFilename)
    {
        string fullPath = Path.GetFullPath(Path.Combine(RomsDir, romFilename));
        if (!File.Exists(fullPath))
        {
            // Skip gracefully if local ROM file is missing
            return;
        }

        byte[] romBytes = File.ReadAllBytes(fullPath);
        Assert.True(romBytes.Length >= 16384, $"ROM {romFilename} must be at least 16 KiB.");

        var system = new SmsSystem(romBytes: romBytes, mapperProfile: SegaMapperProfile.Sega3155235RomOnly);

        float[] pcmAudio = new float[735];
        for (int frame = 0; frame < 300; frame++)
        {
            system.StepFrame(pcmAudio, 44100);
        }

        Assert.NotEqual(0x0000, system.Cpu.ProgramCounter);
        Assert.True(system.Scheduler.MasterTicks > 0);
        Assert.NotNull(system.FrameBuffer);
        Assert.Equal(256 * 192, system.FrameBuffer.Length);
    }
}
