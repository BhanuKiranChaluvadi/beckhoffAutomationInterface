# How to use the Beckhoff Automation Interface tool

This guide tells you how to do the usual tasks. Each task has short steps.
For the full reference of all options, see [README.md](README.md).

## 1. What the tool does

You write PLC code as `.st` text files in git. The tool puts these files into a TwinCAT 3
project, compiles the project and tells you the errors. It can also activate the project on a
runtime and read live PLC values to make sure that the program runs correctly. It can run
test scenarios: send commands to the PLC and check the results.

You do not use the TwinCAT XAE window. The tool never shows a Yes/No dialog box.

## 2. Words in this guide

| Word | Meaning |
|---|---|
| **Source folder** | The folder with your `.st` files and the manifests (`libraries.xml`, `io-devices.xml`, `events.xml`, `variants.xml`, `safety.xml`, `tasks.xml`). Example: `ST\Shark`. |
| **Project** | The TwinCAT solution that the tool makes and keeps up to date: `<folder>\<name>\<name>.sln`. |
| **Adopted project** | A TwinCAT project that the tool did not make. The tool opens it read-only and never saves it. |
| **Variant** | One machine of a machine family (for example Shark, UVM, SAS). One project can hold all variants. |
| **Target** | The runtime that runs the PLC: `umrt` (UmRT_Default, the user mode runtime on this PC) or `local` (the TwinCAT runtime of this PC). |

## 3. Before you start

1. Make sure that the PC has Windows, TwinCAT 3.1 XAE (build 4024 or later) and the .NET SDK.
   See [REQUIREMENTS.md](beckhoffAutomationInterface/REQUIREMENTS.md).
2. Build the tool:

   ```powershell
   cd beckhoffAutomationInterface
   dotnet build -c Debug -f net48
   ```

   The tool is `beckhoffAutomationInterface\bin\Debug\net48\beckhoffAutomationInterface.exe`.
   In this guide, the short name is `bai`. To use the short name, type this one time:

   ```powershell
   Set-Alias bai "C:\path\to\beckhoffAutomationInterface\bin\Debug\net48\beckhoffAutomationInterface.exe"
   ```

3. Run the health check:

   ```powershell
   bai doctor
   ```

   It shows the TwinCAT shells, the TwinCAT version, the state of each runtime, the boot
   projects and the license expiry dates. It changes nothing.

The tool uses the TwinCAT XAE shell by default. To use Visual Studio 2022, add `--shell vs2022`.

## 4. Make a new project from `.st` source

Do this one time for each source folder.

```powershell
bai init C:\work\ST\Shark --into C:\work\TwinCAT
```

- The tool makes `C:\work\TwinCAT\Shark\Shark.sln`, puts all source into it, and compiles it.
- The project name comes from the source folder name. To use a different name, add `--name X`.
- The name must have only letters, digits and `_`. A name like `my-plc` is refused.

Only `init` can make a project. All other commands refuse a project that does not exist.

## 5. Send your changes to the project

After you change `.st` files or a manifest, send the changes:

```powershell
bai sync all C:\work\ST\Shark --project C:\work\TwinCAT\Shark
```

`--project` can be the `.sln`, its folder, or its `.tsproj`.

To send only one part, use one mode instead of `all`:

| Mode | What it sends |
|---|---|
| `code` | `.st` files → POUs, DUTs, GVLs. No compile. |
| `libs` | `libraries.xml` → library references |
| `io` | `io-devices.xml` → I/O tree and variable links |
| `events` | `events.xml` → event classes (no TwinCAT window, very fast) |
| `variants` | `variants.xml` → project variants (see section 9) |
| `safety` | `safety.xml` → TwinSAFE project (see "The safety project" below) |
| `tasks` | `tasks.xml` → PLC tasks and their program calls (see "The tasks" below) |
| `all` | All of the above, then compile |

A second run with no changes reports "0 created, 0 updated, 0 deleted".

### Send only the files that changed

Commit your changes in git first. Then add `--incremental`:

```powershell
bai sync all C:\work\ST\Shark --project C:\work\TwinCAT\Shark --incremental
```

The tool reads the files that changed since the last sync. The first sync must be a full sync.

### Deleted files

The tool does not delete PLC objects by default. It only shows a warning. To delete the PLC
object of a deleted `.st` file, add `--incremental --confirm-delete`. To delete I/O items that
are not in `io-devices.xml`, add `--confirm-delete-io`.

