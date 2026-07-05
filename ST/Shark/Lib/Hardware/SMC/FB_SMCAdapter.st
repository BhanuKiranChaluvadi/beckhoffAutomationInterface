(* Adapter to treat a bit in an SMC Output Block as a standard I_DigitalOutput. *)
FUNCTION_BLOCK FB_SMCAdapter IMPLEMENTS I_DigitalOutput
VAR
    _refBlock     : REFERENCE TO FB_SMCOutputBlock;
    _nIndex       : USINT;
    _bSafeState   : BOOL := FALSE;   // Applied by SetSafeState() — configured at Init time
    _bInitialized : BOOL := FALSE;
END_VAR

METHOD PUBLIC Init : BOOL
VAR_INPUT
    refBlock   : REFERENCE TO FB_SMCOutputBlock;
    nIndex     : USINT;
    bSafeState : BOOL := FALSE;   // Output state applied when SetSafeState() is called
END_VAR
    IF NOT __ISVALIDREF(refBlock) THEN
        Init := FALSE;
        RETURN;
    END_IF

    _refBlock REF= refBlock;
    _nIndex := nIndex;
    _bSafeState := bSafeState;
    _bInitialized := TRUE;
    Init := TRUE;
END_METHOD

METHOD PUBLIC SetOutput
VAR_INPUT
    bValue : BOOL;
END_VAR
    IF _bInitialized THEN
        _refBlock.SetOutput(_nIndex, bValue);
    END_IF
END_METHOD

METHOD PUBLIC GetOutput : BOOL
    IF _bInitialized THEN
        GetOutput := _refBlock.GetOutput(_nIndex);
    ELSE
        GetOutput := FALSE;
    END_IF
END_METHOD

METHOD PUBLIC SetSafeState
    IF _bInitialized THEN
        _refBlock.SetOutput(_nIndex, _bSafeState);
    END_IF
END_METHOD

END_FUNCTION_BLOCK
