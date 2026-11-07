using System.Reflection;
using Sms.Core.Cpu;
using Sms.Core.System;
using Sms.Core.Vdp;

namespace Sms.Core.Debug;

/// <summary>
/// Immutable snapshot of Z80 CPU state for debugger inspection.
/// </summary>
public readonly record struct CpuSnapshot(
    ushort PC,
    ushort SP,
    byte A,
    byte F,
    byte B,
    byte C,
    byte D,
    byte E,
    byte H,
    byte L,
    ushort IX,
    ushort IY,
    ushort AFPrime,
    ushort BCPrime,
    ushort DEPrime,
    ushort HLPrime,
    byte I,
    byte R,
    bool IFF1,
    bool IFF2,
    byte IM,
    bool IsHalted,
    bool IsFaulted
)
{
    public byte APrime => (byte)(AFPrime >> 8);
    public byte FPrime => (byte)(AFPrime & 0xFF);
    public byte BPrime => (byte)(BCPrime >> 8);
    public byte CPrime => (byte)(BCPrime & 0xFF);
    public byte DPrime => (byte)(DEPrime >> 8);
    public byte EPrime => (byte)(DEPrime & 0xFF);
    public byte HPrime => (byte)(HLPrime >> 8);
    public byte LPrime => (byte)(HLPrime & 0xFF);

    public ushort ProgramCounter => PC;
    public ushort StackPointer => SP;
    public byte InterruptMode => IM;
}

/// <summary>
/// Immutable snapshot of SMS VDP state for debugger inspection.
/// </summary>
public readonly record struct VdpSnapshot(
    IReadOnlyList<byte> Vram,
    IReadOnlyList<byte> Cram,
    IReadOnlyList<byte> Registers,
    int Address,
    int Code,
    byte Status,
    bool LinePending
)
{
    public bool SecondControlByte { get; init; }
    public byte ReadBuffer { get; init; }
}

/// <summary>
/// Side-Effect-Free Emulation Debugger Engine and Execution Inspector.
/// Provides breakpoint, watchpoint, state inspection, and tile decoding utilities.
/// </summary>
public sealed class SmsDebugger
{
    private readonly HashSet<ushort> _pcBreakpoints = [];
    private readonly HashSet<ushort> _readWatchpoints = [];
    private readonly HashSet<ushort> _writeWatchpoints = [];
    private bool _watchpointHit;
    private ushort? _hitWatchpointAddress;

    public SmsSystem System { get; }

    public bool IsPaused { get; set; }
    public bool BreakpointHit { get; private set; }
    public ushort? HitAddress { get; private set; }

    public bool HasPcBreakpoint(ushort pc) => _pcBreakpoints.Contains(pc);
    public void ClearBreakpoints()
    {
        ClearPcBreakpoints();
        ClearMemoryWatchpoints();
    }

    public SmsDebugger(SmsSystem system)
    {
        System = system ?? throw new ArgumentNullException(nameof(system));
        EnsureDebugBusAttached();
    }

    private void EnsureDebugBusAttached()
    {
        var cpu = System.Cpu;
        if (cpu == null) return;

        FieldInfo? busField = typeof(Z80).GetField("_bus", BindingFlags.Instance | BindingFlags.NonPublic);
        object? currentBus = busField?.GetValue(cpu);

        if (currentBus is not DebuggingBus)
        {
            AttachDebugBus();
        }
    }

