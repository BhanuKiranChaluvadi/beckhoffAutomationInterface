# Beckhoff Automation Interface — ST → TwinCAT Sync Engine

Syncs a folder of IEC 61131-3 Structured Text (`.st`) source files into a
persistent TwinCAT 3 PLC project via the Beckhoff Automation Interface (COM),
then builds the project and reports pass/fail — a closed-loop
"edit `.st` → sync → build → get errors" workflow without ever touching the
TwinCAT XAE UI by hand.

The tool is idempotent: re-running it against the same source/destination
only creates what's missing, updates what changed, and leaves everything
else untouched.

> **Deletion is opt-in, not automatic.** A normal (non-`--incremental`) sync
> never deletes PLC objects for `.st` files you've removed or renamed — they
> linger harmlessly in the TwinCAT project. Only `--incremental --confirm-delete`
> actually removes objects, and only on an exact, unambiguous name match (see
> "Incremental sync" below). If you delete/rename a file and expect the old
> PLC object gone, you must use that combination — it is not automatic.

## Prerequisites

See [beckhoffAutomationInterface/REQUIREMENTS.md](beckhoffAutomationInterface/REQUIREMENTS.md)
for full details and verification commands. Summary:

- **Windows** (COM automation is Windows-only)
- **.NET SDK 6.0+** (to run `dotnet build`; the app itself targets .NET Framework 4.8)
- **Visual Studio 2022 (17.x)**, any edition, with the *.NET desktop development* workload
- **Beckhoff TwinCAT 3.1 XAE**, installed *after* Visual Studio so it registers into it
  (Build 4026+ recommended)

If TwinCAT was installed before VS 2022 (or vice versa), repair the TwinCAT VS
integration as Administrator:
```powershell
MsiExec.exe /f{23005E9B-9FED-4C05-B4EB-6AC0ECC0BA7F}
```

## Build

```powershell
cd beckhoffAutomationInterface
dotnet build -c Debug -f net48
```

The executable ends up at `beckhoffAutomationInterface\bin\Debug\net48\beckhoffAutomationInterface.exe`.

## Running the executable

This is a standalone `.exe` — no `dotnet run`, no project file needed at
runtime. Run it directly, from anywhere, pointing `--source`/`--dest` at
whatever folders you want:

```powershell
# Full path, from any directory:
C:\path\to\beckhoffAutomationInterface\beckhoffAutomationInterface\bin\Debug\net48\beckhoffAutomationInterface.exe --source "C:\path\to\ST\Shark" --dest "C:\path\to\TwinCAT-projects-root" --init

# Or cd into the output folder first and use a relative path:
cd beckhoffAutomationInterface\bin\Debug\net48
.\beckhoffAutomationInterface.exe --source "C:\path\to\ST\Shark" --dest "C:\path\to\TwinCAT-projects-root" --init
```

**First time trying the tool at all?** Point it at the bundled `ST\sample`
project instead of a real one — it's a small, self-contained demo
(`FB_Motor`/`I_Motor`, a couple of DUTs, one GVL) safe to bootstrap and
throw away, and a good way to confirm your TwinCAT/Visual Studio COM setup
works before running against anything real:

```powershell
.\beckhoffAutomationInterface.exe --source "C:\path\to\ST\sample" --dest "C:\some\scratch\folder" --init
```

