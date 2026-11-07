# Interface contracts — draft v0.1

**Architecture direction approved; semantic contracts v0.1.** Names are illustrative, not frozen API signatures. Shared boundary changes require lead review; unresolved behavior is not implied to be approved or verified.

## Machine and host

- `create(config, romBytes, optionalBiosBytes)` validates configuration and produces either a machine or a structured error. Inputs are immutable copies or owned buffers; configuration identifies machine/region, boot policy, mapper and cartridge RAM where needed. Do not silently strip headers or guess a mapper.
- `powerOn`, `reset`, `setInput`, `runUntil`/`runFrame`, and `inspect` must specify state transitions. Power-on and reset are distinct; deterministic test initialization is not asserted to be physical power-on behavior. Input ordering and timestamps must be reproducible.
- Return completed frame descriptors with dimensions, stride, pixel format and explicit buffer ownership/lifetime; pixels represent palette values at production time, including mid-frame palette changes. Host scaling and presentation are outside the core.
- Produce ordered audio samples or a bounded output queue with specified sample format, rate, ownership and backpressure. Emulated PSG time is independent of host callback cadence. Persistent rational resampling phase prevents drift. Exact formats and queue policy are open before implementation.
- Host owns pacing, file dialogs, ROM/BIOS access and battery-RAM persistence. Errors must not silently alter ROMs or substitute unsupported hardware behavior.
- Host settings provide PAL/NTSC, machine/region, Automatic/Force PSG/Force FM selection, ROM and BIOS paths, and UI/input preferences. PAL/NTSC changes restart the machine; audio changes restart the current ROM unless safe live switching is later demonstrated. Automatic follows the selected hardware and game sound control. Forced modes are user-requested compatibility overrides; exact interaction with hardware detection and mixing remains open. Force FM must warn and produce silence for both unknown and confirmed absent FM support, without presenting unknown as confirmed absent. Define ROM-switch and failure behavior before implementation.
- The host resolves the default `Roms` directory relative to the program location; configuration may override it. Resolve relative configured paths against the same base and display failures rather than silently selecting another directory. Store config/state in a writable user location if necessary, without changing the ROM-path default.
- Debugger reads immutable, side-effect-free snapshots while the machine is paused or at a defined synchronization boundary. Commands for pause, resume, single-instruction step and optional breakpoints are serialized by the machine owner. Snapshots expose CPU registers, decoded memory map, memory, VDP registers, VRAM, tiles and timing; they do not invoke ordinary side-effectful device reads.

## CPU ↔ system bus

- Separate `readMemory(addr16) -> byte`, `writeMemory(addr16, byte)`, `readIo(port16) -> byte`, `writeIo(port16, byte)` and interrupt acknowledge. The SMS bus, not the generic CPU, handles address decoding, including ignored upper I/O bits. Reads can have side effects; word accesses are ordered byte operations.
- Transactions retain fetch/read/write/acknowledge identity where required, order and emulated timestamp. Interrupt acknowledge obtains the machine-provided data byte. Bus can fan out writes to multiple recipients and represent an undriven read separately from a driven byte; contention/open-bus resolution is profile-specific and remains open.
- The initial untimed CPU interface exposes opcode fetch separately from data reads. If a fetch or unsupported opcode faults, the prototype enters a terminal fault state: its caller must discard the machine rather than retry the access or treat the step as elapsed emulation time. Fetch side effects are not rolled back. Recovery and accounting are future machine-level design work.
- CPU state includes register banks, IX/IY, SP, PC, I/R, flags, interrupt mode, IFFs, HALT and delayed-EI state. CPU consumes INT level and edge-latched NMI, honors CPU sampling boundaries, NMI priority and HALT time; it does not clear device IRQ flags by acknowledging INT.
- Proposed accurate evolution: CPU exposes resumable bus/machine-cycle operations and interrupt sampling points, with timestamp phase and wait-state accounting specified. An initial instruction step may return elapsed T-states and ordered operations only as an explicitly approximate tier; it must not pretend those accesses have proven intra-instruction timestamps.

