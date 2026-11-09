using System;
using System.Linq;

namespace Sms.Core.Fm;

// Version 2 is independent of the enclosing machine save-state version.
// Arrays are owned by the snapshot; callers must not mutate it during restore.
public sealed record YM2413PatchState(byte Tl, byte Fb, byte Eg, byte Ml, byte Ar, byte Dr,
    byte Sl, byte Rr, byte Kr, byte Kl, byte Am, byte Pm, byte Ws);

public sealed record YM2413SlotState(int Number, int Type, int Patch, int Wave,
    int Output0, int Output1, uint PgPhase, uint PgOut, byte PgKeep,
    ushort BlkFnum, ushort Fnum, byte Blk, int EgState, int Volume,
    byte KeyFlag, byte SusFlag, ushort Tll, int Rks, int EgRateH,
    int EgRateL, uint EgShift, uint EgOut, uint UpdateRequests);

public sealed record YM2413Snapshot(
    int Version, byte Address, byte AudioControl, byte[] Registers, byte[] CustomPatchRegisters,
    ushort[] FNumber, byte[] Block, bool[] KeyOn, bool[] Sustain, byte[] PatchIndex, byte[] Volume,
    bool RhythmEnable, bool BassDrumOn, bool SnareDrumOn, bool TomTomOn, bool TopCymbalOn, bool HiHatOn,
    byte RhythmControl, byte TestFlag, uint SlotKeyStatus, byte RhythmMode, uint EgCounter,
    uint PmPhase, int AmPhase, byte LfoAm, uint Noise, byte ShortNoise,
    int[] PatchNumber, YM2413PatchState[] Patches, YM2413SlotState[] Slots,
    short[] ChannelOutput, ulong MasterTickRemainder, short LastSample);

public partial class YM2413Fm
{
    public const int SnapshotVersion = 2;

    public YM2413Snapshot CaptureSnapshot()
    {
        var patches = _patches.Select(p => new YM2413PatchState(p.Tl, p.Fb, p.Eg, p.Ml, p.Ar,
            p.Dr, p.Sl, p.Rr, p.Kr, p.Kl, p.Am, p.Pm, p.Ws)).ToArray();
        var slots = _slots.Select(s => new YM2413SlotState(s.Number, s.Type,
            Array.IndexOf(_patches, s.Patch), Array.IndexOf(WaveTableMap, s.WaveTable),
            s.Output[0], s.Output[1], s.PgPhase, s.PgOut, s.PgKeep, s.BlkFnum, s.Fnum,
            s.Blk, s.EgState, s.Volume, s.KeyFlag, s.SusFlag, s.Tll, s.Rks, s.EgRateH,
            s.EgRateL, s.EgShift, s.EgOut, s.UpdateRequests)).ToArray();
        return new(SnapshotVersion, _latchedAddress, _audioControl, (byte[])_registers.Clone(),
            (byte[])_customPatchRegs.Clone(), (ushort[])_fNumber.Clone(), (byte[])_block.Clone(),
            (bool[])_keyOn.Clone(), (bool[])_sustain.Clone(), (byte[])_patchIndex.Clone(), (byte[])_volume.Clone(),
            _rhythmEnable, _bassDrumOn, _snareDrumOn, _tomTomOn, _topCymbalOn, _hiHatOn,
            _rhythmControl, _testFlag, _slotKeyStatus, _rhythmMode, _egCounter, _pmPhase, _amPhase,
            _lfoAm, _noise, _shortNoise, (int[])_patchNumber.Clone(), patches, slots,
            (short[])_chOut.Clone(), _masterTickRemainder, _lastSample);
    }

    /// <summary>Checks the schema and index/range safety without changing the device.</summary>
    public static bool ValidateSnapshot(YM2413Snapshot? s)
    {
        if (s is null || s.Version != SnapshotVersion || s.Registers?.Length != 256 ||
            s.CustomPatchRegisters?.Length != 8 || s.FNumber?.Length != 9 || s.Block?.Length != 9 ||
            s.KeyOn?.Length != 9 || s.Sustain?.Length != 9 || s.PatchIndex?.Length != 9 ||
            s.Volume?.Length != 9 || s.PatchNumber?.Length != 9 || s.Patches?.Length != 38 ||
            s.Slots?.Length != 18 || s.ChannelOutput?.Length != 14 || s.MasterTickRemainder >= 216 ||
            s.RhythmMode > 1 || s.ShortNoise > 1 || s.LfoAm > 13 || s.AmPhase < 0 ||
            s.AmPhase >= AmTable.Length * 64 || s.SlotKeyStatus >= (1u << 18)) return false;
        if (s.FNumber.Any(x => x > 511) || s.Block.Any(x => x > 7) ||
            s.PatchIndex.Any(x => x > 18) || s.Volume.Any(x => x > 15) ||
            s.PatchNumber.Any(x => x < 0 || x > 18)) return false;
        foreach (var p in s.Patches)
            if (p is null || p.Tl > 63 || p.Fb > 7 || p.Eg > 1 || p.Ml > 15 || p.Ar > 15 ||
                p.Dr > 15 || p.Sl > 15 || p.Rr > 15 || p.Kr > 1 || p.Kl > 3 ||
                p.Am > 1 || p.Pm > 1 || p.Ws > 1) return false;
        for (int i = 0; i < s.Slots.Length; i++)
        {
            var v = s.Slots[i];
            if (v is null || v.Number != i || v.Type < 0 || v.Type > 3 || v.Patch < 0 || v.Patch >= 38 ||
                v.Patch != 2 * s.PatchNumber[i / 2] + (i % 2) ||
                v.Wave < 0 || v.Wave > 1 || v.PgPhase >= DpWidth || v.PgOut >= PgWidth || v.PgKeep > 1 ||
                v.BlkFnum > 4095 || v.Fnum > 511 || v.Blk > 7 || v.EgState < 0 || v.EgState > 4 ||
                v.Volume < 0 || v.Volume > 63 || v.KeyFlag > 1 || v.SusFlag > 1 ||
                v.Rks < 0 || v.Rks > 15 || v.EgRateH < 0 || v.EgRateH > 15 ||
                v.EgRateL < 0 || v.EgRateL > 3 || v.EgShift > 13 || v.EgOut > EgMute ||
                v.UpdateRequests > 255) return false;
        }
        return true;
    }

