(*
FB_BubblerLeakTestSequence
Pressurizes the manifold through a specific bubbler, seals it, and monitors
pressure drop over a configurable test duration.

SEQUENCE:
  1. GasStop:     Ensure both sides shut down
  2. Pressurize:  Fill manifold via MFC + bubbler (CM -> HM path)
  3. Seal:        Close all valves, zero MFC
  4. Stabilize:   Wait for pressure to equalize
  5. Test:        Monitor pressure drop over testDuration
  6. Analyze:     Compare drop vs threshold
  7. Cleanup:     Close all valves
  8. Complete:    Report results

HARDWARE:
  - Side MFC + gas iso valve (carrier gas)
  - CM bubbler valve + CM purge valve (fill path)
  - HM bubbler valve + HM purge valve (propagation to HM)
  - CM/HM pressure sensors (monitor)

OUTPUT:
  - bLeakDetected, fColdManifoldPressureDrop, fHotManifoldPressureDrop
*)
FUNCTION_BLOCK FB_BubblerLeakTestSequence IMPLEMENTS I_TreeNode
VAR
    // Internal State
    _bInitialized    : BOOL := FALSE;
    _bParamsConfigured : BOOL := FALSE;
    _ePhase          : INT := 0;

    // Hardware References
    _ipGasIsoValve        : I_Valve;
    _ipCMBubblerValve     : I_Valve;
    _ipHMBubblerValve     : I_Valve;
    _ipCMPurgeValve       : I_Valve;
    _ipHMPurgeValve       : I_Valve;
    _ipCMVacuumIsoValve   : I_Valve;
    _ipHMVacuumIsoValve   : I_Valve;
    _ipCMPressureSensor   : I_Sensor;
    _ipHMPressureSensor   : I_Sensor;
    _ipMFCController      : I_Controller;

    // Gas stop
    _fbGasStop            : FB_GasStopSequence;

    // Parameters
    _stParams             : ST_CmdBubblerLeakTest;
    _eSide                : E_GasSide;

    // Measurement
    _rCMPressureInitial   : LREAL;
    _rHMPressureInitial   : LREAL;
    _rCMPressureFinal     : LREAL;
    _rHMPressureFinal     : LREAL;

    // Timing
    _fbTimer              : TON;

    // Status
    _bError               : BOOL;
    _sErrorMsg            : STRING(255);
END_VAR

// Output results (readable after completion)
VAR_OUTPUT
    bLeakDetected             : BOOL;
    bColdManifoldLeak         : BOOL;
    bHotManifoldLeak          : BOOL;
    fColdManifoldPressureDrop : LREAL;
    fHotManifoldPressureDrop  : LREAL;
    fPressureDrop             : LREAL;     // max of CM/HM
END_VAR

VAR CONSTANT
    PHASE_IDLE       : INT := 0;
    PHASE_GAS_STOP   : INT := 1;
    PHASE_FILL       : INT := 2;
    PHASE_SEAL       : INT := 3;
    PHASE_STABILIZE  : INT := 4;
    PHASE_TEST       : INT := 5;
    PHASE_ANALYZE    : INT := 6;
    PHASE_CLEANUP    : INT := 7;
    PHASE_DONE       : INT := 8;
    PHASE_ERROR      : INT := 99;
END_VAR

METHOD PUBLIC Init : BOOL
VAR_INPUT
    ipProvider    : I_HardwareProvider;
    eSide         : E_GasSide;
    nBubblerIndex : DINT;
END_VAR
    _bInitialized := FALSE;
    _bParamsConfigured := FALSE;
    _ePhase := PHASE_IDLE;
    _bError := FALSE;
    _sErrorMsg := '';
    _eSide := eSide;

    IF ipProvider = 0 THEN
        GVL_ErrorLogger.Logger.LogError('FB_BubblerLeakTestSequence', E_ErrorId.ERR_GEN_NULL_POINTER, 'Provider is null');
        RETURN;
    END_IF

    IF NOT _fbGasStop.Init(ipProvider := ipProvider, eSide := eSide) THEN RETURN; END_IF

    _ipGasIsoValve := ipProvider.GetGasIsoValve(eSide, nBubblerIndex);
    _ipCMBubblerValve := ipProvider.GetCMBubblerValve(eSide, nBubblerIndex);
    _ipHMBubblerValve := ipProvider.GetHMBubblerValve(eSide, nBubblerIndex);
    _ipCMPurgeValve := ipProvider.GetCMPurgeValve(eSide);
    _ipHMPurgeValve := ipProvider.GetHMPurgeValve(eSide);
    _ipCMVacuumIsoValve := ipProvider.GetCMVacuumIsoValve(eSide);
    _ipHMVacuumIsoValve := ipProvider.GetHMVacuumIsoValve(eSide);
    _ipCMPressureSensor := ipProvider.GetCMPressureSensor(eSide);
    _ipHMPressureSensor := ipProvider.GetHMPressureSensor(eSide);
    _ipMFCController := ipProvider.GetMFCController(eSide);

    IF _ipGasIsoValve = 0 OR _ipCMBubblerValve = 0 OR _ipHMBubblerValve = 0 OR
       _ipCMPurgeValve = 0 OR _ipHMPurgeValve = 0 OR
       _ipCMPressureSensor = 0 OR _ipHMPressureSensor = 0 OR _ipMFCController = 0 THEN
        GVL_ErrorLogger.Logger.LogError('FB_BubblerLeakTestSequence', E_ErrorId.ERR_GEN_NULL_POINTER, 'One or more hardware references are null');
        RETURN;
    END_IF

    _bInitialized := TRUE;
    Init := TRUE;
