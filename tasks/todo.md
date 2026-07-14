# Task List: Post-Review Hardening of the ST→TwinCAT Sync/Build Loop

See `tasks/plan.md` for the phase overview and rationale.

---

## Task 1: Add a unit test project covering existing parser/lint/format logic

**Description:** Create a test project (xunit, targeting net48 to match
`beckhoffAutomationInterface.csproj`) and write characterization tests for
the parts of `Sync/` that have no COM dependency: `StFileParser`,
`StPouSource`, `IgnoreRules`, `StLinter`, `StFormatter`. This is a safety
net for every later task that touches parsing/sync logic — it captures
*current* behavior before anything changes.

**Acceptance criteria:**
- [x] New test project added to `beckhoffAutomationInterface.slnx` and builds
- [x] Tests cover: FB file with inline METHODs and PROPERTIES, PROGRAM file
      with an inline private METHOD, INTERFACE with EXTENDS, all three DUT
      kinds (ENUM/STRUCT/ALIAS), a GVL file, a standalone
      `<Owner>.<Method>.st` file, `.stignore`/`--ignore` glob matching,
      `StLinter` prefix violations, `StFormatter`'s four checks (mixed EOL,
      trailing whitespace, missing/extra trailing newline)

**Verification:**
- [x] `dotnet test` passes (25/25)
- [x] Build succeeds: `dotnet build beckhoffAutomationInterface.slnx`

**Status: DONE.** Internal `Sync/*` classes are exposed to the test
project via `InternalsVisibleTo` rather than being made public.

**Dependencies:** None

**Files likely touched:**
- `beckhoffAutomationInterface.Tests/*.cs` (new)
- `beckhoffAutomationInterface.Tests/beckhoffAutomationInterface.Tests.csproj` (new)
- `beckhoffAutomationInterface.slnx`

**Estimated scope:** Medium (3-5 files)

---

## Task 2: Fix duplicate library references

**Description:** `TODO.md` reports `Tc2_Standard`, `Tc2_System`, and
`Tc3_Module` getting added twice during a sync. Likely cause:
`LibrarySyncEngine.Sync` snapshots `existing` references once at the top of
the method (`Sync/LibrarySyncEngine.cs:34-45`), but `AddLibrary` can cause
TwinCAT to auto-add a transitive dependency mid-loop that isn't reflected in
that snapshot — so a later explicit `AddLibrary` call for that same
dependency creates a second reference instead of hitting the existing
`ArgumentException("already contained")` catch (line 58-63). Reproduce
first against the real project before changing anything.

**Acceptance criteria:**
- [ ] Root cause confirmed by reproducing the duplicate against the real
      Shark project (or a smaller repro with a library that has
      dependencies)
- [ ] After the fix, a full sync no longer produces duplicate entries for
      any library, including ones added as a side effect of another
      library's dependencies
- [ ] Re-running the sync a second time is a no-op (0 added, 0 removed) —
      idempotency preserved

**Verification:**
- [ ] Manual: run a full sync against the real project, inspect the PLC
      project's library references (`.tsproj` or via
      `ITcPlcLibraryManager.References`) before/after, confirm no duplicates
- [ ] Manual: run the sync a second time immediately after, confirm
      `LibrarySyncReport.Added`/`Removed` are both empty

**Dependencies:** None

**Files likely touched:**
- `Sync/LibrarySyncEngine.cs`

**Estimated scope:** Small (1-2 files)

**Status: NOT REPRODUCIBLE — likely already fixed.** Ran two consecutive
full syncs against the real Shark project (2026-07-14). Run 1 added
Tc2_Standard/Tc2_System/Tc3_Module (3 added, 0 removed); run 2 immediately
after was a no-op (0 added, 0 removed), and `Shark.plcproj` shows exactly
one `LibraryReference` entry per library — no duplicates. The existing
dedup logic in `LibrarySyncEngine.Sync` (existing-reference snapshot +
`ArgumentException("already contained")` catch) appears to already handle
this. Closing unless it resurfaces under a different trigger (e.g. a
library with an actual transitive dependency chain, which this run didn't
exercise).

