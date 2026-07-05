(*
FB_CommandReceiver — Receives JSON commands from ADS byte stream and dispatches
to hardware provider, spectrometer, and sequence engine.

ADS Pattern:
  DALP Manager (.NET) writes JSON as raw bytes into AdsStream[0..4095].
  This FB polls every scan: if non-zero → parse JSON → dispatch → clear buffer.

Debug:
  Each command has a corresponding stDbgXxx struct and bDbgTrigXxx BOOL.
  Set struct fields and trigger from TwinCAT online to execute any command
  through the same code path as the Pilot.

Handles all command types from E_Command:
  Subsystem:    SetValve, SetMFCFlowRate, SetPressureController, SetHeaterZone,
                SetSwitch, ErrorAcknowledge
  Sequence:     SetTemperatureGradients, CancelProcess, StartBubblerFlow,
                StopBubblerFlow, StartPurge, HoldReleaseSample,
                StartSpotDeposition, StartBubblerLeakTest
  Spectrometer: SetSpectrometerData, SetSpectrometerDark

Requires Tc3_JsonXml library for FB_JsonDomParser + FB_JsonReadWriteDataType.
*)
FUNCTION_BLOCK FB_CommandReceiver
VAR
    AdsStream              : ARRAY[0..4095] OF BYTE;
    ZeroArray              : ARRAY[0..4095] OF BYTE;
    sCommand               : STRING(4096);
    sLastCommandParsed     : STRING(4096);
    SuccessCommandCount    : UDINT := 0;
    ErrorCommandCount      : UDINT := 0;

    fbJson                 : FB_JsonDomParser;
    jsonDoc                : Tc3_JsonXml.PVOID;
    jsonProp               : SJsonValue;
    fbJsonDataType         : FB_JsonReadWriteDataType;

    _nCommand              : DINT;
    _nCommandIndex         : DINT;

    _ipHardware            : I_HardwareProvider;
    _ipSpectrometer        : I_Spectrometer;
    _refOrchestrator       : REFERENCE TO FB_SequenceOrchestrator;
    _rTempDisableThreshold : REAL := 0.0;   // degC if any zone's setpoint is below this, disable all commands that could heat
    _bInitialized          : BOOL := FALSE;

    // Debug: set struct fields + trigger from TwinCAT online to test any command
    // Subsystem
    stDbgSetValve              : ST_CmdSetValve;
    bDbgTrigSetValve           : BOOL;
    stDbgSetFlowRate           : ST_CmdSetFlowRate;
    bDbgTrigSetFlowRate        : BOOL;
    stDbgSetPressure           : ST_CmdSetPressure;
    bDbgTrigSetPressure        : BOOL;
    stDbgSetHeatZone           : ST_CmdSetHeatZone;
    bDbgTrigSetHeatZone        : BOOL;
    stDbgSetSwitch             : ST_CmdSetSwitch;
    bDbgTrigSetSwitch          : BOOL;
    bDbgTrigErrorAck           : BOOL;
    // Sequence
    stDbgTempGradient          : ST_CmdSetTemperatureGradient;
    bDbgTrigTempGradient       : BOOL;
    bDbgTrigCancelProcess      : BOOL;
    stDbgBubblerFlow           : ST_CmdStartBubblerFlow;
    bDbgTrigBubblerFlow        : BOOL;
    stDbgStopBubblerFlow       : ST_CmdStopBubblerFlow;
    bDbgTrigStopBubblerFlow    : BOOL;
    stDbgPurge                 : ST_CmdPurge;
    bDbgTrigPurge              : BOOL;
    stDbgChuckZone             : ST_CmdSetChuckZone;
    bDbgTrigChuckZone          : BOOL;
    stDbgSpotDeposition        : ST_CmdSpotDeposition;
    bDbgTrigSpotDeposition     : BOOL;
    stDbgLeakTest              : ST_CmdBubblerLeakTest;
    bDbgTrigLeakTest           : BOOL;
    // Spectrometer
    stDbgSpectrometerData      : ST_DeviceSettings;
    bDbgTrigSpectrometerData   : BOOL;
    bDbgTrigSpectrometerDark   : BOOL;
END_VAR
VAR_OUTPUT
    nLastCommandIndex      : DINT;
END_VAR
VAR CONSTANT
    JSON_KEY_COMMAND             : STRING := 'command';
    JSON_KEY_INDEX               : STRING := 'index';
    JSON_KEY_PARAMETERS          : STRING := 'parameters';
    JSON_PARAM_BUFFER_SIZE       : UDINT  := 2048;
    // Minimum flow rate above which the inert gas inlet valve is opened.
    // At or below this threshold the valve is closed.
    INERT_FLOW_VALVE_THRESHOLD   : REAL   := 0.001;
END_VAR

    IF NOT _bInitialized THEN
        RETURN;
    END_IF

    IF MEMCMP(ADR(AdsStream), ADR(ZeroArray), SIZEOF(AdsStream)) <> 0 THEN
        sCommand := _ByteArrToString(ADR(AdsStream), SIZEOF(AdsStream));
        IF _ParseAndDispatch() THEN
            SuccessCommandCount := SuccessCommandCount + 1;
        ELSE
            ErrorCommandCount := ErrorCommandCount + 1;
        END_IF
        MEMSET(ADR(AdsStream), 0, SIZEOF(AdsStream));
        sLastCommandParsed := sCommand;
    END_IF

    // Debug triggers (set params + trigger from TwinCAT online)
    _ProcessDebugTriggers();


