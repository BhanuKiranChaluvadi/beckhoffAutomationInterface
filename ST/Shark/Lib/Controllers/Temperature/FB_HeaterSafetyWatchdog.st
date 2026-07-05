(* Heater safety watchdog for runaway heating protection. Monitors continuous heater ON time and manual emergency trip. *)
FUNCTION_BLOCK FB_HeaterSafetyWatchdog
VAR_INPUT
    bHeaterState         : BOOL;                // Current heater output state
    bManualTrip          : BOOL := FALSE;       // Emergency stop button
END_VAR
VAR_OUTPUT
    bTripActive          : BOOL;                // TRUE = force heater OFF
END_VAR
VAR
    _tMaxContinuousOn    : TIME := T#5M;
    _tMinOffTime         : TIME := T#5S;

    _tonContinuousOn     : TON;
    _tonOffTime          : TON;

    _bTripLatched        : BOOL := FALSE;
    _fbError             : FB_ErrorHandler;
    _bCanReset           : BOOL := FALSE;
    _sBlockReason        : STRING(80) := '';
    _bInitialized        : BOOL := FALSE;
END_VAR

METHOD PUBLIC Init : BOOL
VAR_INPUT
    sInstanceName    : STRING(80);
    tMaxContinuousOn : TIME := T#5M;
    tMinOffTime      : TIME := T#5S;
END_VAR
    _fbError.Init(sInstanceName);
    _tMaxContinuousOn := tMaxContinuousOn;
    _tMinOffTime := tMinOffTime;
    _bInitialized := TRUE;
    Init := TRUE;
END_METHOD

METHOD PUBLIC Execute : BOOL
VAR
    rMaxOnSeconds : LREAL;
    rOnSeconds    : LREAL;
END_VAR
    IF NOT _bInitialized THEN
        RETURN;
    END_IF

    // 1. Monitor CONTINUOUS ON time (resets when heater cycles OFF)
    _tonContinuousOn(IN := bHeaterState AND NOT _bTripLatched, PT := _tMaxContinuousOn);

    // Trip if heater stuck ON continuously
    IF _tonContinuousOn.Q AND NOT _bTripLatched THEN
        _bTripLatched := TRUE;
        rMaxOnSeconds := TIME_TO_LREAL(_tMaxContinuousOn) / 1000.0;
        rOnSeconds := TIME_TO_LREAL(_tonContinuousOn.ET) / 1000.0;
        _fbError.RaiseWithValues(E_ErrorId.ERR_CTRL_WATCHDOG_HEATING, 'OnTime', rOnSeconds, rMaxOnSeconds, 's');
    END_IF

    // 2. Manual emergency trip
    IF bManualTrip AND NOT _bTripLatched THEN
        _bTripLatched := TRUE;
        _fbError.Raise(E_ErrorId.ERR_CTRL_LIMIT_EXCEEDED, 'Manual emergency trip activated');
    END_IF

    // 3. Track time since trip (for minimum OFF time enforcement)
    _tonOffTime(IN := _bTripLatched, PT := _tMinOffTime);

    // 4. Determine if reset is allowed
    _bCanReset := NOT bHeaterState           // Heater must be OFF
    AND _tonOffTime.Q              // Minimum OFF time elapsed
    AND _bTripLatched;             // Only when tripped

    // 5. Update block reason
    IF NOT _bTripLatched THEN
        _sBlockReason := '';
    ELSIF bHeaterState THEN
        _sBlockReason := 'Heater still ON';
    ELSIF NOT _tonOffTime.Q THEN
        _sBlockReason := 'Minimum OFF time not elapsed';
    ELSE
        _sBlockReason := '';
    END_IF

    // 6. Update outputs
    bTripActive := _bTripLatched;
    Execute := NOT bTripActive;

END_METHOD

METHOD PUBLIC Reset : BOOL
    IF NOT _bTripLatched THEN
        Reset := TRUE;  // Already clear
        RETURN;
    END_IF

    IF _bCanReset THEN
        _bTripLatched := FALSE;
        _fbError.Clear();
        _tonContinuousOn(IN := FALSE);  // Reset timer
        _sBlockReason := '';
        Reset := TRUE;
    ELSE
        Reset := FALSE;
    END_IF
END_METHOD

METHOD PUBLIC GetError : ST_Error
    GetError := _fbError.GetError();
END_METHOD

METHOD PUBLIC CanReset : BOOL
    CanReset := _bCanReset;
END_METHOD

METHOD PUBLIC GetBlockReason : STRING(80)
    GetBlockReason := _sBlockReason;
END_METHOD

METHOD PUBLIC IsTripActive : BOOL
    IsTripActive := bTripActive;
END_METHOD

END_FUNCTION_BLOCK
