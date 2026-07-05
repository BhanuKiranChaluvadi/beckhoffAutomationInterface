(*
Universal Valve Implementation

Handles all valve types (Pneumatic, Solenoid, 3-Way) via I_DigitalOutput.
- Simple valves: Init with just an output.
- Feedback valves: Init with output + InitFeedback with I_DigitalInput.
*)
FUNCTION_BLOCK FB_Valve IMPLEMENTS I_Valve
VAR
    _sInstanceName    : STRING(80);
    _ipOutput         : I_DigitalOutput;
    _ipFeedback       : I_DigitalInput;
    _tFeedbackTimeout : TIME := T#1S;
    _fbTimeoutTimer   : TON;
    _bCommandedState  : BOOL;
    _bHasFeedback     : BOOL := FALSE;
    _bInitialized     : BOOL := FALSE;
    _fbError          : FB_ErrorHandler;
END_VAR

METHOD PUBLIC Init : BOOL
VAR_INPUT
    sInstanceName : STRING(80);
    ipOutput      : I_DigitalOutput;
END_VAR
    _sInstanceName := sInstanceName;
    _fbError.Init(sInstanceName);

    IF ipOutput = 0 THEN
        _fbError.Raise(E_ErrorId.ERR_VALVE_NOT_CONFIGURED, 'Invalid output interface');
        _bInitialized := FALSE;
        Init := FALSE;
        RETURN;
    END_IF

    _ipOutput := ipOutput;
    _bHasFeedback := FALSE;
    _bInitialized := TRUE;
    Init := TRUE;
END_METHOD

METHOD PUBLIC InitFeedback : BOOL
VAR_INPUT
    ipFeedback : I_DigitalInput;
    tTimeout   : TIME;
END_VAR
    IF NOT _bInitialized THEN
        InitFeedback := FALSE;
        RETURN;
    END_IF

    IF ipFeedback = 0 THEN
        _bHasFeedback := FALSE;
        InitFeedback := FALSE;
        RETURN;
    END_IF

    _ipFeedback := ipFeedback;
    _tFeedbackTimeout := tTimeout;
    _bHasFeedback := TRUE;
    InitFeedback := TRUE;
END_METHOD

METHOD PUBLIC Execute
VAR
    bCurrentState : BOOL;
END_VAR
    IF NOT _bInitialized THEN RETURN;
    END_IF

    // Feedback monitoring (only if configured)
    IF _bHasFeedback THEN
        bCurrentState := _ipFeedback.GetState();

        IF bCurrentState = _bCommandedState THEN
            _fbTimeoutTimer(IN := FALSE);
            IF _fbError.GetErrorId() = E_ErrorId.ERR_VALVE_FEEDBACK_TIMEOUT THEN
                _fbError.Clear();
            END_IF
        ELSE
            _fbTimeoutTimer(IN := TRUE, PT := _tFeedbackTimeout);
            IF _fbTimeoutTimer.Q THEN
                _fbError.Raise(E_ErrorId.ERR_VALVE_FEEDBACK_TIMEOUT, 'Feedback timeout');
            END_IF
        END_IF
    END_IF
END_METHOD

METHOD PUBLIC Open : BOOL
    IF NOT _bInitialized THEN
        _fbError.Raise(E_ErrorId.ERR_VALVE_NOT_CONFIGURED, 'Valve not initialized');
        Open := FALSE;
        RETURN;
    END_IF

    _bCommandedState := TRUE;
    _fbTimeoutTimer(IN := FALSE);
    _ipOutput.SetOutput(TRUE);
    Open := TRUE;
END_METHOD

METHOD PUBLIC Close : BOOL
    IF NOT _bInitialized THEN
        _fbError.Raise(E_ErrorId.ERR_VALVE_NOT_CONFIGURED, 'Valve not initialized');
        Close := FALSE;
        RETURN;
    END_IF

    _bCommandedState := FALSE;
    _fbTimeoutTimer(IN := FALSE);
    _ipOutput.SetOutput(FALSE);
    Close := TRUE;
END_METHOD

METHOD PUBLIC IsOpen : BOOL
    IF NOT _bInitialized THEN
        IsOpen := FALSE;
        RETURN;
    END_IF

    IF _bHasFeedback THEN
        IsOpen := _ipFeedback.GetState();
    ELSE
        IsOpen := _ipOutput.GetOutput();
    END_IF
END_METHOD

METHOD PUBLIC IsClosed : BOOL
    IsClosed := NOT IsOpen();
END_METHOD

METHOD PUBLIC IsError : BOOL
    IsError := _fbError.IsActive();
END_METHOD

METHOD PUBLIC GetError : ST_Error
    GetError := _fbError.GetError();
END_METHOD

METHOD PUBLIC Reset : BOOL
VAR
    bCurrentState : BOOL;
END_VAR
    // With no feedback, a reset is always accepted — the operator must verify
    // externally that the fault is cleared.
    IF NOT _bHasFeedback THEN
        _fbError.Clear();
        _fbTimeoutTimer(IN := FALSE);
        Reset := TRUE;
        RETURN;
    END_IF

    // With feedback, refuse to clear a FEEDBACK_TIMEOUT while the physical
    // state still disagrees with the commanded state — otherwise the operator
    // "resets" a genuinely stuck valve and the error silently re-arms on the
    // next cycle.
    IF _fbError.GetErrorId() = E_ErrorId.ERR_VALVE_FEEDBACK_TIMEOUT THEN
        bCurrentState := _ipFeedback.GetState();
        IF bCurrentState <> _bCommandedState THEN
            Reset := FALSE;
            RETURN;
        END_IF
    END_IF

    _fbError.Clear();
    _fbTimeoutTimer(IN := FALSE);
    Reset := TRUE;
END_METHOD

METHOD PUBLIC GetInstanceName : STRING(80)
    GetInstanceName := _sInstanceName;
END_METHOD

END_FUNCTION_BLOCK
