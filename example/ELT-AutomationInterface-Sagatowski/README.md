# The ELT and the TwinCAT Automation Interface (3-part series)

> Converted from the "SAGATOWSKI GMBH" blog series by the same author:
>
> - [Part 1 of 3](https://www.sagatowski.com/posts/the_elt_and_the_twincat_automation_interface_one/) — the problem & context
> - [Part 2 of 3](https://www.sagatowski.com/posts/the_elt_and_the_twincat_automation_interface_two/) — DTE setup + creating AMS routes
> - [Part 3 of 3](https://www.sagatowski.com/posts/the_elt_and_the_twincat_automation_interface_three/) — target selection, boot project, activation & restart
>
> This folder contains the article series converted to Markdown, plus the
> C# code samples extracted/assembled into runnable `.cs` files:
> [`Target.cs`](Target.cs), [`AMSRoutes.cs`](AMSRoutes.cs),
> [`AutomationInterfaceXml.cs`](AutomationInterfaceXml.cs) and
> [`Program.cs`](Program.cs).

## Part 1 — The Problem

The **Extremely Large Telescope (ELT)** is a telescope under
design/construction with a 39-meter segmented primary mirror — once
finished, it will be the largest optical telescope built. It's a huge,
multi-disciplinary engineering project (electrical, mechanical, optical and
software engineering) involving universities, industry and organizations
across the globe.

One subsystem of the ELT alone has **132 Beckhoff PLCs** running TwinCAT 3.
Creating and deploying software to a single TwinCAT project normally
involves many steps, most of which are one-time activities (writing POUs,
defining I/O, referencing libraries, testing, tagging in version control,
etc.). But some activities have to be repeated **for every single target
PLC**:

- Configuring the target and installing necessary software (IP addresses,
  OPC-UA server, etc.)
- Creating an AMS route to the target
- Selecting the target for deployment of the software
- Activating the configuration on the target

Doing this manually for 132 PLCs is slow (potentially a week of work per
upgrade) and error-prone because of the human intervention required at
every step. Since the ELT needs to stay available for astronomers between
observation runs, software upgrades must happen quickly and reliably —
which means this process needs to be **automated**.

### Visual Studio DTE vs. the TwinCAT Automation Interface

TwinCAT 3's IDE is integrated into Visual Studio. Microsoft's **Development
Tools Environment (DTE)** gives programmatic access to generic Visual
Studio functionality (build/rebuild/clean, opening solutions, etc.), but it
knows nothing about TwinCAT-specific concepts. For everything TwinCAT
specific — task creation, PLC projects, I/O configuration, library
referencing, target selection, activating configurations, and so on — you
need the **TwinCAT Automation Interface (AI)**.

Think of the Automation Interface as a door that gives you access to nearly
all TwinCAT-specific functionality in Visual Studio, without requiring a
human to click through the IDE — it can all be done programmatically from
a machine.

### Test Environment Options

1. Wire up all 132 physical PLCs — not feasible.
2. Test with two or more physical PLCs — good for proof-of-concept.
3. **Virtualization** — since Beckhoff PLCs are essentially standard PCs,
   the TwinCAT runtime can run inside a VM (Windows 7/10). Real-time
   properties are terrible in a VM, but that doesn't matter for testing
   mass-deployment logic. You can clone one VM (with TwinCAT 3 installed)
   as many times as needed, each with a different IP address / AmsNetId.

The next part gets practical and shows how to build a .NET/C# console
application to solve this using the Automation Interface.

---

## Part 2 — DTE Setup & Creating AMS Routes

Deploying software to a single PLC breaks down into eight steps:

1. Starting the version of Visual Studio that was used to create the project
2. Opening the solution
3. Adding an AMS route to the target through the AMS router
4. Selecting the target device
5. Enabling the autostart boot flag
6. Selecting the target architecture
7. Enabling the boot project
8. Activation of the configuration

Steps 1 & 2 only need the **DTE** (pure Visual Studio interactions); steps
3-8 require the **Automation Interface (AI)**.

You can write this in any COM-capable language (C++, .NET) or scripting
language (PowerShell, IronPython) — this series uses C#/.NET since that's
what most Beckhoff documentation and samples use.

### Step 1 & 2 — Start Visual Studio, Open the Solution

Create a C#/.NET console application. Add references to `EnvDTE` and
`EnvDTE80` (Assemblies → Extensions) to get access to the VS DTE, and add a
COM reference to **"Beckhoff TwinCAT XAE Base X.Y Type Library"** to get
access to the Automation Interface:

```csharp
using EnvDTE80;
using TCatSysManagerLib;
```

Initialize the DTE for a specific installed Visual Studio version (version
numbers below — in production, prefer reading the actual version dynamically
from the TwinCAT project's `.sln` file rather than hardcoding it):

| Visual Studio            | Version |
| ------------------------ | ------- |
| Visual Studio 2019       | 16.0    |
| Visual Studio 2017       | 15.0    |
| Visual Studio 2015       | 14.0    |
| Visual Studio 2013       | 12.0    |
| Visual Studio 2012       | 11.0    |
| Visual Studio 2010       | 10.0    |
| Visual Studio 2008       | 9.0     |
| Visual Studio 2005       | 8.0     |
| Visual Studio .NET 2003  | 7.1     |
| Visual Studio .NET 2002  | 7.0     |

```csharp
EnvDTE80.DTE2 dte = System.Type.GetTypeFromProgID("VisualStudio.DTE.15.0");
dte.SuppressUI = true;
dte.MainWindow.Visible = false;
dte.UserControl = false;
```

- If the requested VS version isn't installed, `GetTypeFromProgID()` throws — handle it.
- `SuppressUI`/`Visible = false` keep Visual Studio from popping up UI.
- **`dte.UserControl = false` is critical** — it guarantees VS shuts down
  properly once the DTE is no longer used. Without it, you can end up with
  hundreds of orphaned Visual Studio processes eating system resources
  (learned the hard way, per the author).

Open the solution and grab the (first) project:

```csharp
EnvDTE.Solution visualStudioSolution = dte.Solution;
visualStudioSolution.Open(@filePath);

EnvDTE.Project pro = visualStudioSolution.Projects.Item(1);
```

`@filePath` is the full path to the TwinCAT project's `.sln` file, normally
supplied as a program input argument (the author suggests
[NDesk.Options](http://www.ndesk.org/Options) for CLI argument parsing).
Note steps 1 & 2 only use the VS DTE — the Automation Interface hasn't
been touched yet.

### Step 3 — Add an AMS Route

Creating AMS routes manually is slow (only one at a time), so it's a good
automation target. Two strategies:

1. **Broadcast search** via the AI, adding every device found.
2. **Statically define all targets** (IP address, AmsNetId, etc.) and add
   them from that data.

Broadcast search seems convenient but has drawbacks: not all networks
allow it (unwanted traffic on deterministic networks), a PLC's firewall
might block the broadcast (it uses **UDP port 48899**), and there could be
routers/devices on the network you don't want a route to (e.g.
`localhost`) — requiring an exception list anyway, defeating the purpose.
The static-target approach (#2) is preferred.

Beckhoff's documentation shows the XML needed to create an AMS route via
`ConsumeXml()`. Example with three hosts:

```xml
<?xml version="1.0" encoding="ISO-8859-1"?>
<TreeItem>
    <ItemName>Route Settings</ItemName>
    <PathName>TIRR</PathName>
    <RoutePrj>
        <TargetList>
            <BroadcastSearch>false</BroadcastSearch>
        </TargetList>
        <AddRoute>
            <RemoteName>CX-4286EE</RemoteName>
            <RemoteNetId>10.0.2.15.1.1</RemoteNetId>
            <RemoteIpAddr>192.168.43.85</RemoteIpAddr>
            <UserName>Administrator</UserName>
            <Password>1</Password>
            <NoEncryption/>
        </AddRoute>
        <AddRoute>
            <RemoteName>CX-4286F1</RemoteName>
            <RemoteNetId>10.0.3.22.1.1</RemoteNetId>
            <RemoteIpAddr>192.168.43.86</RemoteIpAddr>
            <UserName>Administrator</UserName>
            <Password>1</Password>
            <NoEncryption/>
        </AddRoute>
        <AddRoute>
            <RemoteName>CX-4253DD</RemoteName>
            <RemoteNetId>10.0.2.16.1.1</RemoteNetId>
            <RemoteIpAddr>192.168.43.87</RemoteIpAddr>
            <UserName>Administrator</UserName>
            <Password>1</Password>
            <NoEncryption/>
        </AddRoute>
    </RoutePrj>
</TreeItem>
```

Username/password are likely the same for all PLCs and could be
hardcoded, but storing credentials as plain text is not recommended in
general — special handling is warranted (out of scope for this series).
Otherwise, each target needs a hostname, AmsNetId and IP address — storable
in a small XML config file whose path is a program input argument. The
article uses .NET's built-in
[`XmlDocument`](https://learn.microsoft.com/en-us/dotnet/api/system.xml.xmldocument?view=netframework-4.8)
class to parse the config and build the XML string consumed by
`ConsumeXml()`.

Model types — see [`Target.cs`](Target.cs) and [`AMSRoutes.cs`](AMSRoutes.cs):

```csharp
class Target {
    public string hostName;
    public string netId;
    public string ipAddr;
    public string username;
    public string password;
}

public class AMSRoutes {
    public ArrayList items = new ArrayList();
}
```

An `AutomationInterfaceXML` singleton class builds the route-creation XML
string from an `AMSRoutes` list — see
[`AutomationInterfaceXml.cs`](AutomationInterfaceXml.cs):

```csharp
class AutomationInterfaceXML {
    private AutomationInterfaceXML() { }

    public static string CreateRoutesXMLString(AMSRoutes routesList) {
        // ... builds <TreeItem><ItemName>Route Settings</ItemName>...
    }
}
```

Using it:

```csharp
string routesXmlString = AutomationInterfaceXml.CreateRoutesXMLString(routesToBeAdded);
routes.ConsumeXml(routesXmlString);
```

Now to actually create the routes through the AI. Any Visual Studio
project can host the operation — it doesn't need to be the target project
being deployed. Get the main AI interface, `ITcSysManager`, from the
loaded project:

```csharp
ITcSysManager10 sysManager = pro.Object;
```

`ITcSysManager` is the root of the Automation Interface — it gives access
to the most basic TwinCAT operations: creating/saving/activating
configurations, getting/setting the target AmsNetId, browsing the project
tree, and more. Every other AI operation requires a reference to
`ITcSysManager`.

The interface is versioned (`ITcSysManager`, `ITcSysManager9`,
`ITcSysManager10`, ...) — each new version extends the previous one as
Beckhoff adds functionality:

```csharp
public interface ITcSysManager10 : ITcSysManager9
```

A good rule of thumb: use whichever interface version was current/latest
at the time the project was created.

To access items in the TwinCAT XAE tree (not just the visible solution
explorer items, but TwinCAT-specific functionality too), use
`LookupTreeItem()` on `ITcSysManager`, giving an `ITcSmTreeItem`. Beckhoff
provides shortcuts for commonly used paths:

| Shortcut | Meaning                                                            |
| -------- | ------------------------------------------------------------------ |
| `TIIC`   | I/O Configuration                                                  |
| `TIID`   | I/O Configuration ▸ I/O Devices                                    |
| `TIRC`   | Real-Time Configuration                                            |
| `TIRR`   | Real-Time Configuration ▸ Route Settings                           |
| `TIRT`   | Real-Time Configuration ▸ Additional Tasks                         |
| `TIRS`   | Real-Time Configuration ▸ Real-Time Settings                       |
| `TIPC`   | PLC Configuration                                                  |
| `TINC`   | NC Configuration                                                   |
| `TICC`   | CNC Configuration                                                  |
| `TIAC`   | CAM Configuration                                                  |

For AMS routes, `TIRR` is what we need:

```csharp
ITcSmTreeItem routes = sysManager.LookupTreeItem("TIRR");
```

`ITcSmTreeItem.ConsumeXml(string bstrXML)` lets us feed it the XML string
built earlier:

```csharp
string routesXmlString = AutomationInterfaceXml.CreateRoutesXMLString(routesToBeAdded);
routes.ConsumeXml(routesXmlString);
```

`ConsumeXml()` throws if any route fails to be added. An alternative
strategy is to call `ConsumeXml()` once per PLC (a single-route XML each
time) so you know up front which targets are reachable before doing any
upgrade — useful for deciding whether to continue deploying to the
remaining reachable PLCs if one is unreachable/broken.

---

## Part 3 — Target Selection, Boot Project & Activation

Recall the eight steps; part 2 covered 1-3, this part covers **4-8**, once
for each PLC:

```csharp
// For every AmsNetId (PLC)
foreach (Target t in amsTargets.items)
{
    // Steps 4-8 go here
}
```

### Step 4 — Select the Target Device

Equivalent to picking the AmsNetId in the Target Selection window in the
IDE. Through the AI, this is `ITcSysManager.SetTargetNetId()`:

```csharp
sysManager.SetTargetNetId(t.netId);
```

### Steps 5-8 — Autostart Boot Flag, Boot Project, Activation, Restart

The **autostart boot flag** is information stored on the target device
itself, not in the PLC project. Selecting "Autostart Boot Project" in the
IDE creates an empty file named `Port_xxx.autostart` (where `xxx` is `851`
for the first PLC runtime) on the target, under
`C:\TwinCAT\3.1\Boot\Plc`, which makes that PLC runtime start
automatically. "Activate Boot Project" must also be selected, to activate
the chosen PLC runtime on the target. TwinCAT 3 supports up to **four PLC
runtimes**.

Since PLC projects are independent of each other, the autostart/boot flag
must be set individually for each one. Use the `TIPC` shortcut to get the
PLC tree item, then iterate its children (each castable to
`ITcPlcProject`):

```csharp
ITcSmTreeItem plcTreeItem = sysManager.LookupTreeItem("TIPC");
int plcChildCount = plcTreeItem.ChildCount;
```

```csharp
log.Info("Enabling boot project and setting BootProjectAutostart on " + sysManager.GetTargetNetId());

// Enable autostart-flag on all PLC-projects, as this flag is not stored in the project itself but rather on the target
for (int i = 1; i <= plcChildCount; i++)
{
    ITcSmTreeItem plcProject = plcTreeItem.Child[i];
    ITcPlcProject iecProject = (ITcPlcProject)plcProject;
    iecProject.GenerateBootProject(true);
    iecProject.BootProjectAutostart = true;
}
```

Next, activate the full configuration — equivalent to clicking "Activate
Configuration" in the IDE:

```csharp
sysManager.ActivateConfiguration();
```

And finally, restart the TwinCAT kernel on the target so the new software
is loaded into memory:

```csharp
log.Info("Restarting the TwinCAT kernel on target " + sysManager.GetTargetNetId());
sysManager.StartRestartTwinCAT();
```

### Wrap-up

That's the full flow for deploying to one PLC, repeated for every target
in the list. In production code you'd want to:

- Follow [SOLID](https://en.wikipedia.org/wiki/SOLID) principles and split
  responsibilities into separate classes (one for the VS DTE, one for the
  Automation Interface, one for XML handling, etc.)
- Properly handle exceptions rather than ignore them (this series
  intentionally skips exception handling for clarity)

Using this combination of the VS DTE and the Automation Interface, the
author was able to upgrade all 132 PLCs in this ELT subsystem in about
half an hour — and that could be made even faster by deploying in
parallel rather than in series.

---

## Notes on Adapting These Samples

- Like the other tutorial in this `example` folder, these samples use the
  full .NET Framework COM automation model (`EnvDTE`/`EnvDTE80` +
  `TCatSysManagerLib`) and are meant to be built with **Visual Studio's
  MSBuild** (or referenced via a manually generated interop DLL), not
  plain `dotnet build` from the CLI — see the parent project's
  [REQUIREMENTS.md](../../beckhoffAutomationInterface/REQUIREMENTS.md)
  (section 5) for the two valid approaches.
- [`Program.cs`](Program.cs) assembles steps 1-8 from all three parts into
  a single, linear `Main()` for clarity. As the article notes, in real
  production code you'd split this into multiple focused classes (DTE
  wrapper, AI wrapper, XML builder) and add proper exception handling and
  logging.
- Replace the hardcoded VS DTE ProgID (`VisualStudio.DTE.15.0`), solution
  path, and AMS route credentials/targets with values (or configuration
  input) matching your own environment.
