using System;

namespace Sms.Core.Fm;

/// <summary>
/// Envelope Generator phase state (maintained for API / test compatibility).
/// </summary>
public enum EnvelopeState
{
    Off,
    Attack,
    Decay,
    Sustain,
    Release
}

/// <summary>
/// Subsystem representing an individual FM operator (maintained for API / test compatibility).
/// </summary>
public class OperatorEngine
{
    public EnvelopeState State { get; set; } = EnvelopeState.Off;
    public double AttenuationDb { get; set; } = 127.0;
    public uint PhaseAccumulator { get; set; }
    public int OutputPrev1 { get; set; }
    public int OutputPrev2 { get; set; }

    public void KeyOn(byte attackRate)
    {
        if (attackRate == 15)
        {
            AttenuationDb = 0.0;
            State = EnvelopeState.Decay;
        }
        else
        {
            AttenuationDb = 48.0;
            State = EnvelopeState.Attack;
        }
    }

    public void KeyOff()
    {
        if (State != EnvelopeState.Off)
        {
            State = EnvelopeState.Release;
        }
    }
}

/// <summary>
/// High-fidelity YM2413 (OPLL) FM sound generator, ported from Mitsutaka Okazaki's emu2413 (MIT Licensed).
/// </summary>
public partial class YM2413Fm
{
    private const int PgBits = 10;
    private const int PgWidth = 1 << PgBits;
    private const int DpBits = 19;
    private const int DpWidth = 1 << DpBits;
    private const int DpBaseBits = DpBits - PgBits;

    private const double EgStep = 0.375;
    private const int EgBits = 7;
    private const int EgMute = (1 << EgBits) - 1; // 127
    private const int EgMax = EgMute - 4; // 123

    private const double TlStep = 0.75;
    private const double SlStep = 3.0;
    private const int DamperRate = 12;

    private const int Attack = 0;
    private const int Decay = 1;
    private const int Sustain = 2;
    private const int Release = 3;
    private const int Damp = 4;

    private static readonly ushort[] ExpTable = new ushort[256];
    private static readonly ushort[] FullSinTable = new ushort[PgWidth];
    private static readonly ushort[] HalfSinTable = new ushort[PgWidth];
    private static readonly ushort[][] WaveTableMap = new ushort[2][];

    private static readonly sbyte[][] PmTable = new sbyte[8][]
    {
        new sbyte[] { 0, 0, 0, 0, 0, 0, 0, 0 },
        new sbyte[] { 0, 0, 1, 0, 0, 0, -1, 0 },
        new sbyte[] { 0, 1, 2, 1, 0, -1, -2, -1 },
        new sbyte[] { 0, 1, 3, 1, 0, -1, -3, -1 },
        new sbyte[] { 0, 2, 4, 2, 0, -2, -4, -2 },
        new sbyte[] { 0, 2, 5, 2, 0, -2, -5, -2 },
        new sbyte[] { 0, 3, 6, 3, 0, -3, -6, -3 },
        new sbyte[] { 0, 3, 7, 3, 0, -3, -7, -3 }
    };

    private static readonly byte[] AmTable = new byte[210]
    {
        0,  0,  0,  0,  0,  0,  0,  0,  1,  1,  1,  1,  1,  1,  1,  1,
        2,  2,  2,  2,  2,  2,  2,  2,  3,  3,  3,  3,  3,  3,  3,  3,
        4,  4,  4,  4,  4,  4,  4,  4,  5,  5,  5,  5,  5,  5,  5,  5,
        6,  6,  6,  6,  6,  6,  6,  6,  7,  7,  7,  7,  7,  7,  7,  7,
        8,  8,  8,  8,  8,  8,  8,  8,  9,  9,  9,  9,  9,  9,  9,  9,
        10, 10, 10, 10, 10, 10, 10, 10, 11, 11, 11, 11, 11, 11, 11, 11,
        12, 12, 12, 12, 12, 12, 12, 12,
        13, 13, 13,
        12, 12, 12, 12, 12, 12, 12, 12,
        11, 11, 11, 11, 11, 11, 11, 11, 10, 10, 10, 10, 10, 10, 10, 10,
        9,  9,  9,  9,  9,  9,  9,  9,  8,  8,  8,  8,  8,  8,  8,  8,
        7,  7,  7,  7,  7,  7,  7,  7,  6,  6,  6,  6,  6,  6,  6,  6,
        5,  5,  5,  5,  5,  5,  5,  5,  4,  4,  4,  4,  4,  4,  4,  4,
        3,  3,  3,  3,  3,  3,  3,  3,  2,  2,  2,  2,  2,  2,  2,  2,
        1,  1,  1,  1,  1,  1,  1,  1,  0,  0,  0,  0,  0,  0,  0
    };

    private static readonly byte[][] EgStepTables = new byte[4][]
    {
        new byte[] { 0, 1, 0, 1, 0, 1, 0, 1 },
        new byte[] { 0, 1, 0, 1, 1, 1, 0, 1 },
        new byte[] { 0, 1, 1, 1, 0, 1, 1, 1 },
        new byte[] { 0, 1, 1, 1, 1, 1, 1, 1 }
    };

    private static readonly uint[] MlTable = new uint[16]
    {
        1, 2, 4, 6, 8, 10, 12, 14,
        16, 18, 20, 20, 24, 24, 30, 30
    };

    private static readonly double[] KlTable = new double[16]
    {
        0.000 * 2, 9.000 * 2, 12.000 * 2, 13.875 * 2, 15.000 * 2, 16.125 * 2,
        16.875 * 2, 17.625 * 2, 18.000 * 2, 18.750 * 2, 19.125 * 2, 19.500 * 2,
        19.875 * 2, 20.250 * 2, 20.625 * 2, 21.000 * 2
    };

    private static readonly ushort[][] TllTable = new ushort[128][];
    private static readonly int[][] RksTable = new int[16][];

