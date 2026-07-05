(* FB_Blackboard — Shared key-value store for Behavior Tree nodes

Provides a simple typed key-value store that nodes can use to share data.
Supports INT (DINT), REAL (LREAL), BOOL, and STRING types.
Keys are STRING(50). Max entries defined by constant.
*)
FUNCTION_BLOCK FB_Blackboard
VAR
    _asKeys    : ARRAY[1..MAX_ENTRIES] OF STRING(50);
    _anIntVals : ARRAY[1..MAX_ENTRIES] OF DINT;
    _arRealVals: ARRAY[1..MAX_ENTRIES] OF LREAL;
    _abBoolVals: ARRAY[1..MAX_ENTRIES] OF BOOL;
    _asStrVals : ARRAY[1..MAX_ENTRIES] OF STRING(255);
    _aeTypes   : ARRAY[1..MAX_ENTRIES] OF INT; // BB_TYPE_UNUSED=0, BB_TYPE_INT=1, BB_TYPE_REAL=2, BB_TYPE_BOOL=3, BB_TYPE_STRING=4
    _nCount    : INT := 0;
END_VAR
VAR CONSTANT
    MAX_ENTRIES    : INT := 64;
    BB_TYPE_UNUSED : INT := 0;
    BB_TYPE_INT    : INT := 1;
    BB_TYPE_REAL   : INT := 2;
    BB_TYPE_BOOL   : INT := 3;
    BB_TYPE_STRING : INT := 4;
END_VAR

METHOD PRIVATE _FindKey : INT
VAR_INPUT
    sKey : STRING(50);
END_VAR
VAR
    i : INT;
END_VAR
    FOR i := 1 TO _nCount DO
        IF _asKeys[i] = sKey THEN
            _FindKey := i;
            RETURN;
        END_IF
    END_FOR
    _FindKey := 0;
END_METHOD

METHOD PRIVATE _AllocKey : INT
VAR_INPUT
    sKey  : STRING(50);
    nType : INT;
END_VAR
VAR
    idx : INT;
END_VAR
    idx := _FindKey(sKey);
    IF idx > 0 THEN
        _aeTypes[idx] := nType;
        _AllocKey := idx;
        RETURN;
    END_IF

    IF _nCount >= MAX_ENTRIES THEN
        _AllocKey := 0;
        RETURN;
    END_IF

    _nCount := _nCount + 1;
    _asKeys[_nCount] := sKey;
    _aeTypes[_nCount] := nType;
    _AllocKey := _nCount;
END_METHOD

METHOD PUBLIC SetInt : BOOL
VAR_INPUT
    sKey   : STRING(50);
    nValue : DINT;
END_VAR
VAR
    idx : INT;
END_VAR
    idx := _AllocKey(sKey, BB_TYPE_INT);
    IF idx > 0 THEN
        _anIntVals[idx] := nValue;
        SetInt := TRUE;
    END_IF
END_METHOD

METHOD PUBLIC TryGetInt : BOOL
VAR_INPUT
    sKey : STRING(50);
END_VAR
VAR_OUTPUT
    nValue : DINT;
END_VAR
VAR
    idx : INT;
END_VAR
    idx := _FindKey(sKey);
    IF idx > 0 AND _aeTypes[idx] = BB_TYPE_INT THEN
        nValue := _anIntVals[idx];
        TryGetInt := TRUE;
    ELSE
        nValue := 0;
        TryGetInt := FALSE;
    END_IF
END_METHOD

METHOD PUBLIC SetReal : BOOL
VAR_INPUT
    sKey   : STRING(50);
    rValue : LREAL;
END_VAR
VAR
    idx : INT;
END_VAR
    idx := _AllocKey(sKey, BB_TYPE_REAL);
    IF idx > 0 THEN
        _arRealVals[idx] := rValue;
        SetReal := TRUE;
    END_IF
END_METHOD

METHOD PUBLIC TryGetReal : BOOL
VAR_INPUT
    sKey : STRING(50);
END_VAR
VAR_OUTPUT
    rValue : LREAL;
END_VAR
VAR
    idx : INT;
END_VAR
    idx := _FindKey(sKey);
    IF idx > 0 AND _aeTypes[idx] = BB_TYPE_REAL THEN
        rValue := _arRealVals[idx];
        TryGetReal := TRUE;
    ELSE
        rValue := 0.0;
        TryGetReal := FALSE;
    END_IF
END_METHOD

METHOD PUBLIC SetBool : BOOL
VAR_INPUT
    sKey   : STRING(50);
    bValue : BOOL;
END_VAR
VAR
    idx : INT;
END_VAR
    idx := _AllocKey(sKey, BB_TYPE_BOOL);
    IF idx > 0 THEN
        _abBoolVals[idx] := bValue;
        SetBool := TRUE;
    END_IF
END_METHOD

METHOD PUBLIC TryGetBool : BOOL
VAR_INPUT
    sKey : STRING(50);
END_VAR
VAR_OUTPUT
    bValue : BOOL;
END_VAR
VAR
    idx : INT;
END_VAR
    idx := _FindKey(sKey);
    IF idx > 0 AND _aeTypes[idx] = BB_TYPE_BOOL THEN
        bValue := _abBoolVals[idx];
        TryGetBool := TRUE;
    ELSE
        bValue := FALSE;
        TryGetBool := FALSE;
    END_IF
END_METHOD

METHOD PUBLIC SetString : BOOL
VAR_INPUT
    sKey   : STRING(50);
    sValue : STRING(255);
END_VAR
VAR
    idx : INT;
END_VAR
    idx := _AllocKey(sKey, BB_TYPE_STRING);
    IF idx > 0 THEN
        _asStrVals[idx] := sValue;
        SetString := TRUE;
    END_IF
END_METHOD

METHOD PUBLIC TryGetString : BOOL
VAR_INPUT
    sKey : STRING(50);
END_VAR
VAR_OUTPUT
    sValue : STRING(255);
END_VAR
VAR
    idx : INT;
END_VAR
    idx := _FindKey(sKey);
    IF idx > 0 AND _aeTypes[idx] = BB_TYPE_STRING THEN
        sValue := _asStrVals[idx];
        TryGetString := TRUE;
    ELSE
        sValue := '';
        TryGetString := FALSE;
    END_IF
END_METHOD

END_FUNCTION_BLOCK
