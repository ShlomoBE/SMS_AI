using System;
using Sms.Core.Fm;
using Sms.Core.Psg;

namespace Sms.Core.Audio;

/// <summary>
/// Specifies the hardware audio output rendering mode for the Sega Master System.
/// </summary>
public enum SmsAudioMode
{
    /// <summary>
    /// Automatic mode: PSG and FM enables follow Port $F2 hardware control flags.
    /// </summary>
    Auto,

    /// <summary>
    /// Forced PSG mode: PSG is always enabled, FM is disabled.
    /// </summary>
    ForcePsg,

    /// <summary>
    /// Forced FM mode: FM is always enabled, PSG is muted unless both are enabled.
    /// </summary>
    ForceFm
}

/// <summary>
/// Sega Master System Audio Mixer and Audio Mode Controller.
/// Manages SN76489 PSG and YM2413 FM sub-engines, routes bus port writes ($7F, $F0, $F1, $F2),
/// evaluates effective audio enables, and generates mixed PCM audio buffers with normalized gain weighting.
/// </summary>
public sealed class SmsAudioMixer
{
    private float[] _psgBuffer = Array.Empty<float>();
    private float[] _fmBuffer = Array.Empty<float>();

    /// <summary>
    /// Gets or sets the audio rendering mode. Defaults to <see cref="SmsAudioMode.Auto"/>.
    /// </summary>
    public SmsAudioMode Mode { get; set; } = SmsAudioMode.Auto;

    /// <summary>Host output gain for PSG; 1 is the existing mix level.</summary>
    public float PsgGain { get; set; } = 1f;

    /// <summary>Host output gain for FM; 1 is the existing mix level.</summary>
    public float FmGain { get; set; } = 1f;

    /// <summary>
    /// Gets the SN76489 Programmable Sound Generator sub-engine.
    /// </summary>
    public SN76489Psg Psg { get; }

    /// <summary>
    /// Gets the YM2413 FM sound synthesizer sub-engine.
    /// </summary>
    public YM2413Fm Fm { get; }

    /// <summary>
    /// Gets a value indicating whether PSG audio output is currently enabled based on <see cref="Mode"/> and Port $F2 audio control.
    /// </summary>
    public bool IsPsgEnabled => Mode switch
    {
        SmsAudioMode.ForcePsg => true,
        SmsAudioMode.ForceFm => !Fm.PsgMuted,
        _ => !Fm.PsgMuted // Auto
    };

    /// <summary>
    /// Gets a value indicating whether FM audio output is currently enabled based on <see cref="Mode"/> and Port $F2 audio control.
    /// </summary>
    public bool IsFmEnabled => Mode switch
    {
        SmsAudioMode.ForcePsg => false,
        SmsAudioMode.ForceFm => true,
        _ => Fm.FmEnabled // Auto
    };

    /// <summary>
    /// Initializes a new instance of <see cref="SmsAudioMixer"/> with optional PSG and FM sub-engine instances.
    /// </summary>
    public SmsAudioMixer(SN76489Psg? psg = null, YM2413Fm? fm = null)
    {
        Psg = psg ?? new SN76489Psg();
        Fm = fm ?? new YM2413Fm();
    }

    /// <summary>
    /// Routes write to Port $7F (PSG Data/Latch).
    /// </summary>
    public void WritePort7F(byte data) => Psg.WritePort(data);

    /// <summary>
    /// Routes write to Port $F0 (YM2413 Register Address).
    /// </summary>
    public void WritePortF0(byte data) => Fm.WritePortAddress(data);

    /// <summary>
    /// Routes write to Port $F1 (YM2413 Register Data).
    /// </summary>
    public void WritePortF1(byte data) => Fm.WritePortData(data);

    /// <summary>
    /// Routes write to Port $F2 (YM2413 Audio Control: Bit 0 = FM Enable, Bit 1 = PSG Mute).
    /// </summary>
    public void WritePortF2(byte data) => Fm.WriteAudioControl(data);

    /// <summary>
    /// Generates mixed mono PCM samples normalized in [-1.0f .. 1.0f] into <paramref name="outputBuffer"/>.
    /// Mixes active channels with gain weighting: PSG weight = 0.5f, FM weight = 0.5f when both active,
    /// 1.0f when single source active, and silence when neither source is active.
    /// </summary>
    public void GeneratePcmSamples(float[] outputBuffer, int sampleCount, double sampleRateHz, ulong masterTicksToAdvance)
    {
        ArgumentNullException.ThrowIfNull(outputBuffer);
        if (sampleCount < 0 || sampleCount > outputBuffer.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(sampleCount));
        }
        if (sampleCount == 0) return;

        EnsureScratchBuffers(sampleCount);

        bool psgActive = IsPsgEnabled;
        bool fmActive = IsFmEnabled;

        // Generate PSG samples (advances PSG internal clocks regardless of active/mute status)
        Psg.GeneratePcmSamples(_psgBuffer, sampleCount, sampleRateHz, masterTicksToAdvance);

        // Generate FM samples
        if (fmActive)
        {
            byte originalAudioControl = Fm.AudioControl;
            bool forcedFmBit = !Fm.FmEnabled;
            if (forcedFmBit)
            {
                Fm.WriteAudioControl((byte)(originalAudioControl | 0x01));
            }

            try
            {
                Fm.GeneratePcmSamples(_fmBuffer, sampleCount, sampleRateHz, masterTicksToAdvance);
            }
            finally
            {
                if (forcedFmBit)
                {
                    Fm.WriteAudioControl(originalAudioControl);
                }
            }
        }
        else
        {
            Array.Clear(_fmBuffer, 0, sampleCount);
        }

        // Mix active audio sources into outputBuffer
        if (psgActive && fmActive)
        {
            // Gain weighting: PSG = 0.5f, FM = 0.5f when both active
            for (int i = 0; i < sampleCount; i++)
            {
                float sample = _psgBuffer[i] * (0.5f * PsgGain) + _fmBuffer[i] * (0.5f * FmGain);
                outputBuffer[i] = Math.Clamp(sample, -1.0f, 1.0f);
            }
        }
        else if (psgActive)
        {
            // Gain weighting: PSG = 1.0f when single source active
            for (int i = 0; i < sampleCount; i++)
            {
                outputBuffer[i] = Math.Clamp(_psgBuffer[i] * PsgGain, -1.0f, 1.0f);
            }
        }
        else if (fmActive)
        {
            // Gain weighting: FM = 1.0f when single source active
            for (int i = 0; i < sampleCount; i++)
            {
                outputBuffer[i] = Math.Clamp(_fmBuffer[i] * FmGain, -1.0f, 1.0f);
            }
        }
        else
        {
            // Neither active: output silence
            Array.Clear(outputBuffer, 0, sampleCount);
        }
    }

    private void EnsureScratchBuffers(int requiredCapacity)
    {
        if (_psgBuffer.Length < requiredCapacity)
        {
            _psgBuffer = new float[requiredCapacity];
            _fmBuffer = new float[requiredCapacity];
        }
    }
}