    private static readonly byte[][] DefaultInst = new byte[19][]
    {
        new byte[] { 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 }, // 0: User
        new byte[] { 0x71, 0x61, 0x1e, 0x17, 0xd0, 0x78, 0x00, 0x17 }, // 1: Violin
        new byte[] { 0x13, 0x41, 0x1a, 0x0d, 0xd8, 0xf7, 0x23, 0x13 }, // 2: Guitar
        new byte[] { 0x13, 0x01, 0x99, 0x00, 0xf2, 0xc4, 0x21, 0x23 }, // 3: Piano
        new byte[] { 0x11, 0x61, 0x0e, 0x07, 0x8d, 0x64, 0x70, 0x27 }, // 4: Flute
        new byte[] { 0x32, 0x21, 0x1e, 0x06, 0xe1, 0x76, 0x01, 0x28 }, // 5: Clarinet
        new byte[] { 0x31, 0x22, 0x16, 0x05, 0xe0, 0x71, 0x00, 0x18 }, // 6: Oboe
        new byte[] { 0x21, 0x61, 0x1d, 0x07, 0x82, 0x81, 0x11, 0x07 }, // 7: Trumpet
        new byte[] { 0x33, 0x21, 0x2d, 0x13, 0xb0, 0x70, 0x00, 0x07 }, // 8: Organ
        new byte[] { 0x61, 0x61, 0x1b, 0x06, 0x64, 0x65, 0x10, 0x17 }, // 9: Horn
        new byte[] { 0x41, 0x61, 0x0b, 0x18, 0x85, 0xf0, 0x81, 0x07 }, // A: Synthesizer
        new byte[] { 0x33, 0x01, 0x83, 0x11, 0xea, 0xef, 0x10, 0x04 }, // B: Harpsichord
        new byte[] { 0x17, 0xc1, 0x24, 0x07, 0xf8, 0xf8, 0x22, 0x12 }, // C: Vibraphone
        new byte[] { 0x61, 0x50, 0x0c, 0x05, 0xd2, 0xf5, 0x40, 0x42 }, // D: Synth Bass
        new byte[] { 0x01, 0x01, 0x55, 0x03, 0xe9, 0x90, 0x03, 0x02 }, // E: Acoustic Bass
        new byte[] { 0x41, 0x41, 0x89, 0x03, 0xf1, 0xe4, 0xc0, 0x13 }, // F: Electric Guitar
        new byte[] { 0x01, 0x01, 0x18, 0x0f, 0xdf, 0xf8, 0x6a, 0x6d }, // R: Bass Drum
        new byte[] { 0x01, 0x01, 0x00, 0x00, 0xc8, 0xd8, 0xa7, 0x68 }, // R: High-Hat / Snare Drum
        new byte[] { 0x05, 0x01, 0x00, 0x00, 0xf8, 0xaa, 0x59, 0x55 }  // R: Tom-tom / Top Cymbal
    };

    private static readonly OpllPatch NullPatch = new();
    private static readonly OpllPatch[] DefaultPatch = new OpllPatch[19 * 2];

    static YM2413Fm()
    {
        // Exp table
        for (int i = 0; i < 256; i++)
        {
            double val = Math.Round((Math.Pow(2.0, i / 256.0) - 1.0) * 1024.0);
            ExpTable[i] = (ushort)Math.Clamp((int)val, 0, 65535);
        }

        // Full sin table
        for (int x = 0; x < PgWidth / 4; x++)
        {
            double sinVal = Math.Sin((x + 0.5) * Math.PI / (PgWidth / 4) / 2.0);
            double logVal = Math.Round(-Math.Log2(sinVal) * 256.0);
            FullSinTable[x] = (ushort)Math.Clamp((int)logVal, 0, 65535);
        }

        for (int x = 0; x < PgWidth / 4; x++)
            FullSinTable[PgWidth / 4 + x] = FullSinTable[PgWidth / 4 - x - 1];

        for (int x = 0; x < PgWidth / 2; x++)
            FullSinTable[PgWidth / 2 + x] = (ushort)(0x8000 | FullSinTable[x]);

        // Half sin table
        for (int x = 0; x < PgWidth / 2; x++)
            HalfSinTable[x] = FullSinTable[x];

        for (int x = PgWidth / 2; x < PgWidth; x++)
            HalfSinTable[x] = 0xFFF;

        WaveTableMap[0] = FullSinTable;
        WaveTableMap[1] = HalfSinTable;

        // TLL table
        for (int fnum = 0; fnum < 16; fnum++)
        {
            for (int block = 0; block < 8; block++)
            {
                TllTable[(block << 4) | fnum] = new ushort[4];
                for (int kl = 0; kl < 4; kl++)
                {
                    if (kl == 0)
                    {
                        TllTable[(block << 4) | fnum][kl] = 0;
                    }
                    else
                    {
                        int tmp = (int)(KlTable[fnum] - (3.000 * 2) * (7 - block));
                        if (tmp <= 0)
                            TllTable[(block << 4) | fnum][kl] = 0;
                        else
                            TllTable[(block << 4) | fnum][kl] = (ushort)((tmp >> (3 - kl)) / EgStep);
                    }
                }
            }
        }

        // RKS table
        for (int fnum8 = 0; fnum8 < 2; fnum8++)
        {
            for (int block = 0; block < 8; block++)
            {
                RksTable[(block << 1) | fnum8] = new int[2]
                {
                    block >> 1,
                    (block << 1) + fnum8
                };
            }
        }

        // Default patches
        for (int j = 0; j < 19; j++)
            GetDefaultPatch(j, DefaultPatch, j * 2);
    }

