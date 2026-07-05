FUNCTION_BLOCK PUBLIC FB_PressurizeHMSequence IMPLEMENTS I_TreeNode
VAR
    _seq : FB_SequenceNode;
    // Safety and supply
    stepCloseNozzle     : FB_CloseValveNode;
    stepClosePCV        : FB_SetControllerNode;
    stepOpenGasIso      : FB_OpenValveNode;
    stepSetMFCBuildupFlow : FB_SetControllerNode;

    // Initial gas pressure buildup
    stepWaitGasPressure : FB_WaitForSensorNode;

    // CM pressure buildup
    stepOpenCMPurge     : FB_OpenValveNode;
    stepWaitCMPressure  : FB_WaitForSensorNode;

    // HM pressurization
    stepOpenHMPurge     : FB_OpenValveNode;
    stepWaitHMPressure  : FB_WaitForSensorNode;

    // Cleanup (reverse order)
    stepCloseHMPurge    : FB_CloseValveNode;
    stepCloseCMPurge    : FB_CloseValveNode;
    stepZeroMFC         : FB_SetControllerNode;
    stepCloseGasIso     : FB_CloseValveNode;

    // Internal state
    _bInitialized          : BOOL := FALSE;
    _bParamsConfigured     : BOOL := FALSE;
    _eSide                 : E_GasSide;
END_VAR

METHOD PUBLIC Init : BOOL
VAR_INPUT
    ipProvider          : I_HardwareProvider;
    eSide               : E_GasSide;
    tCMPressureTimeout  : TIME  := T#10M;
    tHMPressureTimeout  : TIME  := T#10M;
END_VAR
VAR
    ipGasIso            : I_Valve;
    ipCMPurge           : I_Valve;
    ipHMPurge           : I_Valve;
    ipNozzleIso         : I_Valve;
    ipMFC               : I_Controller;
    ipPCV               : I_Controller;
    ipCMPressureSensor  : I_Sensor;
    ipHMPressureSensor  : I_Sensor;
