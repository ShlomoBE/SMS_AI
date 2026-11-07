using System;
using System.Runtime.InteropServices;

namespace Sms.Desktop;

/// <summary>
/// Lightweight, zero-dependency low-latency 44.1 kHz 16-bit Mono PCM audio stream player.
/// Uses WinMM waveOut on Windows with cross-platform silent buffer fallback.
/// </summary>
public sealed class AudioPlayer : IDisposable
{
    private const int SampleRate = 44100;
    private const int Channels = 1;
    private const int BitsPerSample = 16;
    private const int BufferCount = 4;
    // Accommodate the larger 50 Hz PAL frame (882 16-bit mono samples).
    private const int BufferSize = 882 * sizeof(short);

    private readonly bool _isWindows;
    private readonly IntPtr _hWaveOut;
    private readonly WAVEHDR[] _headers;
    private readonly IntPtr[] _headerPtrs;
    private readonly byte[][] _buffers;
    private int _currentBuffer;
    private bool _disposed;

    [StructLayout(LayoutKind.Sequential)]
    private struct WAVEFORMATEX
    {
        public ushort wFormatTag;
        public ushort nChannels;
        public uint nSamplesPerSec;
        public uint nAvgBytesPerSec;
        public ushort nBlockAlign;
        public ushort wBitsPerSample;
        public ushort cbSize;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WAVEHDR
    {
        public IntPtr lpData;
        public uint dwBufferLength;
        public uint dwBytesRecorded;
        public IntPtr dwUser;
        public uint dwFlags;
        public uint dwLoops;
        public IntPtr lpNext;
        public IntPtr reserved;
    }

    private const uint WHDR_DONE = 0x00000001;
    private const uint WAVE_MAPPER = unchecked((uint)-1);

    [DllImport("winmm.dll")]
    private static extern int waveOutOpen(out IntPtr phwo, uint uDeviceID, ref WAVEFORMATEX pwfx, IntPtr dwCallback, IntPtr dwInstance, uint fdwOpen);

    [DllImport("winmm.dll")]
    private static extern int waveOutPrepareHeader(IntPtr hwo, IntPtr pwh, uint cbwh);

    [DllImport("winmm.dll")]
    private static extern int waveOutWrite(IntPtr hwo, IntPtr pwh, uint cbwh);

    [DllImport("winmm.dll")]
    private static extern int waveOutUnprepareHeader(IntPtr hwo, IntPtr pwh, uint cbwh);

    [DllImport("winmm.dll")]
    private static extern int waveOutClose(IntPtr hwo);

    [DllImport("winmm.dll")]
    private static extern int waveOutReset(IntPtr hwo);

    public AudioPlayer()
    {
        _isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
        _headers = new WAVEHDR[BufferCount];
        _headerPtrs = new IntPtr[BufferCount];
        _buffers = new byte[BufferCount][];

        if (!_isWindows) return;

        try
        {
            WAVEFORMATEX fmt = new()
            {
                wFormatTag = 1, // WAVE_FORMAT_PCM
                nChannels = (ushort)Channels,
                nSamplesPerSec = (uint)SampleRate,
                wBitsPerSample = (ushort)BitsPerSample,
                nBlockAlign = (ushort)(Channels * BitsPerSample / 8),
                nAvgBytesPerSec = (uint)(SampleRate * Channels * BitsPerSample / 8),
                cbSize = 0
            };

            int res = waveOutOpen(out _hWaveOut, WAVE_MAPPER, ref fmt, IntPtr.Zero, IntPtr.Zero, 0);
            if (res != 0)
            {
                _hWaveOut = IntPtr.Zero;
                return;
            }

            for (int i = 0; i < BufferCount; i++)
            {
                _buffers[i] = new byte[BufferSize];
                _headerPtrs[i] = Marshal.AllocHGlobal(Marshal.SizeOf<WAVEHDR>());

                WAVEHDR hdr = new()
                {
                    lpData = Marshal.AllocHGlobal(BufferSize),
                    dwBufferLength = (uint)BufferSize,
                    dwFlags = WHDR_DONE
                };

                _headers[i] = hdr;
                Marshal.StructureToPtr(hdr, _headerPtrs[i], false);
                waveOutPrepareHeader(_hWaveOut, _headerPtrs[i], (uint)Marshal.SizeOf<WAVEHDR>());
            }
        }
        catch
        {
            _hWaveOut = IntPtr.Zero;
        }
    }

    /// <summary>
    /// Enqueues audio float PCM samples normalized in [-1.0f .. +1.0f].
    /// </summary>
    public void PlaySamples(ReadOnlySpan<float> pcmSamples)
    {
        if (_hWaveOut == IntPtr.Zero || pcmSamples.IsEmpty) return;

        try
        {
            IntPtr headerPtr = _headerPtrs[_currentBuffer];
            WAVEHDR hdr = Marshal.PtrToStructure<WAVEHDR>(headerPtr);

            // Convert float PCM [-1.0f..1.0f] to 16-bit signed PCM
            byte[] buf = _buffers[_currentBuffer];
            int count = Math.Min(pcmSamples.Length, buf.Length / 2);

            for (int i = 0; i < count; i++)
            {
                float s = Math.Clamp(pcmSamples[i], -1.0f, 1.0f);
                short sample16 = (short)(s * 32767.0f);
                buf[i * 2] = (byte)(sample16 & 0xFF);
                buf[i * 2 + 1] = (byte)((sample16 >> 8) & 0xFF);
            }

            Marshal.Copy(buf, 0, hdr.lpData, count * 2);
            hdr.dwBufferLength = (uint)(count * 2);
            Marshal.StructureToPtr(hdr, headerPtr, false);

            waveOutWrite(_hWaveOut, headerPtr, (uint)Marshal.SizeOf<WAVEHDR>());
            _currentBuffer = (_currentBuffer + 1) % BufferCount;
        }
        catch
        {
            // Ignore audio buffer write errors silently
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_hWaveOut != IntPtr.Zero)
        {
            try
            {
                waveOutReset(_hWaveOut);
                for (int i = 0; i < BufferCount; i++)
                {
                    if (_headerPtrs[i] != IntPtr.Zero)
                    {
                        waveOutUnprepareHeader(_hWaveOut, _headerPtrs[i], (uint)Marshal.SizeOf<WAVEHDR>());
                        WAVEHDR hdr = Marshal.PtrToStructure<WAVEHDR>(_headerPtrs[i]);
                        if (hdr.lpData != IntPtr.Zero) Marshal.FreeHGlobal(hdr.lpData);
                        Marshal.FreeHGlobal(_headerPtrs[i]);
                    }
                }
                waveOutClose(_hWaveOut);
            }
            catch
            {
                // Ignore waveOut cleanup errors
            }
        }
    }
}
