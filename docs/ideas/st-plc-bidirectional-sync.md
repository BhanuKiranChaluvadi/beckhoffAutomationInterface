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
- [ ] `ProduceXml()` on a POU/DUT tree item returns XML shape close enough to
      a real exported .TcPOU/.TcDUT file to convert — spike this first, before
      building the export command around it.
- [ ] Setting DeclarationText/ImplementationText to unchanged text does (or
      doesn't) actually touch the PLC repo's file hashes/timestamps — test
      directly; this determines how much the incremental-write optimization
      actually buys us for "git noise" beyond just speed.
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
  existing full engine stays as-is for the first bootstrap run.
- .st-sync-state file tracking the last-synced commit SHA.
- post-commit git hook (PowerShell script) that computes the diff and
  launches the sync detached.
- .stignore file + --ignore CLI flag, applied during ParseFolder.
- `--export <ObjectName>` command: ProduceXml() → native C# converter → .st
  file written to the inferred mirrored folder path.
- Native C# ST formatter (indentation/style) + linter (naming/syntax),
  run automatically before every sync.
- Re-investigate Event Class automation using the user's known-working
  recipe before deciding whether it needs a manual-then-export fallback.

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
- None blocking MVP start — next concrete step is the Event Class
  re-investigation (get the user's prior working .tsproj-edit recipe) and
  the ProduceXml() spike, since several other pieces (export command,
  formatter) build on those results.
