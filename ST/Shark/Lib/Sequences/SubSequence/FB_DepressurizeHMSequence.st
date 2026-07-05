FUNCTION_BLOCK PUBLIC FB_DepressurizeHMSequence IMPLEMENTS I_TreeNode
VAR
    _seq : FB_SequenceNode;
    // Safety steps
    stepCloseNozzle      : FB_CloseValveNode;
    stepClosePCV         : FB_SetControllerNode;

    // Evacuation
    stepOpenHMVacuumIso  : FB_OpenValveNode;
    stepOpenHMPurge      : FB_OpenValveNode;
    stepWaitHMVacuum     : FB_WaitForSensorNode;
    stepCloseHMVacuumIso : FB_CloseValveNode;
    stepCloseHMPurge     : FB_CloseValveNode;

    // Internal state
    _bInitialized        : BOOL := FALSE;
    _eSide               : E_GasSide;
END_VAR

VAR CONSTANT
    DEFAULT_VACUUM_THRESHOLD : LREAL := 1.0; // Bar - pressure below which vacuum is considered achieved
END_VAR

METHOD PUBLIC Init : BOOL
VAR_INPUT
    ipProvider    : I_HardwareProvider;
    eSide         : E_GasSide;
    tVacuumTimeout : TIME := T#30S;
END_VAR
VAR
    ipNozzleIso   : I_Valve;
    ipPCV         : I_Controller;
    ipHMPurge     : I_Valve;
    ipHMVacuumIso : I_Valve;
    ipHMPressure  : I_Sensor;
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
        GVL_ErrorLogger.Logger.LogError('FB_DepressurizeHMSequence', E_ErrorId.ERR_GEN_NULL_POINTER, 'Provider is null');
        RETURN;
    END_IF
    _eSide := eSide;

    // Resolve hardware references
    ipNozzleIso := ipProvider.GetNozzleIsoValve(eSide);
    ipPCV := ipProvider.GetPressureController(eSide);
    ipHMPurge := ipProvider.GetHMPurgeValve(eSide);
    ipHMVacuumIso := ipProvider.GetHMVacuumIsoValve(eSide);
    ipHMPressure := ipProvider.GetHMPressureSensor(eSide);

    IF ipNozzleIso = 0 OR ipPCV = 0 OR ipHMPurge = 0 OR ipHMVacuumIso = 0 OR ipHMPressure = 0 THEN
        GVL_ErrorLogger.Logger.LogError('FB_DepressurizeHMSequence', E_ErrorId.ERR_GEN_NULL_POINTER, 'Hardware reference is null');
        RETURN;
    END_IF

    // Safety: isolate nozzle and close PCV
    stepCloseNozzle.Init(ipNozzleIso, sNodeName := 'CloseNozzle');
    _seq.AddChild(stepCloseNozzle);

    stepClosePCV.Init(ipController := ipPCV, rSetpoint := GVL_SequenceConstants.PCV_CLOSED_SETPOINT, sNodeName := 'ClosePCV');
    _seq.AddChild(stepClosePCV);

    // Evacuation: open path to exhaust, wait for vacuum, then close
    stepOpenHMVacuumIso.Init(ipHMVacuumIso, sNodeName := 'OpenHMVacuumIso');
    _seq.AddChild(stepOpenHMVacuumIso);

    stepOpenHMPurge.Init(ipHMPurge, sNodeName := 'OpenHMPurge');
    _seq.AddChild(stepOpenHMPurge);

    stepWaitHMVacuum.Init(
        ipSensor    := ipHMPressure,
        rTarget     := DEFAULT_VACUUM_THRESHOLD,
        eMode       := E_CompareMode.LESS_THAN,
        tTimeout    := tVacuumTimeout,
        sNodeName   := 'WaitHMVacuum'
    );
    _seq.AddChild(stepWaitHMVacuum);

    stepCloseHMPurge.Init(ipHMPurge, sNodeName := 'CloseHMPurge');
    _seq.AddChild(stepCloseHMPurge);

    stepCloseHMVacuumIso.Init(ipHMVacuumIso, sNodeName := 'CloseHMVacuumIso');
    _seq.AddChild(stepCloseHMVacuumIso);

    _seq.sName := CONCAT('DepressurizeHM_', TO_STRING(eSide));
    _bInitialized := TRUE;
    Init := TRUE;
END_METHOD

METHOD PUBLIC SetParams
VAR_INPUT
    rVacuumThreshold : LREAL;
    tTimeout         : TIME;
END_VAR
    IF NOT _bInitialized THEN RETURN; END_IF

    stepWaitHMVacuum.Target := rVacuumThreshold;
    stepWaitHMVacuum.Timeout := tTimeout;
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
        Tick.stError.sSource := 'FB_DepressurizeHMSequence';
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
