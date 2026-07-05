(*
FB_HeatingSequence
Sets up temperature gradient for gas delivery path: HM -> Umbilical -> Head.

One-shot: on first tick it enables the selected controllers and writes their
setpoints, then continuously reports SUCCESS while monitoring controller errors.

TEMPERATURE GRADIENT:
  HM (Combiner)     = combinerTemp (from payload)
  Umbilical (Start) = (combinerTemp + headTemp) / 2.0
  Head              = headTemp (from payload)

ENABLE MODES:
  0 = Both sides  1 = A-Only  2 = B-Only  3 = None
*)
FUNCTION_BLOCK FB_HeatingSequence IMPLEMENTS I_TreeNode
VAR
    // Internal State
    _bInitialized : BOOL := FALSE;
    _bApplied     : BOOL := FALSE;
    _bParamsConfigured : BOOL := FALSE;

    // Controller References
    _ipHM_A  : I_Controller;
    _ipHM_B  : I_Controller;
    _ipUmb_A : I_Controller;
    _ipUmb_B : I_Controller;
    _ipHead  : I_Controller;

    // Parameters (from SetParams)
    _rHMTemp_A   : LREAL;
    _rHMTemp_B   : LREAL;
    _rUmbTemp_A  : LREAL;
    _rUmbTemp_B  : LREAL;
    _rHeadTemp   : LREAL;
    _nEnableMode : INT;
    _bEnableA    : BOOL := FALSE;   // TRUE when A-side controllers are active (modes 0 or 1)
    _bEnableB    : BOOL := FALSE;   // TRUE when B-side controllers are active (modes 0 or 2)

    // Status
    _sErrorMsg : STRING(255);
END_VAR

METHOD PUBLIC Init : BOOL
VAR_INPUT
    ipProvider : I_HardwareProvider;
END_VAR
    _bInitialized := FALSE;
    _bApplied := FALSE;
    _bParamsConfigured := FALSE;
    _sErrorMsg := '';

    IF ipProvider = 0 THEN
        GVL_ErrorLogger.Logger.LogError('FB_HeatingSequence', E_ErrorId.ERR_GEN_NULL_POINTER, 'Provider is null');
        RETURN;
    END_IF

    _ipHM_A := ipProvider.GetHMTemperatureController(E_GasSide.A_SIDE);
    _ipHM_B := ipProvider.GetHMTemperatureController(E_GasSide.B_SIDE);
    _ipUmb_A := ipProvider.GetUmbilicalTemperatureController(E_GasSide.A_SIDE);
    _ipUmb_B := ipProvider.GetUmbilicalTemperatureController(E_GasSide.B_SIDE);
    _ipHead := ipProvider.GetHeadTemperatureController();

    IF _ipHead = 0 THEN
        GVL_ErrorLogger.Logger.LogError('FB_HeatingSequence', E_ErrorId.ERR_GEN_NULL_POINTER, 'Head controller not found');
        RETURN;
    END_IF

    _bInitialized := TRUE;
    Init := TRUE;
END_METHOD

METHOD PUBLIC SetParams
VAR_INPUT
    stCmd : ST_CmdSetTemperatureGradient;
END_VAR
    IF NOT _bInitialized THEN RETURN; END_IF

    _rHMTemp_A := REAL_TO_LREAL(stCmd.precursorCombiner);
    _rHMTemp_B := REAL_TO_LREAL(stCmd.reactantCombiner);
    _rHeadTemp := REAL_TO_LREAL(stCmd.headTemp);

    _rUmbTemp_A := (_rHMTemp_A + _rHeadTemp) / 2.0;
    _rUmbTemp_B := (_rHMTemp_B + _rHeadTemp) / 2.0;

    _nEnableMode := stCmd.enableMode;
    _bEnableA    := (stCmd.enableMode = 0 OR stCmd.enableMode = 1);
    _bEnableB    := (stCmd.enableMode = 0 OR stCmd.enableMode = 2);
    _bParamsConfigured := TRUE;
END_METHOD

