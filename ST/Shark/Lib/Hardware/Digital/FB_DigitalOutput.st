(* Digital output control for hardware interface. *)
FUNCTION_BLOCK FB_DigitalOutput IMPLEMENTS I_DigitalOutput
VAR
    _sInstanceName : STRING(80);
    _Output        : REFERENCE TO BOOL;
    _bSafeState    : BOOL := FALSE;   // Applied by SetSafeState() — configured at Init time
    _bInitialized  : BOOL := FALSE;
END_VAR

METHOD PUBLIC Init : BOOL
VAR_INPUT
    sInstanceName : STRING(80);
    ipOutputRef   : REFERENCE TO BOOL;
    bSafeState    : BOOL := FALSE;   // Output state applied when SetSafeState() is called (e.g. on sequence abort)
END_VAR
    IF NOT __ISVALIDREF(ipOutputRef) THEN
        Init := FALSE;
        RETURN;
    END_IF

    _sInstanceName := sInstanceName;
    _Output REF= ipOutputRef;
    _bSafeState := bSafeState;
    _bInitialized := TRUE;
    Init := TRUE;
END_METHOD

METHOD PUBLIC SetOutput
VAR_INPUT
    bValue : BOOL;
END_VAR
    IF _bInitialized AND_THEN __ISVALIDREF(_Output) THEN
        _Output := bValue;
    END_IF
END_METHOD

METHOD PUBLIC GetOutput : BOOL
    IF _bInitialized AND_THEN __ISVALIDREF(_Output) THEN
        GetOutput := _Output;
    ELSE
        GetOutput := FALSE;
    END_IF
END_METHOD

METHOD PUBLIC SetSafeState
    IF _bInitialized AND_THEN __ISVALIDREF(_Output) THEN
        _Output := _bSafeState;
    END_IF
END_METHOD

METHOD PUBLIC GetInstanceName : STRING(80)
    GetInstanceName := _sInstanceName;
END_METHOD
END_FUNCTION_BLOCK
