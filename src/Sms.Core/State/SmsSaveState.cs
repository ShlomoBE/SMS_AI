using System;
using System.Reflection;
using Sms.Core.Audio;
using Sms.Core.Cartridge;
using Sms.Core.Cpu;
using Sms.Core.Fm;
using Sms.Core.Input;
using Sms.Core.Psg;
using Sms.Core.System;
using Sms.Core.Timing;
using Sms.Core.Vdp;

namespace Sms.Core.State;

/// <summary>
/// Snapshot container representing the operator state of an FM voice channel.
/// </summary>
public struct FmOperatorState
{
    public byte State;
    public double AttenuationDb;
    public uint PhaseAccumulator;
    public int OutputPrev1;
    public int OutputPrev2;
}

/// <summary>
/// Versioned Save State container encapsulating total Sega Master System console state.
/// Holds CPU, RAM, Cartridge Mapper, VDP, PSG, YM2413 FM, Master Scheduler, System Bus, and Input Controller states.
/// </summary>
public sealed class SmsSaveState
{
    public uint Version { get; set; } = SmsStateSerializer.CurrentVersion;
    public YM2413Snapshot? FmSnapshot { get; set; }
    public byte[] RomHash { get; set; } = Array.Empty<byte>();
    public byte[] BiosHash { get; set; } = Array.Empty<byte>();
    public SegaMapperProfile MapperProfile { get; set; }

    // CPU State
    public ushort CpuPc { get; set; }
    public ushort CpuSp { get; set; }
    public byte CpuA { get; set; }
    public byte CpuF { get; set; }
    public byte CpuB { get; set; }
    public byte CpuC { get; set; }
    public byte CpuD { get; set; }
    public byte CpuE { get; set; }
    public byte CpuH { get; set; }
    public byte CpuL { get; set; }
    public ushort CpuIx { get; set; }
    public ushort CpuIy { get; set; }
    public byte CpuAPrime { get; set; }
    public byte CpuFPrime { get; set; }
    public byte CpuBPrime { get; set; }
    public byte CpuCPrime { get; set; }
    public byte CpuDPrime { get; set; }
    public byte CpuEPrime { get; set; }
    public byte CpuHPrime { get; set; }
    public byte CpuLPrime { get; set; }
    public byte CpuI { get; set; }
    public byte CpuR { get; set; }
    public bool CpuIff1 { get; set; }
    public bool CpuIff2 { get; set; }
    public byte CpuInterruptMode { get; set; }
    public bool CpuPendingEi { get; set; }
    public bool CpuIsHalted { get; set; }
    public bool CpuIsFaulted { get; set; }
    public ushort CpuMemoryPointer { get; set; }

    // System RAM State (8192 bytes)
    public byte[] Ram { get; set; } = Array.Empty<byte>();

    // Cartridge Mapper State
    public bool HasMapper { get; set; }
    public byte MapperSlot0 { get; set; }
    public byte MapperSlot1 { get; set; }
    public byte MapperSlot2 { get; set; }
    public byte MapperRamControl { get; set; }
    public byte[] MapperSram { get; set; } = Array.Empty<byte>();

    // VDP State
    public byte[] VdpVram { get; set; } = Array.Empty<byte>();
    public byte[] VdpCram { get; set; } = Array.Empty<byte>();
    public byte[] VdpRegisters { get; set; } = Array.Empty<byte>();
    public int VdpAddress { get; set; }
    public int VdpCode { get; set; }
    public bool VdpSecondControlByte { get; set; }
    public byte VdpReadBuffer { get; set; }
    public byte VdpStatus { get; set; }
    public bool VdpLinePending { get; set; }
    public byte LineCounter { get; set; }
    public int TimingCarryTStates { get; set; }

    // PSG State
    public ushort[] PsgTonePeriods { get; set; } = new ushort[3];
    public byte[] PsgAttenuations { get; set; } = new byte[4];
    public byte PsgNoiseControl { get; set; }
    public ushort PsgLfsr { get; set; }
    public int PsgNoiseCounter { get; set; }
    public int[] PsgToneCounters { get; set; } = new int[3];
    public bool[] PsgToneOutputs { get; set; } = new bool[3];
    public byte PsgLatchedRegister { get; set; }
    public int PsgDividerPhase { get; set; }

