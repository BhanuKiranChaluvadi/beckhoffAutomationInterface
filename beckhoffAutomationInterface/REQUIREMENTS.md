# Requirements & Environment Setup

This document captures all runtime, build, and tooling requirements discovered for this project. Follow every section before attempting to build or run.

---

## 1. Operating System

- **Windows only** — COM automation (`EnvDTE`, `ITcSysManager`) is a Windows-exclusive technology
- Tested on Windows 10 / Windows 11

---

## 2. .NET SDK & Runtime

| Requirement | Value | Why |
|---|---|---|
| **Target Framework** | `.NET Framework 4.8` | COM interop with `Interop.TCatSysManager.dll` requires full .NET Framework, not .NET Core/5+. The `dotnet build` CLI does not support `<COMReference>` on .NET Core MSBuild. |
| **Platform target** | `x64` | Visual Studio registers its COM objects (`VisualStudio.DTE.17.0`) only in the **64-bit registry**. A 32-bit (x86) process looks up COM in `WOW6432Node`, where VS is absent — causing `REGDB_E_CLASSNOTREG (0x80040154)`. |
| **.NET SDK version** | 6.0 or later | Required to run `dotnet build` and `dotnet run` |

### Verify
```powershell
dotnet --version        # Must be 6.0+
dotnet --info           # Confirm net48 is available
```

---

## 3. Visual Studio 2022 (Version 17.x)

### Why VS 2022 specifically?

TwinCAT 3.1 Build 4026 ships integration binaries for specific VS versions only:

| TwinCAT folder | Visual Studio version | VS year |
|---|---|---|
| `v150` | 15.x | VS 2017 |
| `v160` | 16.x | VS 2019 |
| `v170` | **17.x** | **VS 2022** ← current install |

Beckhoff compiles its VS extension (`TcXaeVsx.17.0.dll`) against each VS SDK. There is **no `v180` folder** in TwinCAT 3.1 Build 4026, so **VS 2026 (18.x) is not supported**. VS 2022 (17.x) is required.

### Required Edition
Any edition works: **Community**, Professional, or Enterprise.

### Required Workload
Install the `.NET desktop development` workload — needed for the managed DTE automation APIs.

### Install command (run as Administrator)
```powershell
# Download and install VS 2022 Community silently
Invoke-WebRequest -Uri "https://aka.ms/vs/17/release/vs_community.exe" -OutFile "$env:TEMP\vs_community.exe"
Start-Process "$env:TEMP\vs_community.exe" -ArgumentList "--quiet --add Microsoft.VisualStudio.Workload.ManagedDesktop --includeRecommended --wait" -Wait -Verb RunAs
```

### Verify
```powershell
Test-Path "C:\Program Files\Microsoft Visual Studio\2022\Community\Common7\IDE\devenv.exe"
# Must return True

# COM registration must be present
$clsid = (Get-ItemProperty "HKLM:\SOFTWARE\Classes\VisualStudio.DTE.17.0\CLSID")."(default)"
(Get-ItemProperty "HKLM:\SOFTWARE\Classes\CLSID\$clsid\LocalServer32")."(default)"
# Must print the devenv.exe path
```

---

## 4. Beckhoff TwinCAT 3.1 XAE

### What is needed
- **TwinCAT 3.1 XAE** (Engineering) — provides `TCatSysManager.tlb`, project templates, and the VS extension
- Build **4026** or later recommended
- Must be installed **after** Visual Studio 2022 so the installer can register into it

### Why the order matters
The TwinCAT installer scans for installed VS versions at install time and registers:
- The `TcXaeVsx.17.0.dll` extension into VS 2022
- The TwinCAT project template (`TwinCAT Project.tsproj`) into VS's template cache
- The `ITcSysManager` COM interfaces into the registry

If TwinCAT was installed before VS 2022, or VS 2022 was installed later, run a **repair**:
```powershell
# Run as Administrator — repairs TwinCAT XAE Base and re-registers into VS
MsiExec.exe /f{23005E9B-9FED-4C05-B4EB-6AC0ECC0BA7F}
```

### Key paths after install

| Path | Purpose |
|---|---|
| `C:\Program Files (x86)\Beckhoff\TwinCAT\3.1\Components\Base\TypeLib\3.4\TCatSysManager.tlb` | COM type library |
| `C:\Program Files (x86)\Beckhoff\TwinCAT\3.1\Components\Base\PrjTemplate\TwinCAT Project.tsproj` | Project template used by `AddFromTemplate` |
| `C:\Program Files (x86)\Beckhoff\TwinCAT\3.1\Components\Base\v170\TcXaeVsx.17.0.dll` | TwinCAT VS 2022 extension DLL |

