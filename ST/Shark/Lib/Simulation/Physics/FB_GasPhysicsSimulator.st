(* FB_GasPhysicsSimulator - Physics-based gas system simulation

PRESSURE SOURCES (in priority order):
1. Vacuum (exhaust pump) - ALWAYS WINS when active
2. Gas inlet (via MFC) - Pressurizes system
3. Atmosphere (via nozzle) - 1.0 bar reference

PHYSICS RULES:

1. Exhaust Pressure:
- Sensor sits between pump and exhaust iso valve
- Low (vacuum) when: Pump ON (iso valve does not gate the sensor)
- Otherwise: Atmospheric

2. CM Pressure (Cold Manifold):
- Gas path: Gas Source → Gas Iso → MFC → CM Pressure Sensor
- If evacuating (CM Vacuum Iso open): → Vacuum
- If gas flowing AND path to HM AND HM has outlet: → HM pressure (connected vessels)
- If gas flowing AND no outlet: → Max pressure (builds up)
- Otherwise: → Atmospheric

3. HM Pressure (Hot Manifold):
- Gas path from CM: (Bubbler path OR Purge path) → HM → PCV → Nozzle Iso → Atmosphere
- If evacuating (HM Vacuum Iso open): → Vacuum
- If gas input from CM:
- If no outlet (Nozzle closed OR PCV=3): → CM pressure (equalizes)
- If PCV=0 (fully open/purge): → Atmospheric
- If PCV regulating (0 < setpoint < 3): → PCV setpoint
- Otherwise: → Atmospheric

4. MFC Actual Flow:
- Equals setpoint only when Gas Iso valve open
- Returns 0 when Gas Iso closed

CONFLICT RESOLUTION:
When both gas AND exhaust paths active: EXHAUST WINS + log warning

PCV (Pressure Control Valve) Behavior:
- Setpoint = 0: Fully open (purge position) → HM open to atmosphere
- Setpoint = 3: Fully closed → No outlet, pressure builds
- 0 < Setpoint < 3: Regulating → HM maintains at setpoint
*)
FUNCTION_BLOCK FB_GasPhysicsSimulator
VAR_INPUT
    // System state
    bPumpOn : BOOL;                     // Exhaust pump running
    bExhaustVacuumIsoOpen : BOOL;       // Main exhaust valve state

    // Side states (DRY: structured inputs instead of 30+ individual BOOLs)
    stASide : ST_GasSideState;          // A-Side valve/controller states
    stBSide : ST_GasSideState;          // B-Side valve/controller states

    // Configuration
    rAtmosphericPressure : LREAL := 1.0;    // bar (ambient)
    rVacuumPressure : LREAL := 0.001;       // bar (good vacuum)
    rGasInletPressure : LREAL := 3.0;       // bar (gas source pressure - equilibrium when blocked)
    rFlowStabilizedPressure : LREAL := 1.4; // bar (steady-state when gas flows with PCV fully open)
    // Evacuation: 1 bar → 0 bar in ~5 sec = 1 bar / (5s / 0.01s) = 1 / 500 = 0.002 bar/cycle
    rEvacuationRampRate : LREAL := 0.002;   // bar/cycle
    // Pressure build: 2 bar in ~1 min = 2 bar / (60s / 0.01s) = 2 / 6000 ≈ 0.00035 bar/cycle
    rPressureBuildRate : LREAL := 0.00035;  // bar/cycle
    rLeakRate : LREAL := 0.0000;            // bar/cycle (slow leak when isolated)
    rFlowRampRate : LREAL := 0.5;           // sccm/cycle (simulation speed)
    rPCVFullyOpen : LREAL := 0.0;           // PCV setpoint for fully open (purge)
    rPCVFullyClosed : LREAL := 3.0;         // PCV setpoint for fully closed
    rBubblerHeadLoss : LREAL := 0.15;       // bar, pressure drop CM→HM through liquid column
    rMFCFlowThreshold : LREAL := 0.5;       // sccm — minimum flow considered "gas flowing"
END_VAR

