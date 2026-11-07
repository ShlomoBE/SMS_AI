namespace Sms.Core.System;

/// <summary>
/// Specifies the display and timing standard for the Sega Master System console.
/// </summary>
public enum VideoStandard
{
    /// <summary>
    /// NTSC standard: 262 scanlines per frame, 60 Hz visible display (192 visible lines, line 192 VBlank IRQ).
    /// </summary>
    Ntsc,

    /// <summary>
    /// PAL standard: 313 scanlines per frame, 50 Hz visible display (192 visible lines, line 192 VBlank IRQ).
    /// </summary>
    Pal
}
