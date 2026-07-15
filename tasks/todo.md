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

**Event Class creation is NOT a dead end after all — implemented and
confirmed working (2026-07-14).** Added `Sync/TsprojEventClassEditor.cs`,
which edits the .tsproj on disk exactly like `TsprojPlcDataTypeEditor`
(same top-level `<DataTypes>` pool, same close-VS/edit/reopen-VS
sequence in `Program.RunSync`). The content comes from
`ST/Shark/event-classes/BeckhoffLibEvents.xml`, copied verbatim from
`PLC_NFL_SHARK.tsproj`'s real `BeckhoffLibEvents` `<DataType>` (real
GUID `{70AB1C3F-...}`, not a freshly generated one — the earlier 4
dead-end attempts used both the wrong parent element AND an invented
GUID).

Ran against a disposable scratch project (never the real Shark project)
to verify end-to-end, since the doc above records VS silently stripping
hand-inserted `<DataType>` blocks on its own next save as a real risk for
the 4 earlier (wrong-location) attempts:
1. First run crashed with an unhandled `XmlException` (`An XML comment
   cannot contain '--'`) — `BeckhoffLibEvents.xml`'s doc comment had a
   flattened em-dash (literal `--`), illegal inside an XML comment.
   `PlcDataTypeTemplate.Load` doesn't catch parse errors, so this killed
   the whole process right after Visual Studio was closed, before the
   Event Class was ever written or the build ever ran. This — not a
   silent VS strip — was the actual cause of "compilation failed because
   events are not created." Fixed the two `--` occurrences; swept the
   other shipped XML (`plc-data-types/*.xml`, `events.xml`,
   `io-devices.xml`, `libraries.xml`) for the same mistake — none found.
2. Re-ran clean: `~ added BeckhoffLibEvents`, VS reopened, solution
   opened, **BUILD PASSED**. Confirmed via direct `.tsproj` inspection
   afterward that the `BeckhoffLibEvents` `<DataType>` survived VS
   reopening + the solution reload + the build — it was NOT silently
   stripped this time (top-level `<DataTypes>` pool, matching how VS
   itself would place it, unlike the earlier wrong-location attempts).
3. Third run confirmed idempotency: `Event class check: 1 declared, 1
   present, 0 missing` — no re-edit, no VS close/reopen, clean build.

Also fixed an unrelated regression surfaced while verifying this:
`PlcDataTypeTemplateTests.cs` still called `PlcDataTypeTemplate.Load`
with its pre-refactor signature (folder without the `plc-data-types`
subfolder appended) and was failing 2 tests; both production call sites
(`TsprojPlcDataTypeEditor`, `TsprojEventClassEditor`) already passed the
correct folder, so this wasn't the events bug — just stale tests. All 46
tests pass now.

**Not yet re-verified against the real Shark project** — the scratch
test proves the mechanism works, but the real project's io-devices.xml
may exercise the `<Links>`/library-sync `project.Save()` calls this
scratch test didn't (0 links, 0 libraries declared), so a real run is
still the final confirmation.

**Near-miss (2026-07-14): a real run against the actual Shark project was
attempted (dest corrected to `A3D`, not `A3D\Shark` — an earlier attempt
with the wrong `--dest` bootstrapped a separate duplicate project inside
the real one's own folder tree; cleaned up, real project unaffected,
confirmed via untouched file mtimes). It reached IO device sync and
crashed: `System.Runtime.InteropServices.COMException: The remote
procedure call failed (0x800706BE)` in `IoSyncEngine.DeleteOrphans` ->
`ITcSmTreeItem.DeleteChild`.**

Root cause: `io-devices.xml` declared `EK1100_1.1`/`EK1100_2.1` nested
inside `Box 1 (CU2508)`/`Box 44 (CU2508)` (matching the images/devices.jpg
screenshot's visual grouping), but the real `Shark.tsproj`'s actual XML
nesting (confirmed by reading raw tab-depth in the file directly, not
guessing) has them as SIBLINGS — direct children of the Device — with
EK1100 itself carrying the real terminal children. Worse for BH2:
`EK1100_2.1` there has NO children at all; all 19 remaining terminals are
nested under the FIRST terminal, `EL3174_2.1` (apparently TwinCAT's own
bus-order chain encoding, not a real ownership relationship). Because the
manifest didn't match reality, `DeleteOrphans` saw `EK1100_1.1`/
`EK1100_2.1`/`EL3174_2.1` as undeclared and tried to delete them — which
would have deleted almost ALL of both device's real hardware (~28
terminals). The RPC failure happened to prevent this; confirmed via
direct `.tsproj` inspection that nothing was actually lost.

