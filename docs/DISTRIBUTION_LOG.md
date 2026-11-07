# Automated BAI distribution, 2026-11-07

Request: remove manual runner copies and repeated SHA updates in BeckhoffLib.

Decision: publish tested, immutable bundles from this repository. BeckhoffLib adopts the
latest descendant build on a 15-minute poll and installs its committed pin per job. Separate
build publication from consumption so neither repository needs a cross-repository write token.
Keep historical bundles and do not replace an executable held by a running process.

Actions and reasons:

- Traced the existing duplicated version/path/hash values and confirmed variable drift.
- Added hosted Windows tests/build/package/publication for main, and tests without publication
  for pull requests. No PLC source, TwinCAT instance or machine deployment is changed here.
- Included the executable, runtime DLLs and config in the bundle. Record the source commit,
  archive hash and each file hash; smoke-start the executable before publication.
- Keep existing release bytes on reruns; check current main before promotion.
- A local packaging probe exposed a stale previously built executable. Packaging now refuses
  an executable whose reported source commit/version differs from the checkout; rebuild first.
- Local Release unit suite: **1085 passed, 0 failed, 0 skipped**. One existing xUnit blocking-task
  analyzer warning remains. Distribution itself is additionally verified by the hosted workflow.
- First hosted run `42363210943` passed tests/build/package but failed publication: Windows
  PowerShell 5.1 treated the expected first-release absence on gh stderr as a terminating error.
  Use PowerShell 7 for this orchestration step; preserve the failed run as evidence.
- Hosted run [42363404059](https://github.com/BhanuKiranChaluvadi/beckhoffAutomationInterface/actions/runs/42363404059)
  passes every stage and publishes `build-8d5f1f8e6282c54be5ac2740c4e5580b7eb8e8a8`.
  BeckhoffLib downloaded this release, verified all six files and started the executable with
  the matching full source commit. This log update exercises a subsequent automatic build;
  it does not change BAI runtime behavior.
- Publish through a draft: upload all assets before making a release latest/visible. Retry
  repairs unpublished drafts only. This closes an interrupted-upload case where a partially
  uploaded public release would otherwise be mistaken for an immutable complete build.
- No worktree was created. Commit only the new workflow, bundle script and distribution docs;
  do not commit generated binaries or test results.