METHOD PUBLIC Tick : ST_NodeResult
    IF NOT _bInitialized THEN
        Tick.eStatus := E_NodeStatus.FAILURE;
        Tick.bHasError := TRUE;
        Tick.stError.eErrorId := E_ErrorId.ERR_GEN_INITIALIZATION;
        Tick.stError.sSource := 'FB_HeatingSequence';
        Tick.stError.sMessage := 'Init must be called before Tick';
        Tick.sDescription := Tick.stError.sMessage;
        RETURN;
    END_IF

    IF NOT _bParamsConfigured THEN
        Tick.eStatus := E_NodeStatus.FAILURE;
        Tick.bHasError := TRUE;
        Tick.stError.eErrorId := E_ErrorId.ERR_GEN_NOT_CONFIGURED;
        Tick.stError.sSource := 'FB_HeatingSequence';
        Tick.stError.sMessage := 'SetParams must be called before Tick';
        Tick.sDescription := Tick.stError.sMessage;
        RETURN;
    END_IF

    // First tick: apply enable + setpoints once, then return RUNNING immediately.
    // DO NOT check _HeatingComplete() on the same tick we call Enable()/SetSetpoint().
    // FB_HeatZone.Execute() runs before the orchestrator in the PLC task; its cached
    // _stCtrlStatus still reflects _bEnabledCmd = FALSE from the previous scan.
    // Although _HeatingComplete() explicitly blocks while Head bEnabled=FALSE (so it
    // would not complete prematurely), returning RUNNING here gives Execute() at least
    // one scan cycle to process the Enable() command before the first completion check.
    IF NOT _bApplied THEN
        _EnableControllers(_bEnableA, _bEnableB);
        _SetSetpoints(_bEnableA, _bEnableB);
        _bApplied := TRUE;
        Tick.eStatus := E_NodeStatus.RUNNING;
        Tick.rProgress := 0.0;
        Tick.sDescription := 'Applying setpoints...';
        RETURN;
    END_IF

    // Error check runs every tick — controller faults after apply still fail the node.
    IF _CheckForErrors() THEN
        Tick.eStatus := E_NodeStatus.FAILURE;
        Tick.bHasError := TRUE;
        Tick.stError.eErrorId := E_ErrorId.ERR_GEN_INTERNAL;
        Tick.stError.sSource := 'FB_HeatingSequence';
        Tick.stError.sMessage := _sErrorMsg;
        Tick.sDescription := _sErrorMsg;
        RETURN;
    END_IF

    // Stay RUNNING until heating is physically complete: Head at setpoint (anchor)
    // and measured temperature gradient established for each active side.
    // sDescription carries live measured values so ramp progress is visible in telemetry.
    IF NOT _HeatingComplete() THEN
        Tick.eStatus := E_NodeStatus.RUNNING;
        Tick.rProgress := 0.0;
        Tick.sDescription := _BuildGradientStatus();
        RETURN;
    END_IF

    Tick.eStatus := E_NodeStatus.SUCCESS;
    Tick.rProgress := 100.0;
    Tick.sDescription := 'Heating Complete';
END_METHOD

METHOD PUBLIC Reset : BOOL
    _bApplied := FALSE;
    _sErrorMsg := '';

    _ResetIfErrored(_ipHM_A);
    _ResetIfErrored(_ipHM_B);
    _ResetIfErrored(_ipUmb_A);
    _ResetIfErrored(_ipUmb_B);
    _ResetIfErrored(_ipHead);

    Reset := TRUE;
END_METHOD

METHOD PUBLIC Halt
    Reset();
END_METHOD

PROPERTY PUBLIC NodeName : STRING(50)
    GET
        NodeName := 'HeatingSequence';
    END_GET
END_PROPERTY

METHOD PRIVATE _EnableControllers
VAR_INPUT
    bEnableA : BOOL;
    bEnableB : BOOL;