### The safety project (TwinSAFE)

A machine with a TwinSAFE logic terminal (for example EL1918) has a safety project. The tool
can add it to the project.

1. Put the safety project folder into the source folder. Do not change it: use the files that
   the TwinCAT safety editor saved.
2. Write `safety.xml` in the source folder:

   ```xml
   <Safety Name="SHARK_Safety_prj" Folder="safety/SHARK_Safety_prj" />
   ```

   - `Name` is the name of the safety project. The folder must contain `<Name>.splcproj` and
     `TargetSystemConfig.xml`.
   - Optional: `TargetSystemConfig="<file>"`. This file replaces `TargetSystemConfig.xml` in the
     copy. Use it when machines have the same safety logic but different terminals (serial
     numbers).
   - The tool ignores other attributes.
3. Send it: `bai sync safety <source> --project <p>` (or `sync all`, or `init`).

The tool does these steps:

1. It gives the EtherCAT boxes and modules the object IDs that the safety files use. It finds
   them by name, for example `Term 34 (EL1918)`. If a different box has the ID, the two boxes
   change IDs. TwinCAT is closed for this step.
2. It copies the safety folder next to the `.tsproj`, into `<Name>\`. It makes sure that each
   copied file is byte-equal to the source.
3. It adds the safety project. If the project has it already, it does not add it again.
4. It makes sure that the safety logic terminal has its Standard In/Out channels (for example
   `Standard In Var 1`). If not, it stops with `SAFETY FAILED`.
5. At the end, it closes TwinCAT and compares the copied files with the source again.

Why the IDs: if the IDs are not the same, TwinCAT adds the safety project with no error, but the
terminal gets no Standard In/Out channels. Then the links to these channels fail.

**CAUTION: Do not edit the safety files by hand. If a file changes (for example
`TargetSystemConfig.xml`), TwinCAT shows a CRC error dialog box. The tool cannot close it.**

Without `safety.xml`, the tool does nothing for safety. The tool does not download the safety
project to the terminal. `export` does not write `safety.xml`.

### The tasks

A new project has one task: `PlcTask` (10 ms, priority 20). It calls `MAIN`. The PLC template
makes it. If the PLC needs more tasks (for example a fast task for one device), write
`tasks.xml` in the source folder:

```xml
<Tasks>
  <Task Name="PlcTask" CycleTimeUs="10000" Priority="20"><Call>MAIN</Call></Task>
  <Task Name="SpectrometerTask" CycleTimeUs="1000" Priority="2"><Call>PRG_Spectrometer</Call></Task>
