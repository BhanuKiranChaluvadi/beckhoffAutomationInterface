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

## Directory layout

```
beckhoffAutomationInterface/   # the C# tool (Program.cs, Sync/, etc.)
ST/                            # .st source trees, one subfolder per PLC project
  Shark/
    App/                       # PROGRAMs, organized in whatever subfolders you like
    Lib/                       # FUNCTION_BLOCKs, INTERFACEs, DUTs, GVLs
    libraries.xml              # PLC library references to sync (Tc2_Standard, etc.)
    io-devices.xml             # EtherCAT I/O hardware tree + PLC-variable links (optional)
    events.xml                 # Event Classes to sync (see Known limitations below)
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

# Full run: sync .st files, libraries, IO devices/links, then build and report.
# Takes several minutes for a large source tree (Visual Studio has to load,
# the whole PLC tree gets reconciled, then the project is compiled).
.\beckhoffAutomationInterface.exe --source "C:\path\to\ST\Shark" --dest "C:\path\to\TwinCAT-projects-root"

# Bare invocation or --help prints usage and exits.
.\beckhoffAutomationInterface.exe
.\beckhoffAutomationInterface.exe --help
```

### Flags

| Flag | Default | Purpose |
|---|---|---|
| `--source <path>` | `.` | Folder containing the `.st` files |
| `--dest <path>` | `.` | Folder under which `<name>/<name>.sln` is created/opened |
| `--name <name>` | `--source`'s folder name | Project/solution name |
| `--parse-only` | off | Parse every `.st` file with no Visual Studio involved at all — takes seconds. Use this first after any source or parser change to catch syntax/structure errors fast. |
| `--build-only` | off | Skip the `.st`/library/IO sync steps; just reopen the existing project, build, and report. Use for fast iteration on compile errors when the `.st` source hasn't changed. |
| `--events-only` | off | Check `events.xml` against the `.tsproj` (declared vs actual) and stop — no Visual Studio session needed (see Known limitations) |
| `--ignore <glob>` | none | Exclude `.st` files matching this glob pattern (repeatable, e.g. `--ignore "*_deprecated.st" --ignore "Lib/Legacy/**"`). Merged with a `.stignore` file in `--source`, if present. |
| `--incremental` | off | Sync only `.st` files changed/deleted since the last recorded sync (see below) instead of the whole source folder. Requires `--source` to be a git repo with a prior full sync's baseline. |
| `--export <name>` | none | Write the named live PLC object's current text back to its mirrored `.st` file (all supported kinds — see below). |
| `--format-check` | off | Report (never write) `.st` style issues — trailing whitespace, mixed line endings, EOF newline hygiene — with no Visual Studio session needed (see below). |

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
segment, `**` matches across segments. Use `--ignore <glob>` for one-off,
per-invocation exclusions on top of `.stignore`.

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
# 1. Fast syntax check after editing .st files (no Visual Studio, ~instant)
.\beckhoffAutomationInterface.exe --source "C:\...\ST\Shark" --parse-only

# 2. Full sync + build (first run bootstraps a new TwinCAT solution;
#    later runs reopen and reconcile the existing one)
.\beckhoffAutomationInterface.exe --source "C:\...\ST\Shark" --dest "C:\...\TwinCAT"

# 3. Fix compile errors reported, then iterate quickly without re-syncing
.\beckhoffAutomationInterface.exe --source "C:\...\ST\Shark" --dest "C:\...\TwinCAT" --build-only
```

> **Safety note:** `--dest` + the project name determine exactly which
> `.sln`/`.tsproj` get created or overwritten. If nothing exists yet at that
> location, the tool bootstraps a brand-new project there — **always point
> `--dest` at a location you intend to (re)create**, never assume it's a
> no-op to point it somewhere with existing unrelated content.

## Manifests (config data, not `.st` source)

Alongside the `.st` files, a project folder can contain:

- **`libraries.xml`** — PLC library references (`<Library Name="Tc2_Standard" Version="*" Company="Beckhoff Automation GmbH" />`)
- **`io-devices.xml`** — EtherCAT I/O hardware tree (`<Device>`/`<Box>`/`<Terminal>`) plus optional `<Links>` mapping PLC variables to I/O channels
- **`events.xml`** — Event Classes for `Tc3_EventLogger` (see Known limitations — checked, not auto-created)

All three are synced idempotently: existing entries are left alone, missing
ones are added, removed-from-manifest ones are cleaned up (library/IO only).

## Known limitations

- **Event Classes are not auto-creatable.** Both the Automation Interface
  (`CreateChild`/`ConsumeXml`) and direct `.tsproj` XML editing were tried
  and don't work — Visual Studio silently drops any hand-authored
  `<DataType>` block on its own next save, regardless of schema. Create any
  needed Event Class once by hand via the TwinCAT UI (`SYSTEM ▸ Type System
  ▸ Event Classes ▸ New`). The tool DOES check `events.xml` against the live
  project (`Sync/EventClassChecker.cs`) and reports which declared classes
  are still missing, so you always know what needs manual creation — it
  just can't create them for you.
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
