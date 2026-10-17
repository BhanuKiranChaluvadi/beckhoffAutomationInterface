# Three methods to put PLC objects into a TwinCAT project

Status: 2026-10-15. Measured with TwinCAT 3.1 Build 4026 on the NFLite machine UVM
(BeckhoffLib: 405 `.st` files, 1594 PLC objects, of which 1146 are methods and 43 are properties).

| Method | What it is | Status |
|---|---|---|
| A | One COM call sequence for each object (the tool does this now) | In use |
| A2 | Method A on a kept project: write only the changed objects | In use (`build_nflite_machine.ps1 -Incremental`) |
| B | One PLCopen XML import for all objects | Tested. Not in use. |
| C | Write the TwinCAT project files directly | Proposed. Paused. |

## 1. The problem

A fresh build of one machine takes about 10 minutes. About 7.3 minutes of this time is the
code sync: the tool makes about 1600 PLC objects, one after the other, over COM.

| Run | Time |
|---|---|
| One machine, fresh build | about 10 min |
| Four machines, fresh, `-Parallel` (build phase only) | about 15 min |
| Four machines, fresh, `-Parallel -Run` (build + activate + verify) | about 21 min |

The time for the other steps (libraries, I/O tree, links, compile) is about 2–3 minutes. These
steps are not the subject of this page.

## 2. Method A: one COM call sequence for each object (in use)

### How it works

`PouSyncEngine` does these steps for each object:

1. Find the object (`LookupTreeItem`). If it is not there, make it (`CreateChild`).
2. Read the declaration text and the implementation text.
3. Write a text only when it is different from the source.

For a new object, this is about 7 COM calls. For an unchanged object, it is about 4 calls.

### Measured cost

| COM operation | Time |
|---|---|
| Make a new object (all calls) | about 0.27 s |
| Read a declaration | about 1 ms |
| Write a declaration that adds only a comment | about 24 ms |
| Write a method declaration that changes the signature | about 0.37 s |

The expensive part is not the COM call itself. When a write changes a signature, TwinCAT checks
the owner function block again. This takes time in each method.

### Method A2: keep the project (in use)

`build_nflite_machine.ps1 -Incremental` keeps the work folder. Then it runs `bai sync all` into
the kept project. The tool reads each object (1 ms) and writes only the changed objects.

| Case | Time (UVM) |
|---|---|
| No change, or changed or new `.st` files | about 2.2 min |
| Same, with `-Run` (activate and verify) | about 4.0 min |
| Four machines, `-Run -Parallel -Incremental` | about 10.5 min (fresh: about 21 min) |

A kept project must be equal to a fresh project. `sync` never deletes an object, a variable link
or an I/O item. For this reason, the script makes a fresh build when:

- a `.st` file was deleted or renamed (checked before the sync);
- a manifest changed (`io-devices.xml` with its links, `variants.xml`, `libraries.xml`,
  `events.xml`, `event-classes/`, `plc-data-types/`);
- the sync reports `[drift]` (for example, a method was removed from a file).

Test (2026-10-15): change one file and add one file. Sync the kept project. Make a fresh project
from the same sources. Export the code of both projects. Result: 406 files each, no difference.

Method A2 makes the daily work fast. It does not help a fresh build (a release build, the first
build of a new machine, or a build after a deletion).

## 3. Method B: one PLCopen XML import (tested, not in use)

### How it works

1. The tool writes all parsed objects into one PLCopen XML file (TC6 0200 with the 3S
   extensions).
2. One call `ITcPlcIECProject.PlcOpenImport` makes all objects and folders.
3. The normal sync runs after the import. It writes each text that is still different.

### What the import keeps

| Object kind | Declaration as plain text (3S `interfaceasplaintext`) | Body |
|---|---|---|
| FUNCTION, FUNCTION_BLOCK, PROGRAM | Kept (equal to the source, line endings aside) | Kept exactly |
| STRUCT, ENUM, ALIAS, GVL | Kept | – |
| METHOD, PROPERTY | **Not kept**: TwinCAT makes a stub (`METHOD Run`) | Kept exactly (also Get/Set) |
| INTERFACE | **Not kept** | – |
| Folders (3S `projectstructure`) | Kept | – |

### Measured result (UVM)

