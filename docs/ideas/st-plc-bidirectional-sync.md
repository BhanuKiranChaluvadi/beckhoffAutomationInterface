# ST-as-Source-of-Truth: Bidirectional TwinCAT Sync with LLM Feedback Loop

## Problem Statement
How might we make the .st source tree the single source of truth for a TwinCAT
PLC project, keep it automatically and incrementally in sync in both
directions, and give an LLM a fast, reliable compile-feedback loop — while
still allowing rare manual PLC-side edits to flow back into source?

## Recommended Direction
A git post-commit hook (running detached, non-blocking) diffs the ST repo
against the last-synced commit SHA, and feeds only the changed/deleted .st
files into a new incremental mode of the existing C# sync engine, which also
prunes PLC objects whose source file was deleted, then builds and writes a
report. Manual PLC-side edits (IO-scanned DUTs, and — pending
re-investigation, see below — Event Classes) are pulled back into .st on
demand via a new `--export <ObjectName>` command, using
ITcSmTreeItem.ProduceXml() + a native C# XML-to-ST converter, writing
directly to the object's mirrored .st folder path (no separate staging
folder — git diff/review before committing is the review step, since the ST
tree is itself git-tracked). A new C# ST formatter/linter runs as a preflight
step before every sync. Ignore rules come from a .stignore file
(gitignore-style patterns) merged with an optional --ignore CLI argument.
Everything stays in C# — no Python runtime dependency; tctool remains
reference prior art only. Runs locally only for now (the Automation
Interface needs a real licensed VS+TwinCAT desktop session); a CI/cloud
pipeline is a later problem, not a v1 concern.

## Key Assumptions to Validate
- [ ] **Event Classes may NOT be a dead end — re-investigate before ruling
      out.** This session's 4 attempts (last-child of `<System>`, first-child,
      with the `<Hides>` block, wrapped in a synthesized `<TypeSystem>`
      element) all had Visual Studio silently drop the `<DataType>` block on
      its own save. However, the user has successfully done exactly this
      before (close project → edit .tsproj directly → generate matching
      GUIDs → reopen) via an LLM-assisted session. Next step: get the user's
      exact prior working recipe/script and replicate it precisely — our 4
      variants may have missed one specific detail (e.g. a different parent
      element, an additional required reference elsewhere in the file, or a
      GUID that must match something else already present). Do not
      re-attempt blind guessing again; anchor to the known-working example.
- [x] **RESOLVED (spike, 2026-07-06):** `ProduceXml()` on a POU tree item
      does **not** return the ST source — it returns only metadata
      (`FileName`/`FullPath`/export flags/VSProperties; `ChildCount=16` with
      no inline Declaration/Implementation text). Confirmed against
      `FB_HeatZone` in the real Shark project. The correct export path is the
      read-side counterpart of the existing write-side sync engine: read
      `((ITcPlcDeclaration)item).DeclarationText` and
      `((ITcPlcImplementation)item).ImplementationText` directly (both are
      documented as get/set, per
      `example/TC_AI_DOTNET_Samples/.../GeneratePlcProject.cs` lines ~766-773).
      No XML-to-ST converter is needed — `PouSyncEngine.cs` already writes
      these same properties for sync-to-PLC, so export is just the getter
      side of code that already exists. This de-risks Phase 5 significantly.
- [x] **RE-VERIFIED empirically (2026-07-06):** cross-checked the finding
      above against the official examples knowledge graph (94 files: TC2
      samples, TC_AI_DOTNET_Samples, ELT/soup01 tutorials) — no better/more
      complete export mechanism exists anywhere in the examples;
      `TcXmlConverter.cs` is unrelated hex/bool XML parsing, and the
      `ProduceXml`/`ConsumeXml` docs are TC2 EtherCAT/routes-specific, not
      TC3 PLC POU source. Then did a live read-back spike (`FB_HeatZone`,
      including 2+ child METHODs) and diffed the actual returned text
      against the real `.st` source. Two new concrete facts confirmed:
      1. **No `END_METHOD`/`END_FUNCTION_BLOCK`/etc. terminators are stored**
         in `DeclarationText`/`ImplementationText` — matches
         `StFileParser.StripPouTerminators` exactly. Export must **re-add**
         the correct terminator per POU/member kind (the exact reverse
         operation) to produce valid `.st` files.
      2. **Encoding fidelity gotcha**: a `→` (U+2192) character inside a
         comment was silently dropped on round-trip (source: "any channel
         fault → zone fault", read back: "any channel fault  zone fault" —
         arrow replaced with nothing, not even a placeholder). Likely
         TwinCAT stores POU text in a legacy ANSI/codepage format
         internally. This doesn't affect code semantics (comments only, in
         this case) but the export command should document this as a known
         lossy-round-trip risk for non-ASCII characters in ST source
         (comments or string literals), not silently pretend it's lossless.