METHOD PUBLIC Init : BOOL
VAR_INPUT
    ipHardware     : I_HardwareProvider;
    ipSpectrometer : I_Spectrometer;
    rTempDisableThreshold : REAL;
    refOrchestrator : REFERENCE TO FB_SequenceOrchestrator;
END_VAR
    IF ipHardware = 0 OR ipSpectrometer = 0 THEN
        GVL_ErrorLogger.Logger.LogError(
            sSource  := 'CommandReceiver.Init',
            eErrorId := E_ErrorId.ERR_GEN_NULL_POINTER,
            sContext := 'invalid interface'
        );
        RETURN;
    END_IF
    IF NOT __ISVALIDREF(refOrchestrator) THEN
        GVL_ErrorLogger.Logger.LogError(
            sSource  := 'CommandReceiver.Init',
            eErrorId := E_ErrorId.ERR_GEN_NULL_POINTER,
            sContext := 'invalid orchestrator reference'
        );
        RETURN;
    END_IF
    _ipHardware            := ipHardware;
    _ipSpectrometer        := ipSpectrometer;
    _rTempDisableThreshold := rTempDisableThreshold;
    _refOrchestrator       REF= refOrchestrator;
    _bInitialized          := TRUE;
    Init := TRUE;
END_METHOD


METHOD PRIVATE _ParseAndDispatch : BOOL
VAR
    nError         : INT := 0;
    stSetValve     : ST_CmdSetValve;
    stSetFlowRate  : ST_CmdSetFlowRate;
    stSetPressure  : ST_CmdSetPressure;
    stSetHeatZone  : ST_CmdSetHeatZone;
    stSetHeatChannel : ST_CmdSetHeatChannel;
    stSetSwitch    : ST_CmdSetSwitch;
    stTempGrad     : ST_CmdSetTemperatureGradient;
    stBubblerFlow  : ST_CmdStartBubblerFlow;
    stStopBubbler  : ST_CmdStopBubblerFlow;
    stPurge        : ST_CmdPurge;
    stChuckZone    : ST_CmdSetChuckZone;
    stSpotDep      : ST_CmdSpotDeposition;
    stLeakTest     : ST_CmdBubblerLeakTest;
    stSpecData     : ST_DeviceSettings;
    eCmd           : E_Command;
