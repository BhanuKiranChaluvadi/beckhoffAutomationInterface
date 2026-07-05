FUNCTION_BLOCK PUBLIC FB_PressurizeCMSequence IMPLEMENTS I_TreeNode
VAR
    _seq : FB_SequenceNode;
    // Steps
    stepCloseNozzle        : FB_CloseValveNode;    // Isolate from process
    stepClosePCV           : FB_SetControllerNode; // Isolate from exhaust
    stepOpenGasIso         : FB_OpenValveNode;     // Supply
    stepSetMFC             : FB_SetControllerNode; // Flow
    stepOpenCMPurge        : FB_OpenValveNode;     // Path into CM
    stepWaitForPressure    : FB_WaitForSensorNode;
    stepCloseCMPurge       : FB_CloseValveNode;    // Isolate after fill
    stepCloseGasIso        : FB_CloseValveNode;
    stepStopMFC            : FB_SetControllerNode;

    // Internal state
    _bInitialized             : BOOL := FALSE;
    _bParamsConfigured        : BOOL := FALSE;
    _eSide                    : E_GasSide;
END_VAR

METHOD PUBLIC Init : BOOL
VAR_INPUT
    ipProvider      : I_HardwareProvider;
    eSide           : E_GasSide;
END_VAR
VAR
    ipGasIso         : I_Valve;
    ipCMPurge        : I_Valve;
    ipNozzleIso      : I_Valve;
    ipCMPressure     : I_Sensor;
    ipMFC            : I_Controller;
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
        GVL_ErrorLogger.Logger.LogError('FB_PressurizeCMSequence', E_ErrorId.ERR_GEN_NULL_POINTER, 'Provider is null');
        RETURN;
    END_IF
    _eSide := eSide;

    ipMFC := ipProvider.GetMFCController(eSide);
    ipGasIso := ipProvider.GetGasIsoValve(eSide, 0);
    ipCMPurge := ipProvider.GetCMPurgeValve(eSide);
    ipNozzleIso := ipProvider.GetNozzleIsoValve(eSide);
    ipCMPressure := ipProvider.GetCMPressureSensor(eSide);

    IF ipMFC = 0 OR ipGasIso = 0 OR ipCMPurge = 0 OR ipNozzleIso = 0 OR ipCMPressure = 0 THEN
        GVL_ErrorLogger.Logger.LogError('FB_PressurizeCMSequence', E_ErrorId.ERR_GEN_NULL_POINTER, 'Hardware reference is null');
        RETURN;
    END_IF

    // Secure boundaries before pressurizing
    stepCloseNozzle.Init(ipNozzleIso, sNodeName := 'CloseNozzle');
    _seq.AddChild(stepCloseNozzle);

    stepClosePCV.Init(ipController := ipProvider.GetPressureController(eSide), rSetpoint := GVL_SequenceConstants.PCV_CLOSED_SETPOINT, sNodeName := 'ClosePCV');
    _seq.AddChild(stepClosePCV);

    // Open supply and set flow
    stepOpenGasIso.Init(ipGasIso, sNodeName := 'OpenGasIso');
    _seq.AddChild(stepOpenGasIso);

    stepSetMFC.Init(ipController := ipMFC, rSetpoint := 0.0, sNodeName := 'SetMFC');
    _seq.AddChild(stepSetMFC);

    // Pressurize CM
    stepOpenCMPurge.Init(ipCMPurge, sNodeName := 'OpenCMPurge');
    _seq.AddChild(stepOpenCMPurge);

    stepWaitForPressure.Init(ipSensor := ipCMPressure, rTarget := 0.0, eMode := E_CompareMode.GREATER_THAN, sNodeName := 'WaitCMPressure');
    _seq.AddChild(stepWaitForPressure);

    // Cleanup
    stepCloseCMPurge.Init(ipCMPurge, sNodeName := 'IsolateCM');
    _seq.AddChild(stepCloseCMPurge);

    stepCloseGasIso.Init(ipGasIso, sNodeName := 'CloseGasIso');
    _seq.AddChild(stepCloseGasIso);

    stepStopMFC.Init(ipController := ipMFC, rSetpoint := 0.0, sNodeName := 'StopMFC');
    _seq.AddChild(stepStopMFC);

    _seq.sName := CONCAT('PressurizeCM_', TO_STRING(eSide));
    _bParamsConfigured := FALSE;
    _bInitialized := TRUE;
    Init := TRUE;
END_METHOD

METHOD PUBLIC SetParams
VAR_INPUT
    rTargetPressure  : LREAL;
    rFlowRate        : LREAL;
    tTimeout         : TIME;
END_VAR
    IF NOT _bInitialized THEN RETURN; END_IF

    stepSetMFC.Setpoint := rFlowRate;
    stepWaitForPressure.Target := rTargetPressure;
    stepWaitForPressure.Timeout := tTimeout;
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
        Tick.stError.sSource := 'FB_PressurizeCMSequence';
        Tick.stError.sMessage := 'Init must be called before Tick';
        Tick.sDescription := Tick.stError.sMessage;
        RETURN;
    END_IF

    IF NOT _bParamsConfigured THEN
        Tick.eStatus := E_NodeStatus.FAILURE;
        Tick.bHasError := TRUE;
        Tick.stError.eErrorId := E_ErrorId.ERR_GEN_NOT_CONFIGURED;
        Tick.stError.sSource := 'FB_PressurizeCMSequence';
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
