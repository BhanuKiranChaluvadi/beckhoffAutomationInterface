# Beckhoff Automation Interface — ST → TwinCAT Sync Engine

Syncs a folder of IEC 61131-3 Structured Text (`.st`) source files into a
persistent TwinCAT 3 PLC project via the Beckhoff Automation Interface (COM),
then builds the project and reports pass/fail — a closed-loop
"edit `.st` → sync → build → get errors" workflow without ever touching the
TwinCAT XAE UI by hand.

The tool is idempotent: re-running it against the same source/destination
only creates what's missing, updates what changed, and leaves everything
else untouched.

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
| `--events-only` | off | Sync `events.xml` into the `.tsproj` file only, then stop (see Known limitations) |
| `--ignore <glob>` | none | Exclude `.st` files matching this glob pattern (repeatable, e.g. `--ignore "*_deprecated.st" --ignore "Lib/Legacy/**"`). Merged with a `.stignore` file in `--source`, if present. |

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
- **`events.xml`** — Event Classes for `Tc3_EventLogger` (see Known limitations — not yet functional)

All three are synced idempotently: existing entries are left alone, missing
ones are added, removed-from-manifest ones are cleaned up (library/IO only).

## Known limitations

- **Event Classes are not yet automatable.** Both the Automation Interface
  (`CreateChild`/`ConsumeXml`) and direct `.tsproj` XML editing were tried
  and don't work — see `EventClassSync.cs`'s doc comment for the full
  investigation. Until resolved, create any needed Event Class once by hand
  via the TwinCAT UI (`SYSTEM ▸ Type System ▸ Event Classes ▸ New`).
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
