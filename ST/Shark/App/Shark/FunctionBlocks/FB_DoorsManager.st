(*
FB_DoorsManager — Shark-specific door wiring. Implements I_DoorSystem (Facade).

Facade pattern: presents the I_DoorSystem surface to sequence and safety code;
hides the lock/sensor graph and all Shark identifier knowledge from callers.

Layout: 9 proximity sensors, 2 door locks.
  Lock 1 FL (Front Left)  → sensors PRXS_DOOR_LT, PRXS_DOOR_LM
  Lock 2 FR (Front Right) → sensors PRXS_DOOR_RT, PRXS_DOOR_RM
  Remaining sensors (LB, RB, BML, BMR, BMB) are standalone monitoring only.

Sensor index assumption: E_DigitalInputIdentifier door enum values are dense
and align 1:1 with _afbSensors[1..SENSOR_COUNT]. If the enum is renumbered
this mapping must be revisited.
*)
FUNCTION_BLOCK FB_DoorsManager IMPLEMENTS I_DoorSystem
VAR
    _afbSensors   : ARRAY[1..GVL_DoorSystem.DOOR_SENSOR_COUNT] OF FB_DoorSensor;
    _afbLocks     : ARRAY[1..GVL_DoorSystem.DOOR_LOCK_COUNT] OF FB_DoorLock;
    _bInitialized : BOOL := FALSE;
END_VAR


METHOD PUBLIC Init : BOOL
VAR_INPUT
    ipDISub : I_DigitalInputSubsystem;
    ipDOSub : I_DigitalOutputSubsystem;
END_VAR
VAR
    i : INT;
END_VAR
    IF ipDISub = 0 OR ipDOSub = 0 THEN
        GVL_ErrorLogger.Logger.LogError(
            sSource  := 'DoorsManager.Init',
            eErrorId := E_ErrorId.ERR_GEN_NULL_POINTER,
            sContext := 'invalid subsystem interface pointer'
        );
        RETURN;
    END_IF

    // Initialise all 9 standalone monitoring sensors.
    // Relies on dense enum alignment: sensor identifier i maps to _afbSensors[i].
    FOR i := 1 TO GVL_DoorSystem.DOOR_SENSOR_COUNT DO
        _afbSensors[i].Init(ipInput := ipDISub.GetInputByIndex(nInput := i));
    END_FOR

    // Lock 1 — Front Left
    _afbLocks[1].Init(
        ipOutput := ipDOSub.GetOutputByIndex(nOutput := E_DigitalOutputIdentifier.LOCK_DOOR_FL)
    );
    _afbLocks[1].AddSensor(ipInput := ipDISub.GetInputByIndex(nInput := E_DigitalInputIdentifier.PRXS_DOOR_LT));
    _afbLocks[1].AddSensor(ipInput := ipDISub.GetInputByIndex(nInput := E_DigitalInputIdentifier.PRXS_DOOR_LM));

    // Lock 2 — Front Right
    _afbLocks[2].Init(
        ipOutput := ipDOSub.GetOutputByIndex(nOutput := E_DigitalOutputIdentifier.LOCK_DOOR_FR)
    );
    _afbLocks[2].AddSensor(ipInput := ipDISub.GetInputByIndex(nInput := E_DigitalInputIdentifier.PRXS_DOOR_RT));
    _afbLocks[2].AddSensor(ipInput := ipDISub.GetInputByIndex(nInput := E_DigitalInputIdentifier.PRXS_DOOR_RM));

    _bInitialized := TRUE;
    Init := TRUE;
END_METHOD


METHOD PUBLIC IsDoorClosed : BOOL
VAR_INPUT
    nSensorIndex : INT;
END_VAR
    IF nSensorIndex >= 1 AND nSensorIndex <= GVL_DoorSystem.DOOR_SENSOR_COUNT THEN
        IsDoorClosed := _afbSensors[nSensorIndex].IsClosed;
    END_IF
END_METHOD


METHOD PUBLIC IsLockActivated : BOOL
VAR_INPUT
    nLockIndex : INT;
END_VAR
    IF nLockIndex >= 1 AND nLockIndex <= GVL_DoorSystem.DOOR_LOCK_COUNT THEN
        IsLockActivated := _afbLocks[nLockIndex].IsLocked;
    END_IF
END_METHOD


METHOD PUBLIC LockDoor
VAR_INPUT
    nLockIndex : INT;
END_VAR
    IF nLockIndex >= 1 AND nLockIndex <= GVL_DoorSystem.DOOR_LOCK_COUNT THEN
        _afbLocks[nLockIndex].Lock();
    END_IF
END_METHOD


METHOD PUBLIC UnlockDoor
VAR_INPUT
    nLockIndex : INT;
END_VAR
    IF nLockIndex >= 1 AND nLockIndex <= GVL_DoorSystem.DOOR_LOCK_COUNT THEN
        _afbLocks[nLockIndex].Unlock();
    END_IF
END_METHOD


METHOD PUBLIC AreAllDoorsClosed : BOOL
VAR
    i : INT;
END_VAR
    AreAllDoorsClosed := TRUE;
    FOR i := 1 TO GVL_DoorSystem.DOOR_SENSOR_COUNT DO
        IF NOT _afbSensors[i].IsClosed THEN
            AreAllDoorsClosed := FALSE;
            RETURN;
        END_IF
    END_FOR
END_METHOD

END_FUNCTION_BLOCK
