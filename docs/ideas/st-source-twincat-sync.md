# ST-as-Source TwinCAT Sync Engine

## Problem Statement
How might we let an engineer maintain a TwinCAT PLC project as plain .st
text files in Git, and have those files automatically sync — create,
update, compile, and (eventually) IO-map/activate — into a real TwinCAT
XAE project via the Automation Interface, without ever hand-editing the
project inside the IDE?

## Recommended Direction
Build a diff-aware "Incremental ST Compiler" engine (Cluster B): maintain a
manifest mapping .st file path -> TwinCAT tree path, and on each run only
create/update/delete the POUs that changed, pushing text directly into
each POU's DeclarationText/ImplementationText rather than rebuilding the
whole project. Wrap it (Cluster C) with two interchangeable entry points
that share the same engine: a file-watcher "hot reload" loop for solo
work today, and a non-interactive CLI (`stsync build|ci`) for the
team/CI phase later — so growth doesn't require a rewrite, only a new
front-end. Add a drift-guard step so the .st files never silently
diverge from what's actually inside TwinCAT.

## Key Assumptions to Validate
- [x] Spike: can ITcPlcDeclaration/ITcPlcImplementation accept raw ST text
      for a freshly created POU, and does a subsequent build compile it
      correctly? **VALIDATED 2026-07-04** — see beckhoffAutomationInterface/Program.cs.
      Injected ST/Shark/MAIN.st verbatim into the MAIN POU's DeclarationText/
      ImplementationText (no XML wrapping needed), saved, built via
      dte.Solution.SolutionBuild.Build(), and it compiled with 0 errors.
      MAIN.TcPOU on disk matches MAIN.st content exactly.
- [x] Spike: can compiler errors be read back from the DTE (ErrorList /
      BuildEvents) reliably enough to produce a clean pass/fail signal?
      **VALIDATED 2026-07-04** — dte.ToolWindows.ErrorList.ErrorItems.Count
      gives a reliable pass/fail signal (0 = clean build); each ErrorItem
      exposes ErrorLevel/Description/FileName/Line for failure reporting.
- [x] Spike: can brand-new POUs be created (not just existing ones
      updated)? **VALIDATED 2026-07-04** — reflection on the local
      Interop.TCatSysManager.dll revealed the actual `TREEITEMTYPES` enum
      (namespace `Interop.TCatSysManager`), including
      `TREEITEMTYPE_PLCPOUPROG = 602` (PROGRAM), `_PLCPOUFB = 604`
      (FUNCTION_BLOCK), `_PLCPOUFUNC = 603` (FUNCTION), `_PLCFOLDER = 601`,
      `_PLCGVL = 615` (matches the soup01 tutorial's GVL type id exactly —
      cross-validated), `_PLCDUTSTRUCT = 606`, `_PLCLIBMAN = 617`.
      `pousFolder.CreateChild(name, 602, "", null)` creates a new PROGRAM
      POU (the 4th arg, vInfo, must be `null` — an empty string throws
      "Must specify valid information for parsing in the string").
      Ran a multi-file sync (existing MAIN.st updated, new Diagnostics.st
      created) in one pass, both compiled cleanly.
