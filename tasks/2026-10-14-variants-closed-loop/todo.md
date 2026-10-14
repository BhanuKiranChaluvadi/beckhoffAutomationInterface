# XAE-only support, variants, variant I/O, closed loop — Task Breakdown

Sibling of [`plan.md`](plan.md). Each task is S or M and leaves the tree building.

**Standard verification** (every task):
- Build: `cd beckhoffAutomationInterface; dotnet build -c Debug -f net48`
- Tests: `dotnet test beckhoffAutomationInterface.Tests` (169 tests pass at the start)

**COM rule:** test COM paths only on a **scratch** source folder and a **scratch** TwinCAT
project. Never on `ST/Shark`, its project, or a live machine. Activation only on `UmRT_Default`
(`199.4.42.250.1.1`) unless the user says otherwise.

**Reference prototype:** `BeckhoffLib/Experiments/VariantDemo/tool/VariantDemoTool.cs` has
working code for shell start, variants, implicit defines, activation and ADS reads.

---

## Phase 0: Baseline

## Task 0: Clean baseline
**Description:** Work on branch `feature/variants-closed-loop`. The user's untracked `TODO.md`
and `images/*.jpg` stay untracked (their own notes); every commit stages explicit paths only.
Archive the finished reverse-export plan.

**Acceptance criteria:**
- [x] Branch `feature/variants-closed-loop` created from `main`
- [x] Only the user's own untracked notes remain untracked

**Verification:** `git status`, `git log -1`
**Dependencies:** None
**Files touched:** commit only
**Scope:** XS

---

## Phase 1: Works on XAE-only PCs

## Task 1: DTE shell resolver
**Description:** Replace the hard-coded `VisualStudio.DTE.17.0` in `VisualStudioSession.Start()`
with a resolver. Order: `--shell` / `.stconfig shell` if given, else `TcXaeShell.DTE.17.0`, else
`VisualStudio.DTE.17.0`. For each candidate, read the ProgID's CLSID and check that the
`LocalServer32` exe exists. Put the registry reads behind a small interface, so the selection
logic is unit-testable.

**Acceptance criteria:**
- [x] With no option, a PC that has only XAE starts `TcXaeShell` (no `Class not registered` crash) — unit-tested; bench PC check at Checkpoint 1
- [x] `--shell vs2022` on a PC without VS 2022 fails with one clear message that names the missing exe
- [x] The chosen shell and its exe path are printed once at start

**Verification:**
- [x] Unit tests: selection order, explicit override, missing-exe case, nothing-registered case
- [x] Manual: `build` on `ST/sample` scratch project with `--shell xae` and `--shell vs2022` on this PC (2026-10-14: both BUILD PASSED; XAE 10 s, VS 2022 3 min 22 s)

**Dependencies:** Task 0
**Files likely touched:** `VisualStudioSession.cs`, new `DteShellResolver.cs`, `RunOptions.cs` (`--shell`, `.stconfig` key), new `DteShellResolverTests.cs`
**Scope:** M

## Task 2: Process detection for both shells
**Description:** `VisualStudioSession` finds its own process by the name `devenv` only (lines 57–60).
Use the process name of the resolved shell (`devenv` or `TcXaeShell`), so the force-kill on
exit and `DialogAutoDismisser` work with XAE.

**Acceptance criteria:**
- [x] With `TcXaeShell`, the PID is found by the process diff (not only by the window-handle fallback)
- [x] After a run, no `TcXaeShell.exe` that this tool started stays alive

**Verification:**
- [x] Unit test: the process name comes from the resolved shell (`DteShellResolverTests`)
- [x] Manual: run `build` twice with `--shell xae`; check Task Manager has no leftover process
      (2026-10-14: found by process list; TcXaeShell needed >3 s to exit, so the grace period is now 10 s — clean exit, no force-kill)

**Dependencies:** Task 1
**Files likely touched:** `VisualStudioSession.cs`, `DteShellResolver.cs`
**Scope:** S

