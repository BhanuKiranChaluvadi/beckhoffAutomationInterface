// Lib digital-output subsystem.
//
// Owns a small fixed-capacity array of I_DigitalOutput references. Count
// (and the array's effective range) is supplied by the caller at Init time
// via the App-side GVL constant. Lib code never references App enums or GVL.
FUNCTION_BLOCK FB_DigitalOutputSubsystem IMPLEMENTS I_DigitalOutputSubsystem
VAR_OUTPUT
    bInitialized : BOOL := FALSE;
END_VAR
VAR
    _aipOutputs : ARRAY[1..GVL_SubsystemSizes.MAX_DIGITAL_OUTPUTS] OF I_DigitalOutput;
    _nCount     : INT := 0;
END_VAR

METHOD PUBLIC Init : BOOL
VAR_INPUT
    aipOutputs : ARRAY[*] OF I_DigitalOutput;
    nCount     : INT;
END_VAR
VAR
    i      : INT;
    nLower : DINT;
END_VAR
    IF bInitialized THEN
        Init := TRUE;
        RETURN;
    END_IF

    IF nCount < 1 OR nCount > GVL_SubsystemSizes.MAX_DIGITAL_OUTPUTS THEN
        RETURN;
    END_IF

    nLower := LOWER_BOUND(aipOutputs, 1);
    IF nCount > (UPPER_BOUND(aipOutputs, 1) - nLower + 1) THEN
        RETURN;
    END_IF

    _nCount := nCount;
    FOR i := 1 TO _nCount DO
        _aipOutputs[i] := aipOutputs[nLower + i - 1];
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

METHOD PUBLIC GetOutputByIndex : I_DigitalOutput
VAR_INPUT
    nOutput : INT;
END_VAR
    IF nOutput >= 1 AND nOutput <= _nCount THEN
        GetOutputByIndex := _aipOutputs[nOutput];
    ELSE
        GetOutputByIndex := 0;
    END_IF
END_METHOD

METHOD PUBLIC SetOutputByIndex : BOOL
VAR_INPUT
    nOutput : INT;
    bValue  : BOOL;
END_VAR
VAR
    ipOutput : I_DigitalOutput;
END_VAR
    ipOutput := GetOutputByIndex(nOutput);
    IF ipOutput <> 0 THEN
        ipOutput.SetOutput(bValue);
        SetOutputByIndex := TRUE;
    END_IF
END_METHOD

METHOD PUBLIC GetStateByIndex : BOOL
VAR_INPUT
    nOutput : INT;
END_VAR
VAR
    ipOutput : I_DigitalOutput;
END_VAR
    ipOutput := GetOutputByIndex(nOutput);
    IF ipOutput <> 0 THEN
        GetStateByIndex := ipOutput.GetOutput();
    END_IF
END_METHOD

END_FUNCTION_BLOCK
