# Implementation Plan: Reverse Export — Scaffold `.st` Source Tree from an Existing PLC Project

## Overview
Today the tool is one-directional: `.st` files + manifests → TwinCAT project
(create/update/build). This adds the **reverse, one-time bootstrap** direction:
given an *already-existing* TwinCAT PLC project (`--dest`/`--name`), regenerate
the whole `--source` tree — every POU/DUT/GVL as `.st`, plus `libraries.xml`,
`io-devices.xml`, `events.xml` (+ `event-classes/*.xml` templates), and
`links.xml` (variable links) — so a team can *adopt* this tool on a project that
already exists, then manage it as git-tracked ST source from then on.

This is explicitly a **bootstrap/adoption tool, not the ongoing flow.** The
forward direction stays the source of truth (see the "Not Doing" note in
`docs/ideas/st-source-twincat-sync.md`: auto-export must never become the
*primary* loop). Reverse is what you run *once* to create the source tree; after
that you edit `.st` and sync forward as normal.

## Key Insight: this is mostly a read-side mirror of code that already exists
The forward engine already reads almost everything we need — the work is
inverting readers we already have, not discovering new COM APIs:

| Artifact | Reverse mechanism | Reuse / status |
|---|---|---|
| `.st` (all POUs/DUTs/GVLs) | tree walk → `PlcObjectExporter.Export` | **`PlcObjectExporter` already exports any single object** with full round-trip fidelity (incl. methods/properties, terminators, INTERFACE special-casing). Just needs a whole-project walk. |
| `libraries.xml` | enumerate `ITcPlcLibraryManager.References` | **`LibrarySyncEngine` already reads + regex-parses** the display name into Name/Version/Company (`DisplayNamePattern`). Invert into a writer. |
| `links.xml` (variable links) | `ITcSysManager.ProduceMappingInfo` | **Already shipped as `--export-links`.** Reverse just calls it as part of `--export-all`. |
| `events.xml` + `event-classes/*.xml` | read `.tsproj` `<DataTypes>` pool | **`TsprojDataTypePool`/`EventClassChecker` already read the pool.** Dump each event-class `<DataType>` to a template file; emit `<EventClass Name Guid>`. |
| `io-devices.xml` | walk `TIID` Device→Box→Terminal | **The one genuine unknown** — `IoSyncEngine` *writes* `Product` as `vInfo` but never reads it back. Gated behind a Phase-1 spike (Task 2). |

## Architecture Decisions
- **Composable `--export-*` flags mirroring the `--sync-*` stages** (user
  decision 2026-07-15): `--export-code`, `--export-libs`, `--export-io`,
  `--export-events`, and `--export-all` = all four + the existing
  `--export-links`. Same "any subset runs exactly those, in a fixed order"
  model as `SyncStages`, and the same lazy-VS lifecycle (events needs no VS;
  code/libs/io/links do). Keeps the surface consistent and the diff small.
- **Overwrite is safety-gated** (user decision): if `--source` already contains
  `.st` files or any manifest, reverse-export refuses (exit 1) unless
  `--overwrite` is passed. Follows the established `--init` / `--confirm-delete`
  / `--confirm-delete-io` philosophy — destructive-ish effects never happen by
  accident, and `--overwrite` is CLI-only (never read from `.stconfig`).
