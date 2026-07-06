# Language-Specific Guides

Setup, usage, and technical details for each programming language.

## C++ Guide

### Overview
C++ implementations provide **maximum performance and direct hardware access** for TwinCAT automation. These examples use the Windows COM interface directly.

### Environment Setup

**Requirements**:
- Visual Studio 2015 or later
- TwinCAT 2 SDK (downloaded from Beckhoff)
- Windows SDK
- C++ compiler (included with Visual Studio)

**Installation**:
1. Install Visual Studio (Community edition works)
2. Install TwinCAT Runtime
3. Download TwinCAT 2 SDK from Beckhoff website
4. Add TwinCAT SDK to Visual Studio include paths

### Project Structure
```
C++/ProjectName/
├── ProjectName.sln          # Visual Studio solution
├── ProjectName.vcxproj      # Visual Studio project
├── Source/
│   ├── main.cpp
│   ├── AutomationInterface.cpp
│   └── AutomationInterface.h
└── Include/
    └── TwinCAT headers
```

### Building
```bash
# Using Visual Studio
# 1. Open .sln file in Visual Studio
# 2. Build > Build Solution (Ctrl+Shift+B)
# 3. Run (F5)

# Or command line
msbuild ProjectName.sln /p:Configuration=Release /p:Platform=x64
```

### Key Concepts

**COM Interface**:
- COM (Component Object Model) is the primary interface
- Use `#import "tcautomationinterface.dll"` or similar
- Classes available through COM type libraries

**Error Handling**:
```cpp
HRESULT hr = pInterface->Method();
if (FAILED(hr)) {
    // Handle error
    ATLTRACE2(atlTraceError, 0, "Method failed: %x", hr);
}
```

**Resource Management**:
- Use COM smart pointers (CComPtr, _COM_SMARTPTR_TYPEDEF)
- Automatic cleanup with RAII pattern

### Common Patterns

**Initialization**:
```cpp
CoInitializeEx(NULL, COINIT_MULTITHREADED);
// ... use COM objects ...
CoUninitialize();
```

**Error Propagation**:
- Return HRESULT values
- Use FAILED() macro to check results
- Proper cleanup in error paths

### Performance Characteristics
- **Latency**: Very low (sub-millisecond)
- **Memory**: Minimal overhead
- **Throughput**: Highest among three languages
- **Best for**: Real-time critical operations

### Debugging
- Visual Studio debugger fully supported
- Breakpoints, watch windows, call stacks
- Enable debug output with `ATLTRACE`

### Examples in This Repository

| Example | Purpose | Key Files |
|---------|---------|-----------|
| ActivateConfigurationRemoteTC2 | Remote config activation | `main.cpp` |
| AddingRoutesTC2 | Network route management | `*.cpp` |
| EtherCATLinkingTC2 | EtherCAT configuration | `*.cpp` |
| LinkPLCProjectTC2 | PLC project linking | `*.cpp` |
| LinkVariablesTC2 | Real-time variable access | `*.cpp` |
| ScanBoxesTC2 | System discovery | `*.cpp` |
| SysManSamples | Management examples | `*.cpp` |

---

## C# / PowerShell Guide

### Overview
C# and PowerShell implementations provide **ease of development and strong .NET ecosystem integration**. These examples use managed wrappers around COM interfaces.

### Environment Setup

**Requirements**:
- .NET Framework 4.5 or later (usually pre-installed on Windows)
- Visual Studio 2017 or later (optional, but recommended)
- PowerShell 5.0 or Windows PowerShell ISE
- TwinCAT 2 Runtime

**PowerShell Setup**:
1. Open PowerShell as Administrator
2. No additional installation needed (COM access built-in)

**C# Project Setup**:
1. Create Visual Studio project
2. Add reference to TwinCAT COM libraries
3. Use `dynamic` or direct COM interface declarations

### Project Structure
```
CSharp/ProjectName/
├── ProjectName.csproj       # Visual Studio project file
├── ProjectName.sln          # Solution file (optional)
├── Program.cs               # Main entry point
├── AutomationInterface.cs    # COM wrapper
└── packages.config          # NuGet dependencies
```