</Tasks>
```

- Write all tasks, also `PlcTask`. The file is the full list.
- `CycleTimeUs` is the cycle time in µs. It must be a multiple of the base time. The base time
  is 1000 µs (the TwinCAT default). If your project has a different base time, write it on the
  root: `<Tasks BaseTimeUs="500">`. The tool does not change the base time.
- `Priority`: 1 is the highest. Each task must have a different priority.
- `<Call>` names a PROGRAM of the source folder. A task calls its programs in the given order.
  A program can be in one task only.
- Send it: `bai sync tasks <source> --project <p>` (or `sync all`, or `init`).

The tool checks the file before TwinCAT starts. Then it does these steps for each task:

1. It makes the real-time task `TIRT^<Name>` if it is not there. It sets the cycle time and the
   priority if they are different.
2. It makes the PLC task `<Name>` with its first program call if it is not there. TwinCAT links
   it to the real-time task of the same name. It adds the program calls that are missing.
3. It reads the project again. If a change did not occur, it stops with `TASKS FAILED`.

The step comes after the code (the programs must be there) and before the I/O links (a link
names the process image of a task, for example `SpectrometerTask Inputs`). A second run
changes nothing.

**The tool does not delete a task or a program call.** A task that is not in `tasks.xml`, or a
call that the file does not list, gives a `WARNING` line. Remove it in TwinCAT if it is wrong.

Without `tasks.xml`, the tool does nothing for tasks: the project keeps `PlcTask`.

## 6. Compile

```powershell
bai build C:\work\TwinCAT\Shark
```

- Exit code 0 = `BUILD PASSED`. Exit code 1 = errors or timeout.
- Each error shows the `.st` file and the line number.
- Add `--clean` to clean the solution first. Then an old result cannot pass.
- `build` works on any TwinCAT project, also on a project that the tool did not make. If the
  PLC project name is different from the `.tsproj` name, add `--plc <name>`.

## 7. Check the source quickly (no TwinCAT)

These checks take seconds. They do not open TwinCAT.

| Command | What it checks | Exit 1 when |
|---|---|---|
| `bai check parse <source>` | All `.st` files can be read | a file cannot be parsed |
| `bai check format <source>` | Line endings, spaces at line end, empty lines at file end | never (report only) |
| `bai check links <source>` | Each `AT %I*`/`%Q*` variable has a link in `io-devices.xml` | a variable has no link |
| `bai check events <source> --project <p>` | Each event class in `events.xml` is in the project | an event class is missing |

To skip files, add `--ignore <pattern>` (you can use it more than one time), or write the
patterns in a `.stignore` file in the source folder.

## 8. Get `.st` files from an existing project

Use this to start from a project that you made in the TwinCAT window.

```powershell
bai export all C:\work\ST\NewMachine --project C:\old\Machine\Machine.tsproj
```

| Mode | What it writes |
|---|---|
| `code` | All POUs, DUTs, GVLs as `.st` files |
| `libs` | `libraries.xml` |
| `io` | `io-devices.xml` |
| `events` | `events.xml` and `event-classes\*.xml` |
| `links` | `links.xml` (the current variable links) |
| `all` | All of the above |
| `object <Name>` | One object only, for example `bai export object FB_Motor <source> --project <p>` |

- The project is opened read-only. The tool does not change it.
- If the folder already has `.st` files or manifests, the tool refuses. Add `--overwrite` to
  write over them.
- Then commit the folder to git, and use `sync` from now on.

## 9. One project for a family of machines (variants)

1. Write `variants.xml` in the source folder:

   ```xml
   <Variants DefineMode="Implicit">
     <Variant Name="Shark" />
     <Variant Name="SAS" />
     <Group Name="PCV_MODBUS"><Member>Shark</Member></Group>
     <Group Name="PCV_ETHERCAT"><Member>SAS</Member></Group>
   </Variants>
   ```

2. In the code, test the **group** (the feature), not the machine name:

   ```
   {IF defined (PCV_MODBUS)}
       PRG_PcvModbus();
   {ELSE}
       PRG_PcvEtherCAT();
   {END_IF}
   ```

   The variant name and each group name of the variant become PLC defines.

3. Send it: `bai sync variants <source> --project <p>` (or `sync all`).
4. Compile: `bai build <p>` compiles **each** variant one after the other and shows a result
   for each. To compile one variant only, add `--variant SAS`.

### Different I/O for each machine

Keep **one** `io-devices.xml`. Mark the items that only some machines have:

```xml
<Terminal Name="Term 3 (EL2008)" Product="EL2008" Variants="Shark" />
```

- No `Variants` attribute = the item is in all variants.
- `sync io` enables the item in the named variants and disables it in the others.
- Links are the same for all variants. TwinCAT keeps only one set of links. Do not put
  `Variants` on a `<Link>`; the tool refuses it.
- The code must use an `AT %I*`/`%Q*` variable. If not, the variable is not in the process image
  and you cannot link it.

Examples: [`ST/VariantSample`](ST/VariantSample) and [`ST/VariantIoSample`](ST/VariantIoSample).

### More than one EtherCAT device: the network layout

There are two layouts. You can use both in one file.

**A. One network adapter with ports (CU2508).** The machine has one real-time Ethernet adapter
with a CU2508 port multiplier. Each EtherCAT device is on one port of the adapter:

```xml
<IoTree>
  <Adapter Name="Device 1 (RT-Ethernet Adapter)" Mac="000105992C32" Ports="8" />
  <Device Name="BH1" Disabled="false" Via="Device 1 (RT-Ethernet Adapter)" Port="1">
    <Box Name="Box 1 (CU2508)" Product="CU2508-0000-0000" />
    ...
  </Device>
  <Device Name="BH2" Disabled="false" Via="Device 1 (RT-Ethernet Adapter)" Port="2">
