(* CONFIG_TIER: 1-MACHINE  -- Update all values for every new machine build. *)
(* Door subsystem sizing constants for the Shark system.
   DOOR_SENSOR_COUNT must match the number of proximity sensors wired
   to E_DigitalInputIdentifier door entries.
   DOOR_LOCK_COUNT must match the number of electromechanical locks
   wired to E_DigitalOutputIdentifier lock entries (FL, FR). *)
{attribute 'qualified_only'}
VAR_GLOBAL CONSTANT
    DOOR_SENSOR_COUNT : INT := 9;   // Proximity sensors total
    DOOR_LOCK_COUNT   : INT := 2;   // Electromechanical locks (FL, FR)
END_VAR
