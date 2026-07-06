# API & Official Online Documentation Reference

Maps each local example to the relevant Beckhoff Automation Interface API and official online documentation, and provides a URL index for further browsing.

**Main manual**: https://infosys.beckhoff.com/english.php?content=../content/1033/tcautomationinterface/index.html
(Sections: Forward, Overview, System Requirements, Installation, Configuration, API Reference, Samples, How-To)

---

## 🌐 URL Pattern

```
https://infosys.beckhoff.com/english.php?content=../content/1033/tcautomationinterface/[PAGE_ID].html
https://infosys.beckhoff.com/content/1033/tcautomationinterface/[PAGE_ID].html          (short form)
```

---

## 🔧 Core API Interfaces

| Interface | Purpose | Key Methods | Doc URL |
|---|---|---|---|
| `ITcSysManager` | System/config management | `ActivateConfiguration()`, `GetConfiguration()`, `GetProjectHandle()`, `ScanDevices()`, `AttachProject()` | https://infosys.beckhoff.com/content/1033/tcautomationinterface/12425792395.html |
| `ITcSmTreeItem` | Tree item / device / config-node management | `CreateChild()`, `ProduceXml()`, `ConsumeXml()`, `DeleteChild()`, `GetProperty()` | https://infosys.beckhoff.com/content/1033/tcautomationinterface/12425809675.html (CreateChild: 12425844363) |
| `ITcPlcProject` | PLC project operations | `Link()`, `Build()`, `UnLink()` | — |
| `ITcBindVariable` | Real-time variable access | `Link()`, `Read()`, `Write()`, `UnLink()` | — |
| `ITcRoute` | AMS network route management | `Create()`, `Delete()`, `Find()` | — |
| `ProduceXml` / `ConsumeXml` | XML export/import for tree items | — | 12425842315 / 12425843339 |

---

## 📚 Known Documentation Pages (Topic → URL)

| Topic | URL | Page ID |
|---|---|---|
| Building an EtherCAT topology | https://infosys.beckhoff.com/english.php?content=../content/1033/tcautomationinterface/12425787531.html | 12425787531 |
| Scanning/discovering devices | https://infosys.beckhoff.com/english.php?content=../content/1033/tcautomationinterface/12425870731.html | 12425870731 |
| E-Bus SubTypes (device type IDs) | https://infosys.beckhoff.com/content/1033/tcautomationinterface/12425813643.html | 12425813643 |
| XML description format | https://infosys.beckhoff.com/content/1033/tcautomationinterface/12425785611.html | 12425785611 |
| ITcSysManager reference | https://infosys.beckhoff.com/content/1033/tcautomationinterface/12425792395.html | 12425792395 |
| ITcSmTreeItem reference | https://infosys.beckhoff.com/content/1033/tcautomationinterface/12425809675.html | 12425809675 |
| ITcSmTreeItem::CreateChild() | https://infosys.beckhoff.com/content/1033/tcautomationinterface/12425844363.html | 12425844363 |
| ProduceXml() | https://infosys.beckhoff.com/content/1033/tcautomationinterface/12425842315.html | 12425842315 |
| ConsumeXml() | https://infosys.beckhoff.com/content/1033/tcautomationinterface/12425843339.html | 12425843339 |

---

## 🗺️ Example → Official Documentation Mapping

### ActivateConfigurationRemoteTC2
- **Topic**: Configuration activation/lifecycle, remote system access
- **API**: `ITcSysManager::ActivateConfiguration()`, `AttachProject()`, `GetConfiguration()`, `StartEngine()`
- **Code**: `/C++/`, `/CSharp/`, `/Python/ActivateConfigurationRemoteTC2/`

### ActivatePreviousConfigurationTC2
- **Topic**: Configuration versioning/rollback
- **API**: `ITcSysManager::ActivateConfiguration()` (against prior config)
- **Code**: `/CSharp/`, `/Python/ActivatePreviousConfigurationTC2/`

