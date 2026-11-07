using System;

namespace Sms.Core.Fm;

/// <summary>
/// Envelope Generator phase state.
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
/// Subsystem representing an individual FM operator (Modulator or Carrier).
/// </summary>
public class OperatorEngine
{
    public EnvelopeState State { get; set; } = EnvelopeState.Off;
    public double AttenuationDb { get; set; } = 127.0; // 0.0 dB = max volume, 127.0 dB = silent
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
            AttenuationDb = 48.0; // EG attenuation starts at -48 dB (max 6-bit counter) on KeyOn
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
/// From-scratch YM2413 (OPLL) FM sound generator implementation for Sega Master System / Mark III.
/// </summary>
public class YM2413Fm
{
    private static readonly ushort[] LogSinTable = new ushort[256];
    private static readonly ushort[] ExpTable = new ushort[4096];

    static YM2413Fm()
    {
        for (int i = 0; i < 256; i++)
        {
            double sinVal = Math.Sin((i + 0.5) * Math.PI / 512.0);
            double logVal = -Math.Log2(sinVal) * 192.0;
            LogSinTable[i] = (ushort)Math.Clamp((int)Math.Round(logVal), 0, 65535);
        }

        for (int i = 0; i < 4096; i++)
        {
            double expVal = Math.Pow(2.0, -i / 192.0);
            ExpTable[i] = (ushort)Math.Clamp((int)Math.Round(expVal * 1024.0), 0, 65535);
        }
    }

    private byte _latchedAddress;
    private byte _audioControl;
    private readonly byte[] _registers = new byte[256];
    private readonly byte[] _customPatchRegs = new byte[8];

    // Channel state
    private readonly ushort[] _fNumber = new ushort[9];
    private readonly byte[] _block = new byte[9];
    private readonly bool[] _keyOn = new bool[9];
    private readonly bool[] _sustain = new bool[9];
    private readonly byte[] _patchIndex = new byte[9];
    private readonly byte[] _volume = new byte[9];

    // Operators per channel: 0 = Modulator, 1 = Carrier
    private readonly OperatorEngine[,] _operators = new OperatorEngine[9, 2];

    // Rhythm state
    private bool _rhythmEnable;
    private bool _bassDrumOn;
    private bool _snareDrumOn;
    private bool _tomTomOn;
    private bool _topCymbalOn;
    private bool _hiHatOn;

    // Rhythm operators
    private readonly OperatorEngine _bdMod = new();
    private readonly OperatorEngine _bdCar = new();
    private readonly OperatorEngine _sdCar = new();
    private readonly OperatorEngine _hhMod = new();
    private readonly OperatorEngine _tomCar = new();
    private readonly OperatorEngine _cymMod = new();

    private uint _noiseLfsr = 1;

    public YM2413Fm()
    {
        for (int ch = 0; ch < 9; ch++)
        {
            _operators[ch, 0] = new OperatorEngine();
            _operators[ch, 1] = new OperatorEngine();
        }
        Reset();
    }

