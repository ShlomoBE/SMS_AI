using Sms.Core.Audio;
using Sms.Core.Cartridge;
using Sms.Core.Cpu;
using Sms.Core.Input;
using Sms.Core.Vdp;

namespace Sms.Core.System;

/// <summary>
/// Z80 CPU memory and I/O bus implementation for the Sega Master System.
/// Routes memory access between BIOS, Cartridge Mapper, and System RAM,
/// and dispatches I/O port reads/writes to VDP, Audio, and Controller subsystems.
/// </summary>
public sealed class SmsSystemBus : IZ80Bus
{
    public const int RamSize = 0x2000; // 8 KiB System RAM

    /// <summary>
    /// Gets or sets the Port $3E Memory Control register value.
    /// Bit 2: BIOS enable (0 = enabled, 1 = disabled).
    /// Bit 3: Cartridge enable (0 = enabled, 1 = disabled).
    /// </summary>
    public byte MemoryControl { get; set; }

    /// <summary>
    /// Gets the 8 KiB System RAM buffer.
    /// </summary>
    public byte[] Ram { get; }

    /// <summary>
    /// Gets or sets the BIOS ROM buffer (or null if no BIOS loaded).
    /// </summary>
    public byte[]? Bios { get; set; }

    /// <summary>
    /// Gets or sets the cartridge SegaMapper (or null if no cartridge inserted).
    /// </summary>
    public SegaMapper? Mapper { get; set; }

    /// <summary>
    /// Gets or sets the SMS VDP ports interface.
    /// </summary>
    public VdpPorts Vdp { get; set; }

    /// <summary>
    /// Gets or sets the Sega controller ports interface.
    /// </summary>
    public SegaControllerPorts Controllers { get; set; }

    /// <summary>
    /// Gets or sets the SMS Audio Mixer interface.
    /// </summary>
    public SmsAudioMixer AudioMixer { get; set; }

    /// <summary>
    /// Gets or sets the active Video Standard (NTSC or PAL).
    /// </summary>
    public VideoStandard VideoStandard { get; set; } = VideoStandard.Ntsc;

    /// <summary>
    /// Gets or sets the current scanline index (0..261 for NTSC, 0..312 for PAL).
    /// </summary>
    public int CurrentScanline { get; set; }

    /// <summary>
    /// Gets or sets the current H-Counter byte value.
    /// </summary>
    public byte CurrentHCounter { get; set; }

    /// <summary>
    /// Initializes a new instance of <see cref="SmsSystemBus"/>.
    /// </summary>
    public SmsSystemBus(
        SegaMapper? mapper = null,
        byte[]? bios = null,
        VdpPorts? vdp = null,
        SegaControllerPorts? controllers = null,
        SmsAudioMixer? audioMixer = null)
    {
        Ram = new byte[RamSize];
        Mapper = mapper;
        Bios = bios;
        Vdp = vdp ?? VdpPorts.CreateDeterministicFixture(
            new byte[0x4000], new byte[32], new byte[11], 0, 0, false, 0, 0, false);
        Controllers = controllers ?? new SegaControllerPorts();
        AudioMixer = audioMixer ?? new SmsAudioMixer();
        MemoryControl = 0x00;
    }

    /// <inheritdoc/>
    public byte FetchOpcode(ushort address) => ReadMemory(address);

    /// <inheritdoc/>
    public byte ReadMemory(ushort address)
    {
        if (address < 0xC000)
        {
            // Charles MacDonald, IDocs/General.txt §4 (Port $3E Memory Control):
            // D6 (0x40) = Cartridge slot disable (0 = enabled, 1 = disabled)
            // D3 (0x08) = BIOS ROM disable (0 = enabled, 1 = disabled)
            // Cartridge takes precedence if both cartridge and BIOS are enabled.
            bool cartEnabled = (MemoryControl & 0x40) == 0;
            bool biosEnabled = (MemoryControl & 0x08) == 0;

            if (cartEnabled && Mapper != null)
            {
                if (Mapper.TryRead(address, out byte value))
                {
                    return value;
                }
            }

            if (biosEnabled && Bios != null && address < Bios.Length)
            {
                return Bios[address];
            }

            return 0xFF;
        }
        else
        {
            return Ram[address & (RamSize - 1)];
        }
    }

    /// <inheritdoc/>
    public void WriteMemory(ushort address, byte value)
    {
        if (address < 0xC000)
        {
            bool cartEnabled = (MemoryControl & 0x40) == 0;
            if (cartEnabled && Mapper != null)
            {
                Mapper.ObserveWrite(address, value);
            }
        }
        else
        {
            Ram[address & (RamSize - 1)] = value;
            if (address >= 0xFFFC && Mapper != null)
            {
                Mapper.ObserveWrite(address, value);
            }
        }
    }

    /// <inheritdoc/>
    public byte ReadIo(ushort port)
    {
        byte p = (byte)(port & 0xFF);

        // YM2413 FM Port overrides
        if (p == 0xF2) return AudioMixer.Fm.AudioControl;
        if (p is 0xF0 or 0xF1) return 0xFF;

        // Charles MacDonald, IDocs/General.txt §4 (I/O Port Map)
        return (p & 0xC0) switch
        {
            0x00 => (p & 1) == 0 ? MemoryControl : Controllers.ReadPort(0x3F),
            0x40 => (p & 1) == 0 ? GetVCounter() : CurrentHCounter,
            0x80 => (p & 1) == 0 ? Vdp.ReadData() : Vdp.ReadStatus(),
            0xC0 => (p & 1) == 0 ? Controllers.ReadPort(0xDC) : Controllers.ReadPort(0xDD),
            _ => 0xFF
        };
    }

    /// <inheritdoc/>
    public void WriteIo(ushort port, byte value)
    {
        byte p = (byte)(port & 0xFF);

        // YM2413 FM Port overrides
        if (p == 0xF0) { AudioMixer.WritePortF0(value); return; }
        if (p == 0xF1) { AudioMixer.WritePortF1(value); return; }
        if (p == 0xF2) { AudioMixer.WritePortF2(value); return; }

        // Charles MacDonald, IDocs/General.txt §4 (I/O Port Map)
        switch (p & 0xC0)
        {
            case 0x00:
                if ((p & 1) == 0) MemoryControl = value;
                else Controllers.WritePort(0x3F, value);
                break;
            case 0x40:
                AudioMixer.WritePort7F(value);
                break;
            case 0x80:
                if ((p & 1) == 0) Vdp.WriteData(value);
                else Vdp.WriteControl(value);
                break;
            case 0xC0:
                // Writes to C0-FF are unmapped / ignored on SMS
                break;
        }
    }

    /// <inheritdoc/>
    public byte AcknowledgeInterrupt() => 0xFF;

    /// <summary>
    /// Computes the V-Counter byte for the current scanline based on VideoStandard (NTSC or PAL).
    /// </summary>
    public byte GetVCounter()
    {
        int line = CurrentScanline;
        if (VideoStandard == VideoStandard.Ntsc)
        {
            // NTSC (262 lines): 0..218 -> 0..DA, 219..261 -> D5..FF
            return line <= 218 ? (byte)line : (byte)(line - 6);
        }
        else
        {
            // PAL (313 lines): 0..242 -> 0..F2, 243..312 -> BA..FF
            return line <= 242 ? (byte)line : (byte)(line - 57);
        }
    }
}
