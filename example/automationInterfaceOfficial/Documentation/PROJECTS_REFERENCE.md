# Projects Reference — Catalog, Use-Cases & Feature Matrix

Consolidated reference for all 16 TwinCAT Automation Interface examples. Use this file to find the right example for a task, compare languages, and understand what each project demonstrates.

---

## 📋 All Projects at a Glance

| # | Project | C++ | C# | Python | Category |
|---|---|:---:|:---:|:---:|---|
| 1 | ActivateConfigurationRemoteTC2 | ✓ | ✓ | ✓ | Configuration |
| 2 | ActivatePreviousConfigurationTC2 | ✗ | ✓ | ✓ | Configuration |
| 3 | EtherCATLinkingTC2 | ✓ | ✓ | ✓ | EtherCAT |
| 4 | LinkPLCProjectTC2 | ✓ | ✓ | ✗ | PLC Project |
| 5 | LinkVariablesTC2 | ✓ | ✓ | ✓ | Real-time Data |
| 6 | AddingRoutesTC2 | ✓ | ✗ | ✗ | Network |
| 7 | ScanBoxesTC2 | ✓ | ✗ | ✗ | Discovery |
| 8 | SysManSamples | ✓ | ✗ | ✗ | System Mgmt |

**Locations**: `/C++/<Project>/`, `/CSharp/<Project>/`, `/Python/<Project>/`

---

## 🔍 Project Details

### 1. ActivateConfigurationRemoteTC2
**Purpose**: Remotely activate a TwinCAT configuration.
**Key API**: `ITcSysManager::ActivateConfiguration()`, `AttachProject()`, `GetConfiguration()`, `StartEngine()`
**Use cases**: Automated deployment, CI/CD pipelines, remote provisioning.
**Per-language notes**:
- C++: Native COM implementation (`.sln`), error handling via `HRESULT`.
- C#: PowerShell script + C# wrapper classes.
- Python: COM via `pywin32`, exception handling with `com_error`.

### 2. ActivatePreviousConfigurationTC2
**Purpose**: Revert to a previous TwinCAT configuration.
**Key API**: `ITcSysManager::ActivateConfiguration()` (with prior config), configuration state/version management.
**Use cases**: Rollback, disaster recovery, testing procedures.
**Languages**: C# (PowerShell), Python. *(No C++ example provided.)*

