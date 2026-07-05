(* SMC EtherCAT output block - manages 32 digital outputs via 4 bytes. *)
FUNCTION_BLOCK FB_SMCOutputBlock
VAR_IN_OUT
    aOutputBytes : ARRAY[0..3] OF BYTE;
END_VAR
VAR_OUTPUT
    bError     : BOOL;
    eErrorId   : E_ErrorId;
END_VAR
VAR
    _pOutputBytes : POINTER TO ARRAY[0..3] OF BYTE;
END_VAR
VAR CONSTANT
    MAX_OUTPUTS : USINT := 32;
END_VAR

    // Capture the address of the IN_OUT variable during FB execution
    _pOutputBytes := ADR(aOutputBytes);

METHOD PUBLIC SetOutput : BOOL
VAR_INPUT
    nIndex : USINT;
    bState : BOOL;
END_VAR
VAR
    nByteIdx : USINT;
    nBitIdx  : USINT;
END_VAR
    IF nIndex >= MAX_OUTPUTS THEN
        bError := TRUE;
        eErrorId := E_ErrorId.ERR_VALVE_INVALID_INDEX;
        SetOutput := FALSE;
        RETURN;
    END_IF

    // Check if pointer is valid (FB must be executed at least once)
    IF _pOutputBytes = 0 THEN
        bError := TRUE;
        eErrorId := E_ErrorId.ERR_GEN_NULL_POINTER;
        SetOutput := FALSE;
        RETURN;
    END_IF

    nByteIdx := nIndex / 8;
    nBitIdx := nIndex MOD 8;

    IF bState THEN
        _pOutputBytes^[nByteIdx] := _pOutputBytes^[nByteIdx] OR SHL(USINT_TO_BYTE(1), nBitIdx);
    ELSE
        _pOutputBytes^[nByteIdx] := _pOutputBytes^[nByteIdx] AND NOT SHL(USINT_TO_BYTE(1), nBitIdx);
    END_IF

    SetOutput := TRUE;
END_METHOD

METHOD PUBLIC GetOutput : BOOL
VAR_INPUT
    nIndex : USINT;
END_VAR
VAR
    nByteIdx : USINT;
    nBitIdx  : USINT;
END_VAR
    IF nIndex >= MAX_OUTPUTS THEN
        bError := TRUE;
        eErrorId := E_ErrorId.ERR_VALVE_INVALID_INDEX;
        GetOutput := FALSE;
        RETURN;
    END_IF

    IF _pOutputBytes = 0 THEN
        bError := TRUE;
        eErrorId := E_ErrorId.ERR_GEN_NULL_POINTER;
        GetOutput := FALSE;
        RETURN;
    END_IF

    nByteIdx := nIndex / 8;
    nBitIdx := nIndex MOD 8;

    GetOutput := (_pOutputBytes^[nByteIdx] AND SHL(USINT_TO_BYTE(1), nBitIdx)) <> 0;
END_METHOD

METHOD PUBLIC ClearAll
    IF _pOutputBytes = 0 THEN
        RETURN;
    END_IF

    _pOutputBytes^[0] := 0;
    _pOutputBytes^[1] := 0;
    _pOutputBytes^[2] := 0;
    _pOutputBytes^[3] := 0;
END_METHOD

METHOD PUBLIC ClearError
    bError := FALSE;
    eErrorId := E_ErrorId.NO_ERROR;
END_METHOD

END_FUNCTION_BLOCK
