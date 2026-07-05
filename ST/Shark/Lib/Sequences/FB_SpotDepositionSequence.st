(*
FB_SpotDepositionSequence
ALD spot deposition cycle: alternating precursor/reactant exposures
separated by inert gas purges.

CYCLE STRUCTURE (per deposition cycle):
  Inert Purge -> Reactant Exposure -> Inert Purge -> Precursor Exposure

FULL SEQUENCE:
  1. Init:       Prepare exhaust vacuum
  2. Startup A:  Start precursor gas (A-Side) via FB_GasStartSequence
  3. Startup B:  Start reactant gas (B-Side) via FB_GasStartSequence
  4. Cycles:     N x (InertPurge -> ReactantExpose -> InertPurge -> PrecursorExpose)
  5. FinalPurge: Extended inert purge to clear lines
  6. Shutdown:   Stop both sides via FB_GasStopSequence

HARDWARE:
  - A-Side: Precursor MFC + bubbler + valves
  - B-Side: Reactant MFC + bubbler + valves
  - Inert:  IG MFC + inert gas valve (purge between exposures)
  - Common: Exhaust pump, nozzle (start) valves
*)
FUNCTION_BLOCK FB_SpotDepositionSequence IMPLEMENTS I_TreeNode
VAR
    // Internal State
    _bInitialized    : BOOL := FALSE;
    _bParamsConfigured : BOOL := FALSE;
    _ePhase          : INT := 0;

    // Sub-sequences
    _fbPrepareExhaust  : FB_PrepareExhaustSequence;
    _fbGasStartA       : FB_GasStartSequence;
    _fbGasStartB       : FB_GasStartSequence;
    _fbGasStopA        : FB_GasStopSequence;
    _fbGasStopB        : FB_GasStopSequence;

    // Inert gas hardware
    _ipInertValve      : I_Valve;
    _ipInertMFC        : I_Controller;
    _ipNozzleA         : I_Valve;
    _ipNozzleB         : I_Valve;

    // Cycle tracking
    _nCurrentCycle     : DINT;
    _nCycleSubPhase    : INT;

    // Parameters
    _stParams          : ST_CmdSpotDeposition;

    // Timing
    _fbTimer           : TON;

    // Status
    _bError            : BOOL;
    _sErrorMsg         : STRING(255);
END_VAR

VAR CONSTANT
    PHASE_IDLE           : INT := 0;
    PHASE_INIT           : INT := 1;
    PHASE_STARTUP_A      : INT := 2;
    PHASE_STARTUP_B      : INT := 3;
    PHASE_CYCLES         : INT := 4;
    PHASE_FINAL_PURGE    : INT := 5;
    PHASE_SHUTDOWN       : INT := 6;
    PHASE_DONE           : INT := 7;
    PHASE_ERROR          : INT := 99;

    SUB_INERT_PURGE_1    : INT := 0;
    SUB_REACTANT_EXPOSE  : INT := 1;
    SUB_INERT_PURGE_2    : INT := 2;
    SUB_PRECURSOR_EXPOSE : INT := 3;
END_VAR

METHOD PUBLIC Init : BOOL
VAR_INPUT
    ipProvider : I_HardwareProvider;
END_VAR
    _bInitialized := FALSE;
    _bParamsConfigured := FALSE;
    _ePhase := PHASE_IDLE;
    _bError := FALSE;
    _sErrorMsg := '';

    IF ipProvider = 0 THEN
        GVL_ErrorLogger.Logger.LogError('FB_SpotDepositionSequence', E_ErrorId.ERR_GEN_NULL_POINTER, 'Provider is null');
        RETURN;
    END_IF

    IF NOT _fbPrepareExhaust.Init(ipProvider, E_GasSide.A_SIDE) THEN RETURN; END_IF
    IF NOT _fbGasStartA.Init(ipProvider := ipProvider, eSide := E_GasSide.A_SIDE) THEN RETURN; END_IF
    IF NOT _fbGasStartB.Init(ipProvider := ipProvider, eSide := E_GasSide.B_SIDE) THEN RETURN; END_IF
    IF NOT _fbGasStopA.Init(ipProvider := ipProvider, eSide := E_GasSide.A_SIDE) THEN RETURN; END_IF
    IF NOT _fbGasStopB.Init(ipProvider := ipProvider, eSide := E_GasSide.B_SIDE) THEN RETURN; END_IF

    _ipInertValve := ipProvider.GetInertGasValve();
    _ipInertMFC := ipProvider.GetInertMFCController();
    _ipNozzleA := ipProvider.GetNozzleIsoValve(E_GasSide.A_SIDE);
    _ipNozzleB := ipProvider.GetNozzleIsoValve(E_GasSide.B_SIDE);

    IF _ipInertValve = 0 OR _ipInertMFC = 0 OR _ipNozzleA = 0 OR _ipNozzleB = 0 THEN
        GVL_ErrorLogger.Logger.LogError('FB_SpotDepositionSequence', E_ErrorId.ERR_GEN_NULL_POINTER, 'Inert gas or nozzle hardware not found');
        RETURN;
    END_IF

    _bInitialized := TRUE;
    Init := TRUE;