```

| Attribute | On | What it does |
|---|---|---|
| `Name` | `<Adapter>` | The name of the adapter in the I/O tree. |
| `Mac` | `<Adapter>` | The MAC address of the network card, 12 hex digits (`-` and `:` are allowed). |
| `Ports` | `<Adapter>` | The number of ports. A CU2508 has 8. |
| `DeviceDesc` | `<Adapter>` | Optional. The description of the network card in XAE. |
| `Disabled` | `<Adapter>` | Optional. `true` = the adapter is disabled. |
| `Via` | `<Device>` | The `Name` of the `<Adapter>` that the device is on. |
| `Port` | `<Device>` | The port of that adapter, from 1 to `Ports`. |

- Put `<Adapter>` before the devices.
- `Via` and `Adapter` are not allowed on the same device. `Via` and `Port` go together.
- One device on each port. Two devices on one port is an error.
- The tool makes the adapter first. Then it puts each device on its port.
- Use `Product="CU2508-0000-0000"` for the CU2508 box ("CU2508 Ethernet Port").
  `Product="CU2508"` makes a different box ("CU2508 Configuration Port").
- A PC without this network card can build the project.

**B. One EtherCAT master per device.** Each master has its own network adapter:

```xml
<Device Name="BH 1" Disabled="false" Adapter="02000000B101">
```

Two enabled masters with the same (empty) adapter fail the system build with no error text.
On a PC with no EtherCAT card, use test addresses (`02000000B101`, `02000000B201`, ...).
On the real machine, use the real adapter.

TwinCAT cannot change a device from layout B to layout A, or back. `sync io` then stops with
an error. Add `--confirm-delete-io`: the tool deletes the device and makes it again. The links
are made again too.

`export io` writes both layouts back (`<Adapter>`, `Via`, `Port`, `Adapter`).

## 10. Run the program and check live values (closed loop)

### 10.1 Activate

```powershell
bai activate C:\work\TwinCAT\Shark --target umrt --variant SAS
```

Without `--yes`, the tool only shows the boot project that is on the target now. It changes
nothing and gives exit code 1.

**CAUTION: Activation replaces the boot project of the target.** To activate, add `--yes`:

```powershell
bai activate C:\work\TwinCAT\Shark --target umrt --variant SAS --yes
```

The tool compiles the variant, activates it, starts TwinCAT, and waits until the PLC is in Run.
It does not clean first: a clean during TwinCAT's background precompile can lock the shell.
On `umrt`, the tool disables the EtherCAT devices, because UmRT has no EtherCAT adapter.
To start the PLC in simulation for such a test run, add `--define SIMULATION`: the tool adds
this PLC compiler define for this build only (the code tests `{IF defined (SIMULATION)}`).
`build` takes `--define` too. `sync` and `init` do not: they save the project, and a test
define must never be saved into it.
Your project files do not change. The tool works on a temporary copy.

### 10.2 Verify

Write `expect.json` with the values that each variant must show:

```json
{
  "variants": {
    "SAS": [
      { "symbol": "MAIN.rMaxPressure",     "type": "REAL",  "equals": 10.0, "tolerance": 0.001 },
      { "symbol": "PRG_PcvEtherCAT.nCycles", "type": "UDINT", "increases": true },
      { "symbol": "PRG_PcvModbus.nCycles",   "type": "UDINT", "absentOrZero": true }
    ]
  }
}
```

Each line has exactly one check: `equals` (with optional `tolerance`), `increases` (the value
goes up, so the code runs) or `absentOrZero` (the code is not compiled in, or does not run).
For a project without variants, use `"default": [ ... ]` instead of `"variants"`.

```powershell
bai verify --target umrt --expect C:\work\ST\Shark\expect.json --variant SAS
```

The tool reads the live values over ADS. Exit code 0 = `VERIFY PASSED`.
Each check shows `OK` or `FAIL`, the actual value and the expected value.

### 10.3 Test sequences (scenario files)

`verify` reads values one time. `test` runs a **scenario**: it sends commands to the PLC, as
the client does, and then waits for values and checks them. Use it to test a sequence in
simulation.

Write one JSON file for each scenario:

```json
{
  "name": "Purge precursor, change material",
  "tags": ["sequence", "smoke"],
  "requires": ["PCV_ETHERCAT"],
  "timeoutS": 300,
  "steps": [
    { "do": "send", "command": 200, "parameters": { "mode": 1, "side": 1, "bubbler": 1 }, "expect": "accepted" },
    { "do": "waitUntil", "symbol": "PRG_Sequence.fbOrchestrator.eActiveSequence", "type": "INT", "equals": 7, "timeoutS": 5 },
    { "do": "waitUntil", "symbol": "PRG_Sequence.fbOrchestrator.bDone", "equals": true },
    { "do": "check", "symbol": "PRG_Sequence.fbOrchestrator.bError", "equals": false },
    { "do": "write", "symbol": "GVL_Simulation.bSafetyOk", "value": false },
    { "do": "wait", "seconds": 2 }
  ]
}
```

| Field | Meaning |
|---|---|
| `name` | Required. The name in the results. |
| `tags` | Optional. Names that select a set of scenarios, for example `smoke`, `full`, `fault`. |
| `requires` | Optional. Feature groups (from `variants.xml`) that the machine must have. Never a machine name. |
| `timeoutS` | Optional. The time limit for the full scenario. Default: 600 s. |
| `steps` | Required. The steps, in order. |

The steps:

| `do` | Fields | What it does |
|---|---|---|
| `send` | `command`, optional `parameters`, `expect` (`accepted` or `rejected`, default `accepted`), `timeoutS` (default 10) | Writes the command JSON into `PRG_Telemetry.fbCommandReceiver.AdsStream`. Then waits until `nLastCommandIndex` (accepted) or `nLastRejectedCommandIndex` (rejected) shows the index of the command. |
| `waitUntil` | `symbol`, `type`, a condition, optional `timeoutS` | Reads the symbol again and again until the condition is true. Without `timeoutS`, it waits until the scenario time ends. |
| `check` | `symbol`, `type`, a condition | Reads the symbol one time. |
| `write` | `symbol`, `type`, `value` | Writes the value into the symbol. |
| `wait` | `seconds` | Waits. |

A condition is `equals` or `notEquals` (with optional `tolerance` for numbers), or `lessThan`
and/or `greaterThan` (numbers only). `type` is a PLC type: `BOOL`, `BYTE`, `SINT`, `INT`, `UINT`,
`DINT`, `UDINT`, `LINT`, `ULINT`, `REAL`, `LREAL`, `STRING(n)`. For an enum, use `INT`. You can
leave out `type` only when the value is `true` or `false` (then it is `BOOL`). The tool reads the
size of each symbol from the PLC: if `type` does not agree, the step fails and shows the PLC type.

Run all scenarios in a folder:

```powershell
bai test --target umrt --scenarios C:\work\BeckhoffLib\Machines\NFLite\tests `
         --variants C:\work\BeckhoffLib\Machines\NFLite\Shark\variants.xml --tags smoke --junit C:\work\results\shark.xml
```

