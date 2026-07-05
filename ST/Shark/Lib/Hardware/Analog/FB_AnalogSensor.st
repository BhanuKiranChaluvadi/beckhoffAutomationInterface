(* Generic analog sensor — reads raw analog input and scales to engineering units.
   Works with any Beckhoff EL3xxx terminal: RTD (EL32xx), current (EL3048/EL3174), voltage (EL31xx).
   Linear mapping: [rawMin, rawMax] → [scaledMin, scaledMax].
   Values outside [scaledMin, scaledMax] are flagged as range errors. *)
FUNCTION_BLOCK FB_AnalogSensor IMPLEMENTS I_Sensor
VAR
    _ipRawInput    : I_RawAnalogInput;
    _rScaledMin    : LREAL;
    _rScaledMax    : LREAL;
    _rScaleFactor  : LREAL;     // Pre-computed: (scaledMax - scaledMin) / (rawMax - rawMin)
    _rOffset       : LREAL;     // Pre-computed: scaledMin - rawMin * scaleFactor
    _bInitialized  : BOOL := FALSE;
    _rCurrentValue : LREAL := 0.0;
    _fbError       : FB_ErrorHandler;
    _fbHwErrTimer  : TON;    // Debounce: fault must persist continuously for HW_ERROR_DEBOUNCE before error is raised
    _rTrigHwErr    : R_TRIG; // Detects rising edge of timer Q — fires once per 5 s period while fault persists
END_VAR

VAR CONSTANT
    HW_ERROR_DEBOUNCE : TIME := T#5S;  // Transient faults shorter than this are silently swallowed
END_VAR

METHOD PUBLIC Init : BOOL
VAR_INPUT
    sInstanceName : STRING(80);
    ipRawInput    : I_RawAnalogInput;
    rRawMin       : LREAL := 0.0;
    rRawMax       : LREAL;
    rScaledMin    : LREAL := 0.0;
    rScaledMax    : LREAL;
END_VAR
    _fbError.Init(sInstanceName);

    IF ipRawInput = 0 THEN
        _fbError.Raise(E_ErrorId.ERR_GEN_NULL_POINTER, 'Null raw input interface');
        RETURN;
    END_IF

    IF rRawMin >= rRawMax THEN
        _fbError.Raise(E_ErrorId.ERR_GEN_INVALID_PARAM, 'Invalid raw range: min >= max');
        RETURN;
    END_IF

    IF rScaledMin >= rScaledMax THEN
        _fbError.Raise(E_ErrorId.ERR_GEN_INVALID_PARAM, 'Invalid scaled range: min >= max');
        RETURN;
    END_IF

    _ipRawInput := ipRawInput;
    _rScaledMin := rScaledMin;
    _rScaledMax := rScaledMax;

    // Pre-compute linear scaling (avoids per-cycle division)
    _rScaleFactor := (rScaledMax - rScaledMin) / (rRawMax - rRawMin);
    _rOffset := rScaledMin - rRawMin * _rScaleFactor;

    _bInitialized := TRUE;
    _fbError.Clear();
    Init := TRUE;
END_METHOD

METHOD PUBLIC Execute : BOOL
VAR
    nRaw        : DINT;
    bErr        : BOOL;
    bUnderrange : BOOL;
    bOverrange  : BOOL;
END_VAR
    IF NOT _bInitialized THEN
        _fbError.Raise(E_ErrorId.ERR_GEN_INITIALIZATION, 'Sensor not initialized');
        RETURN;
    END_IF

    IF _ipRawInput = 0 THEN
        _fbError.Raise(E_ErrorId.ERR_GEN_NULL_POINTER, 'Null raw input interface');
        RETURN;
    END_IF

    _ipRawInput.GetChannelState(nRawValue => nRaw, bError => bErr, bUnderrange => bUnderrange, bOverrange => bOverrange);

    // Always compute the scaled value — even during errors this reflects the actual hardware reading.
    // For overrange (raw=32767) the result lands slightly above rScaledMax, clearly indicating fault.
    // Consumers must call IsError() before trusting the measurement.
    _rCurrentValue := _rOffset + DINT_TO_LREAL(nRaw) * _rScaleFactor;

    // Debounce transient hardware faults: fault must be continuous for HW_ERROR_DEBOUNCE (5 s).
    // After 5 s, _RaiseHwError() is called once. If the fault persists, the timer is immediately
    // reset so it counts another 5 s — giving a periodic log entry every 5 s until the fault clears.
    // R_TRIG detects the Q rising edge so Raise() fires exactly once per 5 s window.
    _fbHwErrTimer(IN := bErr, PT := HW_ERROR_DEBOUNCE);
    _rTrigHwErr(CLK := _fbHwErrTimer.Q);

    IF bErr THEN
        IF _rTrigHwErr.Q THEN
            _RaiseHwError(nRaw, bUnderrange, bOverrange);
            _fbHwErrTimer(IN := FALSE);  // Reset timer: next period starts from 0 on the following cycle
        END_IF
        RETURN;
    END_IF

    IF _fbError.IsActive() THEN
        _fbError.Clear();
    END_IF
    Execute := TRUE;
END_METHOD

// Builds the hardware-error message and raises ERR_HW_SENSOR_OPEN exactly once per fault event.
// Called only on the rising edge of the debounce timer — not on every scan cycle.
METHOD PRIVATE _RaiseHwError
VAR_INPUT
    nRaw        : DINT;
    bUnderrange : BOOL;
    bOverrange  : BOOL;
END_VAR
VAR
    sMsg : STRING(255);
END_VAR
    IF bOverrange THEN
        sMsg := CONCAT('Hardware error - Overrange: raw=', TO_STRING(nRaw));
    ELSIF bUnderrange THEN
        sMsg := CONCAT('Hardware error - Underrange: raw=', TO_STRING(nRaw));
    ELSE
        sMsg := 'Hardware error';
    END_IF
    _fbError.Clear();  // Reset dedup guard so Raise() logs every periodic re-raise, not just the first
    _fbError.Raise(E_ErrorId.ERR_HW_SENSOR_OPEN, sMsg);
END_METHOD

// -----------------------------------------------------------------------------
// I_Sensor
// -----------------------------------------------------------------------------
METHOD PUBLIC GetMeasurement : LREAL
    GetMeasurement := _rCurrentValue;
END_METHOD

// -----------------------------------------------------------------------------
// I_Errorable
// -----------------------------------------------------------------------------
METHOD PUBLIC IsError : BOOL
    IsError := _fbError.IsActive();
END_METHOD

METHOD PUBLIC GetError : ST_Error
    GetError := _fbError.GetError();
END_METHOD

END_FUNCTION_BLOCK