END_METHOD

METHOD PUBLIC SetParams
VAR_INPUT
    stParams : ST_CmdSpotDeposition;
END_VAR
VAR
    stStartA : ST_CmdStartBubblerFlow;
    stStartB : ST_CmdStartBubblerFlow;
END_VAR
    IF NOT _bInitialized THEN RETURN; END_IF

    _stParams := stParams;

    stStartA.flowRate := stParams.fPrecursorFlowRate;
    stStartA.bubblerIndex := DINT_TO_INT(stParams.nPrecursorBubblerIndex);
    stStartA.coldManifoldOperatingPressure := stParams.fDistributorOperatingPressure;
    stStartA.hotManifoldOperatingPressure := stParams.fCombinerOperatingPressure;
    stStartA.MFCFlowRateTolerance := stParams.fPressureTolerance;
    _fbGasStartA.SetParams(stStartA);

    stStartB.flowRate := stParams.fReactantFlowRate;
    stStartB.bubblerIndex := DINT_TO_INT(stParams.nReactantBubblerIndex);
    stStartB.coldManifoldOperatingPressure := stParams.fDistributorOperatingPressure;
    stStartB.hotManifoldOperatingPressure := stParams.fCombinerOperatingPressure;
    stStartB.MFCFlowRateTolerance := stParams.fPressureTolerance;
    _fbGasStartB.SetParams(stStartB);
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
        Tick.stError.sSource := 'FB_SpotDepositionSequence';
        Tick.stError.sMessage := 'Init must be called before Tick';
        Tick.sDescription := Tick.stError.sMessage;
        RETURN;
    END_IF

    IF NOT _bParamsConfigured THEN
        Tick.eStatus := E_NodeStatus.FAILURE;
        Tick.bHasError := TRUE;
        Tick.stError.eErrorId := E_ErrorId.ERR_GEN_NOT_CONFIGURED;
        Tick.stError.sSource := 'FB_SpotDepositionSequence';
        Tick.stError.sMessage := 'SetParams must be called before Tick';
        Tick.sDescription := Tick.stError.sMessage;
        RETURN;
    END_IF

    CASE _ePhase OF
        PHASE_IDLE:
            _ePhase := PHASE_INIT;
            _fbPrepareExhaust.Reset();

        PHASE_INIT:
            stResult := _fbPrepareExhaust.Tick();
            IF stResult.eStatus = E_NodeStatus.SUCCESS THEN
                _fbGasStartA.Reset();
                _ePhase := PHASE_STARTUP_A;
            ELSIF stResult.eStatus = E_NodeStatus.FAILURE THEN
                _SetError('Exhaust preparation failed');
            END_IF

        PHASE_STARTUP_A:
            stResult := _fbGasStartA.Tick();
            IF stResult.eStatus = E_NodeStatus.SUCCESS THEN
                _fbGasStartB.Reset();
                _ePhase := PHASE_STARTUP_B;
            ELSIF stResult.eStatus = E_NodeStatus.FAILURE THEN
                _SetError('Precursor gas start failed');
            END_IF

        PHASE_STARTUP_B:
            stResult := _fbGasStartB.Tick();
            IF stResult.eStatus = E_NodeStatus.SUCCESS THEN
                _nCurrentCycle := 1;
                _nCycleSubPhase := SUB_INERT_PURGE_1;
                _fbTimer(IN := FALSE);
                _ePhase := PHASE_CYCLES;
            ELSIF stResult.eStatus = E_NodeStatus.FAILURE THEN
                _SetError('Reactant gas start failed');
            END_IF

        PHASE_CYCLES:
            _ExecuteCycle();

        PHASE_FINAL_PURGE:
            _fbTimer(IN := TRUE, PT := DINT_TO_TIME(_stParams.tFinalPurgeTime));
            IF _fbTimer.Q THEN
                _ipInertMFC.SetSetpoint(0.0);
                _ipInertValve.Close();
                _fbGasStopA.Reset();
                _fbGasStopB.Reset();
                _ePhase := PHASE_SHUTDOWN;
            END_IF

        PHASE_SHUTDOWN:
            stResult := _fbGasStopA.Tick();
            IF stResult.eStatus = E_NodeStatus.SUCCESS THEN
                stResult := _fbGasStopB.Tick();
                IF stResult.eStatus = E_NodeStatus.SUCCESS THEN
                    _ePhase := PHASE_DONE;
                ELSIF stResult.eStatus = E_NodeStatus.FAILURE THEN
                    _SetError('B-Side gas stop failed');
                END_IF
            ELSIF stResult.eStatus = E_NodeStatus.FAILURE THEN
                _SetError('A-Side gas stop failed');
            ELSE
                _fbGasStopB.Tick();
            END_IF

        PHASE_DONE:
            ;

        PHASE_ERROR:
            ;
    END_CASE

    IF _ePhase = PHASE_DONE THEN
        Tick.eStatus := E_NodeStatus.SUCCESS;
        Tick.rProgress := 100.0;
        Tick.sDescription := 'Spot Deposition Complete';
    ELSIF _ePhase = PHASE_ERROR THEN
        Tick.eStatus := E_NodeStatus.FAILURE;
        Tick.bHasError := TRUE;
        Tick.stError.eErrorId := E_ErrorId.ERR_GEN_INTERNAL;
        Tick.stError.sSource := 'FB_SpotDepositionSequence';
        Tick.stError.sMessage := _sErrorMsg;
        Tick.sDescription := _sErrorMsg;
    ELSE
        Tick.eStatus := E_NodeStatus.RUNNING;
        Tick.sDescription := CONCAT('Spot Deposition: Phase ', INT_TO_STRING(_ePhase));
        IF _ePhase = PHASE_CYCLES AND _stParams.nDepositionCycles > 0 THEN
            Tick.rProgress := 10.0 + TO_REAL((DINT_TO_LREAL(_nCurrentCycle - 1) / DINT_TO_LREAL(_stParams.nDepositionCycles)) * 80.0);
        END_IF
    END_IF