    private static void DumpToPatch(byte[] dump, int offset, OpllPatch[] patches, int patchOffset)
    {
        patches[patchOffset] = new OpllPatch
        {
            Am = (byte)((dump[offset + 0] >> 7) & 1),
            Pm = (byte)((dump[offset + 0] >> 6) & 1),
            Eg = (byte)((dump[offset + 0] >> 5) & 1),
            Kr = (byte)((dump[offset + 0] >> 4) & 1),
            Ml = (byte)(dump[offset + 0] & 15),
            Kl = (byte)((dump[offset + 2] >> 6) & 3),
            Tl = (byte)(dump[offset + 2] & 63),
            Fb = (byte)(dump[offset + 3] & 7),
            Ws = (byte)((dump[offset + 3] >> 3) & 1),
            Ar = (byte)((dump[offset + 4] >> 4) & 15),
            Dr = (byte)(dump[offset + 4] & 15),
            Sl = (byte)((dump[offset + 6] >> 4) & 15),
            Rr = (byte)(dump[offset + 6] & 15)
        };

        patches[patchOffset + 1] = new OpllPatch
        {
            Am = (byte)((dump[offset + 1] >> 7) & 1),
            Pm = (byte)((dump[offset + 1] >> 6) & 1),
            Eg = (byte)((dump[offset + 1] >> 5) & 1),
            Kr = (byte)((dump[offset + 1] >> 4) & 1),
            Ml = (byte)(dump[offset + 1] & 15),
            Kl = (byte)((dump[offset + 3] >> 6) & 3),
            Tl = 0,
            Fb = 0,
            Ws = (byte)((dump[offset + 3] >> 4) & 1),
            Ar = (byte)((dump[offset + 5] >> 4) & 15),
            Dr = (byte)(dump[offset + 5] & 15),
            Sl = (byte)((dump[offset + 7] >> 4) & 15),
            Rr = (byte)(dump[offset + 7] & 15)
        };
    }

    private static void GetDefaultPatch(int num, OpllPatch[] patches, int patchOffset)
    {
        DumpToPatch(DefaultInst[num], 0, patches, patchOffset);
    }

    // Public state properties required for compatibility
    public byte LatchedAddress => (byte)_adr;
    public byte AudioControl => _audioControl;
    public bool FmEnabled => (_audioControl & 0x01) != 0;
    public bool PsgMuted => (_audioControl & 0x02) != 0;

    // Reflection & save state compatibility fields
    private byte _latchedAddress;
    private byte _audioControl;
    private readonly byte[] _registers = new byte[256];
    private readonly byte[] _customPatchRegs = new byte[8];
    private readonly ushort[] _fNumber = new ushort[9];
    private readonly byte[] _block = new byte[9];
    private readonly bool[] _keyOn = new bool[9];
    private readonly bool[] _sustain = new bool[9];
    private readonly byte[] _patchIndex = new byte[9];
    private readonly byte[] _volume = new byte[9];
    private bool _rhythmEnable;
    private bool _bassDrumOn;
    private bool _snareDrumOn;
    private bool _tomTomOn;
    private bool _topCymbalOn;
    private bool _hiHatOn;
    private uint _noiseLfsr = 1;
    private readonly OperatorEngine[,] _operators = new OperatorEngine[9, 2];
    private readonly OperatorEngine _bdMod = new();
    private readonly OperatorEngine _bdCar = new();
    private readonly OperatorEngine _sdCar = new();
    private readonly OperatorEngine _hhMod = new();
    private readonly OperatorEngine _tomCar = new();
    private readonly OperatorEngine _cymMod = new();

    // Internal emu2413 OPLL State
    private sealed class OpllPatch
    {
        public byte Tl, Fb, Eg, Ml, Ar, Dr, Sl, Rr, Kr, Kl, Am, Pm, Ws;
    }

    private sealed class OpllSlot
    {
        public int Number;
        public int Type; // 0=modulator, 1=carrier
        public OpllPatch Patch = NullPatch;
        public readonly int[] Output = new int[2];
        public ushort[] WaveTable = WaveTableMap[0];
        public uint PgPhase;
        public uint PgOut;
        public byte PgKeep;
        public ushort BlkFnum;
        public ushort Fnum;
        public byte Blk;
        public int EgState = Release;
        public int Volume;
        public byte KeyFlag;
        public byte SusFlag;
        public ushort Tll;
        public int Rks;
        public int EgRateH;
        public int EgRateL;
        public uint EgShift;
        public uint EgOut = EgMute;
        public uint UpdateRequests;
    }

    private uint _clk = 3579545;
    private uint _rate = 44100;
    private uint _adr;
    private byte _testFlag;
    private uint _slotKeyStatus;
    private byte _rhythmMode;
    private uint _egCounter;
    private uint _pmPhase;
    private int _amPhase;
    private byte _lfoAm;
    private uint _noise = 1;
    private byte _shortNoise;
    private readonly int[] _patchNumber = new int[9];
    private readonly OpllSlot[] _slots = new OpllSlot[18];
    private readonly OpllPatch[] _patches = new OpllPatch[19 * 2];
    private readonly short[] _chOut = new short[14];
    // SMS master clock is three times the OPLL clock; one OPLL output
    // step is 72 chip clocks. Keep the remainder across calls.
    private ulong _masterTickRemainder;
    private short _lastSample;

    public YM2413Fm()
    {
        for (int i = 0; i < 9; i++)
        {
            _operators[i, 0] = new OperatorEngine();
            _operators[i, 1] = new OperatorEngine();
        }

        for (int i = 0; i < 18; i++)
        {
            _slots[i] = new OpllSlot();
        }

        for (int i = 0; i < 38; i++)
        {
            _patches[i] = new OpllPatch();
        }

        Reset();
    }

    public void WritePortAddress(byte data)
    {
        _latchedAddress = data;
        _adr = data;
    }

    public void WritePortData(byte data)
    {
        _registers[_latchedAddress] = data;

        if (_latchedAddress < 8)
        {
            _customPatchRegs[_latchedAddress] = data;
        }

        WriteReg(_adr, data);
    }

    public void WriteAudioControl(byte data)
    {
        _audioControl = data;
    }

    public byte ReadRegister(int reg)
    {
        return _registers[reg & 0xFF];
    }

    public byte[] GetCustomPatchRegisters()
    {
        return (byte[])_customPatchRegs.Clone();
    }