    // YM2413 FM State
    public byte FmLatchedAddress { get; set; }
    public byte[] FmRegisters { get; set; } = Array.Empty<byte>();
    public byte[] FmCustomPatch { get; set; } = Array.Empty<byte>();
    public byte FmAudioControl { get; set; }
    public byte FmRhythmControl { get; set; }
    public ushort[] FmFNumbers { get; set; } = new ushort[9];
    public byte[] FmBlocks { get; set; } = new byte[9];
    public bool[] FmKeyOn { get; set; } = new bool[9];
    public bool[] FmSustain { get; set; } = new bool[9];
    public byte[] FmPatches { get; set; } = new byte[9];
    public byte[] FmVolumes { get; set; } = new byte[9];
    public bool FmRhythmEnable { get; set; }
    public bool FmRhythmBassDrumOn { get; set; }
    public bool FmRhythmSnareDrumOn { get; set; }
    public bool FmRhythmTomTomOn { get; set; }
    public bool FmRhythmTopCymbalOn { get; set; }
    public bool FmRhythmHiHatOn { get; set; }
    public uint FmNoiseLfsr { get; set; }
    public FmOperatorState[] FmOperators { get; set; } = Array.Empty<FmOperatorState>();
    public FmOperatorState[] FmRhythmOperators { get; set; } = Array.Empty<FmOperatorState>();

    // Scheduler State
    public ulong SchedulerMasterTicks { get; set; }
    public uint SchedulerCpuPhase { get; set; }
    public uint SchedulerVdpPhase { get; set; }
    public uint SchedulerPsgPhase { get; set; }

    // System Bus State
    public byte BusMemoryControl { get; set; }
    public VideoStandard BusVideoStandard { get; set; }
    public int BusCurrentScanline { get; set; }
    public byte BusCurrentHCounter { get; set; }

    // Controller State
    public byte Joypad1State { get; set; }
    public byte Joypad2State { get; set; }
    public byte IoControl { get; set; }
    public bool ResetPressed { get; set; }
    public bool PausePressed { get; set; }
    public bool PauseNmiPending { get; set; }
    public bool Joypad1ThInput { get; set; }
    public bool Joypad2ThInput { get; set; }

