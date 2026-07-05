(*
E_PressureController — Pressure controller identifier used by both PLC code
and the C# DALP Manager.

The C# client iterates this enum to discover PCTR_ entries via to_string,
using the legacy "A_Side" / "B_Side" naming scheme:
   BindViewModel(model.FindPressureCtrl("A_Side"), ...)

PLC semantics:
   A_Side (1) — Precursor-Hot Bronkhorst Modbus controller (slave 1)
   B_Side (2) — Reactant-Hot  Bronkhorst Modbus controller (slave 2)
*)
{attribute 'qualified_only'}
{attribute 'to_string'}
TYPE E_PressureController :
(
    A_Side := 1,
    B_Side := 2
) INT;
END_TYPE
