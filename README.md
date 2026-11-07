# Sega Master System emulator

## Current development status

The approved project is a new C#/.NET emulator with an eventual Avalonia frontend. The core is in early development and cannot run ROMs yet. Unsupported CPU instructions fail explicitly. The initial instruction-stepped interface is not cycle accurate.

Run `dotnet test SMS.sln --configuration Release` from the project root. Projects target .NET 10 and use xUnit; the .NET 10 SDK (10.0.403 or a later .NET 10 feature band per `global.json`) and package restore access are required. See `docs/z80-test-plan.md` for planned opcode, shift, flag, timing, bus and interrupt coverage; the plan is not a claim that coverage is complete.

Install the .NET 10 SDK normally for durable development; a temporary SDK installation may not be on the system PATH or survive cleanup.

OpenCode project agents are in `.opencode/agents/`; the original Codex definitions remain in `.codex/agents/`. OpenCode agents recommend OpenAI GPT models by role, with stronger assignments for accuracy-sensitive work. Catalog availability does not prove a model is included in a particular GPT Plus subscription. Do not commit or distribute user-supplied ROMs, BIOS files, or local hardware reference documents without permission. `Roms/` and `IDocs/` are local-only and excluded from Git; `.gitignore` is a safeguard, not protection for already tracked or force-added files. Acquire authoritative references separately where needed.

## Included roles
- z80_cpu
- vdp
- memory_mapper
- audio_psg
- audio_fm
- input_io
- timing
- frontend
- qa
- advocate

## How coordination works
`AGENTS.md` is the parent-agent operating policy. The OpenCode Markdown definitions and Codex TOML definitions are separate configurations; they are not permanently running processes. The parent handles shared interfaces and final integration. Concurrent code edits require separate file ownership. The four-thread limit in `.codex/config.toml` applies to Codex and does not configure OpenCode concurrency.

## Design documents
See `docs/architecture.md`, `docs/interfaces.md`, `docs/hardware-assumptions.md`, and `docs/roadmap.md` for approved direction, contracts, hardware uncertainties and outstanding milestones.

## Licensing
The repository contains the GPL-3.0 license in `LICENSE`. The small third-party Z80 test-vector extracts have a separate MIT notice in `tests/Sms.Core.Tests/Fixtures/SingleStepTests-LICENSE.txt`. User-supplied ROMs, BIOS images and local hardware documents are not included.
