(*
FB_DoorSensor — Wraps an I_DigitalInput to represent a door proximity sensor.

Null Object pattern: an uninitialised sensor always reports IsClosed = FALSE
(door assumed open — the safe default for any safety or interlock logic).

bInvert: pass TRUE for normally-closed sensors where a LOW signal means closed.
*)
FUNCTION_BLOCK FB_DoorSensor
VAR
    _ipInput      : I_DigitalInput;
    _bInvert      : BOOL := FALSE;
    _bInitialized : BOOL := FALSE;
END_VAR


METHOD PUBLIC Init : BOOL
VAR_INPUT
    ipInput : I_DigitalInput;
    bInvert : BOOL := FALSE;
END_VAR
    IF ipInput = 0 THEN
        RETURN;
    END_IF
    _ipInput      := ipInput;
    _bInvert      := bInvert;
    _bInitialized := TRUE;
    Init := TRUE;
END_METHOD


PROPERTY PUBLIC IsClosed : BOOL
    GET
        // Null Object: uninitialized returns FALSE (door assumed open — safe default)
        IF _bInitialized AND_THEN _ipInput <> 0 THEN
            IsClosed := _ipInput.GetState() XOR _bInvert;
        END_IF
    END_GET
END_PROPERTY

END_FUNCTION_BLOCK
