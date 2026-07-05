(*
FB_GasStartSequence — Starts bubbler flow for one side.

  ── Refactor of FB_GasStartSequence: "lazy build" architecture ──
  The original built the tree in Init() with placeholder 0.0 values for every
  parameter-driven step, then overwrote them in SetParams() via property setters.
  That split made Init() ambiguous (is a 0.0 real or a placeholder?) and left
  the I14 risk that a skipped SetParams leaves waits with target 0.0.

  This version separates the two kinds of configuration cleanly:
    Init()       → resolve + validate HARDWARE only. No tree, no setpoints.
    SetParams()  → validate params, DERIVE every runtime value, then _BuildTree().
    _BuildTree() → the single recipe: wire each node ONCE with its final value.

  Result: no placeholders anywhere; reading _BuildTree() shows every real value.

Physical path:
  Gas Inlet (3 bar ± 0.5) → MFC → CM [sensor] → CM bubbler valve → Bubbler (heated) →
  HM bubbler valve → HM [sensor] → PCV → Start valve → Micro nozzle → Reactor (~1 bar)

Terminology:
  CM (Cold Manifold) = "distributor" side — MFC feeds here; CM sensor reads here.
  HM (Hot Manifold) = "combiner" side  — after bubbler; PCV controls pressure here.

Phases:
  0. Open PCV fully        : allow free flow during evacuation
  1. Evacuate HM           : open exhaust iso + HM vacuum iso; wait HM < 0.5 bar;
                             close HM vacuum iso
  2. Fill via purge valves : MFC max flow + gas inlet + HM/CM purge open;
                             wait CM > 2.0 bar; close purge valves; stop MFC + close gas inlet
  3. Path-open check       : open start valve + HM bubbler; wait HM < 1.3 bar;
                             close start valve
  4. Establish flow        : reopen start valve; restart MFC + gas inlet;
                             wait CM > CM operating target; wait HM < HM operating target;
                             open CM bubbler; wait HM within tolerance of target;
                             set final MFC flow rate
*)
FUNCTION_BLOCK FB_GasStartSequence IMPLEMENTS I_TreeNode
VAR
    _seq : FB_SequenceNode;

    // Phase 0: Open PCV fully to allow free flow during evacuation
    stepOpenPCVFully      : FB_SetControllerNode;

    // Phase 1: Unconditional evacuation via exhaust pump + HM vacuum iso
    stepOpenExhaustPump   : FB_OpenValveNode;     // Open main exhaust iso valve (VALV_EX_PUMP)
    stepOpenHMVacuumIso   : FB_OpenValveNode;     // Open HM vacuum iso (connects HM to exhaust path)
    stepWaitEvacuation    : FB_WaitForSensorNode; // Wait HM < MIN_EVACUATION_PRESSURE (PCV stays fully open)
    stepCloseHMVacuumIso  : FB_CloseValveNode;    // Close HM vacuum iso

    // Phase 2: Fill manifolds through purge valves with max MFC flow
    stepSetMFCBuildupFlow : FB_SetControllerNode; // MFC = _rBuildupFlow
    stepOpenGasInlet      : FB_OpenValveNode;     // Open gas iso valve
    stepOpenHMPurge       : FB_OpenValveNode;     // Open HM purge valve
    stepOpenCMPurge       : FB_OpenValveNode;     // Open CM purge valve
    stepWaitCMFilled      : FB_WaitForSensorNode; // Wait CM > CM_FILL_PRESSURE (2.0 bar)
    stepCloseHMPurge      : FB_CloseValveNode;    // Close HM purge valve
    stepCloseCMPurge      : FB_CloseValveNode;    // Close CM purge valve
    stepStopMFC           : FB_SetControllerNode; // MFC = 0 (stop flow)
    stepCloseGasInlet     : FB_CloseValveNode;    // Close gas iso valve

    // Phase 3: Path-open check — connect HM to reactor side; verify HM near atmospheric
    stepOpenStartValve1   : FB_OpenValveNode;     // Open start (nozzle iso) valve
    stepOpenHMBubbler     : FB_OpenValveNode;     // Open HM bubbler valve
    stepWaitHMPressureDown: FB_WaitForSensorNode; // Wait HM < HM_ATMOS_CHECK_PRESSURE (1.3 bar)
    stepCloseStartValve   : FB_CloseValveNode;    // Close start valve (re-isolate for Phase 4 fill)

    // Phase 4: Establish operating flow
    stepOpenStartValve2   : FB_OpenValveNode;     // Re-open start valve
    stepSetMFCBuildupFlow2: FB_SetControllerNode; // MFC = _rBuildupFlow (second time)
    stepOpenGasInlet2     : FB_OpenValveNode;     // Re-open gas inlet (second time)
    stepWaitCMAtTarget    : FB_WaitForSensorNode; // Wait CM > _rCMAtTarget (CM operating pressure)
    stepSetPCVOperating   : FB_SetControllerNode; // Set PCV = _rHMTarget — engage back-pressure control
    stepWaitHMBelowTarget : FB_WaitForSensorNode; // Wait HM < _rHMTarget (HM operating pressure)
    stepOpenCMBubbler     : FB_OpenValveNode;     // Open CM bubbler valve
    stepWaitHMAtTarget    : FB_WaitForSensorNode; // Wait HM within _rHMTolerance of _rHMTarget
    stepSetMFCTarget      : FB_SetControllerNode; // MFC = _rFinalFlow (final target flow)

    // ── Hardware references (resolved in Init) ──
    _ipGasIsoValve           : I_Valve;
    _ipCMBubblerValve        : I_Valve;
    _ipHMBubblerValve        : I_Valve;
    _ipHMPurgeValve          : I_Valve;
    _ipCMPurgeValve          : I_Valve;
    _ipHMVacuumIsoValve      : I_Valve;
    _ipNozzleIsoValve        : I_Valve;
    _ipCMPressureSensor      : I_Sensor;
    _ipHMPressureSensor      : I_Sensor;
    _ipMFCController         : I_Controller;
    _ipPressureController    : I_Controller;
    _ipExhaustVacuumIsoValve : I_Valve;

    // ── Init-scoped config ──
    _tEvacuationTimeout : TIME;

    // ── Runtime values (derived in SetParams; consumed by _BuildTree) ──
    _rBuildupFlow       : LREAL;   // MFC buildup/purge flow
    _rCMAtTarget        : LREAL;   // CM operating target = HM target + gradient margin
    _rHMTarget          : LREAL;   // HM operating pressure (PCV setpoint + HM waits)
    _rHMTolerance       : LREAL;   // HM in-range tolerance band (bar)
    _rFinalFlow         : LREAL;   // Final operating MFC flow rate
    _tCMPressureTimeout : TIME;    // CM wait timeout
    _tHMPressureTimeout : TIME;    // HM wait timeout

    // Internal state
    _bInitialized      : BOOL := FALSE;
    _bParamsConfigured : BOOL := FALSE;
    _eSide             : E_GasSide;
    _nBubblerIndex     : DINT;
