# Graph Report - example  (2026-07-06)

## Corpus Check
- 96 files · ~87,525 words
- Verdict: corpus is large enough that graph structure adds value.

## Summary
- 741 nodes · 1028 edges · 55 communities detected
- Extraction: 69% EXTRACTED · 30% INFERRED · 0% AMBIGUOUS · INFERRED: 313 edges (avg confidence: 0.54)
- Token cost: 0 input · 0 output

## God Nodes (most connected - your core abstractions)
1. `GeneratePlcProject` - 37 edges
2. `Form1` - 36 edges
3. `EtherCATAutomationProtocol` - 35 edges
4. `PlcStressTest` - 30 edges
5. `CodeGenerationBaseScript` - 27 edges
6. `MainWindow` - 22 edges
7. `TComObjects` - 22 edges
8. `Script` - 22 edges
9. `ScriptRunner` - 14 edges
10. `TaskCpuSettings` - 11 edges

## Surprising Connections (you probably didn't know these)
- `ProcessOrder PowerShell Script` --semantically_similar_to--> `DataModel`  [INFERRED] [semantically similar]
  example/TC_AI_DOTNET_Samples/src/ScriptingContainer/CodeGenerationDemo/ProcessOrder.ps1 → example\TC_AI_DOTNET_Samples\src\ScriptingContainer\CodeGenerationDemo\DataModel.cs
- `RouteManagement` --semantically_similar_to--> `main()`  [INFERRED] [semantically similar]
  example\TC_AI_DOTNET_Samples\src\ScriptingContainer\Scripting.CSharp.Scripts\Scripts\RouteManagement.cs → example\TC_AI_DOTNET_Samples\src\ScriptingContainer\CodeGenerationDemo\ReadData.ps1
- `TcXmlConvert` --semantically_similar_to--> `ProduceXml / ConsumeXml Methods`  [INFERRED] [semantically similar]
  example\TC_AI_DOTNET_Samples\src\ScriptingContainer\ScriptingTestContainerBase\TcXmlConverter.cs → example/automationInterfaceOfficial/Documentation/API_AND_ONLINE_DOCS.md
- `EtherCATAutomationProtocol` --semantically_similar_to--> `EtherCATLinking`  [INFERRED] [semantically similar]
  example\TC_AI_DOTNET_Samples\src\ScriptingContainer\Scripting.CSharp.Scripts\Scripts\EtherCATAutomationProtocol.cs → example\TC_AI_DOTNET_Samples\src\ScriptingContainer\Scripting.CSharp.ScriptsLateBound\Scripts\EtherCATLinking.cs
- `GenerateCppProject` --semantically_similar_to--> `GenerateSafetyProject Script (Commented Out)`  [INFERRED] [semantically similar]
  example\TC_AI_DOTNET_Samples\src\ScriptingContainer\Scripting.CSharp.Scripts\Scripts\GenerateCppProject.cs → example/TC_AI_DOTNET_Samples/src/ScriptingContainer/Scripting.CSharp.Scripts/Scripts/GenerateSafetyProject.cs

