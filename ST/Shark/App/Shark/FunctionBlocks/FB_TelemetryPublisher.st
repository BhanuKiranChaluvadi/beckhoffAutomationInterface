(*
FB_TelemetryPublisher (Shark) — Detailed per-channel telemetry view.

EXTENDS FB_TelemetryPublisherBase (in Lib) which owns the six shared
subsystem refs, Init plumbing, and the per-scan orchestration body. This
derived FB overrides each hook to write into the Shark-specific
ST_MachineTelemetry struct, including the hand-mapped DIOS_ array.

ADS symbol paths:
    READ: PRG_Telemetry.fbTelemetryPublisher.stTelemetry         (ST_MachineTelemetry) — live, updated every scan
    READ: PRG_Telemetry.fbTelemetryPublisher.stSnapshotTelemetry (ST_MachineTelemetry) — frozen at first error; valid when bSnapshotValid = TRUE
    READ: PRG_Telemetry.fbTelemetryPublisher.stSnapshotError     (ST_Error)            — which error triggered the freeze
    READ: PRG_Telemetry.fbTelemetryPublisher.bSnapshotValid      (BOOL)                — FALSE until a snapshot is captured

Snapshot lifecycle:
    Error fires → bSnapshotValid := TRUE, stSnapshotTelemetry frozen, ADSLOG written.
    Snapshot persists through error-clear. Call fbTelemetryPublisher.ResetSnapshot()
    (e.g. from a sequence or command receiver) to re-arm for the next fault.
*)
FUNCTION_BLOCK FB_TelemetryPublisher EXTENDS FB_TelemetryPublisherBase
VAR_OUTPUT
    stTelemetry         : ST_MachineTelemetry;
    // Error-time snapshot fields. Frozen once per unique fault; persist until ResetSnapshot() is called.
    stSnapshotTelemetry : ST_MachineTelemetry; // frozen copy of stTelemetry at error moment
    stSnapshotError     : ST_Error;            // which error triggered the freeze
    bSnapshotValid      : BOOL;                // FALSE at startup; TRUE once a snapshot has been captured
END_VAR

    SUPER^();


METHOD PUBLIC Init : BOOL
VAR_INPUT
    refValveSub    : REFERENCE TO FB_ValveSubsystem;
    refFlowSub     : REFERENCE TO FB_FlowSubsystem;
    refPressureSub : REFERENCE TO FB_PressureSubsystem;
    refTempSub     : REFERENCE TO FB_TemperatureSubsystem;
    refDISub       : REFERENCE TO FB_DigitalInputSubsystem;
    refDOSub       : REFERENCE TO FB_DigitalOutputSubsystem;
END_VAR
    Init := SUPER^.InitSubsystems(
        refValveSub    := refValveSub,
        refFlowSub     := refFlowSub,
        refPressureSub := refPressureSub,
        refTempSub     := refTempSub,
        refDISub       := refDISub,
        refDOSub       := refDOSub);
END_METHOD


METHOD PUBLIC SetCommandIndex
VAR_INPUT
    nCommandIndex : DINT;
END_VAR
    stTelemetry.STAT_[1].CommandIndex := nCommandIndex;
END_METHOD


METHOD PROTECTED _UpdateValves
VAR
    i       : INT;
    ipValve : I_Valve;
END_VAR
    FOR i := 1 TO GVL_ValveSystem.VALVE_COUNT DO
        ipValve := _refValveSub.GetValveByIndex(nValveId := i);
        IF ipValve <> 0 THEN
            stTelemetry.VALV_[i].Open := ipValve.IsOpen();
        END_IF
    END_FOR
END_METHOD


METHOD PROTECTED _UpdateMFC
VAR
    ipSensor     : I_Sensor;
    ipController : I_Controller;
    status       : ST_ControllerStatus;
    i            : INT;
