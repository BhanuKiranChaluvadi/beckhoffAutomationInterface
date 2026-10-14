# Implementation Plan: XAE-only support, machine variants, variant I/O trees, closed loop

Status: **approved 2026-10-14 — in progress.** Task details: [`todo.md`](todo.md).

## Overview

The P2 machine family (Shark, UVM, SAS, Google) will move to **one** TwinCAT project with
TwinCAT Variant Management. A variant per machine selects machine values, feature code and
I/O hardware. This tool must build, check and run that project without manual clicks.

The plan has five parts:

1. **Fixes** so the tool works on PCs with TwinCAT XAE only (no Visual Studio 2022), and builds
   from a clean state.
2. **Variant builds**: define variants from a manifest, build one or all variants.
3. **Variant I/O trees**: one `io-devices.xml` where a device, box or terminal can belong to
   some machines only (the `TODO.md` request: "some %I and %Q are present for some machines").
4. **Closed loop**: activate a variant on a runtime (UmRT or a real machine), read live values
   over ADS, compare them with expected values. Plus a `doctor` command.
5. **CLI cleanup**: finish `docs/ideas/cli-subcommand-redesign.md` (clean break, one way to
   name the project, consistent option names).

Evidence for parts 1, 2 and 4 comes from a working prototype:
`BeckhoffLib/Experiments/VariantDemo/` (`VariantDemoTool.cs`, `EXPERIMENT_REPORT.md`). There,
two variants were built, activated on `UmRT_Default` and verified over ADS (all checks passed,
2026-10-14).

## Architecture decisions

| # | Decision | Reason |
|---|---|---|
| D1 | Default DTE shell: `TcXaeShell.DTE.17.0`. Fallback: `VisualStudio.DTE.17.0`. Override: `--shell xae\|vs2022` and `.stconfig` key `shell`. | Every TwinCAT XAE install has `TcXaeShell`. The `TODO.md` crash (`Class not registered {33ABD590-…}`) is a VS 2022 ProgID on a PC without a working VS 2022. The prototype used `TcXaeShell` without problems. |
| D2 | Before use, check that the CLSID's `LocalServer32` exe exists. | A ProgID can be registered while its exe is missing. Fail with a clear message, not a COM error. |
| D3 | Variants live in a new source manifest, `variants.xml` (variants, groups, define mode). A new stage `sync variants` applies it. | Same "manifest is the source of truth" pattern as `libraries.xml` / `io-devices.xml` / `events.xml`. |
| D4 | Define mode: **implicit** (`PvDefine = true`, `EnableImplicitDefines = true`). The variant name and its group names become PLC defines. Groups act as feature switches. | Proven in the prototype. No per-variant define lists to keep in sync. |
| D5 | `build --variant <v>` and `build --all-variants`. A variant switch always does Clean + Build. | The compiler does not check code in a branch that is not selected, so all variants must build. Incremental builds can hide old results. |
| D6 | Per-variant I/O: optional `Variants="A,B"` attribute on `<Device>`, `<Box>`, `<Terminal>` and `<Link>` in `io-devices.xml`. No attribute = all variants. The tool sets `PvDisable` on the item and disables it in the variants that do not list it. | Beckhoff's documented mechanism (`ITcSmTreeItem9.PvDisable` + `Disabled` per `CurrentProjectVariant`). Keeps one manifest. **Spike first (Task 7):** link behaviour per variant is not yet proven. |
| D7 | PLC code rule (documented, not enforced by the tool): declare `AT %I*` / `AT %Q*` variables for all machines. An unlinked variable is harmless. Use `{IF defined (...)}` around a declaration only when its type differs between machines. | Keeps the PLC symbol layout identical across variants. This is important for ADS clients. |
| D8 | ADS through native `TcAdsDll.dll` (P/Invoke, `Common64` for this x64 tool). No NuGet package. | Proven in the prototype. No new dependency for a net48 tool. |
| D9 | `verify` reads an expectations file (`expect.json`: symbol, type, expected value or condition). | Plain data. Can live next to the PLC source. No YAML dependency. |
| D10 | `activate` prints the boot project that is on the target now and needs `--yes`. | Activation replaces the target's boot project (the prototype replaced `PLC_NFL_SHARK` on UmRT). |
| D11 | CLI cleanup follows `docs/ideas/cli-subcommand-redesign.md`: a clean break, no compatibility shim. New commands (`activate`, `verify`, `doctor`) are written in the new style from day one. | Already decided in that document. New commands must not add more legacy flags. |

## Task list (index — details in `todo.md`)

### Phase 0: Baseline
- [x] Task 0: Clean baseline (commit or park the untracked `TODO.md`, `images/`)

### Phase 1: Works on XAE-only PCs
- [x] Task 1: DTE shell resolver (`TcXaeShell` default, VS 2022 fallback, `--shell`)
- [x] Task 2: Process detection for `devenv` **and** `TcXaeShell`
- [x] Task 2a: TwinCAT silent mode — no Yes/No prompts, previous setting restored (user request)
- [x] Task 3: Clean before build (`--clean`)
- [x] Task 4: Build-output-pane fallback for empty error texts
- [x] Task 4b: `build <path>` must not map errors to `.st` files of the current directory (found in Task 3)
- [x] Task 4c: failed build exits `-1` — closed, not a bug (test-command artifact)

