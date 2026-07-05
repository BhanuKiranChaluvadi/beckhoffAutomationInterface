(* Digital input control for hardware interface. *)
FUNCTION_BLOCK FB_DigitalInput IMPLEMENTS I_DigitalInput
VAR
    _sInstanceName : STRING(80);
    _Input         : REFERENCE TO BOOL;
    _bInverted     : BOOL := FALSE;
    _bInitialized  : BOOL := FALSE;
END_VAR

METHOD PUBLIC Init : BOOL
VAR_INPUT
    sInstanceName : STRING(80);
    ipInputRef    : REFERENCE TO BOOL;
    bInverted     : BOOL := FALSE;
END_VAR
    IF NOT __ISVALIDREF(ipInputRef) THEN
        Init := FALSE;
        RETURN;
    END_IF

    _sInstanceName := sInstanceName;
    _Input REF= ipInputRef;
    _bInverted := bInverted;
    _bInitialized := TRUE;

    Init := TRUE;
END_METHOD

METHOD PUBLIC GetState : BOOL
    IF NOT _bInitialized OR NOT __ISVALIDREF(_Input) THEN
        GetState := FALSE;
        RETURN;
    END_IF

    GetState := _Input XOR _bInverted;
END_METHOD

METHOD PUBLIC GetInstanceName : STRING(80)
    GetInstanceName := _sInstanceName;
END_METHOD

END_FUNCTION_BLOCK
