using Sms.Core.Cartridge;
using Sms.Core.Cpu;

namespace Sms.Core.Memory;

/// <summary>
/// Restricted untimed CPU adapter for the cartridge-enabled memory prototype.
/// I/O, interrupt acknowledge, BIOS and memory-control ports are not implemented.
/// </summary>
public sealed class SegaCpuBus(SegaMemoryBus memory) : IZ80Bus
{
    private readonly SegaMemoryBus memory = memory ?? throw new ArgumentNullException(nameof(memory));

    public byte FetchOpcode(ushort address) => memory.ReadMemory(address);
    public byte ReadMemory(ushort address) => memory.ReadMemory(address);

    public void WriteMemory(ushort address, byte value)
    {
        if (memory.WriteMemory(address, value) == MapperWriteResult.UnsupportedControlValue)
            throw new NotSupportedException($"Unsupported mapper control 0x{value:X2} at 0x{address:X4}; RAM writes are not rolled back.");
    }

    public byte ReadIo(ushort port) => throw new NotSupportedException("SMS I/O is not implemented.");
    public void WriteIo(ushort port, byte value) => throw new NotSupportedException("SMS I/O is not implemented.");
    public byte AcknowledgeInterrupt() => throw new NotSupportedException("Interrupt acknowledge is not implemented.");
}