| Option | Meaning |
|---|---|
| `--scenarios` | One scenario file, or a folder. The tool reads all `*.json` files in the folder and its subfolders. |
| `--variants`, `--variant` | The tool reads the feature groups of the variant from `variants.xml`. If the file has one variant, `--variant` is not necessary. |
| `--groups` | The feature groups, for example `PCV_MODBUS,SPECTROMETER_PLC`. Use it instead of `--variants`. |
| `--tags` | Run only the scenarios that have one of these tags. |
| `--junit` | Write the results as JUnit XML (one test case for each scenario), for CI. |
| `--timeout` | Time limit for the PLC to go to Run and for the machine to become idle before each scenario. Default: 60 s. |

What the tool does:

1. It reads all scenario files first. If one file is not correct, it shows `file: step n:` and
   the problem, and it sends nothing to the PLC (exit code 2).
2. It skips a scenario when the machine does not have all its `requires` groups. Without
   `--variants` or `--groups`, it skips each scenario that has `requires`.
3. Before each scenario, it waits until no sequence runs (`eActiveSequence` = 0, or 99 = Error).
   It sends ErrorAcknowledge (9999). If the program has `GVL_Simulation.bReset`, it sets it to
   TRUE. Then it waits for `eActiveSequence` = 0.
4. It runs the steps. The scenario stops at the first step that fails. The next scenario
   runs.
5. It shows one line for each scenario (`PASS`, `FAIL` or `SKIP`) and a summary. For a
   failure, it shows the step, the symbol, the expected value and the actual value. It also
   shows `eActiveSequence`, `sStatus` and `stLastError` of `PRG_Sequence.fbOrchestrator`.