    public ushort GetFNumber(int ch) => _fNumber[ch];
    public byte GetBlock(int ch) => _block[ch];
    public bool GetKeyOn(int ch) => _keyOn[ch];
    public bool GetSustain(int ch) => _sustain[ch];
    public byte GetPatchIndex(int ch) => _patchIndex[ch];
    public OpllInstrumentPatch GetChannelPatch(int channel) =>
        FmPatchRom.GetPatch(_patchIndex[channel & 0x0F], _customPatchRegs);
    public byte GetVolume(int ch) => _volume[ch];
    public bool RhythmEnable => _rhythmEnable;
    public bool RhythmBassDrumOn => _bassDrumOn;
    public bool RhythmSnareDrumOn => _snareDrumOn;
    public bool RhythmTomTomOn => _tomTomOn;
    public bool RhythmTopCymbalOn => _topCymbalOn;
    public bool RhythmHiHatOn => _hiHatOn;

    public void Reset()
    {
        _latchedAddress = 0;
        _audioControl = 0;
        _adr = 0;
        _testFlag = 0;
        _slotKeyStatus = 0;
        _rhythmMode = 0;
        _rhythmControl = 0;
        _rhythmEnable = false;
        _bassDrumOn = false;
        _snareDrumOn = false;
        _tomTomOn = false;
        _topCymbalOn = false;
        _hiHatOn = false;
        _egCounter = 0;
        _pmPhase = 0;
        _amPhase = 0;
        _lfoAm = 0;
        _noise = 1;
        _shortNoise = 0;
        _masterTickRemainder = 0;
        _lastSample = 0;

        Array.Clear(_registers, 0, _registers.Length);
        Array.Clear(_customPatchRegs, 0, _customPatchRegs.Length);
        Array.Clear(_fNumber, 0, _fNumber.Length);
        Array.Clear(_block, 0, _block.Length);
        Array.Clear(_keyOn, 0, _keyOn.Length);
        Array.Clear(_sustain, 0, _sustain.Length);
        Array.Clear(_patchIndex, 0, _patchIndex.Length);
        Array.Clear(_volume, 0, _volume.Length);
        Array.Clear(_patchNumber, 0, _patchNumber.Length);
        Array.Clear(_chOut, 0, _chOut.Length);

        for (int i = 0; i < 38; i++)
        {
            _patches[i] = new OpllPatch();
        }

        for (int i = 0; i < 19 * 2; i++)
        {
            OpllPatch src = DefaultPatch[i];
            _patches[i].Am = src.Am;
            _patches[i].Pm = src.Pm;
            _patches[i].Eg = src.Eg;
            _patches[i].Kr = src.Kr;
            _patches[i].Ml = src.Ml;
            _patches[i].Kl = src.Kl;
            _patches[i].Tl = src.Tl;
            _patches[i].Fb = src.Fb;
            _patches[i].Ws = src.Ws;
            _patches[i].Ar = src.Ar;
            _patches[i].Dr = src.Dr;
            _patches[i].Sl = src.Sl;
            _patches[i].Rr = src.Rr;
        }

        for (int i = 0; i < 18; i++)
        {
            ResetSlot(_slots[i], i);
        }

        for (int i = 0; i < 9; i++)
        {
            SetPatch(i, 0);
        }

        for (int ch = 0; ch < 9; ch++)
        {
            _operators[ch, 0].State = EnvelopeState.Off;
            _operators[ch, 0].AttenuationDb = 127.0;
            _operators[ch, 1].State = EnvelopeState.Off;
            _operators[ch, 1].AttenuationDb = 127.0;
        }

        _bdMod.State = EnvelopeState.Off; _bdMod.AttenuationDb = 127.0;
        _bdCar.State = EnvelopeState.Off; _bdCar.AttenuationDb = 127.0;
        _sdCar.State = EnvelopeState.Off; _sdCar.AttenuationDb = 127.0;
        _hhMod.State = EnvelopeState.Off; _hhMod.AttenuationDb = 127.0;
        _tomCar.State = EnvelopeState.Off; _tomCar.AttenuationDb = 127.0;
        _cymMod.State = EnvelopeState.Off; _cymMod.AttenuationDb = 127.0;
    }

    private void ResetSlot(OpllSlot slot, int number)
    {
        slot.Number = number;
        slot.Type = number % 2;
        slot.PgKeep = 0;
        slot.WaveTable = WaveTableMap[0];
        slot.PgPhase = 0;
        slot.Output[0] = 0;
        slot.Output[1] = 0;
        slot.EgState = Release;
        slot.EgShift = 0;
        slot.Rks = 0;
        slot.Tll = 0;
        slot.KeyFlag = 0;
        slot.SusFlag = 0;
        slot.BlkFnum = 0;
        slot.Blk = 0;
        slot.Fnum = 0;
        slot.Volume = 0;
        slot.PgOut = 0;
        slot.EgRateH = 0;
        slot.EgRateL = 0;
        slot.EgOut = EgMute;
        slot.UpdateRequests = 0;
        slot.Patch = NullPatch;
    }

    private void RequestUpdate(OpllSlot slot, uint flag)
    {
        slot.UpdateRequests |= flag;
    }

    private void CommitSlotUpdate(OpllSlot slot)
    {
        if ((slot.UpdateRequests & 1) != 0) // UPDATE_WS
        {
            slot.WaveTable = WaveTableMap[slot.Patch.Ws];
        }

        if ((slot.UpdateRequests & 2) != 0) // UPDATE_TLL
        {
            int tl = (slot.Type & 1) == 0 ? slot.Patch.Tl : slot.Volume;
            slot.Tll = (ushort)(TllTable[slot.BlkFnum >> 5][slot.Patch.Kl] + (tl << 1));
        }

        if ((slot.UpdateRequests & 4) != 0) // UPDATE_RKS
        {
            slot.Rks = RksTable[slot.BlkFnum >> 8][slot.Patch.Kr];
        }

        if ((slot.UpdateRequests & (4 | 8)) != 0) // UPDATE_RKS | UPDATE_EG
        {
            int pRate = GetParameterRate(slot);
            if (pRate == 0)
            {
                slot.EgShift = 0;
                slot.EgRateH = 0;
                slot.EgRateL = 0;
            }
            else
            {
                slot.EgRateH = Math.Min(15, pRate + (slot.Rks >> 2));
                slot.EgRateL = slot.Rks & 3;
                if (slot.EgState == Attack)
                    slot.EgShift = (0 < slot.EgRateH && slot.EgRateH < 12) ? (uint)(13 - slot.EgRateH) : 0u;
                else
                    slot.EgShift = (slot.EgRateH < 13) ? (uint)(13 - slot.EgRateH) : 0u;
            }
        }

        slot.UpdateRequests = 0;
    }

