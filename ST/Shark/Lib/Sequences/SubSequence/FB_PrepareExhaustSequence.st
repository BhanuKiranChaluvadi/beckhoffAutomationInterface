FUNCTION_BLOCK PUBLIC FB_PrepareExhaustSequence IMPLEMENTS I_TreeNode
VAR
    _seq : FB_SequenceNode;
    // Steps
    stepTurnOnPump        : FB_SetDigitalOutputNode;
    stepOpenExhaustVacIso : FB_OpenValveNode;
    stepSetPCVInitial     : FB_SetControllerNode;

    // Internal state
    _bInitialized         : BOOL := FALSE;
    _eSide                : E_GasSide;
END_VAR

METHOD PUBLIC Init : BOOL
VAR_INPUT
    ipProvider      : I_HardwareProvider;
    eSide           : E_GasSide;
END_VAR
    IF _bInitialized THEN
        IF _eSide = eSide THEN
            Init := TRUE;
            RETURN;
        END_IF
        _bInitialized := FALSE;
    END_IF

    IF ipProvider = 0 THEN
        GVL_ErrorLogger.Logger.LogError('FB_PrepareExhaustSequence', E_ErrorId.ERR_GEN_NULL_POINTER, 'Provider is null');
        RETURN;
    END_IF
    _eSide := eSide;

    _seq.ClearChildren();

    stepTurnOnPump.Init(ipDO := ipProvider.GetExhaustPump(), bState := TRUE, sNodeName := 'TurnOnExhaustPump');
    _seq.AddChild(stepTurnOnPump);

    stepOpenExhaustVacIso.Init(ipProvider.GetExhaustVacuumIsoValve(), sNodeName := 'OpenExhaustVacIso');
    _seq.AddChild(stepOpenExhaustVacIso);

    // Set PCV to fully open for unrestricted exhaust flow
    stepSetPCVInitial.Init(ipController := ipProvider.GetPressureController(eSide), rSetpoint := GVL_SequenceConstants.PCV_OPEN_SETPOINT, sNodeName := 'SetPCVOpen');
    _seq.AddChild(stepSetPCVInitial);

    _seq.sName := 'PrepareExhaust';
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
        Tick.stError.sSource := 'FB_PrepareExhaustSequence';
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
