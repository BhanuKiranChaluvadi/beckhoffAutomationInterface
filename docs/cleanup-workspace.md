# Keep BAI work folders together

Use short build paths. TwinCAT projects can exceed the Windows path limit when
generated below a long workspace path. New work should use `C:\bai\work`, binaries
`C:\bai\bin`, and synthetic fixtures `C:\bai\fixtures`.

`scripts/Manage-BaiWorkspace.ps1` inventories legacy `C:\bai-*` folders. It does not
contact a PLC or change the installed TwinCAT system. Close BAI/XAE and stop other
build or installer jobs before applying cleanup.

From the BAI repository, run:

```powershell
# Show sizes and reasons to keep each folder. Writes a JSON report only.
.\scripts\Manage-BaiWorkspace.ps1

# Preview consolidation of inactive folders older than one day.
.\scripts\Manage-BaiWorkspace.ps1 -Action Consolidate

# Apply the preview: completed synthetic fixtures go to fixtures; other work to archive.
.\scripts\Manage-BaiWorkspace.ps1 -Action Consolidate -Apply

# Preview removal of completed synthetic fixtures, including those already consolidated.
.\scripts\Manage-BaiWorkspace.ps1 -Action DeleteFixtures
.\scripts\Manage-BaiWorkspace.ps1 -Action DeleteFixtures -Apply
```

`-MinimumAgeDays 0` includes today's inactive work. `-Names bai-example,bai-example2`
limits an action to exact names. `-ProtectPaths C:\bai-example` adds a protected path.
For a test drive or another short collection path, use `-Root` and `-CollectionRoot`.
Every action defaults to preview; only `-Apply` moves or deletes content.

The script keeps Git checkouts/worktrees, junctions, folders referenced by running
processes, the current script/directory, PATH entries, the BAI_EXE directory and
explicitly protected paths. It also keeps these known session folders:

| Folder | Reason |
|---|---|
| `C:\bai-command-migration` | Working checkout, pinned BAI binaries and current test evidence |
| `C:\bai-offline-completion` | Release bundles, toolchain archive, build projects and validation receipts |
| `C:\bai-test` | Earlier runtime/package evidence |
| `C:\bai-warning-fix-20261019` | Separate unfinished warning-fix work |
| `C:\bai-lock` | Shared runtime lock; all cooperating tools must use the same path |

An archive move preserves files but does not rewrite embedded absolute paths.
Do not reopen an archived project for deployment. Rebuild it at the selected work
path. Historical evidence paths can be found through the JSON old/new path map in
`C:\bai\cleanup`. Do not delete the collection folder as a whole: inspect `archive`
first and retain any test evidence or source copies that you still need.

Deletion is restricted to GUID-named boot-transaction and BSD-coordinator fixtures
with their expected successful `validation.json`. These contain synthetic payloads.
The script never automatically deletes arbitrary projects, binaries or release bundles.
It refuses destination collisions and records each successful move/delete immediately.
If an operation fails, read the report: earlier operations can have completed.

Filesystem acceptance tests: `powershell -NoProfile -File e2e/cleanup-workspace.ps1`.
These cover preview, byte preservation, Git/release protection, junction refusal,
path mapping, destination collisions, selective deletion and traversal rejection.