VAR_OUTPUT
    // Side outputs (DRY: structured outputs)
    stASideOut : ST_GasSideOutput;      // A-Side simulated values
    stBSideOut : ST_GasSideOutput;      // B-Side simulated values

    // Common simulated values
    rExhaustPressure : LREAL;           // Exhaust line pressure
END_VAR

VAR
    _bInitialized : BOOL := FALSE;

    // Internal states (for ramping)
    _stASide : ST_GasSideOutput;
    _stBSide : ST_GasSideOutput;
    _rExhaustPressure : LREAL;
END_VAR

    // =============================================================================
    // MAIN EXECUTION
    // =============================================================================
METHOD Execute
    IF NOT _bInitialized THEN
        _Init();
    END_IF

    // 1. Simulate exhaust system
    _SimulateExhaust();

    // 2. Simulate both sides (DRY: same method, different state structures)
    _SimulateSide(stState := stASide, stOutput := _stASide);
    _SimulateSide(stState := stBSide, stOutput := _stBSide);

    // 3. Export to outputs
    stASideOut := _stASide;
    stBSideOut := _stBSide;
    rExhaustPressure := _rExhaustPressure;
END_METHOD

    // =============================================================================
    // INITIALIZATION
    // =============================================================================
METHOD PRIVATE _Init
    // Start at atmospheric pressure (system at rest)
    _stASide.rCMPressure := rAtmosphericPressure;
    _stASide.rHMPressure := rAtmosphericPressure;
    _stASide.rMFCActual := 0.0;

    _stBSide.rCMPressure := rAtmosphericPressure;
    _stBSide.rHMPressure := rAtmosphericPressure;
    _stBSide.rMFCActual := 0.0;

    _rExhaustPressure := rAtmosphericPressure;

    _bInitialized := TRUE;
END_METHOD

    // =============================================================================
    // EXHAUST SIMULATION
    // =============================================================================
METHOD PRIVATE _SimulateExhaust
VAR
    rTarget : LREAL;
    rRate : LREAL;
END_VAR
    // Sensor sits between pump and iso valve: <Pump> — <Sensor> — <Iso Valve> — <System>
    // Pump ON → vacuum at sensor, regardless of iso valve state
    IF bPumpOn THEN
        rTarget := rVacuumPressure;
        rRate := rEvacuationRampRate;
    ELSE
        rTarget := rAtmosphericPressure;
        rRate := rPressureBuildRate;
    END_IF

    _rExhaustPressure := _RampToward(_rExhaustPressure, rTarget, rRate);
END_METHOD

    // =============================================================================
    // UNIFIED SIDE SIMULATION (DRY: handles both A and B sides)
    // =============================================================================
METHOD PRIVATE _SimulateSide
VAR_INPUT
    stState : ST_GasSideState;
END_VAR
VAR_IN_OUT
    stOutput : ST_GasSideOutput;
END_VAR
VAR
    bGasFlowing : BOOL;
    bBubblerPathOpen : BOOL;
    bPurgePathOpen : BOOL;
    bPathOpenToHM : BOOL;
    bHMHasOutlet : BOOL;
    bPCVFullyClosedState : BOOL;
    bCMEvacuating : BOOL;
    bHMEvacuating : BOOL;
    rHMTarget : LREAL;
