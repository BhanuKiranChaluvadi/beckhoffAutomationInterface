(* FB_NodeList - Encapsulates child management for composite nodes (Composition over Inheritance). *)
FUNCTION_BLOCK FB_NodeList
VAR
    _aChildren   : ARRAY[1..GVL_BT_Constants.MAX_BT_CHILDREN] OF I_TreeNode;
    _nChildCount : INT := 0;
END_VAR

METHOD PUBLIC AddChild : BOOL
VAR_INPUT
    ipNode : I_TreeNode;
END_VAR
    IF ipNode = 0 THEN
        AddChild := FALSE;
        RETURN;
    END_IF
    IF _nChildCount >= GVL_BT_Constants.MAX_BT_CHILDREN THEN
        AddChild := FALSE;
        RETURN;
    END_IF
    _nChildCount := _nChildCount + 1;
    _aChildren[_nChildCount] := ipNode;
    AddChild := TRUE;
END_METHOD

METHOD PUBLIC ClearChildren
VAR
    i : INT;
END_VAR
    FOR i := 1 TO _nChildCount DO
        _aChildren[i] := 0;
    END_FOR
    _nChildCount := 0;
END_METHOD

METHOD PUBLIC GetChild : I_TreeNode
VAR_INPUT
    nIndex : INT;
END_VAR
    IF nIndex >= 1 AND nIndex <= _nChildCount THEN
        GetChild := _aChildren[nIndex];
    ELSE
        GetChild := 0;
    END_IF
END_METHOD

METHOD PUBLIC GetCount : INT
    GetCount := _nChildCount;
END_METHOD

METHOD PUBLIC HaltAll
VAR
    i : INT;
    ipChild : I_TreeNode;
END_VAR
    FOR i := 1 TO _nChildCount DO
        ipChild := _aChildren[i];
        IF ipChild <> 0 THEN
            ipChild.Halt();
        END_IF
    END_FOR
END_METHOD

METHOD PUBLIC ResetAll : BOOL
VAR
    i : INT;
    ipChild : I_TreeNode;
    bAllReset : BOOL := TRUE;
END_VAR
    FOR i := 1 TO _nChildCount DO
        ipChild := _aChildren[i];
        IF ipChild <> 0 THEN
            IF NOT ipChild.Reset() THEN
                bAllReset := FALSE;
            END_IF
        END_IF
    END_FOR
    ResetAll := bAllReset;
END_METHOD
END_FUNCTION_BLOCK