(*
Digital Output Identifier Enumeration - Shark System

Merged from old E_DigitalOutputIdentifier and E_DIOIdentifier output items.
Organized by function: signal lights, camera lights, pumps, chiller, door locks,
heater voltage enable, and solenoid valve outputs consumed by PRG_VALVE.
*)
{attribute 'qualified_only'}
{attribute 'to_string'}
TYPE E_DigitalOutputIdentifier :
    (
        UNDEFINED               := 0,     // Undefined/Invalid Output Identifier

        // Vacuum Pumps
        PUMP_AB_EXH             := 1,     // Exhaust Vacuum Pump
        PUMP_CHK_VAC            := 2,     // Chuck Vacuum Pump

        // Signal Indicator Lights
        LGHT_SGN_GRN            := 3,     // Signal Light - Green
        LGHT_SGN_RED            := 4,     // Signal Light - Red
        LGHT_SGN_YEL            := 5,     // Signal Light - Yellow

        // Camera Lights
        LGHT_CMR_TOP            := 6,     // Top Camera Light
        LGHT_CMR_SIDE           := 7,     // Side Camera Light

        // Chiller
        CHIL_PWR                := 8,     // Chiller Power

        // Door Locks
        LOCK_DOOR_FL            := 9,     // Door Lock - Front Left
        LOCK_DOOR_FR            := 10,    // Door Lock - Front Right

        // Solenoid Valve Outputs (consumed by PRG_VALVE via injection)
        VALV_CHK_LZ1            := 11,    // Chuck Zone 1 Vacuum Valve
        VALV_CHK_LZ2            := 12,    // Chuck Zone 2 Vacuum Valve
        VALV_CHK_LZ3            := 13,    // Chuck Zone 3 Vacuum Valve
        VALV_CHK_VENT           := 14,    // Chuck Vent Valve
        VALV_CHK_WIN            := 15,    // Chuck Chiller Water In
        VALV_CHK_WOUT           := 16     // Chuck Chiller Water Out
    )INT;
END_TYPE
