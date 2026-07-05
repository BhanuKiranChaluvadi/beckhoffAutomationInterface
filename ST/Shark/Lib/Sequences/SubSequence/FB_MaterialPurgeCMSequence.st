FUNCTION_BLOCK PUBLIC FB_MaterialPurgeCMSequence IMPLEMENTS I_TreeNode
VAR
    _seq : FB_SequenceNode;
    // Purge cycle structure
    stepPurgeCycles        : FB_RepeatNode;
    stepPurgeCycleSeq      : FB_SequenceNode;

    // Fill steps
    stepOpenExhaustVacIso  : FB_OpenValveNode;
    stepOpenGasIso         : FB_OpenValveNode;
    stepSetMFC             : FB_SetControllerNode;
    stepOpenCMPurge        : FB_OpenValveNode;
    stepCloseCMPurge       : FB_CloseValveNode;
    stepWaitForFill        : FB_WaitForSensorNode;

    // Dump steps
    stepStopFillMFC        : FB_SetControllerNode;
    stepCloseGasIsoForDump : FB_CloseValveNode;
    stepOpenCMVacuumIso    : FB_OpenValveNode;
    stepCloseCMVacuumIso   : FB_CloseValveNode;
    stepWaitForDump        : FB_WaitForSensorNode;

    // Cleanup
    stepCloseGasIsoFinal      : FB_CloseValveNode;
    stepCloseCMPurgeFinal     : FB_CloseValveNode;
    stepCloseCMVacuumIsoFinal : FB_CloseValveNode;

    // Internal state
    _bInitialized          : BOOL := FALSE;
    _bParamsConfigured     : BOOL := FALSE;
    _eSide                 : E_GasSide;
END_VAR

VAR CONSTANT
    DEFAULT_PURGE_CYCLES   : INT := 3;
    DEFAULT_DUMP_THRESHOLD : LREAL := 1.0; // Bar - pressure below which dump is considered complete
END_VAR

METHOD PUBLIC Init : BOOL
VAR_INPUT
    ipProvider      : I_HardwareProvider;
    eSide           : E_GasSide;
END_VAR
VAR
    ipGasIso         : I_Valve;
    ipCMPurge        : I_Valve;
    ipCMPressure     : I_Sensor;
    ipCMVacuumIso    : I_Valve;
    ipMFC            : I_Controller;
