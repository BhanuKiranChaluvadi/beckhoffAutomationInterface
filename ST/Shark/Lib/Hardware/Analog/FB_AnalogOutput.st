(* Analog output control for hardware interface. *)
FUNCTION_BLOCK FB_AnalogOutput IMPLEMENTS I_AnalogOutput
VAR CONSTANT
    MAX_RAW_VALUE : INT := 32767;  // Full scale for 0-10V or 4-20mA outputs
END_VAR
VAR
    _fbError       : FB_ErrorHandler;
    _sInstanceName : STRING(80);
    _Output        : REFERENCE TO INT;
    _nSafeValue    : INT := 0;    // Applied by SetSafeState() — configured at Init time
END_VAR

METHOD PUBLIC Init : BOOL
VAR_INPUT
    sInstanceName : STRING(80);
    ipOutputRef   : REFERENCE TO INT;
    nSafeValue    : INT := 0;     // Raw output value applied when SetSafeState() is called (0 = de-energise)
END_VAR
    _fbError.Init(sInstanceName);
    _sInstanceName := sInstanceName;

    IF NOT __ISVALIDREF(ipOutputRef) THEN
        _fbError.Raise(E_ErrorId.ERR_GEN_NULL_POINTER, 'Output reference is invalid');
        RETURN;
    END_IF

    _Output REF= ipOutputRef;
    _nSafeValue := LIMIT(0, nSafeValue, MAX_RAW_VALUE);
    _fbError.Clear();
    Init := TRUE;
END_METHOD

METHOD PUBLIC SetOutput
VAR_INPUT
    nValue : INT;
END_VAR
    IF __ISVALIDREF(_Output) THEN
        _Output := LIMIT(0, nValue, MAX_RAW_VALUE);
    END_IF
END_METHOD

METHOD PUBLIC SetPercentage
VAR_INPUT
    rPercentage : REAL;
END_VAR
VAR
    rClamped : REAL;
END_VAR
    rClamped := LIMIT(0.0, rPercentage, 100.0);
    SetOutput(REAL_TO_INT(rClamped * INT_TO_REAL(MAX_RAW_VALUE) / 100.0));
END_METHOD

METHOD PUBLIC GetOutput : INT
    IF __ISVALIDREF(_Output) THEN
        GetOutput := _Output;
    END_IF
END_METHOD

METHOD PUBLIC GetPercentage : REAL
    GetPercentage := INT_TO_REAL(GetOutput()) * 100.0 / INT_TO_REAL(MAX_RAW_VALUE);
END_METHOD

METHOD PUBLIC SetSafeState
    IF __ISVALIDREF(_Output) THEN
        _Output := _nSafeValue;
    END_IF
END_METHOD

METHOD PUBLIC GetError : ST_Error
    GetError := _fbError.GetError();
END_METHOD

METHOD PUBLIC IsError : BOOL
    IsError := _fbError.IsActive();
END_METHOD

END_FUNCTION_BLOCK