END_VAR

VAR CONSTANT
    MIN_EVACUATION_PRESSURE   : LREAL := 0.5;   // Bar — HM evacuation wait target
    CM_FILL_PRESSURE          : LREAL := 2.0;   // Bar — CM fill target via purge valves
    MAX_BUILDUP_FLOW_RATE     : LREAL := 23.0;  // sccm — MFC flow for manifold fill/purge
    HM_ATMOS_CHECK_PRESSURE   : LREAL := 1.3;   // Bar — HM target for path-open check (near atmospheric)
    GRADIENT_MARGIN           : LREAL := 0.2;   // Bar — CM must lead HM by this for forward flow
    HEAD_OPERATING_PRESSURE   : LREAL := 1.0;   // Bar — reactor head steady-state pressure
    MIN_PRESSURE_DIFFERENTIAL : LREAL := 0.2;   // Bar — minimum HM-to-head differential for flow
END_VAR

(* Init — resolve and validate hardware ONLY. Does not build the tree.
   The tree is built by SetParams() once every runtime value is known. *)
METHOD PUBLIC Init : BOOL
VAR_INPUT
    ipProvider         : I_HardwareProvider;
    eSide              : E_GasSide;
    nBubblerIndex      : DINT := 1;
    tEvacuationTimeout : TIME := T#30S;
