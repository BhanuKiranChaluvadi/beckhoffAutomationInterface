(*
FB_SystemStopSequence
Parallel gas shutdown of both sides + exhaust isolation.
*)
FUNCTION_BLOCK FB_SystemStopSequence IMPLEMENTS I_TreeNode
VAR
    _seq : FB_SequenceNode;
    // Parallel gas shutdown (both sides)
    stepParallelGasStop : FB_ParallelNode;
    stepGasStopA         : FB_GasStopSequence;
    stepGasStopB         : FB_GasStopSequence;

    // Exhaust shutdown
    stepCloseExhaustVacuumIso : FB_CloseValveNode;

    // Internal state
    _bInitialized    : BOOL := FALSE;
END_VAR

METHOD PUBLIC Init : BOOL
VAR_INPUT
    ipProvider : I_HardwareProvider;
END_VAR
VAR
    ipExhaustVacuumIsoValve : I_Valve;
END_VAR
    IF _bInitialized THEN
        Init := TRUE;
        RETURN;
    END_IF

    IF ipProvider = 0 THEN
        GVL_ErrorLogger.Logger.LogError('FB_SystemStopSequence', E_ErrorId.ERR_GEN_NULL_POINTER, 'Provider is null');
        RETURN;
    END_IF

    // Parallel gas shutdown (A and B sides simultaneously)
    stepGasStopA.Init(ipProvider := ipProvider, eSide := E_GasSide.A_SIDE);
    stepGasStopA.NodeName := 'GasStop_A';

    stepGasStopB.Init(ipProvider := ipProvider, eSide := E_GasSide.B_SIDE);
    stepGasStopB.NodeName := 'GasStop_B';

    stepParallelGasStop.AddChild(stepGasStopA);
    stepParallelGasStop.AddChild(stepGasStopB);
    stepParallelGasStop.sName := 'ParallelGasStop';
    _seq.AddChild(stepParallelGasStop);

    // Exhaust shutdown
    ipExhaustVacuumIsoValve := ipProvider.GetExhaustVacuumIsoValve();
    stepCloseExhaustVacuumIso.Init(ipExhaustVacuumIsoValve, sNodeName := 'CloseExhaustVacuumIso');
    _seq.AddChild(stepCloseExhaustVacuumIso);

    _seq.sName := 'SystemStop';

    _bInitialized := TRUE;
    Init := TRUE;
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
        Tick.stError.sSource := 'FB_SystemStopSequence';
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
