(*
FB_MaintenancePurgeUmbilicalSequence
Purges the umbilical region (PCV->Nozzle) by pushing inert gas to atmosphere.
Flow path: Gas->MFC->CM Purge->HM Purge->HM->PCV->Nozzle->Atmosphere
Pressure builds CM->HM sequentially to prevent reactive gas backflow.
*)
FUNCTION_BLOCK PUBLIC FB_MaintenancePurgeUmbilicalSequence IMPLEMENTS I_TreeNode
VAR
    _seq : FB_SequenceNode;
    // Phase 1: Preparation
    stepCloseNozzleIso     : FB_CloseValveNode;
    stepClosePCV           : FB_SetControllerNode;

    // Phase 2: Pressurize via purge bypass (CM->HM to prevent backflow)
    stepOpenGasIso         : FB_OpenValveNode;
    stepSetMFC             : FB_SetControllerNode;
    stepOpenCMPurge        : FB_OpenValveNode;
    stepWaitForCMPressure  : FB_WaitForSensorNode;
    stepOpenHMPurge        : FB_OpenValveNode;
    stepWaitForHMPressure  : FB_WaitForSensorNode;

    // Phase 3: Purge to atmosphere
    stepSetPCV             : FB_SetControllerNode;
    stepStabilizationDelay : FB_WaitNode;
    stepOpenNozzleIso      : FB_OpenValveNode;
    stepPurgeDuration      : FB_WaitNode;

    // Phase 4: Cleanup
    stepCleanup            : FB_SequenceNode;
    stepCloseNozzleIsoFinal: FB_CloseValveNode;
    stepClosePCVFinal      : FB_SetControllerNode;
    stepStopMFC            : FB_SetControllerNode;
    stepCloseGasIso        : FB_CloseValveNode;
    stepCloseCMPurge       : FB_CloseValveNode;
    stepCloseHMPurge       : FB_CloseValveNode;

    // Internal state
    _bInitialized          : BOOL := FALSE;
    _bParamsConfigured     : BOOL := FALSE;
    _eSide                 : E_GasSide;
END_VAR

VAR CONSTANT
    DEFAULT_STABILIZATION_DELAY : TIME := T#5S;
    DEFAULT_PURGE_DURATION      : TIME := T#30S;
END_VAR

METHOD PUBLIC Init : BOOL
VAR_INPUT
    ipProvider : I_HardwareProvider;
    eSide      : E_GasSide;
END_VAR
VAR
    ipGasIso      : I_Valve;
    ipMFC         : I_Controller;
    ipCMPurge     : I_Valve;
    ipCMPressure  : I_Sensor;
    ipHMPurge     : I_Valve;
    ipHMPressure  : I_Sensor;
    ipPCV         : I_Controller;
    ipNozzleIso   : I_Valve;
END_VAR
    IF _bInitialized THEN
        IF _eSide = eSide THEN
            Init := TRUE;
            RETURN;
        END_IF
        _seq.ClearChildren();
        stepCleanup.ClearChildren();
        _bInitialized := FALSE;
    END_IF

    IF ipProvider = 0 THEN
        GVL_ErrorLogger.Logger.LogError('FB_MaintenancePurgeUmbilicalSequence', E_ErrorId.ERR_GEN_NULL_POINTER, 'Provider is null');
        RETURN;
    END_IF
    _eSide := eSide;

    // Resolve hardware
    ipGasIso := ipProvider.GetGasIsoValve(eSide, 0);
    ipMFC := ipProvider.GetMFCController(eSide);
    ipCMPurge := ipProvider.GetCMPurgeValve(eSide);
    ipCMPressure := ipProvider.GetCMPressureSensor(eSide);
    ipHMPurge := ipProvider.GetHMPurgeValve(eSide);
    ipHMPressure := ipProvider.GetHMPressureSensor(eSide);
    ipPCV := ipProvider.GetPressureController(eSide);
    ipNozzleIso := ipProvider.GetNozzleIsoValve(eSide);

    // Validate hardware
    IF ipGasIso = 0 OR ipMFC = 0 OR ipCMPurge = 0 OR ipCMPressure = 0 OR
       ipHMPurge = 0 OR ipHMPressure = 0 OR ipPCV = 0 OR ipNozzleIso = 0 THEN
        GVL_ErrorLogger.Logger.LogError('FB_MaintenancePurgeUmbilicalSequence', E_ErrorId.ERR_GEN_NULL_POINTER, 'Hardware reference is null');
        RETURN;
    END_IF

    // Phase 1: Preparation
    stepCloseNozzleIso.Init(ipNozzleIso, sNodeName := 'CloseNozzleIso');
    _seq.AddChild(stepCloseNozzleIso);

    stepClosePCV.Init(ipController := ipPCV, rSetpoint := GVL_SequenceConstants.PCV_CLOSED_SETPOINT, sNodeName := 'ClosePCV');
    _seq.AddChild(stepClosePCV);

    // Phase 2: Pressurize via purge bypass (CM->HM to prevent backflow)
    stepOpenGasIso.Init(ipGasIso, sNodeName := 'OpenGasIso');
    _seq.AddChild(stepOpenGasIso);

    stepSetMFC.Init(ipController := ipMFC, rSetpoint := 0.0, sNodeName := 'SetPurgeMFC');
    _seq.AddChild(stepSetMFC);

    stepOpenCMPurge.Init(ipCMPurge, sNodeName := 'OpenCMPurge');
    _seq.AddChild(stepOpenCMPurge);

    stepWaitForCMPressure.Init(ipSensor := ipCMPressure, rTarget := 0.0, eMode := E_CompareMode.GREATER_THAN, sNodeName := 'WaitForCMPressure');
    _seq.AddChild(stepWaitForCMPressure);

    stepOpenHMPurge.Init(ipHMPurge, sNodeName := 'OpenHMPurge');
    _seq.AddChild(stepOpenHMPurge);

    stepWaitForHMPressure.Init(ipSensor := ipHMPressure, rTarget := 0.0, eMode := E_CompareMode.GREATER_THAN, sNodeName := 'WaitForHMPressure');
    _seq.AddChild(stepWaitForHMPressure);

    // Phase 3: Purge to atmosphere
    stepSetPCV.Init(ipController := ipPCV, rSetpoint := 0.0, sNodeName := 'SetPCVSetpoint');
    _seq.AddChild(stepSetPCV);

    stepStabilizationDelay.Init(tWaitTime := DEFAULT_STABILIZATION_DELAY, sNodeName := 'StabilizationDelay');
    _seq.AddChild(stepStabilizationDelay);

    stepOpenNozzleIso.Init(ipNozzleIso, sNodeName := 'OpenNozzleIso');
    _seq.AddChild(stepOpenNozzleIso);

    stepPurgeDuration.Init(tWaitTime := DEFAULT_PURGE_DURATION, sNodeName := 'PurgeDuration');
    _seq.AddChild(stepPurgeDuration);

    // Phase 4: Cleanup
    stepCleanup.ClearChildren();

    stepCloseNozzleIsoFinal.Init(ipNozzleIso, sNodeName := 'CloseNozzleIsoFinal');
    stepCleanup.AddChild(stepCloseNozzleIsoFinal);

    stepClosePCVFinal.Init(ipController := ipPCV, rSetpoint := GVL_SequenceConstants.PCV_CLOSED_SETPOINT, sNodeName := 'ClosePCVFinal');
    stepCleanup.AddChild(stepClosePCVFinal);

    stepStopMFC.Init(ipController := ipMFC, rSetpoint := 0.0, sNodeName := 'StopMFC');
    stepCleanup.AddChild(stepStopMFC);

    stepCloseGasIso.Init(ipGasIso, sNodeName := 'CloseGasIso');
    stepCleanup.AddChild(stepCloseGasIso);

    stepCloseCMPurge.Init(ipCMPurge, sNodeName := 'CloseCMPurge');
    stepCleanup.AddChild(stepCloseCMPurge);

    stepCloseHMPurge.Init(ipHMPurge, sNodeName := 'CloseHMPurge');
    stepCleanup.AddChild(stepCloseHMPurge);

    stepCleanup.sName := 'Cleanup';
    _seq.AddChild(stepCleanup);

    _seq.sName := CONCAT('MaintUmbilicalPurge_', TO_STRING(eSide));
    _bParamsConfigured := FALSE;
    _bInitialized := TRUE;
    Init := TRUE;