## Hyperedges (group relationships)
- **TreeItem XML Consume/Produce Configuration Pattern** — addingroutestc2_program, ethercatlinkingtc2_program, scanboxestc2_program [INFERRED 0.75]
- **PLC Project Creation and Variable Linking Workflow** — linkvariablestc2_program, ethercatlinkingtc2_program, linkplcprojecttc2_program [INFERRED 0.80]
- **Dual C#/PowerShell Automation Interface Samples** — activateconfigurationremote_program, activateconfigurationremote_ps1, ethercatlinkingtc2_ps1 [INFERRED 0.60]
- **Soup01 Automation Interface Tutorial Incremental Build-up** — implementation1_createtwincatproject, implementation2_addplcproject, implementation3_importlibrary, implementation4_createprofinetcontroller, implementation5_adds210device [EXTRACTED 0.95]
- **ELT AMS Route Deployment Automation Group** — amsroutes_class, target_class, automationinterfacexml_class, eltprogram_main [EXTRACTED 0.90]
- **TcXaeShell Scripting Container Code Generation Framework** — codegenerationbasescript_class, configurationscripta_class, configurationscriptb_class, configurationscriptc_class, codegendemo_app [EXTRACTED 0.85]
- **CreatePlcMode Enum Duplicated Across Scripts** — generatecppproject_generatecppproject, generateplcproject_generateplcproject, plcarchives_plcarchives [INFERRED 0.85]
- **ScriptEarlyBound OnInitialize/OnSolutionCreated/OnCleanUp/OnExecute Lifecycle Pattern** — accessrunningvs_accessrunningvs, ethercatlinking_ethercatlinking, generateplcproject_generateplcproject, tcomobjects_tcomobjects [INFERRED 0.80]
- **Orders.xml-Driven TwinCAT Project Generation (C# GUI + PowerShell)** — datamodel_datamodel, mainwindow_mainwindow, processorder_processorder [INFERRED 0.80]
- **Script Template Method Lifecycle (Initialize/Execute/CleanUp)** — script_class, etherCATlinking_class, generateplcproject_class, novramdevice_class, taskcpusettings_class [INFERRED 0.85]
- **Early vs Late COM Binding Duplication Pattern** — common_latebound_helper, scriptinghelper_helper, earlybinding_earlyboundfactory, latebinding_lateboundfactory [INFERRED 0.80]
- **COM DTE Launch and Management Infrastructure** — dtelauncher_class, runningobjectstable_rotaccess, messagefilter_class, configurationgenerator_ivsfactory [INFERRED 0.75]
- **Cmdlet-to-Worker Script Execution Pipeline** — scriptrunner_scriptrunnercmdlet, scriptrunner_scriptrunner, workerthread_scriptbackgroundworker, workerthread_scriptcontext [EXTRACTED 0.85]
- **TwinCAT Automation Interface Core API Surface** — api_itcsysmanager, api_itcsmtreeitem, api_itcplcproject, api_itcbindvariable, api_itcroute [INFERRED 0.80]
- **Automation Interface Consolidated Documentation Set for LLM Context** — doc_documentation_readme, doc_projects_reference, doc_language_guides, doc_api_and_online_docs [EXTRACTED 0.90]

## Communities

### Community 0 - "Scripting Demo Scripts (Access/Drive/Archive/Route)"
Cohesion: 0.05
Nodes (12): AccessRunningVS, Scripting.CSharp, Ax5000Drive, Scripting.CSharp, ManagePlcLibraries, Scripting.CSharp, PlcArchives, Scripting.CSharp (+4 more)

### Community 1 - "PLC/C++ Project Generation Scripts"
Cohesion: 0.1
Nodes (6): GenerateCppProject, Scripting.CSharp, GeneratePlcProject, Scripting.CSharp, ScriptingTest.LateBinding, GenerateSafetyProject Script (Commented Out)

### Community 2 - "SysManSamples WinForms UI"
Cohesion: 0.09
Nodes (2): Form1, SysManTest

### Community 3 - "CodeGenerationDemo Data Model"
Cohesion: 0.08
Nodes (27): CodeGenerationDemo, DataModel, INotifyPropertyChanged, List, MainWindow Class, ProcessOrder PowerShell Script, ScriptContext, AxisInfo (+19 more)

### Community 4 - "EtherCAT Automation Protocol"
Cohesion: 0.12
Nodes (2): EtherCATAutomationProtocol, Scripting.CSharp

### Community 5 - "Script Base Class"
Cohesion: 0.08
Nodes (5): Script, ScriptEarlyBound, ScriptingTest, ScriptLateBound, TcEnvironment

### Community 6 - "Scripting App & Configuration Factory"
Cohesion: 0.07
Nodes (25): App (WPF Application), Helper (Late-Bound EtherCAT Device Scan/Create), PlcAccessConverter, ConfigurationFactory, IConfigurationFactory, ScriptingTest, EarlyBoundFactory, ScriptingTest (+17 more)

### Community 7 - "EtherCAT Linking & Task CPU Settings"
Cohesion: 0.06
Nodes (11): EtherCATLinking2, Scripting.CSharp, EtherCATLinking, Scripting.CSharp, ScriptingTest.LateBinding, NovRamDevice, ScriptingTest.LateBinding, ScriptLateBound (+3 more)

### Community 8 - "AutomationInterfaceTutorial Implementation Steps"
Cohesion: 0.07
Nodes (17): CodeGenerationDemo App (WPF), CodeGenerationBaseScript Class, ConfigurationScriptA Class, ConfigurationScriptB Class, ConfigurationScriptC Class, EtherCATLinkingTC2 Python Script, ConsoleApp2, Program (+9 more)

### Community 9 - "PLC Stress Test"
Cohesion: 0.15
Nodes (2): PlcStressTest, Scripting.CSharp

### Community 10 - "Code Generation Script (POU/DUT/GVL creation)"
Cohesion: 0.12
Nodes (2): CodeGenerationBaseScript, CodeGenerationDemo

### Community 11 - "Automation Interface API & Docs"
Cohesion: 0.12
Nodes (24): ActivateConfigurationRemoteTC2 Example, ActivatePreviousConfigurationTC2 Example, AddingRoutesTC2 Example, ITcBindVariable Interface, ITcPlcProject Interface, ITcRoute Interface, ITcSmTreeItem Interface, ITcSysManager Interface (+16 more)

### Community 12 - "ScriptRunner PowerShell Module"
Cohesion: 0.11
Nodes (14): CodeGenerationDemo App, CodeGenerationDemo Data ReadMe (Orders.xml), PSCmdlet, ScriptingTestContainer Demo App, GetScriptCmdlet, ScriptRunner PowerShell Module ReadMe (Start-TcScripts), ScriptExecuteContext, ScriptingTest.ScriptRunner (+6 more)

### Community 13 - "Scripting Container Main Window UI"
Cohesion: 0.13
Nodes (4): CodeGenerationDemo, MainWindow, ScriptingTest, Window

### Community 14 - "Configuration Generator"
Cohesion: 0.11
Nodes (9): ConfigurationFactory, DTEInfo, IVsFactory, ScriptingTest, VsFactory, VSInfo, DTELauncher (COM DTE Launch/ROT Wait), IConfigurationFactory (+1 more)

### Community 15 - "TCom Objects (Symbols/DataAreas)"
Cohesion: 0.13
Nodes (4): DataAreaInfo, Scripting.CSharp, SymbolInfo, TComObjects

### Community 16 - "Worker Thread & Progress Reporting"
Cohesion: 0.12
Nodes (7): IProgressProvider, IContext, IContextProvider, IWorker, ScriptBackgroundWorker, ScriptContext, ScriptingTest

### Community 17 - "Configuration Scripts A/B/D"
Cohesion: 0.12
Nodes (9): CodeGenerationBaseScript, CodeGenerationDemo, ConfigurationScriptA, CodeGenerationDemo, ConfigurationScriptB, CodeGenerationDemo, ConfigurationScriptC, CodeGenerationDemo (+1 more)

### Community 18 - "AmsNetId Helper"
Cohesion: 0.12
Nodes (11): AmsNetId, DefaultMacIds, Helper, IProgressProvider, PlcAccessConverter, ProgressStatusChangedArgs, ScriptingTest, ScriptingTest.LateBinding (+3 more)

### Community 19 - "TreeItemType Enums"
Cohesion: 0.12
Nodes (7): BoxTypeExtension, DeviceTypeExtension, EtherCATLinkingTC2, IECLanguageTypeExtension, POULanguageTypeExtension, TreeItemTypeExtension, TwinCAT.SystemManager

### Community 20 - "COM Message Filter"
Cohesion: 0.16
Nodes (4): IOleMessageFilter, IOleMessageFilter, MessageFilter, ScriptingTest

### Community 21 - "TC2 Sample Program Entrypoints"
Cohesion: 0.19
Nodes (8): ActivateConfigurationRemote, AddingRoutesTC2, EltAutomationInterface, EtherCATLinkingTC2, LinkPLCProjectTC2, LinkVariablesTC2, Program, ScanBoxesTC2

### Community 22 - "ProcessOrder PowerShell Script"
Cohesion: 0.28
Nodes (12): Count-Boxes(), Create-Box(), Create-Chart(), Create-Hardware(), Create-Mappings(), Create-Motion(), Create-PlcProject(), Create-Scope() (+4 more)

### Community 23 - "XML Converter (ProduceXml/ConsumeXml)"
Cohesion: 0.17
Nodes (3): ProduceXml / ConsumeXml Methods, ScriptingTest, TcXmlConvert

### Community 24 - "TreeBrowser UI Control"
Cohesion: 0.27
Nodes (2): SysManTest, TreeBrowser

### Community 25 - "DTE Launcher"
Cohesion: 0.27
Nodes (3): DTELauncher, NativeMethods, ScriptingTestContainerBase

### Community 26 - "Running Object Table Access"
Cohesion: 0.29
Nodes (3): ROTAccess, ROTDteInfo, ScriptingTest

### Community 27 - "Script Loader"
Cohesion: 0.43
Nodes (2): ScriptingTest, ScriptLoader

### Community 28 - "TC2 Example Programs (misc)"
Cohesion: 0.38
Nodes (6): AddingRoutesTC2 Program.cs, EtherCATLinkingTC2 ItemTypes.cs (TreeItemType enum), EtherCATLinkingTC2 Program.cs, LinkPLCProjectTC2 Program.cs, LinkVariablesTC2 Program.cs, ScanBoxesTC2 Program.cs

### Community 29 - "ELT & Python Config Scripts"
Cohesion: 0.53
Nodes (6): ActivateConfigurationRemoteTC2 Python Script, ActivatePreviousConfigurationTC2 Python Script, AMSRoutes Class, AutomationInterfaceXml Class, ELT Program (Deployment Main), Target Class

### Community 30 - "WPF App Entrypoints"
Cohesion: 0.4
Nodes (4): App, CodeGenerationDemo, ScriptingTest, Application

### Community 31 - "TreeBrowser Item Node"
Cohesion: 0.5
Nodes (3): SysManTest, SystemManagerBrowserItem, TreeNode

### Community 32 - "AutomationInterfaceXml (Routes XML Builder)"
Cohesion: 0.5
Nodes (2): AutomationInterfaceXml, EltAutomationInterface

### Community 33 - "AMSRoutes"
Cohesion: 0.67
Nodes (2): AMSRoutes, EltAutomationInterface

### Community 34 - "Target (ELT)"
Cohesion: 0.67
Nodes (2): EltAutomationInterface, Target

### Community 35 - "SysManSamples Misc"
Cohesion: 0.67
Nodes (3): SysManSamples Form1.cs, SysManSamples TreeBrowser.cs, SysManSamples TreeBrowserItem.cs (SystemManagerBrowserItem)

### Community 36 - "ActivateConfigurationRemote Scripts"
Cohesion: 0.67
Nodes (1): ActivateConfigurationRemote Program.cs

### Community 37 - "ActivateConfigurationRemote PS1"
Cohesion: 1.0
Nodes (0): 

### Community 38 - "ActivatePreviousConfigurationTC2 Scripts"
Cohesion: 1.0
Nodes (0): 

### Community 39 - "LinkPLCProjectTC2 PS1"
Cohesion: 1.0
Nodes (0): 

### Community 40 - "LinkVariablesTC2 Scripts"
Cohesion: 1.0
Nodes (0): 

### Community 41 - "LinkVariablesTC2 Cross-Language"
Cohesion: 1.0
Nodes (2): LinkVariablesTC2 PowerShell Script (CSharp folder), LinkVariablesTC2 Python Script

### Community 42 - "ScriptRunner AssemblyInfo"
Cohesion: 1.0
Nodes (0): 

### Community 43 - "EtherCATLinkingTC2 Python"
Cohesion: 1.0
Nodes (0): 

### Community 44 - "ActivateConfigurationRemoteTC2 Python"
Cohesion: 1.0
Nodes (0): 

### Community 45 - "Safety Project Generation"
Cohesion: 1.0
Nodes (0): 

### Community 46 - "ActivateConfigurationRemote AssemblyInfo"
Cohesion: 1.0
Nodes (1): ActivateConfigurationRemote AssemblyInfo.cs

### Community 47 - "AddingRoutesTC2 AssemblyInfo"
Cohesion: 1.0
Nodes (1): AddingRoutesTC2 AssemblyInfo.cs

### Community 48 - "EtherCATLinkingTC2 AssemblyInfo"
Cohesion: 1.0
Nodes (1): EtherCATLinkingTC2 AssemblyInfo.cs

### Community 49 - "LinkPLCProjectTC2 AssemblyInfo"
Cohesion: 1.0
Nodes (1): LinkPLCProjectTC2 AssemblyInfo.cs

### Community 50 - "LinkVariablesTC2 AssemblyInfo"
Cohesion: 1.0
Nodes (1): LinkVariablesTC2 AssemblyInfo.cs

### Community 51 - "ScanBoxesTC2 AssemblyInfo"
Cohesion: 1.0
Nodes (1): ScanBoxesTC2 AssemblyInfo.cs

### Community 52 - "SysManSamples AssemblyInfo"
Cohesion: 1.0
Nodes (1): SysManSamples AssemblyInfo.cs

### Community 53 - "IProgressProvider Interface"
Cohesion: 1.0
Nodes (1): IProgressProvider Interface

### Community 54 - "AmsNetId Type"
Cohesion: 1.0
Nodes (1): AmsNetId

## Ambiguous Edges - Review These
- `VarDeclaration` → `PlcAccessConverter`  [AMBIGUOUS]
  example/TC_AI_DOTNET_Samples/src/ScriptingContainer/Scripting.CSharp.ScriptsLateBound/Common.cs · relation: conceptually_related_to
- `EtherCATLinkingTC2 ItemTypes.cs (TreeItemType enum)` → `ScanBoxesTC2 Program.cs`  [AMBIGUOUS]
  example/automationInterfaceOfficial/C++/ScanBoxesTC2/ScanBoxesTC2/Program.cs · relation: conceptually_related_to
- `EtherCATLinkingTC2 Program.cs` → `EtherCATLinkingTC2.ps1`  [AMBIGUOUS]
  example/automationInterfaceOfficial/CSharp/EtherCATLinkingTC2/EtherCATLinkingTC2.ps1 · relation: conceptually_related_to
- `ITcSysManager Interface` → `ELT-AutomationInterface-Sagatowski README`  [AMBIGUOUS]
  example/ELT-AutomationInterface-Sagatowski/README.md · relation: references

## Knowledge Gaps
- **117 isolated node(s):** `ActivateConfigurationRemote`, `AddingRoutesTC2`, `EtherCATLinkingTC2`, `EtherCATLinkingTC2`, `LinkPLCProjectTC2` (+112 more)
  These have ≤1 connection - possible missing edges or undocumented components.
- **Thin community `ActivateConfigurationRemote PS1`** (2 nodes): `ActivateConfigurationRemote.ps1`, `Get-ScriptDirectory()`
  Too small to be a meaningful cluster - may be noise or needs more connections extracted.
- **Thin community `ActivatePreviousConfigurationTC2 Scripts`** (2 nodes): `ActivatePreviousConfigurationTC2.py`, `Get-ScriptDirectory()`
  Too small to be a meaningful cluster - may be noise or needs more connections extracted.
- **Thin community `LinkPLCProjectTC2 PS1`** (2 nodes): `LinkPLCProjectTC2.ps1`, `Get-ScriptDirectory()`
  Too small to be a meaningful cluster - may be noise or needs more connections extracted.
- **Thin community `LinkVariablesTC2 Scripts`** (2 nodes): `LinkVariablesTC2.py`, `Get-ScriptDirectory()`
  Too small to be a meaningful cluster - may be noise or needs more connections extracted.
- **Thin community `LinkVariablesTC2 Cross-Language`** (2 nodes): `LinkVariablesTC2 PowerShell Script (CSharp folder)`, `LinkVariablesTC2 Python Script`
  Too small to be a meaningful cluster - may be noise or needs more connections extracted.
- **Thin community `ScriptRunner AssemblyInfo`** (1 nodes): `AssemblyInfo.cs`
  Too small to be a meaningful cluster - may be noise or needs more connections extracted.
- **Thin community `EtherCATLinkingTC2 Python`** (1 nodes): `EtherCATLinkingTC2.py`
  Too small to be a meaningful cluster - may be noise or needs more connections extracted.
- **Thin community `ActivateConfigurationRemoteTC2 Python`** (1 nodes): `ActivateConfigurationRemoteTC2.py`
  Too small to be a meaningful cluster - may be noise or needs more connections extracted.
- **Thin community `Safety Project Generation`** (1 nodes): `GenerateSafetyProject.cs`
  Too small to be a meaningful cluster - may be noise or needs more connections extracted.
- **Thin community `ActivateConfigurationRemote AssemblyInfo`** (1 nodes): `ActivateConfigurationRemote AssemblyInfo.cs`
  Too small to be a meaningful cluster - may be noise or needs more connections extracted.
- **Thin community `AddingRoutesTC2 AssemblyInfo`** (1 nodes): `AddingRoutesTC2 AssemblyInfo.cs`
  Too small to be a meaningful cluster - may be noise or needs more connections extracted.
- **Thin community `EtherCATLinkingTC2 AssemblyInfo`** (1 nodes): `EtherCATLinkingTC2 AssemblyInfo.cs`
  Too small to be a meaningful cluster - may be noise or needs more connections extracted.
- **Thin community `LinkPLCProjectTC2 AssemblyInfo`** (1 nodes): `LinkPLCProjectTC2 AssemblyInfo.cs`
  Too small to be a meaningful cluster - may be noise or needs more connections extracted.
- **Thin community `LinkVariablesTC2 AssemblyInfo`** (1 nodes): `LinkVariablesTC2 AssemblyInfo.cs`
  Too small to be a meaningful cluster - may be noise or needs more connections extracted.
- **Thin community `ScanBoxesTC2 AssemblyInfo`** (1 nodes): `ScanBoxesTC2 AssemblyInfo.cs`
  Too small to be a meaningful cluster - may be noise or needs more connections extracted.
- **Thin community `SysManSamples AssemblyInfo`** (1 nodes): `SysManSamples AssemblyInfo.cs`
  Too small to be a meaningful cluster - may be noise or needs more connections extracted.
- **Thin community `IProgressProvider Interface`** (1 nodes): `IProgressProvider Interface`
  Too small to be a meaningful cluster - may be noise or needs more connections extracted.
- **Thin community `AmsNetId Type`** (1 nodes): `AmsNetId`
  Too small to be a meaningful cluster - may be noise or needs more connections extracted.

## Suggested Questions
_Questions this graph is uniquely positioned to answer:_

- **What is the exact relationship between `VarDeclaration` and `PlcAccessConverter`?**
  _Edge tagged AMBIGUOUS (relation: conceptually_related_to) - confidence is low._
- **What is the exact relationship between `EtherCATLinkingTC2 ItemTypes.cs (TreeItemType enum)` and `ScanBoxesTC2 Program.cs`?**
  _Edge tagged AMBIGUOUS (relation: conceptually_related_to) - confidence is low._
- **What is the exact relationship between `EtherCATLinkingTC2 Program.cs` and `EtherCATLinkingTC2.ps1`?**
  _Edge tagged AMBIGUOUS (relation: conceptually_related_to) - confidence is low._
- **What is the exact relationship between `ITcSysManager Interface` and `ELT-AutomationInterface-Sagatowski README`?**
  _Edge tagged AMBIGUOUS (relation: references) - confidence is low._
- **Why does `GeneratePlcProject` connect `PLC/C++ Project Generation Scripts` to `Scripting Demo Scripts (Access/Drive/Archive/Route)`, `PLC Stress Test`, `EtherCAT Linking & Task CPU Settings`?**
  _High betweenness centrality (0.029) - this node is a cross-community bridge._
- **Why does `EtherCATAutomationProtocol` connect `EtherCAT Automation Protocol` to `Scripting Demo Scripts (Access/Drive/Archive/Route)`, `EtherCAT Linking & Task CPU Settings`?**
  _High betweenness centrality (0.026) - this node is a cross-community bridge._
- **Why does `PlcStressTest` connect `PLC Stress Test` to `Scripting Demo Scripts (Access/Drive/Archive/Route)`, `PLC/C++ Project Generation Scripts`?**
  _High betweenness centrality (0.023) - this node is a cross-community bridge._