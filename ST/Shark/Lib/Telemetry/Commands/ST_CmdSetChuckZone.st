(*
ST_CmdSetChuckZone — Hold or release the sample on the chuck via vacuum control.
*)
{attribute 'pack_mode' := '0'}
TYPE ST_CmdSetChuckZone EXTENDS ST_CmdHeader :
STRUCT
    holdSample         : BOOL;            // TRUE = hold sample, FALSE = release sample
    size               : INT;             // Wafer size [mm]: 150, 200, or >200
    fPressureThreshold : REAL := 0.65;    // Pressure threshold [bar]
    tPressureTimeout   : DINT := 60000;   // Vacuum timeout [ms] (1 minute)
END_STRUCT
END_TYPE