END_VAR
    IF _bInitialized THEN
        IF _eSide = eSide THEN
            Init := TRUE;
            RETURN;
        END_IF
        _seq.ClearChildren();
        stepPurgeCycleSeq.ClearChildren();
        _bInitialized := FALSE;
    END_IF

    IF ipProvider = 0 THEN
        GVL_ErrorLogger.Logger.LogError('FB_MaterialPurgeCMSequence', E_ErrorId.ERR_GEN_NULL_POINTER, 'Provider is null');
        RETURN;
    END_IF
    _eSide := eSide;

    // Resolve hardware
    ipMFC := ipProvider.GetMFCController(eSide);
    ipGasIso := ipProvider.GetGasIsoValve(eSide, 0);
    ipCMPurge := ipProvider.GetCMPurgeValve(eSide);
    ipCMPressure := ipProvider.GetCMPressureSensor(eSide);
    ipCMVacuumIso := ipProvider.GetCMVacuumIsoValve(eSide);

    // Validate hardware
    IF ipMFC = 0 OR ipGasIso = 0 OR ipCMPurge = 0 OR ipCMPressure = 0 OR ipCMVacuumIso = 0 THEN
        GVL_ErrorLogger.Logger.LogError('FB_MaterialPurgeCMSequence', E_ErrorId.ERR_GEN_NULL_POINTER, 'Hardware reference is null');
        RETURN;
    END_IF

    // Precondition: ensure exhaust path is open
    stepOpenExhaustVacIso.Init(ipProvider.GetExhaustVacuumIsoValve(), sNodeName := 'OpenExhaustVacIso');
    _seq.AddChild(stepOpenExhaustVacIso);

    // Build purge cycle: fill then dump
    stepCloseCMVacuumIso.Init(ipCMVacuumIso, sNodeName := 'EnsureCMVacuumIsoClosed');
    stepPurgeCycleSeq.AddChild(stepCloseCMVacuumIso);

    stepOpenGasIso.Init(ipGasIso, sNodeName := 'OpenGasIso');
    stepPurgeCycleSeq.AddChild(stepOpenGasIso);

    stepSetMFC.Init(ipController := ipMFC, rSetpoint := 0.0, sNodeName := 'SetPurgeMFC');
    stepPurgeCycleSeq.AddChild(stepSetMFC);

    stepOpenCMPurge.Init(ipCMPurge, sNodeName := 'OpenCMPurge');
    stepPurgeCycleSeq.AddChild(stepOpenCMPurge);

    stepWaitForFill.Init(ipSensor := ipCMPressure, rTarget := 0.0, eMode := E_CompareMode.GREATER_THAN, sNodeName := 'WaitForCMFill');
    stepPurgeCycleSeq.AddChild(stepWaitForFill);

    stepStopFillMFC.Init(ipController := ipMFC, rSetpoint := 0.0, sNodeName := 'StopMFC');
    stepPurgeCycleSeq.AddChild(stepStopFillMFC);

    stepCloseGasIsoForDump.Init(ipGasIso, sNodeName := 'CloseGasIso');
    stepPurgeCycleSeq.AddChild(stepCloseGasIsoForDump);

    stepOpenCMVacuumIso.Init(ipCMVacuumIso, sNodeName := 'OpenCMVacuumIso');
    stepPurgeCycleSeq.AddChild(stepOpenCMVacuumIso);

    stepWaitForDump.Init(ipSensor := ipCMPressure, rTarget := DEFAULT_DUMP_THRESHOLD, eMode := E_CompareMode.LESS_THAN, sNodeName := 'WaitForCMDump');
    stepPurgeCycleSeq.AddChild(stepWaitForDump);

    stepPurgeCycles.SetChild(stepPurgeCycleSeq);
    stepPurgeCycles.nNumCycles := DEFAULT_PURGE_CYCLES;
    stepPurgeCycles.sName := 'CMPurgeCycles';
    _seq.AddChild(stepPurgeCycles);

    // Cleanup
    stepCloseGasIsoFinal.Init(ipGasIso, sNodeName := 'CloseGasIsoFinal');
    _seq.AddChild(stepCloseGasIsoFinal);

    stepCloseCMPurgeFinal.Init(ipCMPurge, sNodeName := 'CloseCMPurgeFinal');
    _seq.AddChild(stepCloseCMPurgeFinal);

    stepCloseCMVacuumIsoFinal.Init(ipCMVacuumIso, sNodeName := 'CloseCMVacuumIsoFinal');
    _seq.AddChild(stepCloseCMVacuumIsoFinal);

    _seq.sName := CONCAT('PurgeCM_', TO_STRING(eSide));
    _bParamsConfigured := FALSE;
    _bInitialized := TRUE;
    Init := TRUE;
END_METHOD

METHOD PUBLIC SetParams
VAR_INPUT
    nPurgeCycles            : INT;
    rPurgeFlowRate          : LREAL;
    rPurgePressure          : LREAL;
    rEvacuationPressure     : LREAL;
    tPressurizationTimeout  : TIME;
    tEvacuationTimeout      : TIME;
END_VAR
    IF NOT _bInitialized THEN RETURN; END_IF

    stepPurgeCycles.nNumCycles := nPurgeCycles;
    stepSetMFC.Setpoint := rPurgeFlowRate;
    stepWaitForFill.Target := rPurgePressure;
    stepWaitForFill.Timeout := tPressurizationTimeout;
    stepWaitForDump.Target := rEvacuationPressure;
    stepWaitForDump.Timeout := tEvacuationTimeout;
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
        Tick.stError.sSource := 'FB_MaterialPurgeCMSequence';
        Tick.stError.sMessage := 'Init must be called before Tick';
        Tick.sDescription := Tick.stError.sMessage;
        RETURN;
    END_IF

    IF NOT _bParamsConfigured THEN
        Tick.eStatus := E_NodeStatus.FAILURE;
        Tick.bHasError := TRUE;
        Tick.stError.eErrorId := E_ErrorId.ERR_GEN_NOT_CONFIGURED;
        Tick.stError.sSource := 'FB_MaterialPurgeCMSequence';
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
