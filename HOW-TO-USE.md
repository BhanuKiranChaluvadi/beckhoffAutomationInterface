# How to use the Beckhoff Automation Interface tool

This guide tells you how to do the usual tasks. Each task has short steps.
For the full reference of all options, see [README.md](README.md).

## 1. What the tool does

You write PLC code as `.st` text files in git. The tool puts these files into a TwinCAT 3
project, compiles the project and tells you the errors. It can also activate the project on a
runtime and read live PLC values to make sure that the program runs correctly.

You do not use the TwinCAT XAE window. The tool never shows a Yes/No dialog box.

## 2. Words in this guide

| Word | Meaning |
|---|---|
| **Source folder** | The folder with your `.st` files and the manifests (`libraries.xml`, `io-devices.xml`, `events.xml`, `variants.xml`). Example: `ST\Shark`. |
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
On `umrt`, the tool disables the EtherCAT devices, because UmRT has no EtherCAT adapter.
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

### 10.3 License

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

3. If your folders are not the default ones, set:

   ```powershell
   $env:BECKHOFF_ST_SOURCE   = "C:\work\ST\Shark"
   $env:BECKHOFF_TWINCAT_DEST = "C:\work\TwinCAT"
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
| 0 | Done, and the result is as expected (`BUILD PASSED`, `VERIFY PASSED`, no problems found) |
| 1 | A problem: compile error, failed check, wrong command line, missing project, or `activate` without `--yes` |

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
| The PLC stays in Config after `activate` | Run `bai doctor`. Look at the license expiry date. Renew the trial license in the TwinCAT window. |
| `verify` shows `FAIL` | The variant on the target is not the one you named, or the code does not do what `expect.json` says. Look at the actual values. |
| Linking fails with `0x98510001` | The `%I*`/`%Q*` variable is not used in the code, so it is not in the process image. Use it in the code. |
| A `TcXaeShell` process stays open | Close it in Task Manager. The tool closes its own shell in about 10 seconds. |

## 17. Test the tool

These scripts run real commands on real TwinCAT and compare each result with the expected
result. They work on copies in a temporary folder. They read `ST\` but never change it.

| Script | What it tests | Needs |
|---|---|---|
| `e2e\variant-io-sample.ps1` | Per-variant I/O: init, sync, export, round trip, build of all variants | XAE |
| `e2e\variant-sample.ps1` | Activate and verify each variant on UmRT, and verify that the wrong variant fails | XAE, UmRT with license |
| `e2e\all-commands.ps1` | Every command: wrong command lines, all checks, Shark init/sync/build/export, compile error on the correct line, incremental sync, git hook, read-only runtime checks | XAE, UmRT (run `variant-sample.ps1` first) |

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
| `bai sync code\|libs\|io\|events\|variants\|all <source> --project <p> [--incremental] [--clean] [--confirm-delete] [--confirm-delete-io] [--variant V]` | Yes (`events`: no) |
| `bai build <project> [--plc N] [--clean] [--variant V \| --all-variants]` | Yes |
| `bai check parse\|format\|links <source>` | No |
| `bai check events <source> --project <p>` | No |
| `bai export code\|libs\|io\|events\|links\|all <source> --project <p> [--plc N] [--overwrite]` | Yes (`events`: no) |
| `bai export object <Name> <source> --project <p> [--plc N]` | Yes |
| `bai activate <project> --target umrt\|local [--variant V] [--plc N] [--yes]` | Yes (only with `--yes`) |
| `bai verify --target umrt\|local --expect <expect.json> [--variant V] [--timeout S]` | No |

For each command that opens TwinCAT: `--shell xae|vs2022`.
For `init`, `sync`, `build`, `check`, `export`: `--config <folder>`, `--no-config`.
Add `--help` after a command to see its usage.
