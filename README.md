# bai: Beckhoff Automation Interface tool

`bai` (`beckhoffAutomationInterface.exe`) is a command-line tool for TwinCAT 3 PLC projects.
You keep the PLC code as `.st` text files and XML manifests in git. The tool does these tasks:

- It puts the `.st` files and manifests into a TwinCAT project (`init`, `sync`).
- It compiles the project and maps each error to the `.st` file and line (`build`).
- It writes `.st` files and manifests from an existing project (`export`).
- It activates a project on a runtime and reads live PLC values over ADS (`activate`, `verify`).
- It runs test scenarios against a PLC in simulation (`test`).
- It makes and checks offline release artifacts (`package`, `libraries`, `toolchain`, `esi`).
- It makes a release bundle and installs a compiled package with rollback (`release`, `package install`).

The tool uses the TwinCAT Automation Interface (COM). You do not use the XAE window. The tool
never shows a Yes/No dialog box and never waits for a person.

## Contents

1. [Install and build](#1-install-and-build)
2. [Words](#2-words)
3. [Quick start](#3-quick-start)
4. [Command reference](#4-command-reference)
5. [The source folder](#5-the-source-folder)
6. [Manifest files](#6-manifest-files)
7. [Runtime test files](#7-runtime-test-files)
8. [Limits](#8-limits)
9. [Problems and fixes](#9-problems-and-fixes)
10. [Test the tool](#10-test-the-tool)

## 1. Install and build

### Requirements

| Item | Requirement |
|---|---|
| Operating system | Windows 10 or 11. COM automation works only on Windows. |
| TwinCAT | TwinCAT 3.1 XAE, build 4024 or later (4026 recommended). Variants need 4024 or later. |
| DTE shell | The TwinCAT XAE shell (default). Visual Studio 2022 (17.x) with TwinCAT integration also works (`--shell vs2022`). Visual Studio 2026 is not supported. |
| .NET | .NET SDK 6.0 or later to build. The tool targets .NET Framework 4.8, x64. |
| Runtime commands | `activate`, `verify`, `test` and `runtime-state` need a TwinCAT runtime: UmRT_Default on this PC, the local runtime, or an AMS Net ID. UmRT needs a license. |

If you install Visual Studio 2022 after TwinCAT, repair the TwinCAT integration (as Administrator):

```powershell
MsiExec.exe /f{23005E9B-9FED-4C05-B4EB-6AC0ECC0BA7F}
```

The repository contains `Interop.TCatSysManager.dll` (namespace `Interop.TCatSysManager`). To make
it again, use `TlbImp.exe` from the Windows SDK:

```powershell
TlbImp.exe "C:\Program Files (x86)\Beckhoff\TwinCAT\3.1\Components\Base\TypeLib\3.4\TCatSysManager.tlb" /out:Interop.TCatSysManager.dll
```

### Build and unit tests

```powershell
dotnet build beckhoffAutomationInterface -c Debug -f net48
dotnet test beckhoffAutomationInterface.Tests
```

The tool is `beckhoffAutomationInterface\bin\Debug\net48\beckhoffAutomationInterface.exe`.
It is a standalone `.exe`. In this README, the short name is `bai`:

```powershell
Set-Alias bai "C:\path\to\beckhoffAutomationInterface\bin\Debug\net48\beckhoffAutomationInterface.exe"
bai --help
bai doctor
```

`doctor` shows the DTE shells, the TwinCAT version, the state of each runtime, the boot projects
and the license expiry dates. It changes nothing.

## 2. Words

| Word | Meaning |
|---|---|
| Source folder | The folder with the `.st` files and the manifests. Example: `ST\sample`. |
| Project | A TwinCAT solution that `init` made: `<folder>\<name>\<name>.sln`, with the `.tsproj` in `<folder>\<name>\<name>\`. |
| Adopted project | A TwinCAT project that the tool did not make. `build`, `export`, `activate` and `check events` open it read-only and never save it. `sync` refuses it. |
| Variant | One machine of a machine family. One project can hold all variants (TwinCAT variant management). |
| Feature group | A named set of variants in `variants.xml`. It becomes a PLC compiler define. |
| Target | The runtime that runs the PLC: `umrt` (UmRT_Default on this PC), `local` (the TwinCAT runtime of this PC), or an AMS Net ID. |

## 3. Quick start

Try the tool on the small sample in `ST\sample` first.

```powershell
bai check parse ST\sample                            # read all .st files (no TwinCAT, seconds)
bai init ST\sample --into C:\bai\work                # make C:\bai\work\sample\sample.sln, sync all, build
# change .st files, then:
bai sync all ST\sample --project C:\bai\work\sample  # send the changes, then build
bai build C:\bai\work\sample                         # build only
```

A second `sync` with no changes reports `0 created, 0 updated, 0 deleted`.

## 4. Command reference

Type `bai --help` for commands grouped by workflow. Each command and subcommand has
help with usage, option descriptions, examples, and exit codes:

```powershell
bai build --help
bai package verify --help
bai help machine test
bai --version
```

Help works without opening TwinCAT or reading project files. `help` and `--help` are
equivalent; `-h` is also accepted. The examples use the `bai` alias from the installation
section; the executable remains `beckhoffAutomationInterface.exe`.

Help wraps to the terminal width (80 columns when redirected). Text reports use plain
headings and aligned fields without terminal escape codes. Paths and hashes are kept
intact for copying. Artifact commands that support `--json` emit only JSON on standard
output when that flag is supplied; use it for scripts. Native configuration commands
continue to emit JSON. Command-line errors go to standard error with the relevant usage
and a command-specific help hint.

### Global options

| Option | Commands | Meaning |
|---|---|---|
| `--shell xae\|vs2022` | each command that opens TwinCAT | The DTE shell. Default: the XAE shell if it is installed, else Visual Studio 2022. Also `shell=` in `.stconfig`. |
| `--config <folder>` | `init`, `sync`, `build`, `check`, `export` | Read `.stconfig` from this folder. |
| `--no-config` | `init`, `sync`, `build`, `check`, `export` | Do not read `.stconfig`. |
| `--help`, `-h` | all | Show the usage. Exit code 0. |
| `--version` | top level | Print the tool version and exit. |

`bai` with no arguments shows the usage and gives exit code 1.

### Exit codes

| Code | Meaning |
|---|---|
| 0 | The command is done and the result is as expected (`BUILD PASSED`, `VERIFY PASSED`, `TEST PASSED`, no problem found). |
| 1 | A problem: compile error, failed check, failed scenario, missing project, wrong command line (most commands), or `activate`/`runtime-state --state` without `--yes`. |
| 2 | Only `test`, `machine`, `native`, `release`, `package`, `libraries`, `toolchain`, `esi`: the command line or an input file is not correct. Nothing was changed. |
| 3 | Only `machine test`: another run holds the UmRT lock. Nothing was changed. |

### `init`: make a new project

```
bai init <source> --into <folder> [--name <name>] [--plc <name>] [--platform <name>] [--ignore <glob>]...
```

- Makes `<folder>\<name>\<name>.sln` with a TwinCAT project and a PLC project. Then it runs
  `sync all` (all stages and a build).
- `--name`: the project name. Default: the name of the source folder.
- `--plc`: the PLC project name, if it must be different from `--name`.
- The PLC project name must contain only letters, digits and `_`. The tool refuses a name like
  `my-plc` before TwinCAT starts.
- `--platform`: `TwinCAT RT (x86)`, `TwinCAT RT (x64)` or `TwinCAT OS (x64)`.
- Only `init` can make a project. All other commands refuse a project that does not exist.

### `sync`: send the source to the project

```
bai sync code|libs|io|events|variants|safety|tasks|all <source> --project <project>
         [--clean] [--confirm-delete-io] [--ignore <glob>]... [--variant <name> | --all-variants] [--platform <name>]
```

`--project` is the `.sln` that `init` made, its folder, or its `.tsproj`. `sync` accepts only a
project that `init` made.

| Mode | What it sends | Opens TwinCAT |
|---|---|---|
| `code` | `.st` files to POUs, DUTs and GVLs. No build. | Yes |
| `libs` | `libraries.xml` to library references | Yes |
| `io` | `io-devices.xml` to the I/O tree, PLC data types, CoE and PDO settings, and links | Yes |
| `events` | `events.xml` to event classes (direct `.tsproj` edit) | No |
| `variants` | `variants.xml` to project variants and groups | Yes |
| `safety` | `safety.xml` to the TwinSAFE project | Yes |
| `tasks` | `tasks.xml` to real-time tasks and PLC task calls | Yes |
| `all` | All stages, then a build | Yes |

Order of `sync all`: variants, code, libraries, tasks, I/O tree, `.tsproj` edits (I/O templates
and events), safety, I/O links, build. A missing manifest means "nothing to do" for its stage.

| Option | Meaning |
|---|---|
| `--clean` | Clean the solution before the build. Also `clean=true` in `.stconfig`. |
| `--confirm-delete-io` | Delete I/O items that `io-devices.xml` does not declare. Without it, the tool only shows a warning. It also lets the tool delete and make again a device of the wrong network layout. |
| `--ignore <glob>` | Skip `.st` files that match. You can use it more than one time. |
| `--variant`, `--all-variants` | The variants to build at the end (see `build`). |
| `--platform` | The build platform (see `init`). |
| `--confirm-delete` | Accepted, but has no effect. Scripts of the PLC repository still pass it. |

What `sync code` does with old objects:

- It never deletes a PLC object, METHOD or PROPERTY. If the `.st` source of a synced name is
  gone, it shows a `[drift]` warning with the name. Delete the old object in TwinCAT. The list
  of synced names is in `.st-known-names` in the source folder.
- If two `.st` files define the same object, it stops before it changes anything.

### `build`: compile

```
bai build <project> [--plc <name>] [--clean] [--variant <name> | --all-variants] [--platform <name>] [--define <NAME>]...
```

- `<project>`: a `.tsproj`, a `.sln`, or a folder with exactly one `.tsproj`. It can be an
  adopted project.
- `--plc`: the PLC project name in `TIPC`, if it is not the `.tsproj` name. The name of the
  `.plcproj` file is the PLC project name.
- A project with variants: the tool builds each variant one after the other, with a clean. It
  shows one result for each variant and a summary. `--variant <name>` builds one variant.
- `--define NAME`: add a PLC compiler define for this build only (for example `SIMULATION`).
  You can use it more than one time. `sync` and `init` do not accept it, because they save the
  project.
- The tool builds a temporary copy of the project folder. Your project files do not change.
- Each error shows the `.st` file and line. For a project without `.st` source, it shows the
  compiler text.
- A build that does not finish in 15 minutes stops with `TIMEOUT`. The tool closes the shell.
- Exit code 0 = `BUILD PASSED`. Exit code 1 = errors, a failed variant, or a timeout.

### `check`: fast checks without TwinCAT

```
bai check parse|format <source> [--ignore <glob>]...
bai check events <source> --project <project>
```

| Mode | What it does | Exit code 1 when |
|---|---|---|
| `parse` | Reads all `.st` files. Shows parse errors, objects in two files, naming warnings, and the link report (`[check-links]`: each `AT %I*`/`%Q*` variable of a GVL or PROGRAM against the links in `io-devices.xml` and `links.xml`). | a file cannot be parsed, or one object is in two files. Unlinked variables do not change the exit code. |
| `format` | Shows mixed line endings, spaces at line end, a missing newline at file end, and empty lines at file end. It changes no file. | never: the exit code is always 0 (report only) |
| `events` | Compares the event classes of `events.xml` with the `.tsproj`. | an event class is missing |

Naming warnings (never errors): `FB_` (function block), `PRG_` (program), `I_` (interface),
`F_` (function), `E_` (enum), `ST_` (struct), `T_` (alias), `GVL_` (global variable list).
METHODs and PROPERTYs are not checked.

### `export`: write source from a project

```
bai export code|libs|io|events|tasks|safety|variants|native|links|all <source> --project <project> [--plc <name>] [--overwrite]
bai export object <ObjectName> <source> --project <project> [--plc <name>]
```

| Mode | What it writes into `<source>` |
|---|---|
| `code` | All POUs, DUTs and GVLs as `.st` files, in the folders of the PLC project |
| `libs` | `libraries.xml`. A library outside the Beckhoff `Tc<N>_*` names becomes an XML comment for review. |
| `io` | `io-devices.xml`: devices, adapters, boxes, CoE startup lists and PDO configuration. Lines marked `! verify` need a check of `Product` against the real hardware. |
| `events` | `events.xml` and `event-classes\*.xml` (no TwinCAT window) |
| `tasks` | `tasks.xml` |
| `safety` | `safety.xml` and a byte-equal copy of the safety project in `safety\<Name>\` |
| `variants` | `variants.xml` |
| `native` | `native-project.zip`: a verified snapshot of the native project files |
| `links` | `links.xml`: the current variable links |
| `all` | All of the above |
| `object <Name>` | One object (with its METHODs and PROPERTYs) as one `.st` file |

- The tool opens the project read-only and never saves it. TwinCAT can change a version stamp
  in the `.tsproj` when it opens it. This change has no effect; `git checkout` removes it.
- If `<source>` already has `.st` files or manifests, the tool refuses. Add `--overwrite`.
- Characters that are not ASCII can change in a round trip, because TwinCAT keeps POU text in
  a legacy code page.
- After the export, commit the folder to git and use `sync`. To rebuild an exported project
  with links, keep the PLC instance name: `links.xml` contains it.

### `activate`: run a project on a target

```
bai activate <project> --target umrt|local|<AMS-Net-ID> [--variant <name>] [--plc <name>] [--define <NAME>]... [--timeout <s>] [--yes]
```

**CAUTION: Activation replaces the boot project of the target.**

- Without `--yes`, the tool shows the boot project that is on the target now. It changes nothing
  and gives exit code 1.
- With `--yes`, the tool selects the variant, sets the target, builds, activates, restarts
  TwinCAT and waits until the PLC is in Run (`--timeout`, default 60 s). A failed build stops
  before the activation, so the running program stays.
- The tool works on a temporary copy. Your project files do not change.
- On `umrt`, the tool disables the EtherCAT devices, because UmRT has no EtherCAT adapter.
- `--define SIMULATION` starts the PLC in simulation for a test run.
- If the PLC stays in Config, the tool checks the license of the target and tells you if it
  is the cause.

### `verify`: check live values once

```
bai verify --target umrt|local|<AMS-Net-ID> --expect <expect.json> [--variant <name>] [--timeout <s>]
```

The tool waits until the PLC is in Run (`--timeout`, default 60 s). Then it reads the values of
`expect.json` (section 7) over ADS. Each check shows `OK` or `FAIL`, the actual value and the
expected value. Exit code 0 = `VERIFY PASSED`. It needs no TwinCAT window.

### `test`: run scenarios

```
bai test --target umrt|local|<AMS-Net-ID> --scenarios <file-or-folder> [--variant <name>]
         [--variants <variants.xml> | --groups <G1,G2>] [--tags <a,b>] [--junit <file>] [--timeout <s>]
```

**CAUTION: A scenario sends real commands to the PLC. Use `test` only on a PLC in simulation.**

| Option | Meaning |
|---|---|
| `--scenarios` | One scenario file, or a folder. The tool reads all `*.json` files in the folder and its subfolders. |
| `--variants`, `--variant` | Read the feature groups of the variant from `variants.xml`. If the file has one variant, `--variant` is not necessary. |
| `--groups` | The feature groups, for example `PCV_MODBUS,SPECTROMETER_PLC`. Use it instead of `--variants`. |
| `--tags` | Run only the scenarios that have one of these tags. |
| `--junit` | Write the results as JUnit XML (one test case for each scenario). |
| `--timeout` | The time for the PLC to go to Run, and for the machine to become idle before each scenario. Default: 60 s. |

What the tool does:

1. It reads all scenario files first. If a file is not correct, it shows `file: step n:` and the
   problem. It sends nothing (exit code 2).
2. It skips a scenario if the machine does not have all its `requires` groups. Without
   `--variants` or `--groups`, it skips each scenario that has `requires`.
3. Before each scenario, it requires `GVL_Simulation.bActive = TRUE` and a readable
   `GVL_Simulation.bReset`. It waits until no sequence runs (`eActiveSequence` = 0 or 99). It
   sends ErrorAcknowledge (9999), sets `bReset`, waits until `bReset` is FALSE, and waits for
   `eActiveSequence` = 0.
4. It runs the steps and stops the scenario at the first failure. It keeps the value before each
   `write` step and writes it back after the scenario. It stops a failed scenario, or a sequence
   that still runs, with CancelProcess (1). It records the diagnostics before this cleanup.
5. It runs the optional `cleanup` steps last, also after a failure, with their own `--timeout`.
   A cleanup failure fails the scenario. After a failed cleanup or restore, the tool sends no
   more commands: the remaining scenarios fail.
6. It shows one line for each scenario (`PASS`, `FAIL` or `SKIP`) and a summary. For a failure,
   it shows the step, the symbol, the expected and actual value, and `eActiveSequence`, `sStatus`
   and `stLastError` of `PRG_Sequence.fbOrchestrator`.

Exit code: 0 = at least one scenario passed and none failed (`TEST PASSED`). 1 = a scenario
failed, no scenario ran, or the PLC is not in Run. 2 = wrong command line or scenario file.
The tool cannot recover from a lost PLC connection. Check the runtime before you start again.

### `runtime-state`: read or change the runtime state

```
bai runtime-state --target umrt|local|<AMS-Net-ID> [--expect config|run] [--state config|run --yes]
```

- No option: show the state (Config or Run). Exit code 0.
- `--expect config|run`: exit code 0 if the runtime is in that state, else 1.
- `--state config|run --yes`: switch the runtime and wait until it is in that state (45 s
  maximum). Without `--yes`, it changes nothing and gives exit code 1.
- It uses the system service over ADS. It needs no TwinCAT window and no compiler.

### `doctor`: show the PC state

```
bai doctor
```

It shows the DTE shells, the TwinCAT version, the state of the local runtime and UmRT, the boot
projects and the license expiry dates. It changes nothing. Exit code 0.

### `machine build` and `machine test`: the machines of a PLC repository

```
bai machine build [<Machine>...] [--fresh] [--release] [--simulation] [--parallel] [--platform <name>] [--manifest <file>] [--work <folder>]
bai machine test  [<Machine>...] [--fresh] [--tags <a,b>] [--junit <file>] [--timeout <s>] [--manifest <file>] [--work <folder>]
```

Run these commands in a PLC repository that has a `bai.machines.json` file. The tool finds the
file in the current folder or in a parent folder (or use `--manifest`). No machine name = all
machines of the file.

```json
{ "work": "C:/bai/work", "lock": "C:/bai/lock/umrt.lock",
  "preCheck": ["python", "Tools/check_interlock_sources.py"],
  "buildInputs": ["Lib", "App", "Machines", "Safety", "Tools"],
  "families": { "NFLite": {
      "plc": "NFLite", "platform": "TwinCAT RT (x64)", "sources": ["Lib", "App/NFLite"],
      "shared": "Machines/NFLite", "buildInfo": "App/NFLite/Version/GVL_BuildInfo.st",
      "tests": "Tests/Integration/NFLite", "machines": ["Shark", "UVM", "SAS", "Google"],
      "simulationGas": { "provenance": "Machines/simulation-models.provenance.json", "program": "PRG_GAS_SIMULATION",
                         "cycleTimeUs": 100000, "generator": ["python", "Tools/generate_app_gas_model.py", "{model}",
                         "App/NFLite", "{src}/App/NFLite/Simulation/GVL_SimulationGasModel.st"] } } } }
```

`machine build` does these steps for each machine:

1. It puts the source folder together in `<work>\<family>\<Machine>\src`: the `sources`, the
   shared manifests of `shared` (`libraries.xml`, `events.xml`, `event-classes\`,
   `plc-data-types\`), and the files of `shared\<Machine>\`: `variants.xml`, `io-devices.xml`
   (with its `Base` file and the `Set`/`Replace`/`Add` changes resolved, and `${PLC_INSTANCE}`
   replaced), `tasks.xml`, `safety.xml` with its TwinSAFE files (byte for byte), and the
   simulation gas model (`simulation-gas.json`, checked against the provenance file).
2. It writes the build identity (git tag, commit, dirty flag, machine, UTC time) into the copy
   of `buildInfo`. The file in git does not change.
3. It reads all `.st` files (as `check parse`). A parse error stops the machine before TwinCAT starts.
4. In ONE TwinCAT session, it makes a new project (`init`) or syncs the kept project, and builds
   the variant of the machine. Each declared link must resolve.

The project is kept between runs. The tool builds a fresh project when you give `--fresh`, when
a manifest changed, when a `.st` file or an object was deleted or renamed, when the kept project
has I/O items that the manifest does not have, or when the last run did not end. When the shell
crashes or stops (RPC error or time-out), the tool keeps the failed folder in
`<work>\<family>\_failed\` and builds once more, fresh. A compile error is not tried again.

| Option | Meaning |
|---|---|
| `--simulation` | Build with the compiler define `SIMULATION`, in `<work>\<family>\<Machine>-sim`. |
| `--release` | A build for a real machine: the tree must be clean, `HEAD` must have a tag, the manifests must not have `SIMULATION` or a test adapter address. Always fresh. A refused release gives exit code 2. |
| `--parallel` | Build two machines at the same time (one process and one shell for each machine, about 1 GB RAM each). |

`machine test` holds the lock file (one run on UmRT_Default at a time), runs `preCheck` once,
then for each machine: `machine build --simulation`, activation on UmRT_Default in the same
TwinCAT session (no second shell), `verify` with the machine's `expect.json` and the compiled
build identity, and `test` with the scenarios of `tests` and the feature groups of the machine.
The JUnit result is `<work>\<family>\<Machine>-sim\test-results.xml`. With `--junit <file>` and
more than one machine, the tool adds the machine name to the file name. When all steps pass,
the tool writes `validation.json` (and its `.sha256`) in the same folder: the scenarios with
their hashes, the compiled identity, and the hashes of the TMC and of bai. If a scenario file
changes during the run, the test fails.

**CAUTION: `machine test` replaces the boot project of UmRT_Default.**

#### A native family (`"kind": "native"`)

A native family has one machine. Its I/O tree, NC axes, data types and mappings are not
manifests. They are one native configuration from a reviewed project. Its machine folder is the
`shared` folder (no `variants.xml`; `expect.json` is optional).

```json
"NFPro": { "kind": "native", "plc": "NFPro", "platform": "TwinCAT OS (x64)", "sources": ["Lib", "App/NFPro"],
  "shared": "Machines/NFPro", "buildInfo": "App/NFPro/Version/GVL_BuildInfo.st", "tests": "Tests/Integration/NFPro",
  "machines": ["NFPro"],
  "native": { "configuration": "Machines/NFPro/native/configuration.xml", "safety": "Safety/NFPro/Nanofabricator_Safety",
              "runtimeLicenses": ["TC3 NC PTP", "TC3 NC PTP Axis"] } }
```

There is one build: the full native project. A fresh build makes an empty project with one
PLC, copies the `safety` folder byte for byte next to the `.tsproj`, and applies the
configuration to the closed `.tsproj`. Then the normal session syncs the source and applies the
mappings (`links.xml`) after the PLC compile. All links must read back. After XAE closes, the
safety files must be byte-equal to the source.

- `machine build NFPro`: the project for the machine (release platform). No NC axis may have
  `SimulationMode`; the build fails if one has it.
- `machine build NFPro --simulation` and `machine test NFPro`: the same project in the `-sim`
  folder, with `SIMULATION` and `SimulationMode="1"` on each NC axis (simulated drive and
  encoder). `machine test` activates it on UmRT_Default with the I/O disabled. **Never put the
  `-sim` project on a machine.**
- Before `machine test` builds, it checks that UmRT_Default has each license of
  `runtimeLicenses` and that it has not expired. The folder is
  `C:\ProgramData\Beckhoff\TwinCAT\3.1\Runtimes\UmRT_Default\3.1\Target\License`. A 7-day trial
  file only in the general TwinCAT license folder is not enough.
- A kept project is used again only when the configuration, the safety files and the other
  manifests did not change.

`native` works on the configuration file without TwinCAT (same rules and same bytes as the
Python tool `native_configuration.py` that it replaces):

```
bai native capture --project <reviewed tsproj> --output <new folder>     # configuration.xml + provenance.json
bai native apply   --configuration <configuration.xml> --project <closed tsproj>
bai native links   --configuration <configuration.xml> --plc <PLC name> --output <links.xml>
```

`apply` keeps the original as `<tsproj>.before-native`. It changes only the PLC instance prefix.

The log of each machine is `<work>\<family>\<Machine>[-sim].log`. The log of the run before is
`<Machine>[-sim].previous.log`.

Exit codes: 0 = all machines passed all steps, 1 = a step failed, 2 = wrong command line,
manifest or machine name, or a refused release (nothing was built), 3 = another run holds the
UmRT lock.

### Release artifacts: `package`, `libraries`, `toolchain`, `esi`

These commands work offline. They need no XAE and no runtime. Add `--json` for a result in
JSON. A new archive or baseline file never overwrites an existing file.

```
bai package create --directory <payload> --metadata <json> --output <new-zip> [--json]
bai package verify --package <zip> [--sha256 <hash>] [--json]
bai libraries lock --project <tsproj> --machine <label> --platform <platform> [--variant <name>] [--manifest <libraries.xml>] --output <new-json>
bai libraries verify --project <tsproj> --machine <label> --platform <platform> [--variant <name>] [--manifest <libraries.xml>] --lock <json> --sha256 <hash> [--json]
bai toolchain capture --components <folder> --xae-shell <folder> --version <version> --archive <new-zip> [--json]
bai toolchain verify --archive <zip> --sha256 <hash> --version <version> [--installed] [--json]
bai esi install|verify --system-directory <folder> --devices <folder> --user-directory <folder> [--report <new-json>] [--json]
```

| Command | What it does |
|---|---|
| `package create` | Makes a PLC package ZIP from a payload folder (`boot\`, `project.zip`, `evidence.json`, and `identity.expect.json` for schema 2). The metadata JSON has the package fields but no `files` field. `status` must be `Packaged` and `deploymentApproved` must be false. The tool writes the inventory and `release.json`, then verifies the package. |
| `package verify` | Checks a schema 1 or schema 2 package: the inventory, sizes, SHA256 hashes, boot files and identity data. It refuses unlisted files, duplicate names, links and unsafe paths. Without `--sha256`, it checks only the internal integrity. A pass is not a deployment approval. |
| `libraries lock` | Reads the compiled TMC (platform and variant) and records all `_Libraries` files, also the dependencies, in a new lock file. The project must have one PLC. Omit `--variant` for a project with no variants. `--manifest` also checks the names and versions of `libraries.xml`. |
| `libraries verify` | Compares the project with an approved lock file. Never make an approved lock file again only to pass a check. |
| `toolchain capture` | Archives the TwinCAT `Components` folder and the XAE shell folder. Give the real folders; the tool does not guess. The archive does not install Windows parts, register COM or supply licenses. |
| `toolchain verify` | Checks an archive. `--installed` also compares it with the folders on this PC. |
| `esi install` | Copies ESI catalog files into the system and user folders. It checks all conflicts first and never replaces a different file. Open a new XAE session after the install. |
| `esi verify` | Checks the catalog folders. It writes no file. |

Exit code: 0 = pass, 1 = a check or file operation failed, 2 = wrong arguments.

### `release` and `package install`: make a release and update a machine

```
bai release build --machines <M[,M]> [--tag <tag> | --working-tree] [--bundle <new-folder>]
    --bai-sha256 <hash> --twincat-version <major> [--toolchain <zip> --toolchain-sha256 <hash>]
    --library-lock <json> --library-lock-sha256 <hash> [--platform <name>] [--manifest <file>]
    [--esi-directory <folder>] [--user-esi-directory <folder>]
bai release lock --machines <M[,M]> --twincat-version <major> --output <new-json> [--work <folder>] [--platform <name>]
bai package preflight --current <zip> --current-sha256 <hash> --service-project <folder> --report <new-folder>
    [--target local|umrt|<AMS-Net-ID>] [--rehearsal]
bai package install --package <zip> --sha256 <hash> --current <zip> --current-sha256 <hash>
    --service-project <folder> --evidence <new-folder> [--target local|umrt] [--rehearsal] --yes
bai package install ... --target <AMS-Net-ID> --ssh <user@host> --yes
bai package drift --baseline <project.zip> --sha256 <hash> --project <folder> --report <new-json>
```

Run `release` in the PLC repository. It reads `bai.machines.json`.

| Command | What it does |
|---|---|
| `release build` | Makes a release bundle in a new folder. The default folder is `<work>\release\<machines>-<version>[-dirty]-<time>`. With `--tag`, the tag must be on `HEAD`. Without `--tag`, the version is `git describe`, and `HEAD` must be on the server (push it first). Without `--working-tree`, the repository must be clean. `--working-tree` also builds the uncommitted and untracked files of the build inputs and keeps `working-tree.patch`. This gives a `DevelopmentValidation` package: use it only for a rehearsal on UmRT. The tool checks its own SHA256, the library lock and the release manifests. `--twincat-version` is the TwinCAT major, for example `3.1.4026` (a build such as `3.1.4026.24` also works; only `3.1.4026` is compared): the project and the library lock must have that major, any build. `release.json` records the build of each project. The library lock still compares each compiled library byte by byte, so other library versions need a new, examined lock. `--toolchain` is optional: with it, the tool also checks that archive against the installed TwinCAT files (before and after the build) and keeps it in the bundle. It copies the source, the tool and the ESI files into the bundle. The tool copy builds each machine with the `machine build --release` steps (always fresh, no SIMULATION). Then the tool checks the compiled project and writes `packages\<M>.zip` and `<M>.zip.sha256`. It never activates. When a step fails, `release.json` has status `Failed` and the reason. |
| `release lock` | Makes a candidate library lock from built machines. The default folder is the `work` of `bai.machines.json`. For a bundle, give `--work <bundle>\build`. Examine the lock one time and keep its SHA256. Do not make it again for each release; make it again only when the compiled libraries change (for example after a TwinCAT update). |
| `package preflight` | Checks the package that is installed now. The saved service project must agree with its `project.zip`. The live identity must agree with the package, and the online-change counter must be 0. It writes no runtime value. |
| `package install` | Installs a compiled package without XAE. It verifies both packages and does the preflight. Then it sets Config, copies the boot files, sets Run and verifies the live identity. It keeps the old image, `*.bootdata` and the event journal. When a step after Config fails, it puts the old image back and verifies the old identity. A machine accepts only a `ReleaseCandidate`. `--rehearsal` (UmRT only) also accepts a development package. With `--ssh`, the target is TwinCAT/BSD: the tool copies the package and a helper over SSH. The BSD host needs `python3`, a known host key and `doas` for the helper. Without `--yes`, nothing changes. |
| `package drift` | Compares a saved service project with a `project.zip`. It ignores the XML format. Code, values, element order and comments must agree. Exit code 0 = same, 1 = drift, 2 = error. |

Only one update can run at a time. The lock is `C:\bai\lock\umrt.lock` (UmRT),
`%ProgramData%\Beckhoff\TwinCAT\3.1\boot-update.lock` (local) or `plc-release.lock` on the BSD
host. The evidence folder of `install` has `install.json`, `preflight\`, `transaction.json` and
one log for each step. These commands do not prove that the hardware is ready: physical
acceptance is a separate step.

Exit code: 0 = pass, 1 = refused or failed (the evidence gives the reason), 2 = wrong arguments.

### What the tool never does

- It never makes a project, except with `init`.
- It never saves an adopted project.
- It never deletes PLC objects, tasks or task calls. It deletes I/O items only with
  `--confirm-delete-io`.
- It never writes over source files on `export`, except with `--overwrite`.
- It never activates, and never changes the runtime state, except with `--yes` or with
  `machine test` (only on UmRT_Default).
- It never reads `--confirm-delete-io`, `--overwrite`, `--plc` or `--yes` from `.stconfig`.

## 5. The source folder

### Layout

The tool reads all `.st` files under the source folder. The folder of a file becomes a PLC
folder: `<source>\App\Shark\PRG_MAIN.st` becomes `<PLC> Project > App > Shark > PRG_MAIN`.
One file can hold one object, or one object with its METHODs and PROPERTYs. The manifests
(section 6) are at the top level of the source folder.

### `.stignore`: skip files

One pattern on each line. `#` starts a comment.

```
# A file name, at any depth
FB_Scratch.st
*_deprecated.st
# A full folder: the trailing /** is necessary
Lib/Legacy/**
# The files of one folder, but not its subfolders
Lib/WorkInProgress/*.st
# A folder name at any depth
**/Generated/**
```

- A pattern with no `/` matches the file name at any depth.
- A pattern with `/` matches the full path, from the source folder.
- `*` does not cross `/`. `**` crosses `/`. `?` is one character. Case is not important.
- There is no `!pattern`. `--ignore <glob>` adds patterns for one run.

### `.stconfig`: default options

Put `.stconfig` in the source folder, or give its folder with `--config`.

```
project=C:\bai\work\Shark
shell=xae
clean=true
```

Then `bai sync all ST\Shark` is sufficient.

- Keys: `source`, `project`, `shell`, `clean`. Another key is an error.
- `source` is necessary only when the file is not in the source folder.
- Relative paths are relative to the folder of the file.
- `true`, `1` and `yes` are true (any case). All other values are false.
- An option on the command line always wins. The command (`sync code`, `check parse`, ...)
  always selects the stage.
- The tool says when it loaded the file. `--no-config` ignores it.

## 6. Manifest files

All manifests are optional and are at the top level of the source folder. A second `sync` with
no change changes nothing.

### `libraries.xml`

```xml
<Libraries>
  <Library Name="Tc2_Standard" Version="*" Company="Beckhoff Automation GmbH" />
</Libraries>
```

`sync libs` adds the missing references and removes the references that the file does not list.

### `io-devices.xml`

```xml
<IoTree>
  <Device Name="Device 1 (EtherCAT)" Disabled="false" Adapter="02000000B101">
    <Box Name="Term 1 (EK1100)" Product="EK1100">
      <Terminal Name="Term 2 (EL1008)" Product="EL1008" />
      <Terminal Name="Term 3 (EL2008)" Product="EL2008" Variants="MachineA" />
    </Box>
  </Device>
  <Links>
    <Link PlcVar="sample Instance^PlcTask Inputs^GVL_Shark.bMotorRunSensor"
          IoChannel="Device 1 (EtherCAT)^Term 1 (EK1100)^Term 2 (EL1008)^Channel 1^Input" />
  </Links>
</IoTree>
```

Devices, boxes and terminals:

- `<Box>` and `<Terminal>` are the same. They can be nested to any depth. Put E-bus terminals
  under their coupler (for example EK1100), not under another terminal.
- `Product` is the Beckhoff type name. `Disabled="true"` disables the device.
- An enabled EtherCAT master with no linked variable stops the build with a modal dialog box.
  If the links cannot be made, the tool disables the master so the build can pass.
- `Variants="A,B"` (on `<Device>`, `<Box>`, `<Terminal>`): the item is enabled only in these
  variants. No attribute = all variants. A child cannot be in a variant where its parent is not.
  `Variants` on `<Link>` is an error: TwinCAT keeps one set of links for all variants.

Network layout. There are two layouts. You can use both in one file.

- **One EtherCAT master for each device:** `<Device Name="BH 1" Adapter="02000000B101">`.
  `Adapter` is the MAC address (12 hex digits). Two enabled masters with the same (empty) adapter
  fail the build with no error text. On a PC with no EtherCAT card, use test addresses.
- **One adapter with ports (CU2508):** declare the adapter before the devices, then put each
  device on a port.

  ```xml
  <Adapter Name="Device 1 (RT-Ethernet Adapter)" Mac="000105992C32" Ports="8"
           DeviceName="\DEVICE\{GUID}" />
  <Device Name="BH1" Via="Device 1 (RT-Ethernet Adapter)" Port="1">
    <Box Name="Box 1 (CU2508)" Product="CU2508-0000-0000" />
  </Device>
  ```

  `Mac`: 12 hex digits (`-` and `:` are permitted). `DeviceDesc` and `Disabled` are optional.
  `DeviceName` is the Windows name of the network card (XAE: Adapter tab). A real machine needs
  it, else the activation fails with "Failed to connect to network adapter". `Via` and `Port`
  go together. `Via` and `Adapter` are not permitted on the same device. One device for each
  port. `Product="CU2508"` makes the "Configuration Port" box, which is not the correct one.
- TwinCAT cannot change a device from one layout to the other. `sync io` then stops. Add
  `--confirm-delete-io` to delete the device and make it again (with its links).

Boxes that are not Beckhoff boxes. Give the three numbers from the ESI file (`<Vendor><Id>`,
`<Type ProductCode RevisionNo>`), or from the `.xti` file of the box:

```xml
<Box Name="Box 11 (MARPOSS P3XF STANDARD)" Product="MARPOSS P3XF STANDARD"
     VendorId="#x00000916" ProductCode="#x00004000" RevisionNo="#x00010002" />
```

Give all three or none, as `#x...`, `0x...` or decimal. The tool makes the box from the numbers
and compares its type name with `Product`. The ESI file must be installed
(`C:\Program Files (x86)\Beckhoff\TwinCAT\3.1\Config\Io\EtherCAT`).

CoE startup list. An optional `<CoE>` under a `<Box>` or `<Terminal>`, in the native TwinCAT
`InitCmd` format:

```xml
<Terminal Name="Term 4 (EL3174)" Product="EL3174">
  <CoE>
    <InitCmd>
      <Transition>PS</Transition>
      <Comment>Example only</Comment>
      <Timeout>1000</Timeout>
      <Ccs>1</Ccs>
      <Index>#x8000</Index>
      <SubIndex>19</SubIndex>
      <Data>0500</Data>
    </InitCmd>
  </CoE>
</Terminal>
```

- No `<CoE>`: the tool does not change the user startup commands.
- `<CoE/>`: the tool removes all user startup commands of the terminal.
- A list: the tool replaces the user startup list with it, in this order.
- `Data` is a hex byte string in device byte order. `Ccs` is 1 or 2. TwinCAT makes the `Fixed`
  commands from the ESI and PDO settings. They are not in the manifest.
- The tool reads the list back and stops on a difference. These are project settings, not
  online writes. Confirm the values on the real terminal.

PDO configuration. An optional `<ProcessData>` under a box, in the native TwinCAT format, with
the full list of `<RxPdo>` and `<TxPdo>`. Each `<Entry>` has `Index`, `SubIndex`, `BitLen`,
`Name` and `DataType`. `export io` writes it. Start from an export and edit it.

Links:

- `PlcVar`: `<PLC> Instance^<Task> Inputs|Outputs^<GVL or PROGRAM>.<variable>`.
- `IoChannel`: the I/O tree path without `TIID^`, for example `<Device>^<Coupler>^<Terminal>^Channel 1^Input`.
- The code must use the `AT %I*`/`%Q*` variable. If not, it is not in the process image and the
  link fails (`0x98510001`).

PLC data types. Put already computed `<DataType>` XML for a terminal in
`plc-data-types\<Product>.xml`. `sync io` adds it to the `.tsproj`.

### `variants.xml`

```xml
<Variants DefineMode="Implicit">
  <Variant Name="MachineA" />
  <Variant Name="MachineB" />
  <Group Name="PCV_MODBUS"><Member>MachineA</Member></Group>
  <Group Name="PCV_ETHERCAT"><Member>MachineB</Member></Group>
</Variants>
```

- The PLC gets the name of the active variant and the names of its groups as compiler defines.
  In the code, test the group (the feature), not the machine name:
  `{IF defined (PCV_MODBUS)} ... {END_IF}`.
- Names must be valid PLC identifiers. `DefineMode` must be `Implicit`.
- `sync variants` writes the variants and groups and switches on the defines. It removes a
  variant that the file does not list.
- See `ST\VariantSample` and `ST\VariantIoSample`.

### `safety.xml`

```xml
<Safety Name="SHARK_Safety_prj" Folder="safety/SHARK_Safety_prj" />
```

- `Folder` holds the safety project exactly as the TwinCAT safety editor saved it:
  `<Name>.splcproj` and `TargetSystemConfig.xml`. Optional `TargetSystemConfig="<file>"`
  replaces `TargetSystemConfig.xml` in the copy (same logic, different terminals).
- `sync safety` sets the object IDs of the EtherCAT boxes and modules to the IDs in the safety
  files. It finds the boxes by name (for example `Term 34 (EL1918)`) and edits the `.tsproj`
  with TwinCAT closed. Then it copies the folder next to the `.tsproj`, checks each file
  byte by byte, and adds the safety project one time. It stops with `SAFETY FAILED` if the logic
  terminal has no Standard In/Out channels, or if a copied file is not equal to the source.
- If the IDs are not the same, TwinCAT adds the project with no error but with no Standard
  In/Out channels.
- **CAUTION: Do not edit the safety files. A changed file causes a CRC dialog box that the tool
  cannot close.** The tool does not download the safety project to the terminal.

### `tasks.xml`

```xml
<Tasks BaseTimeUs="1000">
  <Task Name="PlcTask" CycleTimeUs="10000" Priority="20"><Call>MAIN</Call></Task>
  <Task Name="SpectrometerTask" CycleTimeUs="1000" Priority="2"><Call>PRG_Spectrometer</Call></Task>
</Tasks>
```

- Write all tasks, also `PlcTask`. Without the file, the project keeps the one template task
  (`PlcTask`, 10 ms, priority 20, calls `MAIN`).
- `CycleTimeUs` must be a multiple of the base time. `BaseTimeUs` is optional (default 1000).
  The tool does not change the base time.
- Each task has its own priority (1 is the highest). `<Call>` names a PROGRAM of the source.
  A program can be in one task only. A task calls its programs in the given order.
- The tool makes missing tasks and calls and corrects a wrong cycle time or priority. It shows
  a `WARNING` for a task or call that the file does not list. It never deletes one.

### `events.xml`

```xml
<EventClasses>
  <EventClass Name="BeckhoffLibEvents" Guid="{00000000-0000-0000-0000-000000000000}">
    <Event Name="Verbose" Id="1" Severity="Verbose" Message="{0} - {1}" />
  </EventClass>
</EventClasses>
```

- Each class needs a template `event-classes\<Name>.xml` with the `<DataType>` XML and its real
  GUID (from a working project, or from `export events`).
- The Automation Interface cannot make event classes. `sync events` adds the missing classes
  directly to the `.tsproj`, with TwinCAT closed.

### `links.xml`

```xml
<VarLinks>
  <OwnerA Name="TIPC^sample^sample Instance">
    <OwnerB Name="TIID^Device 1 (EtherCAT)^Term 1 (EK1100)^Term 2 (EL1008)">
      <Link VarA="PlcTask Inputs^GVL_Shark.bMotorRunSensor" VarB="Channel 1^Input" />
    </OwnerB>
  </OwnerA>
</VarLinks>
```

- This is the native TwinCAT "Export Variable Mapping" format. `sync io` applies it first, in
  one call. Then it applies the `<Links>` of `io-devices.xml`.
- Use it to find a channel path: link the variable one time in XAE, run `export links`, then
  copy the channel into a `<Link>` in `io-devices.xml`. `links.xml` is in `.gitignore`.
- One `OwnerA` for each PLC instance. Put the task group into `VarA` (no `GrpA`). Other shapes
  can be read by `check parse`, but TwinCAT did not apply them. If you are not sure, use
  `export links`.

## 7. Runtime test files

### `expect.json` (for `verify`)

```json
{
  "variants": {
    "MachineA": [
      { "symbol": "MAIN.sMachine",           "type": "STRING(16)", "equals": "MachineA" },
      { "symbol": "MAIN.rMaxPressure",       "type": "REAL",  "equals": 3.0, "tolerance": 0.001 },
      { "symbol": "PRG_PcvModbus.nCycles",   "type": "UDINT", "increases": true },
      { "symbol": "PRG_PcvEtherCAT.nCycles", "type": "UDINT", "absentOrZero": true }
    ]
  }
}
```

Each line has one check: `equals` (optional `tolerance`), `increases` (read two times, 0.5 s
apart; the code runs) or `absentOrZero` (the code is not compiled in, or does not run). For a
project with no variants, use `"default": [ ... ]`.

### Scenario files (for `test`)

```json
{
  "name": "Purge precursor",
  "tags": ["sequence", "smoke"],
  "requires": ["PCV_ETHERCAT"],
  "timeoutS": 300,
  "steps": [
    { "do": "send", "command": 200, "parameters": { "mode": 1, "side": 1 }, "expect": "accepted" },
    { "do": "waitUntil", "symbol": "PRG_Sequence.fbOrchestrator.eActiveSequence", "type": "INT", "equals": 7, "timeoutS": 5 },
    { "do": "check", "symbol": "PRG_Sequence.fbOrchestrator.bError", "equals": false },
    { "do": "write", "symbol": "GVL_Simulation.bSafetyOk", "value": false },
    { "do": "wait", "seconds": 2 }
  ],
  "cleanup": [
    { "do": "write", "symbol": "GVL_Simulation.bSafetyOk", "value": true }
  ]
}
```

| Field | Meaning |
|---|---|
| `name` | Required. The name in the results. |
| `tags` | Optional. Names that select a set of scenarios. |
| `requires` | Optional. Feature groups that the machine must have. Never a machine name. |
| `timeoutS` | Optional. The time limit for the scenario. Default: 600 s. |
| `steps` | Required. The steps, in order. |
| `cleanup` | Optional. Steps that run after the scenario, also after a failure. Use them to undo manual commands: CancelProcess does not undo all of them. |

| `do` | Fields | What it does |
|---|---|---|
| `send` | `command`, `parameters`, `expect` (`accepted` or `rejected`, default `accepted`), `timeoutS` (default 10) | Writes the command JSON into `PRG_Telemetry.fbCommandReceiver.AdsStream`. Waits until `nLastCommandIndex` (accepted) or `nLastRejectedCommandIndex` (rejected) shows its index. |
| `waitUntil` | `symbol`, `type`, a condition, `timeoutS` | Reads the symbol until the condition is true. Without `timeoutS`, it waits until the scenario time ends. |
| `check` | `symbol`, `type`, a condition | Reads the symbol one time. |
| `write` | `symbol`, `type`, `value` | Writes the value. |
| `wait` | `seconds` | Waits. |

A condition is `equals` or `notEquals` (optional `tolerance`), or `lessThan` and/or
`greaterThan` (numbers only). `type` is `BOOL`, `BYTE`, `SINT`, `INT`, `UINT`, `DINT`, `UDINT`,
`LINT`, `ULINT`, `REAL`, `LREAL` or `STRING(n)`. For an enum, use `INT`. You can omit `type` only
for `true` or `false`. The tool reads the size of each symbol from the PLC. If `type` does not
agree, the step fails and shows the PLC type.

## 8. Limits

- `MDP5001_*` PLC data type names contain a hash of the terminal configuration. They can be
  different on a machine with other terminal revisions. If an alias shows `Unknown type`, build
  one time, read the names in the saved `.tsproj`, and update the alias and `plc-data-types`.
- A link needs the I/O image of the PLC instance. The link check covers only `AT %I*`/`%Q*`
  variables of GVLs and PROGRAMs, not of function block instances.
- UmRT has no EtherCAT adapter. Real I/O needs the real machine.
- The UmRT trial license is valid for 7 days. Only a person can renew it in XAE (it asks for a
  security code). `doctor` shows the expiry date.

## 9. Problems and fixes

| Problem | Fix |
|---|---|
| `Class not registered` | The shell is not installed or not registered. Run `bai doctor`. Use `--shell xae` or `--shell vs2022`. |
| `REGDB_E_CLASSNOTREG (0x80040154)` | The tool runs as x86. Build it unchanged (x64). |
| `TwinCAT project template not found` | Install TwinCAT 3.1 XAE, or repair it (section 1). |
| `project not found` | Check the `--project` path. Make the project with `init` first. |
| `not a project created by this tool` on `sync` | `sync` works only on a project that `init` made. Use `build` or `export` for other projects. |
| `PLC project name ... is not valid` | Rename the source folder, or give `--name` to `init`. |
| `BUILD FAILED — variant defines are off` | Run `bai sync variants` first. |
| `object(s) are defined in more than one .st file` | Delete or rename the old file. |
| `[drift] ... no longer in .st source` | The `.st` file of an object is gone, but the object is still in the project. Delete it in TwinCAT. |
| `io-devices.xml: TwinCAT placed '...' at '...'` | The nesting is wrong. Put E-bus terminals under their coupler. |
| `io-devices.xml: TwinCAT cannot make box '...'` | Install the ESI file, or give `VendorId`/`ProductCode`/`RevisionNo`. |
| `box '...': ... made a "...", but Product says "..."` | The three numbers make a different box. Compare them with the ESI file. |
| `IO SYNC FAILED — ... item(s) have the wrong kind` | The device has the other network layout. Add `--confirm-delete-io`, or make a new project. |
| `Device '...' Via=...` or `... Port=...` | `Via` must name an `<Adapter>`. `Port` must be from 1 to `Ports`. |
| Linking fails with `0x98510001` | Use the `%I*`/`%Q*` variable in the code. |
| `SAFETY FAILED — ... box(es) named ...` | Use the box names of the real project in `io-devices.xml`. |
| `SAFETY FAILED — ... has not got its Standard In/Out channels` | The object IDs do not agree. Look at the `~ id` lines and at the box names. |
| `SAFETY FAILED — ... not byte-equal to the source` | Get the safety folder again from git. Do not edit it. |
| The PLC stays in Config after `activate` | Run `bai doctor` and look at the license expiry date. |
| `verify` shows `FAIL` | Another variant runs, or the code does not do what `expect.json` says. Look at the actual values. |
| `test`: `send` gives `no answer` | The command id is not known, or the command receiver does not run. |
| `TIMEOUT — Build did not finish in 15 minute(s)` | The shell locked during the build. The tool closed it. Run again. |
| A `TcXaeShell` or `devenv` process stays open | The tool closes its shell in about 10 s. After a killed run: `Get-Process TcXaeShell,devenv \| Stop-Process -Force`. Do not pipe the tool output into `Select-Object -First`: it stops the tool early. |
| `RPC_E_SERVERCALL_RETRYLATER` | The tool tries again. If it continues, close old `devenv`/`TcXaeShell` processes. |

The tool sets TwinCAT silent mode during a run, so TwinCAT answers its own questions with the
default. It sets your setting back at the end.

## 10. Test the tool

Unit tests (no TwinCAT): `dotnet test beckhoffAutomationInterface.Tests`.

The scripts in `e2e\` run the real tool on real TwinCAT. They work on copies in a temporary
folder and read the fixtures in `ST\` (`sample`, `VariantSample`, `VariantIoSample`). Each script
ends with `E2E PASSED` (exit code 0) or `E2E FAILED` (exit code 1).

| Script | What it tests | Needs |
|---|---|---|
| `e2e\all-commands.ps1` | Each command: wrong command lines, checks, init/sync/build/export, a compile error on the correct line, read-only runtime checks | XAE, UmRT (run `variant-sample.ps1` first) |
| `e2e\variant-sample.ps1` | Activate and verify each variant on UmRT; the wrong variant fails | XAE, UmRT with license |
| `e2e\variant-io-sample.ps1` | Per-variant I/O: init, sync, export, round trip, build of all variants | XAE |
| `e2e\coe-startup.ps1` | CoE startup lists: import, export, clear, fresh rebuild | XAE |
| `e2e\pdo-configuration.ps1` | PDO configuration and links: import, export, fresh rebuild | XAE |
| `e2e\com-errors.ps1`, `e2e\dispose-timeout.ps1` | COM error reports and shell shutdown | XAE |
| `e2e\release-artifacts.ps1` | Release commands on a kept package and project | a kept package |
| `e2e\cleanup-workspace.ps1` | `scripts\Manage-BaiWorkspace.ps1` on test folders | nothing |

`scripts\Manage-BaiWorkspace.ps1` lists old `C:\bai-*` work folders and moves or deletes them.
Each action only shows a preview until you add `-Apply`. Use short work paths such as
`C:\bai\work`: long paths can exceed the Windows path limit.

To install a GitHub Actions runner for PLC CI, see
[docs/github-self-hosted-runner-install-steps.md](docs/github-self-hosted-runner-install-steps.md).
