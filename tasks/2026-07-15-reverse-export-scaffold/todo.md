# Reverse Export — Task Breakdown

Sibling of `plan.md`. Each task is sized S/M and leaves the tree building.
Verification uses `dotnet build -c Debug -f net48` + the test project, and — for
anything touching COM — a **scratch** source folder and **scratch** TwinCAT
project, never the real `ST/Shark` tree or its project.

---

## Task 0: Clean baseline
**Description:** Commit the current working tree (there are staged/untracked
changes from prior work) so the reverse-export diff is isolated.

**Acceptance criteria:**
- [ ] `git status` clean before Task 1 starts

**Verification:** `git log -1`, `git status`
**Dependencies:** None
**Files touched:** (commit only)
**Scope:** XS

---

## Task 1: `--export-*` + `--overwrite` flags in RunOptions
**Description:** Add the reverse-export flags to `RunOptions.Parse` and a
`ReverseExports` `[Flags]` enum (Code/Libs/Io/Events, mirroring `SyncStages`).
`--export-all` = all four. `--overwrite` is a plain bool, **CLI-only** (never
read from `.stconfig`, exactly like `--init`/`--confirm-delete*`). Add usage
text. No behavior yet — just parsing + surfacing.

**Acceptance criteria:**
- [ ] `--export-code/-libs/-io/-events/-all` parse into a `ReverseExports` value
- [ ] `--overwrite` parses; is NOT settable via `.stconfig`
- [ ] `--export-all` selects all four artifact bits
- [ ] Usage text lists the new flags under a "Reverse export" heading

