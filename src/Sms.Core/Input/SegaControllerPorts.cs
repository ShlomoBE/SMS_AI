namespace Sms.Core.Input;

/// <summary>
/// Controller button inputs for Sega Master System controllers.
/// Bit positions correspond directly to the joypad hardware pin layout.
/// Active-low polarity: 1 = released, 0 = pressed.
/// </summary>
[Flags]
public enum SmsButtons
{
    None = 0,
    Up = 1 << 0,       // Bit 0: Up
    Down = 1 << 1,     // Bit 1: Down
    Left = 1 << 2,     // Bit 2: Left
    Right = 1 << 3,    // Bit 3: Right
    Button1 = 1 << 4,  // Bit 4: Button 1 (TL)
    Button2 = 1 << 5   // Bit 5: Button 2 (TR)
}

/// <summary>
/// Emulates Sega Master System Controller &amp; I/O Ports ($DC, $DD, and $3F control register).
/// SMS Hardware Specifications:
/// - Port $DC: Joypad 1 (bits 0-5) and Joypad 2 low bits (Up/Down, bits 6-7).
/// - Port $DD: Joypad 2 high bits (Left/Right/TL/TR, bits 0-3), Reset (bit 4), Unused (bit 5=1), TH pins (bits 6-7).
/// - Port $3F: I/O Control Register for TR/TH direction and output levels.
/// </summary>
public sealed class SegaControllerPorts
{
    private byte _joypad1State = 0x3F; // Active-low 6 bits: 1 = released, 0 = pressed
    private byte _joypad2State = 0x3F; // Active-low 6 bits: 1 = released, 0 = pressed

    private byte _ioControl = 0xFF;    // Port $3F direction & output levels

    private bool _resetPressed;
    private bool _pausePressed;
    private bool _pauseNmiPending;

    private bool _joypad1ThInput = true;
    private bool _joypad2ThInput = true;

    /// <summary>
    /// Sets the state of one or more buttons for the specified player (1 or 2).
    /// </summary>
    /// <param name="player">1 for Player 1 (Joypad 1), 2 for Player 2 (Joypad 2).</param>
    /// <param name="button">The button or button mask to update.</param>
    /// <param name="pressed">true if pressed (active-low bit 0); false if released (bit 1).</param>
    public void SetButtonState(int player, SmsButtons button, bool pressed)
    {
        byte mask = (byte)button;
        if (mask == 0) return;

        if (player == 1 || player == 0)
        {
            if (pressed)
                _joypad1State = (byte)(_joypad1State & ~mask);
            else
                _joypad1State |= mask;
        }
        else if (player == 2)
        {
            if (pressed)
                _joypad2State = (byte)(_joypad2State & ~mask);
            else
                _joypad2State |= mask;
        }
        else
        {
            throw new ArgumentOutOfRangeException(nameof(player), "Player must be 1 (Joypad 1) or 2 (Joypad 2).");
        }
    }

    /// <summary>
    /// Sets the Console Pause button state.
    /// Transition from released (false) to pressed (true) triggers a pending NMI.
    /// </summary>
    public void SetPauseState(bool pressed)
    {
        if (!_pausePressed && pressed)
        {
            _pauseNmiPending = true;
        }
        _pausePressed = pressed;
    }

    /// <summary>
    /// Sets the Console Reset button state.
    /// Active-low in Port $DD bit 4 (0 = pressed, 1 = released).
    /// </summary>
    public void SetResetState(bool pressed)
    {
        _resetPressed = pressed;
    }

    /// <summary>
    /// Checks if a Pause NMI transition is pending. Returns true on edge trigger and clears the flag.
    /// </summary>
    public bool CheckPauseNmiPending()
    {
        bool pending = _pauseNmiPending;
        _pauseNmiPending = false;
        return pending;
    }

    /// <summary>
    /// Sets external TH pin input level for Joypad 1 or 2 when configured as input.
    /// </summary>
    public void SetThInputState(int player, bool high)
    {
        if (player == 1 || player == 0)
            _joypad1ThInput = high;
        else if (player == 2)
            _joypad2ThInput = high;
        else
            throw new ArgumentOutOfRangeException(nameof(player), "Player must be 1 or 2.");
    }

    /// <summary>
    /// Reads Port $DC (Joypad 1 &amp; Joypad 2 low bits).
    /// Bit 0: Joypad 1 Up
    /// Bit 1: Joypad 1 Down
    /// Bit 2: Joypad 1 Left
    /// Bit 3: Joypad 1 Right
    /// Bit 4: Joypad 1 Button 1 (TL)
    /// Bit 5: Joypad 1 Button 2 (TR)
    /// Bit 6: Joypad 2 Up
    /// Bit 7: Joypad 2 Down
    /// </summary>
    public byte ReadDc()
    {
        int result = 0;

        // Bit 0: J1 Up
        if ((_joypad1State & 0x01) != 0) result |= 0x01;
        // Bit 1: J1 Down
        if ((_joypad1State & 0x02) != 0) result |= 0x02;
        // Bit 2: J1 Left
        if ((_joypad1State & 0x04) != 0) result |= 0x04;
        // Bit 3: J1 Right
        if ((_joypad1State & 0x08) != 0) result |= 0x08;
        // Bit 4: J1 Button 1 (TL)
        if ((_joypad1State & 0x10) != 0) result |= 0x10;

        // Bit 5: J1 Button 2 / TR Pin
        bool j1TrDirectionInput = (_ioControl & 0x01) != 0;
        if (j1TrDirectionInput)
        {
            if ((_joypad1State & 0x20) != 0) result |= 0x20;
        }
        else
        {
            bool j1TrOutputLevel = (_ioControl & 0x10) != 0;
            if (j1TrOutputLevel) result |= 0x20;
        }

        // Bit 6: J2 Up (bit 0 of J2)
        if ((_joypad2State & 0x01) != 0) result |= 0x40;
        // Bit 7: J2 Down (bit 1 of J2)
        if ((_joypad2State & 0x02) != 0) result |= 0x80;

        return (byte)result;
    }