Fixed `io-devices.xml` to mirror the real nesting exactly. Verified on a
scratch project: first run created all 34 items cleanly (0 deleted).
**Second run surfaced a SEPARATE bug**: not idempotent for the BH2 chained
shape — `TreeItemFactory.GetOrCreate`'s `LookupTreeItem` throws for
`EL3174_2.1` even though it exists (probably because forcing terminals to
be "children" of another terminal, rather than a coupler, doesn't produce
a stably re-lookupable path), so it's treated as missing, recreated, and
the OLD subtree then looks like a fresh orphan — 20 created + 20 deleted
every run. BH1's plain single-level coupler nesting had zero such issue
(fully idempotent).

**Fixed properly, not just patched around:** rather than trust the
manifest is now perfectly accurate (or that no other topology surprise
exists), added an opt-in safety gate matching the existing
`--incremental`/`--confirm-delete` pattern: `IoSyncEngine.DeleteOrphans`
now takes a `confirmDelete` flag (new `--confirm-delete-io` CLI flag, off
by default). Without it, undeclared IO items are reported via the new
`IoSyncReport.Warnings` list and never deleted. Verified on a scratch
project: same non-idempotent lookup-miss still creates a duplicate
`EL3174_2.1` subtree each run, but the second run now shows `0 deleted,
20 warning(s)` instead of `20 deleted` — confirmed no data loss is
possible even with the underlying lookup bug still present and
unexplained. All 46 unit tests still pass.

**Still open:** the `GetOrCreate`/`LookupTreeItem` lookup-miss for
terminal-parents-terminal topologies is not root-caused, only made safe.
A real run against Shark shows ~19 created + 19 warned for BH2 every
run (messy but not destructive) until that's actually fixed or the
manifest is restructured to avoid the shape entirely.

**RESOLVED — Task 3 CLOSED (2026-07-14): full sync+build against the real
Shark project now BUILDS CLEAN (0 errors).** The sequence that got there:
1. Re-ran with the safety gate on: Event Class `BeckhoffLibEvents` was
   written to the real .tsproj, survived VS reopen, and all 19
   `TC_EVENTS.BeckhoffLibEvents` errors disappeared. Errors dropped from
   23 to 2 — both `Unknown type: 'MDP5001_320_A369A904'` (EL3214); the
   EL3174 errors (`MDP5001_300_7E2119CA`) were already resolved by the
   injected template.
