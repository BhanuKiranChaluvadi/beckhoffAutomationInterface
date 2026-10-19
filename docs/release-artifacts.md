# Offline release artifact commands

BAI owns reusable package, library-cache, toolchain and EtherCAT catalog operations.
These commands run without XAE, a PLC runtime, or a Python/PowerShell subprocess.
Machine layouts, PLC symbol checks, approved dependency baselines and release policy
remain in the PLC repository. A package integrity pass is not deployment approval.

All commands accept `--json` for a machine-readable result. Exit 0 means success,
exit 1 means an artifact or I/O check failed, and exit 2 means invalid arguments.
New archives and baseline files never overwrite an existing path. Archives are
verified before the completed file is published. Failed temporary archives are removed.

## Verify a package

```powershell
bai package verify --package C:\release\Google.zip --sha256 <hash-from-retained-release-record>
```

Both schema 1 and schema 2 PLC packages are supported. BAI checks the full inventory,
sizes, SHA256 hashes, required boot files, and schema 2 identity expectations. It rejects
unlisted files, duplicate names, symbolic links and unsafe Windows paths. Omitting
`--sha256` checks internal integrity only; it does not authenticate the selected ZIP.
The current package contract describes the PLC on ADS port 851.

## Create a package

```powershell
bai package create --directory C:\release\payload --metadata C:\release\metadata.json --output C:\release\Google.zip
```

The payload contains `boot/`, `project.zip`, `evidence.json` and, for schema 2,
`identity.expect.json`. It must not contain `release.json`. The metadata file contains
the existing package fields (`schemaVersion`, `status`, `kind`, `deploymentApproved`,
machine/platform/source identity and any additional evidence), but no `files` field.
BAI generates the inventory and `release.json` and checks the completed package.
`status` must be `Packaged`; `deploymentApproved` must be false. The caller is
responsible for compiled identity, simulation-off, clean-tag and hardware acceptance
checks. BAI does not infer these from a machine name.

## Capture and verify resolved libraries

```powershell
bai libraries lock --project C:\build\Google.tsproj --machine Google --variant Google --platform "TwinCAT RT (x64)" --output C:\release\Google.lock.json
bai libraries verify --project C:\build\Google.tsproj --machine Google --variant Google --platform "TwinCAT RT (x64)" --lock C:\release\libraries.lock.json --sha256 <approved-lock-hash>
```

The project must contain exactly one PLC. BAI checks the compiled TMC's platform and
explicit variant and captures all `_Libraries` files, including transitive dependencies.
For projects with no variant, omit `--variant`; this explicitly means an empty variant.
`--manifest <libraries.xml>` also checks declared names and requested versions.
Verification accepts the retained multi-machine schema 1 locks. Capture writes a new,
single-machine schema 1 lock; the PLC release workflow can combine these records.
Review a newly captured baseline before using its hash. Never regenerate an approved
baseline automatically to make a failed comparison pass.

## Capture and verify the toolchain

```powershell
bai toolchain capture --components "C:\Program Files (x86)\Beckhoff\TwinCAT\3.1\Components" --xae-shell "C:\Program Files (x86)\Beckhoff\TcXaeShell" --version 3.1.4026.24 --archive C:\release\toolchain.zip
bai toolchain verify --archive C:\release\toolchain.zip --sha256 <approved-archive-hash> --version 3.1.4026.24 --installed
```

Select the actual XAE shell directory on your PC; the command does not guess it.
The existing schema 1 archives from `toolchain_archive.py` remain readable. `--installed`
also checks the captured directories on this PC. Without it, BAI checks only the archive.
The archive retains bytes; it does not install Windows, register COM or supply licenses.

## Install and verify an ESI catalog

```powershell
bai esi install --system-directory C:\catalog\system --devices C:\catalog\vendor --user-directory C:\catalog\user --report C:\release\esi-install.json
bai esi verify --system-directory C:\catalog\system --devices C:\catalog\vendor --user-directory C:\catalog\user
```

Pass the actual system, vendor and per-user catalog paths. BAI checks every conflict
before copying, preserves existing identical files and never replaces differing files.
`verify` writes no catalog files. The PLC wrapper supplies the NFLite vendor directory;
BAI itself has no machine-specific path defaults. Open a fresh XAE session after installation.

## Acceptance evidence and migration order

Unit tests exercise corrupted payloads, unsafe paths, bad manifests, wrong hashes,
wrong compiled platform/variant, library drift, toolchain drift and ESI conflicts.
`e2e/release-artifacts.ps1` runs the actual executable against a retained compiled
package and project, repacks the payload, compares every file, and checks negative exits.
It never activates a runtime. Keep both failed and corrected runs.

Migration is staged. Package creation/verification and ESI operations have PLC adapters.
Native configuration, saved-project comparison and deployment/rollback remain separate
follow-up migrations. Preserve their existing implementations and acceptance evidence
until their BAI replacements pass the corresponding saved-project and UmRT tests.
The BSD transaction helper must remain executable on BSD; the Windows BAI executable
cannot replace that remote process directly.
