# SMS emulator architecture — proposal v0.1

**Status: architecture direction approved by the owner; implementation in progress.** This is a new emulator, not a migration of an existing backend or frontend. Specific unresolved hardware and interface details still require lead review.

## Owner decisions and remaining approval

1. Core/backend: C# and .NET 10; use the .NET SDK and xUnit. `global.json` defines the SDK baseline and roll-forward policy.
2. Frontend: C# and Avalonia, cross-platform desktop, with Windows as the first development environment; verify Linux and macOS availability when those platforms can be tested.
3. Hardware: support both an export SMS II profile and a separate Japanese SMS profile with YM2413. FM is a required from-scratch implementation. Region and video timing are separate configuration concerns; exact revisions and delivery order remain to be specified.
4. Accuracy: start with explicitly approximate instruction-level timing and design for later bus-cycle timing. No cycle-accuracy claim.
5. Boot: use the owner-supplied BIOS locally where compatible. Filename alone does not establish its contents or suitability for either model. A Japanese-profile-compatible BIOS may need to be supplied separately; do not silently reuse the export image or fake a boot.
6. Desktop requirements: menu-driven options, PAL/NTSC selection, PSG/FM switching with restart, controls/hotkeys help, configurable ROM/BIOS paths, ROM drag-and-drop and loader, debugger, and persistent save states. See the frontend section below. PAL timing remains a research and validation task, not an already verified profile.

The owner explicitly approved the updated architecture and roadmap. Unresolved hardware details remain documented and must not be silently invented.

## System boundaries

```text
Host UI / CLI / debugger        host file, video, audio, input adapters
             |                         |
             +---- Emulator facade ----+
                       |
          deterministic machine + scheduler
              |          |          |
             Z80 <---- system bus --> cartridge / mapper + work RAM
                         |          |
                   VDP + video     PSG + YM2413    controllers / I/O
```

The core has no UI, graphics API, audio device, wall-clock or filesystem dependency. The host supplies ROM/optional BIOS bytes and timestamped or deterministically queued inputs, consumes frames and audio, and owns pacing and storage. Emulated state has one owner; a frontend cannot mutate it concurrently. Host-facing operations are serialized on the emulation thread. Debug inspection must distinguish side-effect-free snapshots from actual bus reads.

The machine owns device state and a monotonic integer master-clock timeline. The bus owns decoding and coordinates time-aware accesses; devices do not call one another recursively through ad-hoc callbacks. VDP owns IRQ pending state; CPU owns interrupt acceptance. See [interfaces](interfaces.md) for draft semantics and [hardware assumptions](hardware-assumptions.md) for evidence and limitations.

## Desktop experience — required scope

- A main-window menu exposes ROM loading, video standard, audio mode, save/load state, debugger, settings and help. Help lists the actual configured hotkeys and controller bindings. Fullscreen, pause/resume and reset are proposed additions.
- Switch between PAL and NTSC profiles with a controlled machine restart; do not merely change presentation speed. Initial PAL timing has unresolved evidence and needs separate validation.
- Audio choices are **Automatic**, **Force PSG**, and **Force FM**. Restart the current ROM when changing the selection unless a correct live change is later demonstrated. In Force FM, warn and output silence when FM support is unknown or confirmed absent; distinguish these two cases in the message. Determining support from arbitrary ROM bytes is not reliably established, so use verified compatibility metadata and/or validated runtime evidence without asserting false certainty. Automatic follows selected hardware/game behavior rather than merely muting one chip. Exact hardware-profile override and mixing policy still require validation.
- Default ROM directory is `Roms` beside the program; configuration can override it. The ROM loader lists available images from that directory, and dropping a supported ROM on the main window loads it. Validate unsupported files and present actionable errors; do not assume the executable directory is writable.
- Configuration persists defaults: model/region, PAL/NTSC, PSG/FM, ROM and BIOS paths, controller and hotkey mappings, display options, audio device/volume and state directory. Relative paths resolve against the program directory; configurable settings location and writable fallback must be defined per host. Keep user ROMs, BIOS and save files out of distribution and version control.
- A separate debugger window provides memory map and memory inspection, CPU registers, pause/resume and single-instruction stepping, VRAM map and tile viewer. Proposed additions: disassembly, breakpoints, mapper/VDP registers, current timing and safe snapshots. Reading debugger views must not perform side-effectful bus accesses; all inspection/stepping is synchronized with the emulation thread.
- Save and restore persistent game state, including enough hidden machine and timing state for deterministic continuation. Offer state slots and/or file selection; validate version, game identity and hardware configuration before loading and fail without corrupting the current session. Battery-backed cartridge RAM is separate. Exact UI and compatibility policy remain open.

## Timing strategy

Represent device times in integer master ticks and retain divider phases. The desired long-term boundary is timestamped CPU bus transactions and interrupt sampling points; before a transaction the scheduler advances affected devices to its timestamp. A first instruction-stepped implementation may approximate access times but must label the limitation, retain transaction ordering, and never claim cycle accuracy. Exact intra-tick ordering and initial phase remain research items. Host audio callbacks or display refresh never advance emulated time.

## Ownership and layout

Only the lead edits shared contracts, machine wiring, build, and these architecture documents. Not-yet-created paths are proposed names.

| Owner | Exclusive future implementation area | Dependency |
| --- | --- | --- |
| Z80 | `src/Sms.Core/Cpu/`, `tests/Sms.Core.Tests/Cpu/` | approved bus and timing contracts |
| Memory mapper | `src/Sms.Core/Cartridge/`, `src/Sms.Core/Memory/`, matching tests | approved bus/loader contracts |
| VDP | `src/Sms.Core/Vdp/`, matching tests | timing, bus and output contracts |
| Timing | `src/Sms.Core/Timing/`, matching tests | agreed scheduler/CPU boundary |
| PSG | `src/Sms.Core/Psg/`, matching tests | timestamp and audio contracts |
| FM audio | proposed `src/Sms.Core/Fm/`, matching tests | YM2413, audio-control and timing contracts; specialist assignment by lead |
| Input/I/O | proposed `src/Sms.Core/Input/`, matching tests | bus and timestamp contracts |
| Frontend | `frontend/`, frontend tests | chosen host stack and facade |
| QA | `tests/integration/`, `tests/fixtures/`, reports | independent review, verified fixtures |

Parallel work requires explicit non-overlapping assignments and lead-approved shared contracts. QA reviews separately. Lead integrates only after targeted tests pass.

## Scope controls

YM2413 FM and PAL support are required and included in the roadmap, but remain unimplemented; their detailed hardware profiles and acceptance criteria require validation. Avoid implying coverage for Game Gear, legacy video modes, Codemasters and other mappers, peripherals or unusual cartridges unless separately planned. User-supplied ROMs and BIOS are local test inputs, not distribution assets. Tests must record provenance and licensing. Save states are a later versioned capability; battery-backed RAM persistence is a distinct host service.
