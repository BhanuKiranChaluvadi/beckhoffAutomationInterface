# TwinCAT Automation Interface — Repository Reference

This repository contains TwinCAT 2 Automation Interface code examples in three languages (C++, C#, Python), plus consolidated documentation for use by developers and AI coding assistants (GitHub Copilot, etc.) working on Automation Interface tasks.

> **For Copilot/LLM context**: This file + the 3 files below are the complete knowledge base. Read [PROJECTS_REFERENCE.md](./PROJECTS_REFERENCE.md) to find/compare examples, [LANGUAGE_GUIDES.md](./LANGUAGE_GUIDES.md) for language-specific setup and code patterns, and [API_AND_ONLINE_DOCS.md](./API_AND_ONLINE_DOCS.md) for official Beckhoff API/URL references.

---

## 📁 Repository Structure

```
automation interface/
├── C++/                    # Native C++ examples (7 projects) — Visual Studio .sln
│   ├── ActivateConfigurationRemoteTC2/
│   ├── AddingRoutesTC2/
│   ├── EtherCATLinkingTC2/
│   ├── LinkPLCProjectTC2/
│   ├── LinkVariablesTC2/
│   ├── ScanBoxesTC2/
│   └── SysManSamples/
│
├── CSharp/                 # C# / PowerShell examples (5 projects)
│   ├── ActivateConfigurationRemoteTC2/
│   ├── ActivatePreviousConfigurationTC2/
│   ├── EtherCATLinkingTC2/
│   ├── LinkPLCProjectTC2/
│   └── LinkVariablesTC2/
│
├── Python/                 # Python examples (4 projects)
│   ├── ActivateConfigurationRemoteTC2/
│   ├── ActivatePreviousConfigurationTC2/
│   ├── EtherCATLinkingTC2/
│   └── LinkVariablesTC2/
│
└── Documentation/           # This documentation set (4 files)
    ├── README.md                 ← you are here (overview + navigation)
    ├── PROJECTS_REFERENCE.md     ← project catalog + use-case lookup + feature matrix
    ├── LANGUAGE_GUIDES.md        ← setup, build, code patterns per language
    └── API_AND_ONLINE_DOCS.md    ← official Beckhoff API/URL mapping
```

**Stats**: 16 total projects · 7 C++ · 5 C# · 4 Python · 3 examples available in all three languages (`ActivateConfigurationRemoteTC2`, `EtherCATLinkingTC2`, `LinkVariablesTC2`).

---

## 🚀 Quick Start

1. Pick a language: C++ (performance/native), C# (.NET integration), or Python (scripting/automation).
2. Find the relevant example in [PROJECTS_REFERENCE.md](./PROJECTS_REFERENCE.md).
3. Follow setup/build steps in [LANGUAGE_GUIDES.md](./LANGUAGE_GUIDES.md).
4. Cross-check behavior against official Beckhoff docs via [API_AND_ONLINE_DOCS.md](./API_AND_ONLINE_DOCS.md).

---

## 🗂️ Categories at a Glance

| Category | Examples | Languages |
|---|---|---|
| Configuration Management | ActivateConfigurationRemoteTC2, ActivatePreviousConfigurationTC2 | C++/C#/Python, C#/Python |
| EtherCAT Management | EtherCATLinkingTC2 | C++/C#/Python |
| PLC Project Management | LinkPLCProjectTC2 | C++/C# |
| Real-time Data Access | LinkVariablesTC2 | C++/C#/Python |
| Network Configuration | AddingRoutesTC2 | C++ |
| Device Discovery | ScanBoxesTC2 | C++ |
| System Management | SysManSamples | C++ |

Full details, use-case lookup, and feature comparison tables: [PROJECTS_REFERENCE.md](./PROJECTS_REFERENCE.md)

---

## 🔗 Official Documentation

Main manual: https://infosys.beckhoff.com/english.php?content=../content/1033/tcautomationinterface/index.html
(Sections: Forward, Overview, System Requirements, Installation, Configuration, API, Samples, How-To)

Full URL index and example-to-doc mapping: [API_AND_ONLINE_DOCS.md](./API_AND_ONLINE_DOCS.md)

---

## 📦 Original Source Archives

All examples were extracted from Beckhoff-provided zip files (naming convention: no suffix = C++, `_PS` = C#/PowerShell, `_PY` = Python). Archives have been deleted after extraction; only the unpacked project folders remain.
