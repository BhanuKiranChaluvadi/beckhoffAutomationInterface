# Release packaging, 2026-11-07

Request: reduce repeated release data and clean old local packages/work without losing source edits.

Measured `C:\bai` before cleanup: 37.846 GiB; `work\release` alone: 30.436 GiB.
The NFPro v0.27.0-rc.2 bundle occupied 2.251 GiB. Its ESI catalogs accounted for
2,280,785,913 bytes (847 files); its deployable machine ZIP was only 19.60 MiB.

Actions and reasons:

- Changed the release test first; observed its expected failure because expanded `esi/` remained.
- Archive the installed catalogs directly into `esi.zip`, with separate `system/` and `user/`
  namespaces. Preserve every file and any conflicting override; do not guess a minimum vendor set.
- Reuse the existing locked, hash-checked archive writer. Reopen every archive entry and compare
  its size/hash against the captured inventory. Record `esiArchiveSha256` in release evidence.
  Keep the installed-catalog comparison after compilation. Failures remain failed releases.
- The 28 existing release-builder cases pass. Add a conflicting-name/compression case to ensure
  both scopes survive and that the expanded catalog is not retained.
- Exercised the actual new method on the retained NFPro catalog: 2,280,785,913 bytes became
  149,606,049 bytes (93.4% smaller), with all 847 entries verified, in 31 seconds.
- Changed the service archive test first and observed the expected duplicate `_Boot`/`.vs`
  failure. Omit those two directories only; preserve libraries, compiler information, TMC
  files and all source/safety configuration. The new test passes.
- Exercised the actual package builder on the existing compiled NFPro artifacts, without
  compilation or activation: 20,553,041 bytes became 13,202,796 bytes (35.8% smaller).
  The package verifier passed. Independently compared all 15 boot files and all 626 retained
  service files with the original: every byte hash agrees. These probes are size checks,
  not a newly built or approved PLC release.
- Final Release unit suite: **1087 passed, 0 failed, 0 skipped** (3m51s). One existing
  xUnit blocking-task analyzer warning remains. No TwinCAT build or runtime test is required
  for the archive-only change; physical machine acceptance is not claimed.
- Ran crapper, dryer (threshold 0.8) and mutator scan on the changed C# file: these installed
  tools do not support C# and reported no source files. Complexity/coverage/mutation scores
  from those tools are unavailable; this is not a quality pass. Behavior is covered by the
  C# release tests and the real catalog probe.

Do not rewrite previously issued machine ZIPs or their hashes during cleanup. Keep installed
tool directories, runtime locks, deployment records and worktrees with uncommitted changes.
Local retention inventories and exact archive/file hashes are in
`C:\bai\evidence\cleanup-20261107\`.
