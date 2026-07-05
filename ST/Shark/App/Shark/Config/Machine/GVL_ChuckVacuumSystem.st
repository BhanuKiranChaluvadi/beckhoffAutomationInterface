(* CONFIG_TIER: 1-MACHINE  -- Update all values for every new machine build. *)
(* Chuck vacuum zone size thresholds for the Shark system.
   Maps wafer diameter [mm] to vacuum zone selection:
     diameter <= CHUCK_ZONE_SMALL_MAX  -> zone 1 (small)
     diameter <= CHUCK_ZONE_MEDIUM_MAX -> zone 2 (medium)
     diameter >  CHUCK_ZONE_MEDIUM_MAX -> zone 3 (large) *)
{attribute 'qualified_only'}
VAR_GLOBAL CONSTANT
    CHUCK_ZONE_SMALL_MAX  : INT := 150;   // [mm] upper bound for small-wafer zone
    CHUCK_ZONE_MEDIUM_MAX : INT := 200;   // [mm] upper bound for medium-wafer zone
END_VAR
