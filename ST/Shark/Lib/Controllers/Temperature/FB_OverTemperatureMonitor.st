(*
FB_OverTemperatureMonitor — independent over-temperature interlock.

Wraps a safety thermocouple (I_Sensor) that is measured independently of the
control RTD, providing defence-in-depth over-temperature protection for one
heat channel. The sensor lifecycle (Execute) is owned by the wiring program;
this monitor only reads it.

Behaviour (mirrors FB_HeaterSafetyWatchdog latch/reset semantics):
  * Trip LATCHES when the TC exceeds rTripLimit.
  * A TC sensor fault is treated as a trip (fail-safe) when
    bTreatSensorFaultAsTrip is TRUE.
  * The trip raises ERR_CTRL_LIMIT_EXCEEDED (logged once via FB_ErrorHandler).
  * Reset() is gated: only allowed once the TC has fallen below
    (rTripLimit - rResetHysteresis) and is fault-free.

The monitor never drives hardware directly — the owning controller reads
IsTripActive() and forces its heater OFF. This keeps the monitor a pure,
side-effect-free safety observer (SRP).
 *)
FUNCTION_BLOCK FB_OverTemperatureMonitor
VAR
    _ipSafetyTc: I_Sensor;
    _rTripLimit: LREAL := 0.0;
    _rResetHysteresis: LREAL := 5.0;
    _bTreatSensorFaultAsTrip: BOOL := TRUE;

    _bTripLatched: BOOL := FALSE;
    _rTemperature: LREAL := 0.0;
    _bSensorFault: BOOL := FALSE;
    _bCanReset: BOOL := FALSE;
    _sBlockReason: STRING(80) := '';
    _fbError: FB_ErrorHandler;
    _bInitialized: BOOL := FALSE;
END_VAR

METHOD PUBLIC Init: BOOL
VAR_INPUT
    sInstanceName: STRING(80);
    ipSafetyTc: I_Sensor; // Independent safety thermocouple
    rTripLimit: LREAL; // Over-temperature trip threshold (°C)
    rResetHysteresis: LREAL := 5.0; // Must cool this far below limit to reset (°C)
    bTreatSensorFaultAsTrip: BOOL := TRUE; // Fail-safe on TC fault
END_VAR
    _fbError.Init(sInstanceName);

    IF ipSafetyTc = 0 THEN
        _fbError.Raise(E_ErrorId.ERR_GEN_NULL_POINTER, 'Safety TC sensor reference is null');
        RETURN;
    END_IF

    IF rTripLimit <= 0.0 THEN
        _fbError.Raise(E_ErrorId.ERR_GEN_INVALID_PARAM, 'Trip limit must be positive');
        RETURN;
    END_IF

    _ipSafetyTc := ipSafetyTc;
    _rTripLimit := rTripLimit;
    _rResetHysteresis := rResetHysteresis;
    _bTreatSensorFaultAsTrip := bTreatSensorFaultAsTrip;

    _bInitialized := TRUE;
    _fbError.Clear();
    Init := TRUE;
END_METHOD

METHOD PUBLIC Execute: BOOL
    IF NOT _bInitialized THEN
        RETURN;
    END_IF

    // 1. Read the independent safety sensor (lifecycle owned by wiring program)
    _rTemperature := _ipSafetyTc.GetMeasurement();
    _bSensorFault := _ipSafetyTc.IsError();

    // 2. Over-temperature trip (latching)
    IF NOT _bTripLatched THEN
        IF _rTemperature > _rTripLimit THEN
            _bTripLatched := TRUE;
            _fbError.RaiseWithValues(
                E_ErrorId.ERR_CTRL_LIMIT_EXCEEDED,
                'SafetyTC',
                _rTemperature,
                _rTripLimit,
                'C');
        ELSIF _bSensorFault AND _bTreatSensorFaultAsTrip THEN
            // Fail-safe: loss of the safety sensor is itself a trip
            _bTripLatched := TRUE;
            _fbError.RaiseFromChild(_ipSafetyTc.GetError());
        END_IF
    END_IF

    // 3. Determine whether reset is permitted (cooled below limit - hysteresis, fault-free)
    _bCanReset := _bTripLatched
        AND NOT _bSensorFault
        AND (_rTemperature < (_rTripLimit - _rResetHysteresis));

    // 4. Update block reason (operator feedback)
    IF NOT _bTripLatched THEN
        _sBlockReason := '';
    ELSIF _bSensorFault THEN
        _sBlockReason := 'Safety TC fault';
    ELSIF _rTemperature >= (_rTripLimit - _rResetHysteresis) THEN
        _sBlockReason := 'Temperature still above reset threshold';
    ELSE
        _sBlockReason := '';
    END_IF

    Execute := NOT _bTripLatched;
END_METHOD

METHOD PUBLIC Reset: BOOL
    IF NOT _bTripLatched THEN
        Reset := TRUE; // Already clear
        RETURN;
    END_IF

    IF _bCanReset THEN
        _bTripLatched := FALSE;
        _sBlockReason := '';
        _fbError.Clear();
        Reset := TRUE;
    ELSE
        Reset := FALSE;
    END_IF
END_METHOD

METHOD PUBLIC IsTripActive: BOOL
    IsTripActive := _bTripLatched;
END_METHOD

METHOD PUBLIC CanReset: BOOL
    CanReset := _bCanReset;
END_METHOD

METHOD PUBLIC GetBlockReason: STRING(80)
    GetBlockReason := _sBlockReason;
END_METHOD

METHOD PUBLIC GetError: ST_Error
    GetError := _fbError.GetError();
END_METHOD

METHOD PUBLIC GetTemperature: LREAL
    GetTemperature := _rTemperature;
END_METHOD

METHOD PUBLIC IsInitialized: BOOL
    IsInitialized := _bInitialized;
END_METHOD

END_FUNCTION_BLOCK
