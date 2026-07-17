# CLI Subcommand Redesign

## Problem Statement
How might we replace this tool's ~20 flat CLI flags with a subcommand structure, so each operation only exposes the arguments relevant to it, and a flag can't silently do nothing depending on which other flags happen to be present (exactly what happened with `--tsproj` before it was fixed to work with `--build` alone)?

## Recommended Direction
The core fix is not subcommand syntax for its own sake — it's collapsing the two competing "how do I find the project" mechanisms into one. Today there are two: the `--source`/`--dest`/`--name` triple (implies a `Dest\Name\Name.sln` convention) and `--tsproj`/`--plc-name` (bypasses it entirely). That duality is what actually produced the earlier bug, where `--tsproj` was silently ignored unless paired with an `--export-*` flag.

Ship as one clean break (chosen over keeping old flags working, since a compatibility shim would undercut the whole point):
- Verb subcommands: `build`, `sync code|libs|io|events|all`, `check links|events|format`, `export code|libs|io|events|all`, `init`. Each has its own argument set — combinations that never made sense (e.g. `--export-all --build` together) become structurally impossible instead of just undocumented.
- Unified project targeting: one positional path everywhere (`beckhoff build <path>`). `<path>` can be a `.tsproj` file, a folder containing exactly one, or a conventional `ST/<name>` source tree — the tool resolves which. `--plc-name` becomes a rare override flag, not a load-bearing companion you have to remember to pair with something else.
- Layered on top, same pass, low incremental cost: lean harder into the existing `.stconfig` so most subcommands need zero path arguments in the common case (teammate `cd`s into a project folder, runs `beckhoff build`); add `--json` to `build`/`check` for CI to consume structured pass/fail + error-location output instead of scraping console text.

Update README, `plc-ci-setup-guide.md`, `github-self-hosted-runner-install-steps.md`, and `githooks/*.ps1` in the same pass — none of them can be left referencing the old flags.

## Key Assumptions to Validate
- [ ] Teammates actually invoke this tool directly by hand sometimes, not only via CI/githooks scripts — if not, "discoverability for teammates" delivers much less value than assumed. Check by asking the team directly.
- [ ] A single positional-path resolver can reliably distinguish "loose `.tsproj`" vs "conventional `ST/<name>` tree" without ambiguity — spike this against 2-3 real project layouts before committing to the design.
- [ ] Migration cost is real and non-trivial (four docs/scripts to rewrite) — budget for it explicitly rather than treating this as a quick refactor.

## MVP Scope
**In:** subcommand dispatcher replacing the flat flag parser; unified positional project-targeting resolver replacing both `--source/--dest/--name` and `--tsproj/--plc-name` as separate mechanisms; doc/script updates in the same pass.
**Out:** everything below.

## Not Doing (and Why)
- **Splitting into two separate tools** (a `.st`-sync tool + a minimal CI-only build/check tool) — same clarity achievable with one clean subcommand structure, without a second artifact to maintain.
- **Keeping old flags working alongside new ones** — a compatibility shim would preserve exactly the kind of silent flag-interaction risk this redesign exists to eliminate.
- **Noun-first syntax** (`beckhoff project build`, kubectl-style) — reads worse for a tool whose job is "do this action to a project"; verb-first (git/docker-style) is more familiar to teammates and was rejected in favor of that.
- **Doing this now** — deliberately deferred until the current CI pipeline validation (VS 2022 install, first real `--build` against `PLC_NFL_SHARK_V2`) is finished end-to-end.

## Open Questions
- Does `System.CommandLine` (or a hand-rolled dispatcher) fit better given this is a `net48` console app? Worth a short spike before committing to the parsing approach.
- Exact subcommand/flag names (e.g. `sync all` vs `sync` with no sub-verb) — implementation detail for the execution pass, not this planning pass.