    private void AttachDebugBus()
    {
        var debugBus = new DebuggingBus(System.Bus, this);
        var currentCpu = System.Cpu;
        var newCpu = new Z80(debugBus);
        SyncCpuState(currentCpu, newCpu);

        PropertyInfo? cpuProp = typeof(SmsSystem).GetProperty(nameof(SmsSystem.Cpu));
        if (cpuProp != null && cpuProp.CanWrite)
        {
            cpuProp.SetValue(System, newCpu);
        }
        else
        {
            FieldInfo? field = typeof(SmsSystem).GetField("<Cpu>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic);
            field?.SetValue(System, newCpu);
        }
    }

    private static void SyncCpuState(Z80 src, Z80 dst)
    {
        if (src == null || dst == null) return;
        dst.ProgramCounter = src.ProgramCounter;
        dst.StackPointer = src.StackPointer;
        dst.A = src.A;
        dst.F = src.F;
        dst.B = src.B;
        dst.C = src.C;
        dst.D = src.D;
        dst.E = src.E;
        dst.H = src.H;
        dst.L = src.L;
        dst.IX = src.IX;
        dst.IY = src.IY;
        dst.APrime = src.APrime;
        dst.FPrime = src.FPrime;
        dst.BPrime = src.BPrime;
        dst.CPrime = src.CPrime;
        dst.DPrime = src.DPrime;
        dst.EPrime = src.EPrime;
        dst.HPrime = src.HPrime;
        dst.LPrime = src.LPrime;
        dst.I = src.I;
        dst.R = src.R;
        dst.IFF1 = src.IFF1;
        dst.IFF2 = src.IFF2;
        dst.PendingEi = src.PendingEi;
        dst.InterruptMode = src.InterruptMode;
        dst.IsHalted = src.IsHalted;
    }

    /// <summary>
    /// Reads a byte from memory without altering mapper or I/O status.
    /// </summary>
    public byte ReadMemoryByteSideEffectFree(ushort address)
    {
        return System.Bus.ReadMemory(address);
    }

    /// <summary>
    /// Returns an immutable snapshot of current Z80 CPU registers and status.
    /// </summary>
    public CpuSnapshot GetCpuSnapshot()
    {
        Z80 cpu = System.Cpu;
        return new CpuSnapshot(
            PC: cpu.ProgramCounter,
            SP: cpu.StackPointer,
            A: cpu.A,
            F: cpu.F,
            B: cpu.B,
            C: cpu.C,
            D: cpu.D,
            E: cpu.E,
            H: cpu.H,
            L: cpu.L,
            IX: cpu.IX,
            IY: cpu.IY,
            AFPrime: (ushort)((cpu.APrime << 8) | cpu.FPrime),
            BCPrime: (ushort)((cpu.BPrime << 8) | cpu.CPrime),
            DEPrime: (ushort)((cpu.DPrime << 8) | cpu.EPrime),
            HLPrime: (ushort)((cpu.HPrime << 8) | cpu.LPrime),
            I: cpu.I,
            R: cpu.R,
            IFF1: cpu.IFF1,
            IFF2: cpu.IFF2,
            IM: cpu.InterruptMode,
            IsHalted: cpu.IsHalted,
            IsFaulted: cpu.IsFaulted
        );
    }

    /// <summary>
    /// Returns an immutable snapshot of current VDP state.
    /// </summary>
    public VdpSnapshot GetVdpSnapshot()
    {
        var snap = System.Vdp.Inspect();
        return new VdpSnapshot(
            Vram: snap.Vram,
            Cram: snap.Cram,
            Registers: snap.Registers,
            Address: snap.Address,
            Code: snap.Code,
            Status: snap.Status,
            LinePending: snap.LinePending
        )
        {
            SecondControlByte = snap.SecondControlByte,
            ReadBuffer = snap.ReadBuffer
        };
    }

    /// <summary>
    /// Decodes all 512 8x8 Mode 4 tiles from VRAM into 32-bit ARGB 8x8 pixel color arrays.
    /// </summary>
    public uint[][] DecodeTilePatterns()
    {
        var snap = System.Vdp.Inspect();
        IReadOnlyList<byte> vram = snap.Vram;
        IReadOnlyList<byte> cram = snap.Cram;

        uint[][] tiles = new uint[512][];
        for (int t = 0; t < 512; t++)
        {
            uint[] tile = new uint[64];
            int tileOffset = t * 32;

            for (int y = 0; y < 8; y++)
            {
                int rowOffset = tileOffset + y * 4;
                byte b0 = rowOffset < vram.Count ? vram[rowOffset] : (byte)0;
                byte b1 = rowOffset + 1 < vram.Count ? vram[rowOffset + 1] : (byte)0;
                byte b2 = rowOffset + 2 < vram.Count ? vram[rowOffset + 2] : (byte)0;
                byte b3 = rowOffset + 3 < vram.Count ? vram[rowOffset + 3] : (byte)0;

                for (int x = 0; x < 8; x++)
                {
                    int shift = 7 - x;
                    int colorIdx = (((b3 >> shift) & 1) << 3)
                                 | (((b2 >> shift) & 1) << 2)
                                 | (((b1 >> shift) & 1) << 1)
                                 | ((b0 >> shift) & 1);

                    byte cramByte = colorIdx < cram.Count ? cram[colorIdx] : (byte)0;
                    tile[y * 8 + x] = SmsSystem.ConvertCramToArgb(cramByte);
                }
            }
            tiles[t] = tile;
        }
        return tiles;
    }

    public void AddPcBreakpoint(ushort pc) => _pcBreakpoints.Add(pc);
    public void RemovePcBreakpoint(ushort pc) => _pcBreakpoints.Remove(pc);
    public void ClearPcBreakpoints() => _pcBreakpoints.Clear();

    public void AddMemoryWatchpoint(ushort addr, bool write = true)
    {
        if (write) _writeWatchpoints.Add(addr);
        else _readWatchpoints.Add(addr);
    }

    public void RemoveMemoryWatchpoint(ushort addr, bool write = true)
    {
        if (write) _writeWatchpoints.Remove(addr);
        else _readWatchpoints.Remove(addr);
    }

    public void ClearMemoryWatchpoints()
    {
        _readWatchpoints.Clear();
        _writeWatchpoints.Clear();
    }

    /// <summary>
    /// Executes a single CPU instruction, triggering any watchpoints hit.
    /// </summary>
    public int StepInstruction()
    {
        EnsureDebugBusAttached();
        BreakpointHit = false;
        HitAddress = null;
        _watchpointHit = false;
        _hitWatchpointAddress = null;

        int tStates = System.StepInstruction();

        if (_watchpointHit)
        {
            BreakpointHit = true;
            HitAddress = _hitWatchpointAddress;
        }

        return tStates;
    }

    /// <summary>
    /// Steps instructions for one scanline period.
    /// </summary>
    public void StepScanline()
    {
        StepScanlineWithBreakpoints(out _);
    }

    /// <summary>
    /// Steps instructions for one scanline, checking for breakpoints.
    /// </summary>
    public bool StepScanlineWithBreakpoints(out bool breakpointHit)
    {
        EnsureDebugBusAttached();
        breakpointHit = false;
        const int tStatesPerLine = 76;
        int executed = 0;

        while (executed < tStatesPerLine)
        {
            ushort currentPc = System.Cpu.ProgramCounter;
            if (_pcBreakpoints.Contains(currentPc))
            {
                BreakpointHit = true;
                HitAddress = currentPc;
                breakpointHit = true;
                return true;
            }

            int stepT = StepInstruction();
            executed += stepT;

            if (BreakpointHit)
            {
                breakpointHit = true;
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Runs frame instructions until frame completes or a breakpoint is encountered.
    /// </summary>
    public void StepFrameWithBreakpoints(out bool breakpointHit)
    {
        EnsureDebugBusAttached();
        breakpointHit = false;
        int totalScanlines = System.VideoStandard == VideoStandard.Ntsc ? 262 : 313;

        for (int line = 0; line < totalScanlines; line++)
        {
            if (StepScanlineWithBreakpoints(out breakpointHit))
            {
                return;
            }
        }
    }

    internal void NotifyMemoryRead(ushort address)
    {
        if (_readWatchpoints.Contains(address))
        {
            _watchpointHit = true;
            _hitWatchpointAddress = address;
        }
    }

    internal void NotifyMemoryWrite(ushort address)
    {
        if (_writeWatchpoints.Contains(address))
        {
            _watchpointHit = true;
            _hitWatchpointAddress = address;
        }
    }

    private sealed class DebuggingBus : IZ80Bus
    {
        private readonly SmsSystemBus _inner;
        private readonly SmsDebugger _debugger;

        public DebuggingBus(SmsSystemBus inner, SmsDebugger debugger)
        {
            _inner = inner;
            _debugger = debugger;
        }

        public byte FetchOpcode(ushort address) => _inner.FetchOpcode(address);

        public byte ReadMemory(ushort address)
        {
            _debugger.NotifyMemoryRead(address);
            return _inner.ReadMemory(address);
        }

        public void WriteMemory(ushort address, byte value)
        {
            _debugger.NotifyMemoryWrite(address);
            _inner.WriteMemory(address, value);
        }

        public byte ReadIo(ushort port) => _inner.ReadIo(port);
        public void WriteIo(ushort port, byte value) => _inner.WriteIo(port, value);
        public byte AcknowledgeInterrupt() => _inner.AcknowledgeInterrupt();
    }
}
