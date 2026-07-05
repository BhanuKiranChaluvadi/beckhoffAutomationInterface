(*
Pressure Sensor Identifier Enumeration - Shark System

Preserved from old E_PressureSensorIdentifier.
4 analog pressure sensors (read-only).
For Bronkhorst pressure controllers, see E_PressureController.
*)
{attribute 'qualified_only'}
{attribute 'to_string'}
TYPE E_PressureSensorIdentifier :
    (
        PSNS_A_DST             := 1,     // A-side Distributor Pressure Sensor [0-3 bar]
        PSNS_B_DST             := 2,     // B-side Distributor Pressure Sensor [0-3 bar]
        PSNS_AB_EXH            := 3,     // Exhaust Vacuum Pressure Sensor [0-1 bar]
        PSNS_CHK_VAC           := 4      // Chuck Vacuum Pressure Sensor [inverted: see NOTE in PRG]
    )INT;
END_TYPE