**Verification:**
- [ ] `dotnet test` — new `RunOptionsTests` cases green (per-flag, `--export-all`, `.stconfig`-can't-set-`--overwrite`)
- [ ] `dotnet build` clean
**Dependencies:** Task 0
**Files touched:** `RunOptions.cs`, `beckhoffAutomationInterface.Tests/RunOptionsTests.cs`
**Scope:** S

---

## Task 2 (SPIKE): read a terminal's Product back from the live tree
**Description:** In a throwaway spike (temporary flag or scratch `Program`
branch), create an `EK1100 + EL1008` tree in a scratch project, `Save()`, then
read each terminal back and find where `Product` ("EL1008") lives. Try, in
order: `ItemSubTypeName`, `ItemSubType`, `ProduceXml()`, then parsing the saved
`.tsproj`/`.xti` device XML. Record findings in `plan.md`. **Then decide:**
include `--export-io` (Task 6) via the AI, via XML fallback, or defer it.

**Acceptance criteria:**
- [ ] A reliable way to recover `Product` per terminal is found, OR its absence is proven
- [ ] Decision (AI read / XML fallback / defer) recorded in `plan.md`
- [ ] Spike code removed; scratch project deleted

**Verification:**
- [ ] Manual: spike prints correct product strings for a known scratch tree
- [ ] `git status` shows no leftover spike code
**Dependencies:** Task 1
**Files touched:** (temporary spike only — reverted)
**Scope:** S–M (investigation)

---

## Task 3: ProjectCodeExporter — whole project → `.st`
**Description:** New `Sync/ProjectCodeExporter.cs`: recursively walk the project
root's POUs/DUTs/GVLs subtrees, and for every `PlcObjectExporter.IsSupported`
item, call `PlcObjectExporter.Export` and write it to its mirrored path
(`GetRelativeFolder` + `Name + ".st"`) under `--source`. Skip folder items and
child methods/properties (they're stitched into their owner's file). Return a
report of written/skipped/unsupported so callers can print counts. Reuses the
single-object machinery wholesale — this is the multi-object walk `--export`
never had.

**Acceptance criteria:**
- [ ] Every supported top-level POU/DUT/GVL is written exactly once, at its mirrored folder path
- [ ] Unsupported items are reported, not silently dropped or crashed on
- [ ] Methods/properties are NOT written as standalone files (owner-stitched only)

**Verification:**
- [ ] Round-trip: reverse a scratch project's code → forward `--sync-code` into a *second* scratch project → `--build` PASSED
- [ ] `dotnet build` clean
**Dependencies:** Task 1
**Files touched:** `Sync/ProjectCodeExporter.cs`, `beckhoffAutomationInterface.Tests/*` (walker/report unit test with a fake tree if feasible)
**Scope:** M

---

## Task 4: LibraryManifestWriter — references → `libraries.xml`
**Description:** New `Sync/LibraryManifestWriter.cs`: enumerate
`ITcPlcLibraryManager.References` (0-based!) and reuse
`LibrarySyncEngine.DisplayNamePattern` (extract it to a shared reader) to split
each display name into Name/Version/Company, then write `<Libraries>` XML.
Unparseable display names are emitted with a comment + a warning rather than
dropped.

**Acceptance criteria:**
- [ ] Produces a `libraries.xml` that `LibraryManifestParser.Parse` reads back to the same set
- [ ] 0-based enumeration (no off-by-one, per the documented `ITcPlcReferences` gotcha)
- [ ] Unparseable references are warned about, not lost

**Verification:**
- [ ] Round-trip: reverse a scratch project's libs → `libraries.xml` → forward `--sync-libs` → identical reference set, build clean
- [ ] `dotnet build` clean
**Dependencies:** Task 1
**Files touched:** `Sync/LibraryManifestWriter.cs`, `Sync/LibrarySyncEngine.cs` (extract shared display-name reader), tests
**Scope:** S

---

## Task 5: EventManifestWriter — `.tsproj` pool → `events.xml` + templates
**Description:** New `Sync/EventManifestWriter.cs`: read the `.tsproj`
`<DataTypes>` pool (via `TsprojDataTypePool`), select the entries that are
Event Classes (heuristic: GUID present + the event-class `<DataType>` shape,
distinguishing them from `MDP5001_*`/PLC-data-type entries), write each raw
`<DataType>` block verbatim to `event-classes/<Name>.xml` (the exact input
`TsprojEventClassEditor` consumes), and emit `<EventClass Name Guid>` (+
best-effort `<Event>` rows) into `events.xml`. No VS session needed — pure file
read, like `--check-events`.

**Acceptance criteria:**
- [ ] Each event class yields a template file the forward editor accepts unchanged
- [ ] `events.xml` reads back via `EventManifestParser.Parse`; `--check-events` reports all present
- [ ] Non-event-class pool entries (PLC data types) are NOT emitted as event classes

**Verification:**
- [ ] Round-trip: reverse real Shark's event classes into scratch source → forward `--sync-events` into a scratch project → `--check-events` exit 0
- [ ] `dotnet build` clean
**Dependencies:** Task 1
**Files touched:** `Sync/EventManifestWriter.cs`, tests (fixture `.tsproj` snippet)
**Scope:** M

---

## Task 6: IoManifestWriter — `TIID` tree → `io-devices.xml`  *(only if Task 2 passed)*
**Description:** New `Sync/IoManifestWriter.cs`: walk `TIID`, emit
`<Device Name Disabled>` for each genuine direct child, then recurse
Box/Terminal children (**genuine-direct-child rule** `PathName == parent^name`,
to avoid the flat+nested terminal double-count). `Product` from whatever Task 2
established; `Disabled` from `ITcSmTreeItem.Disabled`. Variable links are left
to `--export-links`/`links.xml` (no `<Links>` duplication — see plan Open Q).

**Acceptance criteria:**
- [ ] Produces `io-devices.xml` that `IoManifestParser.Parse` reads back to the same tree
- [ ] No duplicated terminals (flat-vs-nested handled)
- [ ] Device `Disabled` state preserved

**Verification:**
- [ ] Round-trip: reverse a scratch IO tree → `io-devices.xml` → forward `--sync-io` → `0 created, 0 deleted` (idempotent), build clean
- [ ] `dotnet build` clean
**Dependencies:** Task 2 (spike must pass), Task 1
**Files touched:** `Sync/IoManifestWriter.cs`, tests
**Scope:** M

---

## Task 7: Wire orchestration into Program/SyncPipeline
**Description:** In `SyncPipeline.Run`, add a reverse branch (before the forward
stages, like the existing `--export`/`--export-links` early-returns): run the
selected `ReverseExports` in fixed order (events → code → libs → io → links),
opening VS lazily (events skips it). Enforce the **overwrite guard** up front in
`Program.Main` (scan `--source` for existing `.st`/manifests; exit 1 without
`--overwrite`). `--export-all` also triggers the existing `--export-links`.
Create `--source` if missing.

**Acceptance criteria:**
- [ ] Each `--export-*` alone runs only its artifact; `--export-all` runs all + links
- [ ] `--export-events` alone opens no VS
- [ ] Non-empty `--source` without `--overwrite` → exit 1 with the guard message; with `--overwrite` → proceeds
- [ ] Reverse flags are mutually exclusive with forward stage flags (or documented precedence)

**Verification:**
- [ ] Scratch matrix: each flag alone + `--export-all`; overwrite-guard both ways; `$LASTEXITCODE` checked
- [ ] `dotnet build` clean
**Dependencies:** Tasks 3, 4, 5, (6)
**Files touched:** `Program.cs`, `SyncPipeline.cs`, `RunOptions.cs` (guard helper), tests
**Scope:** M

---

## Task 8: Docs + real-project smoke
**Description:** README: new "Reverse export / adopting an existing project"
section, flag table rows, and a typical one-time adoption workflow
(`--export-all` into an empty dir → commit → forward-sync thereafter). Carry the
non-ASCII lossy caveat over from `--export`. Optionally emit a starter
`.stconfig`. Smoke `--export-all` against the **real Shark** project into a
**scratch** source dir (read-only w.r.t. the project), then forward-sync that
scratch source into a scratch project and confirm `--build` PASSED.

**Acceptance criteria:**
- [ ] README documents every new flag + the adoption workflow + overwrite guard + caveats
- [ ] Real-project smoke: `--export-all` produces a full tree; forward round-trip builds clean
- [ ] `.stconfig` emission decided (done or explicitly deferred)

**Verification:**
- [ ] Manual smoke logged; scratch artifacts cleaned up; real project untouched
- [ ] `dotnet build` clean; final commit
**Dependencies:** Task 7
**Files touched:** `README.md`, maybe `Program.cs`/`SyncPipeline.cs` (`.stconfig` emit)
**Scope:** M