| Step | Time |
|---|---|
| Import of 1593 objects | 40.6 s |
| Sync after the import: 1224 declarations to write (methods, properties, interfaces) | 7.6 min |
| Total `init` | 10.6 min (Method A: about 10 min) |

The import is fast. But TwinCAT does not keep the plain-text declaration of methods, properties
and interfaces. The sync must write 1224 declarations. Each write changes a signature and costs
about 0.37 s. Thus Method B is not faster than Method A.

The round trip was also not exact: 6 of 405 files had one extra empty line after a member
declaration without variables. (The sync ignores line breaks at the end of a text, so it did not
write the stub `METHOD Halt` + line break again.)

Decision: Method B is not in use. The code is kept in the tag `archive/spike-plcopen-bulk-import`
(commit `cba0865`; the branch was deleted on 2026-10-17): `Sync/PlcOpenWriter.cs`, `Sync/PlcOpenBulkImport.cs`, unit tests, and the
`init --no-bulk-import` option.

### Lessons from the spike

- `PlcOpenExport` takes a selection relative to the PLC project, separated by dots and by `;`
  (for example `Lib.Math.F_ModAbs;App.NFLite.PRG_OZONE`). A tree path with `^` is refused.
- TwinCAT checks the XML against the PLCopen schema. One error stops the whole import. For
  example, an enum needs at least one `<value>`.
- TwinCAT's own export writes the declaration only as structured XML, not as plain text. An import
  of that form makes TwinCAT write the declaration text again. Comments, line breaks and pragmas
  then move. This breaks the exact round trip and the error-line mapping of the tool.

## 4. Method C: write the TwinCAT project files directly (proposed, paused)

### The idea

TwinCAT keeps each PLC object as one XML file in the PLC project folder:

| File | Contents |
|---|---|
| `.TcPOU` | PROGRAM, FUNCTION_BLOCK or FUNCTION, with its methods, properties and actions |
| `.TcDUT` | STRUCT, ENUM, ALIAS or UNION |
| `.TcGVL` | Global variable list |
| `.TcIO` | INTERFACE, with its methods and properties |

In these files, the declaration and the body are plain text in a CDATA block. The text is the same
as in our `.st` files. The `.plcproj` file lists each object file (`<Compile Include="...">`) and
each folder (`<Folder Include="...">`).

The tool would do these steps:

1. Close the shell (the tool does this already for its `.tsproj` edits).
2. Write one TwinCAT file for each `.st` file, and the entries in the `.plcproj`.
3. Open the project one time.

There is no COM call for each object. TwinCAT does not change the text, because it reads the text
from the file.

### What we expect, and what we do not know

| Item | Expected or unknown |
|---|---|
| Time to write the files | Seconds |
| Time for TwinCAT to open and check the project | Unknown. Must be measured. |
| Exact text (comments, line breaks, pragmas) | Expected: yes, because the text is stored as written |
| Object IDs (GUIDs) for each object and member | The tool must make them. Rules must be checked. |
| File format for each TwinCAT version | Must be checked against an export from 4026 |
| Encoding and line endings | Must match TwinCAT's own files |
| Error-line mapping of the tool | Must still work after the change |

### Plan for the spike (when work continues)

1. Make a small project with Method A. Read its `.TcPOU`, `.TcDUT`, `.TcGVL`, `.TcIO` and
   `.plcproj` files. Record the format.
   - Pass: each element and attribute of the format is known.
2. Write the files for 10 objects of each kind (function block with methods and properties,
   interface, enum, alias, GVL, program, folders). Open the project. Read the texts back over COM.
   - Pass: each text is equal to the source, and the build passes.
3. Do this for all objects of UVM. Measure the time. Export the code. Compare it with a project
   made with Method A.
   - Pass: 0 different files, the build passes, all links resolve, and the time is clearly less
     than Method A.
4. Do it for all four NFLite machines with `build_nflite_machine.ps1 -Run -Parallel`.
   - Pass: all machines pass the live check on UmRT_Default.

Status: paused on 2026-10-15. We continue this work later.

## 5. Summary

| Method | Fresh build (UVM) | Daily change (UVM) | Exact round trip | Status |
|---|---|---|---|---|
| A | about 10 min | – | Yes | In use (`init`) |
| A2 | – | about 2.2 min | Yes (tested) | In use (`-Incremental`) |
| B | 10.6 min | – | No (6 files) | Not in use |
| C | Unknown (expected: much less) | – | Expected: yes | Paused |
