(* Facade contract for any machine's door management system.
   Sequence and safety code depends on this interface — never on the concrete manager.
   nSensorIndex / nLockIndex accept INT or any enum that widens to INT implicitly. *)
INTERFACE I_DoorSystem

METHOD IsDoorClosed : BOOL
VAR_INPUT
    nSensorIndex : INT;
END_VAR
END_METHOD

METHOD IsLockActivated : BOOL
VAR_INPUT
    nLockIndex : INT;
END_VAR
END_METHOD

METHOD LockDoor
VAR_INPUT
    nLockIndex : INT;
END_VAR
END_METHOD

METHOD UnlockDoor
VAR_INPUT
    nLockIndex : INT;
END_VAR
END_METHOD

METHOD AreAllDoorsClosed : BOOL
END_METHOD

END_INTERFACE
