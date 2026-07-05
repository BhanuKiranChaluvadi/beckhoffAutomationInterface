// Shark Chuck Vacuum System
//
// Shark hardware topology:
//   - Dedicated vacuum pump (PUMP_CHK_VAC) — toggled by this subsystem
//   - 2-way zone valves (VALV_CHK_LZ1/2/3) — open = vacuum applied
//   - Explicit vent valve (VALV_CHK_VENT) — must be closed before holding
//   - Single analog vacuum sensor (PSNS_CHK_VAC)
//
// Threshold semantics: rThreshold is in the same engineering units as the
// analog sensor; held when measurement < rThreshold, released when > rThreshold.
FUNCTION_BLOCK FB_ChuckVacuumSystem IMPLEMENTS I_ChuckVacuumSystem
VAR
    _bInitialized   : BOOL := FALSE;
    _bError         : BOOL := FALSE;

    _ipPump         : I_DigitalOutput;
    _ipValveLZ1     : I_Valve;
    _ipValveLZ2     : I_Valve;
    _ipValveLZ3     : I_Valve;
    _ipVentValve    : I_Valve;
    _ipVacuumSensor : I_Sensor;
END_VAR

VAR CONSTANT
    MAX_ZONES : INT := 3;
END_VAR

METHOD PUBLIC Init : BOOL
VAR_INPUT
    ipProvider : I_HardwareProvider;
END_VAR
    _bInitialized := FALSE;
    _bError := FALSE;

    IF ipProvider = 0 THEN
        GVL_ErrorLogger.Logger.LogError('FB_ChuckVacuumSystem', E_ErrorId.ERR_GEN_NULL_POINTER, 'Provider is null');
        RETURN;
    END_IF

    _ipPump := ipProvider.GetDigitalOutput(E_DigitalOutputIdentifier.PUMP_CHK_VAC);
    _ipValveLZ1 := ipProvider.GetValve(E_ValveIdentifier.VALV_CHK_LZ1);
    _ipValveLZ2 := ipProvider.GetValve(E_ValveIdentifier.VALV_CHK_LZ2);
    _ipValveLZ3 := ipProvider.GetValve(E_ValveIdentifier.VALV_CHK_LZ3);
    _ipVentValve := ipProvider.GetValve(E_ValveIdentifier.VALV_CHK_VENT);
    _ipVacuumSensor := ipProvider.GetPressureSensor(E_PressureSensorIdentifier.PSNS_CHK_VAC);

    IF _ipPump = 0 OR _ipValveLZ1 = 0 OR _ipVentValve = 0 OR _ipVacuumSensor = 0 THEN
        GVL_ErrorLogger.Logger.LogError('FB_ChuckVacuumSystem', E_ErrorId.ERR_GEN_NULL_POINTER, 'Required chuck vacuum hardware missing');
        RETURN;
    END_IF

    _bInitialized := TRUE;
    Init := TRUE;
END_METHOD

METHOD PUBLIC IsInitialized : BOOL
    IsInitialized := _bInitialized;
END_METHOD

METHOD PUBLIC IsError : BOOL
    IsError := _bError;
END_METHOD

METHOD PUBLIC GetMaxZoneCount : INT
    GetMaxZoneCount := MAX_ZONES;
END_METHOD

// Shark: close vent and ensure dedicated pump is on.
METHOD PUBLIC PrepareForHold : BOOL
    IF NOT _bInitialized THEN RETURN; END_IF
    _ipVentValve.Close();
    IF NOT _ipPump.GetOutput() THEN
        _ipPump.SetOutput(TRUE);
    END_IF
    PrepareForHold := TRUE;
END_METHOD

// Shark: open vent to atmosphere. Caller closes zones separately.
METHOD PUBLIC BeginRelease : BOOL
    IF NOT _bInitialized THEN RETURN; END_IF
    _ipVentValve.Open();
    BeginRelease := TRUE;
END_METHOD

// Shark: turn off dedicated chuck vacuum pump.
METHOD PUBLIC FinishRelease : BOOL
    IF NOT _bInitialized THEN RETURN; END_IF
    _ipPump.SetOutput(FALSE);
    FinishRelease := TRUE;
END_METHOD

METHOD PUBLIC OpenZone : BOOL
VAR_INPUT
    nZone : INT;
END_VAR
    OpenZone := _SetZoneState(nZone := nZone, bOpen := TRUE);
END_METHOD

METHOD PUBLIC CloseZone : BOOL
VAR_INPUT
    nZone : INT;
END_VAR
    CloseZone := _SetZoneState(nZone := nZone, bOpen := FALSE);
END_METHOD

// Analog sensor: vacuum held when pressure < threshold.
// nActiveZoneCount is ignored — single sensor measures shared vacuum manifold.
METHOD PUBLIC IsVacuumHeld : BOOL
VAR_INPUT
    nActiveZoneCount : INT;
    rThreshold       : LREAL;
END_VAR
    IF NOT _bInitialized THEN RETURN; END_IF
    IsVacuumHeld := (_ipVacuumSensor.GetMeasurement() < rThreshold);
END_METHOD

// Analog sensor: vacuum released when pressure > threshold.
METHOD PUBLIC IsVacuumReleased : BOOL
VAR_INPUT
    rThreshold : LREAL;
END_VAR
    IF NOT _bInitialized THEN RETURN; END_IF
    IsVacuumReleased := (_ipVacuumSensor.GetMeasurement() > rThreshold);
END_METHOD

METHOD PUBLIC Reset : BOOL
    _bError := FALSE;
    IF _ipValveLZ1 <> 0 AND_THEN _ipValveLZ1.IsError() THEN _ipValveLZ1.Reset(); END_IF
    IF _ipValveLZ2 <> 0 AND_THEN _ipValveLZ2.IsError() THEN _ipValveLZ2.Reset(); END_IF
    IF _ipValveLZ3 <> 0 AND_THEN _ipValveLZ3.IsError() THEN _ipValveLZ3.Reset(); END_IF
    IF _ipVentValve <> 0 AND_THEN _ipVentValve.IsError() THEN _ipVentValve.Reset(); END_IF
    Reset := TRUE;
END_METHOD

METHOD PRIVATE _SetZoneState : BOOL
VAR_INPUT
    nZone : INT;
    bOpen : BOOL;
END_VAR
    IF NOT _bInitialized THEN RETURN; END_IF

    CASE nZone OF
        1:
            IF _ipValveLZ1 = 0 THEN RETURN; END_IF
            IF bOpen THEN _ipValveLZ1.Open(); ELSE _ipValveLZ1.Close(); END_IF
        2:
            IF _ipValveLZ2 = 0 THEN RETURN; END_IF
            IF bOpen THEN _ipValveLZ2.Open(); ELSE _ipValveLZ2.Close(); END_IF
        3:
            IF _ipValveLZ3 = 0 THEN RETURN; END_IF
            IF bOpen THEN _ipValveLZ3.Open(); ELSE _ipValveLZ3.Close(); END_IF
        ELSE
            RETURN;
    END_CASE

    _SetZoneState := TRUE;
END_METHOD

END_FUNCTION_BLOCK
