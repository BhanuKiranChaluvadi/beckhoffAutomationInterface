(* Dual sensor composite — averages two I_Sensor readings with deviation monitoring. *)
FUNCTION_BLOCK FB_DualSensor IMPLEMENTS I_Sensor

VAR
    // Sensor references
    _ipPrimarySensor    : I_Sensor;
    _ipSecondarySensor  : I_Sensor;

    // Configuration
    _rMaxDeviation      : LREAL := 5.0;          // Max allowed sensor difference

    // State
    _bInitialized       : BOOL := FALSE;
    _bUseDualSensors    : BOOL := FALSE;
    _rCurrentValue      : LREAL := 0.0;          // Current averaged value
    _rDeviation         : LREAL := 0.0;          // Sensor deviation (dual mode only)
    _fbError            : FB_ErrorHandler;       // Centralized error handling
END_VAR

METHOD PUBLIC Init : BOOL
VAR_INPUT
    sInstanceName       : STRING(80);            // Instance name for error source
    ipPrimary           : I_Sensor;              // Primary sensor (required)
    ipSecondary         : I_Sensor;              // Secondary sensor (optional, 0 = single mode)
    rMaxDeviation       : LREAL := 5.0;          // Max sensor deviation
END_VAR
    _fbError.Init(sInstanceName);

    IF ipPrimary = 0 THEN
        _fbError.Raise(E_ErrorId.ERR_GEN_NULL_POINTER, 'Primary sensor reference is null');
        RETURN;
    END_IF

    _ipPrimarySensor := ipPrimary;
    _ipSecondarySensor := ipSecondary;
    _rMaxDeviation := rMaxDeviation;

    _bUseDualSensors := (ipSecondary <> 0);
    _bInitialized := TRUE;
    _fbError.Clear();

    Init := TRUE;
END_METHOD

METHOD PUBLIC Execute : BOOL
VAR
    rPrimary      : LREAL;
    rSecondary    : LREAL;
END_VAR

    Execute := FALSE;

    // 1. Initialization Check
    IF NOT _bInitialized THEN
        _fbError.Raise(E_ErrorId.ERR_GEN_INITIALIZATION, 'Dual sensor not initialized');
        RETURN;
    END_IF

    // 2. Primary Sensor Check - Preserve child error details
    IF _ipPrimarySensor.IsError() THEN
        _fbError.RaiseFromChild(_ipPrimarySensor.GetError());
        RETURN;
    END_IF
    rPrimary := _ipPrimarySensor.GetMeasurement();

    // 3. Single Sensor Mode (Early Exit)
    IF NOT _bUseDualSensors THEN
        _rCurrentValue := rPrimary;
        _rDeviation := 0.0;
        IF _fbError.IsActive() THEN
            _fbError.Clear();
        END_IF
        Execute := TRUE;
        RETURN;
    END_IF

    // 4. Secondary Sensor Check - Preserve child error details
    IF _ipSecondarySensor.IsError() THEN
        _fbError.RaiseFromChild(_ipSecondarySensor.GetError());
        RETURN;
    END_IF
    rSecondary := _ipSecondarySensor.GetMeasurement();

    // 5. Calculate average regardless of deviation check
    _rCurrentValue := (rPrimary + rSecondary) / 2.0;

    // 6. Deviation Check - include actual values in message
    _rDeviation := ABS(rPrimary - rSecondary);

    IF _rDeviation > _rMaxDeviation THEN
        _fbError.RaiseWithValues(E_ErrorId.ERR_CTRL_DEVIATION_HIGH, 'Deviation', _rDeviation, _rMaxDeviation, '');
        RETURN;
    END_IF

    // 7. Success
    IF _fbError.IsActive() THEN
        _fbError.Clear();
    END_IF
    Execute := TRUE;
END_METHOD

METHOD PUBLIC GetDeviation : LREAL
    GetDeviation := _rDeviation;
END_METHOD

METHOD PUBLIC IsDualSensor : BOOL
    IsDualSensor := _bUseDualSensors;
END_METHOD

METHOD PUBLIC GetMeasurement : LREAL
    GetMeasurement := _rCurrentValue;
END_METHOD

METHOD PUBLIC IsError : BOOL
    IsError := _fbError.IsActive();
END_METHOD

METHOD PUBLIC GetError : ST_Error
    GetError := _fbError.GetError();
END_METHOD

END_FUNCTION_BLOCK
