# PSG register subset — interfaces v0.1

`PsgRegisters.CreateDeterministicFixture()` explicitly selects a software fixture,
not physical reset or power-on. `Write(byte)` applies ordered, untimed register
writes; `Inspect()` returns a detached immutable value. The separate `PsgToneClock`
is an unconnected single-channel timing prototype; it does not produce samples or
handle period 0/1. There is no integrated clock, noise generator, scheduler, bus
decoder, UI or host API.

Implemented: persistent 3-bit register selection, three 10-bit tone values, four
4-bit attenuation values, effective 3-bit noise control, and assignment of the
SMS 16-bit LFSR seed `0x8000` on **every** noise write, including data writes and
unchanged settings. `NoiseReseedCount` is diagnostic instrumentation counting
noise writes modulo 2^64, not a hardware clock/counter. The seed does not evolve
without a clock; tests verify write-event counts rather than claiming to prove
recovery from a shifted state or generated noise correctness.

## Evidence and limitations

- Maxim, [SMS Power SN76489](https://www.smspower.org/Development/SN76489),
  “SN76489 registers”, “SN76489 register writes”, and “The Linear Feedback Shift
  Register”: immediate nibble updates, data masks, persistent latch, noise-write
  reset with only the highest bit set in the SMS's 16-bit LFSR.
- Maxim, local `IDocs/PSGNotes.txt`, “SN76489 writes”, lines 61–150, and “How the
  SN76489 makes sound”, especially lines 240–251: reset seed `0x8000`; observed
  `0x4000`/`0x2000` may reflect shifting during the write.
- The local notes say most research used a **PAL SMS II** (lines 9–11). Their
  startup zeros/silence/Tone0 latch may be BIOS initialization (lines 54–58);
  the public page also qualifies startup by chip variant. Fixture zeros do not
  resolve physical startup, PAL/NTSC clock/phase, or zero-period output behavior.
  The local and updated public descriptions differ on zero/one tone behavior;
  this implementation only stores zero and does not choose output semantics.

Future integration must add approved clock/timestamp and audio contracts before
claiming sound, timing accuracy or compatibility. No scheduler should treat a
write here as elapsed emulated time. Inspection is not serialization.
