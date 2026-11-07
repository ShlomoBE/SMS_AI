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
            bool biosEnabled = (MemoryControl & 0x04) == 0;
            bool cartEnabled = (MemoryControl & 0x08) == 0;

            if (biosEnabled && Bios != null && address < Bios.Length)
            {
                return Bios[address];
            }

            if (cartEnabled && Mapper != null)
            {
                if (Mapper.TryRead(address, out byte value))
                {
                    return value;
                }
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
            bool cartEnabled = (MemoryControl & 0x08) == 0;
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
        byte portLower = (byte)(port & 0xFF);
        return portLower switch
        {
            0x3E => MemoryControl,
            0x3F => Controllers.ReadPort(0x3F),
            0x7E => GetVCounter(),
            0x7F => CurrentHCounter,
            0xBE => Vdp.ReadData(),
            0xBF or 0xBD => Vdp.ReadStatus(),
            0xDC or 0xC0 => Controllers.ReadPort(0xDC),
            0xDD or 0xC1 => Controllers.ReadPort(0xDD),
            0xF2 => AudioMixer.Fm.AudioControl,
            _ => 0xFF
        };
    }

    /// <inheritdoc/>
    public void WriteIo(ushort port, byte value)
    {
        byte portLower = (byte)(port & 0xFF);
        switch (portLower)
        {
            case 0x3E:
                MemoryControl = value;
                break;
            case 0x3F:
                Controllers.WritePort(0x3F, value);
                break;
            case 0x7E:
            case 0x7F:
                AudioMixer.WritePort7F(value);
                break;
            case 0xBE:
                Vdp.WriteData(value);
                break;
            case 0xBF:
            case 0xBD:
                Vdp.WriteControl(value);
                break;
            case 0xF0:
                AudioMixer.WritePortF0(value);
                break;
            case 0xF1:
                AudioMixer.WritePortF1(value);
                break;
            case 0xF2:
                AudioMixer.WritePortF2(value);
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