- [x] Spike: does ITcSmTreeItem/parent expose a delete method for removing
      a POU whose .st file was deleted from the source folder (completes
      the create/update/**delete** engine)? **VALIDATED 2026-07-04** —
      `ITcSmTreeItem.DeleteChild(bstrName)` on the parent (e.g. the POUs
      folder) removes a child POU by name. Simulated Diagnostics.st being
      deleted from source: detected it as orphaned (exists in project,
      no matching .st file), called `pousFolder.DeleteChild("Diagnostics")`,
      rebuilt with 0 errors, and confirmed Diagnostics.TcPOU was removed
      from disk. The full create/update/delete engine is now validated
      end-to-end with a real TwinCAT/Visual Studio instance.
- [x] Spike: does the Automation Interface expose IO variable linking
      (not just device/box creation), and at what granularity?
      **VALIDATED (partially) 2026-07-05** — `ITcSysManager.LinkVariables(
      bstrV1, bstrV2, offs1, offs2, size)` / `UnlinkVariables(bstrV1,
      bstrV2)` exist and are callable on the TwinCAT 3 XAE `ITcSysManager`
      (confirmed via reflection on Interop.TCatSysManager.dll — inherited
      by all versions up to `ITcSysManager18`), matching Beckhoff's
      TwinCAT 2-era "How to link variables" documentation. Granularity is
      byte-level: `offs1`/`offs2` are byte offsets into each variable and
      `size` is the number of bytes to link, for cases where the two
      variables differ in size.
      **However**, calling it against real Shark project paths in the
      same `TIPC^...^GVLs^GVL_Shark^varName` tree-path convention used
      everywhere else in this engine failed both times with `COMException:
      Item '...' not found` — for a mismatched-type PLC<->PLC link *and*
      for a link to a nonexistent IO path. This means `LinkVariables`
      does **not** use the same declaration-tree addressing as
      `LookupTreeItem`/`CreateChild`; Beckhoff's own sample path
      (`"TIPC^Project1^Standard^Outputs^MyOutput"`) suggests it expects a
      variable that's already surfaced under a task's mapped I/O list
      (e.g. an instance/symbol path), not a raw GVL declaration path.
      **Conclusion**: the AI does expose variable linking, but wiring it
      into this engine would need further research into TwinCAT 3's
      actual instance/symbol path format for linkable variables (out of
      scope for this MVP) — recorded here so the next attempt doesn't
      have to rediscover this from scratch. Test code was added to
      `Program.cs`, run against the live project, and then removed (it
      was a one-off spike, not a shipped feature).
      **Follow-up (2026-07-05, same day)**: confirmed the root cause with
      a targeted `LookupTreeItem` test on real GVL variables
      (`bMotorRunSensor`/`bMotorEnableOutput`, added to `GVL_Shark` as
      `AT %I*`/`AT %Q*` placeholders). `LookupTreeItem("TIPC^Shark^Shark
      Project^GVLs^GVL_Shark")` **succeeds** (the GVL itself is a real
      tree item), but appending the variable name with either `^` or `.`
      (`...^GVL_Shark^bMotorRunSensor`, `...^GVL_Shark.bMotorRunSensor`)
      **fails** with "Subitem ... not found". This proves individual PLC
      variables (whether in a GVL or a POU) are **not** separate tree
      items in the Automation Interface's tree model at all — they only
      exist as text inside the GVL's/POU's `DeclarationText`. Only true
      hardware-side items (Device/Box/Terminal/Channel) and the
      GVL/POU/DUT containers themselves are tree items. This means
      `LinkVariables`'s variable-path arguments must use a fundamentally
      different addressing scheme (most likely a pure ADS symbol path,
      e.g. `GVL_Shark.bMotorRunSensor` without any `TIPC^...` tree
      prefix, resolvable only once the configuration is built/activated
      on a real or simulated running target) rather than anything
      `LookupTreeItem`/`CreateChild` can resolve. Confirming the exact
      format would require either a running target (TwinCAT allows
      "Activate Configuration" even without real I/O, using a local
      simulated runtime) or TwinCAT-3-specific documentation (the
      InfoSys pages found are all TwinCAT 2-era) — both out of scope for
      this pass. **This engine can declare `AT %I*`/`AT %Q*` I/O
      variables and use them in logic (compiles cleanly, validated
      end-to-end), but cannot yet automate the actual hardware linking
      step** — that still requires a human to use the IDE's I/O mapping
      view (or `ConsumeXml`/`LinkVariables` with the correct symbol path,
      once found) after this tool runs.
- [x] Verify: does the tool reuse/reopen a persistent TwinCAT project
      across repeated runs instead of recreating it? **VALIDATED
      2026-07-04** — ran the assembled engine twice in a row: run 1
      bootstrapped a fresh "Shark" project (no solution found), run 2
      detected the existing Shark.sln and reopened it via
      `dte.Solution.Open()` instead of recreating anything, then synced
      and built successfully both times. (Note: this validates project
      persistence across separate process runs, not yet a single
      long-lived DTE session across N in-process builds — that's a
      distinct, still-open question for the eventual watch-mode/Cluster C
      wrapper, and out of scope for the current one-shot CLI usage.)

## MVP Engine — Assembled 2026-07-04
The validated spikes have been consolidated into a reusable engine, live in
beckhoffAutomationInterface/:
- `Sync/StPouSource.cs`, `Sync/StFileParser.cs` — .st → declaration/implementation
- `Sync/PouSyncEngine.cs` — create/update/delete reconciliation against TwinCAT
- `Sync/BuildRunner.cs`, `Sync/SyncReport.cs` — build + result reporting
- `Program.cs` — thin orchestrator: opens the persistent "Shark" project if it
  exists, bootstraps it if not, then runs the sync + build + report pipeline
  against ST/Shark/*.st

Ran twice in a row against a real TwinCAT/Visual Studio instance: first run
bootstraps, second run reopens the same project and re-syncs — both passed.

## Extended 2026-07-04 (later same day): ENUM, STRUCT, FUNCTION, FUNCTION_BLOCK + METHODs
Added support for the remaining core IEC 61131-3 object kinds, all validated
against the real Shark project:
- **ENUM/STRUCT DUTs** (with `{attribute ...}` pragmas) — `TREEITEMTYPE_PLCDUTENUM`
  (605) / `TREEITEMTYPE_PLCDUTSTRUCT` (606), synced into the project's `DUTs`
  folder. DUTs have no Implementation section — only `DeclarationText` is set.
- **FUNCTION** — `TREEITEMTYPE_PLCPOUFUNC` (603), synced like a PROGRAM.
- **FUNCTION_BLOCK with inline METHODs** — `TREEITEMTYPE_PLCPOUFB` (604) for the
  FB itself, `TREEITEMTYPE_PLCMETHOD` (609) for each method, created as a child
  of the FB's own tree item (not the POUs folder). Convention: a FUNCTION_BLOCK
  and all of its METHODs live in **one file** (e.g. `FB_Motor.st` contains
  `FUNCTION_BLOCK FB_Motor ... METHOD Init ... METHOD Reset ...`);
  `StFileParser` splits this into separate synced objects internally by
  scanning for `METHOD <name>` line boundaries (absorbing any immediately
  preceding attribute-pragma lines into that method's section). A standalone
  `<Owner>.<Method>.st` file is also still supported.
- Fixed the naive parser to split on the **last** `END_VAR` instead of the
  first, since FUNCTION_BLOCK/METHOD/FUNCTION headers commonly have multiple
  VAR/VAR_INPUT/VAR_OUTPUT blocks.
- Fixed a latent `BuildRunner` bug: it previously treated every entry in
  `ErrorList.ErrorItems` (including warnings/messages) as a build failure.
  Now only `vsBuildErrorLevelHigh` (=3) counts toward `Success`; everything
  else is reported separately as a warning.
- `PouSyncEngine` now reconciles three tiers per run: top-level POUs
  (PROGRAM/FUNCTION_BLOCK/FUNCTION), top-level DUTs (ENUM/STRUCT), and
  METHODs (synced/pruned per-FB after the FB itself is created/updated),
  with orphan deletion at each tier.

Ran against the live Shark project: first pass created `FB_Motor`,
`F_ClampSpeed`, `E_MotorState`, `ST_MotorStatus`, `FB_Motor.Init`, and
`FB_Motor.Reset` in one sync (6 created, 1 updated for MAIN), build passed.
After consolidating the FB + its two methods into a single `FB_Motor.st`
file, a re-run correctly matched all three as updates (not create/delete)
and still built clean.

## Extended 2026-07-05: GVLs and library references
Added the last two pieces of the original "config-as-data for everything
else" idea, both validated against the live Shark project:
- **GVL** — detected via a file whose first keyword is `VAR_GLOBAL` (e.g.
  `GVL_Shark.st`); like DUTs, a GVL has no Implementation section, so only
  `DeclarationText` (the whole file, including any `{attribute ...}` pragma)
  is set. Synced into the project's `GVLs` folder via `TREEITEMTYPE_PLCGVL`
  (615) — same `PouSyncEngine.SyncTopLevel` machinery as POUs/DUTs, just a
  third tier and tree path.
- **Library references** — not IEC source, so they don't fit the `.st`
  convention; instead a small `libraries.xml` manifest (`<Library Name="..."
  Version="..." Company="..." />`) is parsed by `LibraryManifestParser` and
  reconciled by `LibrarySyncEngine` against `ITcPlcLibraryManager`
  (`sysManager.LookupTreeItem("TIPC^Shark^Shark Project^References")` cast to
  `ITcPlcLibraryManager`), using `AddLibrary`/`RemoveReference`.

Two real bugs were found and fixed only by actually running this against
TwinCAT (not guessable from docs alone):
- **`ITcPlcReferences` is 0-based** — unlike `ITcSmTreeItem.Child`/`ErrorItems`
  (both 1-based), enumerating it with a 1-based loop threw
  `ArgumentOutOfRangeException`. Fixed to `for (int i = 0; i < references.Count; i++)`.
- **Our own duplicate-reference pre-check (reading `.References`) can be
  stale/incomplete right after (re)opening a project.** A second run against
  an already-synced project threw `ArgumentException: Managed Library
  '...' already contained!` from `AddLibrary` itself, even though our
  pre-check said it wasn't present. Fixed by treating that specific
  exception as an idempotent no-op — `AddLibrary`'s internal check is more
  authoritative than our own collection read.

After both fixes, three consecutive runs against the live project confirmed:
create (GVL + library added), then two idempotent re-syncs (both reporting
0 created/added, correct "updated" counts, no spurious errors), all with a
clean build.

## Extended 2026-07-05 (later same day): INTERFACE, ALIAS type, EXTENDS, abstract methods, PUBLIC/PRIVATE
Added the remaining OOP-flavored IEC 61131-3 constructs, all validated
against the live Shark project (`I_Motor`, `T_MotorSpeed`, `FB_MotorBase`):
- **INTERFACE** (`TREEITEMTYPE_PLCITF` = 618) with inline `METHOD` signatures
  (no body) using `TREEITEMTYPE_PLCITFMETH` (610) — a *different* type id
  than FB methods (609). Same single-file convention as FUNCTION_BLOCK
  (`I_Motor.st` contains `INTERFACE I_Motor ... METHOD Init ... METHOD
  Reset ...`); `StFileParser` now shares one `FindMethodBoundaries`/
  `ParseMethodSegments` helper between FB and INTERFACE parsing.
- **ALIAS DUT** (`TYPE T_MotorSpeed : LREAL; END_TYPE`) — detected when a
  `TYPE` file contains neither `STRUCT` nor `(` (enum literal list).
- **EXTENDS** for FUNCTION_BLOCK and INTERFACE, and **IMPLEMENTS** for
  FUNCTION_BLOCK — `StPouSource.BaseType` now carries the EXTENDS/alias
  target, extracted via regex from the header line.
- **Abstract FUNCTION_BLOCK/METHOD** — just an `{attribute 'abstract'}`-style
  pragma (`{abstract}`) before the keyword; no engine changes needed since
  attribute-pragma lines were already preserved as part of the declaration.
- **PUBLIC/PRIVATE method modifiers** (`METHOD PUBLIC Init : BOOL`) — fixed
  `MethodHeaderRegex` to skip an optional access-modifier keyword before
  capturing the method name (previously "PUBLIC"/"PRIVATE" would have been
  misparsed as the method name itself).

Three more real bugs found only by running against TwinCAT, each with a
different, non-obvious `CreateChild` `vInfo` requirement:
- **INTERFACE requires a non-null base-class string even with no EXTENDS**
  — `""` means "no base interface"; passing `null` throws "Base class not
  specified!".
- **ALIAS requires the aliased base type name as `vInfo`** (e.g. `"LREAL"`)
  — omitting it throws the same "Base class not specified!" error.
  (`{attribute}`-style pragmas on aliases weren't the issue; the missing
  base type was.)
- **FUNCTION_BLOCK is the opposite of INTERFACE**: it requires `null` when
  there's no EXTENDS (passing `""` throws "Must specify valid information
  for parsing in the string") and the actual base FB name only when EXTENDS
  is present. So the same `vInfo` rule cannot be shared across kinds —
  `PouSyncEngine` now branches per-kind explicitly.

Also found and fixed two reliability issues unrelated to any single kind:
- **`Program.cs` never called `dte.Quit()`**, even on success — every run
  (crashed or not) leaked a `devenv.exe` process. Five had accumulated
  across this session's testing, and once enough piled up, COM calls
  started failing with `RPC_E_SERVERCALL_RETRYLATER` ("the application is
  busy") purely from resource contention. Fixed with a `try/finally`
  around the whole sync so VS always closes.
- **`RPC_E_SERVERCALL_RETRYLATER` can also happen transiently right after
  a large sync** (16 objects in one pass), while VS's background
  compiler/IntelliSense is still catching up, causing the very next COM
  call (`AddLibrary`) to fail. Generalized the existing `WaitForVsToLoad`
  retry pattern into a `RetryOnBusy(action, description)` helper and
  wrapped the library-sync and build steps with it.

After all fixes, a clean run synced all 16 objects (FB with EXTENDS +
IMPLEMENTS + abstract override + public/private methods, an interface with
methods, an alias, two DUTs, a GVL, a program, and a function) with 0
errors, and a second consecutive run reported 0 created / 16 updated / 0
deleted with no transient failures and a clean VS shutdown (no leaked
processes).

## MVP Scope
One job, done well: sync + compile + report for POUs only, using Shark's
current MAIN.st as the test case.
- In: file->POU create/update/delete engine, direct text injection,
  build trigger, console pass/fail + error output, drift check.
- Out (for now): IO mapping/activation, watch mode, CLI/CI packaging,
  multi-target/team features. (GVLs and library references, originally
  listed here as deferred, are now implemented — see above.)

## Not Doing (and Why)
- Full-project rebuild on every run — too slow/destructive once I/O
  config and hardware scans exist alongside .st-managed POUs.
- Auto-export TwinCAT -> .st as the primary flow — inverts the goal;
  .st must remain the human-edited source of truth.
- Team/CI tooling before the core engine is proven — validate the
  riskiest assumption (text injection + compile feedback) solo first.

## Open Questions
- ~~Does Beckhoff's InfoSys confirm ITcPlcDeclaration/ITcPlcImplementation
  as the intended API for this?~~ **Answered**: yes — InfoSys documents
  both interfaces directly (`DeclarationText`/`ImplementationText`,
  Get/Set, TwinCAT 3.1+), and this was independently confirmed by
  successfully round-tripping real `.st` content through them.
- ~~What's the actual object model for IO variable linking in the AI?~~
  **Answered (partially)**: `ITcSysManager.LinkVariables`/`UnlinkVariables`
  exist and work at byte-level granularity, but require a different
  variable-path format than the declaration-tree paths used everywhere
  else in this engine (see the IO-linking spike above) — the exact
  instance/symbol path format still needs further research before this
  could be wired into the sync engine.
- What's the exact TwinCAT 3 instance/symbol path format `LinkVariables`
  expects for a PLC-side variable (vs. the `TIPC^...^GVLs^...` declaration
  path that works for `LookupTreeItem`/`CreateChild`)?
- Does adding a real (or virtual/simulated) EtherCAT IO device/terminal to
  the Shark project's I/O tree make `LinkVariables` succeed? **Analysis
  (2026-07-05, not yet tested)**: likely necessary but **not sufficient by
  itself**. The spike above proved the *PLC-side* argument already fails
  `LookupTreeItem` at the variable level (only the containing GVL/POU
  resolves) — that's independent of whether a valid IO-side target exists.
  Adding an EL terminal would give a legitimate, resolvable path for the
  *IO-side* argument (e.g. `TIPC^Shark^I/O^Devices^Device 1 (EtherCAT)^
  Term 1 (EK1100)^Channel 1^Input`), but the PLC-side argument would still
  need to be whatever non-tree-path format `LinkVariables` actually
  expects for a GVL variable (hypothesized above to be a plain ADS symbol
  path, e.g. `GVL_Shark.bMotorRunSensor`, not a `TIPC^...` path).
  **Next real test**: add a terminal to the I/O tree (TwinCAT lets you
  insert EtherCAT terminals manually without scanning real hardware, e.g.
  right-click I/O > Devices > Add New Item > EtherCAT), build, then try
  `LinkVariables` with the PLC side as a bare ADS-style symbol string (no
  `TIPC^` prefix) against the new terminal's real tree path. Also worth
  inspecting the generated `.tsproj` XML after manually linking a variable
  via the IDE itself — that's the most reliable way to reverse-engineer
  the exact linked-variable path syntax TwinCAT uses internally, without
  more blind guessing.
- How will conflicting edits be handled once this becomes a team/GitHub
  workflow (two people editing the same POU in parallel)?

## Other Automation Interface Capabilities (Beckhoff InfoSys survey, 2026-07-05)
While researching the IO-linking question, the InfoSys "How to..." index
for the (TwinCAT 2-era, but largely still applicable) Automation Interface
was surveyed for capabilities beyond what this engine already uses. None
of these have been implemented or spike-tested yet — recorded here as a
backlog for future work:
- **Enable/disable a tree item**, change a PLC project's path, force a
  rescan.
- **Change a fieldbus device's address**, exchange one fieldbus device for
  another of the same type.
- **Export/import child info as XML** (`ProduceXml`/`ConsumeXml`) — could
  snapshot or restore a whole subtree (e.g. an I/O configuration) instead
  of rebuilding it item-by-item.
- **Link an NC axis/encoder/drive to IO** — same underlying idea as
  `LinkVariables` but for motion objects; likely has the same PLC-side
  addressing question.
- **Scan devices and boxes** — requires `ITcSysManager3.SetTargetNetId`
  pointed at a real or TwinCAT-simulated running target; Shark currently
  has no target configured, so this is untested.
- **Add a route to a remote ADS target**, **broadcast search** for
  reachable targets on the network.

Of these, XML export/import is the most promising near-term addition
since it doesn't require a live target. Device scanning, NC linking, and
resolving the `LinkVariables` PLC-side path all require either real or
simulated hardware, or further reverse-engineering of TwinCAT 3's internal
addressing — out of scope until a target is available to test against.
