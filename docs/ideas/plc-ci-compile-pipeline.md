# PLC CI Compile Pipeline

## Problem Statement
How might a 2-3 person team get an automatic pass/fail compile signal on every push to main/master of the (separate) Beckhoff PLC project repo, using hardware/licensing we already own, without adding operational overhead disproportionate to team size?

## Recommended Direction
The PLC project repo checks in native TwinCAT artifacts (`.tsproj`/`.plcproj`/`.sln`) directly — there is no `.st` source tree to sync or reconstruct. That means this tool's sync engine is irrelevant to that repo's CI; only its `--build` stage matters.

The TwinCAT XAE engineering license on the test bench machine is full/permanent and node-locked to that PC — a cloud or hosted Windows runner is not an option regardless of cost, so this isn't a build-vs-cost trade-off, it's the only viable machine. The bench machine is otherwise shared for hardware testing, but a compile-only job never activates a configuration or touches real hardware (confirmed by this tool's existing behavior of disabling unresolved EtherCAT masters so builds pass unattended), so it can coexist with other work on that machine today.

Plan:
1. Install the GitHub Actions self-hosted runner (Windows service) on the test bench machine, scoped to the PLC project's repo.
2. One workflow, `on: push: branches: [main, master]`.
3. Checkout the PLC repo, then run this tool in read-only adoption mode:
   `beckhoffAutomationInterface.exe --tsproj <checked-out-path>\<Project>.tsproj --plc-name <RealPlcName> --build`
   — this path is already documented and live-validated for exactly this case ("adopting an arbitrary pre-existing project"): opens via COM, compiles, reports errors, never calls `Project.Save()`/`Solution.SaveAs()`.
4. Exit code gates the job (0 = pass, 1 = fail/timeout). Status shows up via GitHub's native commit-status UI — no Slack/email needed for a team this size.

## Key Assumptions to Validate
- [ ] The runner-agent install is acceptable on the shared bench machine per whatever informal IT practice the team follows — confirm by just installing it and watching for any conflict on the first few runs.
- [ ] `--tsproj`/`--plc-name` correctly resolves against the checked-out repo's actual `.tsproj` path and internal PLC project name (check the real `.plcproj` filename inside the repo if the two differ) — verify with one manual run before wiring the workflow.
- [ ] Compile-only runs stay non-disruptive to concurrent hardware testing on the bench machine — watch the first week of real usage rather than assuming.

## MVP Scope
**In:** self-hosted runner, single push-triggered workflow, `--tsproj`/`--build` compile step, pass/fail exit code, native GitHub commit status.
**Out:** everything below.

## Not Doing (and Why)
- **Cloud/hosted Windows runner** — impossible; license is node-locked to the bench PC, not a cost choice.
- **Required PR check / blocking merges** — start status-only; revisit once the pipeline's proven reliable for a few weeks.
- **Concurrency guarding between overlapping pushes** — real risk (two pushes racing the same TwinCAT COM session) but unlikely to matter yet at 2-3 people's push frequency; add `concurrency:` grouping only once it's actually bitten someone.
- **Multi-project build matrix** — only one real PLC project repo exists today; premature to generalize.
- **Slack/email notifications** — GitHub's own status UI is enough visibility for a 3-person team.
- **Running actual tests / hardware-in-the-loop** — explicitly deferred; team's own plan is to revisit this later, possibly overnight when the bench machine is free.

## Open Questions
- Does `devenv.com <Solution>.sln /Build <config>` work as a simpler no-custom-tool alternative to `--tsproj`/`--build`? Not verified for TwinCAT PLC compiles across TC3 versions — worth a 10-minute manual spike on the bench machine sometime, but not blocking the plan above since the COM-based path is already proven.
- Does the PLC project repo's `.tsproj` already have a `.sln`, or is it a "loose" `.tsproj` with no solution file? Determines whether `--tsproj` (bypasses `.sln` resolution) is required or `--dest`/`--name` would also work.