END_VAR
    jsonDoc := fbJson.ParseDocument(sCommand);

    jsonProp := fbJson.FindMember(jsonDoc, JSON_KEY_COMMAND);
    IF jsonProp <> 0 THEN
        _nCommand := fbJson.GetInt(jsonProp);
    ELSE
        GVL_ErrorLogger.Logger.LogError(
            sSource   := 'CommandReceiver',
            eErrorId  := E_ErrorId.ERR_GEN_INVALID_PARAM,
            sContext  := 'missing command field in JSON',
            eSeverity := E_TcEventSeverity.Warning
        );
        _ParseAndDispatch := FALSE;
        RETURN;
    END_IF

    jsonProp := fbJson.FindMember(jsonDoc, JSON_KEY_INDEX);
    IF jsonProp <> 0 THEN
        _nCommandIndex := fbJson.GetInt(jsonProp);
    END_IF

    eCmd := DINT_TO_INT(_nCommand);
    CASE eCmd OF
        E_Command.SetValve:
            IF _ParseJsonParams('ST_CmdSetValve', SIZEOF(stSetValve), ADR(stSetValve)) THEN
                nError := _CmdSetValve(stCmd := stSetValve);
            ELSE nError := -1; END_IF
        E_Command.SetMFCFlowRate:
            IF _ParseJsonParams('ST_CmdSetFlowRate', SIZEOF(stSetFlowRate), ADR(stSetFlowRate)) THEN
                nError := _CmdSetMFCFlowRate(stCmd := stSetFlowRate);
            ELSE nError := -1; END_IF
        E_Command.SetPressureController:
            IF _ParseJsonParams('ST_CmdSetPressure', SIZEOF(stSetPressure), ADR(stSetPressure)) THEN
                nError := _CmdSetPressure(stCmd := stSetPressure);
            ELSE nError := -1; END_IF
        E_Command.SetHeaterZone:
            IF _ParseJsonParams('ST_CmdSetHeatZone', SIZEOF(stSetHeatZone), ADR(stSetHeatZone)) THEN
                nError := _CmdSetHeatZone(stCmd := stSetHeatZone);
            ELSE nError := -1; END_IF
        E_Command.SetHeaterChannel:
            IF _ParseJsonParams('ST_CmdSetHeatChannel', SIZEOF(stSetHeatChannel), ADR(stSetHeatChannel)) THEN
                nError := _CmdSetHeatChannel(stCmd := stSetHeatChannel);
            ELSE nError := -1; END_IF
        E_Command.SetSwitch:
            IF _ParseJsonParams('ST_CmdSetSwitch', SIZEOF(stSetSwitch), ADR(stSetSwitch)) THEN
                nError := _CmdSetSwitch(stCmd := stSetSwitch);
            ELSE nError := -1; END_IF
        E_Command.ErrorAcknowledge:
            nError := _CmdErrorAcknowledge();
        E_Command.SetTemperatureGradients:
            IF _ParseJsonParams('ST_CmdSetTemperatureGradient', SIZEOF(stTempGrad), ADR(stTempGrad)) THEN
                nError := _CmdSetTemperatureGradient(stCmd := stTempGrad);
            ELSE nError := -1; END_IF
        E_Command.CancelProcess:
            nError := _CmdCancelProcess();
        E_Command.StartBubblerFlow:
            IF _ParseJsonParams('ST_CmdStartBubblerFlow', SIZEOF(stBubblerFlow), ADR(stBubblerFlow)) THEN
                nError := _CmdStartBubblerFlow(stCmd := stBubblerFlow);
            ELSE nError := -1; END_IF
        E_Command.StopBubblerFlow:
            IF _ParseJsonParams('ST_CmdStopBubblerFlow', SIZEOF(stStopBubbler), ADR(stStopBubbler)) THEN
                nError := _CmdStopBubblerFlow(stCmd := stStopBubbler);
            ELSE nError := -1; END_IF
        E_Command.StartPurge:
            IF _ParseJsonParams('ST_CmdPurge', SIZEOF(stPurge), ADR(stPurge)) THEN
                nError := _CmdStartPurge(stCmd := stPurge);
            ELSE nError := -1; END_IF
        E_Command.HoldReleaseSample:
            IF _ParseJsonParams('ST_CmdSetChuckZone', SIZEOF(stChuckZone), ADR(stChuckZone)) THEN
                nError := _CmdHoldReleaseSample(stCmd := stChuckZone);
            ELSE nError := -1; END_IF
        E_Command.StartSpotDeposition:
            IF _ParseJsonParams('ST_CmdSpotDeposition', SIZEOF(stSpotDep), ADR(stSpotDep)) THEN
                nError := _CmdStartSpotDeposition(stCmd := stSpotDep);
            ELSE nError := -1; END_IF
        E_Command.StartBubblerLeakTest:
            IF _ParseJsonParams('ST_CmdBubblerLeakTest', SIZEOF(stLeakTest), ADR(stLeakTest)) THEN
                nError := _CmdStartBubblerLeakTest(stCmd := stLeakTest);
            ELSE nError := -1; END_IF
        E_Command.SetSpectrometerData:
            IF _ParseJsonParams('ST_DeviceSettings', SIZEOF(stSpecData), ADR(stSpecData)) THEN
                nError := _CmdSetSpectrometerData(stSettings := stSpecData);
            ELSE nError := -1; END_IF
        E_Command.SetSpectrometerDark:
            nError := _CmdSetSpectrometerDark();
    ELSE
        GVL_ErrorLogger.Logger.LogError(
            sSource   := 'CommandReceiver',
            eErrorId  := E_ErrorId.ERR_GEN_INVALID_PARAM,
            sContext  := CONCAT('unknown command ', CONCAT(TO_STRING(eCmd), CONCAT(' (', CONCAT(DINT_TO_STRING(_nCommand), ')')))),
            eSeverity := E_TcEventSeverity.Warning
        );
        _ParseAndDispatch := FALSE;
        RETURN;
    END_CASE

    IF nError = 0 THEN
        nLastCommandIndex := _nCommandIndex;
        _ParseAndDispatch := TRUE;
    ELSE
        GVL_ErrorLogger.Logger.LogError(
            sSource   := 'CommandReceiver',
            eErrorId  := E_ErrorId.ERR_GEN_INTERNAL,
            sContext  := CONCAT('command failed ', CONCAT(TO_STRING(eCmd), CONCAT(' (', CONCAT(DINT_TO_STRING(_nCommand), ')')))),
            eSeverity := E_TcEventSeverity.Warning
        );
        _ParseAndDispatch := FALSE;
    END_IF
END_METHOD


METHOD PRIVATE _ParseJsonParams : BOOL
VAR_INPUT
    sTypeName : STRING;
    nSize     : UDINT;
    pDest     : PVOID;
END_VAR
VAR
    sParamJson : STRING(JSON_PARAM_BUFFER_SIZE);
    nLen       : UDINT;
END_VAR
    jsonProp := fbJson.FindMember(jsonDoc, JSON_KEY_PARAMETERS);
    IF jsonProp = 0 THEN
        // No 'parameters' wrapper — fall back to using the full JSON directly
        sParamJson := sCommand;
    ELSE
        nLen := fbJson.GetJsonLength(jsonProp);
        fbJson.CopyJson(jsonProp, sParamJson, nLen);
    END_IF

    IF NOT fbJsonDataType.SetSymbolFromJson(sParamJson, sTypeName, nSize, pDest) THEN
        GVL_ErrorLogger.Logger.LogError(
            sSource   := 'CommandReceiver',
            eErrorId  := E_ErrorId.ERR_GEN_INVALID_PARAM,
            sContext  := CONCAT('failed to parse ', sTypeName),
            eSeverity := E_TcEventSeverity.Warning
        );
        _ParseJsonParams := FALSE;
        RETURN;
    END_IF
    _ParseJsonParams := TRUE;
END_METHOD


// =============================================================================
// COMMAND HANDLERS — Pure execution, no JSON parsing
// =============================================================================

