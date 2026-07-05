(* Compile-time bounds for FB_HeatZone arrays.
   MAX_CHANNELS sizes the per-zone channel arrays (the zone's _aipChannels).
   A "channel" is one RTD + one SSR + one PID controller pair.
   Pick the smallest bound that covers the largest physical heat zone on any
   supported machine. PRO DALP currently has 7 channels; 8 leaves headroom. *)
{attribute 'qualified_only'}
VAR_GLOBAL CONSTANT
    MAX_CHANNELS : INT := 8;   // Maximum channels per heat zone
END_VAR
