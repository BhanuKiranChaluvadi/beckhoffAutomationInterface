# Implementation Plan: Reverse Export — Scaffold `.st` Source Tree from an Existing PLC Project

## Overview
Today the tool is one-directional: `.st` files + manifests → TwinCAT project
(create/update/build). This adds the **reverse, one-time bootstrap** direction:
given an *already-existing* TwinCAT PLC project (`--dest`/`--name`), regenerate
the whole `--source` tree — every POU/DUT/GVL as `.st`, plus `libraries.xml`,
`io-devices.xml`, `events.xml` (+ `event-classes/*.xml` templates), and
`links.xml` (variable links) — so a team can *adopt* this tool on a project that
already exists, then manage it as git-tracked ST source from then on.

This is explicitly a **bootstrap/adoption tool, not the ongoing flow.** The
forward direction stays the source of truth (see the "Not Doing" note in
`docs/ideas/st-source-twincat-sync.md`: auto-export must never become the
*primary* loop). Reverse is what you run *once* to create the source tree; after
that you edit `.st` and sync forward as normal.

## Key Insight: this is mostly a read-side mirror of code that already exists
The forward engine already reads almost everything we need — the work is
inverting readers we already have, not discovering new COM APIs:

| Artifact | Reverse mechanism | Reuse / status |
|---|---|---|
| `.st` (all POUs/DUTs/GVLs) | tree walk → `PlcObjectExporter.Export` | **`PlcObjectExporter` already exports any single object** with full round-trip fidelity (incl. methods/properties, terminators, INTERFACE special-casing). Just needs a whole-project walk. |
| `libraries.xml` | enumerate `ITcPlcLibraryManager.References` | **`LibrarySyncEngine` already reads + regex-parses** the display name into Name/Version/Company (`DisplayNamePattern`). Invert into a writer. |
| `links.xml` (variable links) | `ITcSysManager.ProduceMappingInfo` | **Already shipped as `--export-links`.** Reverse just calls it as part of `--export-all`. |
| `events.xml` + `event-classes/*.xml` | read `.tsproj` `<DataTypes>` pool | **`TsprojDataTypePool`/`EventClassChecker` already read the pool.** Dump each event-class `<DataType>` to a template file; emit `<EventClass Name Guid>`. |
| `io-devices.xml` | walk `TIID` Device→Box→Terminal | **The one genuine unknown** — `IoSyncEngine` *writes* `Product` as `vInfo` but never reads it back. Gated behind a Phase-1 spike (Task 2). |

## Architecture Decisions
- **Composable `--export-*` flags mirroring the `--sync-*` stages** (user
  decision 2026-07-15): `--export-code`, `--export-libs`, `--export-io`,
  `--export-events`, and `--export-all` = all four + the existing
  `--export-links`. Same "any subset runs exactly those, in a fixed order"
  model as `SyncStages`, and the same lazy-VS lifecycle (events needs no VS;
  code/libs/io/links do). Keeps the surface consistent and the diff small.
- **Overwrite is safety-gated** (user decision): if `--source` already contains
  `.st` files or any manifest, reverse-export refuses (exit 1) unless
  `--overwrite` is passed. Follows the established `--init` / `--confirm-delete`
  / `--confirm-delete-io` philosophy — destructive-ish effects never happen by
  accident, and `--overwrite` is CLI-only (never read from `.stconfig`).
