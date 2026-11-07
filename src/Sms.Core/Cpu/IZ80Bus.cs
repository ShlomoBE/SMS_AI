namespace Sms.Core.Cpu;

/// <summary>Untimed, instruction-level bus boundary. Accesses may have side effects.</summary>
public interface IZ80Bus
{
    byte FetchOpcode(ushort address);
    byte ReadMemory(ushort address);
    void WriteMemory(ushort address, byte value);
    byte ReadIo(ushort port);
    void WriteIo(ushort port, byte value);
    byte AcknowledgeInterrupt();
}
