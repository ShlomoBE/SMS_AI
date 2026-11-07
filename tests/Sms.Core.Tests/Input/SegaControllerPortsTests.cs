using Sms.Core.Input;

namespace Sms.Core.Tests.Input;

public sealed class SegaControllerPortsTests
{
    [Fact]
    public void DefaultPowerOnState_ReturnsAllBitsHigh()
    {
        var ports = new SegaControllerPorts();

        Assert.Equal(0xFF, ports.ReadDc());
        Assert.Equal(0xFF, ports.ReadDd());
        Assert.Equal(0xFF, ports.Read3F());
        Assert.Equal(0xFF, ports.ReadPortDc());
        Assert.Equal(0xFF, ports.ReadPortDd());
        Assert.Equal(0xFF, ports.ReadPort3F());
        Assert.Equal(0xFF, ports.ReadPort(0xDC));
        Assert.Equal(0xFF, ports.ReadPort(0xDD));
        Assert.Equal(0xFF, ports.ReadPort(0x3F));
        Assert.False(ports.CheckPauseNmiPending());
    }

    [Theory]
    [InlineData(SmsButtons.Up, 0xFE, 0xFF)]      // Bit 0
    [InlineData(SmsButtons.Down, 0xFD, 0xFF)]    // Bit 1
    [InlineData(SmsButtons.Left, 0xFB, 0xFF)]    // Bit 2
    [InlineData(SmsButtons.Right, 0xF7, 0xFF)]   // Bit 3
    [InlineData(SmsButtons.Button1, 0xEF, 0xFF)] // Bit 4
    [InlineData(SmsButtons.Button2, 0xDF, 0xFF)] // Bit 5
    public void Player1_IndividualButtonPressAndRelease_UpdatesPortDc(SmsButtons button, byte expectedDcPressed, byte expectedDdPpressed)
    {
        var ports = new SegaControllerPorts();

        // Press button
        ports.SetButtonState(1, button, true);
        Assert.Equal(expectedDcPressed, ports.ReadDc());
        Assert.Equal(expectedDdPpressed, ports.ReadDd());

        // Release button
        ports.SetButtonState(1, button, false);
        Assert.Equal(0xFF, ports.ReadDc());
        Assert.Equal(0xFF, ports.ReadDd());
    }

    [Theory]
    [InlineData(SmsButtons.Up, 0xBF, 0xFF)]      // J2 Up -> Port DC Bit 6
    [InlineData(SmsButtons.Down, 0x7F, 0xFF)]    // J2 Down -> Port DC Bit 7
    [InlineData(SmsButtons.Left, 0xFF, 0xFE)]    // J2 Left -> Port DD Bit 0
    [InlineData(SmsButtons.Right, 0xFF, 0xFD)]   // J2 Right -> Port DD Bit 1
    [InlineData(SmsButtons.Button1, 0xFF, 0xFB)] // J2 Button 1 -> Port DD Bit 2
    [InlineData(SmsButtons.Button2, 0xFF, 0xF7)] // J2 Button 2 -> Port DD Bit 3
    public void Player2_IndividualButtonPressAndRelease_UpdatesPortDcAndDd(SmsButtons button, byte expectedDcPressed, byte expectedDdPressed)
    {
        var ports = new SegaControllerPorts();

        // Press button
        ports.SetButtonState(2, button, true);
        Assert.Equal(expectedDcPressed, ports.ReadDc());
        Assert.Equal(expectedDdPressed, ports.ReadDd());

        // Release button
        ports.SetButtonState(2, button, false);
        Assert.Equal(0xFF, ports.ReadDc());
        Assert.Equal(0xFF, ports.ReadDd());
    }