## Task 2a: TwinCAT silent mode (no Yes/No prompts)
**Description:** User request: the tool must never wait for a click. Set
`ITcAutomationSettings.SilentMode = true` (via `dte.GetObject("TcAutomationSettings")`) right after
the shell loads, so TwinCAT answers its own questions with the default. Silent mode is a
persistent XAE setting: read the old value first and restore it on exit, so the user's
interactive XAE still asks. Keep `DialogAutoDismisser` as a safety net. Known prompts
(`images/`): "Target System Process Image Update" (Yes/No, EL1918 AMS Net ID mismatch) and
"…\PLC_NFL_prj.tmc not found" (OK).

**Acceptance criteria:**
- [x] Silent mode is on during every session and the previous value is restored on exit (also after a failure — restore runs first in `Dispose`)
- [x] The log says once: "TwinCAT silent mode on (was: X)"
- [ ] CI run on the bench PC (project with EL1918, no `.tmc`) finishes with no dialog shown

**Verification:**
- [x] Manual: read `SilentMode` before and after a run on this PC — unchanged (2026-10-14: two runs, both "was: off" → "restored to off")
- [ ] Manual (Checkpoint 1): bench PC run with no click

**Dependencies:** Task 1
**Files likely touched:** `VisualStudioSession.cs`
**Scope:** S

## Task 3: Clean before build
**Description:** Add `--clean` to `build`. With it, call `SolutionBuild.Clean(true)` before
the build. Later, Task 7 always cleans when it switches variants.

**Acceptance criteria:**
- [x] `build --clean` runs Clean and then Build. The log shows both, in that order.
- [x] Without `--clean`, behaviour is unchanged

**Verification:**
- [x] Unit test: option parsing (`CleanOptionTests`, incl. `.stconfig clean=true`)
- [x] Manual: scratch project — add an error, build (fails), fix it, `build --clean` (passes) (2026-10-14)

**Dependencies:** Task 0
**Files likely touched:** `Sync/BuildRunner.cs`, `SyncPipeline.cs`, `RunOptions.cs`, tests
**Scope:** S

## Task 4: Output-pane fallback for empty error texts
**Description:** If a build fails and every `ErrorItem.Description` is empty, read the *Build*
output pane and print its error lines (`file(line) : error: text`). Map them through
`ErrorLocationResolver` like normal errors.

**Acceptance criteria:**
- [x] A failed build never prints "BUILD FAILED — N error(s)" with empty texts (fallback when the error list has none)
- [x] Lines from the output pane are parsed into file, line and text

**Verification:**
- [x] Unit test: parse sample output-pane lines (`BuildOutputParserTests`, real 4026 output)
- [x] Manual: hide the XAE window in a scratch run; the errors still print with text
      (2026-10-14: scratch harness called `BuildRunner.Build` with `MainWindow.Visible = false` on a build
      with an injected error. Result: 2 errors with full text from the output pane, e.g.
      `C0046: Identifier 'nUndeclaredVar' not defined (…\MAIN.TcPOU:11)`. The pane IS readable while hidden.)

**Dependencies:** Task 0
**Files likely touched:** `Sync/BuildRunner.cs`, new `Sync/BuildOutputParser.cs`, tests
**Scope:** S

## Task 4b: `build <path>` maps errors to `.st` files of the current directory
**Description:** Found during Task 3 (2026-10-14). `build <tsproj>` leaves the source folder at
its default `.`, so `ErrorLocationResolver` parses the **current directory** and maps a PLC error
to an unrelated file (a `MAIN.TcPOU` error was shown as `Experiments/VariantDemo/src/MAIN.st:9`
because the tool ran from another repo). For `build`, map errors only when a source folder is
given explicitly (`.stconfig source=` or a future `--source`); otherwise print the raw TwinCAT
location.

**Acceptance criteria:**
- [x] `build <tsproj>` from an unrelated directory prints the raw `.TcPOU` location, never a foreign `.st` path
- [x] With an explicit source folder (`--source` or `.stconfig source=`), mapping works as today; legacy `cd <source>; --build` still maps