    private int GetParameterRate(OpllSlot slot)
    {
        if ((slot.Type & 1) == 0 && slot.KeyFlag == 0)
            return 0;

        return slot.EgState switch
        {
            Attack => slot.Patch.Ar,
            Decay => slot.Patch.Dr,
            Sustain => slot.Patch.Eg != 0 ? 0 : slot.Patch.Rr,
            Release => slot.SusFlag != 0 ? 5 : (slot.Patch.Eg != 0 ? slot.Patch.Rr : 7),
            Damp => DamperRate,
            _ => 0
        };
    }

    private void SlotOn(int i)
    {
        OpllSlot slot = _slots[i];
        slot.KeyFlag = 1;
        slot.EgState = Damp;
        RequestUpdate(slot, 8); // UPDATE_EG
    }

    private void SlotOff(int i)
    {
        OpllSlot slot = _slots[i];
        slot.KeyFlag = 0;
        if ((slot.Type & 1) != 0)
        {
            slot.EgState = Release;
            RequestUpdate(slot, 8);
        }
    }

    private void UpdateKeyStatus()
    {
        byte r14 = _registers[0x0E];
        byte rhythmMode = (byte)((r14 >> 5) & 1);
        uint newSlotKeyStatus = 0;

        for (int ch = 0; ch < 9; ch++)
        {
            if ((_registers[0x20 + ch] & 0x10) != 0)
                newSlotKeyStatus |= 3u << (ch * 2);
        }

        if (rhythmMode != 0)
        {
            if ((r14 & 0x10) != 0) newSlotKeyStatus |= 3u << 12; // BD
            if ((r14 & 0x01) != 0) newSlotKeyStatus |= 1u << 14; // HH
            if ((r14 & 0x08) != 0) newSlotKeyStatus |= 1u << 15; // SD
            if ((r14 & 0x04) != 0) newSlotKeyStatus |= 1u << 16; // TOM
            if ((r14 & 0x02) != 0) newSlotKeyStatus |= 1u << 17; // CYM
        }

        uint updatedStatus = _slotKeyStatus ^ newSlotKeyStatus;
        if (updatedStatus != 0)
        {
            for (int i = 0; i < 18; i++)
            {
                if (((updatedStatus >> i) & 1) != 0)
                {
                    if (((newSlotKeyStatus >> i) & 1) != 0)
                        SlotOn(i);
                    else
                        SlotOff(i);
                }
            }
        }

        _slotKeyStatus = newSlotKeyStatus;
    }

    private void SetPatch(int ch, int num)
    {
        _patchNumber[ch] = num;
        _patchIndex[ch] = (byte)num;
        OpllSlot mod = _slots[ch << 1];
        OpllSlot car = _slots[(ch << 1) | 1];
        mod.Patch = _patches[num * 2 + 0];
        car.Patch = _patches[num * 2 + 1];
        RequestUpdate(mod, 255);
        RequestUpdate(car, 255);
    }

    private void SetSusFlag(int ch, int flag)
    {
        _sustain[ch] = flag != 0;
        OpllSlot car = _slots[(ch << 1) | 1];
        car.SusFlag = (byte)flag;
        RequestUpdate(car, 8);
        OpllSlot mod = _slots[ch << 1];
        if ((mod.Type & 1) != 0)
        {
            mod.SusFlag = (byte)flag;
            RequestUpdate(mod, 8);
        }
    }

    private void SetVolume(int ch, int volume)
    {
        _volume[ch] = (byte)(volume >> 2);
        OpllSlot car = _slots[(ch << 1) | 1];
        car.Volume = volume;
        RequestUpdate(car, 2);
    }

    private void SetSlotVolume(OpllSlot slot, int volume)
    {
        slot.Volume = volume;
        RequestUpdate(slot, 2);
    }

    private void SetFNumber(int ch, int fnum)
    {
        _fNumber[ch] = (ushort)fnum;
        OpllSlot car = _slots[(ch << 1) | 1];
        OpllSlot mod = _slots[ch << 1];
        car.Fnum = (ushort)fnum;
        car.BlkFnum = (ushort)((car.BlkFnum & 0xE00) | (fnum & 0x1FF));
        mod.Fnum = (ushort)fnum;
        mod.BlkFnum = (ushort)((mod.BlkFnum & 0xE00) | (fnum & 0x1FF));
        RequestUpdate(car, 8 | 4 | 2);
        RequestUpdate(mod, 8 | 4 | 2);
    }

    private void SetBlock(int ch, int blk)
    {
        _block[ch] = (byte)blk;
        OpllSlot car = _slots[(ch << 1) | 1];
        OpllSlot mod = _slots[ch << 1];
        car.Blk = (byte)blk;
        car.BlkFnum = (ushort)(((blk & 7) << 9) | (car.BlkFnum & 0x1FF));
        mod.Blk = (byte)blk;
        mod.BlkFnum = (ushort)(((blk & 7) << 9) | (mod.BlkFnum & 0x1FF));
        RequestUpdate(car, 8 | 4 | 2);
        RequestUpdate(mod, 8 | 4 | 2);
    }

