// FB_ChuckVacuumReleaseSequence (Lib)
//
// Machine-agnostic chuck-vacuum release sequence. All hardware-specific
// behavior is delegated to the I_ChuckVacuumSystem implementation.
//
// SEQUENCE:
//   Idle           -> close all zones, BeginRelease()  (opens vent if any)
//   WaitingVacuum  -> until IsVacuumReleased OR timeout
//   On release    -> FinishRelease() (stops pump if dedicated)
//   Done / Error
FUNCTION_BLOCK FB_ChuckVacuumReleaseSequence IMPLEMENTS I_TreeNode
VAR
    _bInitialized    : BOOL := FALSE;
    _eState          : E_ChuckVacuumState := E_ChuckVacuumState.Idle;

    _ipChuckVacuum   : I_ChuckVacuumSystem;

    // Parameters
    _tTimeout                   : TIME  := T#1M;
    _rReleasePressureThreshold  : LREAL := 0.8;

    _fbTimer         : TON;
    _bError          : BOOL;
    _sErrorMsg       : STRING(255);
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
        GVL_ErrorLogger.Logger.LogError('FB_ChuckVacuumReleaseSequence', E_ErrorId.ERR_GEN_NULL_POINTER, 'Provider is null');
        RETURN;
    END_IF

    _ipChuckVacuum := ipProvider.GetChuckVacuumSystem();
    IF _ipChuckVacuum = 0 THEN
        GVL_ErrorLogger.Logger.LogError('FB_ChuckVacuumReleaseSequence', E_ErrorId.ERR_GEN_NULL_POINTER, 'Chuck vacuum subsystem not available');
        RETURN;
    END_IF

    IF NOT _ipChuckVacuum.IsInitialized() THEN
        GVL_ErrorLogger.Logger.LogError('FB_ChuckVacuumReleaseSequence', E_ErrorId.ERR_GEN_NULL_POINTER, 'Chuck vacuum subsystem not initialized');
        RETURN;
    END_IF

    _bInitialized := TRUE;
    Init := TRUE;
END_METHOD

METHOD PUBLIC SetParams
VAR_INPUT
    stParams : ST_ChuckVacuumRelease;
END_VAR
    IF NOT _bInitialized THEN RETURN; END_IF
    _tTimeout := stParams.tPressureTimeout;
    _rReleasePressureThreshold := REAL_TO_LREAL(stParams.fReleasePressureThreshold);
END_METHOD

METHOD PUBLIC Tick : ST_NodeResult
VAR
    i, nMax : INT;
END_VAR
    IF NOT _bInitialized THEN
        _sErrorMsg := 'Init must be called before Tick';
        _bError := TRUE;
        _eState := E_ChuckVacuumState.Error;
        Tick.eStatus := E_NodeStatus.FAILURE;
        Tick.bHasError := TRUE;
        Tick.stError.eErrorId := E_ErrorId.ERR_GEN_INITIALIZATION;
        Tick.stError.sSource := 'FB_ChuckVacuumReleaseSequence';
        Tick.stError.sMessage := _sErrorMsg;
        Tick.sDescription := _sErrorMsg;
        RETURN;
    END_IF

    CASE _eState OF
        E_ChuckVacuumState.Idle:
            _eState := E_ChuckVacuumState.OpeningValves;
            _fbTimer(IN := FALSE);

        E_ChuckVacuumState.OpeningValves:
            // Close every zone, then open vent (if any) via BeginRelease
            nMax := _ipChuckVacuum.GetMaxZoneCount();
            FOR i := 1 TO nMax DO
                _ipChuckVacuum.CloseZone(nZone := i);
            END_FOR
            _ipChuckVacuum.BeginRelease();
            _eState := E_ChuckVacuumState.WaitingVacuum;
            _fbTimer(IN := FALSE);

        E_ChuckVacuumState.WaitingVacuum:
            _fbTimer(IN := TRUE, PT := _tTimeout);
            IF _fbTimer.Q THEN
                _sErrorMsg := 'Timeout waiting for vacuum release';
                _bError := TRUE;
                _eState := E_ChuckVacuumState.Error;
            ELSIF _ipChuckVacuum.IsVacuumReleased(rThreshold := _rReleasePressureThreshold) THEN
                // Stop pump (no-op when pump is shared)
                _ipChuckVacuum.FinishRelease();
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
    Tick.sDescription := 'Releasing Vacuum';

    IF _eState = E_ChuckVacuumState.Done THEN
        Tick.eStatus := E_NodeStatus.SUCCESS;
        Tick.rProgress := 100.0;
        Tick.sDescription := 'Vacuum Released';
    ELSIF _eState = E_ChuckVacuumState.Error THEN
        Tick.eStatus := E_NodeStatus.FAILURE;
        Tick.bHasError := TRUE;
        Tick.stError.eErrorId := E_ErrorId.ERR_GEN_TIMEOUT;
        Tick.stError.sSource := 'FB_ChuckVacuumReleaseSequence';
        Tick.stError.sMessage := _sErrorMsg;
        Tick.stError.fExpectedValue := TO_REAL(_rReleasePressureThreshold);
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