- [x] **RESOLVED (spike, 2026-07-06):** Setting `DeclarationText`/
      `ImplementationText` unconditionally on ALL 1261 objects (a full
      resync with zero .st source changes) produces **zero git diff** on any
      `.TcPOU`/`.TcDUT`/`.TcGVL` file. Verified by temporarily `git init`-ing
      the real Shark project, committing a clean baseline, running a full
      non-build-only resync, then checking `git status`/`git diff`: only
      Visual Studio's own IDE cache (`.vs/Shark/v17/.suo`,
      `.vs/Shark/v17/fileList.bin` — both binary, normally gitignored) and a
      transient `Shark.~u` lock file changed; zero PLC source files changed.
      **This disproves the original "unchanged writes cause git noise"
      assumption** — TwinCAT's own project save appears to be
      content-aware/idempotent internally, regardless of whether our code
      calls the setter. Consequence: the incremental sync mode (Phase 3) is
      still valuable for **speed** (a full 1261-object resync takes ~2
      minutes; only touching changed/deleted objects would be much faster),
      but is NOT needed to avoid git noise — that concern was unfounded.
      Temporary git repo was removed after the test; the Shark folder is
      back to its original (non-git-tracked) state.
- [ ] A detached/background hook process can reliably report success/failure
      back to the developer (log file? Windows toast? VS Code task?) without
      blocking `git commit`.
- [ ] git diff against a tracked "last synced SHA" is robust across rebases/
      squash-merges (may need a fallback to full resync if the stored SHA is
      no longer an ancestor of HEAD).

## MVP Scope
- Incremental sync mode: given a list of changed/deleted .st file paths,
  sync only those objects (create/update the changed ones, delete PLC
  objects whose .st file is gone) — new PouSyncEngine entry point; the
  existing full engine stays as-is for the first bootstrap run. (Motivation
  is now SPEED only, not git noise — see resolved assumption above.)
- .st-sync-state file tracking the last-synced commit SHA.
- post-commit git hook (PowerShell script) that computes the diff and
  launches the sync detached.
- .stignore file + --ignore CLI flag, applied during ParseFolder. **DONE
  (2026-07-06)**: `Sync/IgnoreRules.cs` (gitignore-style glob matching),
  wired into `StFileParser.GetStFiles`/`ParseFolder` and both call sites in
  `Program.cs` (`--parse-only` preflight and the main sync path). Verified
  functionally against a scratch temp folder (not the real project).
- `--export <ObjectName>` command: read
  `DeclarationText`/`ImplementationText` directly from the tree item (same
  properties `PouSyncEngine` already writes) → .st file written to the
  inferred mirrored folder path. No XML parsing/conversion needed.
- Native C# ST formatter (indentation/style) + linter (naming/syntax),
  run automatically before every sync.
- Event Class "declared vs actual" checker (replaces the write-based
  `EventClassSync.cs`, since automating Event Class *creation* is a
  confirmed dead end). **DONE (2026-07-06)**: new read-only
  `Sync/EventClassChecker.cs` reads `events.xml` (via the existing
  `EventManifestParser`) and compares against the `.tsproj`'s
  `Project/System/DataType/Name` elements directly (no VS session needed),
  reporting which declared classes are present vs missing — a human still
  has to create missing ones via the XAE UI. Wired into `Program.cs` in the
  same early spot the old write used to run. `--events-only` now exits
  right after this check, before opening Visual Studio at all (it no longer
  needs to, since there's no write to validate through a VS round-trip).
  The old `EventClassSync.cs` (confirmed non-working file-edit approach)
  was deleted — no longer referenced anywhere. Verified functionally with a
  scratch fake `.tsproj` + `events.xml` (2 declared, 1 present, 1 missing
  reported correctly), no real project or VS involved.

## Not Doing (for now, and why)
- Continuous file-watcher daemon — a background always-on process is more
  operational surface area than a git hook for marginal latency gain.
- CI/cloud runner pipeline — needs a real licensed VS+TwinCAT XAE desktop
  session; get the local git-hook workflow fully working first, revisit a
  server-side pipeline later.
- Reusing tctool (Python) as a runtime dependency — single-language (C#)
  project per the user's explicit preference; tctool's converters remain
  useful as a reference for the XML shape, not as a dependency.

## Open Questions
- None blocking further MVP work — remaining pieces (ST formatter, incremental
  sync mode, git hook, `--export` command) are independent of each other and
  can proceed in any order.