### 3. EtherCATLinkingTC2
**Purpose**: Build an EtherCAT topology — create Master/Slave devices, boxes, and terminals, and link them.
**Key API**: `ITcSmTreeItem::CreateChild()` (SubType 111=Master, 130=Slave, 9099=Device/Terminal), `ProduceXml()` / `ConsumeXml()`.
**Use cases**: EtherCAT system setup, automated device configuration, system integration.
**Official doc**: "Building an EtherCAT topology" — see [API_AND_ONLINE_DOCS.md](./API_AND_ONLINE_DOCS.md#ethercatlinkingtc2--building-an-ethercat-topology)
**Languages**: C++ (native), C# (managed wrapper), Python (scripting wrapper).

### 4. LinkPLCProjectTC2
**Purpose**: Link/load/build PLC projects programmatically.
**Key API**: `ITcPlcProject::Link()`, `Build()`, `ITcSysManager::GetProjectHandle()`.
**Use cases**: Automated PLC deployment, build-pipeline automation.
**Languages**: C++, C#. *(No Python example provided.)*

### 5. LinkVariablesTC2
**Purpose**: Link to PLC variables for real-time read/write access.
**Key API**: `ITcBindVariable::Link()`, `Read()`, `Write()`, `UnLink()` — handle lifecycle management.
**Use cases**: Data monitoring, real-time integration, external system communication.
**Languages**: C++ (native COM), C# (managed wrapper), Python (COM bindings, data streaming).

### 6. AddingRoutesTC2
**Purpose**: Add and manage AMS network routes.
**Key API**: `ITcRoute::Create()`, route/network topology configuration.
**Use cases**: Multi-network TwinCAT systems, network bridge setup.
**Language**: C++ only.

### 7. ScanBoxesTC2
**Purpose**: Scan the network to discover/enumerate TwinCAT boxes and determine online device addresses.
**Key API**: `ITcSysManager::ScanDevices()`, device enumeration.
**Official doc**: "Scan Devices" — see [API_AND_ONLINE_DOCS.md](./API_AND_ONLINE_DOCS.md#scanboxestc2--device-scanning)
**Use cases**: System inventory, automated setup, network reconnaissance.
**Language**: C++ only.

### 8. SysManSamples
**Purpose**: Grab-bag of general system management operations/reference implementations.
**Use cases**: Learning reference, pattern library for common tasks not covered by the other examples.
**Language**: C++ only.

---

## 🎯 Find an Example by Task

| Task | Example | Languages | Est. time to understand |
|---|---|---|---|
| Activate a configuration | ActivateConfigurationRemoteTC2 | C++/C#/Python | 10-15 min |
| Roll back a configuration | ActivatePreviousConfigurationTC2 | C#/Python | 10 min |
| Configure EtherCAT devices/topology | EtherCATLinkingTC2 | C++/C#/Python | 20-30 min |
| Load/link a PLC project | LinkPLCProjectTC2 | C++/C# | 15-20 min |
| Real-time variable read/write | LinkVariablesTC2 | C++/C#/Python | 25-35 min |
| Discover TwinCAT boxes on network | ScanBoxesTC2 | C++ | 15 min |
| Add/manage network routes | AddingRoutesTC2 | C++ | 15 min |
| General reference / misc patterns | SysManSamples | C++ | 20-30 min |

---

## 📊 Feature Availability Matrix

```
┌────────────────────────────┬─────┬─────┬────────┐
│ Feature                    │ C++ │ C#  │ Python │
├────────────────────────────┼─────┼─────┼────────┤
│ Config Activation          │  ✓  │  ✓  │   ✓    │
│ Config Rollback             │  ✗  │  ✓  │   ✓    │
│ EtherCAT Setup              │  ✓  │  ✓  │   ✓    │
│ PLC Project Linking         │  ✓  │  ✓  │   ✗    │
│ Real-time Variable Access   │  ✓  │  ✓  │   ✓    │
│ Route Management            │  ✓  │  ✗  │   ✗    │
│ System/Box Discovery        │  ✓  │  ✗  │   ✗    │
│ General System Management   │  ✓  │  ✗  │   ✗    │
└────────────────────────────┴─────┴─────┴────────┘
```

---

## ⭐ Cross-Language Comparison (for examples available in all 3 languages)

| Aspect | C++ | C# | Python |
|---|:---:|:---:|:---:|
| Performance | ⭐⭐⭐⭐⭐ | ⭐⭐⭐⭐ | ⭐⭐⭐ |
| Development speed | ⭐⭐ | ⭐⭐⭐⭐ | ⭐⭐⭐⭐⭐ |
| Memory efficiency | ⭐⭐⭐⭐⭐ | ⭐⭐⭐ | ⭐⭐⭐ |
| Ease of integration | ⭐⭐⭐ | ⭐⭐⭐⭐⭐ | ⭐⭐⭐⭐ |
| Real-time suitability | ⭐⭐⭐⭐⭐ | ⭐⭐⭐⭐ | ⭐⭐⭐ |

Use C++ for max performance/native integration, C# for .NET ecosystems and rapid enterprise development, Python for scripting/automation/data-analysis workflows.

---

## 🚀 Common Workflows

**Deploy a new configuration**: Study `ActivateConfigurationRemoteTC2` → adapt config name/target → run.

**Monitor real-time data**: Study `LinkVariablesTC2` → learn variable-linking/handle pattern → add your variable names → run collection loop.

**Automated system setup end-to-end**: `ScanBoxesTC2` (discover) → `AddingRoutesTC2` (network) → `EtherCATLinkingTC2` (device topology) → activate configuration.

**Build automation pipeline**: `LinkPLCProjectTC2` (load project) → `ActivateConfigurationRemoteTC2` (deploy) → reference `SysManSamples` for supporting ops.

---

## 🎓 Suggested Learning Path

1. **Beginner** (1-2h): `ScanBoxesTC2` → `ActivateConfigurationRemoteTC2` → `AddingRoutesTC2`
2. **Intermediate** (2-4h): `EtherCATLinkingTC2` → `LinkPLCProjectTC2`
3. **Advanced** (4h+): `LinkVariablesTC2` (real-time perf) → `SysManSamples` → combine multiple examples into an integrated solution

---

## 📞 Troubleshooting Pointers

| Problem | Look at |
|---|---|
| Can't find/init COM interface | [LANGUAGE_GUIDES.md](./LANGUAGE_GUIDES.md) — COM Interop sections |
| Configuration won't activate | ActivateConfigurationRemoteTC2 error-handling code |
| EtherCAT device/topology issues | EtherCATLinkingTC2 + [API_AND_ONLINE_DOCS.md](./API_AND_ONLINE_DOCS.md) EtherCAT section |
| Variable link/read/write failing | LinkVariablesTC2 handle-management code |
| Performance concerns | Prefer C++ implementation; see performance notes above |

For official API references and Beckhoff documentation URLs tied to each example, see [API_AND_ONLINE_DOCS.md](./API_AND_ONLINE_DOCS.md).
For setup/build instructions per language, see [LANGUAGE_GUIDES.md](./LANGUAGE_GUIDES.md).
