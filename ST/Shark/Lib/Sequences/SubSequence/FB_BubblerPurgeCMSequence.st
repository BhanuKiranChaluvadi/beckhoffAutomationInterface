FUNCTION_BLOCK PUBLIC FB_BubblerPurgeCMSequence IMPLEMENTS I_TreeNode
VAR
    _seq : FB_SequenceNode;
    // Steps
    stepOpenBubbler  : FB_OpenValveNode;
    stepPurgeCM      : FB_MaterialPurgeCMSequence;
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
    ipCMBubblerValve : I_Valve;
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
        GVL_ErrorLogger.Logger.LogError('FB_BubblerPurgeCMSequence', E_ErrorId.ERR_GEN_NULL_POINTER, 'Provider is null');
        RETURN;
    END_IF
    _eSide := eSide;
    _nBubblerIndex := nBubblerIndex;

    // Resolve hardware
    ipCMBubblerValve := ipProvider.GetCMBubblerValve(eSide, nBubblerIndex);

    IF ipCMBubblerValve = 0 THEN
        GVL_ErrorLogger.Logger.LogError('FB_BubblerPurgeCMSequence', E_ErrorId.ERR_GEN_NULL_POINTER, 'Bubbler Valve not found');
        RETURN;
    END_IF

    stepOpenBubbler.Init(ipCMBubblerValve, sNodeName := 'OpenBubblerCMValve');
    _seq.AddChild(stepOpenBubbler);

    stepPurgeCM.Init(ipProvider, eSide);
    stepPurgeCM.NodeName := 'PurgeCMSequence';
    _seq.AddChild(stepPurgeCM);

    stepCloseBubbler.Init(ipCMBubblerValve, sNodeName := 'CloseBubblerCMValve');
    _seq.AddChild(stepCloseBubbler);

    _seq.sName := CONCAT('BubblerPurgeCM_Side', TO_STRING(eSide));
    _bInitialized := TRUE;
    Init := TRUE;
END_METHOD

METHOD PUBLIC SetParams
VAR_INPUT
    nPurgeCycles            : INT;
    rPurgeFlowRate          : LREAL;
    rPurgePressure          : LREAL;
    rEvacuationPressure     : LREAL;
    tPressurizationTimeout  : TIME;
    tEvacuationTimeout      : TIME;
END_VAR
    IF NOT _bInitialized THEN RETURN; END_IF

    stepPurgeCM.SetParams(
        nPurgeCycles           := nPurgeCycles,
        rPurgeFlowRate         := rPurgeFlowRate,
        rPurgePressure         := rPurgePressure,
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
        Tick.stError.sSource := 'FB_BubblerPurgeCMSequence';
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