END_VAR
    FOR i := 1 TO GVL_FlowSystem.FLOW_ZONE_COUNT DO
        ipSensor     := _refFlowSub.GetSensorByIndex(nZone := i);
        ipController := _refFlowSub.GetControllerByIndex(nZone := i);
        IF ipSensor <> 0 THEN
            stTelemetry.MFCT_[i].Measure := TO_REAL(ipSensor.GetMeasurement());
        END_IF
        IF ipController <> 0 THEN
            status := ipController.GetStatus();
            stTelemetry.MFCT_[i].SetPoint      := TO_REAL(status.rSetpoint);
            stTelemetry.MFCT_[i].TargetReached := status.bAtSetpoint;
        END_IF
    END_FOR
END_METHOD


METHOD PROTECTED _UpdatePressureSensors
VAR
    ipSensor : I_Sensor;
    i        : INT;
END_VAR
    FOR i := 1 TO GVL_PressureSystem.SENSOR_COUNT DO
        ipSensor := _refPressureSub.GetSensorByIndex(nSensor := i);
        IF ipSensor <> 0 THEN
            stTelemetry.PSNS_[i].Pressure := TO_REAL(ipSensor.GetMeasurement());
        END_IF
    END_FOR
END_METHOD


METHOD PROTECTED _UpdatePressureControllers
VAR
    ipController : I_Controller;
    ipSensor     : I_Sensor;
    status       : ST_ControllerStatus;
    i            : INT;
END_VAR
    FOR i := 1 TO GVL_PressureSystem.CONTROLLER_COUNT DO
        ipController := _refPressureSub.GetControllerByIndex(nCtrl := i);
        IF ipController <> 0 THEN
            status := ipController.GetStatus();
            stTelemetry.PCTR_[i].Enable        := status.bEnabled;
            stTelemetry.PCTR_[i].SetPoint      := TO_REAL(status.rSetpoint);
            stTelemetry.PCTR_[i].ValveOutput   := TO_REAL(status.rOutput);
            stTelemetry.PCTR_[i].TargetReached := status.bAtSetpoint;
        END_IF
        ipSensor := _refPressureSub.GetControllerAsSensorByIndex(nCtrl := i);
        IF ipSensor <> 0 THEN
            stTelemetry.PCTR_[i].Measure := TO_REAL(ipSensor.GetMeasurement());
        END_IF
    END_FOR
END_METHOD


METHOD PROTECTED _UpdateHeaters
VAR
    iZone      : INT;
    c          : INT;
    nCount     : INT;
    nGlobalIdx : INT;
    ipChannel  : I_Controller;
    stCtrl     : ST_ControllerStatus;
