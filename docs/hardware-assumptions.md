# Hardware assumptions and evidence — draft v0.1

**This is a proposal, not a conformance claim.** References describe hardware evidence; items marked open or approximate must not be treated as verified for every console revision.

## Initial profile under consideration

The owner selected both export SMS Model II and Japanese SMS with integrated YM2413 FM, and requires PAL/NTSC switching. NTSC is proposed for initial development; PAL support requires independent research and validation. Sega mapper is proposed first. Mark III can use an FM add-on, but is not automatically included. An export SMS II with FM would be a modified or compatibility configuration, not stock. The supplied BIOS-labeled image's contents and model suitability have not been verified; Japanese profile may need a separate compatible BIOS. Game Gear, extra devices and other mappers are not implicitly supported.

## Evidence-based behavior

| Topic | Evidence and qualification |
| --- | --- |
| Memory | `$0000–$BFFF` slot area; 8 KiB work RAM at `$C000–$DFFF`, mirrored at `$E000–$FFFF`. Sega mapping has three 16 KiB windows with `$0000–$03FF` fixed; `$FFFC–$FFFF` mapper writes also reach RAM and reads return RAM. See `IDocs/General.txt` §2 and [SMS Power memory map](https://www.smspower.org/Development/MemoryMap). |
| Mapping caveats | Sega mapper RAM/ROM options and reset values vary by chip; 315-5235 startup `00,00,01,02` is a chip-specific proposed profile, not a universal guarantee. Codemasters mapping is not interchangeable with Sega; local notes are simplified. See [SMS Power mappers](https://www.smspower.org/Development/Mappers). |
| CPU | Z80 uses separate 16-bit memory and I/O address spaces; SMS I/O decoding ignores upper port address bits. INT is level-sensitive and NMI edge-triggered. EI delay, interrupt modes and HALT require CPU tests. See `IDocs/z80cpu_um.pdf`, timing and interrupt response; `IDocs/General.txt` §§3, 5. SMS II acknowledge byte `$FF` is reported in §5; preserve all bits for IM2 despite the manual's even-vector prescription. |
| VDP | SMS VDP has 16 KiB VRAM, 32-byte CRAM, a buffered data port and two-write command protocol. Port aliases and read/write direction matter; status read clears pending flags. See Charles MacDonald, `IDocs/VDP.txt` §§2–5, 12. Its §4 and §10 disagree on collision-bit numbering; independently verify. `IDocs/VDP2.pdf` describes the related TMS9918A, not the SMS VDP. |
| Video | Mode 4 uses 8×8 four-plane tiles, palettes, background priority and sprite evaluation; pixels need palette resolution at the time they are rendered. Exact raster effects and 192/224/240-line and legacy-mode scope are open. `IDocs/VDP.txt` §§8–11. |
| Timing | Initial NTSC hypothesis: master ~10.7386 MHz, VDP dot = master/2, CPU/PSG clock = master/3; 342 dots = 684 master ticks = 228 CPU T-states per line, 262 lines = 179,208 ticks/frame. Source measurements are for an original SMS, not proof of exact SMS II oscillator/phase. See [SMS display timing](https://www.smspower.org/forums/8161-SMSDisplayTiming) and `IDocs/VDP.txt` §11. Do not approximate physical rate as exactly 60 Hz. |
| IRQ / NMI | VDP frame/line pending flags and enable bits form a persistent IRQ level; CPU acknowledge alone does not clear them. Pause press generates NMI. See `IDocs/VDP.txt` §12 and `IDocs/General.txt` §5. Precise event position and ordering remain open. |
| PSG | PSG divides its input by 16; register updates need timely delivery. `IDocs/PSGNotes.txt`, “SN76489 writes” and “How the SN76489 makes sound.” Its author says most tests were on PAL SMS II, so NTSC revision details and initial state need independent verification. |
| YM2413 | [Yamaha YM2413 application manual](https://www.smspower.org/maxim/Documents/YM2413ApplicationManual), §§II.4, II.7 and III: address/data writes, nine melodic channels or six plus five rhythm instruments, user voice and preset instruments. The manual specifies 12 chip-clock cycles after an address and 84 after data before the next write; it does not establish automatic Z80 wait states. The transcription flags errors, so independently cross-check sensitive details. |
| FM integration | `IDocs/General.txt` §7 describes `$F0` address, `$F1` data, `$F2` detection and `$3E` I/O interaction. [SMS Power audio control](https://www.smspower.org/Development/AudioControlPort) describes profile-dependent FM/PSG muting; Japanese SMS and Mark III FM Unit differ. Exact model, absent-device response, timing and mixing remain open. |

## Open issues and approximations

- The local VDP reference notes uncertainties in horizontal event timing and inconsistencies in status descriptions; do not turn estimates into hardware-verified dot-level tests. TH latch edge, contention, power-on state, H/V-counter phases and exact same-timestamp ordering need research.
- Local notes and public references differ on PAL line counts (313 versus 312). PAL is a required feature but must not be declared supported until resolved and validated; do not extrapolate NTSC tests.
- Work RAM initial contents, mapper reset behavior and CPU registers not specified by the CPU reset documentation must not be invented as physical facts. Deterministic initialization belongs to a named test/boot profile.
- BIOS presence, built-in software, slot arbitration, disabled/open bus and bus contention vary by model. BIOS bypass is an approximation and has to be explicitly selected; a legally supplied BIOS is not bundled.
- Instruction-granularity scheduling is a planned approximation if selected. It cannot place all mid-instruction I/O and IRQ events accurately; no cycle accuracy or game compatibility is claimed.
- Z80 BIT undocumented flags need variant-specific verification: the current register BIT F3/F5 operand-bit behavior follows [CPC hardware tests](https://64nops.wordpress.com/2021/01/13/perfectly-accurate-z80-flags-and-cpc-timing/), while Sean Young §4.1 gives a differing description. BIT (HL) uses prior MEMPTR high bits according to [MEMPTR research](https://gist.github.com/drhelius/8497817); the prototype's zero initial MEMPTR is a deterministic approximation, not a physical reset value. Nonzero MEMPTR and SMS-specific behavior remain unverified.

## References and credit

- ZiLOG, *Z80 CPU User Manual*, `IDocs/z80cpu_um.pdf`; Sean Young, *The Undocumented Z80 Documented*, `IDocs/z80-documented.pdf` (verify CPU-variant scope).
- Charles MacDonald, *SMS/GG hardware notes*, `IDocs/General.txt`, and *SMS VDP documentation*, `IDocs/VDP.txt`. We credit Charles MacDonald for hardware information as requested in his notes. The project does not redistribute or link directly to the copied document.
- Maxim, *SN79489 notes*, `IDocs/PSGNotes.txt`.
- [SMS Power development documents index](https://www.smspower.org/Development/Documents), [memory map](https://www.smspower.org/Development/MemoryMap), [mappers](https://www.smspower.org/Development/Mappers), and [VDP references](https://www.smspower.org/Development/VDP-Index).
