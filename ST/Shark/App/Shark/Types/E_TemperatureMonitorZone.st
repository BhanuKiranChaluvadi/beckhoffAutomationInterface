(* Monitoring-only temperature point identifier.
   These points have a single sensor channel and no heater or PID controller.
   Indexed into FB_TemperatureSubsystem._ipMonitorSensors[1..MONITOR_ZONE_COUNT]. *)
{attribute 'qualified_only'}
TYPE E_TemperatureMonitorZone :
(
    SENS_CHAMBER := 1,  // Chamber temperature — TH sensor temperature channel
    SENS_STAGE_X := 2,  // Stage X-axis temperature
    SENS_STAGE_Y := 3,  // Stage Y-axis temperature
    SENS_A_SIDE  := 4,  // A-side temperature (single sensor, no heater)
    SENS_B_SIDE  := 5   // B-side temperature (single sensor, no heater)
);
END_TYPE
