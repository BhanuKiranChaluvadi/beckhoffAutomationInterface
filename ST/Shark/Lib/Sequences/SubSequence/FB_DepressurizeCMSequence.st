FUNCTION_BLOCK PUBLIC FB_DepressurizeCMSequence IMPLEMENTS I_TreeNode
VAR
    _seq : FB_SequenceNode;
    // Steps
    stepOpenCMPurge      : FB_OpenValveNode;
    stepOpenCMVacuumIso  : FB_OpenValveNode;
    stepWaitCMVacuum     : FB_WaitForSensorNode;
    stepCloseCMVacuumIso : FB_CloseValveNode;
    stepCloseCMPurge     : FB_CloseValveNode;

    // Internal state
    _bInitialized        : BOOL := FALSE;
    _eSide               : E_GasSide;
END_VAR

VAR CONSTANT
    DEFAULT_VACUUM_THRESHOLD : LREAL := 1.0; // Bar - pressure below which vacuum is considered achieved
END_VAR

METHOD PUBLIC Init : BOOL
VAR_INPUT
    ipProvider     : I_HardwareProvider;
    eSide          : E_GasSide;
    tVacuumTimeout : TIME := T#30S;
END_VAR
VAR
    ipCMPurge     : I_Valve;
    ipCMVacuumIso : I_Valve;
    ipCMPressure  : I_Sensor;
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
        GVL_ErrorLogger.Logger.LogError('FB_DepressurizeCMSequence', E_ErrorId.ERR_GEN_NULL_POINTER, 'Provider is null');
        RETURN;
    END_IF
    _eSide := eSide;

    // Resolve hardware references
    ipCMPurge := ipProvider.GetCMPurgeValve(eSide);
    ipCMVacuumIso := ipProvider.GetCMVacuumIsoValve(eSide);
    ipCMPressure := ipProvider.GetCMPressureSensor(eSide);

    IF ipCMPurge = 0 OR ipCMVacuumIso = 0 OR ipCMPressure = 0 THEN
        GVL_ErrorLogger.Logger.LogError('FB_DepressurizeCMSequence', E_ErrorId.ERR_GEN_NULL_POINTER, 'Hardware reference is null');
        RETURN;
    END_IF

    // Evacuation: open path to exhaust, wait for vacuum, then close
    stepOpenCMPurge.Init(ipCMPurge, sNodeName := 'OpenCMPurge');
    _seq.AddChild(stepOpenCMPurge);

    stepOpenCMVacuumIso.Init(ipCMVacuumIso, sNodeName := 'OpenCMVacuumIso');
    _seq.AddChild(stepOpenCMVacuumIso);

    stepWaitCMVacuum.Init(
        ipSensor    := ipCMPressure,
        rTarget     := DEFAULT_VACUUM_THRESHOLD,
        eMode       := E_CompareMode.LESS_THAN,
        tTimeout    := tVacuumTimeout,
        sNodeName   := 'WaitCMVacuum'
    );
    _seq.AddChild(stepWaitCMVacuum);

    stepCloseCMVacuumIso.Init(ipCMVacuumIso, sNodeName := 'CloseCMVacuumIso');
    _seq.AddChild(stepCloseCMVacuumIso);

    stepCloseCMPurge.Init(ipCMPurge, sNodeName := 'CloseCMPurge');
    _seq.AddChild(stepCloseCMPurge);

    _seq.sName := CONCAT('DepressurizeCM_', TO_STRING(eSide));
    _bInitialized := TRUE;
    Init := TRUE;
END_METHOD

METHOD PUBLIC SetParams
VAR_INPUT
    rVacuumThreshold : LREAL;
    tTimeout         : TIME;
END_VAR
    IF NOT _bInitialized THEN RETURN; END_IF

    stepWaitCMVacuum.Target := rVacuumThreshold;
    stepWaitCMVacuum.Timeout := tTimeout;
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
        Tick.stError.sSource := 'FB_DepressurizeCMSequence';
        Tick.stError.sMessage := 'Init must be called before Tick';
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