METHOD PRIVATE _CmdSetValve : INT
VAR_INPUT
    stCmd : ST_CmdSetValve;
END_VAR
VAR
    ipValve : I_Valve;
    eValve  : E_ValveIdentifier;
END_VAR
    eValve := stCmd.valveId;
    GVL_ErrorLogger.Logger.LogInfo(
        sSource  := 'CommandReceiver',
        sContext := CONCAT('SetValve(', CONCAT(DINT_TO_STRING(_nCommand), CONCAT('): ', CONCAT(TO_STRING(eValve), CONCAT('(', CONCAT(INT_TO_STRING(stCmd.valveId), CONCAT(') enabled=', BOOL_TO_STRING(stCmd.enabled))))))))
    );
    ipValve := _ipHardware.GetValve(stCmd.valveId);
    IF ipValve <> 0 THEN
        IF stCmd.enabled THEN
            ipValve.Open();
        ELSE
            ipValve.Close();
        END_IF
    ELSE
        _CmdSetValve := -1;
    END_IF
END_METHOD


METHOD PRIVATE _CmdSetMFCFlowRate : INT
VAR_INPUT
    stCmd : ST_CmdSetFlowRate;
END_VAR
VAR
    ipController : I_Controller;
    ipInertValve : I_Valve;
    eMFC         : E_MFCIdentifier;
END_VAR
    eMFC := stCmd.mfcId;
    GVL_ErrorLogger.Logger.LogInfo(
        sSource  := 'CommandReceiver',
        sContext := CONCAT('SetMFCFlowRate(', CONCAT(DINT_TO_STRING(_nCommand), CONCAT('): ', CONCAT(TO_STRING(eMFC), CONCAT('(', CONCAT(INT_TO_STRING(stCmd.mfcId), CONCAT(') flowRate=', REAL_TO_STRING(stCmd.flowRate))))))))
    );
    ipController := _ipHardware.GetFlowController(stCmd.mfcId);
    IF ipController <> 0 THEN
        ipController.SetSetpoint(stCmd.flowRate);
        // When the inert MFC (MFCT_IG_01) is commanded, also control its inlet valve.
        IF eMFC = E_MFCIdentifier.MFCT_IG_01 THEN
            ipInertValve := _ipHardware.GetInertGasValve();
            IF ipInertValve <> 0 THEN
                IF stCmd.flowRate > INERT_FLOW_VALVE_THRESHOLD THEN
                    ipInertValve.Open();
                ELSE
                    ipInertValve.Close();
                END_IF
            END_IF
        END_IF
    ELSE
        _CmdSetMFCFlowRate := -1;
    END_IF
END_METHOD


METHOD PRIVATE _CmdSetPressure : INT
VAR_INPUT
    stCmd : ST_CmdSetPressure;
END_VAR
VAR
    ipController : I_Controller;
    ePressure    : E_PressureController;
END_VAR
    ePressure := stCmd.psctrlId;
    GVL_ErrorLogger.Logger.LogInfo(
        sSource  := 'CommandReceiver',
        sContext := CONCAT('SetPressureController(', CONCAT(DINT_TO_STRING(_nCommand), CONCAT('): ', CONCAT(TO_STRING(ePressure), CONCAT('(', CONCAT(INT_TO_STRING(stCmd.psctrlId), CONCAT(') pressure=', REAL_TO_STRING(stCmd.pressure))))))))
    );
    ipController := _ipHardware.GetPressureControllerById(stCmd.psctrlId);
    IF ipController <> 0 THEN
        ipController.SetSetpoint(stCmd.pressure);
    ELSE
        _CmdSetPressure := -1;
    END_IF
END_METHOD


METHOD PRIVATE _CmdSetHeatZone : INT
VAR_INPUT
    stCmd : ST_CmdSetHeatZone;
END_VAR
VAR
    ipController : I_Controller;
    eLegacyZone  : E_HeatZones;   // per-channel id sent by the DALP/Pilot client (1..15)
    eZone        : E_HeatZone;    // internal controllable zone id (1..13)
END_VAR
    // The legacy DALP/Pilot client addresses heater CHANNELS via E_HeatZones
    // (1..15), but SetHeaterZone operates on ZONES via E_HeatZone (1..13).
    // Reject any id that is not a defined E_HeatZones member, then translate.
    eLegacyZone := stCmd.zoneId;
    IF NOT _LegacyChannelToZone(eLegacy := eLegacyZone, eZone => eZone) THEN
        GVL_ErrorLogger.Logger.LogError(
            sSource   := 'CommandReceiver.SetHeatZone',
            eErrorId  := E_ErrorId.ERR_GEN_INVALID_PARAM,
            sContext  := CONCAT('invalid zoneId ', INT_TO_STRING(stCmd.zoneId)),
            eSeverity := E_TcEventSeverity.Warning
        );
        _CmdSetHeatZone := -1;
        RETURN;
    END_IF

    GVL_ErrorLogger.Logger.LogInfo(
        sSource  := 'CommandReceiver',
        sContext := CONCAT('SetHeatZone(', CONCAT(DINT_TO_STRING(_nCommand), CONCAT('): ', CONCAT(TO_STRING(eLegacyZone), CONCAT(' -> ', CONCAT(TO_STRING(eZone), CONCAT(' temp=', REAL_TO_STRING(stCmd.temp))))))))
    );

    ipController := _ipHardware.GetTemperatureController(eZone);
    IF ipController <> 0 THEN
        IF stCmd.temp > _rTempDisableThreshold THEN
            ipController.SetSetpoint(stCmd.temp);
            ipController.Enable(TRUE);
        ELSE
            ipController.Enable(FALSE);
        END_IF
    ELSE
        _CmdSetHeatZone := -1;
    END_IF