**Verification:**
- [x] Unit test: "no explicit source" → no provenance index used (`ErrorMappingSourceTests`)
- [x] Manual: repeat the Task 3 error injection from another directory (2026-10-14: raw `MAIN.TcPOU (Impl):1`, exit 1)

**Dependencies:** Task 0
**Files likely touched:** `SyncPipeline.cs` (`TryBuildProvenanceIndex`), `RunOptions.cs` (know whether source was explicit), tests
**Scope:** S

## Task 4c: Failed build exits with `-1`, not the documented `1`
**Description:** Found during Task 3. A failed `build` returned exit code `-1` when run from the BeckhoffLib directory (errors mapped to `.st` files there), but `1` when run from a directory without `.st` files (raw locations). So the `-1` is probably raised on the error-mapping path — check it together with Task 4b. README and the CI
guide promise `1` ("CI contract: build failure = exit code 1"). Find where `-1` comes from and
make every failure path return `1`.

**Acceptance criteria:**
- [x] Build failure → exit code 1; build timeout → exit code 1; success → 0
      **Closed — not a bug (2026-10-14).** The `-1` came from the test command: PowerShell's
      `Select-Object -First N` stops the pipeline early and kills the native process. A full
      capture of the same failing build gives exit code 1, from both directories.

**Verification:**
- [ ] Manual: the Task 3 error injection, check `$LASTEXITCODE`
- [ ] Unit test if the exit-code decision can be isolated

**Dependencies:** Task 0
**Files likely touched:** `Program.cs`, `SyncPipeline.cs`
**Scope:** S

### Checkpoint 1: Fixes
- [x] All tests pass (202). Build is clean (0 warnings).
- [x] `build` passes on this PC with `--shell xae` and with `--shell vs2022` (2026-10-14, incl. `--clean`, silent mode on/restored, no shell left running)
- [ ] **Manual on the bench/CI PC (XAE only):** `build` passes. The `TODO.md` crash is gone.
- [ ] Review with the user before Phase 2

---

## Phase 2: Variant builds

## Task 5: `variants.xml` manifest and parser
**Description:** New optional manifest in the source folder:
```xml
<Variants DefineMode="Implicit">
  <Variant Name="Shark"/>  <Variant Name="UVM"/>  <Variant Name="SAS"/>  <Variant Name="Google"/>
  <Group Name="PCV_MODBUS">   <Member>Shark</Member> </Group>
  <Group Name="PCV_ETHERCAT"> <Member>UVM</Member><Member>SAS</Member><Member>Google</Member> </Group>
</Variants>
```
Parse it into a model. Validate: unique names, every group member is a declared variant, and
names are valid PLC identifiers (they become defines). Produce the `ProjectVariantConfig` XML string.

