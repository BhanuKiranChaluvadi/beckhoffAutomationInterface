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
- No worktree was created. Commit only the new workflow, bundle script and distribution docs;
  do not commit generated binaries or test results.
