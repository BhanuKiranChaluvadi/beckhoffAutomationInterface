# CoE startup commands

`sync io` and `sync all` accept an optional `CoE` section under a `Box` or `Terminal`:

```xml
<Terminal Name="Analog input" Product="EL3174">
  <CoE>
    <InitCmd>
      <Transition>PS</Transition>
      <Comment>Example only: confirm the object and bytes for the actual terminal</Comment>
      <Timeout>1000</Timeout>
      <Ccs>1</Ccs>
      <Index>#x8000</Index>
      <SubIndex>19</SubIndex>
      <Data>0500</Data>
    </InitCmd>
  </CoE>
</Terminal>
```

The fields follow TwinCAT's native mailbox CoE `InitCmd` XML. Data is a hexadecimal byte
string in device byte order, not a decimal engineering value. The example is an offline
test input, not a recommended device setting. Index/subindex accept decimal or hex notation.
Commands retain their list order, including repeated writes to the same object.

- No `CoE` section: leave existing user startup commands unchanged.
- `<CoE/>`: explicitly remove all user startup commands on that terminal.
- A populated section: replace the user startup list with that ordered list.
- TwinCAT's `Fixed` commands are generated from its ESI/PDO configuration. They are excluded
  from the manifest; import checks that their native list remains unchanged.
- Import reads the list back from TwinCAT and fails on any difference. Unknown native
  metadata is retained in export; unsupported data cannot silently count as a successful import.
- `export io` and the I/O part of `export all` retain user commands, or an explicit empty
  section for a CoE-capable terminal. They do not read online device values.

The native path is `TreeItem/EtherCAT/Slave/Mailbox/CoE/InitCmds`. Motor calculation data
under `Inverter/Channel/Block/Motor/Calc/CoE` is a different structure and is not imported by
this feature. SoE, safety-project export and task export remain separate work.

Use an exported list where possible. Confirm actual terminal revisions and settings on
the machine before building a replacement configuration. A successful offline round trip
proves project preservation; it does not prove that a physical slave accepts the commands.

Validation evidence is retained in `C:/bai-coe-spike/` on the development PC. The test uses
a disabled EtherCAT master, EK1100 and EL3174 in an isolated XAE project. No physical target
is activated. Unit cases cover malformed input, command order, fixed-command exclusion,
hex/decimal and CDATA normalization, and manifest export/import.

Repeat the end-to-end check with:

```powershell
powershell -NoProfile -File e2e/coe-startup.ps1 -Bai <bai.exe> -Work C:\new-coe-test
```

The work folder must be new. On 2026-10-18 all seven CLI stages passed in
`C:/bai-coe-e2e-20261018/`: initial build/export, unchanged re-import, fresh build/export,
and explicit clear/export. The two exported commands match exactly after the fresh build.
All **574 unit tests** passed (`C:/bai-coe-spike/tests/coe-tests-final.trx`). Tested executable
SHA256: `25B0C436B201D39F3D0861BE7A9B97228BAF4FEB7934FA16535591E4E72E0F86`.
This is an uncommitted development build at `C:/bai-coe-bin/beckhoffAutomationInterface.exe`.

The later COM-diagnostic follow-up passed **576 unit tests**, all seven CoE stages again
in `C:/bai-coe-final-rerun-20261018/`, and the native failure check in
`C:/bai-com-negative-20261018/`. Its executable SHA256 is
`6520C8F4DAD9567FDA25F5ACC0A9F4EFC8249570DEFF97D1B4CEE02859B069CE`.
The tested build is retained in `C:/bai-final-bin/`. See
[the XAE failure investigation](ideas/xae-sync-crash-20261018.md) for the failed first
attempt and the correction that preserves normal COM name-collision recovery.

After the four-machine NFLite matrix completed, the portable copy of this final tool also
passed packaged UVM identity verification and two NFPro input scenarios with five identity
checks. The normal `beckhoffAutomationInterface/bin/Debug/net48` executable now has the same
`6520C8F4...` hash. The matrix's earlier `9443EF4F...` tool is retained separately in
`C:/bai-test/stopfix-validation-20261018/bundle/bai/`; the final portable tool is in
`bundle/bai-reviewed/`. These are uncommitted development builds, not a new published version.