END_VAR
    // Detailed (per-CHANNEL) view: one slot per FB_BeckhoffTempController.
    FOR iZone := 1 TO GVL_TemperatureZones.CONTROLLABLE_ZONE_MAX DO
        nCount := GVL_TemperatureZoneConfig.ZONE_CONFIG[iZone].nChannelCount;
        FOR c := 1 TO nCount DO
            nGlobalIdx := _refTempSub.GetChannelGlobalIndex(eZone := iZone, nChannelIdx := c);
            IF nGlobalIdx < 1 THEN CONTINUE; END_IF
            ipChannel := _refTempSub.GetChannel(eZone := iZone, nChannelIdx := c);
            IF ipChannel = 0 THEN CONTINUE; END_IF
            stCtrl := ipChannel.GetStatus();
            stTelemetry.HETR_[nGlobalIdx].Enable             := stCtrl.bEnabled;
            stTelemetry.HETR_[nGlobalIdx].TempAvg            := TO_REAL(stCtrl.rMeasuredValue);
            stTelemetry.HETR_[nGlobalIdx].SP                 := TO_REAL(stCtrl.rSetpoint);
            stTelemetry.HETR_[nGlobalIdx].TemperatureReached := stCtrl.bAtSetpoint;
            stTelemetry.HETR_[nGlobalIdx].Status             := SEL(
                stCtrl.stError.eErrorId = E_ErrorId.NO_ERROR, INT#1, INT#0);
        END_FOR
    END_FOR

    // Per-ZONE summary (zone aggregate from FB_HeatZone).
    _UpdateZoneHeaters(stTelemetry.ZHETR_);
END_METHOD


METHOD PROTECTED _UpdateDigitalIO
    // Camera lights
    stTelemetry.DIOS_[E_DIOIdentifier.LGHT_CMR_SIDE].State :=
        _refDOSub.GetStateByIndex(nOutput := E_DigitalOutputIdentifier.LGHT_CMR_SIDE);
    stTelemetry.DIOS_[E_DIOIdentifier.LGHT_CMR_TOP].State :=
        _refDOSub.GetStateByIndex(nOutput := E_DigitalOutputIdentifier.LGHT_CMR_TOP);

    // Chiller
    stTelemetry.DIOS_[E_DIOIdentifier.CHIL_PWR].State :=
        _refDOSub.GetStateByIndex(nOutput := E_DigitalOutputIdentifier.CHIL_PWR);

    // Signal lights
    stTelemetry.DIOS_[E_DIOIdentifier.LGHT_SGN_RED].State :=
        _refDOSub.GetStateByIndex(nOutput := E_DigitalOutputIdentifier.LGHT_SGN_RED);
    stTelemetry.DIOS_[E_DIOIdentifier.LGHT_SGN_YEL].State :=
        _refDOSub.GetStateByIndex(nOutput := E_DigitalOutputIdentifier.LGHT_SGN_YEL);
    stTelemetry.DIOS_[E_DIOIdentifier.LGHT_SGN_GRN].State :=
        _refDOSub.GetStateByIndex(nOutput := E_DigitalOutputIdentifier.LGHT_SGN_GRN);

    // Door locks
    stTelemetry.DIOS_[E_DIOIdentifier.LOCK_DOOR_FL].State :=
        _refDOSub.GetStateByIndex(nOutput := E_DigitalOutputIdentifier.LOCK_DOOR_FL);
    stTelemetry.DIOS_[E_DIOIdentifier.LOCK_DOOR_FR].State :=
        _refDOSub.GetStateByIndex(nOutput := E_DigitalOutputIdentifier.LOCK_DOOR_FR);

    // Door sensors (9 proximity sensors)
    stTelemetry.DIOS_[E_DIOIdentifier.PRXS_DOOR_LT].State :=
        _refDISub.GetStateByIndex(nInput := E_DigitalInputIdentifier.PRXS_DOOR_LT);
    stTelemetry.DIOS_[E_DIOIdentifier.PRXS_DOOR_LM].State :=
        _refDISub.GetStateByIndex(nInput := E_DigitalInputIdentifier.PRXS_DOOR_LM);
    stTelemetry.DIOS_[E_DIOIdentifier.PRXS_DOOR_LB].State :=
        _refDISub.GetStateByIndex(nInput := E_DigitalInputIdentifier.PRXS_DOOR_LB);
    stTelemetry.DIOS_[E_DIOIdentifier.PRXS_DOOR_RT].State :=
        _refDISub.GetStateByIndex(nInput := E_DigitalInputIdentifier.PRXS_DOOR_RT);
    stTelemetry.DIOS_[E_DIOIdentifier.PRXS_DOOR_RM].State :=
        _refDISub.GetStateByIndex(nInput := E_DigitalInputIdentifier.PRXS_DOOR_RM);
    stTelemetry.DIOS_[E_DIOIdentifier.PRXS_DOOR_RB].State :=
        _refDISub.GetStateByIndex(nInput := E_DigitalInputIdentifier.PRXS_DOOR_RB);
    stTelemetry.DIOS_[E_DIOIdentifier.PRXS_DOOR_BML].State :=
        _refDISub.GetStateByIndex(nInput := E_DigitalInputIdentifier.PRXS_DOOR_BML);
    stTelemetry.DIOS_[E_DIOIdentifier.PRXS_DOOR_BMR].State :=
        _refDISub.GetStateByIndex(nInput := E_DigitalInputIdentifier.PRXS_DOOR_BMR);
    stTelemetry.DIOS_[E_DIOIdentifier.PRXS_DOOR_BMB].State :=
        _refDISub.GetStateByIndex(nInput := E_DigitalInputIdentifier.PRXS_DOOR_BMB);

    // Chuck vacuum pump
    stTelemetry.DIOS_[E_DIOIdentifier.PUMP_CHK_VAC].State :=
        _refDOSub.GetStateByIndex(nOutput := E_DigitalOutputIdentifier.PUMP_CHK_VAC);

    // Chuck valves
    stTelemetry.DIOS_[E_DIOIdentifier.VALV_CHK_LZ1].State :=
        _refDOSub.GetStateByIndex(nOutput := E_DigitalOutputIdentifier.VALV_CHK_LZ1);
    stTelemetry.DIOS_[E_DIOIdentifier.VALV_CHK_LZ2].State :=
        _refDOSub.GetStateByIndex(nOutput := E_DigitalOutputIdentifier.VALV_CHK_LZ2);
    stTelemetry.DIOS_[E_DIOIdentifier.VALV_CHK_LZ3].State :=
        _refDOSub.GetStateByIndex(nOutput := E_DigitalOutputIdentifier.VALV_CHK_LZ3);
    stTelemetry.DIOS_[E_DIOIdentifier.VALV_CHK_VENT].State :=
        _refDOSub.GetStateByIndex(nOutput := E_DigitalOutputIdentifier.VALV_CHK_VENT);

    // 23: HETR_VOLT_ON — DO
    stTelemetry.DIOS_[E_DIOIdentifier.HETR_VOLT_ON].State :=
        _refDISub.GetStateByIndex(nInput := E_DigitalInputIdentifier.HETR_VOLT_ON); 

    // Safety — DI
    stTelemetry.DIOS_[E_DIOIdentifier.SAFE_STO_ON].State :=
        _refDISub.GetStateByIndex(nInput := E_DigitalInputIdentifier.SAFE_STO_ON);
    stTelemetry.DIOS_[E_DIOIdentifier.SAFE_OVERRIDE].State := TRUE; // Always TRUE on Shark
END_METHOD


METHOD PROTECTED _UpdateSequenceStateTelemetry
VAR
    stSeqStatus : ST_SequenceStatus;
    ipSensor    : I_Sensor;
END_VAR
    // Read sequence state from PRG_Sequence via GetStatus() method
    stSeqStatus := PRG_Sequence.GetStatus();

    // Convert E_Sequence enum to INT for MachineState
    stTelemetry.STAT_[1].MachineState := stSeqStatus.eActiveSequence;

    // Progress is already a percentage (0-100)
    stTelemetry.STAT_[1].SubState := REAL_TO_INT(stSeqStatus.rProgress);
    stTelemetry.STAT_[1].SubStateCount := 100;

    // Error ID: non-zero if error state
    IF stSeqStatus.bError THEN
        stTelemetry.STAT_[1].ErrorID := UDINT_TO_INT(stSeqStatus.stLastError.eErrorId);
    ELSE
        stTelemetry.STAT_[1].ErrorID := 0;
    END_IF

    // Track last purged bubblers (A-side precursor, B-side reactant)
    stTelemetry.STAT_[1].LastPrecursorIndex := stSeqStatus.nLastPurgedBubblerA;
    stTelemetry.STAT_[1].LastReactantIndex  := stSeqStatus.nLastPurgedBubblerB;

    // A-side temperature (single monitor sensor — no heater)
    ipSensor := PRG_TEMPERATURE_SENSOR.aipMonitorSensors[E_TemperatureMonitorZone.SENS_A_SIDE];
    IF ipSensor <> 0 THEN
        stTelemetry.STAT_[1].TempAside := TO_REAL(ipSensor.GetMeasurement());
    ELSE
        stTelemetry.STAT_[1].TempAside := 0.0;
    END_IF

    // B-side temperature (single monitor sensor — no heater)
    ipSensor := PRG_TEMPERATURE_SENSOR.aipMonitorSensors[E_TemperatureMonitorZone.SENS_B_SIDE];
    IF ipSensor <> 0 THEN
        stTelemetry.STAT_[1].TempBside := TO_REAL(ipSensor.GetMeasurement());
    ELSE
        stTelemetry.STAT_[1].TempBside := 0.0;
    END_IF

    // Chamber temperature/humidity — on Shark the ambient TH sensor serves as the
    // chamber sensor. MACH_[1] is already populated by _UpdateMachineSpecific (called
    // one line earlier in FB_TelemetryPublisherBase), so simply alias it here.
    stTelemetry.STAT_[1].TempChamber := stTelemetry.MACH_[1].AmbTemperature;
    stTelemetry.STAT_[1].HumiChamber := stTelemetry.MACH_[1].AmbHumidity;

    // Sample hold state
    stTelemetry.STAT_[1].isSampleHold := (stSeqStatus.eActiveSequence = E_Sequence.ChuckVacuumHold);

    // Machine firmware version
    stTelemetry.STAT_[1].sVersion := GVL_Version.stLibVersion.sVersion;
END_METHOD


// Freeze stTelemetry into snapshot storage on a new fault edge.
// Called at most once per unique error — not every scan.
METHOD PROTECTED _OnErrorCapture
VAR_INPUT
    stError : ST_Error;
END_VAR
VAR
    sMsg : STRING(255);
END_VAR
    stSnapshotTelemetry := stTelemetry;
    stSnapshotError     := stError;
    bSnapshotValid      := TRUE;

    sMsg := CONCAT('snapshot frozen for ', TO_STRING(stError.eErrorId));
    sMsg := CONCAT(sMsg, CONCAT(' [', CONCAT(stError.sSource, ']')));
    GVL_ErrorLogger.Logger.LogEvent(
        sSource   := 'Shark.TelemetryPublisher',
        eEventId  := E_EventId.EVT_INFO,
        sContext  := sMsg,
        eSeverity := E_TcEventSeverity.Warning
    );
END_METHOD


// Collect Shark machine-specific errors (safety manager) not covered by
// the shared subsystems. Called only when all four shared subsystems report
// no active error.
METHOD PROTECTED _CollectMachineSpecificError : ST_Error
    IF PRG_SHARK.fbSafetyManager.IsError() THEN
        _CollectMachineSpecificError := PRG_SHARK.fbSafetyManager.GetError();
        RETURN;
    END_IF
    _CollectMachineSpecificError.eErrorId := E_ErrorId.NO_ERROR;
END_METHOD


// Clear bSnapshotValid on operator reset.
// stSnapshotTelemetry is intentionally preserved — it remains readable as
// a historical record; bSnapshotValid = FALSE signals it is stale.
METHOD PROTECTED _OnSnapshotReset
    bSnapshotValid := FALSE;
END_METHOD


// Write ambient temperature and humidity into MACH_[1].
// Temperature is read from PRG_TEMPERATURE_SENSOR (SENS_CHAMBER — the TH sensor temperature channel).
// Humidity is read from PRG_HUMIDITY_SENSOR (AMBT_HUMID — the TH sensor humidity channel).
// Called once per scan by FB_TelemetryPublisherBase via the _UpdateMachineSpecific hook.
METHOD PROTECTED _UpdateMachineSpecific
VAR
    ipSensor : I_Sensor;
END_VAR
    // Ambient temperature (TH sensor — temperature channel, wired as SENS_CHAMBER)
    ipSensor := PRG_TEMPERATURE_SENSOR.aipMonitorSensors[E_TemperatureMonitorZone.SENS_CHAMBER];
    IF ipSensor <> 0 THEN
        stTelemetry.MACH_[1].AmbTemperature := TO_REAL(ipSensor.GetMeasurement());
    ELSE
        stTelemetry.MACH_[1].AmbTemperature := 0.0;
    END_IF

    // Ambient humidity (TH sensor — humidity channel)
    ipSensor := PRG_HUMIDITY_SENSOR.ipSensor;
    IF ipSensor <> 0 THEN
        stTelemetry.MACH_[1].AmbHumidity := TO_REAL(ipSensor.GetMeasurement());
    ELSE
        stTelemetry.MACH_[1].AmbHumidity := 0.0;
    END_IF
END_METHOD
