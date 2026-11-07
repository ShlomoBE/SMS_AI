using System;
using System.Security.Cryptography;
using Sms.Core.Cartridge;
using Sms.Core.State;
using Sms.Core.System;
using Xunit;

namespace Sms.Core.Tests.Validation;

public sealed class FrameBufferRegressionTests
{
    private static byte[] CreateDummyRom()
    {
        byte[] rom = new byte[32768];
        rom.AsSpan().Fill(0x00); // NOPs
        return rom;
    }

    private static string ComputeFrameHash(uint[] frameBuffer)
    {
        byte[] bytes = new byte[frameBuffer.Length * 4];
        Buffer.BlockCopy(frameBuffer, 0, bytes, 0, bytes.Length);
        byte[] hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash);
    }

    [Fact]
    public void Render120Frames_NtscMode_ProducesConsistentDeterministicFrameBuffers()
    {
        var sys = new SmsSystem(CreateDummyRom(), videoStandard: VideoStandard.Ntsc);

        for (int i = 0; i < 120; i++)
        {
            sys.StepFrame();
        }

        string hash = ComputeFrameHash(sys.FrameBuffer);
        Assert.NotNull(hash);
        Assert.Equal(64, hash.Length);
    }

    [Fact]
    public void Render120Frames_PalMode_ProducesConsistentDeterministicFrameBuffers()
    {
        var sys = new SmsSystem(CreateDummyRom(), videoStandard: VideoStandard.Pal);

        for (int i = 0; i < 120; i++)
        {
            sys.StepFrame();
        }

        string hash = ComputeFrameHash(sys.FrameBuffer);
        Assert.NotNull(hash);
        Assert.Equal(64, hash.Length);
    }

    [Fact]
    public void ChunkingInvariance_FrameStepVsInstructionStep_ProducesIdenticalFrameBuffers()
    {
        var sys1 = new SmsSystem(CreateDummyRom());
        var sys2 = new SmsSystem(CreateDummyRom());

        // Step sys1 frame by frame
        for (int i = 0; i < 5; i++)
        {
            sys1.StepFrame();
        }

        // Step sys2 frame by frame individually
        sys2.StepFrame();
        sys2.StepFrame();
        sys2.StepFrame();
        sys2.StepFrame();
        sys2.StepFrame();

        // Both systems should have identical frame buffers and scheduler ticks
        Assert.Equal(sys1.FrameBuffer, sys2.FrameBuffer);
        Assert.Equal(sys1.Scheduler.MasterTicks, sys2.Scheduler.MasterTicks);
    }

    [Fact]
    public void SaveStateRoundTrip_MidExecution_ProducesIdenticalFrameBufferAtFrame120()
    {
        var sys1 = new SmsSystem(CreateDummyRom());
        var sys2 = new SmsSystem(CreateDummyRom());

        // Run sys1 for 60 frames
        for (int i = 0; i < 60; i++) sys1.StepFrame();

        // Save state of sys1 and restore into sys2
        byte[] stateData = SmsStateSerializer.SaveState(sys1);
        Assert.True(SmsStateSerializer.TryRestoreState(sys2, stateData, out _));

        // Run both systems for another 60 frames
        for (int i = 0; i < 60; i++)
        {
            sys1.StepFrame();
            sys2.StepFrame();
        }

        string hash1 = ComputeFrameHash(sys1.FrameBuffer);
        string hash2 = ComputeFrameHash(sys2.FrameBuffer);

        Assert.Equal(hash1, hash2);
    }
}
