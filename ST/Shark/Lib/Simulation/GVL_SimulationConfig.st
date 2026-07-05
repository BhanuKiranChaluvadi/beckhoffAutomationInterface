(* Simulation physics constants shared by all machines *)
{attribute 'qualified_only'}
VAR_GLOBAL CONSTANT
    // Temperature simulation
    AMBIENT_TEMP          : REAL := 25.0;   // Room temperature (°C)
    HEATING_RATE          : REAL := 0.01;   // °C per cycle when heater ON  (~1°C/s at 10ms task)
    COOLING_RATE          : REAL := 0.002;  // °C per cycle passive cooling (~0.2°C/s at 10ms task)

    // Pressure simulation
    ATMOSPHERIC_PRESSURE  : REAL := 1.0;    // Atmospheric pressure (bar)
    CHUCK_VACUUM_PRESSURE : REAL := 0.01;   // Chuck vacuum achieved pressure (bar)

    // MFC simulation
    MFC_RAMP_RATE         : REAL := 0.5;    // sccm per cycle ramp rate
END_VAR