END_METHOD


(* Translate the legacy per-CHANNEL id (E_HeatZones, 1..15 — what the DALP/Pilot
   client sends) into the internal controllable ZONE id (E_HeatZone, 1..13).
   The 11 single-channel zones map 1:1. The two 2-channel zones each have two
   legacy channel ids that both resolve to the single owning zone:
       HETR_EXH_01 / HETR_EXH_02 -> HETR_EXH
       HETR_CHK_01 / HETR_CHK_02 -> HETR_CHK
   Commanding either channel id therefore enables the whole zone (FB_HeatZone
   fans Enable() out to every channel it owns).
   Returns TRUE and sets eZone on success; returns FALSE (leaving eZone
   untouched) for any value that is not a defined E_HeatZones member, so the
   caller can fail the command instead of acting on a wrong zone. *)
METHOD PRIVATE _LegacyChannelToZone : BOOL
VAR_INPUT
    eLegacy : E_HeatZones;
END_VAR
VAR_OUTPUT
    eZone : E_HeatZone;
END_VAR
    _LegacyChannelToZone := TRUE;
    CASE eLegacy OF
        E_HeatZones.HETR_HEAD_01 : eZone := E_HeatZone.HETR_HEAD;
        E_HeatZones.HETR_A_STR_01: eZone := E_HeatZone.HETR_A_STR;
        E_HeatZones.HETR_A_CMB_01: eZone := E_HeatZone.HETR_A_CMB;
        E_HeatZones.HETR_A_B1_01 : eZone := E_HeatZone.HETR_A_B1;
        E_HeatZones.HETR_A_B2_01 : eZone := E_HeatZone.HETR_A_B2;
        E_HeatZones.HETR_A_B3_01 : eZone := E_HeatZone.HETR_A_B3;
        E_HeatZones.HETR_B_STR_01: eZone := E_HeatZone.HETR_B_STR;
        E_HeatZones.HETR_B_CMB_01: eZone := E_HeatZone.HETR_B_CMB;
        E_HeatZones.HETR_B_B1_01 : eZone := E_HeatZone.HETR_B_B1;
        E_HeatZones.HETR_B_B2_01 : eZone := E_HeatZone.HETR_B_B2;
        E_HeatZones.HETR_B_B3_01 : eZone := E_HeatZone.HETR_B_B3;
        E_HeatZones.HETR_EXH_01,
        E_HeatZones.HETR_EXH_02  : eZone := E_HeatZone.HETR_EXH;
        E_HeatZones.HETR_CHK_01,
        E_HeatZones.HETR_CHK_02  : eZone := E_HeatZone.HETR_CHK;
    ELSE
        // Unknown id — not a defined E_HeatZones member. Fail; do not guess.
        _LegacyChannelToZone := FALSE;
    END_CASE
END_METHOD


METHOD PRIVATE _CmdSetHeatChannel : INT
VAR_INPUT
    stCmd : ST_CmdSetHeatChannel;
END_VAR
VAR
    ipController : I_Controller;
    eChannel     : E_HeatChannel;
END_VAR
    eChannel := stCmd.channelId;
    GVL_ErrorLogger.Logger.LogInfo(
        sSource  := 'CommandReceiver',
        sContext := CONCAT('SetHeatChannel(', CONCAT(DINT_TO_STRING(_nCommand), CONCAT('): ', CONCAT(TO_STRING(eChannel), CONCAT('(', CONCAT(INT_TO_STRING(stCmd.channelId), CONCAT(') temp=', REAL_TO_STRING(stCmd.temp))))))))
    );
    ipController := _ipHardware.GetTemperatureChannel(stCmd.channelId);
    IF ipController <> 0 THEN
        IF stCmd.temp > _rTempDisableThreshold THEN
            ipController.SetSetpoint(stCmd.temp);
            ipController.Enable(TRUE);
        ELSE
            ipController.Enable(FALSE);
        END_IF
    ELSE
        _CmdSetHeatChannel := -1;
    END_IF
END_METHOD


METHOD PRIVATE _CmdSetSwitch : INT
VAR_INPUT
    stCmd : ST_CmdSetSwitch;
END_VAR
VAR
    ipOutput : I_DigitalOutput;
    eDIO     : E_DIOIdentifier;
    eOutput  : E_DigitalOutputIdentifier;
