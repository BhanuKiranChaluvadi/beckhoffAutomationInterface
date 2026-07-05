(*
FB_GasStopSequence
Shuts down gas flow for one side. Parallel shutdown of manifold sections.
Bubbler array sized at compile-time upper bound (5); actual count comes
from provider.GetMaxBubblerCount() at runtime.
*)
FUNCTION_BLOCK FB_GasStopSequence IMPLEMENTS I_TreeNode
VAR
    _seq : FB_SequenceNode;
    // Delivery path shutdown
    stepCloseNozzleIso : FB_CloseValveNode;
    stepClosePCV       : FB_SetControllerNode;

    // HM shutdown (parallel)
    stepHMShutdown       : FB_ParallelNode;
    stepsCloseHMBubbler  : ARRAY[1..5] OF FB_CloseValveNode;
    stepCloseHMPurge     : FB_CloseValveNode;
    stepCloseHMVacuumIso : FB_CloseValveNode;

    // CM shutdown (parallel)
    stepCMShutdown       : FB_ParallelNode;
    stepsCloseCMBubbler  : ARRAY[1..5] OF FB_CloseValveNode;
    stepCloseCMPurge     : FB_CloseValveNode;
    stepCloseCMVacuumIso : FB_CloseValveNode;

    // Gas inlet shutdown (parallel)
    stepGasInletShutdown : FB_ParallelNode;
    stepsCloseGasIso     : ARRAY[1..5] OF FB_CloseValveNode;
    stepZeroMFC          : FB_SetControllerNode;

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
    ipValve      : I_Valve;
    ipController : I_Controller;
    nMaxBubblers : DINT;
    i            : DINT;
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
        GVL_ErrorLogger.Logger.LogError('FB_GasStopSequence', E_ErrorId.ERR_GEN_NULL_POINTER, 'Provider is null');
        RETURN;
    END_IF

    nMaxBubblers := ipProvider.GetMaxBubblerCount(eSide);

    stepHMShutdown.ClearChildren();
    stepCMShutdown.ClearChildren();
    stepGasInletShutdown.ClearChildren();

    // Delivery path
    ipValve := ipProvider.GetNozzleIsoValve(eSide);
    stepCloseNozzleIso.Init(ipValve, sNodeName := 'CloseNozzleIso');
    _seq.AddChild(stepCloseNozzleIso);

    ipController := ipProvider.GetPressureController(eSide);
    stepClosePCV.Init(ipController := ipController, rSetpoint := GVL_SequenceConstants.PCV_CLOSED_SETPOINT, sNodeName := 'ClosePCV');
    _seq.AddChild(stepClosePCV);

    // HM shutdown
    ipValve := ipProvider.GetHMVacuumIsoValve(eSide);
    stepCloseHMVacuumIso.Init(ipValve, sNodeName := 'CloseHMVacIso');
    stepHMShutdown.AddChild(stepCloseHMVacuumIso);

    ipValve := ipProvider.GetHMPurgeValve(eSide);
    stepCloseHMPurge.Init(ipValve, sNodeName := 'CloseHMPurge');
    stepHMShutdown.AddChild(stepCloseHMPurge);

    FOR i := 1 TO nMaxBubblers DO
        ipValve := ipProvider.GetHMBubblerValve(eSide, i);
        IF ipValve <> 0 THEN
            stepsCloseHMBubbler[i].Init(ipValve);
            stepsCloseHMBubbler[i].sName := CONCAT('CloseHMBubbler_', TO_STRING(i));
            stepHMShutdown.AddChild(stepsCloseHMBubbler[i]);
        END_IF
    END_FOR

    stepHMShutdown.sName := 'HM_Shutdown';
    _seq.AddChild(stepHMShutdown);

    // CM shutdown
    ipValve := ipProvider.GetCMVacuumIsoValve(eSide);
    stepCloseCMVacuumIso.Init(ipValve, sNodeName := 'CloseCMVacIso');
    stepCMShutdown.AddChild(stepCloseCMVacuumIso);

    ipValve := ipProvider.GetCMPurgeValve(eSide);
    stepCloseCMPurge.Init(ipValve, sNodeName := 'CloseCMPurge');
    stepCMShutdown.AddChild(stepCloseCMPurge);

    FOR i := 1 TO nMaxBubblers DO
        ipValve := ipProvider.GetCMBubblerValve(eSide, i);
        IF ipValve <> 0 THEN
            stepsCloseCMBubbler[i].Init(ipValve);
            stepsCloseCMBubbler[i].sName := CONCAT('CloseCMBubbler_', TO_STRING(i));
            stepCMShutdown.AddChild(stepsCloseCMBubbler[i]);
        END_IF
    END_FOR

    stepCMShutdown.sName := 'CM_Shutdown';
    _seq.AddChild(stepCMShutdown);

    // Gas inlet shutdown
    ipController := ipProvider.GetMFCController(eSide);
    stepZeroMFC.Init(ipController := ipController, rSetpoint := 0.0, sNodeName := 'ZeroMFC');
    stepGasInletShutdown.AddChild(stepZeroMFC);

    FOR i := 1 TO nMaxBubblers DO
        ipValve := ipProvider.GetGasIsoValve(eSide, i);
        IF ipValve <> 0 THEN
            stepsCloseGasIso[i].Init(ipValve);
            stepsCloseGasIso[i].sName := CONCAT('CloseGasIso_', TO_STRING(i));
            stepGasInletShutdown.AddChild(stepsCloseGasIso[i]);
        END_IF
    END_FOR

    stepGasInletShutdown.sName := 'Inlet_Shutdown';
    _seq.AddChild(stepGasInletShutdown);

    _seq.sName := CONCAT('GasStop_', TO_STRING(eSide));
    _eSide := eSide;
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
        Tick.stError.sSource := 'FB_GasStopSequence';
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