END_VAR
    // Re-init guard: same side/index → no-op; different → drop any prior build
    IF _bInitialized THEN
        IF _eSide = eSide AND _nBubblerIndex = nBubblerIndex THEN
            Init := TRUE;
            RETURN;
        END_IF
        _seq.ClearChildren();
        _bInitialized := FALSE;
        _bParamsConfigured := FALSE;
    END_IF

    IF ipProvider = 0 THEN
        GVL_ErrorLogger.Logger.LogError('FB_GasStartSequence', E_ErrorId.ERR_GEN_NULL_POINTER, 'Provider is null');
        RETURN;
    END_IF

    _eSide := eSide;
    _nBubblerIndex := nBubblerIndex;
    _tEvacuationTimeout := tEvacuationTimeout;

    // Resolve hardware references into members
    _ipGasIsoValve           := ipProvider.GetGasIsoValve(eSide, nBubblerIndex);
    _ipCMBubblerValve        := ipProvider.GetCMBubblerValve(eSide, nBubblerIndex);
    _ipHMBubblerValve        := ipProvider.GetHMBubblerValve(eSide, nBubblerIndex);
    _ipHMPurgeValve          := ipProvider.GetHMPurgeValve(eSide);
    _ipCMPurgeValve          := ipProvider.GetCMPurgeValve(eSide);
    _ipHMVacuumIsoValve      := ipProvider.GetHMVacuumIsoValve(eSide);
    _ipNozzleIsoValve        := ipProvider.GetNozzleIsoValve(eSide);
    _ipCMPressureSensor      := ipProvider.GetCMPressureSensor(eSide);
    _ipHMPressureSensor      := ipProvider.GetHMPressureSensor(eSide);
    _ipMFCController         := ipProvider.GetMFCController(eSide);
    _ipPressureController    := ipProvider.GetPressureController(eSide);
    _ipExhaustVacuumIsoValve := ipProvider.GetExhaustVacuumIsoValve();

    // Validate all hardware
    IF _ipGasIsoValve = 0 OR _ipCMBubblerValve = 0 OR _ipHMBubblerValve = 0 OR
       _ipHMPurgeValve = 0 OR _ipCMPurgeValve = 0 OR _ipHMVacuumIsoValve = 0 OR
       _ipNozzleIsoValve = 0 OR _ipCMPressureSensor = 0 OR _ipHMPressureSensor = 0 OR
       _ipMFCController = 0 OR _ipPressureController = 0 OR _ipExhaustVacuumIsoValve = 0 THEN
        GVL_ErrorLogger.Logger.LogError('FB_GasStartSequence', E_ErrorId.ERR_GEN_NULL_POINTER, 'One or more hardware references are null');
        RETURN;
    END_IF

    _seq.sName := CONCAT(CONCAT('GasStart_', TO_STRING(eSide)), CONCAT('_Idx', TO_STRING(nBubblerIndex)));
    _bParamsConfigured := FALSE;
    _bInitialized := TRUE;
    Init := TRUE;
END_METHOD

(* SetParams — validate the command, derive every runtime value, then build the tree.
   After this returns TRUE, no node holds a placeholder value. *)
METHOD PUBLIC SetParams : BOOL
VAR_INPUT
    stParams : ST_CmdStartBubblerFlow;
END_VAR
VAR
    rHMTarget      : LREAL;
    rFlowTolerance : LREAL;
