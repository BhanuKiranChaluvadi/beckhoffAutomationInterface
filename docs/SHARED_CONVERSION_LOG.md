# Shared PLC conversion

2026-11-18

- Inspected the pinned release path, current incremental sync, PLC source format, test runners and workflow triggers. The repeated cost is object-by-object COM import; parsing and compilation are separate work.
- Created `feature/shared-plc-conversion` from the clean existing BAI branch. No existing work was reset.
- Wrote regression tests before implementing the conversion modules. Tests remain unrun at the user's request.
- Added a pure whole-object XML writer, an immutable content-addressed cache, and an offline project updater. Methods and properties travel with their owner; project IDs survive updates. Compiler pragmas remain source text.
- Connected the shared code stage used by init, sync, machine builds and PLC test projects. Adopted projects retain the existing shadow-copy/COM path. Kept unchanged projects retain the sync-manifest shortcut.
- Added `bai convert` to prepare shared source without opening XAE. Source content and converter identity choose cache entries; paths and machine names do not. Build identities, generated gas models and test doubles naturally have different content hashes.
- No PLC test suite, C# test suite, gate, runtime activation or release deployment has been run for this change.

- Compiled the tool and test project after the final refactor: build succeeded with zero errors and one pre-existing xUnit1031 warning in MachineCommandsTests. No tests were executed.
- Moved source hashing/file-kind decisions into the source model so XML conversion and the existing sync manifest share them without depending on each other's storage.
- Kept machine-dev sessions open and retained source-owned stale deletion before closed-project updates; untracked project members are preserved.
- Inspected crapper/dryer support: neither supports C#. Mutator was not run because it executes tests. XML acceptance and workflow contracts are explicitly pending.
- Recorded this implementation on an isolated local feature branch. No main merge/push, publication or tool-pin update is part of this untested change.

Pending acceptance (only when authorized): run the conversion regressions and a fresh TwinCAT build for each family plus representative property/interface and test projects. Measure cold/warm import times before claiming a speedup.