    // Reflection handles for private hardware latches
    private static readonly FieldInfo Z80MemoryPointerField = typeof(Z80).GetField("_memoryPointer", BindingFlags.Instance | BindingFlags.NonPublic)!;
    private static readonly PropertyInfo Z80IsFaultedProperty = typeof(Z80).GetProperty("IsFaulted", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!;

    private static readonly FieldInfo LineCounterField = typeof(SmsSystem).GetField("_lineCounter", BindingFlags.Instance | BindingFlags.NonPublic)!;
    private static readonly FieldInfo TimingCarryField = typeof(SmsSystem).GetField("_timingCarryTStates", BindingFlags.Instance | BindingFlags.NonPublic)!;

    private static readonly FieldInfo PsgTonePeriodField = typeof(SN76489Psg).GetField("tonePeriod", BindingFlags.Instance | BindingFlags.NonPublic)!;
    private static readonly FieldInfo PsgToneCounterField = typeof(SN76489Psg).GetField("toneCounter", BindingFlags.Instance | BindingFlags.NonPublic)!;
    private static readonly FieldInfo PsgToneOutputHighField = typeof(SN76489Psg).GetField("toneOutputHigh", BindingFlags.Instance | BindingFlags.NonPublic)!;
    private static readonly FieldInfo PsgAttenuationField = typeof(SN76489Psg).GetField("attenuation", BindingFlags.Instance | BindingFlags.NonPublic)!;
    private static readonly FieldInfo PsgLatchedRegisterField = typeof(SN76489Psg).GetField("latchedRegister", BindingFlags.Instance | BindingFlags.NonPublic)!;
    private static readonly FieldInfo PsgNoiseControlField = typeof(SN76489Psg).GetField("noiseControl", BindingFlags.Instance | BindingFlags.NonPublic)!;
    private static readonly FieldInfo PsgLfsrField = typeof(SN76489Psg).GetField("lfsr", BindingFlags.Instance | BindingFlags.NonPublic)!;
    private static readonly FieldInfo PsgNoiseCounterField = typeof(SN76489Psg).GetField("noiseCounter", BindingFlags.Instance | BindingFlags.NonPublic)!;
    private static readonly FieldInfo PsgDividerPhaseField = typeof(SN76489Psg).GetField("dividerPhase", BindingFlags.Instance | BindingFlags.NonPublic)!;

    private static readonly FieldInfo FmLatchedAddressField = typeof(YM2413Fm).GetField("_latchedAddress", BindingFlags.Instance | BindingFlags.NonPublic)!;
    private static readonly FieldInfo FmAudioControlField = typeof(YM2413Fm).GetField("_audioControl", BindingFlags.Instance | BindingFlags.NonPublic)!;
    private static readonly FieldInfo FmRegistersField = typeof(YM2413Fm).GetField("_registers", BindingFlags.Instance | BindingFlags.NonPublic)!;
    private static readonly FieldInfo FmCustomPatchRegsField = typeof(YM2413Fm).GetField("_customPatchRegs", BindingFlags.Instance | BindingFlags.NonPublic)!;
    private static readonly FieldInfo FmFNumberField = typeof(YM2413Fm).GetField("_fNumber", BindingFlags.Instance | BindingFlags.NonPublic)!;
    private static readonly FieldInfo FmBlockField = typeof(YM2413Fm).GetField("_block", BindingFlags.Instance | BindingFlags.NonPublic)!;
    private static readonly FieldInfo FmKeyOnField = typeof(YM2413Fm).GetField("_keyOn", BindingFlags.Instance | BindingFlags.NonPublic)!;
    private static readonly FieldInfo FmSustainField = typeof(YM2413Fm).GetField("_sustain", BindingFlags.Instance | BindingFlags.NonPublic)!;
    private static readonly FieldInfo FmPatchIndexField = typeof(YM2413Fm).GetField("_patchIndex", BindingFlags.Instance | BindingFlags.NonPublic)!;
    private static readonly FieldInfo FmVolumeField = typeof(YM2413Fm).GetField("_volume", BindingFlags.Instance | BindingFlags.NonPublic)!;
    private static readonly FieldInfo FmOperatorsField = typeof(YM2413Fm).GetField("_operators", BindingFlags.Instance | BindingFlags.NonPublic)!;
    private static readonly FieldInfo FmRhythmEnableField = typeof(YM2413Fm).GetField("_rhythmEnable", BindingFlags.Instance | BindingFlags.NonPublic)!;
    private static readonly FieldInfo FmBassDrumOnField = typeof(YM2413Fm).GetField("_bassDrumOn", BindingFlags.Instance | BindingFlags.NonPublic)!;
    private static readonly FieldInfo FmSnareDrumOnField = typeof(YM2413Fm).GetField("_snareDrumOn", BindingFlags.Instance | BindingFlags.NonPublic)!;
    private static readonly FieldInfo FmTomTomOnField = typeof(YM2413Fm).GetField("_tomTomOn", BindingFlags.Instance | BindingFlags.NonPublic)!;
    private static readonly FieldInfo FmTopCymbalOnField = typeof(YM2413Fm).GetField("_topCymbalOn", BindingFlags.Instance | BindingFlags.NonPublic)!;
    private static readonly FieldInfo FmHiHatOnField = typeof(YM2413Fm).GetField("_hiHatOn", BindingFlags.Instance | BindingFlags.NonPublic)!;
    private static readonly FieldInfo FmBdModField = typeof(YM2413Fm).GetField("_bdMod", BindingFlags.Instance | BindingFlags.NonPublic)!;
    private static readonly FieldInfo FmBdCarField = typeof(YM2413Fm).GetField("_bdCar", BindingFlags.Instance | BindingFlags.NonPublic)!;
    private static readonly FieldInfo FmSdCarField = typeof(YM2413Fm).GetField("_sdCar", BindingFlags.Instance | BindingFlags.NonPublic)!;
    private static readonly FieldInfo FmHhModField = typeof(YM2413Fm).GetField("_hhMod", BindingFlags.Instance | BindingFlags.NonPublic)!;
    private static readonly FieldInfo FmTomCarField = typeof(YM2413Fm).GetField("_tomCar", BindingFlags.Instance | BindingFlags.NonPublic)!;
    private static readonly FieldInfo FmCymModField = typeof(YM2413Fm).GetField("_cymMod", BindingFlags.Instance | BindingFlags.NonPublic)!;
    private static readonly FieldInfo FmNoiseLfsrField = typeof(YM2413Fm).GetField("_noiseLfsr", BindingFlags.Instance | BindingFlags.NonPublic)!;

    private static readonly FieldInfo Joypad1StateField = typeof(SegaControllerPorts).GetField("_joypad1State", BindingFlags.Instance | BindingFlags.NonPublic)!;
    private static readonly FieldInfo Joypad2StateField = typeof(SegaControllerPorts).GetField("_joypad2State", BindingFlags.Instance | BindingFlags.NonPublic)!;
    private static readonly FieldInfo IoControlField = typeof(SegaControllerPorts).GetField("_ioControl", BindingFlags.Instance | BindingFlags.NonPublic)!;
    private static readonly FieldInfo ResetPressedField = typeof(SegaControllerPorts).GetField("_resetPressed", BindingFlags.Instance | BindingFlags.NonPublic)!;
    private static readonly FieldInfo PausePressedField = typeof(SegaControllerPorts).GetField("_pausePressed", BindingFlags.Instance | BindingFlags.NonPublic)!;
    private static readonly FieldInfo PauseNmiPendingField = typeof(SegaControllerPorts).GetField("_pauseNmiPending", BindingFlags.Instance | BindingFlags.NonPublic)!;
    private static readonly FieldInfo Joypad1ThInputField = typeof(SegaControllerPorts).GetField("_joypad1ThInput", BindingFlags.Instance | BindingFlags.NonPublic)!;
    private static readonly FieldInfo Joypad2ThInputField = typeof(SegaControllerPorts).GetField("_joypad2ThInput", BindingFlags.Instance | BindingFlags.NonPublic)!;

    /// <summary>
    /// Captures a complete snapshot of system state from an active <see cref="SmsSystem"/>.
    /// </summary>
    public static SmsSaveState FromSystem(SmsSystem system)
    {
        ArgumentNullException.ThrowIfNull(system);

        var state = new SmsSaveState();

        // CPU
        var cpu = system.Cpu;
        state.CpuPc = cpu.ProgramCounter;
        state.CpuSp = cpu.StackPointer;
        state.CpuA = cpu.A;
        state.CpuF = cpu.F;
        state.CpuB = cpu.B;
        state.CpuC = cpu.C;
        state.CpuD = cpu.D;
        state.CpuE = cpu.E;
        state.CpuH = cpu.H;
        state.CpuL = cpu.L;
        state.CpuIx = cpu.IX;
        state.CpuIy = cpu.IY;
        state.CpuAPrime = cpu.APrime;
        state.CpuFPrime = cpu.FPrime;
        state.CpuBPrime = cpu.BPrime;
        state.CpuCPrime = cpu.CPrime;
        state.CpuDPrime = cpu.DPrime;
        state.CpuEPrime = cpu.EPrime;
        state.CpuHPrime = cpu.HPrime;
        state.CpuLPrime = cpu.LPrime;
        state.CpuI = cpu.I;
        state.CpuR = cpu.R;
        state.CpuIff1 = cpu.IFF1;
        state.CpuIff2 = cpu.IFF2;
        state.CpuInterruptMode = cpu.InterruptMode;
        state.CpuPendingEi = cpu.PendingEi;
        state.CpuIsHalted = cpu.IsHalted;
        state.CpuIsFaulted = cpu.IsFaulted;
        state.CpuMemoryPointer = (ushort)(Z80MemoryPointerField?.GetValue(cpu) ?? (ushort)0);

        // System RAM
        state.Ram = system.Ram.ToArray();

        // Cartridge Mapper
        if (system.Mapper != null)
        {
            state.HasMapper = true;
            var regs = system.Mapper.Registers;
            state.MapperSlot0 = regs.Slot0;
            state.MapperSlot1 = regs.Slot1;
            state.MapperSlot2 = regs.Slot2;
            state.MapperRamControl = regs.Control;
            state.MapperSram = system.Mapper.ExportSram();
        }

        // VDP
        var snapshot = system.Vdp.Inspect();
        state.VdpVram = snapshot.Vram.ToArray();
        state.VdpCram = snapshot.Cram.ToArray();
        state.VdpRegisters = snapshot.Registers.ToArray();
        state.VdpAddress = snapshot.Address;
        state.VdpCode = snapshot.Code;
        state.VdpSecondControlByte = snapshot.SecondControlByte;
        state.VdpReadBuffer = snapshot.ReadBuffer;
        state.VdpStatus = snapshot.Status;
        state.VdpLinePending = snapshot.LinePending;
        state.LineCounter = (byte)(LineCounterField?.GetValue(system) ?? (byte)0);
        state.TimingCarryTStates = (int)(TimingCarryField?.GetValue(system) ?? 0);

        // PSG
        var psg = system.AudioMixer.Psg;
        state.PsgTonePeriods = new ushort[3] { psg.GetTonePeriod(0), psg.GetTonePeriod(1), psg.GetTonePeriod(2) };
        state.PsgAttenuations = new byte[4] { psg.GetAttenuation(0), psg.GetAttenuation(1), psg.GetAttenuation(2), psg.GetAttenuation(3) };
        state.PsgNoiseControl = psg.NoiseControl;
        state.PsgLfsr = psg.Lfsr;
        state.PsgLatchedRegister = psg.LatchedRegister;
        state.PsgNoiseCounter = (int)(PsgNoiseCounterField?.GetValue(psg) ?? 1);
        state.PsgToneCounters = new int[3] { psg.GetToneCounter(0), psg.GetToneCounter(1), psg.GetToneCounter(2) };
        state.PsgToneOutputs = new bool[3] { psg.GetToneOutputHigh(0), psg.GetToneOutputHigh(1), psg.GetToneOutputHigh(2) };
        state.PsgDividerPhase = (int)(PsgDividerPhaseField?.GetValue(psg) ?? 0);

        // YM2413 FM
        state.RomHash = system.Mapper?.ComputeRomHash() ?? Array.Empty<byte>();
        state.BiosHash = system.Bus.Bios is { } bios ? global::System.Security.Cryptography.SHA256.HashData(bios) : Array.Empty<byte>();
        if (system.Mapper != null) state.MapperProfile = system.Mapper.Profile;
        var fm = system.AudioMixer.Fm;
        state.FmSnapshot = fm.CaptureSnapshot();
        state.FmLatchedAddress = fm.LatchedAddress;
        state.FmAudioControl = fm.AudioControl;
        state.FmRegisters = FmRegistersField?.GetValue(fm) is byte[] fmRegs ? (byte[])fmRegs.Clone() : new byte[256];
        state.FmCustomPatch = fm.GetCustomPatchRegisters();
        state.FmRhythmControl = fm.ReadRegister(0x0E);
        for (int i = 0; i < 9; i++)
        {
            state.FmFNumbers[i] = fm.GetFNumber(i);
            state.FmBlocks[i] = fm.GetBlock(i);
            state.FmKeyOn[i] = fm.GetKeyOn(i);
            state.FmSustain[i] = fm.GetSustain(i);
            state.FmPatches[i] = fm.GetPatchIndex(i);
            state.FmVolumes[i] = fm.GetVolume(i);
        }
        state.FmRhythmEnable = fm.RhythmEnable;
        state.FmRhythmBassDrumOn = fm.RhythmBassDrumOn;
        state.FmRhythmSnareDrumOn = fm.RhythmSnareDrumOn;
        state.FmRhythmTomTomOn = fm.RhythmTomTomOn;
        state.FmRhythmTopCymbalOn = fm.RhythmTopCymbalOn;
        state.FmRhythmHiHatOn = fm.RhythmHiHatOn;
        state.FmNoiseLfsr = (uint)(FmNoiseLfsrField?.GetValue(fm) ?? 1u);

        if (FmOperatorsField?.GetValue(fm) is OperatorEngine[,] ops)
        {
            state.FmOperators = new FmOperatorState[18];
            for (int ch = 0; ch < 9; ch++)
            {
                for (int op = 0; op < 2; op++)
                {
                    var o = ops[ch, op];
                    state.FmOperators[ch * 2 + op] = new FmOperatorState
                    {
                        State = (byte)o.State,
                        AttenuationDb = o.AttenuationDb,
                        PhaseAccumulator = o.PhaseAccumulator,
                        OutputPrev1 = o.OutputPrev1,
                        OutputPrev2 = o.OutputPrev2
                    };
                }
            }
        }

        var rhythmOps = new (FieldInfo? field, int index)[]
        {
            (FmBdModField, 0), (FmBdCarField, 1), (FmSdCarField, 2),
            (FmHhModField, 3), (FmTomCarField, 4), (FmCymModField, 5)
        };
        state.FmRhythmOperators = new FmOperatorState[6];
        foreach (var (field, idx) in rhythmOps)
        {
            if (field?.GetValue(fm) is OperatorEngine o)
            {
                state.FmRhythmOperators[idx] = new FmOperatorState
                {
                    State = (byte)o.State,
                    AttenuationDb = o.AttenuationDb,
                    PhaseAccumulator = o.PhaseAccumulator,
                    OutputPrev1 = o.OutputPrev1,
                    OutputPrev2 = o.OutputPrev2
                };
            }
        }

        // Scheduler
        var sched = system.Scheduler;
        state.SchedulerMasterTicks = sched.MasterTicks;
        state.SchedulerCpuPhase = sched.CpuPhase;
        state.SchedulerVdpPhase = sched.VdpPhase;
        state.SchedulerPsgPhase = sched.PsgPhase;

        // Bus
        state.BusMemoryControl = system.Bus.MemoryControl;
        state.BusVideoStandard = system.VideoStandard;
        state.BusCurrentScanline = system.Bus.CurrentScanline;
        state.BusCurrentHCounter = system.Bus.CurrentHCounter;

        // Controllers
        var ctrl = system.Controllers;
        state.Joypad1State = (byte)(Joypad1StateField?.GetValue(ctrl) ?? 0x3F);
        state.Joypad2State = (byte)(Joypad2StateField?.GetValue(ctrl) ?? 0x3F);
        state.IoControl = (byte)(IoControlField?.GetValue(ctrl) ?? 0xFF);
        state.ResetPressed = (bool)(ResetPressedField?.GetValue(ctrl) ?? false);
        state.PausePressed = (bool)(PausePressedField?.GetValue(ctrl) ?? false);
        state.PauseNmiPending = (bool)(PauseNmiPendingField?.GetValue(ctrl) ?? false);
        state.Joypad1ThInput = (bool)(Joypad1ThInputField?.GetValue(ctrl) ?? true);
        state.Joypad2ThInput = (bool)(Joypad2ThInputField?.GetValue(ctrl) ?? true);

        return state;
    }

    /// <summary>
    /// Validates captured save state consistency against running system.
    /// </summary>
    public bool Validate(SmsSystem system, out string? errorMessage)
    {
        errorMessage = null;

        if (Version != SmsStateSerializer.CurrentVersion)
        {
            errorMessage = $"Unsupported save state version {Version}. Expected version {SmsStateSerializer.CurrentVersion}.";
            return false;
        }

        if (!YM2413Fm.ValidateSnapshot(FmSnapshot))
        {
            errorMessage = "Invalid FM snapshot.";
            return false;
        }

        if (HasMapper != (system.Mapper != null) || (HasMapper &&
            (MapperProfile != system.Mapper!.Profile || !RomHash.AsSpan().SequenceEqual(system.Mapper.ComputeRomHash()))))
        {
            errorMessage = "Save state cartridge ROM or mapper profile does not match the loaded cartridge.";
            return false;
        }

        byte[] currentBiosHash = system.Bus.Bios is { } bios ? global::System.Security.Cryptography.SHA256.HashData(bios) : Array.Empty<byte>();
        if (BiosHash == null || !BiosHash.AsSpan().SequenceEqual(currentBiosHash))
        {
            errorMessage = "Save state BIOS does not match the loaded BIOS.";
            return false;
        }

        if (SchedulerCpuPhase >= MasterScheduler.CpuDivisor || SchedulerVdpPhase >= MasterScheduler.VdpDivisor ||
            SchedulerPsgPhase >= MasterScheduler.PsgDivisor)
        {
            errorMessage = "Invalid scheduler phase.";
            return false;
        }

        if (Ram == null || Ram.Length != SmsSystemBus.RamSize)
        {
            errorMessage = $"System RAM length mismatch. Expected {SmsSystemBus.RamSize} bytes, got {Ram?.Length ?? 0}.";
            return false;
        }

        if (VdpVram == null || VdpVram.Length != 0x4000)
        {
            errorMessage = $"VDP VRAM length mismatch. Expected 16384 bytes, got {VdpVram?.Length ?? 0}.";
            return false;
        }

        if (VdpCram == null || VdpCram.Length != 32)
        {
            errorMessage = $"VDP CRAM length mismatch. Expected 32 bytes, got {VdpCram?.Length ?? 0}.";
            return false;
        }

        if (VdpRegisters == null || VdpRegisters.Length != 11)
        {
            errorMessage = $"VDP Registers length mismatch. Expected 11 bytes, got {VdpRegisters?.Length ?? 0}.";
            return false;
        }

        if ((uint)VdpAddress > 0x3fff || (uint)VdpCode > 3 || Array.Exists(VdpCram, value => value > 0x3f))
        {
            errorMessage = "Invalid VDP state.";
            return false;
        }

        if (!Enum.IsDefined(typeof(VideoStandard), BusVideoStandard))
        {
            errorMessage = $"Invalid VideoStandard value {(int)BusVideoStandard}.";
            return false;
        }

        if (system.Mapper != null && HasMapper)
        {
            if (system.Mapper.SramSize != MapperSram.Length)
            {
                errorMessage = $"Cartridge SRAM size mismatch ({MapperSram.Length} vs expected {system.Mapper.SramSize}).";
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Restores captured snapshot onto the target <see cref="SmsSystem"/>.
    /// </summary>
    public void ApplyTo(SmsSystem system)
    {
        ArgumentNullException.ThrowIfNull(system);

        // CPU
        var cpu = system.Cpu;
        cpu.ProgramCounter = CpuPc;
        cpu.StackPointer = CpuSp;
        cpu.A = CpuA;
        cpu.F = CpuF;
        cpu.B = CpuB;
        cpu.C = CpuC;
        cpu.D = CpuD;
        cpu.E = CpuE;
        cpu.H = CpuH;
        cpu.L = CpuL;
        cpu.IX = CpuIx;
        cpu.IY = CpuIy;
        cpu.APrime = CpuAPrime;
        cpu.FPrime = CpuFPrime;
        cpu.BPrime = CpuBPrime;
        cpu.CPrime = CpuCPrime;
        cpu.DPrime = CpuDPrime;
        cpu.EPrime = CpuEPrime;
        cpu.HPrime = CpuHPrime;
        cpu.LPrime = CpuLPrime;
        cpu.I = CpuI;
        cpu.R = CpuR;
        cpu.IFF1 = CpuIff1;
        cpu.IFF2 = CpuIff2;
        cpu.InterruptMode = CpuInterruptMode;
        cpu.PendingEi = CpuPendingEi;
        cpu.IsHalted = CpuIsHalted;
        Z80IsFaultedProperty?.SetValue(cpu, CpuIsFaulted);
        Z80MemoryPointerField?.SetValue(cpu, CpuMemoryPointer);

        // RAM
        if (Ram.Length == system.Ram.Length)
        {
            Ram.CopyTo(system.Ram, 0);
        }

        // Mapper
        if (system.Mapper != null && HasMapper)
        {
            system.Mapper.ObserveWrite(0xFFFD, MapperSlot0);
            system.Mapper.ObserveWrite(0xFFFE, MapperSlot1);
            system.Mapper.ObserveWrite(0xFFFF, MapperSlot2);
            system.Mapper.ObserveWrite(0xFFFC, MapperRamControl);
            if (MapperSram.Length > 0 && system.Mapper.SramSize == MapperSram.Length)
            {
                system.Mapper.ImportSram(MapperSram);
            }
        }

        // VDP
        system.Bus.Vdp = VdpPorts.CreateDeterministicFixture(
            VdpVram, VdpCram, VdpRegisters, VdpAddress, VdpCode, VdpSecondControlByte, VdpReadBuffer, VdpStatus, VdpLinePending);
        LineCounterField?.SetValue(system, LineCounter);
        TimingCarryField?.SetValue(system, TimingCarryTStates);

        // PSG
        var psg = system.AudioMixer.Psg;
        PsgTonePeriodField?.SetValue(psg, (ushort[])PsgTonePeriods.Clone());
        PsgToneCounterField?.SetValue(psg, (int[])PsgToneCounters.Clone());
        PsgToneOutputHighField?.SetValue(psg, (bool[])PsgToneOutputs.Clone());
        PsgAttenuationField?.SetValue(psg, (byte[])PsgAttenuations.Clone());
        PsgLatchedRegisterField?.SetValue(psg, PsgLatchedRegister);
        PsgNoiseControlField?.SetValue(psg, PsgNoiseControl);
        PsgLfsrField?.SetValue(psg, PsgLfsr);
        PsgNoiseCounterField?.SetValue(psg, PsgNoiseCounter);
        PsgDividerPhaseField?.SetValue(psg, PsgDividerPhase);

        // FM
        var fm = system.AudioMixer.Fm;
        FmLatchedAddressField?.SetValue(fm, FmLatchedAddress);
        FmAudioControlField?.SetValue(fm, FmAudioControl);
        if (FmRegistersField?.GetValue(fm) is byte[] fmRegs && FmRegisters.Length <= fmRegs.Length)
        {
            FmRegisters.CopyTo(fmRegs, 0);
        }
        if (FmCustomPatchRegsField?.GetValue(fm) is byte[] customPatchRegs && FmCustomPatch.Length == customPatchRegs.Length)
        {
            FmCustomPatch.CopyTo(customPatchRegs, 0);
        }
        FmFNumberField?.SetValue(fm, (ushort[])FmFNumbers.Clone());
        FmBlockField?.SetValue(fm, (byte[])FmBlocks.Clone());
        FmKeyOnField?.SetValue(fm, (bool[])FmKeyOn.Clone());
        FmSustainField?.SetValue(fm, (bool[])FmSustain.Clone());
        FmPatchIndexField?.SetValue(fm, (byte[])FmPatches.Clone());
        FmVolumeField?.SetValue(fm, (byte[])FmVolumes.Clone());
        FmRhythmEnableField?.SetValue(fm, FmRhythmEnable);
        FmBassDrumOnField?.SetValue(fm, FmRhythmBassDrumOn);
        FmSnareDrumOnField?.SetValue(fm, FmRhythmSnareDrumOn);
        FmTomTomOnField?.SetValue(fm, FmRhythmTomTomOn);
        FmTopCymbalOnField?.SetValue(fm, FmRhythmTopCymbalOn);
        FmHiHatOnField?.SetValue(fm, FmRhythmHiHatOn);
        FmNoiseLfsrField?.SetValue(fm, FmNoiseLfsr);

        if (FmOperatorsField?.GetValue(fm) is OperatorEngine[,] ops && FmOperators.Length == 18)
        {
            for (int ch = 0; ch < 9; ch++)
            {
                for (int op = 0; op < 2; op++)
                {
                    var s = FmOperators[ch * 2 + op];
                    var o = ops[ch, op];
                    o.State = (EnvelopeState)s.State;
                    o.AttenuationDb = s.AttenuationDb;
                    o.PhaseAccumulator = s.PhaseAccumulator;
                    o.OutputPrev1 = s.OutputPrev1;
                    o.OutputPrev2 = s.OutputPrev2;
                }
            }
        }

        var rhythmOps = new (FieldInfo? field, int index)[]
        {
            (FmBdModField, 0), (FmBdCarField, 1), (FmSdCarField, 2),
            (FmHhModField, 3), (FmTomCarField, 4), (FmCymModField, 5)
        };
        foreach (var (field, idx) in rhythmOps)
        {
            if (field?.GetValue(fm) is OperatorEngine o && idx < FmRhythmOperators.Length)
            {
                var s = FmRhythmOperators[idx];
                o.State = (EnvelopeState)s.State;
                o.AttenuationDb = s.AttenuationDb;
                o.PhaseAccumulator = s.PhaseAccumulator;
                o.OutputPrev1 = s.OutputPrev1;
                o.OutputPrev2 = s.OutputPrev2;
            }
        }

        // Scheduler
        var schedProp = typeof(SmsSystem).GetProperty("Scheduler", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!;
        var snapshot = new MasterSchedulerSnapshot(SchedulerMasterTicks, SchedulerCpuPhase, SchedulerVdpPhase, SchedulerPsgPhase);
        schedProp.SetValue(system, new MasterScheduler(snapshot));

        // Bus
        system.Bus.MemoryControl = BusMemoryControl;
        system.VideoStandard = BusVideoStandard;
        system.Bus.CurrentScanline = BusCurrentScanline;
        system.Bus.CurrentHCounter = BusCurrentHCounter;

        // Controllers
        var ctrl = system.Controllers;
        Joypad1StateField?.SetValue(ctrl, Joypad1State);
        Joypad2StateField?.SetValue(ctrl, Joypad2State);
        IoControlField?.SetValue(ctrl, IoControl);
        ResetPressedField?.SetValue(ctrl, ResetPressed);
        PausePressedField?.SetValue(ctrl, PausePressed);
        PauseNmiPendingField?.SetValue(ctrl, PauseNmiPending);
        Joypad1ThInputField?.SetValue(ctrl, Joypad1ThInput);
        Joypad2ThInputField?.SetValue(ctrl, Joypad2ThInput);
        system.AudioMixer.Fm.RestoreSnapshot(FmSnapshot!);
    }
}