- **Reuse, don't rewrite.** New code is thin *writers* + one *project walker*;
  each leans on an existing Sync/* reader. No new COM techniques except the
  gated IO product-read spike.
- **IO scoped behind a spike** (user decision): Task 2 proves whether a live
  terminal's `Product` is readable. Ship `--export-io` only if it works
  reliably; otherwise fall back to `.tsproj`/`.xti` XML parsing or defer
  `--export-io` while still shipping code/libs/events/links/all now.
- **Round-trip is the acceptance test.** For each artifact: reverse-export from
  the real project into a *scratch* source folder, then forward-sync that
  scratch source into a *scratch* project and confirm `--build` passes — proving
  the generated source is faithful. Never write into the real ST/Shark tree.

## Target CLI

```
Reverse export (regenerate --source FROM an existing --dest/--name project):
  --export-code     all POUs/DUTs/GVLs -> mirrored .st files under --source
  --export-libs     library references -> libraries.xml
  --export-io       TIID device/box/terminal tree -> io-devices.xml  (spike-gated)
  --export-events   .tsproj event classes -> events.xml + event-classes/*.xml
  --export-all      all of the above + --export-links (links.xml)
  --overwrite       allow reverse-export to overwrite existing files in --source
                    (required if --source already has .st files or manifests)

Already exists, unchanged, and folded into --export-all:
  --export <name>   single object -> its .st file
  --export-links    all variable links -> links.xml
```

**Fixed order for any selected subset** (mirrors the sync pipeline's phases):
events (no VS) → code → libs → io-tree → io-links/links.xml → done. VS is
opened lazily by the first artifact that needs the Automation Interface; a
lone `--export-events` never launches it.

## Implementation status — LIVE-VALIDATED 2026-07-16 (all phases complete)

**Everything below is now empirically confirmed against real TwinCAT/Visual
Studio COM, including two real production projects — not just unit-tested.**
160 tests passing (was 125 before this feature).

**Task 2 spike — RESOLVED.** `ItemSubTypeName` alone was NOT the answer; the
stronger, confirmed-reliable signal is `item.Name`'s own trailing
`"(Product)"` parenthetical — TwinCAT's own default naming convention when a
device is dragged from the ESI catalog — used verbatim (not regex-shortened),
which correctly handles even non-Beckhoff/hyphenated catalog strings (Festo
`EX260-SEC1`) that a bare product-code regex would have truncated wrongly.
Falls back to a regex match embedded in `Name` (TwinCAT's OTHER auto-naming
style, e.g. `EK1100_1.1`), then to `ItemSubTypeName`, in that order. Verified
against 30 real terminals in `PLC_NFL_SHARK_V2` — every value matched its
`ItemSubTypeName` description exactly. `IoManifestWriter.DeriveProduct` no
longer needs a `ProduceXml`/`.xti` fallback; the current approach is sufficient.

**Two real bugs found and fixed only by running against a live project:**
1. **`ITcPlcLibRef.Name` never actually returns the assumed combined
   `"Name, Version (Company)"` display format** — the real value is the bare
   library name, optionally `#`-prefixed (e.g. `"Tc2_Standard"`,
   `"#Tc2_System"`). `LibrarySyncEngine.TryParseDisplayName` now recognizes
   this real shape, defaulting `Version="*"`/`Company="Beckhoff Automation
   GmbH"` for the Beckhoff `Tc<N>_*` namespace (the exact convention already
   used throughout this repo's own `libraries.xml` files), while still
   refusing to guess a company for an unrecognized/third-party bare name.
2. **Attempting to `RemoveReference` an implicit (`#`-prefixed) reference
   crashed with a real, unhandled `COMException`** ("Specified library '...'
   not found!") the moment a manifest didn't explicitly list it — true for
   almost every real manifest, since implicit/template-provided references are
   never meant to be listed. `TryParseDisplayName` now also returns
   `isImplicit`, and `LibrarySyncEngine.Sync`'s orphan-removal loop skips any
   implicit reference unconditionally. This was a genuine crash-on-first-run
   bug in the EXISTING forward sync, only surfaced by reverse-export's
   round-trip testing — not something reverse export introduced.

**A genuine architecture gap was found and fixed: reverse export could not
target an arbitrary pre-existing project.** The original design assumed
`RunOptions.ProjectName` serves both the on-disk `.sln`/`.tsproj` file name
AND the real PLC-project name inside `TIPC` — true only for projects this
tool bootstrapped itself. Confirmed live against BOTH real projects that this
assumption fails for genuinely pre-existing ones:
- `PLC_NFL_SHARK_V2`: **no `.sln` at all** (a "loose" `.tsproj`), and its PLC
  project is named `PLC_NFL_prj` — different from the `.tsproj`'s own name
  `PLC_NFL_SHARK`.
- `PLC_NF_PRO`: has a `.sln` matching this tool's own folder convention, but
  its PLC project is `PLC_NF_PRO_project` — again different from the XAE
  project name `PLC_NF_PRO`.

Fixed with two new CLI overrides, CLI-only (never from `.stconfig`):
- **`--tsproj <path>`**: adopt an arbitrary `.tsproj` directly via
  `EnvDTE.Solution.AddFromFile` (new `TwinCatProjectOpener.OpenExistingReadOnly`),
  bypassing the `.sln`/dest-name convention entirely.
- **`--plc-name <name>`**: the real `TIPC` tree name, decoupled from
  `ProjectName` (which stays the on-disk file-name concept).

**Critical safety fix found and applied BEFORE running against real
projects:** `TwinCatSession.EnsureOpen` originally only skipped its
`Project.Save()`/`Solution.SaveAs()` step when `--tsproj` was explicitly
given — meaning reverse export via the *normal* `--dest`/`--name` path (as
used for `PLC_NF_PRO`, which already has a matching `.sln`) would have
actually WRITTEN to the real project. Fixed so reverse export ALWAYS attaches
read-only via `OpenExistingReadOnly`, regardless of whether a `.sln` already
exists — verified via byte-identical file hashes on a scratch project before
touching anything real.

**Live-validated end-to-end against two real production projects, read-only:**
| Project | Method | Result |
|---|---|---|
| `PLC_NFL_SHARK_V2` (no `.sln`) | `--tsproj ...PLC_NFL_SHARK.tsproj --plc-name PLC_NFL_prj` | 305 `.st` files, 11 libraries (exact match to the existing hand-authored `ST/Shark/libraries.xml`), IO tree (6 devices/45 nodes), 5 event classes, `links.xml` — all written to `A3D/PLC/st/shark_v2` |
| `PLC_NF_PRO` (has `.sln`) | `--dest ...A3D\PLC --name PLC_NF_PRO --plc-name PLC_NF_PRO_project` | 48 `.st` files (1 correctly reported as unsupported, not crashed), 10 libraries, IO tree (1 device/34 nodes, 5 flagged for review — a non-Beckhoff Festo/MFC catalog string, correctly not guessed), 2 event classes, `links.xml` — written to `A3D/PLC/st/pro` |

Also confirmed a full forward round-trip (reverse-export → forward-sync into a
fresh scratch project → `--build`) passes end-to-end, proving generated
source is genuinely usable, not just superficially plausible.

**One caveat found, disclosed to the user, and remediated:** merely *opening*
a real project via `OpenExistingReadOnly` — even though no code ever calls
`Save()`/`SaveAs()` — causes TwinCAT itself to silently bump one
auto-generated version-stamp field per project (`Global_Version.TcGVL`'s
`ProductVersion` for Shark V2; `PLC_NF_PRO_project.plcproj`'s
`ProgramVersion` + some XML re-indentation for PRO), reflecting the locally
installed TwinCAT build number. Confirmed cosmetic (no logic/content change)
via `git diff` both times, reverted via `git checkout --` immediately after
each run per explicit user confirmation. "Read-only" now means "never
saves/writes anything semantic," not "the filesystem is provably 100% inert
at the byte level" — worth knowing before a future run against a project with
uncommitted changes of its own.

## Task List — ALL COMPLETE

### Phase 1: Foundation + de-risk IO
- [x] Task 0: Create task dir; commit current working tree first (clean baseline)
- [x] Task 1: `--export-*` + `--overwrite` flags in `RunOptions` (+ `RunOptionsTests`)
- [x] Task 2 (SPIKE): RESOLVED — see status above (Name-parenthetical primary signal)

### Checkpoint A
- [x] Tests green; spike result recorded above; IO scope confirmed sufficient (no `.xti` fallback needed)

### Phase 2: Low-risk reversers (reuse proven readers)
- [x] Task 3: `ProjectCodeExporter` — walk tree → all `.st` (reuses `PlcObjectExporter`; new `IsExportableKind` helper)
- [x] Task 4: `LibraryManifestWriter` — references → `libraries.xml` (fixed real display-name format + implicit-reference crash)
- [x] Task 5: `EventManifestWriter` — `.tsproj` pool → `events.xml` + `event-classes/*.xml`

### Checkpoint B
- [x] Round-trips verified LIVE (writer → forward sync → BUILD PASSED on scratch projects)

### Phase 3: IO + orchestration
- [x] Task 6: `IoManifestWriter` — `TIID` tree → `io-devices.xml` (product heuristic confirmed against 30 real terminals + 5 real Festo/custom devices)
- [x] Task 7: Wire `--export-*`/`--export-all` into `Program`/`SyncPipeline` — lazy VS, overwrite guard, fixed order; PLUS `--tsproj`/`--plc-name` for arbitrary pre-existing projects; PLUS the always-read-only safety fix

### Checkpoint C
- [x] Overwrite guard verified via the built `.exe`
- [x] `--export-all` into an empty scratch source that forward-syncs + builds clean — CONFIRMED LIVE (twice: via `--tsproj` no-`.sln` path, and via normal `--dest`/`--name` path)

### Phase 4: Docs + real-project smoke
- [x] Task 8: README (reverse section + flag-table rows + adoption workflow + caveats)
- [x] Real-project smoke — CONFIRMED against BOTH `PLC_NFL_SHARK_V2` and `PLC_NF_PRO`, read-only, output at `A3D/PLC/st/shark_v2` and `A3D/PLC/st/pro`

### Checkpoint: Complete
- [x] All acceptance criteria met, build green (160 tests), live-validated against two real production projects, committed

## Risks and Mitigations
| Risk | Impact | Mitigation |
|------|--------|------------|
| Live terminal `Product` not readable from the tree | High (blocks `--export-io`) | Task 2 spike **before** committing to IO; `.tsproj`/`.xti` XML fallback; defer `--export-io` as last resort — everything else still ships |
| Reverse-export clobbers hand-edited `.st` source | High | `--overwrite` gate (CLI-only, refuses by default); all verification uses scratch source dirs, never ST/Shark |
| Event-class `<DataType>` entries indistinguishable from PLC data types / `MDP5001_*` types in the pool | Med | Heuristic on the event-class `<DataType>` shape (GUID + event SubItems); copy raw `<DataType>` verbatim to the template so the forward editor round-trips it; document per-`<Event>` field reversal as best-effort |
| Non-ASCII chars lossy on round-trip | Low | Known/documented for `--export` already (legacy codepage); carry the same caveat into the reverse docs |
| Enumerating IO tree double-counts terminals (flat + nested) | Med | Reuse the proven "genuine direct child only" rule (`PathName == parent^name`) from `IoSyncEngine.DeleteOrphans` |
| VS lifecycle regressions when only some `--export-*` run | Med | Reuse the existing lazy-`EnsureOpen`/`EnsureClosed` pattern; per-flag scratch runs in Checkpoint C |

## Open Questions
- **Event `<Event>` detail reversal:** `events.xml`'s `<Event Id/Severity/Message>`
  children are richer than what the forward path strictly needs (the editor
  consumes the raw `event-classes/<Name>.xml` template + a Name/Guid). Is a
  best-effort reconstruction of the `<Event>` rows enough for v1, or must it be
  byte-exact? (Proposed: best-effort; the template file is the source of truth
  for round-trip.) — resolve during Task 5.
- **`io-devices.xml` `<Links>` vs `links.xml`:** variable links are captured
  natively and completely by `--export-links` → `links.xml`. Do we also need to
  reverse them into `io-devices.xml`'s simpler `<Links>` section, or is
  `links.xml` sufficient? (Proposed: `links.xml` only; don't duplicate.) —
  confirm during Task 6/7.
- **Emit a starter `.stconfig`?** Reverse-export knows `dest`/`name`; optionally
  drop a `.stconfig` into the new `--source` so subsequent forward syncs need
  only `--source`. (Proposed: yes, nice-to-have in Task 8 if cheap.)