Exit code: 0 = all scenarios passed (`TEST PASSED`), 1 = a scenario failed or the PLC is not in
Run, 2 = the command line or a scenario file is not correct.

**CAUTION: A scenario sends real commands to the PLC. Use `test` only on a PLC in simulation.**

### 10.4 License

- UmRT needs a license. A 7-day trial license is free. Only a person can make or renew it in
  the TwinCAT window (it asks for a security code).
- `bai doctor` shows the expiry date.
- If the PLC does not go to Run, the tool tells you if the license is the cause.

## 11. Save typing with `.stconfig`

Put a `.stconfig` file in the source folder:

```
project=C:\work\TwinCAT\Shark
incremental=true
```

Then this is enough: `bai sync all C:\work\ST\Shark`.

- Permitted keys: `source`, `project`, `shell`, `clean`, `incremental`. Other keys are errors.
- An option on the command line always wins.
- These options are **never** read from `.stconfig`: `--confirm-delete`, `--confirm-delete-io`,
  `--overwrite`, `--plc`, `--yes`.
- To ignore the file one time, add `--no-config`.

See [.stconfig.example](.stconfig.example).

## 12. Sync automatically after each commit

1. Make the project one time with `bai init`.
2. Turn on the git hook in your source repository:

   ```powershell
   git config core.hooksPath githooks
   ```

3. Tell the hook which source folder and project folder to use:

   ```powershell
   $env:BECKHOFF_ST_SOURCE   = "C:\work\ST\Shark"     # required; without it the hook logs SKIPPED
   $env:BECKHOFF_TWINCAT_DEST = "C:\work\TwinCAT"     # default: Documents\TwinCAT
   ```

After each commit that changes `.st` files, the hook runs `sync all --incremental` in the
background. The result is in `githooks\logs\<time>.log` (last line `RESULT: SUCCESS` or
`RESULT: FAILED`). If the project does not exist, the hook fails. It never makes a project.

## 13. Use in CI

```powershell
bai build "$env:GITHUB_WORKSPACE" --plc MyPlc
if ($LASTEXITCODE -ne 0) { throw "PLC build failed" }
```

See [docs/plc-ci-setup-guide.md](docs/plc-ci-setup-guide.md) and
[docs/github-self-hosted-runner-install-steps.md](docs/github-self-hosted-runner-install-steps.md).

## 14. Exit codes

| Code | Meaning |
|---|---|
| 0 | Done, and the result is as expected (`BUILD PASSED`, `VERIFY PASSED`, `TEST PASSED`, no problems found) |
| 1 | A problem: compile error, failed check, failed scenario, wrong command line, missing project, or `activate` without `--yes` |
| 2 | Only `test`: the command line or a scenario file is not correct. Nothing was sent to the PLC. |

When the command line is wrong, the tool shows the correct usage. If you use an option of the
old command line (for example `--dest`, `--plc-name`, `--sync-code`), the error tells you the
new option.

## 15. What the tool never does

- It never shows a dialog box and never waits for a person.
- It never makes a project, except with `init`.
- It never saves an adopted project (`build`, `export`, `activate` use it read-only).
- It never deletes PLC objects or I/O items, except with `--confirm-delete` or
  `--confirm-delete-io`.
- It never writes over `.st` files on export, except with `--overwrite`.
- It never activates, except with `--yes`.

## 16. If there is a problem