END_METHOD

METHOD PRIVATE _ExecuteCycle
    CASE _nCycleSubPhase OF
        SUB_INERT_PURGE_1, SUB_INERT_PURGE_2:
            IF NOT _fbTimer.IN THEN
                _ipNozzleA.Close();
                _ipNozzleB.Close();
                _ipInertValve.Open();
                _ipInertMFC.SetSetpoint(REAL_TO_LREAL(_stParams.fInertMaxPurgeFlow));
            END_IF
            _fbTimer(IN := TRUE, PT := DINT_TO_TIME(_stParams.tInertPurgeTime));
            IF _fbTimer.Q THEN
                _fbTimer(IN := FALSE);
                _ipInertMFC.SetSetpoint(REAL_TO_LREAL(_stParams.fInertMinPurgeFlow));
                _ipInertValve.Close();
                _nCycleSubPhase := _nCycleSubPhase + 1;
            END_IF

        SUB_REACTANT_EXPOSE:
            IF NOT _fbTimer.IN THEN
                _ipNozzleB.Open();
            END_IF
            _fbTimer(IN := TRUE, PT := DINT_TO_TIME(_stParams.tReactantExposureTime));
            IF _fbTimer.Q THEN
                _fbTimer(IN := FALSE);
                _ipNozzleB.Close();
                _nCycleSubPhase := _nCycleSubPhase + 1;
            END_IF

        SUB_PRECURSOR_EXPOSE:
            IF NOT _fbTimer.IN THEN
                _ipNozzleA.Open();
            END_IF
            _fbTimer(IN := TRUE, PT := DINT_TO_TIME(_stParams.tPrecursorExposureTime));
            IF _fbTimer.Q THEN
                _fbTimer(IN := FALSE);
                _ipNozzleA.Close();
                _nCurrentCycle := _nCurrentCycle + 1;
                IF _nCurrentCycle > _stParams.nDepositionCycles THEN
                    _ipInertValve.Open();
                    _ipInertMFC.SetSetpoint(REAL_TO_LREAL(_stParams.fInertMaxPurgeFlow));
                    _fbTimer(IN := FALSE);
                    _ePhase := PHASE_FINAL_PURGE;
                ELSE
                    _nCycleSubPhase := SUB_INERT_PURGE_1;
                END_IF
            END_IF
    END_CASE
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
    _nCurrentCycle := 0;
    _nCycleSubPhase := 0;
    _fbTimer(IN := FALSE);
    _fbPrepareExhaust.Reset();
    _fbGasStartA.Reset();
    _fbGasStartB.Reset();
    _fbGasStopA.Reset();
    _fbGasStopB.Reset();
    Reset := TRUE;
END_METHOD

METHOD PUBLIC Halt
    IF _ipInertMFC <> 0 THEN _ipInertMFC.SetSetpoint(0.0); END_IF
    IF _ipInertValve <> 0 THEN _ipInertValve.Close(); END_IF
    IF _ipNozzleA <> 0 THEN _ipNozzleA.Close(); END_IF
    IF _ipNozzleB <> 0 THEN _ipNozzleB.Close(); END_IF
    _fbGasStopA.Halt();
    _fbGasStopB.Halt();
    Reset();
END_METHOD

PROPERTY PUBLIC NodeName : STRING(50)
    GET
        NodeName := 'SpotDeposition';
    END_GET
END_PROPERTY

END_FUNCTION_BLOCK