## Scheduler ↔ devices

- `MasterTick` is an integer count in the selected model's master-clock domain. Scheduler advances monotonically and preserves CPU, VDP, PSG and resampler phases across calls. Overflow strategy and initial phase must be specified in implementation.
- Proposed `advanceTo(t)` processes device events strictly before `t`; then the bus performs an access at `t`. Events exactly at `t` are resolved by a single explicit ordering policy before approval of accuracy-sensitive tests. This is a proposed software convention, not a verified hardware phase; if hardware evidence requires another convention, update the contract.
- Device events and accesses at the same time must not be reordered nondeterministically. No device advances beyond the scheduler's current operation; devices expose levels/events rather than directly invoking CPU execution. Interrupt level is recomputed after relevant changes.
- PSG writes and VDP accesses occur at emulated access time. Frame boundaries and host sample consumption do not drive hardware clocks. Chunked and unchunked advances must produce equivalent state and output under the selected accuracy tier.

## Bus ↔ mapper / memory

- Mapper provides address-aware read/write responses including whether it drives the bus, and receives relevant writes even outside `$0000–$BFFF`. Bus owns slot selection, system RAM and arbitration. In the Sega profile, a write to `$FFFC–$FFFF` updates both work RAM and mapper; reads there return RAM, and writes to `$DFFC–$DFFF` do not update mapper.
- Mapper type is explicit; automatic identification, if added, is advisory and overridable. Model-specific `$3E` memory control, BIOS mapping, cartridge RAM and persistence must be represented. Bank wrapping, unusual ROM sizes, headers, reset state and electrical contention need explicit policies before coding.

## VDP, PSG and I/O

- Bus decodes read/write direction and mirrored ports; VDP exposes data/control read/write, counter reads, and a timestamped TH-latch input. PSG receives writes independently. The precise TH trigger remains to be verified.
- VDP owns VRAM, CRAM, registers, control latch, read buffer, timing/rendering state and line/frame pending flags. Its IRQ output is a level combining enabled pending sources; status read, not CPU acknowledge, clears relevant pending flags.
- PSG owns tone/noise counters, latch, attenuation and clock phase; writes affect emulated sound at the access time. Controller state and Pause input are owned by input/I/O; Pause supplies an NMI transition rather than a VDP IRQ. Regional port behavior is profile-dependent.

### YM2413 and sound control

- FM is an explicit configurable device/profile, not a property silently assigned to all SMS II hardware. The bus routes timestamped `$F0` register-address writes and `$F1` register-data writes to the YM2413 and handles `$F2` detection/audio control with the selected hardware profile and I/O arbitration. Exact decoding, reset, absent-device reads and `$3E` interactions require profile-specific validation.
- The YM2413 owns its address latch, register and operator/envelope/rhythm state and clock phase. Scheduler advances it independently of host audio callbacks. Mixer applies hardware-profile PSG/FM enable behavior; output timing, gain and any analog filtering are configurable or explicitly approximate until verified. For Japanese SMS versus Mark III FM add-on, `$F2` PSG mute behavior differs.
- An address/data interface and deterministic sample output must cover melodic channels, user instrument, key-on/off, envelopes and rhythm mode. Manual write-spacing requirements are chip-clock cycles, not automatically CPU wait states; model behavior for writes violating them is unresolved. Save states later include FM and mixer phases.

## Future save states

Versioned, validated serialization must capture complete machine and mapper identity, ROM identity, CPU hidden state, RAM, device buffers/latches, IRQ/NMI state, timing/divider and resampler phases, FM state, and partial rendering/audio state as required for deterministic continuation. No host handles or ROM copyrighted bytes need be embedded. Saving and loading occur at a defined synchronized boundary. Validate version and game/profile identity before applying a state; failed loads leave the running machine unchanged. Define migration and external persistent-RAM interactions before implementation. Save/load equivalence is a required test.