`--init` is required the first time (creates a new solution/TwinCAT/PLC
project at `--dest`); every run after that reopens and reconciles the
existing one — see [Stage flags](#stage-flags-composable-one-shot-modes)
below for `--init`'s full rationale, and the sections further down for
every other flag, the manifests (`libraries.xml`/`io-devices.xml`/
`events.xml`), and the typical day-to-day workflow.

## Directory layout

```
beckhoffAutomationInterface/   # the C# tool (Program.cs, SyncPipeline.cs, Sync/, etc.)
ST/                            # .st source trees, one subfolder per PLC project
  Shark/                       # the real production project
    App/                       # PROGRAMs, organized in whatever subfolders you like
    Lib/                       # FUNCTION_BLOCKs, INTERFACEs, DUTs, GVLs
    libraries.xml              # PLC library references to sync (Tc2_Standard, etc.)
    io-devices.xml             # EtherCAT I/O hardware tree + PLC-variable links (optional)
    events.xml                 # Event Classes to sync (see Known limitations below)
  sample/                      # small self-contained demo — safe first project to try the tool on
example/                       # reference material from Beckhoff/community samples
docs/                          # design notes
```

A `.st` file's path relative to its `ST/<Project>/` root is mirrored as a PLC
folder in the TwinCAT project (e.g. `ST/Shark/App/Shark/PRG_MAIN.st` becomes
`Shark Project ▸ App ▸ Shark ▸ PRG_MAIN`).

## Running it

The tool takes the folder of `.st` files as `--source` and the folder under
which the TwinCAT solution lives as `--dest`. Both default to `.` (the
current directory) if omitted; the project/solution name defaults to
`--source`'s own folder name.

```powershell
cd beckhoffAutomationInterface\bin\Debug\net48

# First-ever run for a project: --init explicitly allows creating the
# solution/TwinCAT/PLC project. Without it, a missing solution is a hard
# error (exit 1) in EVERY mode — so a mistyped path fails loudly instead of
# silently building a fresh empty project.
.\beckhoffAutomationInterface.exe --source "C:\path\to\ST\Shark" --dest "C:\path\to\TwinCAT-projects-root" --init

# Full run: sync .st files, libraries, IO devices/links, events, then build
# and report. Takes several minutes for a large source tree.
.\beckhoffAutomationInterface.exe --source "C:\path\to\ST\Shark" --dest "C:\path\to\TwinCAT-projects-root"

# Bare invocation or --help prints usage and exits.
.\beckhoffAutomationInterface.exe
.\beckhoffAutomationInterface.exe --help
```

### Stage flags (composable one-shot modes)

The pipeline is made of five stages. Passing any stage flag(s) runs **exactly
those stages** — in a fixed order, regardless of flag order — then exits.
Passing **none** of them runs everything (the full sync+build above). This is
how one-time per-project setup (device tree, Create PLC Data Type, Event
Classes) runs in isolation without touching code or compiling.

| Stage flag | What it does | Visual Studio? |
|---|---|---|
| `--sync-code` | `.st` files → PLC POUs (parse, lint, drift warnings, sync, save). No build. | opens VS |
| `--sync-libs` | `libraries.xml` → PLC library references. | opens VS |
| `--sync-io` | `io-devices.xml` → device/box/terminal tree, "Create PLC Data Type" `.tsproj` templates, and `<Links>`. Undeclared items are only **warned** about unless `--confirm-delete-io`. | opens VS, closes it for the `.tsproj` edit, reopens only if links need it |
| `--sync-events` | `events.xml` + `event-classes/*.xml` → missing Event Classes written directly into the `.tsproj`. | **no VS at all** — pure file edit, the fastest mode |
| `--build` (alias `--build-only`, deprecated) | Open, compile, report errors mapped back to `.st file:line`. **Exit code 0 = BUILD PASSED, 1 = failed or timed out.** | opens VS |

Stage execution order for any subset: code → libs → io-tree → `.tsproj` edits
(io templates + events) → io links → build. Visual Studio is opened lazily by
the first stage that needs it and closed for the `.tsproj` edits.

```powershell
# One-time setup, in isolation (no code sync, no build):
.\beckhoffAutomationInterface.exe --source ... --dest ... --sync-io --sync-events

# Only push ST code changes into the project, don't compile:
.\beckhoffAutomationInterface.exe --source ... --dest ... --sync-code

# CI pipeline: compile only, fail the job on errors. Never bootstraps —
# a wrong path exits 1 instead of green-building an empty project.
.\beckhoffAutomationInterface.exe --source ... --dest ... --build
if ($LASTEXITCODE -ne 0) { throw "PLC build failed" }
```

### Other flags

| Flag | Default | Purpose |
|---|---|---|
| `--source <path>` (alias `--src`) | `.` | Folder containing the `.st` files |
| `--dest <path>` (alias `--dst`) | `.` | Folder under which `<name>/<name>.sln` lives |
| `--name <name>` | `--source`'s folder name | Project/solution name |
| `--init` | off | Allow creating the solution/TwinCAT/PLC project when missing. Without it, a missing solution is a hard error (exit 1) in every mode. |
| `--check-events` (alias `--events-only`, deprecated) | off | Read-only check of `events.xml` against the `.tsproj` (declared vs actual), then exit — code 1 if any declared class is missing, 0 otherwise. No Visual Studio session. Usable as a fast pipeline gate. |
| `--check-links` | off | Read-only check of every declared `%I`/`%Q` GVL/PROGRAM variable against `io-devices.xml`'s `<Links>` section, then exit — code 1 if any is unlinked or any `.st` file failed to parse, 0 otherwise. No Visual Studio session (see below). |
| `--parse-only` | off | Parse every `.st` file with no Visual Studio involved at all — takes seconds. Use this first after any source or parser change to catch syntax/structure errors fast. Also prints the same %I/%Q link report as `--check-links`, non-blocking. |
| `--ignore <glob>` | none | Exclude `.st` files matching this glob pattern (repeatable, e.g. `--ignore "*_deprecated.st" --ignore "Lib/Legacy/**"`). Merged with a `.stignore` file in `--source`, if present. |
| `--incremental` | off | Sync only `.st` files changed/deleted since the last recorded sync (see below) instead of the whole source folder. Requires `--source` to be a git repo with a prior full sync's baseline. |
| `--confirm-delete-io` | off | Actually delete IO tree items not declared in `io-devices.xml`. Without it they are only warned about — never deleted. |
| `--export <name>` | none | Write the named live PLC object's current text back to its mirrored `.st` file (all supported kinds — see below). |
| `--export-links` | off | Write ALL currently-linked PLC-variable-to-IO-channel mappings out to `links.xml` (see below) — the way to capture links made by hand in the TwinCAT IDE. |
| `--format-check` | off | Report (never write) `.st` style issues — trailing whitespace, mixed line endings, EOF newline hygiene — with no Visual Studio session needed (see below). |
| `--config <path>` | `--source`'s folder | Look for a `.stconfig` defaults file in this folder instead (see below). |
| `--no-config` | off | Ignore any `.stconfig` defaults file for this run only. |

The `githooks/` incremental worker is unaffected by `--init`: it always
targets an already-bootstrapped project (the reopen path).

### Default options (`.stconfig`)

Retyping `--source`/`--dest`/`--name` (and other flags) on every invocation
gets old fast. Drop a `.stconfig` file at the **top level of your `--source`
project** — the same place a `.stignore` file already lives — and the tool
loads it automatically:

```
# ST\Shark\.stconfig
dest=C:\path\to\TwinCAT-projects-root
name=Shark
incremental=true
```

```powershell
.\beckhoffAutomationInterface.exe --source "C:\path\to\ST\Shark"
# resolves dest/name/etc. from ST\Shark\.stconfig — only --source still needed
```

**Discovery order:** `--config <path>` (look there instead), else the
resolved `--source` folder, else the process's current directory if neither
is given. Use `--config` to keep `.stconfig` somewhere other than inside
`--source` — that file can then set its own `source` key too, since at that
point `--source` hasn't been resolved from the command line yet.

**Precedence:** an explicit command-line flag always wins; `.stconfig` only
fills in what you didn't type; anything neither specifies falls back to
today's hardcoded default (e.g. `.` for `--source`/`--dest`).

**Supported keys:** `source`, `dest`, `name`, `export`, plus the boolean
flags `export-links`, `incremental`, `parse-only`, `format-check`,
`check-events`, `check-links` (a value of `true`/`1`/`yes`, case-insensitive,
is truthy — anything else, including a missing key, is false), and the five
stage keys `sync-code`, `sync-libs`, `sync-io`, `sync-events`, `build`.

**Stage keys are a group, not five independent defaults:** if the command
line names *any* `--sync-*`/`--build` flag, `.stconfig`'s stage keys are
ignored entirely for that run — a config default like `build=true` can
never silently tack an extra stage onto a one-off `--sync-code`-only
invocation. If the command line names no stage flag at all, `.stconfig`'s
stage keys are used instead (if any are set); if neither specifies one, the
run defaults to `All`, same as always.

**`--init`, `--confirm-delete`, and `--confirm-delete-io` can never come
from `.stconfig`** — only a real command-line flag counts, full stop. These
exist specifically so their effect can't happen by accident (see
`--confirm-delete-io` above and `--init`'s rationale below); letting a
defaults file set them would defeat the entire point.

Pass `--no-config` to ignore `.stconfig` for one invocation without
deleting or renaming it. When a `.stconfig` was actually loaded, the tool
prints a one-line note before the usual `Source=/Dest=/Project=` line, so
the behavior is never silent.

See [`.stconfig.example`](.stconfig.example) at the repo root for a fully
annotated template — copy it to `.stconfig` in your launch folder and edit.

### Ignoring source files

Add a `.stignore` file at the root of `--source` to permanently exclude `.st`
files from every sync (one gitignore-style glob pattern per line, `#`
comments and blank lines allowed):

```
# .stignore
*_deprecated.st
Lib/Legacy/**
```

A pattern with no `/` matches the file name at any depth; a pattern with `/`
is matched against the whole source-relative path. `*` matches within a path
segment, `**` matches across segments — so **to ignore an entire folder**,
the pattern needs a trailing `/**` (e.g. `Lib/Legacy/**`); a bare folder name
with no `/` only matches a *file* by that exact name, not a folder. Use
`--ignore <glob>` for one-off, per-invocation exclusions on top of
`.stignore`.

See [`.stignore.example`](.stignore.example) at the repo root for a fuller,
annotated sample covering each pattern shape (single file, name pattern,
whole folder, one folder's direct files but not its subfolders, a folder at
any depth) — copy it to `.stignore` in your `--source` folder and edit.

### Incremental sync

Every successful `.st` sync (full or `--incremental`) records the current git
commit SHA in `.st-sync-state` at the root of `--source` (requires `--source`
to be inside a git repo; skipped silently otherwise). A later run with
`--incremental` reads that SHA, computes `git diff --name-status` against it,
and parses/syncs ONLY the changed `.st` files instead of the whole folder —
much faster once a project has many objects.

```powershell
# First run: full sync, establishes the baseline in .st-sync-state
.\beckhoffAutomationInterface.exe --source "C:\...\ST\Shark" --dest "C:\...\TwinCAT"

# Later runs, after committing .st changes: only re-syncs what changed
.\beckhoffAutomationInterface.exe --source "C:\...\ST\Shark" --dest "C:\...\TwinCAT" --incremental
```

By default, `git diff`-reported deletions are only **warned** about, never
acted on — `.st-sync-state`'s baseline still advances, but the corresponding
PLC object(s) are left in place. Add `--confirm-delete` to actually remove
them:

```powershell
.\beckhoffAutomationInterface.exe --source "C:\...\ST\Shark" --dest "C:\...\TwinCAT" --incremental --confirm-delete
```

`--confirm-delete` is conservative by design: it only deletes an object when
the deleted file's name (without extension) matches **exactly one** live PLC
object, using the official `ITcSmTreeItem.DeleteChild()` API. It skips (and
reports why) anything ambiguous — zero matches, more than one match, or a
standalone `<Owner>.<Method>.st` file (deleting an individual method isn't
supported; remove it manually). `--incremental` refuses to run (exit code 1)
if no baseline exists yet — run a full sync first.

### Automatic sync on commit (git hook)

A `post-commit` hook in [`githooks/`](githooks/) can trigger `--incremental`
automatically, detached (so `git commit` returns immediately) — sync output
goes to `githooks/logs/<timestamp>.log`, not the terminal.

One-time setup:

```powershell
git config core.hooksPath githooks
```

The hook (`githooks/post-commit`) is a fast no-op unless the commit touched
`*.st` files, then launches `githooks/run-incremental-sync.ps1` hidden/
detached. That script assumes the TwinCAT project already exists at
`C:\Users\<you>\Documents\TwinCAT` with the ST source at `<repo>\ST\Shark` —
override with `$env:BECKHOFF_TWINCAT_DEST` / `$env:BECKHOFF_ST_SOURCE` if
different. It does **not** pass `--name`, so the project name defaults to
the source folder's own directory name ("Shark") — this must match whatever
name the project was originally bootstrapped with.

### Exporting manual PLC-side edits

Some objects can only be created manually via the XAE UI (e.g. IO-scanned
DUTs, Event Classes) but should still live in `.st` source once created.
`--export <ObjectName>` finds that live object, reads its current text, and
writes it to the correct mirrored `.st` file path (creating folders as
needed) — the read-side counterpart of the normal sync, using the same
`DeclarationText`/`ImplementationText` properties `PouSyncEngine` writes.

```powershell
.\beckhoffAutomationInterface.exe --source "C:\...\ST\Shark" --dest "C:\...\TwinCAT" --export T_Beckhoff_AmbientSensor
```

`--export` now supports every PLC object kind synced from `.st` source: DUTs
(STRUCT/ENUM/ALIAS), GVLs, and FUNCTION_BLOCK/PROGRAM/FUNCTION/INTERFACE
objects — including their child METHODs and PROPERTIES, stitched back
together in tree order with the correct terminators
(`END_FUNCTION_BLOCK`/`END_PROGRAM`/`END_FUNCTION`/`END_INTERFACE`/
`END_METHOD`/`END_PROPERTY`/`END_GET`/`END_SET`) re-added, since none of
these are ever stored in `Declaration`/`ImplementationText`. Errors refuse
cleanly if the name isn't found, or matches more than one object.

**Known caveat**: non-ASCII characters can be lossy on round-trip (see the
Phase 1 spike findings in `docs/ideas/st-plc-bidirectional-sync.md`) —
TwinCAT appears to store POU text internally in a legacy codepage, so rare
special characters in comments/strings may not survive an export exactly.

### Naming-convention linting

Every parse (`--parse-only` or a full sync) runs a naming-convention linter
over the parsed objects and prints warnings — it never blocks the sync or
rewrites anything. It checks the prefix convention already used throughout
this codebase:

| Kind | Expected prefix | Example |
|---|---|---|
| `FUNCTION_BLOCK` | `FB_` | `FB_HeatZone` |
| `PROGRAM` | `PRG_` | `PRG_MAIN` |
| `INTERFACE` | `I_` | `I_Tunable` |
| `FUNCTION` | `F_` | `F_ClampSpeed` |
| `ENUM` (`TYPE ... : (...)`) | `E_` | `E_CTRL_MODE` |
| `STRUCT` | `ST_` | `ST_HeatZoneConfig` |
| Alias (`TYPE X : Y;`) | `T_` | `T_Beckhoff_AmbientSensor` |
| GVL | `GVL_` | `GVL_HeatZone` |

Known false positives (safe to ignore): the default TwinCAT template's own
`MAIN` program, and alias-of-a-struct types conventionally kept under the
`ST_` prefix instead of `T_` (e.g. `ST_MFC_Telemetry : ST_MFCTelemetry`) to
signal they represent the same struct shape. METHODs and PROPERTIES have no
naming convention and are never checked.

### Checking %I/%Q ↔ IO links

`io-devices.xml`'s `<Links>` section (see "Manifests" below for the full
sync mechanism) maps a declared `%I`/`%Q` variable to a physical IO channel;
nothing enforces that every variable you declare actually has one.
`--check-links` (or, more lightly, every `--parse-only`) parses all `.st`
source, finds every
`AT %I*`/`AT %Q*` declaration in a **GVL or PROGRAM**, and cross-checks it
against `<Links>`:

