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
runtime. Every invocation starts with a command: `build`, `sync`, `check`,
`export`, or `init`. Run bare or with `--help` to list them:

```powershell
.\beckhoffAutomationInterface.exe --help
```

Run it directly, from anywhere, pointing at whatever folders you want:

```powershell
# Full path, from any directory:
C:\path\to\beckhoffAutomationInterface\beckhoffAutomationInterface\bin\Debug\net48\beckhoffAutomationInterface.exe init "C:\path\to\ST\Shark" --dest "C:\path\to\TwinCAT-projects-root"

# Or cd into the output folder first and use a relative path:
cd beckhoffAutomationInterface\bin\Debug\net48
.\beckhoffAutomationInterface.exe init "C:\path\to\ST\Shark" --dest "C:\path\to\TwinCAT-projects-root"
```

**First time trying the tool at all?** Point it at the bundled `ST\sample`
project instead of a real one — it's a small, self-contained demo
(`FB_Motor`/`I_Motor`, a couple of DUTs, one GVL) safe to bootstrap and
throw away, and a good way to confirm your TwinCAT/Visual Studio COM setup
works before running against anything real:

```powershell
.\beckhoffAutomationInterface.exe init "C:\path\to\ST\sample" --dest "C:\some\scratch\folder"
```

`init` is required the first time (creates a new solution/TwinCAT/PLC
project at `--dest`); every later run uses `sync` to reopen and reconcile
the existing one — see [Commands](#commands) below for the full command
reference, and the sections further down for the manifests
(`libraries.xml`/`io-devices.xml`/`events.xml`) and the typical day-to-day
workflow.

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

Every subcommand takes the folder of `.st` files (or, for `build`, the PLC
project itself) as a **positional path** — the first argument after the
command name. `sync`/`init` also take `--dest`, the folder under which the
TwinCAT solution lives; it defaults to `.` (the current directory) if
omitted, as does the project/solution name (defaults to the source folder's
own name).

```powershell
cd beckhoffAutomationInterface\bin\Debug\net48

# First-ever run for a project: `init` explicitly allows creating the
# solution/TwinCAT/PLC project. Without it (i.e. using `sync`), a missing
# solution is a hard error (exit 1) — so a mistyped path fails loudly
# instead of silently building a fresh empty project.
.\beckhoffAutomationInterface.exe init "C:\path\to\ST\Shark" --dest "C:\path\to\TwinCAT-projects-root"

# Full run: sync .st files, libraries, IO devices/links, events, then build
# and report. Takes several minutes for a large source tree.
.\beckhoffAutomationInterface.exe sync all "C:\path\to\ST\Shark" --dest "C:\path\to\TwinCAT-projects-root"

# Bare invocation or --help prints usage and exits.
.\beckhoffAutomationInterface.exe
.\beckhoffAutomationInterface.exe --help
```

### Commands

The pipeline is made of six stages. `sync <mode>` runs **exactly one**
stage (or `all` for every stage in order) then exits — this is how
one-time per-project setup (device tree, Create PLC Data Type, Event
Classes) runs in isolation without touching code or compiling. `build`
compiles only, without syncing anything first.

| Command | What it does | Visual Studio? |
|---|---|---|
| `sync code <path>` | `.st` files → PLC POUs (parse, lint, drift warnings, sync, save). No build. | opens VS |
| `sync libs <path>` | `libraries.xml` → PLC library references. | opens VS |
| `sync io <path>` | `io-devices.xml` → device/box/terminal tree, "Create PLC Data Type" `.tsproj` templates, and `<Links>`. Undeclared items are only **warned** about unless `--confirm-delete-io`. | opens VS, closes it for the `.tsproj` edit, reopens only if links need it |
| `sync events <path>` | `events.xml` + `event-classes/*.xml` → missing Event Classes written directly into the `.tsproj`. | **no VS at all** — pure file edit, the fastest mode |
| `sync variants <path>` | `variants.xml` → TwinCAT project variants and groups; switches on the PLC's variant defines (see [Machine variants](#machine-variants)). No manifest = nothing to do. | opens VS |
| `sync all <path>` | Every stage above, in order, then build. | opens VS |
| `activate <path> --target umrt\|local [--variant V] --yes` | Build one variant and activate it on UmRT_Default or this PC's runtime, then wait for the PLC to reach Run. Without `--yes` it only shows the target's current boot project (changes nothing). See [Closed loop on a runtime](#closed-loop-on-a-runtime). | opens VS (only with `--yes`) |
| `verify --target umrt\|local --expect <expect.json> [--variant V]` | Read live PLC values over ADS and compare them with `expect.json`. Exit 0 = all as expected. | no VS |
| `doctor` | Read-only report: DTE shells, TwinCAT version, local runtime and UmRT state, boot projects, license expiry. | no VS |
| `build <path> [--plc-name X] [--variant V \| --all-variants]` | Open, compile, report errors mapped back to `.st file:line` (or plain compiler output for a native, non-`.st`-managed project). A project with variants is built **once per variant** (see [Machine variants](#machine-variants)). **Exit code 0 = BUILD PASSED, 1 = failed or timed out.** `<path>` may be a `.tsproj` file directly, or a folder containing exactly one. | opens VS |

Stage execution order for `sync all`: variants → code → libs → io-tree → `.tsproj`
edits (io templates + events) → io links → build. Visual Studio is opened
lazily by the first stage that needs it and closed for the `.tsproj` edits.

> **Known gap:** unlike the old flat-flag CLI, `sync` runs exactly one mode
> per invocation — there's no current way to compose an arbitrary subset
> (e.g. the old `--sync-io --sync-events` in one run) other than `all`
> (every stage) or separate invocations (each reopening Visual Studio). Not
> a blocker for the common cases below, but a real regression if you relied
> on combining specific stages — see `docs/ideas/cli-subcommand-redesign.md`.

```powershell
# One-time setup, in isolation (no code sync, no build) — two invocations,
# each opening/closing Visual Studio once (see "Known gap" above):
.\beckhoffAutomationInterface.exe sync io "C:\...\ST\Shark" --dest "C:\...\TwinCAT"
.\beckhoffAutomationInterface.exe sync events "C:\...\ST\Shark" --dest "C:\...\TwinCAT"

# Only push ST code changes into the project, don't compile:
.\beckhoffAutomationInterface.exe sync code "C:\...\ST\Shark" --dest "C:\...\TwinCAT"

# CI pipeline: compile only, fail the job on errors. Works for a project
# this tool manages via .st source, or an arbitrary native one (no .st tree,
# no .sln) — either way, never bootstraps: a wrong path exits 1 instead of
# green-building nothing.
.\beckhoffAutomationInterface.exe build "C:\...\SomeProject"
if ($LASTEXITCODE -ne 0) { throw "PLC build failed" }
```

### Other flags

These apply within the subcommands noted; `--source`/`--dest`/`--name` and
`--tsproj`/`--plc-name` from the old flat-flag CLI are now expressed as the
positional path (see [Commands](#commands) above) plus `--project` for
`check events`/`export`.

| Flag | Applies to | Default | Purpose |
|---|---|---|---|
| `--dest <path>` | `sync`, `init` | `.` | Folder under which `<name>/<name>.sln` lives |
| `--name <name>` | `sync`, `init` | source path's folder name | Project/solution name |
| `--project <path>` | `check events`, `export *` | — | The existing `.tsproj` file, or a folder containing exactly one, to attach to |
| `--plc-name <name>` | `build`, `export *` | the `.tsproj`'s own base name | The real PLC project name inside `TIPC`, if it differs — check the project's own `.plcproj` file name if unsure |
| `--shell xae\|vs2022` | every command that opens TwinCAT | `xae` if installed, else `vs2022` | Which DTE shell to automate. `xae` = TwinCAT XAE shell (`TcXaeShell.DTE.17.0`, present on every TwinCAT engineering PC); `vs2022` = Visual Studio 2022 with TwinCAT integrated. The tool checks that the shell's exe exists before it starts. Also `shell=` in `.stconfig`. |
| `--clean` | `build`, `sync` | off | Clean the solution before the build stage, so no stale result can pass. Also `clean=true` in `.stconfig`. |
| `--ignore <glob>` | `sync`, `check` | none | Exclude `.st` files matching this glob pattern (repeatable). Merged with a `.stignore` file in the source folder, if present. |
| `--incremental` | `sync` | off | Sync only `.st` files changed/deleted since the last recorded sync (see below) instead of the whole source folder. Requires the source folder to be a git repo with a prior full sync's baseline. |
| `--confirm-delete-io` | `sync io`, `sync all` | off | Actually delete IO tree items not declared in `io-devices.xml`. Without it they are only warned about — never deleted. |
| `--overwrite` | `export *` | off | Allow reverse export to overwrite existing files in the source folder. Required when it already contains `.st` files or manifests (safe-by-default, CLI-only — never from `.stconfig`). |
| `--config <path>` | all | source path's folder | Look for a `.stconfig` defaults file in this folder instead (see below). |
| `--no-config` | all | off | Ignore any `.stconfig` defaults file for this run only. |

The single-object `--export <name>` legacy flag (write one live PLC object's
text back to `.st`) has no subcommand equivalent yet.

The `githooks/` incremental worker is unaffected by `init`: it always
targets an already-bootstrapped project (the `sync` reopen path).

### Default options (`.stconfig`)

Retyping `--dest`/`--name` (and other flags) on every invocation gets old
fast. Drop a `.stconfig` file at the **top level of your source folder** —
the same place a `.stignore` file already lives — and the tool loads it
automatically:

```
# ST\Shark\.stconfig
dest=C:\path\to\TwinCAT-projects-root
name=Shark
incremental=true
```

```powershell
.\beckhoffAutomationInterface.exe sync all "C:\path\to\ST\Shark"
# resolves dest/name/etc. from ST\Shark\.stconfig — only the source path still needed
```

**Discovery order:** `--config <path>` (look there instead), else the
resolved source path, else the process's current directory if neither is
given. Use `--config` to keep `.stconfig` somewhere other than inside the
source folder — that file can then set its own `source` key too, since at
that point the source path hasn't been resolved from the command line yet.

**Precedence:** an explicit command-line flag (or positional path) always
wins; `.stconfig` only fills in what you didn't type; anything neither
specifies falls back to today's hardcoded default (e.g. `.` for the source
path/`--dest`).

**Supported keys:** `source`, `dest`, `name`, `export`, `shell`, plus the boolean
flags `export-links`, `incremental`, `parse-only`, `format-check`,
`check-events`, `check-links`, `clean` (a value of `true`/`1`/`yes`, case-insensitive,
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
.\beckhoffAutomationInterface.exe sync all "C:\...\ST\Shark" --dest "C:\...\TwinCAT"

# Later runs, after committing .st changes: only re-syncs what changed
.\beckhoffAutomationInterface.exe sync all "C:\...\ST\Shark" --dest "C:\...\TwinCAT" --incremental
```

By default, `git diff`-reported deletions are only **warned** about, never
acted on — `.st-sync-state`'s baseline still advances, but the corresponding
PLC object(s) are left in place. Add `--confirm-delete` to actually remove
them:

```powershell
.\beckhoffAutomationInterface.exe sync all "C:\...\ST\Shark" --dest "C:\...\TwinCAT" --incremental --confirm-delete
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
`export object <ObjectName>` finds that live object, reads its current text,
and writes it to the correct mirrored `.st` file path (creating folders as
needed) — the read-side counterpart of the normal sync, using the same
`DeclarationText`/`ImplementationText` properties `PouSyncEngine` writes.

```powershell
.\beckhoffAutomationInterface.exe export object T_Beckhoff_AmbientSensor "C:\...\ST\Shark" --project "C:\...\TwinCAT\Shark\Shark.tsproj"
```

`export object` now supports every PLC object kind synced from `.st` source: DUTs
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

### Reverse export (adopting an existing project)

`export object <name>` above writes **one** object back to `.st`. `export
code|libs|io|events|all` is the whole-project version: point one at an
**existing** TwinCAT project (`--project`) and it regenerates the entire
source tree (the positional path), so you can start managing a project you
already have as git-tracked `.st` source. This is the reverse of the normal
sync — a **one-time bootstrap**, not the ongoing flow (once the tree exists,
edit `.st` and sync forward with `sync` as usual).

| Command | Generates |
|---|---|
| `export code` | every POU/DUT/GVL → mirrored `.st` files under the source path |
| `export libs` | library references → `libraries.xml` |
| `export io` | the `TIID` device/box/terminal tree → `io-devices.xml` |
| `export events` | `.tsproj` Event Classes → `events.xml` + `event-classes/*.xml` (no Visual Studio needed) |
| `export all` | all of the above **plus** `export links` (`links.xml`) |

```powershell
# One-time: bootstrap a fresh source tree from an existing project, then commit it.
.\beckhoffAutomationInterface.exe export all "C:\...\ST\Shark-new" --project "C:\...\TwinCAT\Shark\Shark.tsproj"
cd "C:\...\ST\Shark-new"; git init; git add -A; git commit -m "Import Shark from TwinCAT"
# From here on, edit .st and sync FORWARD as normal (see "Typical workflow").
```

**Adopting an ARBITRARY pre-existing project** (one this tool didn't bootstrap
itself) just needs `--project` pointed at the right place, since a real
project's PLC-project name (inside `TIPC`) commonly differs from its
`.tsproj`/solution file name, and it may not even have a `.sln` at all:

| Flag | Purpose |
|---|---|
| `--project <path>` | The `.tsproj` file (or a folder containing exactly one) to attach to (read-only, never saved) — no `--dest`/`--name`/`.sln` resolution involved. Works whether or not the project has a `.sln` at all. |
| `--plc-name <name>` | The real PLC-project name inside `TIPC`, if it differs from the `.tsproj` file's own base name — check the project's own `.plcproj` file name if unsure. |

```powershell
# A project with NO .sln at all (check its own .plcproj file name for --plc-name):
.\beckhoffAutomationInterface.exe export all "C:\...\ST\new" --project "C:\...\SomeProject\SomeProject.tsproj" --plc-name RealPlcProjectName

# A project that DOES have a matching .sln, but a differently-named PLC project inside:
.\beckhoffAutomationInterface.exe export all "C:\...\ST\new" --project "C:\...\Projects\SomeProject" --plc-name RealPlcProjectName
```

**`--project`/`--plc-name` also work with `build` alone — no `export`
required.** This is the path for CI compiling a PLC project that's natively
hosted (its own `.tsproj`/`.plcproj` checked into git directly, no `.st`
source tree, possibly no `.sln` at all) rather than one this tool manages:

```powershell
# CI: compile an arbitrary pre-existing project, no reverse export involved.
.\beckhoffAutomationInterface.exe build "C:\...\SomeProject" --plc-name RealPlcProjectName
if ($LASTEXITCODE -ne 0) { throw "PLC build failed" }
```

Both forms attach to the project **read-only** — no `Project.Save()`/
`Solution.SaveAs()` is ever called on this path, regardless of whether a
`.sln` already exists (confirmed live: hashing the `.tsproj`/`.sln` before and
after showed byte-identical content in both cases). One caveat found during
live validation: merely *opening* a project this way can still cause TwinCAT
itself to silently bump one auto-generated version-stamp field (e.g. a
`ProductVersion`/`ProgramVersion` attribute reflecting your installed TwinCAT
build) — confirmed cosmetic via `git diff` (no logic/content change) both
times it was observed. If the project is git-tracked, a quick `git status`/
`git diff` after a reverse-export run will show this clearly if it occurs, and
it's safe to `git checkout --` away.

**Safety — `--overwrite`:** to avoid clobbering hand-edited source, reverse
export **refuses** (exit 1) if the source path already contains any `.st`
files or manifests. Pass `--overwrite` to regenerate in place, or point the
source path at an empty folder. Like `--init`/`--confirm-delete*`,
`--overwrite`, `--project`, and `--plc-name` are all CLI-only and never read
from `.stconfig`. Reverse export also requires the project to already exist
(it never bootstraps — a missing project is a hard error, since there is
nothing to export from).

**Caveats:**
- The non-ASCII round-trip caveat above applies to `export code` too (it reuses
  the same per-object exporter).
- `export io` recovers each terminal's `Product` primarily from the trailing
  `"(Product)"` in its **Name** — TwinCAT's own default naming convention when
  a device is dragged from the ESI catalog (confirmed live against 30 real
  terminals, including non-Beckhoff/hyphenated catalog strings like a Festo
  `EX260-SEC1`, which a bare product-code pattern would truncate wrongly).
  Falls back to a pattern embedded in Name (TwinCAT's other auto-naming style,
  e.g. `EK1100_1.1`), then to `ItemSubTypeName` — see `Sync/IoManifestWriter.cs`.
  Anything not lifted from the Name parenthetical is listed as `! verify` in
  the output — **review those `io-devices.xml` `Product` attributes against
  the real hardware before relying on the manifest for a forward sync.**
- A library reference whose name isn't in Beckhoff's own `Tc<N>_*` namespace
  (e.g. a custom/third-party library) is left as an XML comment in
  `libraries.xml` for manual review rather than guessed, since its real
  Company/Version can't be recovered from what TwinCAT exposes.

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
`check links` (or, more lightly, every `check parse`) parses all `.st`
source, finds every
`AT %I*`/`AT %Q*` declaration in a **GVL or PROGRAM**, and cross-checks it
against `<Links>`:

```powershell
.\beckhoffAutomationInterface.exe check links "C:\...\ST\Shark"
```

```
2026-07-15 ...: [check-links] 34 declared, 0 linked, 34 unlinked, 0 stale link(s).
    ! UNLINKED PRG_DIGITAL_INPUT.IO_DoorLT (%I) — App/Shark/Programs/PRG_DIGITAL_INPUT.st
    ! UNLINKED GVL_Safety.SafetyOk (%I) — App/Shark/GVL_Safety.st
    ...
```

It reports two things: declared variables with **no matching `<Link>`**
(exit code 1 under `check links` if any exist — a stale-configuration gate
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

**`io-devices.xml`'s `<Links>` section is the recommended, permanent way to
declare links** — it's the whole reason `.st`/manifest source exists in this
project at all: plain text, hand-readable, git-diffable, easy to review in a
PR. `links.xml`, covered below, is a **discovery aid** for finding out a
real channel path you don't already know — not a second permanent source of
truth to maintain alongside it. Once you know the real `IoChannel`, write it
into `io-devices.xml` as a normal `<Link>` entry (see above) and treat
`links.xml` as disposable (it's `.gitignore`d by default).

**Recommended workflow when you don't yet know the real wiring:**
1. Link the variable to hardware once by hand in the TwinCAT IDE (drag the
   channel onto the variable in Solution Explorer, or right-click the
   variable → "Link To...") — this is where your knowledge of the actual
   panel wiring comes in; nothing here can be derived automatically.
2. Save, then run:
   ```powershell
   .\beckhoffAutomationInterface.exe export links "C:\...\ST\Shark" --project "C:\...\TwinCAT\Shark\Shark.tsproj"
   ```
   which writes the current mapping to `links.xml` via
   `ITcSysManager.ProduceMappingInfo` — the same thing "Export Variable
   Mapping" does.
3. Read the `IoChannel`-equivalent path back out of `links.xml` (each
   `<Link>`'s `OwnerB`/`VarB`) and transcribe it into a proper `<Link
   PlcVar="..." IoChannel="..."/>` entry in `io-devices.xml` — now it's
   readable, reviewable, and lives with the rest of the project's manifests.
4. Delete (or just leave — it's gitignored) `links.xml`.

If you have a whole batch of variables to link at once, skip step 3's
transcription and just keep `links.xml` as-is for that run — both
mechanisms coexist (`links.xml` applies first, then `io-devices.xml`'s
`<Links>` on top, neither replaces the other) — but transcribing back to
`io-devices.xml` is still worth doing before committing, for the same
readability reason.

**Schema, if you ever need to hand-author or read `links.xml` directly.**
Live testing (2026-07-15, TwinCAT 3.1) found `ConsumeMappingInfo` is picky
about the exact shape:
```xml
<VarLinks>
  <OwnerA Name="TIPC^Shark^Shark Instance">
    <OwnerB Name="TIID^Device 1 (EtherCAT)^Term 1 (EK1100)^Term 2 (EL1008)">
      <Link VarA="PlcTask Inputs^GVL_Shark.bMotorRunSensor" VarB="Channel 1^Input" />
    </OwnerB>
  </OwnerA>
</VarLinks>
```
— one `OwnerA` per PLC instance (both directions together, no `Type`
attribute), and the `PlcTask Inputs`/`PlcTask Outputs` group folded directly
into `VarA` (no separate `GrpA`) — confirmed round-trip-verified end to end
(linked, built cleanly, `BUILD PASSED`). A richer variant also exists in the
wild (`OwnerA` with separate `Prefix`/`Type`, `Link` with a separate `GrpA`
plus `TypeA`/`InOutA`/`GuidA` metadata — the real
`Spectrometer Instance Mappings.xml`'s own shape, and Beckhoff's official
`CodeGenerationDemo` sample's alternate shape) — `check links` parses
both, but only the shape above was confirmed to actually apply via
`ConsumeMappingInfo` in this environment; the richer one was tried first and
silently applied nothing (no COM exception, but an `export links`
round-trip came back empty). If in doubt, always prefer `export links`
over hand-authoring this file.

See [`links.xml.example`](links.xml.example) at the repo root for a fully
annotated template. `check links`/`check parse` also cross-reference
`links.xml` entries the same way they do `io-devices.xml`'s `<Links>` — a
`links.xml` entry whose variable is nested through a `FUNCTION_BLOCK`
instance (e.g. `MAIN.fbSpec.inLogicSig[1]`, real usage seen in that same
reference project) can't be statically verified either way, so it's
reported informationally as "unresolvable," never as unlinked or stale.

### Style checking

`check format` scans every non-ignored `.st` file under the source path and
prints a dry-run report of hygiene issues — it never modifies any file, and
needs no Visual Studio session at all (similar to `check parse`):

```powershell
.\beckhoffAutomationInterface.exe check format "C:\...\ST\Shark"
```

It checks for: mixed line endings (both CRLF and bare LF in the same file),
trailing whitespace, a missing trailing newline at end of file, and extra
blank lines at end of file. This is deliberately minimal — it does **not**
re-indent or otherwise rewrite code; a full auto-formatter remains a
possible future addition (see `docs/ideas/st-plc-bidirectional-sync.md`).

### Typical workflow

```powershell
# 0. First time only: bootstrap the TwinCAT solution + one-time project setup
.\beckhoffAutomationInterface.exe init "C:\...\ST\Shark" --dest "C:\...\TwinCAT"
.\beckhoffAutomationInterface.exe sync io "C:\...\ST\Shark" --dest "C:\...\TwinCAT"
.\beckhoffAutomationInterface.exe sync events "C:\...\ST\Shark" --dest "C:\...\TwinCAT"

# 1. Fast syntax check after editing .st files (no Visual Studio, ~instant)
.\beckhoffAutomationInterface.exe check parse "C:\...\ST\Shark"

# 2. Full sync + build (reopens and reconciles the existing project)
.\beckhoffAutomationInterface.exe sync all "C:\...\ST\Shark" --dest "C:\...\TwinCAT"

# 3. Fix compile errors reported, then iterate quickly without re-syncing
.\beckhoffAutomationInterface.exe build "C:\...\TwinCAT\Shark"
```

> **Safety note:** `--dest` + the project name determine exactly which
> `.sln`/`.tsproj` are targeted. Creating a NEW project always requires
> `init` (not `sync`) — a missing solution under `sync` is a hard error, so
> a mistyped path can never silently bootstrap (or bury a duplicate project
> inside) the wrong location.

## Machine variants

One PLC project can serve a family of machines with TwinCAT Variant Management
(TwinCAT 3.1.4024+). Declare the variants in **`variants.xml`** next to the other
manifests (example: [`ST/VariantSample`](ST/VariantSample)):

```xml
<Variants DefineMode="Implicit">
  <Variant Name="MachineA" />
  <Variant Name="MachineB" />
  <Group Name="PCV_MODBUS">   <Member>MachineA</Member> </Group>
  <Group Name="PCV_ETHERCAT"> <Member>MachineB</Member> </Group>
</Variants>
```

- `sync variants` (also part of `sync all`, first stage) writes the variants and groups into
  the project and switches on the PLC's variant defines. A second run reports "no change";
  a variant removed from the file is removed from the project.
- The PLC gets the **active variant's name and its group names** as compiler defines. Use a
  group as a feature switch: `{IF defined (PCV_MODBUS)}`. Keep machine names in one
  configuration GVL; test features everywhere else.
- Variant and group names must be valid PLC identifiers (they become defines).
- `build` builds **every** variant (clean each time), because the compiler does not check code in
  a branch that is not selected. `--variant <name>` builds one. The run ends with a summary
  and exits 1 if any variant fails.
- Each build prints the PLC's message pragmas, e.g. `{warning 'VARIANT CHECK: MachineA'}`, as
  `[PLC MESSAGE]` lines, so the log shows which branch was compiled.

## Closed loop on a runtime

Build, run and check a project without touching XAE by hand. Two targets only:
`umrt` (the User Mode Runtime `UmRT_Default` on this PC) and `local` (this PC's own runtime).

```powershell
.\beckhoffAutomationInterface.exe doctor                                             # state + license expiry
.\beckhoffAutomationInterface.exe activate C:\p\VariantSample --target umrt --variant MachineA --yes
.\beckhoffAutomationInterface.exe verify --target umrt --expect ST\VariantSample\expect.json --variant MachineA
```

- **`activate` replaces the target's boot project.** Without `--yes` it prints what is on the
  target now and stops. With `--yes`: select the variant → set the target → boot project
  autostart → clean + build → activate → restart → wait for the PLC to reach Run. A failed build
  stops before activation, so the running program stays.
- **The project's own files are never written.** `build <path>` and `activate` work on a
  temporary **shadow copy** of the project folder (deleted afterwards), because TwinCAT saves the
  project by itself during a variant build and during activation. Error locations are reported
  in the original project.
- **UmRT has no EtherCAT adapter.** With `--target umrt`, `activate` disables the I/O devices in
  the shadow copy, so the PLC logic runs and can be verified. Real I/O needs the real machine.
- **`expect.json`** lists, per variant, the live values that prove the right program runs
  (example: [`ST/VariantSample/expect.json`](ST/VariantSample/expect.json)):
  - `"equals"` — a value (strings, BOOL, numbers; `"tolerance"` for REAL/LREAL)
  - `"increases"` — a counter that must move (read twice, 0.5 s apart)
  - `"absentOrZero"` — code that must not run: not linked into the program, or 0
- **If the PLC never reaches Run** and TwinCAT stays in Config mode, `verify`/`activate` check the
  target's license: missing or expired → that is the cause; valid → a device the target cannot
  start (or another configuration error). `doctor` shows the expiry date. A 7-day trial license
  must be renewed in XAE by a person (it asks for a security code).

**End-to-end check:** [`e2e/variant-sample.ps1`](e2e/variant-sample.ps1) runs the whole loop on
`ST/VariantSample` — init, build all variants, then for each variant activate, verify it, and
verify the other one (which must fail) — and compares every exit code with the expected one.

## Manifests (config data, not `.st` source)

Alongside the `.st` files, a project folder can contain:

- **`libraries.xml`** — PLC library references (`<Library Name="Tc2_Standard" Version="*" Company="Beckhoff Automation GmbH" />`)
- **`io-devices.xml`** — EtherCAT I/O hardware tree (`<Device>`/`<Box>`/`<Terminal>`) plus optional `<Links>` mapping PLC variables to I/O channels
- **`variants.xml`** — TwinCAT project variants and groups (see [Machine variants](#machine-variants))
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
  `check events` still exists as a fast preflight/pipeline gate.
- **`MDP5001_*` PLC data type names are config-hash suffixes** TwinCAT
  computes from a terminal's PDO/revision configuration — they are NOT
  portable across machines whose ESI catalogs instantiate different terminal
  revisions. If `.st` aliases report `Unknown type` for one, run `build`
  once and read the actual generated `MDP5001_*` names out of the saved
  `.tsproj`, then update the aliases and `plc-data-types/*.xml` template.
- **IO variable linking** requires the PLC instance's I/O image to exist,
  which normally only materializes after *Activate Configuration* against a
  real or simulated target. If a declared link can't be resolved, the tool
  falls back to disabling the affected EtherCAT master so the build still
  passes unattended.

## Troubleshooting

- **A `TcXaeShell.exe` / `devenv.exe` process is left running after a crash/interrupt:** the
  tool always tries to close the shell in a `finally`/`Dispose`, but if
  you kill the tool itself mid-run, close it manually: `Get-Process TcXaeShell,devenv | Stop-Process -Force`.
  Note: piping the tool's output into PowerShell's `Select-Object -First N` kills the tool
  early (and leaves the shell running) — capture the output first, then filter it.
- **`Class not registered` when the DTE is created:** the requested shell's ProgID is
  registered but its class or exe is not (seen with `VisualStudio.DTE.17.0` on XAE-only PCs).
  Use the default (`--shell xae`); the tool prints which shell and exe it uses.
- **No Yes/No dialogs:** every run turns on TwinCAT's silent mode
  (`ITcAutomationSettings.SilentMode`), so TwinCAT answers its own questions with the default
  (for example "Target System Process Image Update"). Your previous setting is restored on
  exit, so your interactive XAE still asks. `DialogAutoDismisser` stays as a safety net for
  Visual Studio message boxes such as "…`.tmc` not found".
- **`RPC_E_SERVERCALL_RETRYLATER` / random COM failures:** these are
  automatically retried internally; if you still see one bubble up, an
  existing lingering `devenv.exe` from a previous run is the usual cause —
  close it and retry.
- See [beckhoffAutomationInterface/REQUIREMENTS.md](beckhoffAutomationInterface/REQUIREMENTS.md)
  for COM-registration and environment verification commands if the tool
  fails before ever reaching your `.st` sources.
