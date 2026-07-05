FUNCTION_BLOCK PUBLIC FB_BubblerPurgeHMSequence IMPLEMENTS I_TreeNode
VAR
    _seq : FB_SequenceNode;
    // Steps
    stepOpenBubbler  : FB_OpenValveNode;
    stepPurgeHM      : FB_MaterialPurgeHMSequence;
    stepCloseBubbler : FB_CloseValveNode;

    // Internal state
    _bInitialized    : BOOL := FALSE;
    _eSide           : E_GasSide;
    _nBubblerIndex   : DINT;
END_VAR

METHOD PUBLIC Init : BOOL
VAR_INPUT
    ipProvider      : I_HardwareProvider;
    eSide           : E_GasSide;
    nBubblerIndex   : DINT;
END_VAR
VAR
    ipHMBubblerValve : I_Valve;
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
        GVL_ErrorLogger.Logger.LogError('FB_BubblerPurgeHMSequence', E_ErrorId.ERR_GEN_NULL_POINTER, 'Provider is null');
        RETURN;
    END_IF
    _eSide := eSide;
    _nBubblerIndex := nBubblerIndex;

    // Resolve hardware
    ipHMBubblerValve := ipProvider.GetHMBubblerValve(eSide, nBubblerIndex);

    IF ipHMBubblerValve = 0 THEN
        GVL_ErrorLogger.Logger.LogError('FB_BubblerPurgeHMSequence', E_ErrorId.ERR_GEN_NULL_POINTER, 'Bubbler Valve not found');
        RETURN;
    END_IF

    stepOpenBubbler.Init(ipHMBubblerValve, sNodeName := 'OpenBubblerHMValve');
    _seq.AddChild(stepOpenBubbler);

    stepPurgeHM.Init(ipProvider, eSide);
    stepPurgeHM.NodeName := 'PurgeHMSequence';
    _seq.AddChild(stepPurgeHM);

    stepCloseBubbler.Init(ipHMBubblerValve, sNodeName := 'CloseBubblerHMValve');
    _seq.AddChild(stepCloseBubbler);

    _seq.sName := CONCAT('BubblerPurgeHM_Side', TO_STRING(eSide));
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
    IF NOT _bInitialized THEN RETURN; END_IF

    stepPurgeHM.SetParams(
        nPurgeCycles           := nPurgeCycles,
        rPurgeFlowRate         := rPurgeFlowRate,
        rPurgePressure         := rPurgePressure,
        rFinalPressure         := rFinalPressure,
        rEvacuationPressure    := rEvacuationPressure,
        tPressurizationTimeout := tPressurizationTimeout,
        tEvacuationTimeout     := tEvacuationTimeout
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
        Tick.stError.sSource := 'FB_BubblerPurgeHMSequence';
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