- **Reuse, don't rewrite.** New code is thin *writers* + one *project walker*;
  each leans on an existing Sync/* reader. No new COM techniques except the
  gated IO product-read spike.
- **IO scoped behind a spike** (user decision): Task 2 proves whether a live
  terminal's `Product` is readable. Ship `--export-io` only if it works
  reliably; otherwise fall back to `.tsproj`/`.xti` XML parsing or defer
  `--export-io` while still shipping code/libs/events/links/all now.
- **Round-trip is the acceptance test.** For each artifact: reverse-export from
  the real project into a *scratch* source folder, then forward-sync that
  scratch source into a *scratch* project and confirm `--build` passes — proving
  the generated source is faithful. Never write into the real ST/Shark tree.

## Target CLI

```
Reverse export (regenerate --source FROM an existing --dest/--name project):
  --export-code     all POUs/DUTs/GVLs -> mirrored .st files under --source
  --export-libs     library references -> libraries.xml
  --export-io       TIID device/box/terminal tree -> io-devices.xml  (spike-gated)
  --export-events   .tsproj event classes -> events.xml + event-classes/*.xml
  --export-all      all of the above + --export-links (links.xml)
  --overwrite       allow reverse-export to overwrite existing files in --source
                    (required if --source already has .st files or manifests)

Already exists, unchanged, and folded into --export-all:
  --export <name>   single object -> its .st file
  --export-links    all variable links -> links.xml
```

**Fixed order for any selected subset** (mirrors the sync pipeline's phases):
events (no VS) → code → libs → io-tree → io-links/links.xml → done. VS is
opened lazily by the first artifact that needs the Automation Interface; a
lone `--export-events` never launches it.

## Task List

### Phase 1: Foundation + de-risk IO
- [ ] Task 0: Create task dir; commit current working tree first (clean baseline)
- [ ] Task 1: `--export-*` + `--overwrite` flags in `RunOptions` (+ `RunOptionsTests`)
- [ ] Task 2 (SPIKE): read a live terminal's `Product` back from the tree; decide IO include-vs-defer

### Checkpoint A
- [ ] Tests green; spike result recorded in this file; IO scope decided

### Phase 2: Low-risk reversers (reuse proven readers)
- [ ] Task 3: `ProjectCodeExporter` — walk POUs/DUTs/GVLs → all `.st` (reuse `PlcObjectExporter`)
- [ ] Task 4: `LibraryManifestWriter` — references → `libraries.xml`
- [ ] Task 5: `EventManifestWriter` — `.tsproj` pool → `events.xml` + `event-classes/*.xml`

### Checkpoint B
- [ ] Round-trip each: reverse into scratch source → forward-sync into scratch project → `--build` PASSED

### Phase 3: IO (if spike passed) + orchestration
- [ ] Task 6: `IoManifestWriter` — `TIID` tree → `io-devices.xml` (only if Task 2 passed)
- [ ] Task 7: Wire `--export-*`/`--export-all` into `Program`/`SyncPipeline` — lazy VS, overwrite guard, fixed order

### Checkpoint C
- [ ] `--export-all` into an empty scratch source produces a tree that forward-syncs + builds clean
- [ ] Overwrite guard: refuses on a non-empty source without `--overwrite`, proceeds with it

### Phase 4: Docs + real-project smoke
- [ ] Task 8: README (reverse section + flag table + typical adoption workflow); real Shark smoke into a scratch source dir; final commit

### Checkpoint: Complete
- [ ] All acceptance criteria in `todo.md` met; committed

## Risks and Mitigations
| Risk | Impact | Mitigation |
|------|--------|------------|
| Live terminal `Product` not readable from the tree | High (blocks `--export-io`) | Task 2 spike **before** committing to IO; `.tsproj`/`.xti` XML fallback; defer `--export-io` as last resort — everything else still ships |
| Reverse-export clobbers hand-edited `.st` source | High | `--overwrite` gate (CLI-only, refuses by default); all verification uses scratch source dirs, never ST/Shark |
| Event-class `<DataType>` entries indistinguishable from PLC data types / `MDP5001_*` types in the pool | Med | Heuristic on the event-class `<DataType>` shape (GUID + event SubItems); copy raw `<DataType>` verbatim to the template so the forward editor round-trips it; document per-`<Event>` field reversal as best-effort |
| Non-ASCII chars lossy on round-trip | Low | Known/documented for `--export` already (legacy codepage); carry the same caveat into the reverse docs |
| Enumerating IO tree double-counts terminals (flat + nested) | Med | Reuse the proven "genuine direct child only" rule (`PathName == parent^name`) from `IoSyncEngine.DeleteOrphans` |
| VS lifecycle regressions when only some `--export-*` run | Med | Reuse the existing lazy-`EnsureOpen`/`EnsureClosed` pattern; per-flag scratch runs in Checkpoint C |

## Open Questions
- **Event `<Event>` detail reversal:** `events.xml`'s `<Event Id/Severity/Message>`
  children are richer than what the forward path strictly needs (the editor
  consumes the raw `event-classes/<Name>.xml` template + a Name/Guid). Is a
  best-effort reconstruction of the `<Event>` rows enough for v1, or must it be
  byte-exact? (Proposed: best-effort; the template file is the source of truth
  for round-trip.) — resolve during Task 5.
- **`io-devices.xml` `<Links>` vs `links.xml`:** variable links are captured
  natively and completely by `--export-links` → `links.xml`. Do we also need to
  reverse them into `io-devices.xml`'s simpler `<Links>` section, or is
  `links.xml` sufficient? (Proposed: `links.xml` only; don't duplicate.) —
  confirm during Task 6/7.
- **Emit a starter `.stconfig`?** Reverse-export knows `dest`/`name`; optionally
  drop a `.stconfig` into the new `--source` so subsequent forward syncs need
  only `--source`. (Proposed: yes, nice-to-have in Task 8 if cheap.)