---

## Task 3: Investigate and fix "Unknown type" DUT errors for EL3174/EL3214-derived types

**Description:** `TODO.md` logs a real build error: `Error C0077 Unknown
type: 'MDP5001_320_A369A904'` (and a sibling `MDP5001_300_7E2119CA` for a
different terminal) in
`.../App/Shark/Types/T_Beckhoff_TempSensor_Chuck.TcDUT`. These look like
auto-generated EtherCAT terminal (EL3174, EL3214) process-image/parameter
DUT names. Determine whether `IoManifestParser`/`IoSyncEngine` should be
generating stub DUTs for these terminal-specific types, or whether this is
a naming mismatch between what a hand-written `.st` DUT expects and what
TwinCAT actually names the terminal's generated type — don't guess at a
fix before reproducing and inspecting the actual `.tsproj`/tree state.

**Acceptance criteria:**
- [ ] Root cause identified and written down (which side owns
      `MDP5001_300_7E2119CA`/`MDP5001_320_A369A904` — TwinCAT-generated or
      user-authored — and why the reference fails to resolve today)
- [ ] Full sync + build against the real project produces zero `C0077
      Unknown type` errors for these two types

**Verification:**
- [ ] Manual: full sync + build against the real Shark project, confirm
      clean build (0 errors) where these two errors previously appeared

**Dependencies:** None

**Files likely touched:**
- `Sync/IoManifestParser.cs`
- `Sync/IoSyncEngine.cs`
- Possibly a `.st` DUT source under `ST/`

**Estimated scope:** Medium (investigation-first; fix scope depends on root cause)

**Status: ROOT-CAUSED, fix needs your input.** Reproduced against the real
Shark project (2026-07-14) — same 2 types, now 4 error sites:
`T_Beckhoff_AmbientSensor.st`, `T_Beckhoff_PressureSensor.st` (both alias
`MDP5001_300_7E2119CA`) and `T_Beckhoff_TempSensor_Chuck.st`,
`T_Beckhoff_TempSensor_General.st` (both alias `MDP5001_320_A369A904`).

