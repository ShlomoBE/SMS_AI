# Sega mapper foundation (interface contract v0.1)

`SegaMapper` requires an explicit `Sega3155235RomOnly` or
`Sega3155235Sram8KiB` profile and copies raw ROM input.
No header stripping, mapper guessing, filesystem access, or ROM
mutation occurs. Complete power-of-two counts of 16 KiB banks, from 1 through
32 banks (512 KiB), are supported. Smaller physical ROMs and unusual sizes
are rejected rather than padded. This is a software support policy, not a
claim that all smaller cartridges physically contain this chip.

## Evidence

- [SMS Power, Mappers](https://www.smspower.org/Development/Mappers), Sega
  mapper / Registers / ROM mapping: three 16 KiB slots; first 1 KiB fixed to
  ROM bank zero; exact FFFC-FFFF control writes; power-of-two ROM mirroring;
  315-5235 power-up values `00,00,01,02`. Latches retain raw written bytes;
  only the physical ROM address selection is masked.
  RAM mapping: control bit 3 overlays slot 2 with SRAM; bit 2 selects a
  16 KiB SRAM bank. The reference qualifies that most SRAM cartridges seem
  to have 8 KiB, mirrored to fill the windows. Our 8 KiB size is an explicit
  board configuration, not a universal property of the chip or every game.
- [SMS Power, SMS Paging Chips](https://www.smspower.org/Development/SMSPagingChips),
  315-5235 section: believed capacity of 32 banks. This limits this explicit
  profile to 512 KiB; it does not pretend to be a universal Sega mapper.
  Battery-backup support is also described as believed, not a specification
  that fixes every cartridge's SRAM capacity or wiring.
- Charles MacDonald, *SMS/GG hardware notes*, local `IDocs/General.txt`,
  section 2, lines 73-98: work RAM is 8 KiB, mirrored through E000-FFFF;
  FFFC-FFFF writes reach RAM as well as mapper latches. Credit to Charles
  MacDonald for hardware information; the reference itself is not copied.
- `docs/interfaces.md` v0.1, Bus / mapper: the bus owns system RAM, slot
  enable, arbitration and fan-out. The lead approved the mapper/bus-local
  SRAM profile and `CartridgeRamWritten` result extension.

## Integration and explicit gaps

`TryRead` reports whether the cartridge drives the bus. A false result does
not define open-bus voltage/data. Reads from C000-FFFF, including mapper
registers, are undriven by both supported cartridge profiles. The bus returns
work RAM there when enabled. `Registers` is debugger inspection, not a
memory-mapped register read.

`ObserveWrite` is observation, **never exclusive consumption**. The bus must
fan out FFFC-FFFF writes to enabled work RAM even when the mapper updates a
latch. DFFC-DFFF writes affect RAM only; they must not update the mapper.
`SegaMemoryBus` also delivers 8000-BFFF writes to the mapper. When SRAM is
selected these return `CartridgeRamWritten`, including same-value writes;
this result denotes a delivered SRAM write, not dirty-state tracking.
ROM-window writes with SRAM disabled remain ignored. Tests include both
the earlier test-only fan-out harness and the production restricted bus.

## Explicit 8 KiB SRAM profile

Supply exactly 8192 initial bytes (copied) or an explicit byte fill using the
three-argument constructor. The two-argument constructor remains ROM-only:
requesting the SRAM profile without initialization fails. The byte-fill
overload requires the SRAM profile; ROM-only rejects nonempty SRAM.
Deterministic initialization is not a physical SRAM power-up assertion.

With control bit 3 set, reads/writes at 8000-BFFF use physical SRAM index
`address & 0x1FFF`. Thus 8000/A000 and 9FFF/BFFF alias. Bit 2 is retained,
but both selections alias the same physical 8 KiB. Slot-2 ROM latch writes
at FFFF still take effect while hidden; clearing bit 3 reveals that ROM
bank. Other ROM slots, fixed vectors and work RAM are unaffected.

`SramSize` is physical length (8192 or zero). `ExportSram` returns an owned
physical-byte copy, empty for ROM-only; it never exports duplicated mapped
windows. `ImportSram` requires the SRAM profile and exactly 8192 bytes,
validates before mutation, copies the input and preserves mapper latches.
The machine owner must serialize these operations at a synchronization
boundary. Import/export are not save states or battery filesystem policy.

Both profiles reject FFFC bits 0, 1, 4 and 7: bank shifting, high SRAM
window and development ROM-write mode remain deferred. ROM-only also
continues rejecting bit 3. Unsupported values return
`UnsupportedControlValue` without altering mapper latches or SRAM; the bus
has already delivered the write to work RAM and does not roll it back.
The caller must report unsupported execution, not pretend emulation
succeeded. Bits 5-6 are retained and inert. Bit 2 is retained and inert for
ROM-only and aliases for 8 KiB SRAM. This is not full 315-5235 conformance.

Other SRAM sizes, upper C000-FFFF cartridge mapping, contention, dirty
tracking, host battery-file persistence/identity and save-state policy are
deferred. No filesystem or automatic battery-presence inference is added.

`ResetToPowerOnProfile` reapplies the documented chip power-up latch values.
It preserves every SRAM byte and does not model a console reset button or assert other mapper revisions
share these initial values. BIOS, port 3E, system RAM startup contents,
save states and electrical behavior remain outside this foundation.

Tests generate all ROM bytes synthetically; no external/commercial ROMs are used.