END_VAR
    // switchId is sent by DALP Manager as an E_DIOIdentifier value (telemetry index 1..25).
    // Translate to E_DigitalOutputIdentifier before addressing the hardware.
    eDIO := stCmd.switchId;
    GVL_ErrorLogger.Logger.LogInfo(
        sSource  := 'CommandReceiver',
        sContext := CONCAT('SetSwitch(', CONCAT(DINT_TO_STRING(_nCommand), CONCAT('): ', CONCAT(TO_STRING(eDIO), CONCAT('(', CONCAT(INT_TO_STRING(stCmd.switchId), CONCAT(') enabled=', BOOL_TO_STRING(stCmd.enabled))))))))
    );

    CASE eDIO OF
        E_DIOIdentifier.LGHT_CMR_SIDE : eOutput := E_DigitalOutputIdentifier.LGHT_CMR_SIDE;
        E_DIOIdentifier.LGHT_CMR_TOP  : eOutput := E_DigitalOutputIdentifier.LGHT_CMR_TOP;
        E_DIOIdentifier.CHIL_PWR      : eOutput := E_DigitalOutputIdentifier.CHIL_PWR;
        // LGHT_SGN_RED, LGHT_SGN_YEL, LGHT_SGN_GRN are owned by FB_SignalLightController — not manually settable.
        E_DIOIdentifier.LOCK_DOOR_FL  : eOutput := E_DigitalOutputIdentifier.LOCK_DOOR_FL;
        E_DIOIdentifier.LOCK_DOOR_FR  : eOutput := E_DigitalOutputIdentifier.LOCK_DOOR_FR;
        E_DIOIdentifier.PUMP_CHK_VAC  : eOutput := E_DigitalOutputIdentifier.PUMP_CHK_VAC;
        E_DIOIdentifier.VALV_CHK_LZ1  : eOutput := E_DigitalOutputIdentifier.VALV_CHK_LZ1;
        E_DIOIdentifier.VALV_CHK_LZ2  : eOutput := E_DigitalOutputIdentifier.VALV_CHK_LZ2;
        E_DIOIdentifier.VALV_CHK_LZ3  : eOutput := E_DigitalOutputIdentifier.VALV_CHK_LZ3;
        E_DIOIdentifier.VALV_CHK_VENT : eOutput := E_DigitalOutputIdentifier.VALV_CHK_VENT;
    ELSE
        // DIO indices 9-17 are digital inputs (PRXS_DOOR_*) — not writable outputs.
        // DIO indices 24-25 are safety inputs (SAFE_STO_ON, SAFE_OVERRIDE) — not writable.
        GVL_ErrorLogger.Logger.LogError(
            sSource   := 'CommandReceiver.SetSwitch',
            eErrorId  := E_ErrorId.ERR_GEN_INVALID_PARAM,
            sContext  := CONCAT('DIO index ', CONCAT(TO_STRING(eDIO), ' is not a writable digital output')),
            eSeverity := E_TcEventSeverity.Warning
        );
        _CmdSetSwitch := -1;
        RETURN;
    END_CASE

    ipOutput := _ipHardware.GetDigitalOutput(eOutput);
    IF ipOutput <> 0 THEN
        ipOutput.SetOutput(stCmd.enabled);
    ELSE
        _CmdSetSwitch := -1;
    END_IF
END_METHOD


METHOD PRIVATE _CmdSetTemperatureGradient : INT
VAR_INPUT
    stCmd : ST_CmdSetTemperatureGradient;
END_VAR
    GVL_ErrorLogger.Logger.LogInfo(
        sSource  := 'CommandReceiver',
        sContext := CONCAT('SetTemperatureGradients: mode=', CONCAT(INT_TO_STRING(stCmd.enableMode), CONCAT(' head=', REAL_TO_STRING(stCmd.headTemp))))
    );
    IF NOT _refOrchestrator.StartHeating(stParams := stCmd) THEN
        _CmdSetTemperatureGradient := -1;
    END_IF
END_METHOD


METHOD PRIVATE _CmdErrorAcknowledge : INT
    GVL_System.bGlobalEmergencyStop := FALSE;
    _refOrchestrator.ErrorAcknowledge();
    GVL_ErrorLogger.Logger.LogInfo(
        sSource  := 'CommandReceiver',
        sContext := 'ErrorAcknowledge received'
    );
END_METHOD


METHOD PRIVATE _CmdCancelProcess : INT
    GVL_ErrorLogger.Logger.LogInfo(
        sSource  := 'CommandReceiver',
        sContext := 'CancelProcess'
    );
    IF NOT _refOrchestrator.SystemStop() THEN
        _CmdCancelProcess := -1;
    END_IF
END_METHOD


METHOD PRIVATE _CmdStartBubblerFlow : INT
VAR_INPUT
    stCmd : ST_CmdStartBubblerFlow;
END_VAR
VAR
    eSide : E_GasSide;
END_VAR
    eSide := stCmd.side;
    GVL_ErrorLogger.Logger.LogInfo(
        sSource  := 'CommandReceiver',
        sContext := CONCAT('StartBubblerFlow(', CONCAT(DINT_TO_STRING(_nCommand), CONCAT('): side=', CONCAT(TO_STRING(eSide), CONCAT('(', CONCAT(INT_TO_STRING(stCmd.side), CONCAT(') flow=', CONCAT(REAL_TO_STRING(stCmd.flowRate), CONCAT(' bubbler=', INT_TO_STRING(stCmd.bubblerIndex))))))))))
    );
    IF NOT _refOrchestrator.StartBubblerFlow(stParams := stCmd) THEN
        _CmdStartBubblerFlow := -1;
    END_IF
END_METHOD


METHOD PRIVATE _CmdStopBubblerFlow : INT
VAR_INPUT
    stCmd : ST_CmdStopBubblerFlow;