END_METHOD

METHOD PUBLIC SetParams
VAR_INPUT
    stParams : ST_CmdBubblerLeakTest;
END_VAR
    IF NOT _bInitialized THEN RETURN; END_IF
    _stParams := stParams;
    _bParamsConfigured := TRUE;
END_METHOD

METHOD PUBLIC Tick : ST_NodeResult
VAR
    stResult : ST_NodeResult;
END_VAR
    IF NOT _bInitialized THEN
        Tick.eStatus := E_NodeStatus.FAILURE;
        Tick.bHasError := TRUE;
        Tick.stError.eErrorId := E_ErrorId.ERR_GEN_INITIALIZATION;
        Tick.stError.sSource := 'FB_BubblerLeakTestSequence';
        Tick.stError.sMessage := 'Init must be called before Tick';
        Tick.sDescription := Tick.stError.sMessage;
        RETURN;
    END_IF

    IF NOT _bParamsConfigured THEN
        Tick.eStatus := E_NodeStatus.FAILURE;
        Tick.bHasError := TRUE;
        Tick.stError.eErrorId := E_ErrorId.ERR_GEN_NOT_CONFIGURED;
        Tick.stError.sSource := 'FB_BubblerLeakTestSequence';
        Tick.stError.sMessage := 'SetParams must be called before Tick';
        Tick.sDescription := Tick.stError.sMessage;
        RETURN;
    END_IF

    CASE _ePhase OF
        PHASE_IDLE:
            // Reset outputs
            bLeakDetected := FALSE;
            bColdManifoldLeak := FALSE;
            bHotManifoldLeak := FALSE;
            fColdManifoldPressureDrop := 0.0;
            fHotManifoldPressureDrop := 0.0;
            fPressureDrop := 0.0;
            _fbGasStop.Reset();
            _ePhase := PHASE_GAS_STOP;

        PHASE_GAS_STOP:
            stResult := _fbGasStop.Tick();
            IF stResult.eStatus = E_NodeStatus.SUCCESS THEN
                _ePhase := PHASE_FILL;
                _fbTimer(IN := FALSE);
            ELSIF stResult.eStatus = E_NodeStatus.FAILURE THEN
                _SetError('Gas stop failed');
            END_IF

        PHASE_FILL:
            // Open path: GasIso -> CMBubbler -> CMPurge -> HMPurge -> HMBubbler
            // Set MFC to test flow, wait for fill pressure
            IF NOT _fbTimer.IN THEN
                _ipMFCController.SetSetpoint(REAL_TO_LREAL(_stParams.testFlowRate));
                _ipGasIsoValve.Open();
                _ipCMBubblerValve.Open();
                _ipCMPurgeValve.Open();
                _ipHMPurgeValve.Open();
                _ipHMBubblerValve.Open();
                // Close exhaust paths
                IF _ipCMVacuumIsoValve <> 0 THEN _ipCMVacuumIsoValve.Close(); END_IF
                IF _ipHMVacuumIsoValve <> 0 THEN _ipHMVacuumIsoValve.Close(); END_IF
            END_IF
            _fbTimer(IN := TRUE, PT := DINT_TO_TIME(_stParams.pressureTimeout));
            IF _ipCMPressureSensor.GetMeasurement() >= REAL_TO_LREAL(_stParams.fillPressure) AND
               _ipHMPressureSensor.GetMeasurement() >= REAL_TO_LREAL(_stParams.fillPressure) THEN
                _ePhase := PHASE_SEAL;
                _fbTimer(IN := FALSE);
            ELSIF _fbTimer.Q THEN
                _SetError('Timeout waiting for fill pressure');
            END_IF

        PHASE_SEAL:
            // Close everything, zero MFC
            _ipMFCController.SetSetpoint(0.0);
            _ipGasIsoValve.Close();
            _ipCMBubblerValve.Close();
            _ipHMBubblerValve.Close();
            _ipCMPurgeValve.Close();
            _ipHMPurgeValve.Close();
            _fbTimer(IN := FALSE);
            _ePhase := PHASE_STABILIZE;

        PHASE_STABILIZE:
            _fbTimer(IN := TRUE, PT := DINT_TO_TIME(_stParams.stabilizationTime));
            IF _fbTimer.Q THEN
                // Record initial pressures
                _rCMPressureInitial := _ipCMPressureSensor.GetMeasurement();
                _rHMPressureInitial := _ipHMPressureSensor.GetMeasurement();
                _fbTimer(IN := FALSE);
                _ePhase := PHASE_TEST;
            END_IF

        PHASE_TEST:
            _fbTimer(IN := TRUE, PT := DINT_TO_TIME(_stParams.testDuration));
            IF _fbTimer.Q THEN
                // Record final pressures
                _rCMPressureFinal := _ipCMPressureSensor.GetMeasurement();
                _rHMPressureFinal := _ipHMPressureSensor.GetMeasurement();
                _fbTimer(IN := FALSE);
                _ePhase := PHASE_ANALYZE;
            END_IF

        PHASE_ANALYZE:
            // Calculate pressure drops
            fColdManifoldPressureDrop := _rCMPressureInitial - _rCMPressureFinal;
            fHotManifoldPressureDrop := _rHMPressureInitial - _rHMPressureFinal;

            bColdManifoldLeak := (fColdManifoldPressureDrop > REAL_TO_LREAL(_stParams.leakThreshold));
            bHotManifoldLeak := (fHotManifoldPressureDrop > REAL_TO_LREAL(_stParams.leakThreshold));
            bLeakDetected := bColdManifoldLeak OR bHotManifoldLeak;

            // Max drop for summary
            IF fColdManifoldPressureDrop > fHotManifoldPressureDrop THEN
                fPressureDrop := fColdManifoldPressureDrop;
            ELSE
                fPressureDrop := fHotManifoldPressureDrop;
            END_IF

            _ePhase := PHASE_CLEANUP;

        PHASE_CLEANUP:
            // Ensure all valves closed
            _ipCMBubblerValve.Close();
            _ipHMBubblerValve.Close();
            _ipCMPurgeValve.Close();
            _ipHMPurgeValve.Close();
            _ipGasIsoValve.Close();
            _ePhase := PHASE_DONE;

        PHASE_DONE:
            // Stay done

        PHASE_ERROR:
            // Stay in error
    END_CASE

    IF _ePhase = PHASE_DONE THEN
        Tick.eStatus := E_NodeStatus.SUCCESS;
        Tick.rProgress := 100.0;
        IF bLeakDetected THEN
            Tick.sDescription := CONCAT('LEAK DETECTED - Drop: ', LREAL_TO_STRING(fPressureDrop));
        ELSE
            Tick.sDescription := 'Leak test PASSED';
        END_IF
    ELSIF _ePhase = PHASE_ERROR THEN
        Tick.eStatus := E_NodeStatus.FAILURE;
        Tick.bHasError := TRUE;
        Tick.stError.eErrorId := E_ErrorId.ERR_GEN_INTERNAL;
        Tick.stError.sSource := 'FB_BubblerLeakTestSequence';
        Tick.stError.sMessage := _sErrorMsg;
        Tick.sDescription := _sErrorMsg;
    ELSE
        Tick.eStatus := E_NodeStatus.RUNNING;
        Tick.sDescription := CONCAT('Leak Test: Phase ', INT_TO_STRING(_ePhase));
    END_IF