**Acceptance criteria:**
- [x] Valid file → model + correct `ProjectVariantConfig` XML (byte-identical to the prototype's string)
- [x] Invalid file → one error per problem, with line numbers
- [x] No `variants.xml` → no variant behaviour (current projects unchanged)

**Verification:**
- [x] Unit tests: valid, duplicate variant, unknown member, bad identifier, missing file (`VariantManifestTests`, 15 tests)

**Dependencies:** Task 0
**Files touched:** new `Sync/VariantManifest.cs` (model + parser + ProjectVariantConfig XML), new `VariantManifestTests.cs`
**Scope:** S

## Task 6: `sync variants` stage
**Description:** New stage that applies `variants.xml` to the project:
`ITcSysManager14.ProjectVariantConfig`, then on the PLC item `ITcSmTreeItem9.PvDefine = true`
and `ConsumeXml(<EnableImplicitDefines>true</EnableImplicitDefines>)`. Read before you write,
so a second run changes nothing. Add it to `sync all`.

**Acceptance criteria:**
- [x] After `sync variants`, the `.tsproj` shows `<ProjectVariant PvUseDefine="true">` and the variants (2026-10-14, scratch `init` of `ST/VariantSample`)
- [x] A second run reports "no change" (2026-10-14: "Variants already up to date (no change).")
- [x] A variant removed from the manifest is removed from the project (report it, like other stages) — unit-tested (`VariantSyncTests`); real run at Checkpoint 2

**Verification:**
- [x] Unit test: diff logic (current config vs manifest → actions)
- [x] Manual: scratch project; run twice; read the `.tsproj` and check `<ProjectVariant PvUseDefine="true">`

**Dependencies:** Task 5
**Files likely touched:** new `Sync/VariantSyncEngine.cs`, `SyncPipeline.cs`, `RunOptions.cs` (stage), tests
**Scope:** M

## Task 7: `build --variant` and `--all-variants`
**Description:** `build <project> --variant <v>` sets `CurrentProjectVariant`, cleans, builds and
reports. Each build prints the PLC's message pragmas (`[PLC MESSAGE]`, C0373) as proof of the compiled branch. `--all-variants` reads the variant list from the project and does this for each one.
At the end it prints one summary table (variant → pass/fail, error count) and exits non-zero if
any variant fails. If the project has variants but `PvDefine` is off, fail with a clear message
("run `sync variants`"), because the PLC would get no defines.

**Acceptance criteria:**
- [x] `--variant X` builds only X (case-insensitive). An unknown X fails with the list of valid names.
- [x] `--all-variants` builds every variant, even after one fails, and prints the summary. Also the default when the project has variants.
- [x] The exit code is 0 only if all requested variants pass

**Verification:**
- [x] Unit tests: summary/exit-code logic, unknown-variant message (`VariantBuildPlanTests`)
- [x] Manual on a scratch project from `ST/VariantSample` (2026-10-14), each response as expected:
      1. `build` → both variants, each prints its own `[PLC MESSAGE]` branch lines, ALL 2 PASSED, exit 0
      2. `build --variant machineb` → only MachineB, exit 0
      3. `build --variant Google` → "unknown variant 'Google'. The project has: MachineA, MachineB.", exit 1
      4. error injected only in MachineB's branch → MachineA PASSED, MachineB FAILED (2 errors), exit 1

**Dependencies:** Tasks 3, 6
**Files likely touched:** `Sync/BuildRunner.cs`, `SyncPipeline.cs`, `RunOptions.cs`, new `Sync/VariantBuildReport.cs`, tests
**Scope:** M

## Task 7b: `init` rejects an invalid project name before it starts XAE
**Description:** Found in Task 6 (2026-10-14). `init ST/variant-sample` derives the project name
`variant-sample`; TwinCAT refuses it and the tool crashes with an unhandled exception
("Cannot create new PLC project 'variant-sample'!", exit -532462766) after a 25 s XAE start.
Validate the name (PLC identifier) in `init`/`sync` before the shell starts, and suggest `--name`.

**Acceptance criteria:**
- [x] `init <folder-with-hyphen>` fails in under a second with a clear message and exit 1, no XAE started (2026-10-14: 0.2 s)
- [x] `--name ValidName` works around it (init + 2 variants built)

**Verification:**
- [x] Unit test: name validation (`PlcProjectNameTests`)
- [x] Manual: repeat the Task 6 failure case (`check parse` on that folder still works)

**Dependencies:** Task 0
**Files likely touched:** `Program.cs` or `RunOptions.cs`, tests
**Scope:** S

### Checkpoint 2: Variant builds
- [x] Add `ST/VariantSample` (port of `Experiments/VariantDemo/src` + `variants.xml`; a hyphen is not allowed in a PLC project name)
- [x] `init ST/VariantSample` (full sync) then `build --all-variants`: both pass. The build log shows
      `VARIANT CHECK` and `FEATURE CHECK` messages for the correct branch.
- [x] Remove the variants from the scratch project's manifest → `sync variants` removes them
      (2026-10-14: MachineB removed → "- variant MachineB", build covers MachineA only, `--variant MachineB` refused)
- [ ] Review with the user before Phase 3

---

## Phase 3: Variant I/O trees

## Task 8: Spike — per-variant I/O items and links
**Description:** Findings only, no merged code. On a scratch project with one EtherCAT master
and two terminals:
1. Set `PvDisable = true` on terminal T2. Disable it in variant B only (`CurrentProjectVariant = B`, `Disabled = SMDS_DISABLED`).
2. Link a PLC `AT %I*` variable to a T2 channel. Build A and B. Activate B on UmRT (masters disabled for UmRT).
3. Test `PvLink`: can a link exist in A only?
4. Record what the `.tsproj` / `.xti` store for each case, and what `ProduceXml` shows.

**Acceptance criteria:**
- [ ] A findings note answers: does a link to a disabled terminal break build or activation? Does `PvLink` work, and how?
- [ ] Decision recorded: D6 as written, or the fallback (variant-neutral links)

**Verification:** Manual, on a scratch project. Findings note added to `docs/ideas/variant-io-spike.md`.
**Dependencies:** Task 6
**Files likely touched:** new `docs/ideas/variant-io-spike.md`
**Scope:** S

## Task 9: `Variants` attribute in `io-devices.xml`
**Description:** Extend `IoManifestParser` (devices, boxes, terminals, links) with an optional
`Variants="A,B"` attribute. Validate the names against `variants.xml`. A child cannot be present
in a variant where its parent is absent.

**Acceptance criteria:**
- [ ] The attribute is parsed into the spec model. No attribute = all variants.
- [ ] An unknown variant name, or a child wider than its parent, gives a clear error with the item path

**Verification:**
- [ ] Unit tests: absent, single, multiple, unknown name, child-wider-than-parent
- [ ] Existing `IoManifestParserTests` still pass (no attribute = old behaviour)

**Dependencies:** Tasks 5, 8
**Files likely touched:** `Sync/IoManifestParser.cs`, spec model types, `IoManifestParserTests.cs`
**Scope:** S

## Task 10: `sync io` applies per-variant enable/disable
**Description:** For each item with a `Variants` attribute: set `PvDisable = true`, then for each
project variant set `Disabled` to enabled or disabled. Restore the original `CurrentProjectVariant`
at the end. Apply link rules from the Task 8 decision. Report the changes per variant.

**Acceptance criteria:**
- [ ] The scratch terminal is enabled in A and disabled in B after `sync io`
- [ ] A second run reports no change
- [ ] Items without the attribute are untouched (no `PvDisable` set)

**Verification:**
- [ ] Unit test: plan logic (manifest + current state → per-variant actions)
- [ ] Manual: scratch project; `build --all-variants` passes after `sync io`

**Dependencies:** Tasks 9, 6
**Files likely touched:** `Sync/IoSyncEngine.cs`, `Sync/VariableLinkEngine.cs` (if links), `SyncPipeline.cs`, tests
**Scope:** M

## Task 11: `export io` writes `Variants` back
**Description:** Reverse export: for each I/O item with `PvDisable = true`, read its state in
every variant and write the `Variants` attribute. This is needed for adopting an existing
multi-variant project.

**Acceptance criteria:**
- [ ] Export → sync → export of the scratch project gives the same `io-devices.xml`
- [ ] Items without `PvDisable` export without the attribute (no diff for current projects)

**Verification:**
- [ ] Unit test: state → attribute formatting
- [ ] Manual: the round trip above

**Dependencies:** Task 10
**Files likely touched:** `Sync/IoManifestWriter.cs`, `Sync/PlcObjectExporter.cs` (if shared), tests
**Scope:** M

### Checkpoint 3: Variant I/O
- [ ] Scratch project: a terminal that exists for variant A only is disabled in B
- [ ] `build --all-variants` passes. The export round trip has no diff.
- [ ] README section "Different I/O per machine" written (manifest attribute + D7 PLC rule)
- [ ] Review with the user before Phase 4

---

## Phase 4: Closed loop

## Task 12: ADS client and `expect.json`
**Description:** Port the prototype's `AdsClient` (P/Invoke to `Common64\TcAdsDll.dll`, because
this tool is x64): read state, read a symbol by name (handle → value → release), with typed reads
for BOOL, INT, DINT, UDINT, REAL, LREAL and STRING(n). Add an `expect.json` parser:
```json
{ "variants": { "SAS": [ { "symbol": "MAIN.sMachine", "type": "STRING(16)", "equals": "SAS" },
                         { "symbol": "PRG_PcvEtherCAT.nCycles", "type": "UDINT", "increases": true },
                         { "symbol": "PRG_PcvModbus.nCycles", "absentOrZero": true } ] } }
```