END_VAR
VAR
    eSide : E_GasSide;
END_VAR
    eSide := stCmd.side;
    GVL_ErrorLogger.Logger.LogInfo(
        sSource  := 'CommandReceiver',
        sContext := CONCAT('StopBubblerFlow(', CONCAT(DINT_TO_STRING(_nCommand), CONCAT('): side=', CONCAT(TO_STRING(eSide), CONCAT('(', CONCAT(INT_TO_STRING(stCmd.side), ')'))))))
    );
    IF NOT _refOrchestrator.StopBubblerFlow(stParams := stCmd) THEN
        _CmdStopBubblerFlow := -1;
    END_IF
END_METHOD


METHOD PRIVATE _CmdStartPurge : INT
VAR_INPUT
    stCmd : ST_CmdPurge;
END_VAR
VAR
    eMode : E_PurgeMode;
    eSide : E_GasSide;
END_VAR
    eMode := stCmd.mode;
    eSide := stCmd.side;
    GVL_ErrorLogger.Logger.LogInfo(
        sSource  := 'CommandReceiver',
        sContext := CONCAT('StartPurge(', CONCAT(DINT_TO_STRING(_nCommand), CONCAT('): ', CONCAT(TO_STRING(eMode), CONCAT('(', CONCAT(INT_TO_STRING(stCmd.mode), CONCAT(') side=', CONCAT(TO_STRING(eSide), CONCAT('(', CONCAT(INT_TO_STRING(stCmd.side), CONCAT(') bubbler=', INT_TO_STRING(stCmd.bubbler))))))))))))
    );
    IF NOT _refOrchestrator.StartPurge(stParams := stCmd) THEN
        _CmdStartPurge := -1;
    END_IF
END_METHOD


METHOD PRIVATE _CmdHoldReleaseSample : INT
VAR_INPUT
    stCmd     : ST_CmdSetChuckZone;
END_VAR
VAR
    iZone     : INT;
    stHold    : ST_ChuckVacuumHold;
    stRelease : ST_ChuckVacuumRelease;
END_VAR
    GVL_ErrorLogger.Logger.LogInfo(
        sSource  := 'CommandReceiver',
        sContext := CONCAT('HoldReleaseSample: hold=', CONCAT(BOOL_TO_STRING(stCmd.holdSample), CONCAT(' waferSize=', INT_TO_STRING(stCmd.size))))
    );
    IF stCmd.size < GVL_ChuckVacuumSystem.CHUCK_ZONE_SMALL_MAX THEN
        iZone := 1;
    ELSIF stCmd.size < GVL_ChuckVacuumSystem.CHUCK_ZONE_MEDIUM_MAX THEN
        iZone := 2;
    ELSE
        iZone := 3;
    END_IF

    IF stCmd.holdSample THEN
        stHold.iSelectZone := iZone;
        stHold.fLockPressureThreshold := stCmd.fPressureThreshold;
        stHold.tPressureTimeout := DINT_TO_TIME(stCmd.tPressureTimeout);
        IF NOT _refOrchestrator.StartChuckVacuumHold(stParams := stHold) THEN
            _CmdHoldReleaseSample := -1;
        END_IF
    ELSE
        stRelease.fReleasePressureThreshold := stCmd.fPressureThreshold;
        stRelease.tPressureTimeout := DINT_TO_TIME(stCmd.tPressureTimeout);
        IF NOT _refOrchestrator.StartChuckVacuumRelease(stParams := stRelease) THEN
            _CmdHoldReleaseSample := -1;
        END_IF
    END_IF
END_METHOD


METHOD PRIVATE _CmdStartSpotDeposition : INT
VAR_INPUT
    stCmd : ST_CmdSpotDeposition;
END_VAR
    GVL_ErrorLogger.Logger.LogInfo(
        sSource  := 'CommandReceiver',
        sContext := CONCAT('StartSpotDeposition: cycles=', CONCAT(DINT_TO_STRING(stCmd.nDepositionCycles), CONCAT(' precFlow=', REAL_TO_STRING(stCmd.fPrecursorFlowRate))))
    );
    IF NOT _refOrchestrator.StartSpotDeposition(stParams := stCmd) THEN
        _CmdStartSpotDeposition := -1;
    END_IF
END_METHOD


METHOD PRIVATE _CmdStartBubblerLeakTest : INT
VAR_INPUT
    stCmd : ST_CmdBubblerLeakTest;
END_VAR
VAR
    eSide : E_GasSide;
END_VAR
    eSide := stCmd.side;
    GVL_ErrorLogger.Logger.LogInfo(
        sSource  := 'CommandReceiver',
        sContext := CONCAT('StartBubblerLeakTest(', CONCAT(DINT_TO_STRING(_nCommand), CONCAT('): side=', CONCAT(TO_STRING(eSide), CONCAT('(', CONCAT(INT_TO_STRING(stCmd.side), CONCAT(') bubbler=', DINT_TO_STRING(stCmd.bubblerIndex))))))))
    );
    IF NOT _refOrchestrator.StartBubblerLeakTest(stParams := stCmd) THEN
        _CmdStartBubblerLeakTest := -1;
    END_IF
END_METHOD


METHOD PRIVATE _CmdSetSpectrometerData : INT
VAR_INPUT
    stSettings : ST_DeviceSettings;