| Problem | What to do |
|---|---|
| `Class not registered` | The shell is not installed. Run `bai doctor`. Use `--shell xae` or `--shell vs2022`. |
| `project not found` | Check the `--project` path. Make the project with `bai init` first. |
| `not a project created by this tool` on `sync` | `sync` only works on a project made by `init`. For other projects, use `build` or `export`. |
| `PLC project name ... is not valid` | Rename the source folder, or add `--name ValidName` to `init`. |
| `BUILD FAILED — variant defines are off` | Run `bai sync variants` first. |
| `object(s) are defined in more than one .st file` (`check parse` or `sync`) | Two files define the same object. Only one would reach the PLC, and which one depends on file order. Delete or rename the old file. |
| `io-devices.xml: TwinCAT placed '...' at '...'` | The nesting in `io-devices.xml` is wrong. E-bus terminals go under their coupler (for example EK1100), not under another terminal. |
| `IO SYNC FAILED — ... item(s) have the wrong kind` | A device in the project has the other network layout (section 9, "More than one EtherCAT device"). Add `--confirm-delete-io`, or make a new project. |
| `io-devices.xml: Device '...' Via=...` or `... Port=...` | The `Via`/`Port` attributes are wrong. `Via` must be the name of an `<Adapter>`. `Port` must be from 1 to `Ports`. |
| The PLC stays in Config after `activate` | Run `bai doctor`. Look at the license expiry date. Renew the trial license in the TwinCAT window. |
| `verify` shows `FAIL` | The variant on the target is not the one you named, or the code does not do what `expect.json` says. Look at the actual values. |
| `test`: `send` gives `no answer` | The command id is not known, or the command receiver does not run. Look at the PLC log. |
| `test`: `the "type" does not match the PLC symbol` | Use the PLC type that the message shows in the `type` field of the step. |
| `SAFETY FAILED — ... box(es) named ...` | A box name of the safety files is not in `io-devices.xml`. Use the names of the real project, for example `Term 34 (EL1918)`. |
| `SAFETY FAILED — ... has not got its Standard In/Out channels` | The object IDs do not agree. Look at the `~ id` lines and at the box names in `io-devices.xml`. |
| `SAFETY FAILED — ... not byte-equal to the source` | A safety file changed. Get the safety folder again from git. Do not edit the files. |
| Linking fails with `0x98510001` | The `%I*`/`%Q*` variable is not used in the code, so it is not in the process image. Use it in the code. |
| A `TcXaeShell` process stays open | Close it in Task Manager. The tool closes its own shell in about 10 seconds. |
| `TIMEOUT — Build did not finish in 15 minute(s)` | The shell locked up during the build. The tool closed the shell it started. Run again. |

## 17. Test the tool

These scripts run real commands on real TwinCAT and compare each result with the expected
result. They work on copies in a temporary folder. They read `ST\` but never change it.

| Script | What it tests | Needs |
|---|---|---|
| `e2e\variant-io-sample.ps1` | Per-variant I/O: init, sync, export, round trip, build of all variants | XAE |
| `e2e\variant-sample.ps1` | Activate and verify each variant on UmRT, and verify that the wrong variant fails | XAE, UmRT with license |
| `e2e\all-commands.ps1` | Every command: wrong command lines, all checks (with generated failures), init/sync/build/export, compile error on the correct line, incremental sync, git hook, read-only runtime checks | XAE, UmRT (run `variant-sample.ps1` first) |

```powershell
.\e2e\all-commands.ps1
```

Each script ends with `E2E PASSED` (exit 0) or `E2E FAILED` (exit 1) and a table of all steps.
`all-commands.ps1` keeps the full output of each step in `<work folder>\logs\`.

Unit tests (no TwinCAT): `dotnet test beckhoffAutomationInterface.Tests`.

## 18. All commands

| Command | Opens TwinCAT? |
|---|---|
| `bai doctor` | No |
| `bai init <source> --into <folder> [--name N]` | Yes |
| `bai sync code\|libs\|io\|events\|variants\|safety\|tasks\|all <source> --project <p> [--incremental] [--clean] [--confirm-delete] [--confirm-delete-io] [--variant V]` | Yes (`events`: no) |
| `bai build <project> [--plc N] [--clean] [--variant V \| --all-variants] [--define NAME]...` | Yes |
| `bai check parse\|format\|links <source>` | No |
| `bai check events <source> --project <p>` | No |
| `bai export code\|libs\|io\|events\|links\|all <source> --project <p> [--plc N] [--overwrite]` | Yes (`events`: no) |
| `bai export object <Name> <source> --project <p> [--plc N]` | Yes |
| `bai activate <project> --target umrt\|local [--variant V] [--plc N] [--define NAME]... [--yes]` | Yes (only with `--yes`) |
| `bai verify --target umrt\|local --expect <expect.json> [--variant V] [--timeout S]` | No |
| `bai test --target umrt\|local --scenarios <file or folder> [--variant V] [--variants <variants.xml> \| --groups G1,G2] [--tags a,b] [--junit <file>] [--timeout S]` | No |

For each command that opens TwinCAT: `--shell xae|vs2022`.
For `init`, `sync`, `build`, `check`, `export`: `--config <folder>`, `--no-config`.
Add `--help` after a command to see its usage.