    private void UpdateRhythmMode()
    {
        byte newRhythmMode = (byte)((_registers[0x0E] >> 5) & 1);
        _rhythmEnable = newRhythmMode != 0;
        _rhythmMode = newRhythmMode;

        if (_rhythmMode != 0)
        {
            _slots[14].Type = 3; _slots[14].PgKeep = 1; // HH
            _slots[15].Type = 3;                       // SD
            _slots[16].Type = 3;                       // TOM
            _slots[17].Type = 3; _slots[17].PgKeep = 1; // CYM
            SetPatch(6, 16);
            SetPatch(7, 17);
            SetPatch(8, 18);
            SetSlotVolume(_slots[14], ((_registers[0x37] >> 4) & 15) << 2);
            SetSlotVolume(_slots[16], ((_registers[0x38] >> 4) & 15) << 2);
        }
        else
        {
            _slots[14].Type = 0; _slots[14].PgKeep = 0;
            _slots[15].Type = 1;
            _slots[16].Type = 0;
            _slots[17].Type = 1; _slots[17].PgKeep = 0;
            SetPatch(6, _registers[0x36] >> 4);
            SetPatch(7, _registers[0x37] >> 4);
            SetPatch(8, _registers[0x38] >> 4);
        }
    }

    private void UpdateAmPm()
    {
        if ((_testFlag & 2) != 0)
        {
            _pmPhase = 0;
            _amPhase = 0;
        }
        else
        {
            _pmPhase += (_testFlag & 8) != 0 ? 1024u : 1u;
            _amPhase = (_amPhase + ((_testFlag & 8) != 0 ? 64 : 1)) % (AmTable.Length * 64);
        }
        _lfoAm = AmTable[(_amPhase >> 6) % AmTable.Length];
    }

    private void UpdateNoise(int cycle)
    {
        for (int i = 0; i < cycle; i++)
        {
            if ((_noise & 1) != 0)
            {
                _noise ^= 0x800200u;
            }
            _noise >>= 1;
        }
    }

    private void UpdateShortNoise()
    {
        uint pgHh = _slots[14].PgOut;
        uint pgCym = _slots[17].PgOut;

        byte hBit2 = (byte)((pgHh >> (PgBits - 8)) & 1);
        byte hBit7 = (byte)((pgHh >> (PgBits - 3)) & 1);
        byte hBit3 = (byte)((pgHh >> (PgBits - 7)) & 1);

        byte cBit3 = (byte)((pgCym >> (PgBits - 7)) & 1);
        byte cBit5 = (byte)((pgCym >> (PgBits - 5)) & 1);

        _shortNoise = (byte)((hBit2 ^ hBit7) | (hBit3 ^ cBit5) | (cBit3 ^ cBit5));
    }

    private void CalcPhase(OpllSlot slot, uint pmPhase, byte reset)
    {
        int pm = slot.Patch.Pm != 0 ? PmTable[(slot.Fnum >> 6) & 7][(pmPhase >> 10) & 7] : 0;
        if (reset != 0) slot.PgPhase = 0;

        slot.PgPhase += (uint)((((slot.Fnum & 0x1FF) * 2 + pm) * MlTable[slot.Patch.Ml]) << slot.Blk >> 2);
        slot.PgPhase &= (DpWidth - 1);
        slot.PgOut = slot.PgPhase >> DpBaseBits;
    }

    private byte LookupAttackStep(OpllSlot slot, uint counter)
    {
        int index;
        switch (slot.EgRateH)
        {
            case 12:
                index = (int)((counter & 0xC) >> 1);
                return (byte)(4 - EgStepTables[slot.EgRateL][index]);
            case 13:
                index = (int)((counter & 0xC) >> 1);
                return (byte)(3 - EgStepTables[slot.EgRateL][index]);
            case 14:
                index = (int)((counter & 0xC) >> 1);
                return (byte)(2 - EgStepTables[slot.EgRateL][index]);
            case 0:
            case 15:
                return 0;
            default:
                index = (int)(counter >> (int)slot.EgShift);
                return (byte)(EgStepTables[slot.EgRateL][index & 7] != 0 ? 4 : 0);
        }
    }

    private byte LookupDecayStep(OpllSlot slot, uint counter)
    {
        int index;
        switch (slot.EgRateH)
        {
            case 0:
                return 0;
            case 13:
                index = (int)(((counter & 0xC) >> 1) | (counter & 1));
                return EgStepTables[slot.EgRateL][index];
            case 14:
                index = (int)((counter & 0xC) >> 1);
                return (byte)(EgStepTables[slot.EgRateL][index] + 1);
            case 15:
                return 2;
            default:
                index = (int)(counter >> (int)slot.EgShift);
                return EgStepTables[slot.EgRateL][index & 7];
        }
    }

    private void StartEnvelope(OpllSlot slot)
    {
        if (Math.Min(15, slot.Patch.Ar + (slot.Rks >> 2)) == 15)
        {
            slot.EgState = Decay;
            slot.EgOut = 0;
        }
        else
        {
            slot.EgState = Attack;
        }
        RequestUpdate(slot, 8);
    }

    private void CalcEnvelope(OpllSlot slot, OpllSlot? buddy, uint egCounter, byte test)
    {
        if (test == 0 && (slot.EgState == Sustain || slot.EgState == Release) &&
            (slot.EgOut >= EgMute || slot.EgRateH == 0))
        {
            return;
        }

        uint mask = (1u << (int)slot.EgShift) - 1u;

        if (slot.EgState == Attack)
        {
            if (0 < slot.EgOut && 0 < slot.EgRateH && (egCounter & mask & ~3u) == 0)
            {
                byte s = LookupAttackStep(slot, egCounter);
                if (s > 0)
                {
                    slot.EgOut = (uint)Math.Max(0, (int)slot.EgOut - ((int)slot.EgOut >> s) - 1);
                }
            }
        }
        else
        {
            if (slot.EgRateH > 0 && (egCounter & mask) == 0)
            {
                slot.EgOut = (uint)Math.Min(EgMute, (int)slot.EgOut + LookupDecayStep(slot, egCounter));
            }
        }

        switch (slot.EgState)
        {
            case Damp:
                if (slot.EgOut >= EgMax && (egCounter & mask) == 0)
                {
                    StartEnvelope(slot);
                    if ((slot.Type & 1) != 0)
                    {
                        if (slot.PgKeep == 0) slot.PgPhase = 0;
                        if (buddy != null && buddy.PgKeep == 0) buddy.PgPhase = 0;
                    }
                }
                break;

            case Attack:
                if (slot.EgOut == 0)
                {
                    slot.EgState = Decay;
                    RequestUpdate(slot, 8);
                }
                break;

            case Decay:
                if ((slot.EgOut >> 3) == slot.Patch.Sl)
                {
                    slot.EgState = Sustain;
                    RequestUpdate(slot, 8);
                }
                break;
        }

        if (test != 0)
        {
            slot.EgOut = 0;
        }
    }