END_VAR
    IF NOT _bInitialized THEN RETURN; END_IF

    rHMTarget := REAL_TO_LREAL(stParams.hotManifoldOperatingPressure);

    // Validate: pressure differential must be sufficient for flow
    IF (rHMTarget - HEAD_OPERATING_PRESSURE) < MIN_PRESSURE_DIFFERENTIAL THEN
        GVL_ErrorLogger.Logger.LogError(
            'FB_GasStartSequence',
            E_ErrorId.ERR_GEN_INVALID_PARAM,
            'hotManifoldOperatingPressure too low — insufficient pressure differential for flow');
        RETURN;
    END_IF

    IF REAL_TO_LREAL(stParams.flowRate) <= 0.0 THEN
        GVL_ErrorLogger.Logger.LogError(
            'FB_GasStartSequence',
            E_ErrorId.ERR_GEN_INVALID_PARAM,
            'flowRate must be > 0');
        RETURN;
    END_IF

    rFlowTolerance := REAL_TO_LREAL(stParams.MFCFlowRateTolerance);

    // ── Derive every runtime value here (single place; no placeholders) ──
    _rHMTarget          := rHMTarget;
    _rBuildupFlow       := MAX(REAL_TO_LREAL(stParams.flowRate), MAX_BUILDUP_FLOW_RATE);
    _rCMAtTarget        := rHMTarget + GRADIENT_MARGIN;   // CM must lead HM for forward flow
    _rHMTolerance       := rHMTarget * (rFlowTolerance / 100.0);
    _rFinalFlow         := REAL_TO_LREAL(stParams.flowRate);
    _tCMPressureTimeout := DINT_TO_TIME(REAL_TO_DINT(stParams.coldManifoldPressureTimeout));
    _tHMPressureTimeout := DINT_TO_TIME(REAL_TO_DINT(stParams.hotManifoldPressureTimeout));

    // Build (or rebuild) the tree now that every value is final
    _seq.ClearChildren();
    _BuildTree();

    _bParamsConfigured := TRUE;
    SetParams := TRUE;
END_METHOD

(* _BuildTree — the single recipe. Every node is wired ONCE with its final value:
   constants for fixed steps, derived members _r*/_t* for parameter-driven steps. *)