Root cause: `MDP5001_300_7E2119CA`/`MDP5001_320_A369A904` are TwinCAT
auto-generated PDO type names for specific EL3174/EL3214 analog-input
terminal instances (confirmed by `T_Beckhoff_PressureSensor.st`'s own
comment: *"auto-generated by TwinCAT and depends on the specific EL3xxx
terminal used"*). But **`ST/Shark` has no `io-devices.xml` manifest at
all**, and the live project has zero EL3174/EL3214 terminals or
`MDP5001_*` types anywhere in its `.tsproj`/`.xti` files — the hardware
was never added to this project's EtherCAT tree, so TwinCAT never
generated the types the aliases hard-reference.

This isn't a bug in the sync tool — `IoManifestParser`/`IoSyncEngine`
already support declaring `Device -> Box -> Terminal` by product code
(see `Sync/IoDeviceSpec.cs`), so an `io-devices.xml` naming the actual
EL3174/EL3214 terminals (and whatever coupler/master they sit under)
would let the tool create them. **Needs from you:** the actual bus
topology (master name, coupler, terminal order/slot) to author that
manifest — I don't have the physical hardware layout. Separately: the
auto-generated GUID suffix may not be stable/reproducible across a fresh
terminal scan, in which case the hardcoded literal in the two `.st` alias
pairs might need updating to whatever TwinCAT actually generates once the
terminals exist — can't confirm this without adding them and re-checking.

**Update (2026-07-14) — deeper than expected, still open.** Got the
topology from the user (`images/devices.jpg`), extended
`IoDeviceSpec`/`IoManifestParser`/`IoSyncEngine` to support arbitrarily
nested Box/Terminal hierarchy (Task 4, done), and authored
`ST/Shark/io-devices.xml` for BH1/BH2 (Task 5). Ran it against the real
project: **the IO tree was created correctly** (34 items, right
nesting — `CreateChild` + recursion works exactly as designed) **but the
build still fails with the identical 4 `Unknown type` errors.**

Conclusion: `CreateChild(name, TREEITEMTYPE_TERM, "", "EL3174")` creates a
generic terminal placeholder, but does NOT trigger whatever TwinCAT
mechanism generates the instance-specific `MDP5001_*` PDO/parameter DUT —
that appears to only come from a real hardware/topology scan (Insert via
ESI catalog against real or simulated hardware), not from the bare
Automation Interface creation call. This matches the same category of
dead-end already documented elsewhere in this repo (Event Class creation
in `docs/ideas/st-plc-bidirectional-sync.md`, and IO channel linking
requiring Activate Configuration in `docs/ideas/st-source-twincat-sync.md`)
— some things the Automation Interface can create structurally, it
apparently can't fully instantiate without a real scan.

**Not yet investigated / needs a decision:**
- Does `ITcSmTreeItem`/`ITcSysManager` expose any call to trigger PDO/type
  generation for an already-created terminal (short of a full hardware
  scan)? Unconfirmed — would need targeted spiking against the
  Automation Interface docs/object model.
- Alternatively: is Activate Configuration (against a real or simulated
  target) the only path, same as the existing IO-linking limitation? If
  so, this bug may not be fixable through this tool alone — it may need
  to stay a manual one-time step (create terminals via this tool, then a
  human runs Activate Configuration once), similar to Event Classes.

**Update (2026-07-14) — root cause of the ConsumeXml failures found:
`ProduceXml`/`ConsumeXml` use a different XML schema than the project
file.** Dumped `device.ProduceXml(true)` to disk for inspection. It
returns `<TreeItem><ItemName>...<EtherCAT>{master-level settings only:
DC, EoE, SlaveSettings, ...}</EtherCAT></TreeItem>` — a "TreeItem"
schema entirely different from the `.tsproj`/`.xti` **project file**
schema (`<TcSmItem><Box Id="..."><EtherCAT CreateDeviceDataType="true"
...>`). No `<Box>` element appears anywhere in the `TreeItem` output
despite `ChildCount=22` and `bRecursive=true` — this schema doesn't
expose per-slave/per-terminal configuration, `CreateDeviceDataType`, or
`PlcDataTypes` at all. Every attempt so far (both the original
per-terminal one and the later device-level merge) fed project-file-
shaped XML into an API that doesn't speak that dialect — the write
wasn't rejected, it was just building a document with fields TwinCAT's
`ConsumeXml` doesn't recognize as instructions to change anything, so
nothing changed.

**Conclusion: "Create PLC Data Type" does not appear to be settable via
the documented `ITcSmTreeItem` Automation Interface at all** (checked
every interface version 2-10; nothing else exists). This is the same
category of dead-end already hit for Event Classes and IO-channel
linking-without-Activate-Configuration elsewhere in this repo — some
project settings are UI-only / project-file-only, with no Automation
Interface equivalent.

**Options going forward (needs a decision, not a code fix):**
1. **Accept as a manual, one-time step.** The tool already creates the
   device/terminal tree correctly and already verifies+warns when
   CreateDeviceDataType isn't set (see the Warnings mechanism above) —
   extend that warning to give clear, specific instructions ("check
   'Create PLC Data Type' + 'Channel/Slot' on the Plc tab of terminal X,
   then re-run"), same UX as the existing Event Class dead-end.
2. **Edit the `.tsproj` file directly on disk**, outside COM entirely,
   injecting the exact known-good `<Box>/<EtherCAT
   CreateDeviceDataType=...>` + `<DataTypes>`/`<PlcDataTypes>` XML
   (already extracted and validated against the reference project — see
   `ST/Shark/plc-data-types/*.xml`) into the live `.tsproj`, then having
   Visual Studio reload before building. More ambitious, and carries
   real risk (concurrent-write/corruption if VS has the file open,
   timing around save/reload) — would need its own careful, isolated
   implementation and testing before trusting it against a real project.

**(Older) update (2026-07-14) — attempted fix via ConsumeXml, confirmed NOT
working.** User pointed at `images/create_PLC_dataType.jpg` (a working
reference project, `PLC_NFL_SHARK`, with "Create PLC Data Type" +
"Channel/Slot" checked on its real EL3174 terminals) and its saved
`.xti` file, which showed the setting is
`CreateDeviceDataType="true" DeviceDataTypePerChannel="true"` on the
Box's `<EtherCAT>` element, with the generated `MDP5001_*` DUTs stored
inline in the same file. Confirmed via reflection that `ITcSmTreeItem`
exposes `ProduceXml(bool)`/`ConsumeXml(string)`/`GetLastXmlError()`.
Implemented `IoSyncEngine.ApplyPlcDataTypeSetting`: read the terminal's
current XML, flip only those two attributes, write back via
`ConsumeXml`. Added a `CreatePlcType="Channel"` attribute to
`IoNodeSpec`/`io-devices.xml` and applied it to all EL3174/EL3214
terminals.

Ran it against the real project (which had to be rebuilt from scratch
in this same session — see below): `ConsumeXml` did not throw, and the
tool reported `~ state EL3174_2.1 -> Create PLC Data Type (Channel)`
for all 14 terminals. **But checking the actual saved `Shark.tsproj`
afterward shows `CreateDeviceDataType` is completely absent from
`EL3174_2.1`'s `<EtherCAT>` element — not `false`, missing entirely.**
The write did not persist, and the build shows the identical 4
`Unknown type` errors. `ConsumeXml` not throwing does not mean it did
what we asked; something about the XML round-trip (wrong scope from
`ProduceXml(false)`, a required companion element we didn't include, or
this attribute being rejected outside interactive UI context) is
silently dropping the change. Not spending more full-project-rebuild
cycles (~15-20 min each) guessing further without new information —
next step needs either Beckhoff documentation/support on this specific
attribute, or accepting this as a manual one-time UI step per terminal
(same precedent as Event Classes below).

**Separate regression surfaced by this session's rebuild:** the user
deleted the live `A3D/Shark` project mid-session, and the tool's
"reopen if exists, else bootstrap new" logic (`TwinCatProjectOpener`)
silently took the bootstrap-new path since `Shark.sln` no longer
existed. The fresh project has no Event Classes (they were created
once, manually, in the now-deleted project — matches the documented
dead-end in `docs/ideas/st-plc-bidirectional-sync.md`), so the build
now also shows 19 new errors referencing `TC_EVENTS.BeckhoffLibEvents`
in `FB_TcEventLoggerSink`. Not a tool bug, but a real consequence: this
tool cannot recreate Event Classes, so deleting the project loses them.
User feedback from this: check destination project state before
launching a run that could silently switch from "reuse" to "rebuild
from scratch" — flag it up front rather than after the fact.

**Fixed (2026-07-14):** `ApplyPlcDataTypeSetting` now re-reads the
node's XML after `ConsumeXml` and only reports success if
`CreateDeviceDataType` is actually present; otherwise it adds to a new
`IoSyncReport.Warnings` list (printed as `!! WARNING`). No more silent
false-positive "state changed" claims.

**User direction (2026-07-14) on workflow going forward:** before
compiling, verify (1) devices are present — create if missing — and
(2) event classes are present, and only proceed to POU/build work once
both are confirmed. Devices are already checked/created every run via
`IoSyncEngine` (idempotent), and Events already have a fast, no-VS
read-only check (`--events-only`, via `EventClassChecker`) — but neither
is being used as an explicit go/no-go gate before the expensive full
sync+build. **Possible follow-up task (not started):** reorder
`Program.RunSync` so IO device sync (and an events check) run — and are
confirmed — before the POU sync/build, so a missing prerequisite is
caught before spending ~5+ minutes syncing 1261 POUs. Not implemented
yet; flagging for a decision on priority.

---

## Checkpoint: Known bugs closed
- [ ] Full sync+build against the real Shark project shows no duplicate
      library references and no `C0077` errors for either type above
- [ ] `TODO.md` / `TODO.xml` deleted (superseded by Tasks 2-3 above)

---

## Task 4: Confirm TwinCAT's reported FileName/Line shape per PLC-object kind

**Description:** `TODO.md` already shows what a DUT compile error reports:
`FileName` = the exported `.TcDUT` path, `Line` = always `1`. Before
designing the general fix (Task 5-6), confirm the same shape (or a
different one) for a FUNCTION_BLOCK error, a METHOD error, a PROPERTY
error, and a GVL error — deliberately break each kind (e.g. reference an
undeclared variable) in a throwaway `.st` file, run a sync, and record the
raw `BuildError.FileName`/`Line` the tool prints today for each.

**Acceptance criteria:**
- [ ] Written note (append to this task or to `docs/ideas/`) listing, per
      `PouKind`, the exact `FileName` pattern observed and whether `Line`
      is ever meaningful (not always `1`)

**Verification:**
- [x] Manual: reproduced against a real TwinCAT/Visual Studio run for DUT
- [ ] Still need: FUNCTION_BLOCK, METHOD, PROPERTY, GVL cases

**Dependencies:** None (can run in parallel with Phase 2)

**Files likely touched:** None (investigation only)

**Estimated scope:** Small (1-2 files, or none — notes only)

**Partial finding (DUT, 2026-07-14):** Task 3's repro gave 4 real DUT
compile errors from the Shark project:
`T_Beckhoff_AmbientSensor.TcDUT:5`, `T_Beckhoff_PressureSensor.TcDUT:13`,
`T_Beckhoff_TempSensor_Chuck.TcDUT:1`,
`T_Beckhoff_TempSensor_General.TcDUT:1`. Two things confirmed:
1. `FileName` is always the exported `.TcDUT` path under the TwinCAT
   project's own folder tree, never the original `.st` relative path.
2. `Line` is **not always 1** — `:5` and `:13` are plausible-looking line
   numbers for their respective files. So the fix in Task 6 can't assume
   "line is always meaningless" — it needs to map a real line number in
   the *exported* file back to the right line in the *original* `.st`
   file, which is harder than just substituting the file name. Still need
   FUNCTION_BLOCK/METHOD/PROPERTY/GVL cases to know if this holds generally.

---

## Task 5: Track `.st` file/line provenance through parsing and sync

**Description:** Extend `StFileParser`'s segment splitting (it already
computes segment boundaries in `FindMethodBoundaries`/`ParseMethodSegments`
— that's the data needed) so every emitted `StPouSource` carries its
originating `.st` relative path and 1-based starting line, and thread that
through `PouSyncEngine.Sync` so each synced tree item's identity can be
traced back to a specific file+line.

**Acceptance criteria:**
- [ ] `StPouSource` (or an accompanying type) exposes source file + starting
      line for its declaration and (if present) implementation sections
- [ ] All Task 1 tests still pass unmodified
- [ ] New tests confirm correct provenance for: a multi-method FB file
      (each method's line matches where its `METHOD` keyword actually is),
      and a standalone `<Owner>.<Method>.st` file

**Verification:**
- [ ] `dotnet test` passes, including new provenance-specific tests

**Dependencies:** Task 1 (test harness), Task 4 (confirms what shape is worth mapping to)

**Files likely touched:**
- `Sync/StFileParser.cs`
- `Sync/StPouSource.cs`
- `Sync/PouSyncEngine.cs`

**Estimated scope:** Medium (3-5 files)

---

## Task 6: Translate build errors back to `.st` path/line before printing

**Description:** Using the provenance map from Task 5, rewrite
`BuildError.FileName`/`Line` to the original `.st` relative path/line before
`Program.RunSync` prints it. When no provenance entry matches (e.g. an
error from library code, or an object not touched this run), fall back to
the raw TwinCAT-reported value, clearly labeled so it's never confused with
a mapped one.

**Acceptance criteria:**
- [ ] For each kind exercised in Task 4, the tool prints the real `.st`
      relative path and a line landing on/near the actual broken line —
      not an internal export path with `Line: 1`
- [ ] Unmapped errors still print (raw + labeled), never silently dropped

**Verification:**
- [ ] Manual: deliberately break one method in a multi-method FB file, run
      the tool, confirm the printed error names that file and a plausible
      line
- [ ] Unit tests on the pure translation function: given a fake provenance
      map + a fake `BuildError` list, assert the remapped output (including
      the unmapped fallback case)

**Dependencies:** Task 5

**Files likely touched:**
- `Program.cs` (build-report printing in `RunSync`)
- `Sync/ErrorLocationResolver.cs` (new, or similarly named)

**Estimated scope:** Medium (3-5 files)

---

## Checkpoint: Feedback loop is trustworthy
- [ ] Deliberately breaking one method in a multi-method FB file and
      re-running the tool prints that file and a line landing on/near the
      real error
- [ ] Both mapped and unmapped errors are visible in the output — nothing
      silently dropped

---

## Task 7: Decide the orphan/rename policy

**Description:** No code — a decision. Renaming/deleting a METHOD, PROPERTY,
or top-level POU inside `.st` source currently leaves the stale version
compiling silently in the PLC project forever, except for whole top-level
objects under `--incremental --confirm-delete` (exact name match only).
Pick one:
- **(a) Warn-only, always on** — every run (not just `--incremental`) diffs
  the current parse's object/member names against a recorded "known names"
  set and prints a warning for anything that disappeared, without deleting
  anything. Lowest risk, matches the existing safer-default philosophy.
- **(b) Prune, opt-in** — extend deletion to METHOD/PROPERTY children and to
  full-sync top-level orphans, gated behind an explicit flag analogous to
  `--confirm-delete`.
- **(c) Both** — warn always, prune only when explicitly requested.

**Acceptance criteria:**
- [ ] One paragraph decision recorded (in this file or `tasks/plan.md`)
      covering: which option, at what scope (methods/properties only? full
      top-level orphans too? both incremental and full sync, or just one?)

**Verification:** N/A (decision task)

**Dependencies:** None

**Files likely touched:** None

**Estimated scope:** XS (decision only)

---

## Task 8: Implement the chosen orphan/rename policy

**Description:** Build whatever Task 7 decided. If warn-only: track known
object/member names across runs (extend `.st-sync-state` or add a sibling
state file) and diff against the current parse on every run, printing a
warning list — no deletions. If prune is in scope: extend
`PouSyncEngine.Sync` to delete METHOD/PROPERTY children of an owner that
are no longer present in the current parse of that owner's file, and/or
extend full-sync to compute and delete top-level orphans the same way
`IncrementalDeleter` does today for `--incremental`, gated behind an
explicit flag.

**Acceptance criteria:**
- [ ] Renaming a METHOD inside an FB `.st` file and re-running the tool
      produces the behavior Task 7 decided (a clear warning naming the
      stale method, and/or its removal) — never silent staleness
- [ ] Same for a deleted top-level POU/DUT/GVL outside `--incremental`
      (if Task 7's decision covers full sync)

**Verification:**
- [ ] Manual: rename then delete a method, and separately a top-level POU,
      in a throwaway `.st` tree; run the tool twice; confirm the reported
      behavior matches Task 7's decision
- [ ] Unit tests on the pure name-diffing logic (given "before" and "after"
      name sets, assert the correct warn/prune list)

**Dependencies:** Task 1 (test harness); Task 7 (the spec to build)

**Files likely touched:**
- `Sync/PouSyncEngine.cs`
- `Sync/SyncState.cs`
- `RunOptions.cs`
- `Program.cs`

**Estimated scope:** Medium–Large (split further once Task 7's scope is known — if it covers both methods/properties AND full-sync top-level orphans, split into two tasks: one per scope)

---

## Checkpoint: Complete
- [ ] All four issues (2 from TODO.md + 2 from the code review) verified
      against a real TwinCAT/Visual Studio run, not just unit tests
- [ ] `TODO.md`/`TODO.xml` removed
- [ ] Renaming/deleting `.st` content no longer leaves invisible stale code
      compiling silently
- [ ] A build error reliably points back at the `.st` file/line that
      actually caused it