**Acceptance criteria:**
- [x] The parser supports `equals`, `increases`, `absentOrZero` and a `tolerance` for REAL/LREAL
- [x] ADS error 1808 (symbol not found) is reported as "absent", not as a crash

**Verification:**
- [x] Unit tests: parser, value decoding from byte arrays, comparisons (`ExpectationTests`, fake PLC incl. "wrong variant running")
- [x] Manual: read `MAIN.sMachine` from `UmRT_Default` while a variant runs — done through `verify` (Task 14): "MachineB"

**Dependencies:** Task 0
**Files likely touched:** new `Runtime/AdsClient.cs`, new `Runtime/ExpectationFile.cs`, tests
**Scope:** M

## Task 13: `activate` command
**Description:** `activate <project> --target umrt|local [--variant v] [--yes]`. Only these two targets (user decision). Resolve the AMS Net ID from the target's config (UmRT: `TcRegistry.xml` of `UmRT_Default`; local: registry `HKLM\SOFTWARE\WOW6432Node\Beckhoff\TwinCAT3\System\AmsNetId`). Steps: open the project,
set the variant, `SetTargetNetId`, set boot autostart, clean + build, then print the project
that is on the target now (ADS or `CurrentProjectInfo.json` for a local UmRT). Without `--yes`,
stop here. With `--yes`: `ActivateConfiguration`, `StartRestartTwinCAT`.

