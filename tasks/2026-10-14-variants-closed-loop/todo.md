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
- [ ] With `TcXaeShell`, the PID is found by the process diff (not only by the window-handle fallback)
- [ ] After a run, no `TcXaeShell.exe` that this tool started stays alive

**Verification:**
- [ ] Unit test: the process name comes from the resolved shell
- [ ] Manual: run `build` twice with `--shell xae`; check Task Manager has no leftover process

**Dependencies:** Task 1
**Files likely touched:** `VisualStudioSession.cs`, `DteShellResolver.cs`
**Scope:** S

## Task 3: Clean before build
**Description:** Add `--clean` to `build`. With it, call `SolutionBuild.Clean(true)` before
the build. Later, Task 7 always cleans when it switches variants.

**Acceptance criteria:**
- [ ] `build --clean` runs Clean and then Build. The log shows both.
- [ ] Without `--clean`, behaviour is unchanged

**Verification:**
- [ ] Unit test: option parsing
- [ ] Manual: scratch project — add an error, build (fails), fix it, `build --clean` (passes)

**Dependencies:** Task 0
**Files likely touched:** `Sync/BuildRunner.cs`, `SyncPipeline.cs`, `RunOptions.cs`, tests
**Scope:** S

## Task 4: Output-pane fallback for empty error texts
**Description:** If a build fails and every `ErrorItem.Description` is empty, read the *Build*
output pane and print its error lines (`file(line) : error: text`). Map them through
`ErrorLocationResolver` like normal errors.

**Acceptance criteria:**
- [ ] A failed build never prints "BUILD FAILED — N error(s)" with empty texts
- [ ] Lines from the output pane are parsed into file, line and text

**Verification:**
- [ ] Unit test: parse sample output-pane lines (`...MAIN.TcPOU(13) : error: C0032: ...`)
- [ ] Manual: hide the XAE window in a scratch run; the errors still print with text

**Dependencies:** Task 0
**Files likely touched:** `Sync/BuildRunner.cs`, new `Sync/BuildOutputParser.cs`, tests
**Scope:** S

### Checkpoint 1: Fixes
- [ ] All tests pass. Build is clean.
- [ ] `build` passes on this PC with `--shell xae` and with `--shell vs2022`
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
- [ ] Valid file → model + correct `ProjectVariantConfig` XML (same shape as the prototype)
- [ ] Invalid file → one error per problem, with line numbers
- [ ] No `variants.xml` → no variant behaviour (current projects unchanged)

**Verification:**
- [ ] Unit tests: valid, duplicate variant, unknown member, bad identifier, missing file

**Dependencies:** Task 0
**Files likely touched:** new `Sync/VariantManifestParser.cs`, new `VariantManifestParserTests.cs`
**Scope:** S

## Task 6: `sync variants` stage
**Description:** New stage that applies `variants.xml` to the project:
`ITcSysManager14.ProjectVariantConfig`, then on the PLC item `ITcSmTreeItem9.PvDefine = true`
and `ConsumeXml(<EnableImplicitDefines>true</EnableImplicitDefines>)`. Read before you write,
so a second run changes nothing. Add it to `sync all`.

**Acceptance criteria:**
- [ ] After `sync variants`, `ProduceXml` of the PLC item shows `PvUseDefine="true"` and `EnableImplicitDefines=true`
- [ ] A second run reports "no change"
- [ ] A variant removed from the manifest is removed from the project (report it, like other stages)

**Verification:**
- [ ] Unit test: diff logic (current config vs manifest → actions)
- [ ] Manual: scratch project; run twice; read the `.tsproj` and check `<ProjectVariant PvUseDefine="true">`

**Dependencies:** Task 5
**Files likely touched:** new `Sync/VariantSyncEngine.cs`, `SyncPipeline.cs`, `RunOptions.cs` (stage), tests
**Scope:** M

## Task 7: `build --variant` and `--all-variants`
**Description:** `build <project> --variant <v>` sets `CurrentProjectVariant`, cleans, builds and
reports. `--all-variants` reads the variant list from the project and does this for each one.
At the end it prints one summary table (variant → pass/fail, error count) and exits non-zero if
any variant fails. If the project has variants but `PvDefine` is off, fail with a clear message
("run `sync variants`"), because the PLC would get no defines.

**Acceptance criteria:**
- [ ] `--variant X` builds only X. An unknown X fails with the list of valid names.
- [ ] `--all-variants` builds every variant, even after one fails, and prints the summary
- [ ] The exit code is 0 only if all requested variants pass

**Verification:**
- [ ] Unit tests: summary/exit-code logic, unknown-variant message
- [ ] Manual: `ST/variant-sample` (Checkpoint 2)

**Dependencies:** Tasks 3, 6
**Files likely touched:** `Sync/BuildRunner.cs`, `SyncPipeline.cs`, `RunOptions.cs`, new `Sync/VariantBuildReport.cs`, tests
**Scope:** M

### Checkpoint 2: Variant builds
- [ ] Add `ST/variant-sample` (port of `Experiments/VariantDemo/src` + `variants.xml`)
- [ ] `sync all ST/variant-sample` then `build --all-variants`: both pass. The build log shows
      `VARIANT CHECK` and `FEATURE CHECK` messages for the correct branch.
- [ ] Remove the variants from the scratch project's manifest → `sync variants` removes them
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
- [ ] The parser supports `equals`, `increases`, `absentOrZero` and a `tolerance` for REAL/LREAL
- [ ] ADS error 1808 (symbol not found) is reported as "absent", not as a crash

**Verification:**
- [ ] Unit tests: parser, value decoding from byte arrays, comparisons
- [ ] Manual: read `MAIN.sMachine` from `UmRT_Default` while a variant runs

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
- [ ] Same output as the prototype for `ST/variant-sample` (6 checks per variant)
- [ ] The Config-mode case prints the license hint and exits non-zero

**Verification:**
- [ ] Unit test: result aggregation and exit code
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