END_VAR
    GVL_ErrorLogger.Logger.LogInfo(
        sSource  := 'CommandReceiver',
        sContext := CONCAT('SetSpectrometerData: scanRate=', CONCAT(UDINT_TO_STRING(stSettings.ScanRate), CONCAT(' avg=', UDINT_TO_STRING(stSettings.SpectrumAverage))))
    );
    _ipSpectrometer.SetFrequency(stSettings.ScanRate);
    _ipSpectrometer.SetLightIntensity(
        TargetLight := stSettings.LightTargetPercentage,
        AutoAdjust  := stSettings.IntensityAutoAdjust
    );
    _ipSpectrometer.SetAverageSample(stSettings.SpectrumAverage);
    _ipSpectrometer.SetSpectrumLimits(
        nStartRatio  := stSettings.SpectrumStart,
        nStopRatio   := stSettings.SpectrumStop,
        nTotalPixels := INT_TO_UDINT(GVL_Spectrometer.SPECTROMETER_PIXEL_COUNT)
    );
    _ipSpectrometer.SetQualityTreshold(stSettings.QualityThresholdPercentage);
END_METHOD


METHOD PRIVATE _CmdSetSpectrometerDark : INT
    GVL_ErrorLogger.Logger.LogInfo(
        sSource  := 'CommandReceiver',
        sContext := 'SetSpectrometerDark'
    );
    _ipSpectrometer.CalibrateDark(bExecute := TRUE);
END_METHOD


// =============================================================================
// DEBUG TRIGGERS — TwinCAT online: set struct fields, set trigger BOOL
// =============================================================================

METHOD PRIVATE _ProcessDebugTriggers
    // Subsystem
    IF bDbgTrigSetValve THEN
        bDbgTrigSetValve := FALSE;
        _CmdSetValve(stCmd := stDbgSetValve);
    END_IF
    IF bDbgTrigSetFlowRate THEN
        bDbgTrigSetFlowRate := FALSE;
        _CmdSetMFCFlowRate(stCmd := stDbgSetFlowRate);
    END_IF
    IF bDbgTrigSetPressure THEN
        bDbgTrigSetPressure := FALSE;
        _CmdSetPressure(stCmd := stDbgSetPressure);
    END_IF
    IF bDbgTrigSetHeatZone THEN
        bDbgTrigSetHeatZone := FALSE;
        _CmdSetHeatZone(stCmd := stDbgSetHeatZone);
    END_IF
    IF bDbgTrigSetSwitch THEN
        bDbgTrigSetSwitch := FALSE;
        _CmdSetSwitch(stCmd := stDbgSetSwitch);
    END_IF
    IF bDbgTrigErrorAck THEN
        bDbgTrigErrorAck := FALSE;
        _CmdErrorAcknowledge();
    END_IF

    // Sequence
    IF bDbgTrigTempGradient THEN
        bDbgTrigTempGradient := FALSE;
        _CmdSetTemperatureGradient(stCmd := stDbgTempGradient);
    END_IF
    IF bDbgTrigCancelProcess THEN
        bDbgTrigCancelProcess := FALSE;
        _CmdCancelProcess();
    END_IF
    IF bDbgTrigBubblerFlow THEN
        bDbgTrigBubblerFlow := FALSE;
        _CmdStartBubblerFlow(stCmd := stDbgBubblerFlow);
    END_IF
    IF bDbgTrigStopBubblerFlow THEN
        bDbgTrigStopBubblerFlow := FALSE;
        _CmdStopBubblerFlow(stCmd := stDbgStopBubblerFlow);
    END_IF
    IF bDbgTrigPurge THEN
        bDbgTrigPurge := FALSE;
        _CmdStartPurge(stCmd := stDbgPurge);
    END_IF
    IF bDbgTrigChuckZone THEN
        bDbgTrigChuckZone := FALSE;
        _CmdHoldReleaseSample(stCmd := stDbgChuckZone);
    END_IF
    IF bDbgTrigSpotDeposition THEN
        bDbgTrigSpotDeposition := FALSE;
        _CmdStartSpotDeposition(stCmd := stDbgSpotDeposition);
    END_IF
    IF bDbgTrigLeakTest THEN
        bDbgTrigLeakTest := FALSE;
        _CmdStartBubblerLeakTest(stCmd := stDbgLeakTest);
    END_IF

    // Spectrometer
    IF bDbgTrigSpectrometerData THEN
        bDbgTrigSpectrometerData := FALSE;
        _CmdSetSpectrometerData(stSettings := stDbgSpectrometerData);
    END_IF
    IF bDbgTrigSpectrometerDark THEN
        bDbgTrigSpectrometerDark := FALSE;
        _CmdSetSpectrometerDark();
    END_IF
END_METHOD


METHOD PRIVATE _ByteArrToString : STRING(4096)
VAR_INPUT
    pbyBuf : POINTER TO BYTE;
    diLen  : DINT;
END_VAR
VAR
    sResult  : STRING(4096) := '';
    nCopyLen : DINT;
END_VAR
    nCopyLen := MIN(diLen, 4095);
    MEMCPY(ADR(sResult), pbyBuf, TO_UDINT(nCopyLen));
    _ByteArrToString := sResult;
END_METHOD

END_FUNCTION_BLOCK
