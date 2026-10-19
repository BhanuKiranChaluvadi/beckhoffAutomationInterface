# XAE crash during fresh NFPro source import

On 2026-10-18, a fresh NFPro build started to import 1,568 PLC objects. XAE process 16004
failed at 15:46:07 local time. Windows Application Error event 1000 records an access
violation (`0xc0000005`) in `VCRUNTIME140_CLR0400.dll`, version 14.29.30154.0.

BAI then lost the COM server while `PouSyncEngine.SyncMethods` called `CreateChild` through
`TreeItemFactory`. It raised HRESULT `0x800706BE` and terminated with an unhandled exception.
This was during project creation, before a PLC build result. The Windows event identifies
the native crash location; it does not establish the underlying defect inside XAE.

Evidence is retained in `C:/bai-nfpro-inputs/crash-evidence/`: the complete BAI log, wrapper
log and Windows events. The failed generated project remains beside it. No physical target
was involved.

The recovery copied the previously compiled NFPro project to a new directory, applied the
two changed simulation files and a new build stamp, then used `sync all`. It compiled
successfully at 15:50:20. Evidence: `C:/bai-nfpro-inputs-recovery/build.log`. This provides
compile coverage of the change; it does not count as a successful fresh project build.

BAI now reports a failed `CreateChild` with the full object path, item type and original
HRESULT. The top-level pipeline also handles COM failures with exit 1. Regression tests
inject lost-server and name-collision errors and verify the diagnostic while preserving
the original COM exception type, HRESULT and instance.
This improves failure reporting; it does not repair or automatically retry the XAE crash.

The first diagnostic implementation wrapped the COM exception. The live CoE test caught
that this broke recovery from the template's existing `MAIN` program. The final code adds
diagnostic context to the original exception instead. The failed test is retained under
`C:/bai-coe-final-e2e-20261018/`.

All 576 unit tests pass. The reusable `e2e/com-errors.ps1` also passed a native XAE negative
test: a function block named `MAIN` conflicts with the template program and must fail.
The CLI returned exit 1 with the object path and HRESULT, without an unhandled-exception
crash. Evidence: `C:/bai-com-negative-20261018/`.

If the crash recurs, retain the XAE crash dump and correlate the reported object with the
source import. Use that evidence to investigate XAE/background compilation. Do not record
the suspected internal cause as confirmed without a dump or a repeatable isolated case.