### Verify
```powershell
Test-Path "C:\Program Files (x86)\Beckhoff\TwinCAT\3.1\Components\Base\PrjTemplate\TwinCAT Project.tsproj"
# Must return True
```

---

## 5. Interop Assembly (`Interop.TCatSysManager.dll`)

The project uses a **manually generated COM interop DLL** (Approach B) so that `dotnet build` works without Visual Studio IDE.

### Namespace
The DLL exports the namespace `Interop.TCatSysManager` — **not** `TCatSysManagerLib`.

```csharp
using Interop.TCatSysManager;   // correct
// using TCatSysManagerLib;     // wrong — will not compile
```

### How it was generated
```powershell
TlbImp.exe "C:\Program Files (x86)\Beckhoff\TwinCAT\3.1\Components\Base\TypeLib\3.4\TCatSysManager.tlb" /out:"Interop.TCatSysManager.dll"
```

`TlbImp.exe` is part of the Windows SDK:
```
C:\Program Files (x86)\Microsoft SDKs\Windows\<version>\bin\NETFX 4.8 Tools\TlbImp.exe
```

### Regenerate if corrupted
```powershell
$tlbimp = Get-ChildItem "C:\Program Files (x86)\Microsoft SDKs\Windows\" -Recurse -Filter "TlbImp.exe" | Select-Object -First 1 -ExpandProperty FullName
& $tlbimp "C:\Program Files (x86)\Beckhoff\TwinCAT\3.1\Components\Base\TypeLib\3.4\TCatSysManager.tlb" /out:".\Interop.TCatSysManager.dll"
```

---

## 6. DTE Version (`VisualStudio.DTE.17.0`)

The ProgID used in code must match the installed Visual Studio version:

| ProgID | Visual Studio | Year |
|---|---|---|
| `VisualStudio.DTE.16.0` | 16.x | VS 2019 |
| `VisualStudio.DTE.17.0` | 17.x | VS 2022 ← **use this** |
| `VisualStudio.DTE.18.0` | 18.x | VS 2026 (not supported by TwinCAT 3.1 Build 4026) |

> **Note:** The `EnvDTE` NuGet package version (currently `8.0.2`) is **independent** of the VS IDE version number. Package `8.0.2` supports all modern VS versions including 17.x.

### Verify registration (run as 64-bit PowerShell)
```powershell
$clsid = (Get-ItemProperty "HKLM:\SOFTWARE\Classes\VisualStudio.DTE.17.0\CLSID")."(default)"
(Get-ItemProperty "HKLM:\SOFTWARE\Classes\CLSID\$clsid\LocalServer32")."(default)"
# Expected output: "C:\Program Files\Microsoft Visual Studio\2022\...\devenv.exe"
```

---

## 7. Common Errors & Fixes

| Error | Root Cause | Fix |
|---|---|---|
| `REGDB_E_CLASSNOTREG (0x80040154)` | App running as x86, VS COM only in 64-bit registry | Set `<PlatformTarget>x64</PlatformTarget>` in `.csproj` |
| `REGDB_E_CLASSNOTREG` on `VisualStudio.DTE.17.0` | VS 2022 not installed, or wrong ProgID | Install VS 2022; check ProgID matches installed version |
| `STG_E_FILENOTFOUND` on `AddFromTemplate` | Template path wrong | Use `C:\Program Files (x86)\Beckhoff\...\PrjTemplate\TwinCAT Project.tsproj` |
| `The template specified cannot be found` | TwinCAT XAE not registered in VS 2022 | Run `MsiExec.exe /f{23005E9B-9FED-4C05-B4EB-6AC0ECC0BA7F}` as admin |
| `CS0246: 'TCatSysManagerLib' not found` | Wrong namespace in `using` directive | Change to `using Interop.TCatSysManager;` |
| `MSB4803: ResolveComReference not supported` | Using `<COMReference>` with `dotnet build` CLI | Use manual `Interop.dll` + `<Reference>` instead |

---

## 8. Build & Run

```powershell
# From project directory
dotnet build
dotnet run

# From workspace root (requires solution file)
dotnet build
dotnet run --project beckhoffAutomationInterface
```

---

## 9. Setup Checklist

- [ ] Windows OS
- [ ] .NET SDK 6.0+ installed
- [ ] Visual Studio 2022 (17.x) installed with `.NET desktop development` workload
- [ ] TwinCAT 3.1 XAE Build 4026 installed **after** VS 2022
- [ ] TwinCAT XAE repaired if VS 2022 was installed after TwinCAT
- [ ] `Interop.TCatSysManager.dll` present in project root
- [ ] Project builds with `dotnet build` (no errors)
- [ ] `VisualStudio.DTE.17.0` resolves correctly (verify with PowerShell above)