END_VAR
    // === MFC Flow Simulation ===
    IF stState.bGasIsoOpen THEN
        stOutput.rMFCActual := _RampToward(stOutput.rMFCActual, stState.rMFCSetpoint, rFlowRampRate);
    ELSE
        stOutput.rMFCActual := _RampToward(stOutput.rMFCActual, 0.0, rFlowRampRate);
    END_IF
    bGasFlowing := stOutput.rMFCActual > rMFCFlowThreshold;

    // === Path Analysis ===
    bBubblerPathOpen := (stState.bCMBubbler1Open AND stState.bHMBubbler1Open) OR
                        (stState.bCMBubbler2Open AND stState.bHMBubbler2Open) OR
                        (stState.bCMBubbler3Open AND stState.bHMBubbler3Open);
    bPurgePathOpen := stState.bCMPurgeOpen AND stState.bHMPurgeOpen;
    bPathOpenToHM := bBubblerPathOpen OR bPurgePathOpen;

    bPCVFullyClosedState := stState.rPCVSetpoint >= rPCVFullyClosed - 0.1;
    bHMHasOutlet := stState.bNozzleIsoOpen AND (NOT bPCVFullyClosedState);

    // === Evacuation States ===
    // Full path: Manifold → Purge Valve → Vacuum Iso → Exhaust Line → Exhaust Vacuum Iso → Pump
    bCMEvacuating := stState.bCMPurgeOpen AND stState.bCMVacuumIsoOpen AND bExhaustVacuumIsoOpen AND bPumpOn;
    bHMEvacuating := stState.bHMPurgeOpen AND stState.bHMVacuumIsoOpen AND bExhaustVacuumIsoOpen AND bPumpOn;

    // === Manifold Pressure Simulation ===
    // HM first — CM depends on HM target for connected-vessel physics
    rHMTarget := _SimulateHMPressure(
        bHMGasInput   := bGasFlowing AND bPathOpenToHM,
        bHMEvacuating := bHMEvacuating,
        bHMHasOutlet  := bHMHasOutlet,
        bPCVFullyOpen := stState.rPCVSetpoint <= rPCVFullyOpen + 0.1,
        rPCVSetpoint  := stState.rPCVSetpoint,
        bPathOpenToHM := bPathOpenToHM,
        rCMPressure   := stOutput.rCMPressure,
        rHMPressure   := stOutput.rHMPressure);

    _SimulateCMPressure(
        bCMGasInput   := bGasFlowing,
        bCMEvacuating := bCMEvacuating,
        bPathOpenToHM := bPathOpenToHM,
        bHMHasOutlet  := bHMHasOutlet,
        rHMTarget     := rHMTarget,
        rHMPressure   := stOutput.rHMPressure,
        rCMPressure   := stOutput.rCMPressure);
END_METHOD

    // =============================================================================
    // HM PRESSURE — Priority: Exhaust > Gas Input > Open-to-atm > Equalize > Stagnant
    // =============================================================================

(* Determine HM target pressure and ramp toward it.
   Returns the HM target (needed by CM for connected-vessel physics). *)
METHOD PRIVATE _SimulateHMPressure : LREAL
VAR_INPUT
    bHMGasInput   : BOOL;
    bHMEvacuating : BOOL;
    bHMHasOutlet  : BOOL;
    bPCVFullyOpen : BOOL;
    rPCVSetpoint  : LREAL;
    bPathOpenToHM : BOOL;
    rCMPressure   : LREAL;     // Current CM pressure (for equalization)
END_VAR
VAR_IN_OUT
    rHMPressure : LREAL;
END_VAR
VAR
    rTarget : LREAL;
    rRate   : LREAL := rPressureBuildRate;
END_VAR
    IF bHMGasInput AND bHMEvacuating THEN
        // CONFLICT: Gas flowing while exhaust open — exhaust wins
        rTarget := rVacuumPressure;
        rRate := rEvacuationRampRate;
    ELSIF bHMEvacuating THEN
        rTarget := rVacuumPressure;
        rRate := rEvacuationRampRate;
    ELSIF bHMGasInput THEN
        IF NOT bHMHasOutlet THEN
            // No outlet (nozzle closed OR PCV fully closed): equilibrates at gas inlet pressure
            rTarget := rGasInletPressure;
        ELSIF bPCVFullyOpen THEN
            // PCV fully open: stabilizes at elevated pressure due to flow resistance
            rTarget := rFlowStabilizedPressure;
        ELSE
            // PCV regulating: HM reaches PCV setpoint (back-pressure)
            rTarget := rPCVSetpoint;
        END_IF
    ELSIF bHMHasOutlet THEN
        // No gas input but open to atmosphere: drifts to atmospheric
        rTarget := rAtmosphericPressure;
    ELSIF bPathOpenToHM THEN
        // Connected to CM — equalize
        rTarget := rCMPressure;
    ELSE
        // Stagnant: isolated zone holds pressure
        rTarget := rHMPressure;
        rRate := 0.0;
    END_IF

    rHMPressure := _RampToward(rHMPressure, rTarget, rRate);
    _SimulateHMPressure := rTarget;