    /// <summary>
    /// Resets registers, operators, and state.
    /// </summary>
    public void Reset()
    {
        _latchedAddress = 0;
        _audioControl = 0;
        Array.Clear(_registers, 0, _registers.Length);
        Array.Clear(_customPatchRegs, 0, _customPatchRegs.Length);
        Array.Clear(_fNumber, 0, _fNumber.Length);
        Array.Clear(_block, 0, _block.Length);
        Array.Clear(_keyOn, 0, _keyOn.Length);
        Array.Clear(_sustain, 0, _sustain.Length);
        Array.Clear(_patchIndex, 0, _patchIndex.Length);
        Array.Clear(_volume, 0, _volume.Length);

        _rhythmEnable = false;
        _bassDrumOn = false;
        _snareDrumOn = false;
        _tomTomOn = false;
        _topCymbalOn = false;
        _hiHatOn = false;

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

    #region Port Interface (Ports $F0, $F1, $F2)

    /// <summary>
    /// Writes register address to Port $F0.
    /// </summary>
    public void WritePortAddress(byte regAddress)
    {
        _latchedAddress = regAddress;
    }

    /// <summary>
    /// Writes register data to Port $F1 (using latched address).
    /// </summary>
    public void WritePortData(byte regData)
    {
        byte addr = _latchedAddress;
        _registers[addr] = regData;

        if (addr <= 0x07)
        {
            _customPatchRegs[addr] = regData;
        }
        else if (addr == 0x0E)
        {
            UpdateRhythmControl(regData);
        }
        else if (addr >= 0x10 && addr <= 0x18)
        {
            int ch = addr & 0x0F;
            _fNumber[ch] = (ushort)((_fNumber[ch] & 0x100) | regData);
        }
        else if (addr >= 0x20 && addr <= 0x28)
        {
            int ch = addr & 0x0F;
            ushort fHigh = (ushort)((regData & 0x01) << 8);
            _fNumber[ch] = (ushort)((_fNumber[ch] & 0x0FF) | fHigh);
            _block[ch] = (byte)((regData >> 1) & 0x07);
            _sustain[ch] = (regData & 0x20) != 0;

            bool newKeyOn = (regData & 0x10) != 0;
            if (newKeyOn != _keyOn[ch])
            {
                _keyOn[ch] = newKeyOn;
                OpllInstrumentPatch patch = GetChannelPatch(ch);
                if (newKeyOn)
                {
                    _operators[ch, 0].KeyOn(patch.Modulator.AttackRate);
                    _operators[ch, 1].KeyOn(patch.Carrier.AttackRate);
                }
                else
                {
                    _operators[ch, 0].KeyOff();
                    _operators[ch, 1].KeyOff();
                }
            }
        }
        else if (addr >= 0x30 && addr <= 0x38)
        {
            int ch = addr & 0x0F;
            _patchIndex[ch] = (byte)((regData >> 4) & 0x0F);
            _volume[ch] = (byte)(regData & 0x0F);
        }
    }

    /// <summary>
    /// Writes Audio Control byte to Port $F2.
    /// Bit 0: FM Enable, Bit 1: PSG Mute.
    /// </summary>
    public void WriteAudioControl(byte controlData)
    {
        _audioControl = controlData;
    }

    #endregion

    #region Inspection Properties

    public byte LatchedAddress => _latchedAddress;
    public byte AudioControl => _audioControl;
    public bool FmEnabled => (_audioControl & 0x01) != 0;
    public bool PsgMuted => (_audioControl & 0x02) != 0;

    public byte ReadRegister(byte regAddress) => _registers[regAddress];
    public ushort GetFNumber(int channel) => _fNumber[channel & 0x0F];
    public byte GetBlock(int channel) => _block[channel & 0x0F];
    public bool GetKeyOn(int channel) => _keyOn[channel & 0x0F];
    public bool GetSustain(int channel) => _sustain[channel & 0x0F];
    public byte GetPatchIndex(int channel) => _patchIndex[channel & 0x0F];
    public byte GetVolume(int channel) => _volume[channel & 0x0F];

    public bool RhythmEnable => _rhythmEnable;
    public bool RhythmBassDrumOn => _bassDrumOn;
    public bool RhythmSnareDrumOn => _snareDrumOn;
    public bool RhythmTomTomOn => _tomTomOn;
    public bool RhythmTopCymbalOn => _topCymbalOn;
    public bool RhythmHiHatOn => _hiHatOn;

    public OpllInstrumentPatch GetChannelPatch(int channel)
    {
        int pIdx = _patchIndex[channel & 0x0F];
        return FmPatchRom.GetPatch(pIdx, _customPatchRegs);
    }

    public byte[] GetCustomPatchRegisters() => (byte[])_customPatchRegs.Clone();

    #endregion

    private void UpdateRhythmControl(byte data)
    {
        _rhythmEnable = (data & 0x20) != 0;

        bool newBd = (data & 0x10) != 0;
        bool newSd = (data & 0x08) != 0;
        bool newTom = (data & 0x04) != 0;
        bool newCym = (data & 0x02) != 0;
        bool newHh = (data & 0x01) != 0;

        OpllInstrumentPatch ch6Patch = GetChannelPatch(6);
        OpllInstrumentPatch ch7Patch = GetChannelPatch(7);
        OpllInstrumentPatch ch8Patch = GetChannelPatch(8);

        if (newBd != _bassDrumOn)
        {
            _bassDrumOn = newBd;
            if (newBd) { _bdMod.KeyOn(ch6Patch.Modulator.AttackRate); _bdCar.KeyOn(ch6Patch.Carrier.AttackRate); }
            else { _bdMod.KeyOff(); _bdCar.KeyOff(); }
        }

        if (newSd != _snareDrumOn)
        {
            _sdDrumOnTransition(newSd, ch7Patch);
        }

        if (newTom != _tomTomOn)
        {
            _tomTomOn = newTom;
            if (newTom) _tomCar.KeyOn(ch8Patch.Carrier.AttackRate);
            else _tomCar.KeyOff();
        }

        if (newCym != _topCymbalOn)
        {
            _topCymbalOn = newCym;
            if (newCym) _cymMod.KeyOn(ch8Patch.Modulator.AttackRate);
            else _cymMod.KeyOff();
        }

        if (newHh != _hiHatOn)
        {
            _hiHatOn = newHh;
            if (newHh) _hhMod.KeyOn(ch7Patch.Modulator.AttackRate);
            else _hhMod.KeyOff();
        }
    }

    private void _sdDrumOnTransition(bool newSd, OpllInstrumentPatch ch7Patch)
    {
        _snareDrumOn = newSd;
        if (newSd) _sdCar.KeyOn(ch7Patch.Carrier.AttackRate);
        else _sdCar.KeyOff();
    }

    #region Sample Synthesis & Output

    /// <summary>
    /// Generates PCM samples into the provided output audio buffer.
    /// </summary>
    public void GeneratePcmSamples(float[] outputBuffer, int sampleCount, double sampleRateHz, ulong masterTicksToAdvance)
    {
        if (outputBuffer == null || sampleCount <= 0) return;

        if (!FmEnabled)
        {
            Array.Clear(outputBuffer, 0, sampleCount);
            return;
        }

        double ticksPerSample = (double)masterTicksToAdvance / Math.Max(1, sampleCount);

        for (int s = 0; s < sampleCount; s++)
        {
            AdvanceEngine(ticksPerSample);
            float sample = ComputeCurrentSample();
            outputBuffer[s] = Math.Clamp(sample, -1.0f, 1.0f);
        }
    }

    private void AdvanceEngine(double ticks)
    {
        // Update noise LFSR
        bool bit = ((_noiseLfsr & 1) ^ ((_noiseLfsr >> 2) & 1)) != 0;
        _noiseLfsr = (_noiseLfsr >> 1) ^ (bit ? 0x400000u : 0u);

        // Advance operators
        int activeChannels = _rhythmEnable ? 6 : 9;
        for (int ch = 0; ch < activeChannels; ch++)
        {
            AdvanceChannel(ch, ticks);
        }

        if (_rhythmEnable)
        {
            AdvanceOperator(_bdMod, _fNumber[6], _block[6], GetChannelPatch(6).Modulator, false, ticks);
            AdvanceOperator(_bdCar, _fNumber[6], _block[6], GetChannelPatch(6).Carrier, false, ticks);
            AdvanceOperator(_sdCar, _fNumber[7], _block[7], GetChannelPatch(7).Carrier, false, ticks);
            AdvanceOperator(_hhMod, _fNumber[7], _block[7], GetChannelPatch(7).Modulator, false, ticks);
            AdvanceOperator(_tomCar, _fNumber[8], _block[8], GetChannelPatch(8).Carrier, false, ticks);
            AdvanceOperator(_cymMod, _fNumber[8], _block[8], GetChannelPatch(8).Modulator, false, ticks);
        }
    }

    private void AdvanceChannel(int ch, double ticks)
    {
        OpllInstrumentPatch patch = GetChannelPatch(ch);
        bool sus = _sustain[ch];

        AdvanceOperator(_operators[ch, 0], _fNumber[ch], _block[ch], patch.Modulator, sus, ticks);
        AdvanceOperator(_operators[ch, 1], _fNumber[ch], _block[ch], patch.Carrier, sus, ticks);
    }

    private void AdvanceOperator(OperatorEngine op, ushort fNum, byte block, OperatorParameters params_, bool sustainFlag, double ticks)
    {
        if (op.State == EnvelopeState.Off) return;

        // Phase advance
        double mult = params_.Multiplier == 0 ? 0.5 : params_.Multiplier;
        double phaseStep = (fNum << block) * mult;
        op.PhaseAccumulator += (uint)(phaseStep * ticks * 4.0);

        // Envelope advance based on elapsed time in seconds
        double timeSec = ticks / 3579545.0;

        switch (op.State)
        {
            case EnvelopeState.Attack:
                if (params_.AttackRate == 15)
                {
                    op.AttenuationDb = 0.0;
                    op.State = EnvelopeState.Decay;
                }
                else
                {
                    double attackSpeed = Math.Pow(2, params_.AttackRate) * 16.0;
                    op.AttenuationDb -= attackSpeed * timeSec;
                    if (op.AttenuationDb <= 0.0)
                    {
                        op.AttenuationDb = 0.0;
                        op.State = EnvelopeState.Decay;
                    }
                }
                break;

            case EnvelopeState.Decay:
                double sustainDb = params_.SustainLevel * 3.0;
                double decaySpeed = Math.Pow(2, params_.DecayRate) * 2.0;
                op.AttenuationDb += decaySpeed * timeSec;
                if (op.AttenuationDb >= sustainDb)
                {
                    op.AttenuationDb = sustainDb;
                    op.State = EnvelopeState.Sustain;
                }
                break;

            case EnvelopeState.Sustain:
                if (!params_.EgType)
                {
                    // Percussive sound: decay continues
                    double percussiveSpeed = Math.Pow(2, params_.ReleaseRate) * 2.0;
                    op.AttenuationDb += percussiveSpeed * timeSec;
                    if (op.AttenuationDb >= 127.0)
                    {
                        op.AttenuationDb = 127.0;
                        op.State = EnvelopeState.Off;
                    }
                }
                break;

            case EnvelopeState.Release:
                double rate = sustainFlag ? 5.0 : params_.ReleaseRate;
                double releaseSpeed = Math.Pow(2, rate) * 3.0;
                op.AttenuationDb += releaseSpeed * timeSec;
                if (op.AttenuationDb >= 127.0)
                {
                    op.AttenuationDb = 127.0;
                    op.State = EnvelopeState.Off;
                }
                break;
        }
    }

    private float ComputeCurrentSample()
    {
        double sum = 0.0;

        int activeCount = _rhythmEnable ? 6 : 9;
        for (int ch = 0; ch < activeCount; ch++)
        {
            sum += ComputeMelodicChannelSample(ch);
        }

        if (_rhythmEnable)
        {
            sum += ComputeRhythmSample();
        }

        double normalized = sum / (10.0 * 1024.0);
        return (float)normalized;
    }

    private double ComputeMelodicChannelSample(int ch)
    {
        OpllInstrumentPatch patch = GetChannelPatch(ch);
        OperatorEngine mod = _operators[ch, 0];
        OperatorEngine car = _operators[ch, 1];

        if (car.State == EnvelopeState.Off) return 0.0;

        // Modulator output
        double modAtten = mod.AttenuationDb + (patch.Modulator.TotalLevel * 0.75);
        int feedbackShift = patch.Modulator.Feedback == 0 ? 0 : (9 - patch.Modulator.Feedback);
        uint fbPhase = feedbackShift == 0 ? 0u : (uint)((mod.OutputPrev1 + mod.OutputPrev2) >> feedbackShift);

        int modOut = LookupSin(mod.PhaseAccumulator + fbPhase, patch.Modulator.Waveform, modAtten);
        mod.OutputPrev2 = mod.OutputPrev1;
        mod.OutputPrev1 = modOut;

        // Carrier output modulated by Modulator
        double carAtten = car.AttenuationDb + (_volume[ch] * 3.0);
        uint carPhaseMod = (uint)(car.PhaseAccumulator + (modOut * 4));
        int carOut = LookupSin(carPhaseMod, patch.Carrier.Waveform, carAtten);

        return carOut;
    }

    private double ComputeRhythmSample()
    {
        double sum = 0.0;
        OpllInstrumentPatch patch6 = GetChannelPatch(6);
        OpllInstrumentPatch patch7 = GetChannelPatch(7);
        OpllInstrumentPatch patch8 = GetChannelPatch(8);

        // Bass Drum (Ch 6)
        if (_bassDrumOn && _bdCar.State != EnvelopeState.Off)
        {
            double modAtten = _bdMod.AttenuationDb + (patch6.Modulator.TotalLevel * 0.75);
            int modOut = LookupSin(_bdMod.PhaseAccumulator, patch6.Modulator.Waveform, modAtten);
            double carAtten = _bdCar.AttenuationDb + (_volume[6] * 3.0);
            int bdOut = LookupSin((uint)(_bdCar.PhaseAccumulator + (modOut * 4)), patch6.Carrier.Waveform, carAtten);
            sum += bdOut;
        }

        // Snare Drum (Ch 7 Carrier + Noise)
        if (_snareDrumOn && _sdCar.State != EnvelopeState.Off)
        {
            bool noiseBit = (_noiseLfsr & 1) != 0;
            double carAtten = _sdCar.AttenuationDb + (_volume[7] * 3.0);
            uint phase = noiseBit ? (_sdCar.PhaseAccumulator + 0x10000u) : _sdCar.PhaseAccumulator;
            int sdOut = LookupSin(phase, patch7.Carrier.Waveform, carAtten);
            sum += sdOut;
        }

        // High Hat (Ch 7 Modulator + Noise)
        if (_hiHatOn && _hhMod.State != EnvelopeState.Off)
        {
            bool noiseBit = (_noiseLfsr & 1) != 0;
            double modAtten = _hhMod.AttenuationDb + (patch7.Modulator.TotalLevel * 0.75);
            uint phase = noiseBit ? (_hhMod.PhaseAccumulator + 0x8000u) : _hhMod.PhaseAccumulator;
            int hhOut = LookupSin(phase, patch7.Modulator.Waveform, modAtten);
            sum += hhOut * 0.5;
        }

        // Tom Tom (Ch 8 Carrier)
        if (_tomTomOn && _tomCar.State != EnvelopeState.Off)
        {
            double carAtten = _tomCar.AttenuationDb + (_volume[8] * 3.0);
            int tomOut = LookupSin(_tomCar.PhaseAccumulator, patch8.Carrier.Waveform, carAtten);
            sum += tomOut;
        }

        // Top Cymbal (Ch 8 Modulator)
        if (_topCymbalOn && _cymMod.State != EnvelopeState.Off)
        {
            double modAtten = _cymMod.AttenuationDb + (patch8.Modulator.TotalLevel * 0.75);
            int cymOut = LookupSin(_cymMod.PhaseAccumulator, patch8.Modulator.Waveform, modAtten);
            sum += cymOut * 0.5;
        }

        return sum;
    }

    private static int LookupSin(uint phase, byte waveform, double attenuationDb)
    {
        if (attenuationDb >= 127.0) return 0;

        uint phase10 = (phase >> 10) & 0x3FF;
        uint quadrant = (phase10 >> 8) & 3;
        uint idx = phase10 & 255;
        uint tableIdx = (quadrant == 1 || quadrant == 3) ? (255 - idx) : idx;
        ushort logSin = LogSinTable[tableIdx];
        bool isNegative = quadrant >= 2;

        if (waveform == 1 && isNegative)
            return 0;

        int logAtten = (int)(attenuationDb * 32.0);
        int totalLog = logSin + logAtten;

        if (totalLog >= 4096)
            return 0;

        int amp = ExpTable[totalLog];
        return (isNegative && waveform == 0) ? -amp : amp;
    }

    #endregion
}