    /// <summary>
    /// Alias for ReadDc.
    /// </summary>
    public byte ReadPortDc() => ReadDc();

    /// <summary>
    /// Reads Port $DD (Joypad 2 high bits &amp; System).
    /// Bit 0: Joypad 2 Left
    /// Bit 1: Joypad 2 Right
    /// Bit 2: Joypad 2 Button 1 (TL)
    /// Bit 3: Joypad 2 Button 2 (TR)
    /// Bit 4: Reset Button (0 = pressed, 1 = released)
    /// Bit 5: Unused (1)
    /// Bit 6: Joypad 1 TH pin input/output state
    /// Bit 7: Joypad 2 TH pin input/output state
    /// </summary>
    public byte ReadDd()
    {
        int result = 0;

        // Bit 0: J2 Left (bit 2 of J2)
        if ((_joypad2State & 0x04) != 0) result |= 0x01;
        // Bit 1: J2 Right (bit 3 of J2)
        if ((_joypad2State & 0x08) != 0) result |= 0x02;
        // Bit 2: J2 Button 1 (TL) (bit 4 of J2)
        if ((_joypad2State & 0x10) != 0) result |= 0x04;

        // Bit 3: J2 Button 2 / TR Pin
        bool j2TrDirectionInput = (_ioControl & 0x04) != 0;
        if (j2TrDirectionInput)
        {
            if ((_joypad2State & 0x20) != 0) result |= 0x08;
        }
        else
        {
            bool j2TrOutputLevel = (_ioControl & 0x40) != 0;
            if (j2TrOutputLevel) result |= 0x08;
        }

        // Bit 4: Reset Button (0 = pressed, 1 = released; default 1)
        if (!_resetPressed) result |= 0x10;

        // Bit 5: Unused (always 1)
        result |= 0x20;

        // Bit 6: J1 TH Pin State
        bool j1ThDirectionInput = (_ioControl & 0x02) != 0;
        if (j1ThDirectionInput)
        {
            if (_joypad1ThInput) result |= 0x40;
        }
        else
        {
            bool j1ThOutputLevel = (_ioControl & 0x20) != 0;
            if (j1ThOutputLevel) result |= 0x40;
        }

        // Bit 7: J2 TH Pin State
        bool j2ThDirectionInput = (_ioControl & 0x08) != 0;
        if (j2ThDirectionInput)
        {
            if (_joypad2ThInput) result |= 0x80;
        }
        else
        {
            bool j2ThOutputLevel = (_ioControl & 0x80) != 0;
            if (j2ThOutputLevel) result |= 0x80;
        }

        return (byte)result;
    }

    /// <summary>
    /// Alias for ReadDd.
    /// </summary>
    public byte ReadPortDd() => ReadDd();

    /// <summary>
    /// Writes Port $3F (I/O Control Register).
    /// Bit 0: Joypad 1 TR Direction (1 = Input, 0 = Output)
    /// Bit 1: Joypad 1 TH Direction (1 = Input, 0 = Output)
    /// Bit 2: Joypad 2 TR Direction (1 = Input, 0 = Output)
    /// Bit 3: Joypad 2 TH Direction (1 = Input, 0 = Output)
    /// Bit 4: Joypad 1 TR Output Level (0 or 1)
    /// Bit 5: Joypad 1 TH Output Level (0 or 1)
    /// Bit 6: Joypad 2 TR Output Level (0 or 1)
    /// Bit 7: Joypad 2 TH Output Level (0 or 1)
    /// </summary>
    public void Write3F(byte value)
    {
        _ioControl = value;
    }

    /// <summary>
    /// Alias for Write3F.
    /// </summary>
    public void WritePort3F(byte value) => Write3F(value);

    /// <summary>
    /// Reads current value of Port $3F I/O Control Register.
    /// </summary>
    public byte Read3F() => _ioControl;

    /// <summary>
    /// Alias for Read3F.
    /// </summary>
    public byte ReadPort3F() => Read3F();

    /// <summary>
    /// General bus port read dispatcher for SMS input ports ($DC, $DD, $3F).
    /// </summary>
    public byte ReadPort(ushort port)
    {
        byte lower = (byte)(port & 0xFF);
        if (lower == 0xDC) return ReadDc();
        if (lower == 0xDD) return ReadDd();
        if (lower == 0x3F) return Read3F();
        return 0xFF;
    }

    /// <summary>
    /// General bus port write dispatcher for SMS input ports ($3F).
    /// </summary>
    public void WritePort(ushort port, byte value)
    {
        byte lower = (byte)(port & 0xFF);
        if (lower == 0x3F)
        {
            Write3F(value);
        }
    }
}
