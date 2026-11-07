# Development roadmap — draft v0.1

**Gate zero passed:** the owner approved the architecture direction and initial scope. A completed research task does not constitute an implementation or compatibility milestone.

| Stage | Scope and parallel ownership | Exit gate |
| --- | --- | --- |
| 0. Decisions | Owner selected C#/.NET core, C#/Avalonia desktop, export SMS II and Japanese SMS FM profiles, initial approximate timing and supplied BIOS; lead resolves remaining profile/BIOS details. | Owner explicitly approves documents and implementation scope. |
| 1. Foundation | Lead creates build, deterministic headless harness, shared bus/timing contracts and test conventions. QA designs independent fixtures and CI. | Reproducible build/tests; agreed ownership and error handling; no guessed hardware behavior. |
| 2. CPU and memory | Z80 and mapper specialists work in separate directories after contracts freeze; timing specialist supplies scheduler integration in its area. | CPU instruction/flag/timing/interrupt and ordered bus tests; RAM mirrors, mapper write fan-out, banking and synthetic ROM tests pass. |
| 3. Video and input | VDP and input specialists work separately; lead integrates scheduler and IRQ/NMI wiring. | Port latch/read buffer, IRQ, counters and deterministic pixel fixtures pass; homebrew visual tests with recorded provenance. Accuracy limitations documented. |
| 4. Audio | PSG and from-scratch YM2413 FM implement timed writes and deterministic synthesis in separate areas; lead coordinates FM/PSG mixing and profile-specific sound control. Host audio adapter remains separate. | PSG tone/noise tests; YM2413 address/data, instruments, key-on/off and rhythm tests; `$F2` detection/mute tests and sample-clock chunk-invariance pass. Compare against reliable reference or hardware traces; document remaining fidelity limits. |
| 5. Desktop frontend | Frontend specialist builds Avalonia menus, configuration, drag-and-drop and directory loader, help, settings and video/audio/input adapters; lead owns facade. | Headless and UI integration tests, configurable paths, restart handling, Force FM unknown/unsupported warnings and silence, safe errors and explicit performance/latency checks on approved platform. |
| 6. Debugger and save states | Lead defines synchronized snapshot/step and versioned state contracts; frontend builds debugger and save/load UI; subsystem owners supply complete state. | CPU registers, memory/map, VRAM/tiles and stepping validated; deterministic save/restore equivalence, incorrect-ROM/version rejection and atomic failure tests pass. |
| 7. PAL and compatibility validation | Timing/VDP specialists research and validate PAL; QA independently tests both standards, audio profiles and public test programs. | Reproducible reports identify test versions, model/profile, passes and known failures. No unsupported compatibility or cycle-accuracy claim. |
| Later | Battery RAM persistence, further models/mappers and enhanced accuracy by separate approval. | Feature-specific hardware evidence and regression suite. |

## Cross-cutting test strategy

- Synthetic deterministic tests cover address boundaries, memory write fan-out, CPU flags/prefixes/interrupts, VDP command interruption and IRQ levels, clock phase, input edges, PSG writes and host adapter ownership.
- Compare CPU bus traces and device output with authoritative references or independently verified fixtures; document source and exact variant. Test simultaneous events and chunking invariance, while not presenting an unverified phase as an oracle.
- Candidate externally supplied programs include [ZEXALL SMS](https://www.smspower.org/Homebrew/ZEXALL-SMS), [SMS Test Suite](https://www.smspower.org/Homebrew/TestSuite-SMS), and [SN76489 TestRom](https://www.smspower.org/Homebrew/SN76489TestRom-SMS). Availability/source inclusion is not proof of redistribution permission: check each license, provenance and variant before use or distribution. No commercial ROM or BIOS without lawful user supply.
- Owner supplied a BIOS-labeled image and two game images under `Roms/` for testing. Keep these local, never redistribute or commit them as fixtures, and confirm BIOS/model suitability. Their presence does not guarantee FM coverage: use a purpose-built [YM2413 TestRom](https://www.smspower.org/Homebrew/YM2413TestRom-SMS) as an additional candidate after checking its terms and expected behavior.
- On regression failure stop integration, assign ownership, and fix or explicitly defer with the milestone incomplete; do not weaken tests to pass. Independent QA review is required for implementation milestones.

## Current status

Phase 1 (CPU & Scheduler), Phase 2 (Video & Input), and Phase 3 (Audio Subsystem) are complete (868 passing unit tests). The solution contains a complete Z80 CPU instruction engine and interrupt state machine, deterministic `MasterScheduler`, Sega mapper with 8 KiB SRAM, memory bus & CPU adapter, VDP Mode 4 Background Renderer, VDP Sprite Engine & Compositor, Sega Controller Ports (Ports $DC/$DD/$3F & Pause NMI), complete SN76489 PSG Engine, from-scratch YM2413 (OPLL) FM Engine with 15 ROM patches, and `SmsAudioMixer` supporting Auto, ForcePsg, and ForceFm audio modes with Port $F2 control. Hardware uncertainties are documented in [hardware assumptions](hardware-assumptions.md) and [architecture](architecture.md).