### Checkpoint 1: Fixes
- [ ] 169+ unit tests pass. `build` passes on this PC with `--shell xae` and `--shell vs2022`.
- [ ] Manual: `build` passes on the XAE-only bench/CI PC (the `TODO.md` crash is gone).

### Phase 2: Variant builds
- [x] Task 5: `variants.xml` manifest + parser
- [x] Task 6: `sync variants` stage (apply variants, groups, implicit defines)
- [x] Task 7: `build --variant` / `--all-variants` with per-variant report
- [x] Task 7b: `init` rejects an invalid project name before XAE starts (found in Task 6)

### Checkpoint 2: Variant builds
- [x] `ST/VariantSample` (port of the VariantDemo) builds both variants. Build log shows each variant's own branch.

### Phase 3: Variant I/O trees
- [ ] Task 8: Spike — per-variant disable of I/O items and links (scratch project, findings only)
- [ ] Task 9: `Variants` attribute in `io-devices.xml` (parser + validation)
- [ ] Task 10: `sync io` applies per-variant enable/disable
- [ ] Task 11: `export io` writes the `Variants` attribute back (round trip)

### Checkpoint 3: Variant I/O
- [ ] Scratch project: a terminal that exists for variant A only is disabled in B. Both variants build. Export → sync → export gives the same file.

### Phase 4: Closed loop
- [x] Task 12: ADS client + `expect.json` parser
- [x] Task 13: `activate` command
- [x] Task 14: `verify` command
- [ ] Task 15: `doctor` command

### Checkpoint 4: Closed loop
- [ ] `ST/variant-sample`: `activate` + `verify` pass for both variants on `UmRT_Default`.

### Phase 5: CLI cleanup
- [ ] Task 16: CLI spec (command table, option names, legacy → new map) — review with the user
- [ ] Task 17: New parser for `build`/`activate`/`verify`/`doctor`
- [ ] Task 18: New parser for `check` / `export`
- [ ] Task 19: New parser for `sync` / `init`; remove legacy flags
- [ ] Task 20: Update README, CI guide, runner guide, `githooks/*.ps1`

### Checkpoint 5: Complete
- [ ] All tests pass. No legacy flag remains. Docs and hooks use the new syntax.
- [ ] Full loop on the bench PC: `sync all` → `build --all-variants` → `activate` → `verify`.

## Risks and mitigations

| Risk | Impact | Mitigation |
|---|---|---|
| Per-variant links do not behave as expected (for example, a link to a disabled terminal breaks the build or activation). | High | Task 8 spike before any I/O code. Fallback: keep links variant-neutral and rely on disabled devices only. |
| Activation needs real EtherCAT hardware; UmRT has none. | Medium | For UmRT runs, use the existing `DisableAllMasters` fallback (or a `UmRT` variant with all masters disabled). Real I/O is verified on the machine only. |
| Trial license expires every 7 days; runtime stays in Config with no clear error. | Medium | `doctor` shows the expiry date. `verify` prints a license hint when the runtime stays in Config. |
| `activate` overwrites a production boot project. | High | D10: print the current project, require `--yes`. Never default a target. |
| COM calls fail while XAE is busy; modal dialogs block. | Medium | Keep the existing message filter and `DialogAutoDismisser`. Add new dialogs only after they are seen in a real run. |
| COM behaviour cannot be unit-tested. | Medium | Keep COM code thin. Unit-test the pure parts (resolvers, parsers, comparisons). Verify COM paths manually on scratch projects, never on `ST/Shark` or a live machine project. |
| CLI clean break breaks teammates' scripts. | Medium | Task 16 review first. Update every doc and hook in the same pass (Task 20). |

## Order change (2026-10-14)

Phase 4 (closed loop on the runtime) runs **before** Phase 3 (variant I/O trees). Reason: the
user asked for a closed loop that is tested end to end; Phase 4 depends only on Phase 2, while
Phase 3 starts with a spike (Task 8) whose result is still open.

## Decisions on the open questions (2026-10-14, user: "follow recommendations")

1. Expectations format: **JSON** (D9).
2. `variants.xml` lives **next to the other manifests** in the source folder (D3).
3. `activate` has **two targets only** (user, 2026-10-14): `--target umrt` (UmRT_Default) or `--target local` (this PC's runtime). The AMS Net ID is read from the target's own config (UmRT: `...\Runtimes\UmRT_Default\3.1\TcRegistry.xml`; local: registry `TwinCAT3\System\AmsNetId`). Real machines are out of scope for now. `--yes` is still required (D10).
4. `ST/variant-sample` (port of the VariantDemo) is **added** to this repo as the test fixture.
5. Phase 5 (CLI cleanup) starts **after Checkpoint 4**, as ordered in this plan.
6. `tasks/2026-07-15-reverse-export-scaffold/` **moved to `tasks/archive/`** (work done per git history).