### PowerShell Usage

**Running Scripts**:
```powershell
# Set execution policy if needed
Set-ExecutionPolicy -ExecutionPolicy RemoteSigned -Scope CurrentUser

# Run script
.\ActivateConfiguration.ps1 -ComputerName "TCBOX" -ConfigName "Config1"

# With parameters
.\script.ps1 -Parameter Value
```

**COM Interop in PowerShell**:
```powershell
$tcInterface = New-Object -ComObject "TwinCAT.Automation"
$result = $tcInterface.Method()
```

### C# Usage

**Building**:
```bash
# Using Visual Studio
# Open .csproj in Visual Studio and build

# Or command line
dotnet build ProjectName.csproj
# or
msbuild ProjectName.csproj
```

**Compilation**:
```bash
csc.exe /target:exe /out:program.exe Program.cs AutomationInterface.cs
```

### Key Concepts

**COM Interop**:
```csharp
// Using dynamic (easier)
dynamic tcInterface = new Object();
object result = tcInterface.Method();

// Or strong typing
ITwinCATAutomation intf = (ITwinCATAutomation)new TwinCATClass();
```

**Error Handling**:
```csharp
try {
    intf.Method();
} catch (COMException ex) {
    Console.WriteLine("COM Error: {0:X}", ex.ErrorCode);
}
```

**Async Operations**:
```csharp
await Task.Run(() => {
    // Long-running operation
});
```

### Common Patterns

**Initialization**:
```csharp
// C#
var tc = new TwinCATAutomation();

// PowerShell
$tc = New-Object -ComObject "TwinCAT.Automation"
```

**Error Handling**:
```csharp
int hResult = Marshal.GetHRForException(exception);
if (FAILED(hResult)) {
    // Handle error
}
```

### Performance Characteristics
- **Latency**: Low (1-5 milliseconds typical)
- **Memory**: Moderate (.NET runtime overhead)
- **Throughput**: Good (adequate for most tasks)
- **Best for**: Integration with .NET applications, scripting

### Debugging

**Visual Studio**:
- Full debugging support
- Watch windows, breakpoints
- IntelliSense for COM objects (with type info)

**PowerShell ISE**:
- Breakpoints and step-through debugging
- Output window for tracing

### Examples in This Repository

| Example | Purpose | Type |
|---------|---------|------|
| ActivateConfigurationRemoteTC2 | Remote config activation | PowerShell/C# |
| ActivatePreviousConfigurationTC2 | Configuration rollback | PowerShell |
| EtherCATLinkingTC2 | EtherCAT configuration | C# |
| LinkPLCProjectTC2 | PLC project linking | C# |
| LinkVariablesTC2 | Real-time variable access | C# |

---

## Python Guide

### Overview
Python implementations provide **rapid development and excellent scripting capabilities**. These examples use ctypes or COM wrappers for automation interface access.

### Environment Setup

**Requirements**:
- Python 3.6 or later (3.8+ recommended)
- `pywin32` package (for COM support)
- TwinCAT Runtime installed on the system

**Installation**:
```bash
# Install Python 3.x from python.org

# Install required packages
pip install pywin32

# Post-install (required for COM)
python -m pip install --upgrade pywin32
python -m pip install pywin32-ctypes

# Register COM interface (Windows)
python -m pywin32_postinstall -install
```

### Project Structure
```
Python/ProjectName/
├── main.py                  # Main script
├── automation_interface.py   # COM wrapper module
├── requirements.txt         # Python dependencies
└── README.md               # Documentation
```

### Running Python Scripts

**Direct Execution**:
```bash
python script.py
python script.py --parameter value
```

**With Arguments**:
```bash
python LinkVariablesTC2.py --tcbox "192.168.1.100" --interval 100
```

**As Module**:
```python
import sys
sys.path.insert(0, 'C:/path/to/project')
from automation_interface import TwinCATInterface
```