```powershell
.\beckhoffAutomationInterface.exe --source "C:\...\ST\Shark" --check-links
```

```
2026-07-15 ...: [check-links] 34 declared, 0 linked, 34 unlinked, 0 stale link(s).
    ! UNLINKED PRG_DIGITAL_INPUT.IO_DoorLT (%I) — App/Shark/Programs/PRG_DIGITAL_INPUT.st
    ! UNLINKED GVL_Safety.SafetyOk (%I) — App/Shark/GVL_Safety.st
    ...
```

It reports two things: declared variables with **no matching `<Link>`**
(exit code 1 under `--check-links` if any exist — a stale-configuration gate
usable in CI), and `<Link>` entries whose `PlcVar` matches **no** declared
variable (a stale/typo'd entry, e.g. left over after a rename) — these are
always informational only, never affecting the exit code.

**Scope limit:** only `GVL`/`PROGRAM`-level declarations are checked. A
`FUNCTION_BLOCK`'s own `AT %I*/%Q*` member (or a `STRUCT`/DUT member) is
resolvable only through wherever that block is actually *instantiated*
(e.g. `PRG_MAIN.fbMotor.bEnable`), which can't be determined from static
source alone — those declarations exist and sync normally, they're just not
covered by this check.

### Variable links (`links.xml`)

`io-devices.xml`'s `<Links>` section (above) is this tool's own simple
schema, applied one pair at a time. `links.xml`, if present, is TwinCAT's
**own native** `<VarLinks>` export/import format — the exact schema the XAE
IDE itself produces via "Export Variable Mapping" and reads back via
"Import Variable Mapping" — applied in **one bulk COM call**
(`ITcSysManager.ConsumeMappingInfo`). Both coexist: if present, `links.xml`
is applied first, then `io-devices.xml`'s `<Links>` on top — neither
replaces the other.

**The easiest way to get a real, correct `links.xml`:** link your `%I`/`%Q`
variables to hardware once by hand in the TwinCAT IDE (or against a
simulated target), then run:

```powershell
.\beckhoffAutomationInterface.exe --source "C:\...\ST\Shark" --dest "C:\...\TwinCAT" --export-links
```

which writes the current mapping to `links.xml` via
`ITcSysManager.ProduceMappingInfo` — the same thing "Export Variable
Mapping" does. This is how a file like the real
`Spectrometer Instance Mappings.xml` (seen in a working reference project)
comes to exist. `--export-links` overwrites any existing `links.xml`, the
same convention `--export <name>` already uses for its target `.st` file.

See [`links.xml.example`](links.xml.example) at the repo root for a fully
annotated template, including the schema shape and a second, older/hand-
authored variant that also works (seen in Beckhoff's own official
`CodeGenerationDemo` sample). `--check-links`/`--parse-only` also
cross-reference `links.xml` entries the same way they do `io-devices.xml`'s
`<Links>` — a `links.xml` entry whose variable is nested through a
`FUNCTION_BLOCK` instance (e.g. `MAIN.fbSpec.inLogicSig[1]`, real usage seen
in that same reference project) can't be statically verified either way, so
it's reported informationally as "unresolvable," never as unlinked or stale.

### Style checking

`--format-check` scans every non-ignored `.st` file under `--source` and
prints a dry-run report of hygiene issues — it never modifies any file, and
needs no Visual Studio session at all (similar to `--parse-only`):

```powershell
.\beckhoffAutomationInterface.exe --source "C:\...\ST\Shark" --format-check
```

It checks for: mixed line endings (both CRLF and bare LF in the same file),
trailing whitespace, a missing trailing newline at end of file, and extra
blank lines at end of file. This is deliberately minimal — it does **not**
re-indent or otherwise rewrite code; a full auto-formatter remains a
possible future addition (see `docs/ideas/st-plc-bidirectional-sync.md`).

### Typical workflow

```powershell
# 0. First time only: bootstrap the TwinCAT solution + one-time project setup
.\beckhoffAutomationInterface.exe --source "C:\...\ST\Shark" --dest "C:\...\TwinCAT" --init
.\beckhoffAutomationInterface.exe --source "C:\...\ST\Shark" --dest "C:\...\TwinCAT" --sync-io --sync-events

# 1. Fast syntax check after editing .st files (no Visual Studio, ~instant)
.\beckhoffAutomationInterface.exe --source "C:\...\ST\Shark" --parse-only

# 2. Full sync + build (reopens and reconciles the existing project)
.\beckhoffAutomationInterface.exe --source "C:\...\ST\Shark" --dest "C:\...\TwinCAT"

# 3. Fix compile errors reported, then iterate quickly without re-syncing
.\beckhoffAutomationInterface.exe --source "C:\...\ST\Shark" --dest "C:\...\TwinCAT" --build
```

> **Safety note:** `--dest` + the project name determine exactly which
> `.sln`/`.tsproj` are targeted. Creating a NEW project always requires
> `--init` — without it, a missing solution is a hard error, so a mistyped
> path can never silently bootstrap (or bury a duplicate project inside)
> the wrong location.

## Manifests (config data, not `.st` source)

Alongside the `.st` files, a project folder can contain:

- **`libraries.xml`** — PLC library references (`<Library Name="Tc2_Standard" Version="*" Company="Beckhoff Automation GmbH" />`)
- **`io-devices.xml`** — EtherCAT I/O hardware tree (`<Device>`/`<Box>`/`<Terminal>`) plus optional `<Links>` mapping PLC variables to I/O channels
- **`events.xml`** — Event Classes for `Tc3_EventLogger`, created from matching
  `event-classes/<Name>.xml` templates via a direct `.tsproj` edit (the
  Automation Interface has no path for this — see `Sync/TsprojEventClassEditor.cs`)

All three are synced idempotently: existing entries are left alone, missing
ones are added, removed-from-manifest ones are cleaned up (library/IO only —
and IO cleanup requires the explicit `--confirm-delete-io` flag, otherwise
undeclared items are only warned about).

## Known limitations

- **Event Classes have no Automation Interface path** (`CreateChild`/
  `ConsumeXml` can't express them), so the tool creates them by editing the
  `.tsproj` directly while Visual Studio is closed
  (`Sync/TsprojEventClassEditor.cs`), using the exact `<DataType>` XML from a
  matching `event-classes/<Name>.xml` template with its REAL GUID. Verified
  surviving VS reload + build on the live project (2026-07-14). The read-only
  `--check-events` still exists as a fast preflight/pipeline gate.
- **`MDP5001_*` PLC data type names are config-hash suffixes** TwinCAT
  computes from a terminal's PDO/revision configuration — they are NOT
  portable across machines whose ESI catalogs instantiate different terminal
  revisions. If `.st` aliases report `Unknown type` for one, run `--build`
  once and read the actual generated `MDP5001_*` names out of the saved
  `.tsproj`, then update the aliases and `plc-data-types/*.xml` template.
- **IO variable linking** requires the PLC instance's I/O image to exist,
  which normally only materializes after *Activate Configuration* against a
  real or simulated target. If a declared link can't be resolved, the tool
  falls back to disabling the affected EtherCAT master so the build still
  passes unattended.

## Troubleshooting

- **A `devenv.exe` process is left running after a crash/interrupt:** the
  tool always tries to close Visual Studio in a `finally`/`Dispose`, but if
  you kill the tool itself mid-run, close it manually: `Get-Process devenv | Stop-Process -Force`.
- **`RPC_E_SERVERCALL_RETRYLATER` / random COM failures:** these are
  automatically retried internally; if you still see one bubble up, an
  existing lingering `devenv.exe` from a previous run is the usual cause —
  close it and retry.
- See [beckhoffAutomationInterface/REQUIREMENTS.md](beckhoffAutomationInterface/REQUIREMENTS.md)
  for COM-registration and environment verification commands if the tool
  fails before ever reaching your `.st` sources.
