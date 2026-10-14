# Spike: per-variant I/O items and links (Task 8)

Date: 2026-10-14. TwinCAT 3.1.4026.24, TcXaeShell, scratch copy of a project built from
`ST/VariantSample` (variants MachineA / MachineB, implicit defines on).

Setup: EtherCAT master → EK1100 → **EL1008** (all machines) and **EL2008** (MachineA only).
`GVL_IO.bIn AT %I*` linked to EL1008 channel 1, `GVL_IO.bOut AT %Q*` linked to EL2008 channel 1.

## Results

| # | Question | Result |
|---|---|---|
| 1 | Can an I/O item be present in some variants only? | **Yes.** `ITcSmTreeItem9.PvDisable = true` on the item, then per variant (`CurrentProjectVariant = X`) set `Disabled`. EL2008: MachineA `SMDS_NOT_DISABLED`, MachineB `SMDS_DISABLED`. |
| 2 | Do both variants build when MachineB has the EL2008 disabled but a link to it exists? | **Yes.** MachineA and MachineB: 0 failed projects. A link to a terminal that is disabled in a variant is harmless. |
| 3 | How is it stored / how can a tool read it back? | `.tsproj`: `<ProjectVariant PvUseDisable="true">` with one `<Variant><Name/><Disable/>` per variant under the item. **`ITcSysManager14.ProjectVariantItems` returns the whole state as one XML** (`<Item Name="TIID^…" PvUseDisable="true"><Variant><Name>MachineA</Name><Disable>false</Disable>…`). Use it for `export io`. |
| 4 | Can a link exist in one variant only (`PvLink`)? | **No (not this way).** `PvLink = true` on the PLC variable is accepted (`PvUseLink="true"`), but `UnlinkVariables` while MachineB is active removed the link for **all** variants. The `.tsproj` has one global `<Mappings>` block. **Decision: links are variant-neutral** (plan D6 fallback). No `Variants` attribute on `<Link>`. |
| 5 | Can a `%I*`/`%Q*` variable be linked right after it is declared? | **Only once it is in the process image.** An `AT %I*` variable that no code uses is left out of the `.tmc` (only `PlcTask Internal` exists) and `LinkVariables` fails with `0x98510001`. After a build where the variable is used, `PlcTask Inputs/Outputs` exist and linking works. (`ITcSmTreeItem.ChildCount` of the PLC instance stays 0; link by path.) |
| 6 | What happens on UmRT with an enabled EtherCAT master? | Activation succeeds, but **TwinCAT stays in Config** (no EtherCAT adapter on UmRT). The license was valid. So the "stays in Config" cause is not always the license. |

## Consequences for the plan

- Task 9/10: `Variants="A,B"` attribute on `<Device>`, `<Box>`, `<Terminal>` only — **not** on `<Link>`.
- Task 10: write with `PvDisable` + `Disabled` per variant (proven); read with `ProjectVariantItems`.
- Task 11: `export io` reads `ProjectVariantItems` → `Variants` attribute.
- New Task 15b: `activate --target umrt` must disable the I/O devices for that activation (in memory;
  the project is attached read-only), and the "stays in Config" hint must check the license before
  blaming it.
- PLC code rule D7 holds: declare `%I*`/`%Q*` for all machines; a link to an item disabled in a
  variant is harmless. Note result 5: an I/O variable must be used to exist in the process image.
