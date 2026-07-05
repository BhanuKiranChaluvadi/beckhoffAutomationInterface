(*
FB_DoorLock — Controls a door lock output and aggregates its proximity sensors.

Composite pattern: a lock owns N FB_DoorSensor children (1..GVL_SubsystemSizes.MAX_SENSORS_PER_LOCK).
AllSensorsClosed iterates only sensors that were registered via AddSensor — a lock
with 1 sensor and a lock with 2 sensors both work correctly without any code change.
*)
FUNCTION_BLOCK FB_DoorLock
VAR
    _ipOutput     : I_DigitalOutput;
    _afbSensors   : ARRAY[1..GVL_SubsystemSizes.MAX_SENSORS_PER_LOCK] OF FB_DoorSensor;
    _nSensorCount : INT := 0;
    _bInitialized : BOOL := FALSE;
END_VAR


METHOD PUBLIC Init : BOOL
VAR_INPUT
    ipOutput : I_DigitalOutput;
END_VAR
    IF ipOutput = 0 THEN
        RETURN;
    END_IF
    _ipOutput     := ipOutput;
    _nSensorCount := 0;
    _bInitialized := TRUE;
    Init := TRUE;
END_METHOD


METHOD PUBLIC AddSensor : BOOL
VAR_INPUT
    ipInput : I_DigitalInput;
    bInvert : BOOL := FALSE;
END_VAR
    IF _nSensorCount >= GVL_SubsystemSizes.MAX_SENSORS_PER_LOCK THEN
        RETURN;
    END_IF
    IF _afbSensors[_nSensorCount + 1].Init(ipInput := ipInput, bInvert := bInvert) THEN
        _nSensorCount := _nSensorCount + 1;
        AddSensor := TRUE;
    END_IF
END_METHOD


METHOD PUBLIC Lock
    IF _bInitialized AND_THEN _ipOutput <> 0 THEN
        _ipOutput.SetOutput(TRUE);
    END_IF
END_METHOD


METHOD PUBLIC Unlock
    IF _bInitialized AND_THEN _ipOutput <> 0 THEN
        _ipOutput.SetOutput(FALSE);
    END_IF
END_METHOD


PROPERTY PUBLIC IsLocked : BOOL
    GET
        IF _bInitialized AND_THEN _ipOutput <> 0 THEN
            IsLocked := _ipOutput.GetOutput();
        END_IF
    END_GET
END_PROPERTY


METHOD PUBLIC AllSensorsClosed : BOOL
VAR
    i : INT;
END_VAR
    // Composite: iterate only registered sensors (not the full array bound)
    AllSensorsClosed := TRUE;
    FOR i := 1 TO _nSensorCount DO
        IF NOT _afbSensors[i].IsClosed THEN
            AllSensorsClosed := FALSE;
            RETURN;
        END_IF
    END_FOR
END_METHOD

END_FUNCTION_BLOCK
