FUNCTION_BLOCK PUBLIC FB_MaterialPurgeHMSequence IMPLEMENTS I_TreeNode
VAR
    _seq : FB_SequenceNode;
    // Preparation
    stepCloseNozzle        : FB_CloseValveNode;
    stepClosePCV           : FB_SetControllerNode;

    // Purge cycles (n-1 times)
    stepCyclesRepeat       : FB_RepeatNode;
    stepCycleSeq           : FB_SequenceNode;
    stepDepressurizeHM     : FB_DepressurizeHMSequence;
    stepPressurizeHM       : FB_PressurizeHMSequence;

    // Final cycle
    stepFinalDepressurize  : FB_DepressurizeHMSequence;
    stepFinalPressurize    : FB_PressurizeHMSequence;

    // Internal state
    _bInitialized          : BOOL := FALSE;
    _eSide                 : E_GasSide;
END_VAR

VAR CONSTANT
    DEFAULT_PURGE_CYCLES : INT := 2;
END_VAR

METHOD PUBLIC Init : BOOL
VAR_INPUT
    ipProvider      : I_HardwareProvider;
    eSide           : E_GasSide;
END_VAR
VAR
    ipPCV            : I_Controller;
    ipNozzleIso      : I_Valve;
    ipHMPurge        : I_Valve;
    ipHMVacuumIso    : I_Valve;
    ipHMPressure     : I_Sensor;
END_VAR
    IF _bInitialized THEN
        IF _eSide = eSide THEN
            Init := TRUE;
            RETURN;
        END_IF
        _seq.ClearChildren();
        stepCycleSeq.ClearChildren();
        _bInitialized := FALSE;
    END_IF

    IF ipProvider = 0 THEN
        GVL_ErrorLogger.Logger.LogError('FB_MaterialPurgeHMSequence', E_ErrorId.ERR_GEN_NULL_POINTER, 'Provider is null');
        RETURN;
    END_IF
    _eSide := eSide;

    // Resolve hardware for prep
    ipPCV := ipProvider.GetPressureController(eSide);
    ipNozzleIso := ipProvider.GetNozzleIsoValve(eSide);
    ipHMPurge := ipProvider.GetHMPurgeValve(eSide);
    ipHMVacuumIso := ipProvider.GetHMVacuumIsoValve(eSide);
    ipHMPressure := ipProvider.GetHMPressureSensor(eSide);

    // Validate hardware
    IF ipPCV = 0 OR ipNozzleIso = 0 OR ipHMPurge = 0 OR ipHMVacuumIso = 0 OR ipHMPressure = 0 THEN
        GVL_ErrorLogger.Logger.LogError('FB_MaterialPurgeHMSequence', E_ErrorId.ERR_GEN_NULL_POINTER, 'Hardware reference is null');
        RETURN;
    END_IF

    // Preparation: close boundaries
    stepCloseNozzle.Init(ipNozzleIso, sNodeName := 'CloseNozzle');
    _seq.AddChild(stepCloseNozzle);

    stepClosePCV.Init(ipController := ipPCV, rSetpoint := GVL_SequenceConstants.PCV_CLOSED_SETPOINT, sNodeName := 'ClosePCV');
    _seq.AddChild(stepClosePCV);

    // Purge cycles: depressurize then pressurize (n-1 times)
    stepCycleSeq.ClearChildren();

    stepDepressurizeHM.Init(ipProvider := ipProvider, eSide := eSide);
    stepDepressurizeHM.NodeName := 'DepressurizeHM';
    stepCycleSeq.AddChild(stepDepressurizeHM);

    stepPressurizeHM.Init(ipProvider := ipProvider, eSide := eSide);
    stepPressurizeHM.NodeName := 'PressurizeHM';
    stepCycleSeq.AddChild(stepPressurizeHM);

    stepCyclesRepeat.SetChild(stepCycleSeq);
    stepCyclesRepeat.nNumCycles := DEFAULT_PURGE_CYCLES;
    stepCyclesRepeat.sName := 'HMPurgeCycles';
    _seq.AddChild(stepCyclesRepeat);

    // Final cycle: end at operating pressure
    stepFinalDepressurize.Init(ipProvider := ipProvider, eSide := eSide);
    stepFinalDepressurize.NodeName := 'FinalDepressurizeHM';
    _seq.AddChild(stepFinalDepressurize);

    stepFinalPressurize.Init(ipProvider := ipProvider, eSide := eSide);
    stepFinalPressurize.NodeName := 'FinalPressurizeHM';
    _seq.AddChild(stepFinalPressurize);

    _seq.sName := CONCAT('PurgeHM_', TO_STRING(eSide));
    _bInitialized := TRUE;
    Init := TRUE;
END_METHOD

METHOD PUBLIC SetParams
VAR_INPUT
    nPurgeCycles            : INT;
    rPurgeFlowRate          : LREAL;
    rPurgePressure          : LREAL;
    rFinalPressure          : LREAL;   // Operating pressure for final pressurization
    rEvacuationPressure     : LREAL;
    tPressurizationTimeout  : TIME;
    tEvacuationTimeout      : TIME;
END_VAR
VAR
    nCycleRepeatCount : INT;
END_VAR
    IF NOT _bInitialized THEN RETURN; END_IF

    // Calculate repeat count: nPurgeCycles - 1 (because we have n-1 repeats plus 1 final)
    IF nPurgeCycles > 1 THEN
        nCycleRepeatCount := nPurgeCycles - 1;
    ELSE
        nCycleRepeatCount := 1;
    END_IF

    stepCyclesRepeat.nNumCycles := nCycleRepeatCount;

    stepPressurizeHM.SetParams(
        rTargetCMPressure := 0.0,
        rTargetHMPressure := rPurgePressure,
        rFlowRate := rPurgeFlowRate,
        tCMPressureTimeout := tPressurizationTimeout,
        tHMPressureTimeout := tPressurizationTimeout
    );

    stepDepressurizeHM.SetParams(
        rVacuumThreshold := rEvacuationPressure,
        tTimeout := tEvacuationTimeout
    );

    stepFinalDepressurize.SetParams(
        rVacuumThreshold := rEvacuationPressure,
        tTimeout := tEvacuationTimeout
    );

    // Final pressurization uses operating pressure, not purge pressure
    stepFinalPressurize.SetParams(
        rTargetCMPressure := 0.0,
        rTargetHMPressure := rFinalPressure,
        rFlowRate := rPurgeFlowRate,
        tCMPressureTimeout := tPressurizationTimeout,
        tHMPressureTimeout := tPressurizationTimeout
    );
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
        Tick.stError.sSource := 'FB_MaterialPurgeHMSequence';
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