END_METHOD

    // =============================================================================
    // CM PRESSURE — Priority: Exhaust > Gas Input > Equalize > Stagnant
    // =============================================================================

(* Determine CM target pressure and ramp toward it. *)
METHOD PRIVATE _SimulateCMPressure
VAR_INPUT
    bCMGasInput   : BOOL;
    bCMEvacuating : BOOL;
    bPathOpenToHM : BOOL;
    bHMHasOutlet  : BOOL;
    rHMTarget     : LREAL;     // HM target from _SimulateHMPressure
    rHMPressure   : LREAL;     // Current HM pressure (for equalization)
END_VAR
VAR_IN_OUT
    rCMPressure : LREAL;
END_VAR
VAR
    rTarget : LREAL;
    rRate   : LREAL := rPressureBuildRate;
END_VAR
    IF bCMGasInput AND bCMEvacuating THEN
        // CONFLICT: Gas flowing while exhaust open — exhaust wins
        rTarget := rVacuumPressure;
        rRate := rEvacuationRampRate;
    ELSIF bCMEvacuating THEN
        rTarget := rVacuumPressure;
        rRate := rEvacuationRampRate;
    ELSIF bCMGasInput THEN
        // CM is upstream of HM — gas pushes through, CM doesn't wait for HM
        IF bPathOpenToHM AND bHMHasOutlet THEN
            // Connected vessels: CM = HM target + head loss (bubbler restriction)
            rTarget := rHMTarget + rBubblerHeadLoss;
            IF rTarget > rGasInletPressure THEN rTarget := rGasInletPressure; END_IF
        ELSE
            // HM blocked or CM isolated: builds to gas inlet pressure
            rTarget := rGasInletPressure;
        END_IF
    ELSIF bPathOpenToHM THEN
        // No gas input but connected to HM: equalize with HM
        rTarget := rHMPressure;
    ELSE
        // Stagnant: isolated zone holds pressure
        rTarget := rCMPressure;
        rRate := 0.0;
    END_IF

    rCMPressure := _RampToward(rCMPressure, rTarget, rRate);
END_METHOD

    // =============================================================================
    // UTILITY: Ramp value toward target
    // =============================================================================
METHOD PRIVATE _RampToward : LREAL
VAR_INPUT
    rCurrent : LREAL;
    rTarget : LREAL;
    rRate : LREAL;
END_VAR
    // Fail fast: treat non-positive rate as no movement
    IF rRate <= 0.0 THEN
        _RampToward := rCurrent;
        RETURN;
    END_IF

    IF rCurrent < rTarget THEN
        _RampToward := MIN(rCurrent + rRate, rTarget);
    ELSIF rCurrent > rTarget THEN
        _RampToward := MAX(rCurrent - rRate, rTarget);
    ELSE
        _RampToward := rTarget;
    END_IF
END_METHOD

    // =============================================================================
    // RESET: Return to initial state
    // =============================================================================
METHOD Reset
    _bInitialized := FALSE;

    // Reset internal state
    _stASide.rCMPressure := rAtmosphericPressure;
    _stASide.rHMPressure := rAtmosphericPressure;
    _stASide.rMFCActual  := 0.0;
    _stBSide.rCMPressure := rAtmosphericPressure;
    _stBSide.rHMPressure := rAtmosphericPressure;
    _stBSide.rMFCActual  := 0.0;
    _rExhaustPressure    := rAtmosphericPressure;

    // Flush stale values from public outputs immediately (test isolation)
    stASideOut       := _stASide;
    stBSideOut       := _stBSide;
    rExhaustPressure := rAtmosphericPressure;
END_METHOD

END_FUNCTION_BLOCK