    /// <summary>Restores a version-2 snapshot; invalid input throws before device state is changed.</summary>
    public void RestoreSnapshot(YM2413Snapshot snapshot)
    {
        if (!ValidateSnapshot(snapshot)) throw new ArgumentException("Invalid or unsupported YM2413 snapshot.", nameof(snapshot));
        // Snapshot arrays are caller-owned and must not be mutated concurrently.
        _latchedAddress = snapshot.Address; _adr = snapshot.Address; _audioControl = snapshot.AudioControl;
        snapshot.Registers.CopyTo(_registers, 0); snapshot.CustomPatchRegisters.CopyTo(_customPatchRegs, 0);
        snapshot.FNumber.CopyTo(_fNumber, 0); snapshot.Block.CopyTo(_block, 0); snapshot.KeyOn.CopyTo(_keyOn, 0);
        snapshot.Sustain.CopyTo(_sustain, 0); snapshot.PatchIndex.CopyTo(_patchIndex, 0); snapshot.Volume.CopyTo(_volume, 0);
        _rhythmEnable = snapshot.RhythmEnable; _bassDrumOn = snapshot.BassDrumOn; _snareDrumOn = snapshot.SnareDrumOn;
        _tomTomOn = snapshot.TomTomOn; _topCymbalOn = snapshot.TopCymbalOn; _hiHatOn = snapshot.HiHatOn;
        _rhythmControl = snapshot.RhythmControl; _testFlag = snapshot.TestFlag; _slotKeyStatus = snapshot.SlotKeyStatus;
        _rhythmMode = snapshot.RhythmMode; _egCounter = snapshot.EgCounter; _pmPhase = snapshot.PmPhase;
        _amPhase = snapshot.AmPhase; _lfoAm = snapshot.LfoAm; _noise = snapshot.Noise; _shortNoise = snapshot.ShortNoise;
        snapshot.PatchNumber.CopyTo(_patchNumber, 0); snapshot.ChannelOutput.CopyTo(_chOut, 0);
        _masterTickRemainder = snapshot.MasterTickRemainder; _lastSample = snapshot.LastSample;
        for (int i = 0; i < 38; i++)
        {
            var p = snapshot.Patches[i]; var d = _patches[i];
            d.Tl=p.Tl; d.Fb=p.Fb; d.Eg=p.Eg; d.Ml=p.Ml; d.Ar=p.Ar; d.Dr=p.Dr; d.Sl=p.Sl;
            d.Rr=p.Rr; d.Kr=p.Kr; d.Kl=p.Kl; d.Am=p.Am; d.Pm=p.Pm; d.Ws=p.Ws;
        }
        for (int i = 0; i < 18; i++)
        {
            var v = snapshot.Slots[i]; var d = _slots[i];
            d.Number=v.Number; d.Type=v.Type; d.Patch=_patches[v.Patch]; d.WaveTable=WaveTableMap[v.Wave];
            d.Output[0]=v.Output0; d.Output[1]=v.Output1; d.PgPhase=v.PgPhase; d.PgOut=v.PgOut;
            d.PgKeep=v.PgKeep; d.BlkFnum=v.BlkFnum; d.Fnum=v.Fnum; d.Blk=v.Blk; d.EgState=v.EgState;
            d.Volume=v.Volume; d.KeyFlag=v.KeyFlag; d.SusFlag=v.SusFlag; d.Tll=v.Tll; d.Rks=v.Rks;
            d.EgRateH=v.EgRateH; d.EgRateL=v.EgRateL; d.EgShift=v.EgShift; d.EgOut=v.EgOut; d.UpdateRequests=v.UpdateRequests;
        }
    }
}