    [Fact]
    public void SimultaneousButtonCombinations_And_CrossControllerIsolation()
    {
        var ports = new SegaControllerPorts();

        // Player 1: Up (bit 0) + Right (bit 3) + Button1 (bit 4)
        ports.SetButtonState(1, SmsButtons.Up | SmsButtons.Right | SmsButtons.Button1, true);

        // Expect Port DC: 0xFF & ~(0x01 | 0x08 | 0x10) = 0xE6
        Assert.Equal(0xE6, ports.ReadDc());
        // Player 2 / Port DD should remain isolated (0xFF)
        Assert.Equal(0xFF, ports.ReadDd());

        // Now press Player 2: Down (DC bit 7) + Button2 (DD bit 3)
        ports.SetButtonState(2, SmsButtons.Down | SmsButtons.Button2, true);

        // Expect Port DC: 0xE6 & ~(0x80) = 0x66
        Assert.Equal(0x66, ports.ReadDc());
        // Expect Port DD: 0xFF & ~(0x08) = 0xF7
        Assert.Equal(0xF7, ports.ReadDd());

        // Releasing Player 1 should leave Player 2 pressed
        ports.SetButtonState(1, SmsButtons.Up | SmsButtons.Right | SmsButtons.Button1, false);
        Assert.Equal(0x7F, ports.ReadDc()); // J2 Down still pressed (bit 7 = 0)
        Assert.Equal(0xF7, ports.ReadDd()); // J2 Button2 still pressed (bit 3 = 0)

        // Releasing Player 2 restores default state
        ports.SetButtonState(2, SmsButtons.Down | SmsButtons.Button2, false);
        Assert.Equal(0xFF, ports.ReadDc());
        Assert.Equal(0xFF, ports.ReadDd());
    }

    [Fact]
    public void Port3F_TR1_DirectionAndOutputOverrideLogic()
    {
        var ports = new SegaControllerPorts();

        // TR1 is Port $DC bit 5. $3F bit 0 = Direction (1=In, 0=Out), bit 4 = Output level.
        // Default: TR1 is Input ($3F = 0xFF). J1 Button 2 pressed -> bit 5 = 0 (0xDF).
        ports.SetButtonState(1, SmsButtons.Button2, true);
        Assert.Equal(0xDF, ports.ReadDc());

        // Set TR1 to Output = 1 ($3F = 0xFF, i.e., bit 0=0: 0xFE, bit 4=1: 0x10 -> 0xFE)
        ports.Write3F(0xFE); // Bit 0 = 0 (Output), Bit 4 = 1 (Output High)
        // Even though Button2 is pressed, output level override forces bit 5 to 1 (high).
        Assert.Equal(0xFF, ports.ReadDc());

        // Set TR1 to Output = 0 ($3F: bit 0=0, bit 4=0 -> 0xEE)
        ports.Write3F(0xEE); // Bit 0 = 0 (Output), Bit 4 = 0 (Output Low)
        // Release Button2
        ports.SetButtonState(1, SmsButtons.Button2, false);
        // Even though Button2 is released, output level override forces bit 5 to 0 (low).
        Assert.Equal(0xDF, ports.ReadDc());

        // Restore Input mode ($3F bit 0 = 1 -> 0xFF)
        ports.Write3F(0xFF);
        Assert.Equal(0xFF, ports.ReadDc());
    }

    [Fact]
    public void Port3F_TH1_DirectionAndOutputOverrideLogic()
    {
        var ports = new SegaControllerPorts();

        // TH1 is Port $DD bit 6. $3F bit 1 = Direction (1=In, 0=Out), bit 5 = Output level.
        // Default: TH1 Input = 1 ($DD bit 6 = 1).
        Assert.Equal(0xFF, ports.ReadDd());

        // Set TH1 Output = 0 ($3F bit 1 = 0, bit 5 = 0 -> 0xDD)
        ports.Write3F(0xDD);
        Assert.Equal(0xBF, ports.ReadDd()); // Bit 6 cleared (0xBF)

        // Set TH1 Output = 1 ($3F bit 1 = 0, bit 5 = 1 -> 0xFD)
        ports.Write3F(0xFD);
        Assert.Equal(0xFF, ports.ReadDd()); // Bit 6 set (0xFF)

        // Restore TH1 Input mode, test external TH input set low
        ports.Write3F(0xFF);
        ports.SetThInputState(1, false);
        Assert.Equal(0xBF, ports.ReadDd());

        ports.SetThInputState(1, true);
        Assert.Equal(0xFF, ports.ReadDd());
    }

    [Fact]
    public void Port3F_TR2_DirectionAndOutputOverrideLogic()
    {
        var ports = new SegaControllerPorts();

        // TR2 is Port $DD bit 3. $3F bit 2 = Direction (1=In, 0=Out), bit 6 = Output level.
        // Press J2 Button 2 in Input mode -> $DD bit 3 = 0 (0xF7)
        ports.SetButtonState(2, SmsButtons.Button2, true);
        Assert.Equal(0xF7, ports.ReadDd());

        // Set TR2 Output = 1 ($3F bit 2 = 0, bit 6 = 1 -> 0xFB)
        ports.Write3F(0xFB);
        Assert.Equal(0xFF, ports.ReadDd()); // Bit 3 forced to 1

        // Set TR2 Output = 0 ($3F bit 2 = 0, bit 6 = 0 -> 0xBB)
        ports.Write3F(0xBB);
        ports.SetButtonState(2, SmsButtons.Button2, false);
        Assert.Equal(0xF7, ports.ReadDd()); // Bit 3 forced to 0

        // Restore Input mode
        ports.Write3F(0xFF);
        Assert.Equal(0xFF, ports.ReadDd());
    }