METHOD PRIVATE _BuildTree
    // Phase 0 — Open PCV fully to allow free flow during evacuation.
    stepOpenPCVFully.Init(ipController := _ipPressureController, rSetpoint := GVL_SequenceConstants.PCV_OPEN_SETPOINT, sNodeName := 'OpenPCVFully');
    _seq.AddChild(stepOpenPCVFully);

    // Phase 1 — Unconditional evacuation via exhaust pump + HM vacuum iso.
    stepOpenExhaustPump.Init(ipValve := _ipExhaustVacuumIsoValve, sNodeName := 'OpenExhaustPump');
    _seq.AddChild(stepOpenExhaustPump);

    stepOpenHMVacuumIso.Init(ipValve := _ipHMVacuumIsoValve, sNodeName := 'OpenHMVacuumIso');
    _seq.AddChild(stepOpenHMVacuumIso);

    stepWaitEvacuation.Init(ipSensor := _ipHMPressureSensor, rTarget := MIN_EVACUATION_PRESSURE, eMode := E_CompareMode.LESS_THAN, tTimeout := _tEvacuationTimeout, sNodeName := 'WaitEvacuation');
    _seq.AddChild(stepWaitEvacuation);

    stepCloseHMVacuumIso.Init(ipValve := _ipHMVacuumIsoValve, sNodeName := 'CloseHMVacuumIso');
    _seq.AddChild(stepCloseHMVacuumIso);

    // Phase 2 — Fill manifolds through purge valves; wait CM fills to 2.0 bar.
    stepSetMFCBuildupFlow.Init(ipController := _ipMFCController, rSetpoint := _rBuildupFlow, sNodeName := 'SetMFCBuildup');
    _seq.AddChild(stepSetMFCBuildupFlow);

    stepOpenGasInlet.Init(ipValve := _ipGasIsoValve, sNodeName := 'OpenGasInlet');
    _seq.AddChild(stepOpenGasInlet);

    stepOpenHMPurge.Init(ipValve := _ipHMPurgeValve, sNodeName := 'OpenHMPurge');
    _seq.AddChild(stepOpenHMPurge);

    stepOpenCMPurge.Init(ipValve := _ipCMPurgeValve, sNodeName := 'OpenCMPurge');
    _seq.AddChild(stepOpenCMPurge);

    stepWaitCMFilled.Init(ipSensor := _ipCMPressureSensor, rTarget := CM_FILL_PRESSURE, eMode := E_CompareMode.GREATER_THAN, tTimeout := _tCMPressureTimeout, sNodeName := 'WaitCMFilled');
    _seq.AddChild(stepWaitCMFilled);

    stepCloseHMPurge.Init(ipValve := _ipHMPurgeValve, sNodeName := 'CloseHMPurge');
    _seq.AddChild(stepCloseHMPurge);

    stepCloseCMPurge.Init(ipValve := _ipCMPurgeValve, sNodeName := 'CloseCMPurge');
    _seq.AddChild(stepCloseCMPurge);

    stepStopMFC.Init(ipController := _ipMFCController, rSetpoint := 0.0, sNodeName := 'StopMFC');
    _seq.AddChild(stepStopMFC);

    // Phase 3 — Path-open check: connect HM to reactor; verify HM near atmospheric.
    stepOpenStartValve1.Init(ipValve := _ipNozzleIsoValve, sNodeName := 'OpenStartValve1');
    _seq.AddChild(stepOpenStartValve1);

    stepOpenHMBubbler.Init(ipValve := _ipHMBubblerValve, sNodeName := 'OpenHMBubbler');
    _seq.AddChild(stepOpenHMBubbler);

    stepWaitHMPressureDown.Init(ipSensor := _ipHMPressureSensor, rTarget := HM_ATMOS_CHECK_PRESSURE, eMode := E_CompareMode.LESS_THAN, tTimeout := _tHMPressureTimeout, sNodeName := 'WaitHMPressureDown');
    _seq.AddChild(stepWaitHMPressureDown);

    stepCloseStartValve.Init(ipValve := _ipNozzleIsoValve, sNodeName := 'CloseStartValve');
    _seq.AddChild(stepCloseStartValve);

    // Phase 4 — Establish operating flow.
    stepOpenStartValve2.Init(ipValve := _ipNozzleIsoValve, sNodeName := 'OpenStartValve2');
    _seq.AddChild(stepOpenStartValve2);

    stepSetMFCBuildupFlow2.Init(ipController := _ipMFCController, rSetpoint := _rBuildupFlow, sNodeName := 'SetMFCBuildup2');
    _seq.AddChild(stepSetMFCBuildupFlow2);

    stepOpenGasInlet2.Init(ipValve := _ipGasIsoValve, sNodeName := 'OpenGasInlet2');
    _seq.AddChild(stepOpenGasInlet2);

    stepWaitCMAtTarget.Init(ipSensor := _ipCMPressureSensor, rTarget := _rCMAtTarget, eMode := E_CompareMode.GREATER_THAN, tTimeout := _tCMPressureTimeout, sNodeName := 'WaitCMAtTarget');
    _seq.AddChild(stepWaitCMAtTarget);

    // Engage PCV back-pressure control to the operating target before the HM pressure waits.
    stepSetPCVOperating.Init(ipController := _ipPressureController, rSetpoint := _rHMTarget, sNodeName := 'SetPCVOperating');
    _seq.AddChild(stepSetPCVOperating);

    stepWaitHMBelowTarget.Init(ipSensor := _ipHMPressureSensor, rTarget := _rHMTarget, eMode := E_CompareMode.LESS_THAN, tTimeout := _tHMPressureTimeout, sNodeName := 'WaitHMBelowTarget');
    _seq.AddChild(stepWaitHMBelowTarget);

    stepOpenCMBubbler.Init(ipValve := _ipCMBubblerValve, sNodeName := 'OpenCMBubbler');
    _seq.AddChild(stepOpenCMBubbler);

    stepWaitHMAtTarget.Init(ipSensor := _ipHMPressureSensor, rTarget := _rHMTarget, rTolerance := _rHMTolerance, eMode := E_CompareMode.WITHIN_TOLERANCE, tTimeout := _tHMPressureTimeout, sNodeName := 'WaitHMAtTarget');
    _seq.AddChild(stepWaitHMAtTarget);

    stepSetMFCTarget.Init(ipController := _ipMFCController, rSetpoint := _rFinalFlow, sNodeName := 'SetMFCTarget');
    _seq.AddChild(stepSetMFCTarget);
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
        Tick.stError.sSource := 'FB_GasStartSequence';
        Tick.stError.sMessage := 'Init must be called before Tick';
        Tick.sDescription := Tick.stError.sMessage;
        RETURN;
    END_IF

    IF NOT _bParamsConfigured THEN
        Tick.eStatus := E_NodeStatus.FAILURE;
        Tick.bHasError := TRUE;
        Tick.stError.eErrorId := E_ErrorId.ERR_GEN_NOT_CONFIGURED;
        Tick.stError.sSource := 'FB_GasStartSequence';
        Tick.stError.sMessage := 'SetParams must be called before Tick';
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