END_METHOD

METHOD PRIVATE _SetError
VAR_INPUT
    sMsg : STRING(255);
END_VAR
    _sErrorMsg := sMsg;
    _bError := TRUE;
    _ePhase := PHASE_ERROR;
END_METHOD

METHOD PUBLIC Reset : BOOL
    _ePhase := PHASE_IDLE;
    _bError := FALSE;
    _sErrorMsg := '';
    _fbTimer(IN := FALSE);
    bLeakDetected := FALSE;
    bColdManifoldLeak := FALSE;
    bHotManifoldLeak := FALSE;
    fColdManifoldPressureDrop := 0.0;
    fHotManifoldPressureDrop := 0.0;
    fPressureDrop := 0.0;
    _fbGasStop.Reset();
    Reset := TRUE;
END_METHOD

METHOD PUBLIC Halt
    IF _ipMFCController <> 0 THEN _ipMFCController.SetSetpoint(0.0); END_IF
    IF _ipGasIsoValve <> 0 THEN _ipGasIsoValve.Close(); END_IF
    IF _ipCMBubblerValve <> 0 THEN _ipCMBubblerValve.Close(); END_IF
    IF _ipHMBubblerValve <> 0 THEN _ipHMBubblerValve.Close(); END_IF
    IF _ipCMPurgeValve <> 0 THEN _ipCMPurgeValve.Close(); END_IF
    IF _ipHMPurgeValve <> 0 THEN _ipHMPurgeValve.Close(); END_IF
    Reset();
END_METHOD

PROPERTY PUBLIC NodeName : STRING(50)
    GET
        NodeName := 'BubblerLeakTest';
    END_GET
END_PROPERTY

END_FUNCTION_BLOCK