2. Root-caused the EL3214 leftover: **the MDP5001_<suffix> is a
   config-hash TwinCAT computes from the terminal's actual PDO/revision
   configuration, so it is NOT portable across ESI revisions.** The
   reference project's real-scanned EL3214s (RevisionNo #x00110000,
   CoeType 3) hash to A369A904; this machine's ESI catalog instantiates
   EL3214 at #x00120000 (CoeType 15, different Pdo flags) which hashes to
   **MDP5001_320_5D7E181C**. EL3174 worked purely because both projects'
   EL3174s are the same revision (#x00110000 → same hash 7E2119CA).
   Confirmed empirically: a --build-only run let TwinCAT load + save, and
   it silently rewrote every injected A369A904 occurrence in the .tsproj
   to 5D7E181C (45 refs) — TwinCAT's own regeneration is authoritative.
3. Fix: updated the two .st aliases (T_Beckhoff_TempSensor_Chuck/_General)
   from A369A904 → 5D7E181C, and rebuilt plc-data-types/EL3214.xml from
   the TwinCAT-generated defs (Status_803D15A4_Plc + I_5D7E181C +
   5D7E181C; the 182C60D3 device-level pair is revision-independent and
   unchanged). Re-ran full sync+build: **BUILD PASSED**, event check
   `1 declared, 1 present, 0 missing`, libraries `0 added, 0 removed` (no
   duplicates — Task 2's checkpoint met in the same run).

Portability caveat recorded in the template/alias comments: on a machine
whose ESI revision differs, TwinCAT will regenerate a different suffix
and the aliases must be updated to what TwinCAT actually generates
(empirically: run --build-only once, then read the MDP5001_* names out of
the saved .tsproj).

---

## Checkpoint: Known bugs closed
- [x] Full sync+build against the real Shark project shows no duplicate
      library references and no `C0077` errors for either type above
      (2026-07-14: BUILD PASSED, 0 errors — see Task 3's RESOLVED note)
- [x] `TODO.md` / `TODO.xml` deleted (superseded by Tasks 2-3 above)

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
- [x] Written note (append to this task or to `docs/ideas/`) listing, per
      `PouKind`, the exact `FileName` pattern observed and whether `Line`
      is ever meaningful (not always `1`)

**Verification:**
- [x] Manual: reproduced against a real TwinCAT/Visual Studio run for DUT
- [x] FUNCTION_BLOCK, METHOD, PROPERTY, GVL cases (scratch project with one
      deliberately broken object of each kind, 2026-07-14)

**Status: DONE (2026-07-14). Findings** (TwinCAT 3.1.4026 / VS2022; every
`Line` IS meaningful — 1-based within the named SECTION, not the file):

| Kind      | FileName pattern                          | Line is relative to        |
|-----------|-------------------------------------------|----------------------------|
| FB body   | `...\FB_X.TcPOU (Impl):N`                 | the FB's implementation    |
| METHOD    | `...\FB_X.TcPOU@Method (Impl):N`          | that method's impl         |
| PROPERTY  | `...\FB_X.TcPOU@Prop.Get (Impl):N`        | that accessor's body       |
| GVL       | `...\GVL_X.TcGVL:N` (no section marker)   | declaration (= file line)  |
| DUT       | `...\T_X.TcDUT:N` (no section marker)     | declaration (= file line)  |
| project   | empty FileName, `Line 0`                  | unmappable (labeled raw)   |

In every observed case `real .st line = section start line + (N - 1)` —
exactly the data Task 5's provenance records. IMPORTANT probe gotcha: a
broken POU that nothing references builds CLEAN — TwinCAT only compiles
objects reachable from a task, so error-shape probes must reference the
broken objects from MAIN.

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
- [x] `StPouSource` (or an accompanying type) exposes source file + starting
      line for its declaration and (if present) implementation sections
      (SourceFileName / DeclarationStartLine / ImplementationStartLine /
      SourceRelativePath — computed by StFileParser for every kind)
- [x] All Task 1 tests still pass unmodified
- [x] New tests confirm correct provenance for: a multi-method FB file
      (each method's line matches where its `METHOD` keyword actually is,
      including one preceded by an attribute pragma), and a standalone
      `<Owner>.<Method>.st` file

**Verification:**
- [x] `dotnet test` passes, including new provenance-specific tests
      (57/57, 2026-07-14 — see StPouProvenanceTests.cs)

**Status: DONE (2026-07-14).**

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
- [x] For each kind exercised in Task 4, the tool prints the real `.st`
      relative path and a line landing on/near the actual broken line —
      not an internal export path with `Line: 1`
- [x] Unmapped errors still print (raw + labeled), never silently dropped

**Verification:**
- [x] Manual: deliberately break one method in a multi-method FB file, run
      the tool, confirm the printed error names that file and a plausible
      line (2026-07-14, scratch project: every kind mapped to the EXACT
      broken line — FB_BrokenBody.st:6, FB_BrokenMethod.st:16 [a broken
      METHOD in a multi-method FB], FB_BrokenProp.st:9 [GET accessor],
      GVL_Broken.st:3; the project-level summary error printed raw with
      "[unmapped — raw TwinCAT location]")
- [x] Unit tests on the pure translation function: given a fake provenance
      map + a fake `BuildError` list, assert the remapped output (including
      the unmapped fallback case) — ErrorLocationResolverTests.cs, 9 tests

**Status: DONE (2026-07-14).** `Sync/ErrorLocationResolver.cs` (pure,
COM-free) + `Program.RunSync` printing. The provenance index is built from
a fresh full parse at print time (independent of the sync's own possibly
incremental/partial parse), so errors in unchanged files still map; a
parse failure just falls back to raw locations.

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
- [x] One paragraph decision recorded (in this file or `tasks/plan.md`)
      covering: which option, at what scope (methods/properties only? full
      top-level orphans too? both incremental and full sync, or just one?)

**DECISION (2026-07-14): Option (c) — warn always, prune opt-in.** Scope:
warn on EVERY run (full and incremental) for any disappeared name at both
levels — top-level POU/DUT/GVL objects AND METHOD/PROPERTY members —
tracked via a recorded known-names state file next to `.st-sync-state`.
Pruning stays exactly where it already is (whole top-level objects under
`--incremental --confirm-delete`, exact-match only) — NOT extended in this
pass. Rationale: this matches the user's consistently-demonstrated
preference for warn-by-default/delete-by-explicit-flag, chosen explicitly
twice in this repo (`--confirm-delete` for .st deletions, and
`--confirm-delete-io` after IO orphan deletion nearly destroyed real
hardware config on 2026-07-14 — see the near-miss writeup above). Deleting
PLC objects has real blast radius on a shared project; a warning that names
the stale object achieves the plan's actual goal (never SILENT staleness)
at zero risk.

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
- [x] Renaming a METHOD inside an FB `.st` file and re-running the tool
      produces the behavior Task 7 decided (a clear warning naming the
      stale method, and/or its removal) — never silent staleness
- [x] Same for a deleted top-level POU/DUT/GVL outside `--incremental`
      (if Task 7's decision covers full sync)

**Verification:**
- [x] Manual (2026-07-14/15, throwaway scratch project + real VS runs):
      renamed METHOD HealthyFirst→HealthyRenamed → `[drift] ... ! stale
      FB_BrokenMethod.HealthyFirst`, state file updated (old name dropped,
      new recorded). Deleted top-level FB_BrokenProp.st on a FULL sync →
      `! stale FB_BrokenProp` + `! stale FB_BrokenProp.Setpoint`, both
      dropped from state. No deletions performed in either case.
- [x] Unit tests on the pure name-diffing logic (given "before" and "after"
      name sets, assert the correct warn/prune list) —
      KnownNamesTrackerTests.cs, 6 tests

**Status: DONE (2026-07-14/15).** `Sync/KnownNamesTracker.cs` (pure logic:
CollectNames/DiffFull/DiffWithinOwners/Merge + Read/Write of
`.st-known-names` next to `.st-sync-state`) wired into `Program.RunSync`:
warns on every run; full sync diffs everything, incremental diffs only
members of re-parsed owners (top-level deletions on incremental remain
git's job via IncrementalDeleter). Recorded state: full sync records the
parse verbatim; incremental folds the partial parse into the previous
record minus proven-stale names. No pruning added anywhere — per Task 7's
decision, deletion stays exactly where it already was.

**Dependencies:** Task 1 (test harness); Task 7 (the spec to build)

**Files likely touched:**
- `Sync/PouSyncEngine.cs`
- `Sync/SyncState.cs`
- `RunOptions.cs`
- `Program.cs`

**Estimated scope:** Medium–Large (split further once Task 7's scope is known — if it covers both methods/properties AND full-sync top-level orphans, split into two tasks: one per scope)

---

## Checkpoint: Complete
- [x] All four issues (2 from TODO.md + 2 from the code review) verified
      against a real TwinCAT/Visual Studio run, not just unit tests
      (duplicate libs + C0077: real Shark project BUILD PASSED 2026-07-14;
      error mapping + drift warnings: scratch-project VS runs 2026-07-14/15)
- [x] `TODO.md`/`TODO.xml` removed
- [x] Renaming/deleting `.st` content no longer leaves invisible stale code
      compiling silently
- [x] A build error reliably points back at the `.st` file/line that
      actually caused it