    [Fact]
    public void Port3F_TH2_DirectionAndOutputOverrideLogic()
    {
        var ports = new SegaControllerPorts();

        // TH2 is Port $DD bit 7. $3F bit 3 = Direction (1=In, 0=Out), bit 7 = Output level.
        // Set TH2 Output = 0 ($3F bit 3 = 0, bit 7 = 0 -> 0x77)
        ports.Write3F(0x77);
        Assert.Equal(0x7F, ports.ReadDd()); // Bit 7 cleared

        // Set TH2 Output = 1 ($3F bit 3 = 0, bit 7 = 1 -> 0xF7)
        ports.Write3F(0xF7);
        Assert.Equal(0xFF, ports.ReadDd()); // Bit 7 set

        // Restore Input mode, test external TH input set low
        ports.Write3F(0xFF);
        ports.SetThInputState(2, false);
        Assert.Equal(0x7F, ports.ReadDd());

        ports.SetThInputState(2, true);
        Assert.Equal(0xFF, ports.ReadDd());
    }

    [Fact]
    public void ConsolePauseButton_TriggersSingleNmiPerPressTransition()
    {
        var ports = new SegaControllerPorts();

        Assert.False(ports.CheckPauseNmiPending());

        // 1. Press Pause: transition released -> pressed
        ports.SetPauseState(true);
        Assert.True(ports.CheckPauseNmiPending());

        // 2. Clear on read: second check returns false
        Assert.False(ports.CheckPauseNmiPending());

        // 3. Held button does not re-trigger
        ports.SetPauseState(true);
        Assert.False(ports.CheckPauseNmiPending());

        // 4. Release Pause
        ports.SetPauseState(false);
        Assert.False(ports.CheckPauseNmiPending());

        // 5. Press Pause again: transition released -> pressed triggers NMI again
        ports.SetPauseState(true);
        Assert.True(ports.CheckPauseNmiPending());
    }

    [Fact]
    public void ConsoleResetButton_UpdatesPortDdBIt4()
    {
        var ports = new SegaControllerPorts();

        // Default: released -> bit 4 = 1 (0xFF)
        Assert.Equal(0xFF, ports.ReadDd());

        // Press Reset -> bit 4 = 0 (0xEF)
        ports.SetResetState(true);
        Assert.Equal(0xEF, ports.ReadDd());

        // Release Reset -> bit 4 = 1 (0xFF)
        ports.SetResetState(false);
        Assert.Equal(0xFF, ports.ReadDd());
    }

    [Fact]
    public void GeneralBusReadAndWriteMethods_MatchPortSpecificMethods()
    {
        var ports = new SegaControllerPorts();

        ports.WritePort(0x3F, 0xEE);
        Assert.Equal(0xEE, ports.ReadPort(0x3F));
        Assert.Equal(0xEE, ports.Read3F());

        ports.SetButtonState(1, SmsButtons.Up, true);
        Assert.Equal(ports.ReadDc(), ports.ReadPort(0xDC));
        Assert.Equal(ports.ReadDd(), ports.ReadPort(0xDD));

        // Unhandled read/write ports return defaults without error
        Assert.Equal(0xFF, ports.ReadPort(0x00));
        ports.WritePort(0x00, 0x12);
    }

    [Fact]
    public void InvalidPlayerAndArguments_ThrowsException()
    {
        var ports = new SegaControllerPorts();

        Assert.Throws<ArgumentOutOfRangeException>(() => ports.SetButtonState(3, SmsButtons.Up, true));
        Assert.Throws<ArgumentOutOfRangeException>(() => ports.SetButtonState(-1, SmsButtons.Up, true));
        Assert.Throws<ArgumentOutOfRangeException>(() => ports.SetThInputState(3, false));

        // No-op for SmsButtons.None
        ports.SetButtonState(1, SmsButtons.None, true);
        Assert.Equal(0xFF, ports.ReadDc());
    }
}