### EtherCATLinkingTC2 — "Building an EtherCAT topology"
- **Primary doc**: https://infosys.beckhoff.com/english.php?content=../content/1033/tcautomationinterface/12425787531.html
  - Sub-sections: General info · Creating an EtherCAT device (Master=111/Slave=130) · Creating EtherCAT boxes (e.g. EK1100, SubType 9099, identified by Product Revision) · Creating terminals and inserting into topology (`bstrBefore` positioning) · Exceptions to ItemSubType 9099 (e.g. EP6002/EL600x = 9101, EL602x = 9103) · Changing "Previous Port" via XML (`<PreviousPort>`, `<PhysAddr>`) · Adding slaves to a HotConnect group (XML `<HotConnect>`) · Activating the configuration (`ITcSysManager::ActivateConfiguration()`)
- **Secondary docs**: E-Bus SubTypes (12425813643) · Device Scanning (12425870731) · XML Description (12425785611)
- **API**: `ITcSmTreeItem::CreateChild()`, `ProduceXml()`, `ConsumeXml()`
- **Code**: `/C++/`, `/CSharp/`, `/Python/EtherCATLinkingTC2/`
- **Note**: Product revision can use a wildcard (e.g. `"EK1100"` instead of `"EK1100-0000-0017"`) to auto-select the newest revision.

### LinkPLCProjectTC2
- **Topic**: PLC project linking/build
- **API**: `ITcPlcProject::Link()`, `Build()`, `ITcSysManager::GetProjectHandle()`
- **Code**: `/C++/`, `/CSharp/LinkPLCProjectTC2/`

### LinkVariablesTC2
- **Topic**: Variable linking, real-time read/write, handle lifecycle
- **API**: `ITcBindVariable::Link()`, `Read()`, `Write()`, `UnLink()`
- **Code**: `/C++/`, `/CSharp/`, `/Python/LinkVariablesTC2/`

### ScanBoxesTC2 — "Scan Devices"
- **Primary doc**: https://infosys.beckhoff.com/english.php?content=../content/1033/tcautomationinterface/12425870731.html
- **API**: `ITcSysManager::ScanDevices()`
- **Purpose**: Determine real/online addresses before importing them into an offline config (see EtherCAT activation flow above).
- **Code**: `/C++/ScanBoxesTC2/`

### AddingRoutesTC2
- **Topic**: AMS route creation/network topology
- **API**: `ITcRoute::Create()`
- **Code**: `/C++/AddingRoutesTC2/`

### SysManSamples
- **Topic**: General system management reference patterns
- **API**: Multiple (`ITcSysManager` and related)
- **Code**: `/C++/SysManSamples/`

---

## 🔁 Typical End-to-End Flow (Offline → Online EtherCAT Configuration)

1. Create offline EtherCAT Master/Slave/devices/terminals (`EtherCATLinkingTC2` pattern, `CreateChild()`).
2. Scan the real network for online devices (`ScanBoxesTC2` pattern, `ScanDevices()`).
3. Read the online device XML (`ProduceXml()`), extract real/physical addresses.
4. Import those addresses into the offline config (`ConsumeXml()`).
5. Activate the configuration (`ActivateConfigurationRemoteTC2` pattern, `ActivateConfiguration()`).

This flow is documented in the "Activate the EtherCAT configuration" sub-section of the EtherCAT topology page (12425787531).

---

## 🔎 How to Find More Documentation

1. **Browse**: Start at the main manual and use the left-hand table of contents.
2. **Search**: https://infosys.beckhoff.com/search/ — search "Automation Interface" + your topic.
3. **In-product help**: TwinCAT XAE → F1 for context-sensitive help on Automation Interface topics.
4. **ID pattern guessing**: Known page IDs cluster around `1242578xxxx`–`1242587xxxx`; try adjacent IDs in the same range if a linked page 404s.

---

## 📝 Notes

- Documentation version referenced: TwinCAT 2 Automation Interface manual, v1.2 (2023-01-25).
- Code snippets in the official docs are primarily C#; C++ and Python usage in this repo follows the same method/parameter semantics via COM.
- Always prefer the live Beckhoff manual as source of truth; URLs/IDs here are a navigation aid, not a mirror.