    private void UpdateSlots()
    {
        _egCounter++;
        for (int i = 0; i < 18; i++)
        {
            OpllSlot slot = _slots[i];
            OpllSlot? buddy = null;
            if (slot.Type == 0) buddy = _slots[i + 1];
            if (slot.Type == 1) buddy = _slots[i - 1];

            if (slot.UpdateRequests != 0)
                CommitSlotUpdate(slot);

            CalcEnvelope(slot, buddy, _egCounter, (byte)(_testFlag & 1));
            CalcPhase(slot, _pmPhase, (byte)(_testFlag & 4));
        }
    }

    private static short LookupExpTable(ushort i)
    {
        int t = ExpTable[(i & 0xFF) ^ 0xFF] + 1024;
        int res = t >> ((i & 0x7F00) >> 8);
        return (short)(((i & 0x8000) != 0 ? ~res : res) << 1);
    }

    private short ToLinear(ushort h, OpllSlot slot, short am)
    {
        if (slot.EgOut > EgMax) return 0;
        int att = Math.Min(EgMute, (int)(slot.EgOut + slot.Tll + am)) << 4;
        return LookupExpTable((ushort)(h + att));
    }

    private short CalcSlotCar(int ch, short fm)
    {
        OpllSlot slot = _slots[(ch << 1) | 1];
        short am = (short)(slot.Patch.Am != 0 ? _lfoAm : 0);
        slot.Output[1] = slot.Output[0];
        slot.Output[0] = ToLinear(slot.WaveTable[(slot.PgOut + 2 * (fm >> 1)) & (PgWidth - 1)], slot, am);
        return (short)slot.Output[0];
    }

    private short CalcSlotMod(int ch)
    {
        OpllSlot slot = _slots[ch << 1];
        short fm = (short)(slot.Patch.Fb > 0 ? (slot.Output[1] + slot.Output[0]) >> (9 - slot.Patch.Fb) : 0);
        short am = (short)(slot.Patch.Am != 0 ? _lfoAm : 0);
        slot.Output[1] = slot.Output[0];
        slot.Output[0] = ToLinear(slot.WaveTable[(slot.PgOut + fm) & (PgWidth - 1)], slot, am);
        return (short)slot.Output[0];
    }

    private short CalcSlotTom()
    {
        OpllSlot slot = _slots[16];
        return ToLinear(slot.WaveTable[slot.PgOut], slot, 0);
    }

    private short CalcSlotSnare()
    {
        OpllSlot slot = _slots[15];
        uint phase = ((slot.PgOut >> (PgBits - 2)) & 1) != 0
            ? ((_noise & 1) != 0 ? 0x300u : 0x200u)
            : ((_noise & 1) != 0 ? 0x0u : 0x100u);

        return ToLinear(slot.WaveTable[phase & (PgWidth - 1)], slot, 0);
    }

    private short CalcSlotCym()
    {
        OpllSlot slot = _slots[17];
        uint phase = _shortNoise != 0 ? 0x300u : 0x100u;
        return ToLinear(slot.WaveTable[phase & (PgWidth - 1)], slot, 0);
    }

    private short CalcSlotHat()
    {
        OpllSlot slot = _slots[14];
        uint phase = _shortNoise != 0
            ? ((_noise & 1) != 0 ? 0x2D0u : 0x234u)
            : ((_noise & 1) != 0 ? 0x34u : 0xD0u);

        return ToLinear(slot.WaveTable[phase & (PgWidth - 1)], slot, 0);
    }

    private void UpdateOutput()
    {
        UpdateAmPm();
        UpdateShortNoise();
        UpdateSlots();

        // CH1-6
        for (int i = 0; i < 6; i++)
        {
            _chOut[i] = (short)(-(CalcSlotCar(i, CalcSlotMod(i))) >> 1);
        }

        // CH7 (Melodic vs Bass Drum)
        if (_rhythmMode == 0)
        {
            _chOut[6] = (short)(-(CalcSlotCar(6, CalcSlotMod(6))) >> 1);
        }
        else
        {
            _chOut[9] = CalcSlotCar(6, CalcSlotMod(6));
        }
        UpdateNoise(14);

        // CH8 (Melodic vs HiHat / Snare)
        if (_rhythmMode == 0)
        {
            _chOut[7] = (short)(-(CalcSlotCar(7, CalcSlotMod(7))) >> 1);
        }
        else
        {
            _chOut[10] = CalcSlotHat();
            _chOut[11] = CalcSlotSnare();
        }
        UpdateNoise(2);

        // CH9 (Melodic vs Tom / Cymbal)
        if (_rhythmMode == 0)
        {
            _chOut[8] = (short)(-(CalcSlotCar(8, CalcSlotMod(8))) >> 1);
        }
        else
        {
            _chOut[12] = CalcSlotTom();
            _chOut[13] = CalcSlotCym();
        }
        UpdateNoise(2);
    }

    private short CalcOneSample()
    {
        UpdateOutput();
        int sum = 0;
        for (int i = 0; i < 14; i++)
        {
            sum += _chOut[i];
        }
        return (short)Math.Clamp(sum, -32768, 32767);
    }

