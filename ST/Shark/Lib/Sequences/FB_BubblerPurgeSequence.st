(*
FB_BubblerPurgeSequence
Purge a specific bubbler: Prepare exhaust -> CM purge -> delay -> HM purge.
*)
FUNCTION_BLOCK FB_BubblerPurgeSequence IMPLEMENTS I_TreeNode
VAR
    _seq : FB_SequenceNode;
    // Steps
    stepPrepareExhaust : FB_PrepareExhaustSequence;
    stepBubblerPurgeCM : FB_BubblerPurgeCMSequence;
    stepDelay          : FB_WaitNode;
    stepBubblerPurgeHM : FB_BubblerPurgeHMSequence;

    // Internal state
    _bInitialized    : BOOL := FALSE;
    _eSide           : E_GasSide;
    _nBubblerIndex   : DINT;
END_VAR

VAR CONSTANT
    SWITCH_DELAY : TIME := T#2S;
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
        GVL_ErrorLogger.Logger.LogError('FB_BubblerPurgeSequence', E_ErrorId.ERR_GEN_NULL_POINTER, 'Provider is null');
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

    stepDelay.Init(tWaitTime := SWITCH_DELAY, sNodeName := 'SwitchDelay');
    _seq.AddChild(stepDelay);

    stepBubblerPurgeHM.Init(ipProvider, eSide, nBubblerIndex);
    stepBubblerPurgeHM.NodeName := 'BubblerPurgeHM';
    _seq.AddChild(stepBubblerPurgeHM);

    _seq.sName := CONCAT(CONCAT('BubblerPurgeFull_Side', TO_STRING(eSide)), CONCAT('_Idx', TO_STRING(nBubblerIndex)));

    _bInitialized := TRUE;
    Init := TRUE;
END_METHOD

METHOD PUBLIC SetParams
VAR_INPUT
    stCmd : ST_CmdPurge;
END_VAR
    IF NOT _bInitialized THEN RETURN; END_IF

    stepBubblerPurgeCM.SetParams(
        nPurgeCycles           := stCmd.coldManifoldPurgeCycles,
        rPurgeFlowRate         := REAL_TO_LREAL(stCmd.purgeGasFlow),
        rPurgePressure         := REAL_TO_LREAL(stCmd.purgeHighPressureLimit),
        rEvacuationPressure    := REAL_TO_LREAL(stCmd.purgeLowPressureLimit),
        tPressurizationTimeout := DINT_TO_TIME(stCmd.highPressureTimeout),
        tEvacuationTimeout     := DINT_TO_TIME(stCmd.lowPressureTimeout)
    );

    stepBubblerPurgeHM.SetParams(
        nPurgeCycles           := stCmd.hotManifoldPurgeCycles,
        rPurgeFlowRate         := REAL_TO_LREAL(stCmd.purgeGasFlow),
        rPurgePressure         := REAL_TO_LREAL(stCmd.purgeHighPressureLimit),
        rFinalPressure         := REAL_TO_LREAL(stCmd.hotManifoldOperatingPressure),
        rEvacuationPressure    := REAL_TO_LREAL(stCmd.purgeLowPressureLimit),
        tPressurizationTimeout := DINT_TO_TIME(stCmd.highPressureTimeout),
        tEvacuationTimeout     := DINT_TO_TIME(stCmd.lowPressureTimeout)
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
        Tick.stError.sSource := 'FB_BubblerPurgeSequence';
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
