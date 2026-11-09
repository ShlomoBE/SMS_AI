using System;
using System.IO;
using System.Text;
using System.Text.Json;
using Sms.Core.Fm;
using Sms.Core.Cartridge;
using Sms.Core.Audio;
using Sms.Core.System;

namespace Sms.Core.State;

/// <summary>
/// Binary Save State Engine &amp; Cartridge SRAM (.sav) Persistence Serializer for Sega Master System.
/// Header: Magic "SMSSTATE" (8 bytes ASCII), Version = 2 (uint32).
/// Supports transactional state restoration and standalone SRAM persistence.
/// </summary>
public static class SmsStateSerializer
{
    private static readonly byte[] MagicHeader = "SMSSTATE"u8.ToArray();
    public const uint CurrentVersion = 2;
    private const int MaxStateBytes = 1024 * 1024;

    /// <summary>
    /// Captures complete emulator state into a versioned binary byte array.
    /// </summary>
    public static byte[] SaveState(SmsSystem system)
    {
        ArgumentNullException.ThrowIfNull(system);

        var state = SmsSaveState.FromSystem(system);
        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true);

        SerializeState(state, writer);
        writer.Flush();
        return ms.ToArray();
    }

    /// <summary>
    /// Attempts to restore emulator state from binary save state data.
    /// Returns false and leaves target system unmodified if validation or loading fails.
    /// </summary>
    public static bool TryRestoreState(SmsSystem system, ReadOnlySpan<byte> stateData, out string? errorMessage)
    {
        errorMessage = null;
        if (system == null)
        {
            errorMessage = "Target SMS system is null.";
            return false;
        }

        if (stateData.Length < 12)
        {
            errorMessage = "State data is too small to contain a valid header.";
            return false;
        }

        if (stateData.Length > MaxStateBytes)
        {
            errorMessage = "Save state exceeds the maximum supported size.";
            return false;
        }

        try
        {
            using var ms = new MemoryStream(stateData.ToArray());
            using var reader = new BinaryReader(ms, Encoding.UTF8, leaveOpen: true);

            var magic = reader.ReadBytes(8);
            if (!magic.AsSpan().SequenceEqual(MagicHeader))
            {
                errorMessage = "Invalid save state magic header.";
                return false;
            }

            uint version = reader.ReadUInt32();
            if (version != CurrentVersion)
            {
                errorMessage = $"Unsupported save state version {version}. Required version is {CurrentVersion}.";
                return false;
            }

            var state = DeserializeState(reader, version);
            if (ms.Position != ms.Length)
            {
                errorMessage = "Unexpected trailing save state data.";
                return false;
            }
            if (!state.Validate(system, out errorMessage))
            {
                return false;
            }

            var previous = SmsSaveState.FromSystem(system);
            try
            {
                state.ApplyTo(system);
            }
            catch
            {
                previous.ApplyTo(system);
                throw;
            }
            return true;
        }
        catch (Exception ex)
        {
            errorMessage = $"Corrupted save state payload: {ex.Message}";
            return false;
        }
    }

    /// <summary>
    /// Saves emulator state to a file.
    /// </summary>
    public static void SaveStateToFile(SmsSystem system, string filePath)
    {
        ArgumentNullException.ThrowIfNull(system);
        ArgumentException.ThrowIfNullOrEmpty(filePath);

        byte[] data = SaveState(system);
        string? dir = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }
        File.WriteAllBytes(filePath, data);
    }

    /// <summary>
    /// Restores emulator state from a file.
    /// Throws an exception if loading or validation fails.
    /// </summary>
    public static void LoadStateFromFile(SmsSystem system, string filePath)
    {
        ArgumentNullException.ThrowIfNull(system);
        ArgumentException.ThrowIfNullOrEmpty(filePath);

        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException("Save state file not found.", filePath);
        }

        using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length > MaxStateBytes) throw new InvalidDataException("Save state exceeds the maximum supported size.");
        byte[] data = new byte[(int)stream.Length];
        stream.ReadExactly(data);
        if (!TryRestoreState(system, data, out string? errorMessage))
        {
            throw new InvalidOperationException($"Failed to restore save state from file: {errorMessage}");
        }
    }

    /// <summary>
    /// Saves physical cartridge SRAM (8 KiB) to a persistence file (.sav).
    /// </summary>
    public static void SaveSramToFile(SmsSystem system, string sramFilePath)
    {
        ArgumentNullException.ThrowIfNull(system);
        ArgumentException.ThrowIfNullOrEmpty(sramFilePath);

        if (system.Mapper == null || system.Mapper.SramSize == 0)
        {
            throw new InvalidOperationException("The active cartridge does not contain SRAM.");
        }

        byte[] sram = system.Mapper.ExportSram();
        string? dir = Path.GetDirectoryName(sramFilePath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }
        File.WriteAllBytes(sramFilePath, sram);
    }

    /// <summary>
    /// Loads physical cartridge SRAM (8 KiB) from a persistence file (.sav).
    /// </summary>
    public static void LoadSramFromFile(SmsSystem system, string sramFilePath)
    {
        ArgumentNullException.ThrowIfNull(system);
        ArgumentException.ThrowIfNullOrEmpty(sramFilePath);

        if (system.Mapper == null || system.Mapper.SramSize == 0)
        {
            throw new InvalidOperationException("The active cartridge does not contain SRAM.");
        }

        if (!File.Exists(sramFilePath))
        {
            throw new FileNotFoundException("SRAM save file not found.", sramFilePath);
        }

        using var stream = new FileStream(sramFilePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length != system.Mapper.SramSize)
        {
            throw new ArgumentException($"SRAM file size ({stream.Length} bytes) does not match required cartridge SRAM size ({system.Mapper.SramSize} bytes).");
        }
        byte[] bytes = new byte[system.Mapper.SramSize];
        stream.ReadExactly(bytes);

        system.Mapper.ImportSram(bytes);
    }

    private static void SerializeState(SmsSaveState state, BinaryWriter w)
    {
        w.Write(MagicHeader);
        w.Write(state.Version);

        // CPU
        w.Write(state.CpuPc);
        w.Write(state.CpuSp);
        w.Write(state.CpuA);
        w.Write(state.CpuF);
        w.Write(state.CpuB);
        w.Write(state.CpuC);
        w.Write(state.CpuD);
        w.Write(state.CpuE);
        w.Write(state.CpuH);
        w.Write(state.CpuL);
        w.Write(state.CpuIx);
        w.Write(state.CpuIy);
        w.Write(state.CpuAPrime);
        w.Write(state.CpuFPrime);
        w.Write(state.CpuBPrime);
        w.Write(state.CpuCPrime);
        w.Write(state.CpuDPrime);
        w.Write(state.CpuEPrime);
        w.Write(state.CpuHPrime);
        w.Write(state.CpuLPrime);
        w.Write(state.CpuI);
        w.Write(state.CpuR);
        w.Write(state.CpuIff1);
        w.Write(state.CpuIff2);
        w.Write(state.CpuInterruptMode);
        w.Write(state.CpuPendingEi);
        w.Write(state.CpuIsHalted);
        w.Write(state.CpuIsFaulted);
        w.Write(state.CpuMemoryPointer);

        // RAM
        w.Write((uint)state.Ram.Length);
        w.Write(state.Ram);

        // Mapper
        w.Write(state.HasMapper);
        w.Write(state.MapperSlot0);
        w.Write(state.MapperSlot1);
        w.Write(state.MapperSlot2);
        w.Write(state.MapperRamControl);
        w.Write((uint)state.MapperSram.Length);
        if (state.MapperSram.Length > 0)
        {
            w.Write(state.MapperSram);
        }

        // VDP
        w.Write((uint)state.VdpVram.Length);
        w.Write(state.VdpVram);
        w.Write((uint)state.VdpCram.Length);
        w.Write(state.VdpCram);
        w.Write((uint)state.VdpRegisters.Length);
        w.Write(state.VdpRegisters);
        w.Write(state.VdpAddress);
        w.Write(state.VdpCode);
        w.Write(state.VdpSecondControlByte);
        w.Write(state.VdpReadBuffer);
        w.Write(state.VdpStatus);
        w.Write(state.VdpLinePending);
        w.Write(state.LineCounter);
        w.Write(state.TimingCarryTStates);

        // PSG
        for (int i = 0; i < 3; i++) w.Write(state.PsgTonePeriods[i]);
        for (int i = 0; i < 4; i++) w.Write(state.PsgAttenuations[i]);
        w.Write(state.PsgNoiseControl);
        w.Write(state.PsgLfsr);
        w.Write(state.PsgNoiseCounter);
        for (int i = 0; i < 3; i++) w.Write(state.PsgToneCounters[i]);
        for (int i = 0; i < 3; i++) w.Write(state.PsgToneOutputs[i]);
        w.Write(state.PsgLatchedRegister);
        w.Write(state.PsgDividerPhase);

        // FM
        w.Write(state.FmLatchedAddress);
        w.Write(state.FmAudioControl);
        w.Write((uint)state.FmRegisters.Length);
        w.Write(state.FmRegisters);
        w.Write((uint)state.FmCustomPatch.Length);
        w.Write(state.FmCustomPatch);
        w.Write(state.FmRhythmControl);
        for (int i = 0; i < 9; i++) w.Write(state.FmFNumbers[i]);
        for (int i = 0; i < 9; i++) w.Write(state.FmBlocks[i]);
        for (int i = 0; i < 9; i++) w.Write(state.FmKeyOn[i]);
        for (int i = 0; i < 9; i++) w.Write(state.FmSustain[i]);
        for (int i = 0; i < 9; i++) w.Write(state.FmPatches[i]);
        for (int i = 0; i < 9; i++) w.Write(state.FmVolumes[i]);
        w.Write(state.FmRhythmEnable);
        w.Write(state.FmRhythmBassDrumOn);
        w.Write(state.FmRhythmSnareDrumOn);
        w.Write(state.FmRhythmTomTomOn);
        w.Write(state.FmRhythmTopCymbalOn);
        w.Write(state.FmRhythmHiHatOn);
        w.Write(state.FmNoiseLfsr);

        w.Write((uint)state.FmOperators.Length);
        foreach (var op in state.FmOperators)
        {
            w.Write(op.State);
            w.Write(op.AttenuationDb);
            w.Write(op.PhaseAccumulator);
            w.Write(op.OutputPrev1);
            w.Write(op.OutputPrev2);
        }

        w.Write((uint)state.FmRhythmOperators.Length);
        foreach (var op in state.FmRhythmOperators)
        {
            w.Write(op.State);
            w.Write(op.AttenuationDb);
            w.Write(op.PhaseAccumulator);
            w.Write(op.OutputPrev1);
            w.Write(op.OutputPrev2);
        }

        // Scheduler
        w.Write(state.SchedulerMasterTicks);
        w.Write(state.SchedulerCpuPhase);
        w.Write(state.SchedulerVdpPhase);
        w.Write(state.SchedulerPsgPhase);

        // Bus
        w.Write(state.BusMemoryControl);
        w.Write((byte)state.BusVideoStandard);
        w.Write(state.BusCurrentScanline);
        w.Write(state.BusCurrentHCounter);

        // Controllers
        w.Write(state.Joypad1State);
        w.Write(state.Joypad2State);
        w.Write(state.IoControl);
        w.Write(state.ResetPressed);
        w.Write(state.PausePressed);
        w.Write(state.PauseNmiPending);
        w.Write(state.Joypad1ThInput);
        w.Write(state.Joypad2ThInput);
        w.Write((byte)state.RomHash.Length);
        w.Write(state.RomHash);
        w.Write((byte)state.MapperProfile);
        w.Write((byte)state.BiosHash.Length);
        w.Write(state.BiosHash);
        byte[] fmSnapshot = JsonSerializer.SerializeToUtf8Bytes(state.FmSnapshot);
        w.Write(fmSnapshot.Length);
        w.Write(fmSnapshot);
    }

    private static SmsSaveState DeserializeState(BinaryReader r, uint version)
    {
        var state = new SmsSaveState { Version = version };

        // CPU
        state.CpuPc = r.ReadUInt16();
        state.CpuSp = r.ReadUInt16();
        state.CpuA = r.ReadByte();
        state.CpuF = r.ReadByte();
        state.CpuB = r.ReadByte();
        state.CpuC = r.ReadByte();
        state.CpuD = r.ReadByte();
        state.CpuE = r.ReadByte();
        state.CpuH = r.ReadByte();
        state.CpuL = r.ReadByte();
        state.CpuIx = r.ReadUInt16();
        state.CpuIy = r.ReadUInt16();
        state.CpuAPrime = r.ReadByte();
        state.CpuFPrime = r.ReadByte();
        state.CpuBPrime = r.ReadByte();
        state.CpuCPrime = r.ReadByte();
        state.CpuDPrime = r.ReadByte();
        state.CpuEPrime = r.ReadByte();
        state.CpuHPrime = r.ReadByte();
        state.CpuLPrime = r.ReadByte();
        state.CpuI = r.ReadByte();
        state.CpuR = r.ReadByte();
        state.CpuIff1 = r.ReadBoolean();
        state.CpuIff2 = r.ReadBoolean();
        state.CpuInterruptMode = r.ReadByte();
        state.CpuPendingEi = r.ReadBoolean();
        state.CpuIsHalted = r.ReadBoolean();
        state.CpuIsFaulted = r.ReadBoolean();
        state.CpuMemoryPointer = r.ReadUInt16();

        // RAM
        uint ramLen = r.ReadUInt32();
        if (ramLen != SmsSystemBus.RamSize) throw new InvalidDataException("Invalid RAM length.");
        state.Ram = r.ReadBytes((int)ramLen);

        // Mapper
        state.HasMapper = r.ReadBoolean();
        state.MapperSlot0 = r.ReadByte();
        state.MapperSlot1 = r.ReadByte();
        state.MapperSlot2 = r.ReadByte();
        state.MapperRamControl = r.ReadByte();
        uint sramLen = r.ReadUInt32();
        if (sramLen != 0 && sramLen != SegaMapper.Sram8KiBSize) throw new InvalidDataException("Invalid SRAM length.");
        state.MapperSram = sramLen > 0 ? r.ReadBytes((int)sramLen) : Array.Empty<byte>();

        // VDP
        uint vramLen = r.ReadUInt32();
        if (vramLen != 0x4000) throw new InvalidDataException("Invalid VRAM length.");
        state.VdpVram = r.ReadBytes((int)vramLen);
        uint cramLen = r.ReadUInt32();
        if (cramLen != 32) throw new InvalidDataException("Invalid CRAM length.");
        state.VdpCram = r.ReadBytes((int)cramLen);
        uint regsLen = r.ReadUInt32();
        if (regsLen != 11) throw new InvalidDataException("Invalid VDP register length.");
        state.VdpRegisters = r.ReadBytes((int)regsLen);
        state.VdpAddress = r.ReadInt32();
        state.VdpCode = r.ReadInt32();
        state.VdpSecondControlByte = r.ReadBoolean();
        state.VdpReadBuffer = r.ReadByte();
        state.VdpStatus = r.ReadByte();
        state.VdpLinePending = r.ReadBoolean();
        state.LineCounter = r.ReadByte();
        state.TimingCarryTStates = r.ReadInt32();

        // PSG
        for (int i = 0; i < 3; i++) state.PsgTonePeriods[i] = r.ReadUInt16();
        for (int i = 0; i < 4; i++) state.PsgAttenuations[i] = r.ReadByte();
        state.PsgNoiseControl = r.ReadByte();
        state.PsgLfsr = r.ReadUInt16();
        state.PsgNoiseCounter = r.ReadInt32();
        for (int i = 0; i < 3; i++) state.PsgToneCounters[i] = r.ReadInt32();
        for (int i = 0; i < 3; i++) state.PsgToneOutputs[i] = r.ReadBoolean();
        state.PsgLatchedRegister = r.ReadByte();
        state.PsgDividerPhase = r.ReadInt32();

        // FM
        state.FmLatchedAddress = r.ReadByte();
        state.FmAudioControl = r.ReadByte();
        uint fmRegsLen = r.ReadUInt32();
        if (fmRegsLen != 256) throw new InvalidDataException("Invalid FM register length.");
        state.FmRegisters = r.ReadBytes((int)fmRegsLen);
        uint fmPatchLen = r.ReadUInt32();
        if (fmPatchLen != 8) throw new InvalidDataException("Invalid FM patch length.");
        state.FmCustomPatch = r.ReadBytes((int)fmPatchLen);
        state.FmRhythmControl = r.ReadByte();
        for (int i = 0; i < 9; i++) state.FmFNumbers[i] = r.ReadUInt16();
        for (int i = 0; i < 9; i++) state.FmBlocks[i] = r.ReadByte();
        for (int i = 0; i < 9; i++) state.FmKeyOn[i] = r.ReadBoolean();
        for (int i = 0; i < 9; i++) state.FmSustain[i] = r.ReadBoolean();
        for (int i = 0; i < 9; i++) state.FmPatches[i] = r.ReadByte();
        for (int i = 0; i < 9; i++) state.FmVolumes[i] = r.ReadByte();
        state.FmRhythmEnable = r.ReadBoolean();
        state.FmRhythmBassDrumOn = r.ReadBoolean();
        state.FmRhythmSnareDrumOn = r.ReadBoolean();
        state.FmRhythmTomTomOn = r.ReadBoolean();
        state.FmRhythmTopCymbalOn = r.ReadBoolean();
        state.FmRhythmHiHatOn = r.ReadBoolean();
        state.FmNoiseLfsr = r.ReadUInt32();

        uint opCount = r.ReadUInt32();
        if (opCount != 18) throw new InvalidDataException("Invalid FM operator count.");
        state.FmOperators = new FmOperatorState[opCount];
        for (int i = 0; i < opCount; i++)
        {
            state.FmOperators[i] = new FmOperatorState
            {
                State = r.ReadByte(),
                AttenuationDb = r.ReadDouble(),
                PhaseAccumulator = r.ReadUInt32(),
                OutputPrev1 = r.ReadInt32(),
                OutputPrev2 = r.ReadInt32()
            };
        }

        uint rhythmOpCount = r.ReadUInt32();
        if (rhythmOpCount != 6) throw new InvalidDataException("Invalid FM rhythm operator count.");
        state.FmRhythmOperators = new FmOperatorState[rhythmOpCount];
        for (int i = 0; i < rhythmOpCount; i++)
        {
            state.FmRhythmOperators[i] = new FmOperatorState
            {
                State = r.ReadByte(),
                AttenuationDb = r.ReadDouble(),
                PhaseAccumulator = r.ReadUInt32(),
                OutputPrev1 = r.ReadInt32(),
                OutputPrev2 = r.ReadInt32()
            };
        }

        // Scheduler
        state.SchedulerMasterTicks = r.ReadUInt64();
        state.SchedulerCpuPhase = r.ReadUInt32();
        state.SchedulerVdpPhase = r.ReadUInt32();
        state.SchedulerPsgPhase = r.ReadUInt32();

        // Bus
        state.BusMemoryControl = r.ReadByte();
        state.BusVideoStandard = (VideoStandard)r.ReadByte();
        state.BusCurrentScanline = r.ReadInt32();
        state.BusCurrentHCounter = r.ReadByte();

        // Controllers
        state.Joypad1State = r.ReadByte();
        state.Joypad2State = r.ReadByte();
        state.IoControl = r.ReadByte();
        state.ResetPressed = r.ReadBoolean();
        state.PausePressed = r.ReadBoolean();
        state.PauseNmiPending = r.ReadBoolean();
        state.Joypad1ThInput = r.ReadBoolean();
        state.Joypad2ThInput = r.ReadBoolean();

        int hashLength = r.ReadByte();
        if (hashLength != (state.HasMapper ? 32 : 0)) throw new InvalidDataException("Invalid ROM identity length.");
        state.RomHash = r.ReadBytes(hashLength);
        if (state.RomHash.Length != hashLength) throw new EndOfStreamException("Truncated ROM identity.");
        state.MapperProfile = (SegaMapperProfile)r.ReadByte();
        int biosHashLength = r.ReadByte();
        if (biosHashLength != 0 && biosHashLength != 32) throw new InvalidDataException("Invalid BIOS identity length.");
        state.BiosHash = r.ReadBytes(biosHashLength);
        if (state.BiosHash.Length != biosHashLength) throw new EndOfStreamException("Truncated BIOS identity.");

        int fmSnapshotLength = r.ReadInt32();
        if (fmSnapshotLength <= 0 || fmSnapshotLength > 65536)
            throw new InvalidDataException("Invalid FM snapshot length.");
        byte[] fmSnapshot = r.ReadBytes(fmSnapshotLength);
        if (fmSnapshot.Length != fmSnapshotLength)
            throw new EndOfStreamException("Truncated FM snapshot.");
        state.FmSnapshot = JsonSerializer.Deserialize<YM2413Snapshot>(fmSnapshot);

        return state;
    }
}
