using Sms.Core.Audio;
using Sms.Core.Cartridge;
using Sms.Core.Cpu;
using Sms.Core.Input;
using Sms.Core.Timing;
using Sms.Core.Vdp;

namespace Sms.Core.System;

/// <summary>
/// Sega Master System Console Host Facade.
/// Coordinates Z80 CPU, Memory & I/O Bus, VDP, Input Controllers, Audio Mixer, and Master Scheduler.
/// </summary>
public sealed class SmsSystem
{
    public const int DisplayWidth = 256;
    public const int DisplayHeight = 192;
    public const int DisplayPixelCount = DisplayWidth * DisplayHeight;

    private readonly VdpPixel[] _bgBuffer = new VdpPixel[DisplayWidth];
    private readonly VdpPixel[] _finalBuffer = new VdpPixel[DisplayWidth];

    private byte _lineCounter;
    private int _timingCarryTStates;

    /// <summary>
    /// Gets or sets the Video Standard (NTSC or PAL).
    /// </summary>
    public VideoStandard VideoStandard
    {
        get => Bus.VideoStandard;
        set => Bus.VideoStandard = value;
    }

    /// <summary>
    /// Gets or sets the Audio Rendering Mode (Auto, ForcePsg, or ForceFm).
    /// </summary>
    public SmsAudioMode AudioMode
    {
        get => AudioMixer.Mode;
        set => AudioMixer.Mode = value;
    }

    /// <summary>
    /// Gets the 256x192 32-bit ARGB (0xFFRRGGBB) frame buffer.
    /// </summary>
    public uint[] FrameBuffer { get; }

    /// <summary>
    /// Gets the Z80 CPU instance.
    /// </summary>
    public Z80 Cpu { get; private set; }

    /// <summary>
    /// Gets the System Bus instance.
    /// </summary>
    public SmsSystemBus Bus { get; }

    /// <summary>
    /// Gets the VDP Ports interface.
    /// </summary>
    public VdpPorts Vdp => Bus.Vdp;

    /// <summary>
    /// Gets the Controller Ports interface.
    /// </summary>
    public SegaControllerPorts Controllers => Bus.Controllers;

    /// <summary>
    /// Gets the Audio Mixer instance.
    /// </summary>
    public SmsAudioMixer AudioMixer => Bus.AudioMixer;

    /// <summary>
    /// Gets the Master System Scheduler instance.
    /// </summary>
    public MasterScheduler Scheduler { get; private set; }

    /// <summary>
    /// Gets the Cartridge Mapper instance (if present).
    /// </summary>
    public SegaMapper? Mapper => Bus.Mapper;

    /// <summary>
    /// Gets the System Work RAM buffer (8 KiB).
    /// </summary>
    public byte[] Ram => Bus.Ram;

    /// <summary>
    /// Initializes a new instance of <see cref="SmsSystem"/>.
    /// </summary>
    public SmsSystem(
        SegaMapper? mapper = null,
        byte[]? bios = null,
        VideoStandard videoStandard = VideoStandard.Ntsc,
        SmsAudioMode audioMode = SmsAudioMode.Auto)
    {
        FrameBuffer = new uint[DisplayPixelCount];
        Bus = new SmsSystemBus(mapper: mapper, bios: bios);
        Bus.VideoStandard = videoStandard;
        Bus.AudioMixer.Mode = audioMode;

        Cpu = new Z80(Bus);
        Scheduler = new MasterScheduler();

        Reset();
    }

    /// <summary>
    /// Initializes a new instance of <see cref="SmsSystem"/> with raw ROM bytes and mapper profile.
    /// </summary>
    public SmsSystem(
        ReadOnlySpan<byte> romBytes,
        SegaMapperProfile mapperProfile = SegaMapperProfile.Sega3155235RomOnly,
        byte[]? bios = null,
        VideoStandard videoStandard = VideoStandard.Ntsc,
        SmsAudioMode audioMode = SmsAudioMode.Auto)
        : this(new SegaMapper(romBytes, mapperProfile, mapperProfile == SegaMapperProfile.Sega3155235Sram8KiB ? new byte[8192] : ReadOnlySpan<byte>.Empty), bios, videoStandard, audioMode)
    {
    }

    /// <summary>
    /// Resets console CPU, Memory, VDP, Input, Audio, Scheduler, and FrameBuffer state.
    /// </summary>
    public void Reset()
    {
        Cpu = new Z80(Bus);

        Bus.MemoryControl = 0x00;
        Array.Clear(Bus.Ram, 0, Bus.Ram.Length);

        Bus.Mapper?.ResetToPowerOnProfile();

        Bus.Vdp = VdpPorts.CreateDeterministicFixture(
            new byte[0x4000], new byte[32], new byte[11], 0, 0, false, 0, 0, false);
        Bus.Controllers = new SegaControllerPorts();
        Bus.AudioMixer = new SmsAudioMixer();

        Scheduler = new MasterScheduler();

        Array.Clear(FrameBuffer, 0, FrameBuffer.Length);

        Bus.CurrentScanline = 0;
        Bus.CurrentHCounter = 0;
        _lineCounter = 0;
        _timingCarryTStates = 0;
    }

    /// <summary>
    /// Restores VDP fixture state from save state.
    /// </summary>
    public void RestoreVdpFixture(byte[] vram, byte[] cram, byte[] registers, int address, int code, bool secondControlByte, byte readBuffer, byte status, bool linePending)
    {
        Bus.Vdp = VdpPorts.CreateDeterministicFixture(vram, cram, registers, address, code, secondControlByte, readBuffer, status, linePending);
    }

