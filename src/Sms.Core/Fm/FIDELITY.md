# YM2413 rewrite status

This is an unfinished adaptation of Mitsutaka Okazaki's emu2413 v1.6.3,
https://github.com/digital-sound-antiques/emu2413/blob/master/emu2413.c;
license and copyright are in EMU2413-LICENSE.txt. See also Yamaha's
[YM2413 application manual](https://www.smspower.org/maxim/Documents/YM2413ApplicationManual)
and [SMS Power YM2413 notes](https://www.smspower.org/Development/YM2413).
An emulator is a reference implementation, not proof of hardware fidelity.

The SMS master clock is assumed to be three times the OPLL clock; the core
steps at chip clock / 72, matching emu2413's internal rate. Host output is
currently zero-order hold, not upstream's windowed-sinc converter: aliasing,
gain, analog filtering and exact upstream sample agreement remain unverified.
The caller supplies elapsed master ticks and sample count; sampleRateHz is
validated but timing derives from those supplied counts. Sample placement is
uniform within each call and therefore arbitrary partitions can shift sampling
boundaries; no global host sample phase is tracked here. Writes are effective
at call boundaries. Minimum write spacing is not modeled. Do not claim chip
accuracy or reference-vector equivalence without further tests.

## Test and state migration notes

The old `ApproximateEnvelopeRate_UsesChipClockReference` test injected state
into `_operators`, which does not drive the rewritten synthesizer, and demanded
the old implementation's assumed 4 dB/second rather than chip behaviour. It
was replaced by native-rate emu2413 numeric key-on and key-off output vectors.
The old enabled-violin test expected audible output within 5000 SMS master
ticks. At 216 master ticks per native step this permits only 23 steps; pinned
emu2413 produces zero for its first 32 in the same configuration. The test now
allows 216000 ticks and still requires nonzero bounded output. Reference tests
use emu2413 commit a1d5120903b75f93d3eb7690e34a29c0b4add903, `OPLL_new`
with clock 3600000 and rate 50000 to disable its converter; C `OPLL_writeReg`
and successive `OPLL_calc` calls produce the checked-in integer vectors.
Reference equality is not a hardware measurement. The remaining legacy
`ZeroEnvelopeRate_DoesNotAdvanceAttenuation` reflection test also addresses
non-driving `_operators` and is not meaningful coverage; it was not removed.

The typed `CaptureSnapshot`, static `ValidateSnapshot` and `RestoreSnapshot`
API uses FM schema version 2 and rejects version 1. Snapshot at a synchronized
device boundary, serialize all fields of `YM2413Snapshot` including its typed
patch and slot records, validate before machine-level application, then restore.
Invalid input throws before mutation. Snapshot arrays are independent copies,
but callers must not mutate them concurrently with validation or restoration.
The machine-level serializer owns identity checks, atomic multi-device restore,
file handling and its own version rejection policy. The FM snapshot includes
the F2 audio-control register but excludes host audio mode and gains.

The version-2 FM snapshot includes address and audio-control latches; all register
and custom-patch bytes and externally exposed channel/rhythm compatibility
fields; all 38 patch parameter records; nine patch binding indices; for each
of 18 slots its patch index, type, number, output history, phase/phase output,
phase-keep, block/f-number, envelope state/output/rates/shift, total level,
key/sustain flags, volume and pending-update flags; chip test, key mask,
rhythm mode, envelope and LFO counters, noise and short-noise state; 14 channel
outputs; master-tick remainder and held sample. Wave-table and patch object
references must be rebuilt from validated indices, not serialized as pointers.
Validate array sizes, ranges and cross-field invariants in a temporary object
before applying any state; FM restore is atomic for non-concurrently-mutated
input. A future filtered
resampler additionally requires its history and phase. Reject v1 at the shared
format boundary; do not delete or overwrite user files. Legacy OperatorEngine
compatibility fields are not synthesis state but must be considered if their
observable behavior is retained. Shared serializer changes are out of scope.
