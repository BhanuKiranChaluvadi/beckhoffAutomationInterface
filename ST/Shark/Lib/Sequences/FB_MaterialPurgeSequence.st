(*
FB_MaterialPurgeSequence
Full material purge: Prepare exhaust -> HM purge cycles -> Umbilical purge -> Cleanup.
*)
FUNCTION_BLOCK FB_MaterialPurgeSequence IMPLEMENTS I_TreeNode
VAR
    _seq : FB_SequenceNode;
    // Phase 1: Prepare exhaust
    stepPrepareExhaust : FB_PrepareExhaustSequence;

    // Phase 2: HM purge (vacuum/pressurize cycles)
    stepHMPurge : FB_MaterialPurgeHMSequence;

    // Phase 3: Umbilical purge (push-to-atmosphere)
    stepUmbilicalPurge : FB_MaterialPurgeUmbilicalSequence;

    // Phase 4: Cleanup
    stepCleanup          : FB_ParallelNode;
    stepCloseHMPurge     : FB_CloseValveNode;
    stepCloseCMPurge     : FB_CloseValveNode;
    stepCloseExhaustPump : FB_CloseValveNode;
    stepZeroMFCFinal     : FB_SetControllerNode;

    // Internal state
    _bInitialized    : BOOL := FALSE;
    _eSide           : E_GasSide;
END_VAR

METHOD PUBLIC Init : BOOL
VAR_INPUT
    ipProvider : I_HardwareProvider;
    eSide      : E_GasSide;
END_VAR
VAR
    ipExhaustVacuumIso : I_Valve;
    ipHMPurge          : I_Valve;
    ipCMPurge          : I_Valve;
    ipMFC              : I_Controller;
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
        GVL_ErrorLogger.Logger.LogError('FB_MaterialPurgeSequence', E_ErrorId.ERR_GEN_NULL_POINTER, 'Provider is null');
        RETURN;
    END_IF
    _eSide := eSide;

    stepPrepareExhaust.Init(ipProvider := ipProvider, eSide := eSide);
    stepPrepareExhaust.NodeName := 'PrepareExhaust';
    _seq.AddChild(stepPrepareExhaust);

    stepHMPurge.Init(ipProvider := ipProvider, eSide := eSide);
    stepHMPurge.NodeName := 'HMPurge';
    _seq.AddChild(stepHMPurge);

    stepUmbilicalPurge.Init(ipProvider := ipProvider, eSide := eSide);
    stepUmbilicalPurge.NodeName := 'UmbilicalPurge';
    _seq.AddChild(stepUmbilicalPurge);

    // Cleanup phase
    stepCleanup.ClearChildren();

    ipHMPurge := ipProvider.GetHMPurgeValve(eSide);
    ipCMPurge := ipProvider.GetCMPurgeValve(eSide);
    ipExhaustVacuumIso := ipProvider.GetExhaustVacuumIsoValve();
    ipMFC := ipProvider.GetMFCController(eSide);

    stepCloseHMPurge.Init(ipHMPurge, sNodeName := 'CloseHMPurge');
    stepCleanup.AddChild(stepCloseHMPurge);

    stepCloseCMPurge.Init(ipCMPurge, sNodeName := 'CloseCMPurge');
    stepCleanup.AddChild(stepCloseCMPurge);

    stepCloseExhaustPump.Init(ipExhaustVacuumIso, sNodeName := 'CloseExhaustPump');
    stepCleanup.AddChild(stepCloseExhaustPump);

    stepZeroMFCFinal.Init(ipController := ipMFC, rSetpoint := 0.0, sNodeName := 'ZeroMFC');
    stepCleanup.AddChild(stepZeroMFCFinal);

    stepCleanup.sName := 'Cleanup';
    _seq.AddChild(stepCleanup);

    _seq.sName := CONCAT('MaterialPurge_', TO_STRING(eSide));
    _bInitialized := TRUE;
    Init := TRUE;
END_METHOD

METHOD PUBLIC SetParams
VAR_INPUT
    stCmd : ST_CmdPurge;
END_VAR
    IF NOT _bInitialized THEN RETURN; END_IF

    stepHMPurge.SetParams(
        nPurgeCycles           := stCmd.hotManifoldPurgeCycles,
        rPurgeFlowRate         := REAL_TO_LREAL(stCmd.purgeGasFlow),
        rPurgePressure         := REAL_TO_LREAL(stCmd.purgeHighPressureLimit),
        rFinalPressure         := REAL_TO_LREAL(stCmd.hotManifoldOperatingPressure),
        rEvacuationPressure    := REAL_TO_LREAL(stCmd.purgeLowPressureLimit),
        tPressurizationTimeout := DINT_TO_TIME(stCmd.highPressureTimeout),
        tEvacuationTimeout     := DINT_TO_TIME(stCmd.lowPressureTimeout)
    );

    stepUmbilicalPurge.SetParams(
        rPurgeFlowRate      := REAL_TO_LREAL(stCmd.purgeGasFlow),
        rCMPressureTarget   := REAL_TO_LREAL(stCmd.purgeHighPressureLimit),
        rHMPressureTarget   := REAL_TO_LREAL(stCmd.purgeHighPressureLimit),
        rPCVSetpoint        := REAL_TO_LREAL(stCmd.hotManifoldOperatingPressure),
        tCMPressureTimeout  := DINT_TO_TIME(stCmd.highPressureTimeout),
        tHMPressureTimeout  := DINT_TO_TIME(stCmd.highPressureTimeout),
        tStabilizationDelay := DINT_TO_TIME(stCmd.stabilisationDelay),
        tPurgeDuration      := DINT_TO_TIME(stCmd.umbilicalFlushTime)
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
        Tick.stError.sSource := 'FB_MaterialPurgeSequence';
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
