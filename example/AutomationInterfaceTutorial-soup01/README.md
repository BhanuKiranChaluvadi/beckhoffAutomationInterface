# Beckhoff — Using the TwinCAT Automation Interface (Part 2)

> Converted from the tutorial at
> [soup01.com — "Beckhoff#Using Automation interface"](http://soup01.com/en/2023/03/24/beckhoffusing-automation-interface-2/)
> (published 2023-03-24). This folder contains the article's text content
> converted to Markdown plus the C# code samples extracted into runnable
> `.cs` files (see the `Implementation-*.cs` files next to this README).

## What is the TwinCAT Automation Interface?

The TwinCAT Automation Interface allows a TwinCAT user to automatically
create programs and operate tools via an API. It can be implemented from
various languages (Windows PowerShell, IronPython, VBScript, C#, VB.NET,
etc.). This article implements:

- PLC project creation
- Adding a Profinet Controller and Devices
- Importing a GVL (Global Variable List)

In the conventional/manual approach, TwinCAT projects are generally created
by hand through the IDE. This is error-prone and time-consuming. By using
the Automation Interface, the hardware configuration of the TwinCAT project
and the import of programs are created automatically and can be adapted for
different applications/machine variants.

## System Requirements

- The Automation Interface engineering PC and the actual IPC's TwinCAT
  Runtime version **must be the same or higher** than the engineering PC
  version.
- The Automation Interface script can be executed on 32-bit or 64-bit
  platforms, but you must **compile and execute the script/console app in
  32-bit mode**.

### TwinCAT Version

TwinCAT Version **3.1 Build 4020.0 or higher** is required.

### Programming Language

**C#** and **Visual Basic .NET** are recommended.

## Create the Console App

1. Start Visual Studio.
2. Create a new project.
3. Choose a **Console Application (C#)** project template.
4. Configure the project name and continue.

## Installation

Before using the Automation Interface, a little groundwork is required.

### .NET Packages

Download and install the required .NET packages/tools.

### NuGet Packages

If you are using a Visual Studio version lower than what ships with NuGet
built in, download and install the NuGet package manager from
<https://www.nuget.org/downloads>.

### EnvDTE Package

Go to **NuGet → Browse** and search for `EnvDTE` to install this package
(used to drive the Visual Studio automation object model — `EnvDTE.DTE`).

### Add Reference

Add the TwinCAT Type Library reference to your C# project:

- Reference → COM → check **"Beckhoff TwinCAT XAE Base 3.3 Type Library"**
  (or the version installed on your system) → OK.

### Check Your DTE Version

Before programming with the Automation Interface, check the version of the
Visual Studio DTE object registered on your machine:

- Open the Registry Editor → `HKEY_CLASSES_ROOT` → look for
  `VisualStudio.DTE.<version>` (e.g. `VisualStudio.DTE.16.0` for VS2019).

This ProgID is what you pass to `Type.GetTypeFromProgID(...)` in code.

---

## Implementation 1 — Create the TwinCAT Project

Shows how to use the TwinCAT Automation Interface to create a TwinCAT
project with C#, launching Visual Studio in the process.

See [`Implementation1_CreateTwinCATProject.cs`](Implementation1_CreateTwinCATProject.cs).

**Result**: Visual Studio is launched by the Automation Interface and a new
TwinCAT project is created on disk.

### `ITcSysManager`

`ITcSysManager` is the main interface of the TwinCAT Automation Interface —
it provides all the basic operations for a TwinCAT project. It is obtained
from the DTE's `Solution.AddFromTemplate(...)` call:

```csharp
ITcSysManager sysManager = (ITcSysManager)dte.Solution
    .AddFromTemplate(template, @"C:\Users\chungw\Downloads\Myp\Solution1", "MyProject")
    .Object;
```

---

## Implementation 2 — Add a PLC Project

Shows how to create a Standard PLC Project **without** launching the full
Visual Studio UI interactively for that step, and how to import a GVL from
a PLCopenXML file.

See [`Implementation2_AddPlcProject.cs`](Implementation2_AddPlcProject.cs).

**Result**: A Standard PLC Project is inserted into the TwinCAT project, and
a Global Variable List named `GVL_1` is created inside it.

### `ITcSmTreeItem`

`ITcSmTreeItem` lets you get an item inside your TwinCAT project tree via
`LookupTreeItem`:

```csharp
ITcSmTreeItem plc = sysManager.LookupTreeItem("TIPC");
```

This creates the PLC project from a template:

```csharp
ITcSmTreeItem newPlc = plc.CreateChild(plcName, 0, "", myStandardPlcProjectTemplate);
```

And this retrieves the PLC Project object:

```csharp
ITcSmTreeItem plcProject = sysManager.LookupTreeItem("TIPC^MyPLC^MyPLC Project");
```

The path string `"TIPC^MyPLC^MyPLC Project"` is the absolute path of the
object inside the TwinCAT project — you can find it in the **PathName**
property of any object in TwinCAT, with path segments separated by `^`.

### `ITcPlcIECProject`

Also used to import data from the PLCopenXML standard format and perform
library operations. In the full TwinCAT IDE you have **Import PLCopenXML**,
**Export PLCopenXML**, **Save as Library**, and **Save as library and
install** — but through the Automation Interface only **Import
PLCopenXML**, **Export PLCopenXML**, and **Save as Library** are available.

```csharp
ITcPlcIECProject importExport = (ITcPlcIECProject)plcProject;
importExport.PlcOpenImport(@"C:\Users\chungw\Downloads\Myp\GVL_1.xml",
    (int)PLCIMPORTOPTIONS.PLCIMPORTOPTIONS_NONE);
```

---

## Implementation 3 — Import a Library

Shows how to import PLC libraries (e.g. the Beckhoff motion control
libraries) into the PLC project using the Automation Interface.

See [`Implementation3_ImportLibrary.cs`](Implementation3_ImportLibrary.cs).

**Result**: `Tc2_MC2` and `Tc2_MC2_Drive` libraries are imported into the
TwinCAT project's library references.

### `ITcPlcLibraryManager`

`ITcPlcLibraryManager` gives access to the PLC library manager inside
TwinCAT. `AddLibrary` adds a library to the PLC project and requires
`Name` / `Version` / `Company` parameters:

```csharp
ITcSmTreeItem references = sysManager.LookupTreeItem("TIPC^MyPLC^MyPLC Project^References");
ITcPlcLibraryManager libManager = (ITcPlcLibraryManager)references;

libManager.AddLibrary("Tc2_MC2", "*", "Beckhoff Automation GmbH");
libManager.AddLibrary("Tc2_MC2_Drive", "*", "Beckhoff Automation GmbH");
```

Here, `Tc2_MC2` is the library `Name`, `*` means "any/latest" `Version`,
and `Beckhoff Automation GmbH` is the `Company`.

---

## Implementation 4 — Create a Profinet IO Controller

Shows how to use the Automation Interface to insert a Profinet IO
Controller into the TwinCAT project's I/O configuration.

See [`Implementation4_CreateProfinetController.cs`](Implementation4_CreateProfinetController.cs).

**Result**: A `PNIO Controller` device is inserted into the I/O
Configuration.

Almost all TwinCAT tree parts can be exported as XML (right-click the item
→ **Extension → Selected Items → Export XML Description**), which is
useful for discovering the exact XML fragment/parameters accepted by
`ConsumeXml`.

`LookupTreeItem("TIID")` returns the **I/O** node of the TwinCAT project.
Once you have the I/O object, you can add a Profinet Controller, an
EtherCAT Master, etc. via `CreateChild`:

```csharp
ITcSmTreeItem io = sysManager.LookupTreeItem("TIID");
ITcSmTreeItem profinetController = io.CreateChild("PNIO Controller", 113, null, null);
```

The number `113` corresponds to the Profinet Controller (RT) device type.

You can then change properties on the created object using `ConsumeXml`,
using the XML fragments discoverable via the Export XML Description
feature mentioned above:

```csharp
profinetController.ConsumeXml(
    "<TreeItem><DevicePnControllerDef><IpSettings><IP>#x0103a8c0</IP></IpSettings></DevicePnControllerDef></TreeItem>");

profinetController.ConsumeXml(
    "<TreeItem><DevicePnControllerDef><IpSettings><Subnet>#x00ffffff</Subnet></IpSettings></DevicePnControllerDef></TreeItem>");
```

`<IpSettings><IP>#x0103a8c0</IP></IpSettings>` changes the controller's IP
address. The hex value `#x0103a8c0` decodes byte-by-byte (in reverse) to
the dotted-decimal IP `192.168.3.1` (bytes: `01 03 a8 c0` → reversed
`c0 a8 03 01` → `192.168.3.1`).

---

## Implementation 5 — Add an S210 Profinet Device

Finally, adds a Profinet device (a Siemens Sinamics **S210** drive) as a
child of the Profinet Controller created in Implementation 4, sets its
name/IP/gateway, and inserts PROFIsafe/standard telegrams.

See [`Implementation5_AddS210Device.cs`](Implementation5_AddS210Device.cs).

```csharp
ITcSmTreeItem s210 = profinetController.CreateChild(
    "PNDevices_1_S210", 97, null,
    @"C:\TwinCAT\3.1\Config\Io\Profinet\GSDML-V2.25-Siemens-Sinamics_S210-20220506.xml#0x0002020C");
```

The 4th parameter is a path into the device's **GSDML** file, with the
`#0x0002020C` suffix specifying the exact device version (here, `V5.1`).

Renaming the box and changing its IP/gateway is done the same way as
before, via `ConsumeXml`:

```csharp
s210.ConsumeXml("<TreeItem><ItemName>box1234</ItemName></TreeItem>");
s210.ConsumeXml("<TreeItem><PnIoBoxDef><IpSettings><IP>#x0a03a8c0</IP></IpSettings></PnIoBoxDef></TreeItem>");
s210.ConsumeXml("<TreeItem><PnIoBoxDef><IpSettings><Gateway>#x0103a8c0</Gateway></IpSettings></PnIoBoxDef></TreeItem>");
```

Navigating down the tree to reach the drive's telegram configuration:

```csharp
ITcSmTreeItem s210Api = s210.get_Child(1);   // the device's API object
ITcSmTreeItem drive = s210Api.get_Child(2);  // "Term 2 (Drive)"
```

Finally, adding the PROFIsafe and standard telegrams:

```csharp
ITcSmTreeItem safeTelegram = drive.CreateChild("ProfiSAFE_Telegram30", s210Telegram30, null, null);
ITcSmTreeItem stdTelegram  = drive.CreateChild("StandardTelegram3", s210Telegram3, null, null);
```

**Result**: A Profinet device named `box1234` is added under the `PNIO
Controller`, with `ProfiSAFE_Telegram30` and `StandardTelegram3` installed
under its drive terminal.

---

## Notes on Adapting These Samples

- These samples target the **full .NET Framework + Visual Studio COM
  automation model** (`EnvDTE.DTE`, `TCatSysManagerLib` via
  `<COMReference>` or a generated interop DLL) — they are not directly
  runnable from `dotnet build`/CLI unless you use the manual-interop-DLL
  approach documented in the parent project's
  [REQUIREMENTS.md](../../beckhoffAutomationInterface/REQUIREMENTS.md)
  (section 5).
- The project **must build/run as 32-bit** (`x86`), matching the
  Automation Interface's bitness requirement.
- Paths such as `C:\Users\chungw\Downloads\Myp`, the `VisualStudio.DTE.16.0`
  ProgID, and the GSDML file name/version are specific to the original
  author's machine and TwinCAT/device catalog — adjust them to match your
  own environment (Visual Studio version installed, project output
  directory, and the GSDML files shipped with your TwinCAT installation
  under `C:\TwinCAT\3.1\Config\Io\Profinet\`).