    /// <summary>
    /// Restores Scheduler state from save state.
    /// </summary>
    public void RestoreScheduler(ulong masterTicks, uint cpuPhase, uint vdpPhase, uint psgPhase)
    {
        Scheduler = new MasterScheduler(masterTicks, cpuPhase, vdpPhase, psgPhase);
    }

    /// <summary>
    /// Executes one CPU instruction, checking for Pause NMI and VDP IRQ delivery, and advancing scheduler.
    /// Returns CPU T-states consumed by the step.
    /// </summary>
    public int StepInstruction()
    {
        int tStates;

        if (Controllers.CheckPauseNmiPending())
        {
            tStates = Cpu.AcceptNmi();
        }
        else if (Vdp.IrqAsserted && Cpu.IFF1 && !Cpu.PendingEi)
        {
            tStates = Cpu.AcceptIrq();
        }
        else
        {
            tStates = Cpu.Step();
        }

        Scheduler.AdvanceCpuTStates(tStates);
        return tStates;
    }

    /// <summary>
    /// Executes a full frame (262 lines for NTSC, 313 lines for PAL).
    /// Renders visible scanlines 0..191 via VDP background &amp; sprite engines into <see cref="FrameBuffer"/>,
    /// triggers VDP line and frame interrupts, updates Pause NMI, and generates audio PCM samples.
    /// </summary>
    /// <param name="audioBuffer">Optional buffer to receive mixed PCM audio samples [-1.0f .. 1.0f].</param>
    /// <param name="sampleRate">Target audio sampling rate in Hz (default 44100).</param>
    public void StepFrame(float[]? audioBuffer = null, int sampleRate = 44100)
    {
        int totalScanlines = VideoStandard == VideoStandard.Ntsc ? 262 : 313;
        const int tStatesPerLine = 228; // NTSC/PAL standard: 228 CPU T-states per scanline (684 master ticks / 3)

        for (int line = 0; line < totalScanlines; line++)
        {
            Bus.CurrentScanline = line;

            byte[] registers = Vdp.InternalRegisters;
            byte[] vram = Vdp.InternalVram;
            byte[] cram = Vdp.InternalCram;

            UpdateVdpScanlineEvents(line, registers);

            if (line < DisplayHeight)
            {
                VdpMode4Background.RenderScanline(vram, cram, registers, line, _bgBuffer);
                VdpSpriteEngine.ProcessScanline(
                    vram, cram, registers, line, _bgBuffer, _finalBuffer,
                    out bool overflow, out bool collision);

                if (overflow) Vdp.InjectSpriteOverflow();
                if (collision) Vdp.InjectSpriteCollision();

                int lineOffset = line * DisplayWidth;
                for (int x = 0; x < DisplayWidth; x++)
                {
                    FrameBuffer[lineOffset + x] = ConvertCramToArgb(_finalBuffer[x].CramColor);
                }
            }

            int targetTStates = tStatesPerLine - _timingCarryTStates;
            int executedLineTStates = 0;
            while (executedLineTStates < targetTStates)
            {
                executedLineTStates += StepInstruction();
            }
            _timingCarryTStates = executedLineTStates - targetTStates;
        }

        if (audioBuffer != null && audioBuffer.Length > 0)
        {
            ulong frameMasterTicks = (ulong)totalScanlines * 684;
            AudioMixer.GeneratePcmSamples(audioBuffer, audioBuffer.Length, sampleRate, frameMasterTicks);
        }
    }

    /// <summary>
    /// Sets button state for player 1 or player 2 controller.
    /// </summary>
    public void SetButtonState(int player, SmsButtons button, bool pressed)
    {
        Controllers.SetButtonState(player, button, pressed);
    }

    /// <summary>
    /// Sets Console Pause button state (edge transition from false to true triggers NMI).
    /// </summary>
    public void SetPauseState(bool pressed)
    {
        Controllers.SetPauseState(pressed);
    }

    /// <summary>
    /// Sets Console Reset button state.
    /// </summary>
    public void SetResetState(bool pressed)
    {
        Controllers.SetResetState(pressed);
    }

    private static readonly uint[] CramLut = BuildCramLut();

    private static uint[] BuildCramLut()
    {
        uint[] lut = new uint[64];
        for (int i = 0; i < 64; i++)
        {
            byte r = (byte)((i & 0x03) * 85);
            byte g = (byte)(((i >> 2) & 0x03) * 85);
            byte b = (byte)(((i >> 4) & 0x03) * 85);
            lut[i] = 0xFF000000u | ((uint)r << 16) | ((uint)g << 8) | b;
        }
        return lut;
    }

    /// <summary>
    /// Converts a 6-bit SMS CRAM color byte (00BBGGRR) to a 32-bit ARGB (0xFFRRGGBB) pixel.
    /// </summary>
    public static uint ConvertCramToArgb(byte cramColor)
    {
        return CramLut[cramColor & 63];
    }

    private void UpdateVdpScanlineEvents(int line, byte[] registers)
    {
        byte r10 = registers.Length > 10 ? registers[10] : (byte)0;

        if (line < DisplayHeight)
        {
            if (_lineCounter == 0)
            {
                _lineCounter = r10;
                Vdp.InjectLineInterrupt();
            }
            else
            {
                _lineCounter--;
            }
        }
        else
        {
            _lineCounter = r10;
        }

        if (line == DisplayHeight)
        {
            Vdp.InjectFrameInterrupt();
        }
    }
}
