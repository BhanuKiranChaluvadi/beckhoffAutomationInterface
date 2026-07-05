(* CONFIG_TIER: 1-MACHINE  -- Update all values for every new machine build. *)
(* Array bound constants for the Shark temperature subsystem.
   Heat zones:        1-13 (E_HeatZone)
   Monitor points:      1-4  (E_TemperatureMonitorZone)
   Per-zone channels: 1..GVL_HeatZone.MAX_CHANNELS (Lib constant)
   Total channels (sum of nChannelCount across zones):
     HEAD (shared)      = 1
     A-side singles     = 5 * 1 = 5
     B-side singles     = 5 * 1 = 5
     EXH (2) + CHK (2)  = 4
     Total              = 15 *)
{attribute 'qualified_only'}
VAR_GLOBAL CONSTANT
    CONTROLLABLE_ZONE_MAX : INT := 13;  // Zone count (matches highest E_HeatZone value)
    CHANNEL_MAX           : INT := 15;  // Sum of nChannelCount across all zones
    MONITOR_ZONE_COUNT    : INT := 5;   // Monitor points 1-5 (E_TemperatureMonitorZone); 1=SENS_CHAMBER, 2=SENS_STAGE_X, 3=SENS_STAGE_Y, 4=SENS_A_SIDE, 5=SENS_B_SIDE; all wired on Shark
END_VAR
