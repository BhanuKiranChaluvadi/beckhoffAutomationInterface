(*
FB_MaintenancePurgeSequence
Complete maintenance purge for a specific bubbler (CM + HM + Umbilical regions).
Use when replacing bubblers or performing maintenance requiring full gas path purge.
*)
FUNCTION_BLOCK FB_MaintenancePurgeSequence IMPLEMENTS I_TreeNode
VAR
    _seq : FB_SequenceNode;
    // Steps
    stepPrepareExhaust : FB_PrepareExhaustSequence;
    stepBubblerPurgeCM : FB_BubblerPurgeCMSequence;
    stepDelayCMtoHM    : FB_WaitNode;
    stepBubblerPurgeHM : FB_BubblerPurgeHMSequence;
    stepDelayHMtoUmb   : FB_WaitNode;
    stepUmbilicalPurge : FB_MaintenancePurgeUmbilicalSequence;

    // Internal state
    _bInitialized    : BOOL := FALSE;
    _eSide           : E_GasSide;
    _nBubblerIndex   : DINT;
END_VAR

VAR CONSTANT
    PHASE_TRANSITION_DELAY : TIME := T#5S;
END_VAR

METHOD PUBLIC Init : BOOL
VAR_INPUT
    ipProvider    : I_HardwareProvider;
    eSide         : E_GasSide;
    nBubblerIndex : DINT;
END_VAR
    IF _bInitialized THEN
        IF _eSide = eSide AND _nBubblerIndex = nBubblerIndex THEN
            Init := TRUE;
            RETURN;
        END_IF
        _seq.ClearChildren();
        _bInitialized := FALSE;
    END_IF

    IF ipProvider = 0 THEN
        GVL_ErrorLogger.Logger.LogError('FB_MaintenancePurgeSequence', E_ErrorId.ERR_GEN_NULL_POINTER, 'Provider is null');
        RETURN;
    END_IF
    _eSide := eSide;
    _nBubblerIndex := nBubblerIndex;

    stepPrepareExhaust.Init(ipProvider, eSide);
    stepPrepareExhaust.NodeName := 'PrepareExhaust';
    _seq.AddChild(stepPrepareExhaust);

    stepBubblerPurgeCM.Init(ipProvider, eSide, nBubblerIndex);
    stepBubblerPurgeCM.NodeName := 'BubblerPurgeCM';
    _seq.AddChild(stepBubblerPurgeCM);

    stepDelayCMtoHM.Init(tWaitTime := PHASE_TRANSITION_DELAY, sNodeName := 'DelayCMtoHM');
    _seq.AddChild(stepDelayCMtoHM);

    stepBubblerPurgeHM.Init(ipProvider, eSide, nBubblerIndex);
    stepBubblerPurgeHM.NodeName := 'BubblerPurgeHM';
    _seq.AddChild(stepBubblerPurgeHM);

    stepDelayHMtoUmb.Init(tWaitTime := PHASE_TRANSITION_DELAY, sNodeName := 'DelayHMtoUmb');
    _seq.AddChild(stepDelayHMtoUmb);

    stepUmbilicalPurge.Init(ipProvider, eSide);
    stepUmbilicalPurge.NodeName := 'UmbilicalPurge';
    _seq.AddChild(stepUmbilicalPurge);

    _seq.sName := CONCAT(CONCAT('MaintenancePurge_Side', TO_STRING(eSide)), CONCAT('_Idx', TO_STRING(nBubblerIndex)));

    _bInitialized := TRUE;
    Init := TRUE;
END_METHOD

METHOD PUBLIC SetParams
VAR_INPUT
    stCmd : ST_CmdPurge;
END_VAR
    IF NOT _bInitialized THEN RETURN; END_IF

    // Phase 1: CM Purge parameters
    stepBubblerPurgeCM.SetParams(
        nPurgeCycles           := stCmd.coldManifoldPurgeCycles,
        rPurgeFlowRate         := REAL_TO_LREAL(stCmd.purgeGasFlow),
        rPurgePressure         := REAL_TO_LREAL(stCmd.purgeHighPressureLimit),
        rEvacuationPressure    := REAL_TO_LREAL(stCmd.purgeLowPressureLimit),
        tPressurizationTimeout := DINT_TO_TIME(stCmd.highPressureTimeout),
        tEvacuationTimeout     := DINT_TO_TIME(stCmd.lowPressureTimeout)
    );

    // Phase 3: HM Purge parameters (Final state = operating pressure)
    stepBubblerPurgeHM.SetParams(
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
        Tick.stError.sSource := 'FB_MaintenancePurgeSequence';
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
