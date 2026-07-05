(* CONFIG_TIER: 1-MACHINE  -- Update all values for every new machine build. *)
(*
Global system state — Shark system.
From old GVL_System.st: system-wide flags and identity.
*)
{attribute 'qualified_only'}
VAR_GLOBAL
    bSystemInitialized      : BOOL := FALSE;
    bSystemHealthy          : BOOL := FALSE;
    bGlobalEmergencyStop    : BOOL := FALSE;
    bGlobalSafetyOK         : BOOL := FALSE;
    sSystemName             : STRING(80) := 'PLC_SHARK_PROJ';
    sSystemVersion          : STRING(20) := '1.2.0';
END_VAR