    public void WriteReg(uint reg, byte data)
    {
        if (reg >= 0x40) return;

        if ((0x19 <= reg && reg <= 0x1F) || (0x29 <= reg && reg <= 0x2F) || (0x39 <= reg && reg <= 0x3F))
        {
            reg -= 9;
        }

        _registers[reg] = data;

        switch (reg)
        {
            case 0x00:
                SetCustomPatchParam(0, data);
                break;
            case 0x01:
                SetCustomPatchParam(1, data);
                break;
            case 0x02:
                SetCustomPatchKlTl(0, data);
                break;
            case 0x03:
                SetCustomPatchKlWsFb(data);
                break;
            case 0x04:
                SetCustomPatchArDr(0, data);
                break;
            case 0x05:
                SetCustomPatchArDr(1, data);
                break;
            case 0x06:
                SetCustomPatchSlRr(0, data);
                break;
            case 0x07:
                SetCustomPatchSlRr(1, data);
                break;

            case 0x0E:
                _rhythmControl = data;
                _bassDrumOn = (data & 0x10) != 0;
                _snareDrumOn = (data & 0x08) != 0;
                _tomTomOn = (data & 0x04) != 0;
                _topCymbalOn = (data & 0x02) != 0;
                _hiHatOn = (data & 0x01) != 0;
                UpdateRhythmMode();
                UpdateKeyStatus();
                break;

            case 0x0F:
                _testFlag = data;
                break;

            case >= 0x10 and <= 0x18:
                int ch10 = (int)(reg - 0x10);
                SetFNumber(ch10, data + ((_registers[0x20 + ch10] & 1) << 8));
                break;

            case >= 0x20 and <= 0x28:
                int ch20 = (int)(reg - 0x20);
                _keyOn[ch20] = (data & 0x10) != 0;
                SetFNumber(ch20, ((data & 1) << 8) + _registers[0x10 + ch20]);
                SetBlock(ch20, (data >> 1) & 7);
                SetSusFlag(ch20, (data >> 5) & 1);
                UpdateKeyStatus();
                break;

            case >= 0x30 and <= 0x38:
                int ch30 = (int)(reg - 0x30);
                if ((_registers[0x0E] & 32) != 0 && reg >= 0x36)
                {
                    if (reg == 0x37)
                        SetSlotVolume(_slots[14], ((data >> 4) & 15) << 2);
                    else if (reg == 0x38)
                        SetSlotVolume(_slots[16], ((data >> 4) & 15) << 2);
                }
                else
                {
                    SetPatch(ch30, (data >> 4) & 15);
                }
                SetVolume(ch30, (data & 15) << 2);
                break;
        }
    }

    private byte _rhythmControl;

    private void SetCustomPatchParam(int slotType, byte data)
    {
        _patches[slotType].Am = (byte)((data >> 7) & 1);
        _patches[slotType].Pm = (byte)((data >> 6) & 1);
        _patches[slotType].Eg = (byte)((data >> 5) & 1);
        _patches[slotType].Kr = (byte)((data >> 4) & 1);
        _patches[slotType].Ml = (byte)(data & 15);
        for (int i = 0; i < 9; i++)
        {
            if (_patchNumber[i] == 0)
            {
                RequestUpdate(_slots[(i << 1) | slotType], 4 | 8);
            }
        }
    }

    private void SetCustomPatchKlTl(int slotType, byte data)
    {
        _patches[slotType].Kl = (byte)((data >> 6) & 3);
        _patches[slotType].Tl = (byte)(data & 63);
        for (int i = 0; i < 9; i++)
        {
            if (_patchNumber[i] == 0)
            {
                RequestUpdate(_slots[(i << 1) | slotType], 2);
            }
        }
    }

    private void SetCustomPatchKlWsFb(byte data)
    {
        _patches[1].Kl = (byte)((data >> 6) & 3);
        _patches[1].Ws = (byte)((data >> 4) & 1);
        _patches[0].Ws = (byte)((data >> 3) & 1);
        _patches[0].Fb = (byte)(data & 7);
        for (int i = 0; i < 9; i++)
        {
            if (_patchNumber[i] == 0)
            {
                RequestUpdate(_slots[i << 1], 1);
                RequestUpdate(_slots[(i << 1) | 1], 1 | 2);
            }
        }
    }

    private void SetCustomPatchArDr(int slotType, byte data)
    {
        _patches[slotType].Ar = (byte)((data >> 4) & 15);
        _patches[slotType].Dr = (byte)(data & 15);
        for (int i = 0; i < 9; i++)
        {
            if (_patchNumber[i] == 0)
            {
                RequestUpdate(_slots[(i << 1) | slotType], 8);
            }
        }
    }

    private void SetCustomPatchSlRr(int slotType, byte data)
    {
        _patches[slotType].Sl = (byte)((data >> 4) & 15);
        _patches[slotType].Rr = (byte)(data & 15);
        for (int i = 0; i < 9; i++)
        {
            if (_patchNumber[i] == 0)
            {
                RequestUpdate(_slots[(i << 1) | slotType], 8);
            }
        }
    }

    public void GeneratePcmSamples(float[] outputBuffer, int sampleCount, double sampleRateHz, ulong masterTicksToAdvance)
    {
        ArgumentNullException.ThrowIfNull(outputBuffer);
        if (sampleCount < 0 || sampleCount > outputBuffer.Length) throw new ArgumentOutOfRangeException(nameof(sampleCount));
        if (!double.IsFinite(sampleRateHz) || sampleRateHz <= 0) throw new ArgumentOutOfRangeException(nameof(sampleRateHz));
        ulong previous = 0;
        for (int s = 0; s < sampleCount; s++)
        {
            ulong boundary = (ulong)((UInt128)masterTicksToAdvance * (uint)(s + 1) / (uint)sampleCount);
            AdvanceMasterTicks(boundary - previous);
            previous = boundary;
            outputBuffer[s] = FmEnabled ? Math.Clamp(_lastSample / 4095.0f, -1.0f, 1.0f) : 0;
        }
        AdvanceMasterTicks(masterTicksToAdvance - previous);
    }

    private void AdvanceMasterTicks(ulong ticks)
    {
        UInt128 total = (UInt128)_masterTickRemainder + ticks;
        ulong steps = (ulong)(total / 216);
        _masterTickRemainder = (ulong)(total % 216);
        for (ulong i = 0; i < steps; i++) _lastSample = CalcOneSample();
    }
}