END_VAR
    IF _bInitialized THEN
        IF _eSide = eSide THEN
            Init := TRUE;
            RETURN;
        END_IF
        _seq.ClearChildren();
        _bInitialized := FALSE;
    END_IF

    IF ipProvider = 0 THEN
        GVL_ErrorLogger.Logger.LogError('FB_PressurizeHMSequence', E_ErrorId.ERR_GEN_NULL_POINTER, 'Provider is null');
        RETURN;
    END_IF
    _eSide := eSide;

    // Resolve hardware references
    ipMFC := ipProvider.GetMFCController(eSide);
    ipGasIso := ipProvider.GetGasIsoValve(eSide, 0);
    ipCMPurge := ipProvider.GetCMPurgeValve(eSide);
    ipHMPurge := ipProvider.GetHMPurgeValve(eSide);
    ipNozzleIso := ipProvider.GetNozzleIsoValve(eSide);
    ipPCV := ipProvider.GetPressureController(eSide);
    ipCMPressureSensor := ipProvider.GetCMPressureSensor(eSide);
    ipHMPressureSensor := ipProvider.GetHMPressureSensor(eSide);

    IF ipMFC = 0 OR ipGasIso = 0 OR ipCMPurge = 0 OR ipHMPurge = 0 OR ipNozzleIso = 0 OR
       ipPCV = 0 OR ipCMPressureSensor = 0 OR ipHMPressureSensor = 0 THEN
        GVL_ErrorLogger.Logger.LogError('FB_PressurizeHMSequence', E_ErrorId.ERR_GEN_NULL_POINTER, 'Hardware reference is null');
        RETURN;
    END_IF

    // Safety: close nozzle and PCV before pressurizing
    stepCloseNozzle.Init(ipNozzleIso, sNodeName := 'CloseNozzle');
    _seq.AddChild(stepCloseNozzle);

    stepClosePCV.Init(ipController := ipPCV, rSetpoint := GVL_SequenceConstants.PCV_CLOSED_SETPOINT, sNodeName := 'ClosePCV');
    _seq.AddChild(stepClosePCV);

    // Open supply
    stepOpenGasIso.Init(ipGasIso, sNodeName := 'OpenGasIso');
    _seq.AddChild(stepOpenGasIso);

    stepSetMFCBuildupFlow.Init(ipController := ipMFC, rSetpoint := 0.0, sNodeName := 'SetMFCBuildup');
    _seq.AddChild(stepSetMFCBuildupFlow);

    // Wait for initial gas pressure before opening CM Purge
    stepWaitGasPressure.Init(ipSensor := ipCMPressureSensor, rTarget := 0.0, eMode := E_CompareMode.GREATER_THAN, tTimeout := tCMPressureTimeout, sNodeName := 'WaitGasPressure');
    _seq.AddChild(stepWaitGasPressure);

    // Build CM pressure
    stepOpenCMPurge.Init(ipCMPurge, sNodeName := 'OpenCMPurge');
    _seq.AddChild(stepOpenCMPurge);

    stepWaitCMPressure.Init(ipSensor := ipCMPressureSensor, rTarget := 0.0, eMode := E_CompareMode.GREATER_THAN, tTimeout := tCMPressureTimeout, sNodeName := 'WaitCMPressure');
    _seq.AddChild(stepWaitCMPressure);

    // Pressurize HM
    stepOpenHMPurge.Init(ipHMPurge, sNodeName := 'OpenHMPurge');
    _seq.AddChild(stepOpenHMPurge);

    stepWaitHMPressure.Init(ipSensor := ipHMPressureSensor, rTarget := 0.0, eMode := E_CompareMode.GREATER_THAN, tTimeout := tHMPressureTimeout, sNodeName := 'WaitHMPressure');
    _seq.AddChild(stepWaitHMPressure);

    // Cleanup in reverse order
    stepCloseHMPurge.Init(ipHMPurge, sNodeName := 'IsolateHM');
    _seq.AddChild(stepCloseHMPurge);

    stepCloseCMPurge.Init(ipCMPurge, sNodeName := 'CloseCMPurge');
    _seq.AddChild(stepCloseCMPurge);

    stepZeroMFC.Init(ipController := ipMFC, rSetpoint := 0.0, sNodeName := 'ZeroMFC');
    _seq.AddChild(stepZeroMFC);

    stepCloseGasIso.Init(ipGasIso, sNodeName := 'CloseGasIso');
    _seq.AddChild(stepCloseGasIso);

    _seq.sName := CONCAT('PressurizeHM_', TO_STRING(eSide));
    _bParamsConfigured := FALSE;
    _bInitialized := TRUE;
    Init := TRUE;
END_METHOD

METHOD PUBLIC SetParams
VAR_INPUT
    rTargetCMPressure   : LREAL;   // Target CM pressure
    rTargetHMPressure   : LREAL;   // Target HM pressure (full pressurize)
    rFlowRate           : LREAL;   // MFC buildup flow rate (sccm)
    tCMPressureTimeout  : TIME;    // Timeout for CM pressure buildup
    tHMPressureTimeout  : TIME;
END_VAR
    IF NOT _bInitialized THEN RETURN; END_IF

    stepSetMFCBuildupFlow.Setpoint := rFlowRate;

    stepWaitGasPressure.Target := rTargetCMPressure;
    stepWaitGasPressure.Timeout := tCMPressureTimeout;

    stepWaitCMPressure.Target := rTargetCMPressure;
    stepWaitCMPressure.Timeout := tCMPressureTimeout;

    stepWaitHMPressure.Target := rTargetHMPressure;
    stepWaitHMPressure.Timeout := tHMPressureTimeout;
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
        Tick.stError.sSource := 'FB_PressurizeHMSequence';
        Tick.stError.sMessage := 'Init must be called before Tick';
        Tick.sDescription := Tick.stError.sMessage;
        RETURN;
    END_IF

    IF NOT _bParamsConfigured THEN
        Tick.eStatus := E_NodeStatus.FAILURE;
        Tick.bHasError := TRUE;
        Tick.stError.eErrorId := E_ErrorId.ERR_GEN_NOT_CONFIGURED;
        Tick.stError.sSource := 'FB_PressurizeHMSequence';
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