END_VAR
    IF _ipHM_A <> 0 THEN
        _ipHM_A.Enable(bEnableA);
    END_IF
    IF _ipUmb_A <> 0 THEN
        _ipUmb_A.Enable(bEnableA);
    END_IF
    IF _ipHM_B <> 0 THEN
        _ipHM_B.Enable(bEnableB);
    END_IF
    IF _ipUmb_B <> 0 THEN
        _ipUmb_B.Enable(bEnableB);
    END_IF
    IF _ipHead <> 0 THEN
        _ipHead.Enable(bEnableA OR bEnableB);
    END_IF
END_METHOD

METHOD PRIVATE _SetSetpoints
VAR_INPUT
    bEnableA : BOOL;
    bEnableB : BOOL;
END_VAR
    IF bEnableA THEN
        IF _ipHM_A <> 0 THEN
            _ipHM_A.SetSetpoint(_rHMTemp_A);
        END_IF
        IF _ipUmb_A <> 0 THEN
            _ipUmb_A.SetSetpoint(_rUmbTemp_A);
        END_IF
    END_IF

    IF bEnableB THEN
        IF _ipHM_B <> 0 THEN
            _ipHM_B.SetSetpoint(_rHMTemp_B);
        END_IF
        IF _ipUmb_B <> 0 THEN
            _ipUmb_B.SetSetpoint(_rUmbTemp_B);
        END_IF
    END_IF

    IF _ipHead <> 0 AND (bEnableA OR bEnableB) THEN
        _ipHead.SetSetpoint(_rHeadTemp);
    END_IF
END_METHOD

METHOD PRIVATE _CheckForErrors : BOOL
    _sErrorMsg := '';
    IF _ControllerInError(_ipHM_A, 'HM A-Side') THEN _CheckForErrors := TRUE; RETURN; END_IF
    IF _ControllerInError(_ipUmb_A, 'Umbilical A-Side') THEN _CheckForErrors := TRUE; RETURN; END_IF
    IF _ControllerInError(_ipHM_B, 'HM B-Side') THEN _CheckForErrors := TRUE; RETURN; END_IF
    IF _ControllerInError(_ipUmb_B, 'Umbilical B-Side') THEN _CheckForErrors := TRUE; RETURN; END_IF
    IF _ControllerInError(_ipHead, 'Head') THEN _CheckForErrors := TRUE; RETURN; END_IF
    _CheckForErrors := FALSE;
END_METHOD

// Heating is physically complete when both conditions hold:
//   1. Head has reached its target setpoint — the high-temperature anchor of the gradient.
//      Until Head is enabled and at setpoint, checking the downstream gradient is premature.
//   2. The measured temperature ordering is correct for every active side:
//      Head.measured > Umbilical.measured > HM.measured
//
// HM and Umbilical are NOT required to reach their individual programmed setpoints.
// The gradient being established is the physical completion criterion — a zone that is
// colder than the one upstream of it would cause gas condensation in the delivery line.
METHOD PRIVATE _HeatingComplete : BOOL
VAR
    stHead : ST_ControllerStatus;
END_VAR
    // Mode 3 (ENABLE_NONE): no heating active, nothing to wait for.
    IF NOT _bEnableA AND NOT _bEnableB THEN
        _HeatingComplete := TRUE;
        RETURN;
    END_IF

    // Head must be enabled and at setpoint before declaring the gradient valid.
    stHead := _ipHead.GetStatus();
    IF NOT stHead.bEnabled OR NOT stHead.bAtSetpoint THEN
        RETURN;   // FALSE by default
    END_IF

    // Check the gradient for each active side.
    IF _bEnableA AND NOT _SideGradientCorrect(_ipHead, _ipUmb_A, _ipHM_A) THEN
        RETURN;
    END_IF
    IF _bEnableB AND NOT _SideGradientCorrect(_ipHead, _ipUmb_B, _ipHM_B) THEN
        RETURN;
    END_IF

    _HeatingComplete := TRUE;
END_METHOD

// Builds a human-readable snapshot of measured temperatures for every active side.
// Called from Tick() as sDescription while waiting, so live temperatures are visible
// in telemetry and the watch window without needing a separate diagnostic variable.
METHOD PRIVATE _BuildGradientStatus : STRING(255)
VAR
    sResult : STRING(255);
    stHead  : ST_ControllerStatus;
    stUmbA  : ST_ControllerStatus;
    stHMA   : ST_ControllerStatus;
    stUmbB  : ST_ControllerStatus;
    stHMB   : ST_ControllerStatus;
