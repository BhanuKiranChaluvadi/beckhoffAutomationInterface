(* Aggregation policy for FB_HeatZone â€” how multiple channel temperatures
   collapse into the zone's single "headline" reading (rAggregated).

   Example for a 3-channel zone reading 78Â°C, 80Â°C, 82Â°C:
     AVERAGE  -> 80.0   (typical default; smooths sensor noise)
     MAXIMUM  -> 82.0   (conservative; useful for safety / over-temp display)
     MINIMUM  -> 78.0   (useful for "are we ready to start the recipe?")
     MASTER   -> reading of channel[1] only (legacy single-sensor behavior) *)
{attribute 'qualified_only'}
TYPE E_TempAggregation :
(
    AVERAGE  := 0,
    MAXIMUM  := 1,
    MINIMUM  := 2,
    MASTER   := 3
);
END_TYPE
