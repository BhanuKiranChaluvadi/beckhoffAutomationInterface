// FB_ChuckVacuumHoldSequence (Lib)
//
// Machine-agnostic chuck-vacuum hold sequence. All hardware-specific
// behavior (pump topology, vent valve presence, sensor type) is delegated
// to the machine-specific I_ChuckVacuumSystem implementation provided by
// the hardware provider.
//
// SEQUENCE:
//   Idle           -> PrepareForHold() then wait pump stabilize
//   VerifyingPump  -> after stabilize, open zone valves
//   OpeningValves  -> hierarchical open 1..nZoneCount, then watch sensor
//   WaitingVacuum  -> until IsVacuumHeld OR timeout
//   Done / Error
FUNCTION_BLOCK FB_ChuckVacuumHoldSequence IMPLEMENTS I_TreeNode
VAR
    _bInitialized    : BOOL := FALSE;
    _eState          : E_ChuckVacuumState := E_ChuckVacuumState.Idle;

    _ipChuckVacuum   : I_ChuckVacuumSystem;

    // Parameters
    _nZoneCount              : INT  := 1;
    _tTimeout                : TIME := T#1M;
    _rLockPressureThreshold  : LREAL := 0.65;

    _fbTimer         : TON;
    _bError          : BOOL;
    _sErrorMsg       : STRING(255);
END_VAR

VAR CONSTANT
    PUMP_STABILIZE_TIME : TIME := T#500MS;
END_VAR

METHOD PUBLIC Init : BOOL
VAR_INPUT
    ipProvider : I_HardwareProvider;
END_VAR
    _bInitialized := FALSE;
    _eState := E_ChuckVacuumState.Idle;
    _bError := FALSE;
    _sErrorMsg := '';

    IF ipProvider = 0 THEN
        GVL_ErrorLogger.Logger.LogError('FB_ChuckVacuumHoldSequence', E_ErrorId.ERR_GEN_NULL_POINTER, 'Provider is null');
        RETURN;
    END_IF

    _ipChuckVacuum := ipProvider.GetChuckVacuumSystem();
    IF _ipChuckVacuum = 0 THEN
        GVL_ErrorLogger.Logger.LogError('FB_ChuckVacuumHoldSequence', E_ErrorId.ERR_GEN_NULL_POINTER, 'Chuck vacuum subsystem not available');
        RETURN;
    END_IF

    IF NOT _ipChuckVacuum.IsInitialized() THEN
        GVL_ErrorLogger.Logger.LogError('FB_ChuckVacuumHoldSequence', E_ErrorId.ERR_GEN_NULL_POINTER, 'Chuck vacuum subsystem not initialized');
        RETURN;
    END_IF

    _bInitialized := TRUE;
    Init := TRUE;
END_METHOD

METHOD PUBLIC SetParams
VAR_INPUT
    stParams : ST_ChuckVacuumHold;
END_VAR
VAR
    nMax : INT;
END_VAR
    IF NOT _bInitialized THEN RETURN; END_IF
    nMax := _ipChuckVacuum.GetMaxZoneCount();
    _nZoneCount := LIMIT(1, stParams.iSelectZone, nMax);
    _tTimeout := stParams.tPressureTimeout;
    _rLockPressureThreshold := REAL_TO_LREAL(stParams.fLockPressureThreshold);
END_METHOD

METHOD PUBLIC Tick : ST_NodeResult
VAR
    i : INT;
END_VAR
    IF NOT _bInitialized THEN
        _sErrorMsg := 'Init must be called before Tick';
        _bError := TRUE;
        _eState := E_ChuckVacuumState.Error;
        Tick.eStatus := E_NodeStatus.FAILURE;
        Tick.bHasError := TRUE;
        Tick.stError.eErrorId := E_ErrorId.ERR_GEN_INITIALIZATION;
        Tick.stError.sSource := 'FB_ChuckVacuumHoldSequence';
        Tick.stError.sMessage := _sErrorMsg;
        Tick.sDescription := _sErrorMsg;
        RETURN;
    END_IF

    CASE _eState OF
        E_ChuckVacuumState.Idle:
            // Close vent (if any), ensure pump source is active
            _ipChuckVacuum.PrepareForHold();
            _eState := E_ChuckVacuumState.VerifyingPump;
            _fbTimer(IN := FALSE);

        E_ChuckVacuumState.VerifyingPump:
            // Brief stabilization window for pump/vent valve to settle
            _fbTimer(IN := TRUE, PT := PUMP_STABILIZE_TIME);
            IF _fbTimer.Q THEN
                _fbTimer(IN := FALSE);
                _eState := E_ChuckVacuumState.OpeningValves;
            END_IF

        E_ChuckVacuumState.OpeningValves:
            // Open zone valves hierarchically (innermost first)
            FOR i := 1 TO _nZoneCount DO
                _ipChuckVacuum.OpenZone(nZone := i);
            END_FOR
            _eState := E_ChuckVacuumState.WaitingVacuum;
            _fbTimer(IN := FALSE);

        E_ChuckVacuumState.WaitingVacuum:
            _fbTimer(IN := TRUE, PT := _tTimeout);
            IF _fbTimer.Q THEN
                _sErrorMsg := 'Timeout waiting for vacuum';
                _bError := TRUE;
                _eState := E_ChuckVacuumState.Error;
            ELSIF _ipChuckVacuum.IsVacuumHeld(nActiveZoneCount := _nZoneCount, rThreshold := _rLockPressureThreshold) THEN
                _eState := E_ChuckVacuumState.Done;
            END_IF

        E_ChuckVacuumState.Done:
            ; // terminal
        E_ChuckVacuumState.Error:
            ; // terminal until Reset
    END_CASE

    Tick.eStatus := E_NodeStatus.RUNNING;
    Tick.rProgress := 0.0;
    Tick.bHasError := _bError;
    Tick.sDescription := 'Applying Vacuum';

    IF _eState = E_ChuckVacuumState.Done THEN
        Tick.eStatus := E_NodeStatus.SUCCESS;
        Tick.rProgress := 100.0;
        Tick.sDescription := 'Vacuum Held';
    ELSIF _eState = E_ChuckVacuumState.Error THEN
        Tick.eStatus := E_NodeStatus.FAILURE;
        Tick.bHasError := TRUE;
        Tick.stError.eErrorId := E_ErrorId.ERR_GEN_TIMEOUT;
        Tick.stError.sSource := 'FB_ChuckVacuumHoldSequence';
        Tick.stError.sMessage := _sErrorMsg;
        Tick.stError.fExpectedValue := TO_REAL(_rLockPressureThreshold);
        Tick.stError.sUnits := 'bar';
        Tick.sDescription := _sErrorMsg;
    END_IF
END_METHOD

METHOD PUBLIC GetState : E_ChuckVacuumState
    GetState := _eState;
END_METHOD

METHOD PUBLIC Reset : BOOL
    _eState := E_ChuckVacuumState.Idle;
    _bError := FALSE;
    _sErrorMsg := '';
    _fbTimer(IN := FALSE);
    IF _ipChuckVacuum <> 0 THEN
        _ipChuckVacuum.Reset();
    END_IF
    Reset := TRUE;
END_METHOD

METHOD PUBLIC Halt
    Reset();
END_METHOD

END_FUNCTION_BLOCK
