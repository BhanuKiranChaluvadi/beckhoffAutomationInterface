// Lib digital-input subsystem.
//
// Owns a small fixed-capacity array of I_DigitalInput references. Count
// (and the array's effective range) is supplied by the caller at Init time
// via the App-side GVL constant. Lib code never references App enums or GVL.
FUNCTION_BLOCK FB_DigitalInputSubsystem IMPLEMENTS I_DigitalInputSubsystem
VAR_OUTPUT
    bInitialized : BOOL := FALSE;
END_VAR
VAR
    _aipInputs : ARRAY[1..GVL_SubsystemSizes.MAX_DIGITAL_INPUTS] OF I_DigitalInput;
    _nCount    : INT := 0;
END_VAR

METHOD PUBLIC Init : BOOL
VAR_INPUT
    aipInputs : ARRAY[*] OF I_DigitalInput;
    nCount    : INT;
END_VAR
VAR
    i      : INT;
    nLower : DINT;
END_VAR
    IF bInitialized THEN
        Init := TRUE;
        RETURN;
    END_IF

    IF nCount < 1 OR nCount > GVL_SubsystemSizes.MAX_DIGITAL_INPUTS THEN
        RETURN;
    END_IF

    nLower := LOWER_BOUND(aipInputs, 1);
    IF nCount > (UPPER_BOUND(aipInputs, 1) - nLower + 1) THEN
        RETURN;
    END_IF

    _nCount := nCount;
    FOR i := 1 TO _nCount DO
        _aipInputs[i] := aipInputs[nLower + i - 1];
    END_FOR

    bInitialized := TRUE;
    Init := TRUE;
END_METHOD

METHOD PUBLIC Execute : BOOL
    Execute := TRUE;
END_METHOD

METHOD PUBLIC IsInitialized : BOOL
    IsInitialized := bInitialized;
END_METHOD

METHOD PUBLIC GetCount : INT
    GetCount := _nCount;
END_METHOD

METHOD PUBLIC GetInputByIndex : I_DigitalInput
VAR_INPUT
    nInput : INT;
END_VAR
    IF nInput >= 1 AND nInput <= _nCount THEN
        GetInputByIndex := _aipInputs[nInput];
    ELSE
        GetInputByIndex := 0;
    END_IF
END_METHOD

METHOD PUBLIC GetStateByIndex : BOOL
VAR_INPUT
    nInput : INT;
END_VAR
VAR
    ipInput : I_DigitalInput;
END_VAR
    ipInput := GetInputByIndex(nInput);
    IF ipInput <> 0 THEN
        GetStateByIndex := ipInput.GetState();
    END_IF
END_METHOD

END_FUNCTION_BLOCK