### Key Concepts

**COM Access via pywin32**:
```python
import win32com.client

tc = win32com.client.Dispatch("TwinCAT.Automation")
result = tc.Method()
```

**Error Handling**:
```python
from pywintypes import com_error

try:
    tc.Method()
except com_error as e:
    print(f"COM Error: {e}")
```

**Async Operations**:
```python
import asyncio

async def async_task():
    # Long-running operation
    pass

asyncio.run(async_task())
```

### Common Patterns

**Configuration**:
```python
# Using environment variables
import os
TC_HOST = os.environ.get('TC_HOST', 'localhost')
TC_PORT = os.environ.get('TC_PORT', 5000)

# Or config file
import json
with open('config.json') as f:
    config = json.load(f)
```

**Logging**:
```python
import logging

logging.basicConfig(level=logging.INFO)
logger = logging.getLogger(__name__)
logger.info("Starting automation task")
```

**Error Recovery**:
```python
import time

max_retries = 3
for attempt in range(max_retries):
    try:
        result = tc.Method()
        break
    except Exception as e:
        if attempt < max_retries - 1:
            time.sleep(2 ** attempt)  # Exponential backoff
        else:
            raise
```

### Performance Characteristics
- **Latency**: Moderate (10-50 milliseconds typical)
- **Memory**: Lower than .NET but higher than C++
- **Throughput**: Adequate for scripting tasks
- **Best for**: Automation scripts, data analysis, integration workflows

### Debugging

**Print Statements**:
```python
print(f"Debug: {variable}")
```

**Python Debugger (pdb)**:
```python
import pdb
pdb.set_trace()  # Breakpoint
```

**IDE Debugging** (VS Code, PyCharm):
- Full debugging support
- Breakpoints, watch windows
- Interactive console

### Virtual Environment (Recommended)

```bash
# Create virtual environment
python -m venv venv

# Activate
# Windows
venv\Scripts\activate
# Linux/macOS
source venv/bin/activate

# Install dependencies
pip install -r requirements.txt

# Deactivate
deactivate
```

### Examples in This Repository

| Example | Purpose | Key Features |
|---------|---------|--------------|
| ActivateConfigurationRemoteTC2 | Remote config activation | COM interface usage |
| ActivatePreviousConfigurationTC2 | Configuration rollback | Error handling |
| EtherCATLinkingTC2 | EtherCAT configuration | Device enumeration |
| LinkVariablesTC2 | Real-time variable access | Data streaming |

---

## Comparison Table

| Aspect | C++ | C# | Python |
|--------|:---:|:--:|:------:|
| **Setup Time** | ⏱️⏱️⏱️ | ⏱️⏱️ | ⏱️ |
| **Performance** | ⚡⚡⚡⚡⚡ | ⚡⚡⚡⚡ | ⚡⚡⚡ |
| **Development Speed** | 🐌 | 🚗 | 🚀 |
| **Learning Curve** | Steep | Moderate | Easy |
| **Real-time Capable** | ✓ | ✓ | ~ |
| **Production Ready** | ✓ | ✓ | ✓ |
| **Community Support** | Active | Very Active | Very Active |

---

## Choosing Your Language

### Use C++ if:
- ✓ Real-time performance is critical
- ✓ Memory efficiency is important
- ✓ Integrating with legacy C++ code
- ✓ Building high-throughput systems

### Use C# if:
- ✓ Target is Windows/.NET ecosystem
- ✓ Rapid development is important
- ✓ Integrating with .NET applications
- ✓ Using Visual Studio as IDE

### Use Python if:
- ✓ Rapid prototyping needed
- ✓ Scripting and automation primary
- ✓ Data analysis integration required
- ✓ Cross-platform capability needed

---

## Additional Resources

- [TwinCAT Automation Interface Manual](https://infosys.beckhoff.com/english.php?content=../content/1033/tcautomationinterface/index.html)
- Official Examples Documentation
- Microsoft COM Documentation
- Python pywin32 Documentation