**Acceptance criteria:**
- [ ] Without `--yes`, nothing changes on the target, and the current boot project is printed
- [ ] With `--yes`, the target boot project becomes the selected variant
- [ ] A build failure stops before activation

**Verification:**
- [ ] Unit test: target name → AMS Net ID resolution (`umrt`, `local`, unknown name rejected, no default target)
- [ ] Manual: `UmRT_Default`. Back up its `Boot` folder first.

**Dependencies:** Tasks 7, 12
**Files likely touched:** new `Runtime/Activator.cs`, `Program.cs` (new command), tests
**Scope:** M

## Task 14: `verify` command
**Description:** `verify --target umrt|local --expect <file> --variant <v>`. Wait up to N seconds
for PLC Run. If TwinCAT stays in Config, print "runtime stays in Config — check the license
(`doctor`)". Then read and compare every expectation and print one OK/FAIL line each. Exit 0
only if all pass.

**Acceptance criteria:**
- [x] Same output as the prototype for `ST/VariantSample` (6 checks per variant)
      (2026-10-14 on UmRT running MachineB: `--variant MachineB` → 6/6 OK, VERIFY PASSED, exit 0;
      `--variant MachineA` → 6/6 FAIL (wrong variant running), exit 1)
- [x] The Config-mode case prints the license hint and exits non-zero (2026-10-14: `--target local`, TwinCAT: Config, exit 1)

**Verification:**
- [x] Unit tests: expectation selection, targets, argument parsing (`RuntimeCommandTests`); checks in `ExpectationTests`
- [ ] Manual: Checkpoint 4

**Dependencies:** Task 12
**Files likely touched:** new `Runtime/Verifier.cs`, `Program.cs`, tests
**Scope:** M

## Task 15: `doctor` command
**Description:** Print: registered DTE shells and their exe paths (Task 1 resolver), TwinCAT
version, local UmRT instances (`C:\ProgramData\Beckhoff\TwinCAT\3.1\Runtimes\*`) with running
state and AMS Net ID, and the trial license `ExpireTime` per runtime. Mark an expired or
soon-expired (< 2 days) license.

