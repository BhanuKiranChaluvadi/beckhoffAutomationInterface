(* 
Generic Heat Zone Physical Simulator
Pure function block that calculates a temperature based on heater commanded state, 
ambient loss, and physical rates.

Call Init() once with constant zone parameters, then call the body each cycle
with only the per-cycle bHeaterOn input.

Engineering Rules Used:
- Functional Preference (Pure calculation, isolated I/O)
- Simplicity First (Basic integration math, no complex thermal models)
*)
FUNCTION_BLOCK FB_SimulateHeaterZone
VAR_INPUT
    bHeaterOn : BOOL;  // Commanded state from %Q (per-cycle input)
END_VAR
VAR_OUTPUT
    rCurrentTemp : REAL;  // Calculated physical temperature
END_VAR
VAR
    _rAmbient     : REAL;  // Base ambient temperature (°C)
    _rHeatingRate : REAL;  // Degrees per cycle gained when ON
    _rCoolingRate : REAL;  // Degrees per cycle lost to ambient
    _rMaxTemp     : REAL;  // Maximum physical limit of the system
    _rCurrentTemp : REAL;  // Internal integrator memory
    _bInitialized : BOOL;
    _bFirstCycle  : BOOL;
END_VAR

    IF NOT _bInitialized THEN
        RETURN;
    END_IF

    // Initialization catch for first cycle
    IF NOT _bFirstCycle THEN
        IF _rAmbient > 0.0 THEN
            _rCurrentTemp := _rAmbient;
        ELSE
            _rCurrentTemp := 25.0; // Fail-safe default
        END_IF
        _bFirstCycle := TRUE;
    END_IF

    // Thermal integration logic
    IF bHeaterOn THEN
        _rCurrentTemp := _rCurrentTemp + _rHeatingRate;
    ELSIF _rCurrentTemp > _rAmbient THEN
        _rCurrentTemp := _rCurrentTemp - _rCoolingRate;
    END_IF

    // Physical Boundary Limits
    IF _rCurrentTemp > _rMaxTemp THEN
        _rCurrentTemp := _rMaxTemp;
    ELSIF _rCurrentTemp < _rAmbient THEN
        _rCurrentTemp := _rAmbient;
    END_IF

    // Output to caller
    rCurrentTemp := _rCurrentTemp;

(* Set constant zone parameters. Call once during initialization. *)
METHOD PUBLIC Init
VAR_INPUT
    rAmbient     : REAL;  // Base ambient temperature (e.g. 25.0 °C)
    rHeatingRate : REAL;  // Degrees per cycle gained when ON
    rCoolingRate : REAL;  // Degrees per cycle lost to ambient
    rMaxTemp     : REAL;  // Maximum physical limit of the system
END_VAR
    _rAmbient     := rAmbient;
    _rHeatingRate := rHeatingRate;
    _rCoolingRate := rCoolingRate;
    _rMaxTemp     := rMaxTemp;
    _bInitialized := TRUE;
END_METHOD

END_FUNCTION_BLOCK