END_METHOD

METHOD PUBLIC SetParams
VAR_INPUT
    rPurgeFlowRate      : LREAL;    // MFC flow rate during purge (sccm/slm)
    rCMPressureTarget   : LREAL;    // CM pressure target before opening HM Purge (bar)
    rHMPressureTarget   : LREAL;    // HM pressure target before opening nozzle (bar)
    rPCVSetpoint        : LREAL;    // PCV setpoint during purge (bar)
    tCMPressureTimeout  : TIME;     // Timeout for CM pressure build
    tHMPressureTimeout  : TIME;     // Timeout for HM pressure build
    tStabilizationDelay : TIME;     // Delay after setting PCV
    tPurgeDuration      : TIME;     // Duration of purge to atmosphere
END_VAR
    IF NOT _bInitialized THEN RETURN; END_IF

    stepSetMFC.Setpoint := rPurgeFlowRate;

    stepWaitForCMPressure.Target := rCMPressureTarget;
    stepWaitForCMPressure.Timeout := tCMPressureTimeout;

    stepWaitForHMPressure.Target := rHMPressureTarget;
    stepWaitForHMPressure.Timeout := tHMPressureTimeout;

    stepSetPCV.Setpoint := rPCVSetpoint;

    stepStabilizationDelay.WaitTime := tStabilizationDelay;
    stepPurgeDuration.WaitTime := tPurgeDuration;
    _bParamsConfigured := TRUE;
END_METHOD

PROPERTY PUBLIC NodeName : STRING(50)
    GET
        NodeName := _seq.sName;
    END_GET
    SET
        _seq.sName := NodeName;
    END_SET
END_PROPERTY

METHOD Tick : ST_NodeResult
    IF NOT _bInitialized THEN
        Tick.eStatus := E_NodeStatus.FAILURE;
        Tick.bHasError := TRUE;
        Tick.stError.eErrorId := E_ErrorId.ERR_GEN_INITIALIZATION;
        Tick.stError.sSource := 'FB_MaintenancePurgeUmbilicalSequence';
        Tick.stError.sMessage := 'Init must be called before Tick';
        Tick.sDescription := Tick.stError.sMessage;
        RETURN;
    END_IF

    IF NOT _bParamsConfigured THEN
        Tick.eStatus := E_NodeStatus.FAILURE;
        Tick.bHasError := TRUE;
        Tick.stError.eErrorId := E_ErrorId.ERR_GEN_NOT_CONFIGURED;
        Tick.stError.sSource := 'FB_MaintenancePurgeUmbilicalSequence';
        Tick.stError.sMessage := 'SetParams must be called before Tick';
        Tick.sDescription := Tick.stError.sMessage;
        RETURN;
    END_IF

    Tick := _seq.Tick();
END_METHOD

{attribute 'estimated-stack-usage' := '500'}
METHOD Reset : BOOL
    Reset := _seq.Reset();
END_METHOD

{attribute 'estimated-stack-usage' := '500'}
METHOD Halt
    _seq.Halt();
END_METHOD

END_FUNCTION_BLOCK