END_VAR
    stHead  := _ipHead.GetStatus();
    sResult := CONCAT('Gradient: Head=', LREAL_TO_STRING(stHead.rMeasuredValue));

    IF _bEnableA THEN
        stUmbA  := _ipUmb_A.GetStatus();
        stHMA   := _ipHM_A.GetStatus();
        sResult := CONCAT(sResult, CONCAT(' UmbA=', LREAL_TO_STRING(stUmbA.rMeasuredValue)));
        sResult := CONCAT(sResult, CONCAT(' HMA=',  LREAL_TO_STRING(stHMA.rMeasuredValue)));
    END_IF

    IF _bEnableB THEN
        stUmbB  := _ipUmb_B.GetStatus();
        stHMB   := _ipHM_B.GetStatus();
        sResult := CONCAT(sResult, CONCAT(' UmbB=', LREAL_TO_STRING(stUmbB.rMeasuredValue)));
        sResult := CONCAT(sResult, CONCAT(' HMB=',  LREAL_TO_STRING(stHMB.rMeasuredValue)));
    END_IF

    _BuildGradientStatus := sResult;
END_METHOD

// Checks that the measured temperature ordering along one gas-delivery side is correct:
//   Head > Umbilical > HM (Hot Manifold)
// This ordering ensures no zone is colder than the one upstream of it — a colder
// downstream zone would cause gas condensation inside the line.
// Uses rMeasuredValue (live sensor reading) rather than bAtSetpoint, because the
// gradient being established is the physical completion criterion, not whether each
// zone has individually converged to its programmed number.
METHOD PRIVATE _SideGradientCorrect : BOOL
VAR_INPUT
    ipHead : I_Controller;   // Head — highest temperature in the path
    ipUmb  : I_Controller;   // Umbilical — midpoint
    ipHM   : I_Controller;   // Hot Manifold — lowest temperature in the path
END_VAR
VAR
    stHead : ST_ControllerStatus;
    stUmb  : ST_ControllerStatus;
    stHM   : ST_ControllerStatus;
END_VAR
    IF ipHead = 0 OR ipUmb = 0 OR ipHM = 0 THEN
        RETURN;   // Any null pointer means gradient cannot be verified — block completion
    END_IF

    stHead := ipHead.GetStatus();
    stUmb  := ipUmb.GetStatus();
    stHM   := ipHM.GetStatus();

    // Strict greater-than matches the reference implementation.
    // Gradient holds when each zone is measurably warmer than the one behind it.
    _SideGradientCorrect := (stHead.rMeasuredValue > stUmb.rMeasuredValue)
                        AND (stUmb.rMeasuredValue  > stHM.rMeasuredValue);
END_METHOD

METHOD PRIVATE _ControllerInError : BOOL
VAR_INPUT
    ipCtrl : I_Controller;
    sLabel : STRING(80);
END_VAR
VAR
    stStatus : ST_ControllerStatus;
END_VAR
    IF ipCtrl = 0 THEN RETURN; END_IF
    stStatus := ipCtrl.GetStatus();
    IF stStatus.stError.eErrorId <> E_ErrorId.NO_ERROR THEN
        _sErrorMsg := CONCAT(sLabel, ' error');
        _ControllerInError := TRUE;
    END_IF
END_METHOD

METHOD PRIVATE _ResetIfErrored
VAR_INPUT
    ipCtrl : I_Controller;
END_VAR
VAR
    stStatus : ST_ControllerStatus;
END_VAR
    IF ipCtrl = 0 THEN RETURN; END_IF
    stStatus := ipCtrl.GetStatus();
    IF stStatus.stError.eErrorId <> E_ErrorId.NO_ERROR THEN
        ipCtrl.Reset();
    END_IF
END_METHOD

END_FUNCTION_BLOCK