**Acceptance criteria:**
- [ ] On this PC it shows both shells, TwinCAT 3.1.4026.24, `UmRT_Default` (`199.4.42.250.1.1`) and its license expiry
- [ ] It never changes anything (read-only)

**Verification:**
- [ ] Unit test: license XML parse and expiry classification
- [ ] Manual: run on this PC and on the bench PC

**Dependencies:** Task 1
**Files likely touched:** new `Doctor.cs`, `Program.cs`, tests
**Scope:** S

### Checkpoint 4: Closed loop
- [ ] `ST/variant-sample`: `activate --yes` + `verify` pass for both variants on `UmRT_Default`
- [ ] `doctor` output reviewed
- [ ] Review with the user before Phase 5

---

## Phase 5: CLI cleanup (finishes `docs/ideas/cli-subcommand-redesign.md`)

## Task 16: CLI spec
**Description:** Write the final command table: one positional `<project>` everywhere (a
`.tsproj`, a folder with one, or an `ST/<name>` tree), consistent option names (`--plc`,
`--variant`, `--target`, `--shell`, `--json`), and a legacy → new mapping table. Decide on
`System.CommandLine` vs a hand-rolled dispatcher (net48).

**Acceptance criteria:**
- [ ] Spec covers every command and option. No two options mean the same thing.
- [ ] The user approves the spec

**Verification:** Review only.
**Dependencies:** Checkpoint 4 (new commands are known)
**Files likely touched:** `docs/ideas/cli-subcommand-redesign.md`
**Scope:** S

## Task 17: New parser for `build`, `activate`, `verify`, `doctor`
**Description:** These commands produce their options directly, with no translation to legacy flags.

**Acceptance criteria:**
- [ ] The four commands follow the spec. `--help` per command.

**Verification:** Unit tests per command (move the relevant `RunOptionsTests`)
**Dependencies:** Task 16
**Files likely touched:** new `Cli/*.cs`, `Program.cs`, tests
**Scope:** M

## Task 18: New parser for `check` and `export`
**Acceptance criteria:**
- [ ] Both commands follow the spec. The old translation methods for them are deleted.

**Verification:** Unit tests
**Dependencies:** Task 17
**Files likely touched:** `Cli/*.cs`, `Program.cs`, tests
**Scope:** M

## Task 19: New parser for `sync` and `init`; remove legacy flags
**Acceptance criteria:**
- [ ] `sync` and `init` follow the spec. `.stconfig` keys still work (renamed keys documented).
- [ ] No legacy flat flag is accepted. `RunOptions` keeps only the options model.

**Verification:** Unit tests. `RunOptionsTests` / `RunOptionsConfigFileTests` updated.
**Dependencies:** Task 18
**Files likely touched:** `RunOptions.cs`, `Cli/*.cs`, `Program.cs`, tests
**Scope:** M

## Task 20: Docs and hooks
**Acceptance criteria:**
- [ ] README, `docs/plc-ci-setup-guide.md`, `docs/github-self-hosted-runner-install-steps.md`
      and `githooks/run-incremental-sync.ps1` use only the new syntax
- [ ] `grep` finds no legacy flag in docs or scripts

**Verification:** `grep -rn -- "--source\|--tsproj\|--sync-\|--export-" README.md docs githooks` returns nothing
**Dependencies:** Task 19
**Files likely touched:** `README.md`, `docs/*.md`, `githooks/*.ps1`
**Scope:** M

### Checkpoint 5: Complete
- [ ] All tests pass. No legacy flag remains.
- [ ] Bench PC full loop: `sync all` → `build --all-variants` → `activate --yes` → `verify`
- [ ] Ready for review

---

## Parallel work

| Can run in parallel | Must be in order |
|---|---|
| Tasks 3, 4, 5 and 12 after Task 0 | 1 → 2 · 5 → 6 → 7 · 8 → 9 → 10 → 11 · 12 → 13/14 · 16 → 17 → 18 → 19 → 20 |
| Task 15 after Task 1 | Phase 5 after Checkpoint 4 |
