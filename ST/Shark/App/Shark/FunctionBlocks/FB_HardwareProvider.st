(* Hardware provider for Shark System (Dual-sided gas, Modbus pressure).
   Delegates Init and index-based subsystem access to FB_HardwareProviderBase.

   Shark-specific differences from PRO:
   - Per-side mix valves (VALV_A_MIX, VALV_B_MIX) instead of shared VALV_AB_MIX
   - Separate CM and HM vacuum isolation (DST_EXH vs CMB_EXH) instead of shared exhaust
   - Modbus pressure controllers via E_PressureController
    - 3 bubblers per side (no B-side gas-only lines)
    - No inert MFC side mapping (accessed via generic GetValve) *)
FUNCTION_BLOCK FB_HardwareProvider EXTENDS FB_HardwareProviderBase IMPLEMENTS I_HardwareProvider
VAR
    _nMaxBubblers : ARRAY[1..2] OF DINT := [3, 3];  // index matches E_GasSide: A_SIDE=1 → 3, B_SIDE=2 → 3
    _fbChuckVacuum         : FB_ChuckVacuumSystem;
    _bChuckVacuumInitTried : BOOL := FALSE;
END_VAR

// =============================================================================
// GENERIC GETTERS — thin wrappers delegating to base index-based helpers
// =============================================================================

METHOD GetValve : I_Valve
VAR_INPUT
    nIndex : INT;
END_VAR
    GetValve := SUPER^.GetValveByIndex(nIndex);
END_METHOD

METHOD GetDigitalOutput : I_DigitalOutput
VAR_INPUT
    nIndex : INT;
END_VAR
    GetDigitalOutput := SUPER^.GetOutputByIndex(nIndex);
END_METHOD

METHOD GetDigitalInput : I_DigitalInput
VAR_INPUT
    nIndex : INT;
END_VAR
    GetDigitalInput := SUPER^.GetInputByIndex(nIndex);
END_METHOD

METHOD GetPressureSensor : I_Sensor
VAR_INPUT
    nIndex : INT;
END_VAR
    GetPressureSensor := SUPER^.GetSensorByIndex(nIndex);
END_METHOD

METHOD GetFlowController : I_Controller
VAR_INPUT
    nIndex : INT;
END_VAR
    GetFlowController := SUPER^.GetFlowControllerByIndex(nIndex);
END_METHOD

METHOD GetPressureControllerById : I_Controller
VAR_INPUT
    nIndex : INT;
END_VAR
    GetPressureControllerById := SUPER^.GetPressureControllerByIndex(nIndex);
END_METHOD

METHOD GetTemperatureController : I_Controller
VAR_INPUT
    nIndex : INT;
END_VAR
    GetTemperatureController := SUPER^.GetTemperatureControllerByIndex(nIndex);
END_METHOD

METHOD GetTemperatureChannel : I_Controller
VAR_INPUT
    nGlobalIdx : INT;
END_VAR
    GetTemperatureChannel := SUPER^.GetTemperatureChannelByGlobalIndex(nGlobalIdx);
END_METHOD

// =============================================================================
// VALVE GETTERS - Side-abstracted
// =============================================================================

// Shark: One MFC valve per side (no extra gas lines)
METHOD GetGasIsoValve : I_Valve
VAR_INPUT
    eSide : E_GasSide;
    nBubblerIndex : DINT;
END_VAR
    IF eSide = E_GasSide.A_SIDE THEN
        GetGasIsoValve := GetValve(E_ValveIdentifier.VALV_A_MFC01);
    ELSE
        GetGasIsoValve := GetValve(E_ValveIdentifier.VALV_B_MFC01);
    END_IF
END_METHOD

// CM (Distributor) bubbler inlet valves
METHOD GetCMBubblerValve : I_Valve
VAR_INPUT
    eSide : E_GasSide;
    nBubblerIndex : DINT;
END_VAR
    nBubblerIndex := LIMIT(1, nBubblerIndex, 3);

    IF eSide = E_GasSide.A_SIDE THEN
        CASE nBubblerIndex OF
            1: GetCMBubblerValve := GetValve(E_ValveIdentifier.VALV_A_DST_B1);
            2: GetCMBubblerValve := GetValve(E_ValveIdentifier.VALV_A_DST_B2);
            3: GetCMBubblerValve := GetValve(E_ValveIdentifier.VALV_A_DST_B3);
        END_CASE
    ELSE
        CASE nBubblerIndex OF
            1: GetCMBubblerValve := GetValve(E_ValveIdentifier.VALV_B_DST_B1);
            2: GetCMBubblerValve := GetValve(E_ValveIdentifier.VALV_B_DST_B2);
            3: GetCMBubblerValve := GetValve(E_ValveIdentifier.VALV_B_DST_B3);
        END_CASE
    END_IF
END_METHOD

// HM (Combiner) bubbler outlet valves
METHOD GetHMBubblerValve : I_Valve
VAR_INPUT
    eSide : E_GasSide;
    nBubblerIndex : DINT;
END_VAR
    nBubblerIndex := LIMIT(1, nBubblerIndex, 3);

    IF eSide = E_GasSide.A_SIDE THEN
        CASE nBubblerIndex OF
            1: GetHMBubblerValve := GetValve(E_ValveIdentifier.VALV_A_CMB_B1);
            2: GetHMBubblerValve := GetValve(E_ValveIdentifier.VALV_A_CMB_B2);
            3: GetHMBubblerValve := GetValve(E_ValveIdentifier.VALV_A_CMB_B3);
        END_CASE
    ELSE
        CASE nBubblerIndex OF
            1: GetHMBubblerValve := GetValve(E_ValveIdentifier.VALV_B_CMB_B1);
            2: GetHMBubblerValve := GetValve(E_ValveIdentifier.VALV_B_CMB_B2);
            3: GetHMBubblerValve := GetValve(E_ValveIdentifier.VALV_B_CMB_B3);
        END_CASE
    END_IF
END_METHOD

METHOD GetCMPurgeValve : I_Valve
VAR_INPUT
    eSide : E_GasSide;
END_VAR
    IF eSide = E_GasSide.A_SIDE THEN
        GetCMPurgeValve := GetValve(E_ValveIdentifier.VALV_A_DST_PRG);
    ELSE
        GetCMPurgeValve := GetValve(E_ValveIdentifier.VALV_B_DST_PRG);
    END_IF
END_METHOD

METHOD GetHMPurgeValve : I_Valve
VAR_INPUT
    eSide : E_GasSide;
END_VAR
    IF eSide = E_GasSide.A_SIDE THEN
        GetHMPurgeValve := GetValve(E_ValveIdentifier.VALV_A_CMB_PRG);
    ELSE
        GetHMPurgeValve := GetValve(E_ValveIdentifier.VALV_B_CMB_PRG);
    END_IF
END_METHOD

// Shark: Separate CM and HM vacuum isolation (Distributor Exhaust vs Combiner Exhaust)
METHOD GetCMVacuumIsoValve : I_Valve
VAR_INPUT
    eSide : E_GasSide;
END_VAR
    IF eSide = E_GasSide.A_SIDE THEN
        GetCMVacuumIsoValve := GetValve(E_ValveIdentifier.VALV_A_DST_EXH);
    ELSE
        GetCMVacuumIsoValve := GetValve(E_ValveIdentifier.VALV_B_DST_EXH);
    END_IF
END_METHOD

METHOD GetHMVacuumIsoValve : I_Valve
VAR_INPUT
    eSide : E_GasSide;
END_VAR
    IF eSide = E_GasSide.A_SIDE THEN
        GetHMVacuumIsoValve := GetValve(E_ValveIdentifier.VALV_A_CMB_EXH);
    ELSE
        GetHMVacuumIsoValve := GetValve(E_ValveIdentifier.VALV_B_CMB_EXH);
    END_IF
END_METHOD

METHOD GetNozzleIsoValve : I_Valve
VAR_INPUT
    eSide : E_GasSide;
END_VAR
    IF eSide = E_GasSide.A_SIDE THEN
        GetNozzleIsoValve := GetValve(E_ValveIdentifier.VALV_A_STR);
    ELSE
        GetNozzleIsoValve := GetValve(E_ValveIdentifier.VALV_B_STR);
    END_IF
END_METHOD

// =============================================================================
// VALVE GETTERS - Common
// =============================================================================

// Shark has per-side mix valves (VALV_A_MIX, VALV_B_MIX), no shared mix valve.
// Returns 0 — use GetValve(E_ValveIdentifier.VALV_A_MIX) for direct access.
METHOD GetMixValve : I_Valve
    GetMixValve := 0;
END_METHOD

METHOD GetExhaustVacuumIsoValve : I_Valve
    GetExhaustVacuumIsoValve := GetValve(E_ValveIdentifier.VALV_EX_PUMP);
END_METHOD

// =============================================================================
// DIGITAL OUTPUT GETTERS
// =============================================================================

METHOD GetExhaustPump : I_DigitalOutput
    GetExhaustPump := GetDigitalOutput(E_DigitalOutputIdentifier.PUMP_AB_EXH);
END_METHOD

// =============================================================================
// SENSOR GETTERS - Side-abstracted
// =============================================================================

// CM (Distributor) pressure sensors
METHOD GetCMPressureSensor : I_Sensor
VAR_INPUT
    eSide : E_GasSide;
END_VAR
    IF eSide = E_GasSide.A_SIDE THEN
        GetCMPressureSensor := GetPressureSensor(E_PressureSensorIdentifier.PSNS_A_DST);
    ELSE
        GetCMPressureSensor := GetPressureSensor(E_PressureSensorIdentifier.PSNS_B_DST);
    END_IF
END_METHOD

// HM (Combiner/Hot Manifold) — Bronkhorst controller as sensor via __QUERYINTERFACE
METHOD GetHMPressureSensor : I_Sensor
VAR_INPUT
    eSide : E_GasSide;
END_VAR
    IF eSide = E_GasSide.A_SIDE THEN
        GetHMPressureSensor := SUPER^.GetPressureControllerAsSensor(E_PressureController.A_Side);
    ELSE
        GetHMPressureSensor := SUPER^.GetPressureControllerAsSensor(E_PressureController.B_Side);
    END_IF
END_METHOD

METHOD GetMFCFlowSensor : I_Sensor
VAR_INPUT
    eSide : E_GasSide;
END_VAR
    IF eSide = E_GasSide.A_SIDE THEN
        GetMFCFlowSensor := SUPER^.GetFlowControllerAsSensor(E_MFCIdentifier.MFCT_A_01);
    ELSE
        GetMFCFlowSensor := SUPER^.GetFlowControllerAsSensor(E_MFCIdentifier.MFCT_B_01);
    END_IF
END_METHOD

// =============================================================================
// SENSOR GETTERS - Common
// =============================================================================

METHOD GetExhaustPressureSensor : I_Sensor
    GetExhaustPressureSensor := GetPressureSensor(E_PressureSensorIdentifier.PSNS_AB_EXH);
END_METHOD

// =============================================================================
// CONTROLLER GETTERS - Side-abstracted
// =============================================================================

METHOD GetMFCController : I_Controller
VAR_INPUT
    eSide : E_GasSide;
END_VAR
    IF eSide = E_GasSide.A_SIDE THEN
        GetMFCController := SUPER^.GetFlowControllerByIndex(E_MFCIdentifier.MFCT_A_01);
    ELSE
        GetMFCController := SUPER^.GetFlowControllerByIndex(E_MFCIdentifier.MFCT_B_01);
    END_IF
END_METHOD

// Shark: Modbus Bronkhorst pressure controllers (E_PressureController)
METHOD GetPressureController : I_Controller
VAR_INPUT
    eSide : E_GasSide;
END_VAR
    IF eSide = E_GasSide.A_SIDE THEN
        GetPressureController := SUPER^.GetPressureControllerByIndex(E_PressureController.A_Side);
    ELSE
        GetPressureController := SUPER^.GetPressureControllerByIndex(E_PressureController.B_Side);
    END_IF
END_METHOD

// Inert Gas MFC Controller (Shark-only, used by SpotDeposition)
METHOD GetInertMFCController : I_Controller
    GetInertMFCController := SUPER^.GetFlowControllerByIndex(E_MFCIdentifier.MFCT_IG_01);
END_METHOD

// Inert Gas Valve (used by SpotDeposition)
METHOD GetInertGasValve : I_Valve
    GetInertGasValve := GetValve(E_ValveIdentifier.VALV_IG_MFC01);
END_METHOD

// Chuck Vacuum Subsystem — lazy-initialized on first request.
METHOD GetChuckVacuumSystem : I_ChuckVacuumSystem
    IF NOT _bChuckVacuumInitTried THEN
        _bChuckVacuumInitTried := TRUE;
        _fbChuckVacuum.Init(ipProvider := THIS^);
    END_IF
    GetChuckVacuumSystem := _fbChuckVacuum;
END_METHOD

// =============================================================================
// TEMPERATURE CONTROLLER GETTERS - Side-abstracted
// =============================================================================

METHOD GetHMTemperatureController : I_Controller
VAR_INPUT
    eSide : E_GasSide;
END_VAR
    IF eSide = E_GasSide.A_SIDE THEN
        GetHMTemperatureController := SUPER^.GetTemperatureControllerByIndex(E_HeatZone.HETR_A_CMB);
    ELSE
        GetHMTemperatureController := SUPER^.GetTemperatureControllerByIndex(E_HeatZone.HETR_B_CMB);
    END_IF
END_METHOD

METHOD GetUmbilicalTemperatureController : I_Controller
VAR_INPUT
    eSide : E_GasSide;
END_VAR
    IF eSide = E_GasSide.A_SIDE THEN
        GetUmbilicalTemperatureController := SUPER^.GetTemperatureControllerByIndex(E_HeatZone.HETR_A_STR);
    ELSE
        GetUmbilicalTemperatureController := SUPER^.GetTemperatureControllerByIndex(E_HeatZone.HETR_B_STR);
    END_IF
END_METHOD

METHOD GetBubblerTemperatureController : I_Controller
VAR_INPUT
    eSide : E_GasSide;
    nBubblerIndex : DINT;
END_VAR
    nBubblerIndex := LIMIT(1, nBubblerIndex, 3);

    IF eSide = E_GasSide.A_SIDE THEN
        CASE nBubblerIndex OF
            1: GetBubblerTemperatureController := SUPER^.GetTemperatureControllerByIndex(E_HeatZone.HETR_A_B1);
            2: GetBubblerTemperatureController := SUPER^.GetTemperatureControllerByIndex(E_HeatZone.HETR_A_B2);
            3: GetBubblerTemperatureController := SUPER^.GetTemperatureControllerByIndex(E_HeatZone.HETR_A_B3);
        END_CASE
    ELSE
        CASE nBubblerIndex OF
            1: GetBubblerTemperatureController := SUPER^.GetTemperatureControllerByIndex(E_HeatZone.HETR_B_B1);
            2: GetBubblerTemperatureController := SUPER^.GetTemperatureControllerByIndex(E_HeatZone.HETR_B_B2);
            3: GetBubblerTemperatureController := SUPER^.GetTemperatureControllerByIndex(E_HeatZone.HETR_B_B3);
        END_CASE
    END_IF
END_METHOD

// =============================================================================
// TEMPERATURE CONTROLLER GETTERS - Common
// =============================================================================

METHOD GetHeadTemperatureController : I_Controller
    GetHeadTemperatureController := SUPER^.GetTemperatureControllerByIndex(E_HeatZone.HETR_HEAD);
END_METHOD

METHOD GetExhaustTemperatureController : I_Controller
    GetExhaustTemperatureController := SUPER^.GetTemperatureControllerByIndex(E_HeatZone.HETR_EXH);
END_METHOD

METHOD GetChuckTemperatureController : I_Controller
    GetChuckTemperatureController := SUPER^.GetTemperatureControllerByIndex(E_HeatZone.HETR_CHK);
END_METHOD

// =============================================================================
// UTILITY & CONFIGURATION
// =============================================================================

// Shark has no gas-only lines — all bubblers are bubbler mode
METHOD IsGasMode : BOOL
VAR_INPUT
    eSide : E_GasSide;
    nBubblerIndex : DINT;
END_VAR
    IsGasMode := FALSE;
END_METHOD

METHOD GetMaxBubblerCount : DINT
VAR_INPUT
    eSide : E_GasSide;
END_VAR
    GetMaxBubblerCount := _nMaxBubblers[eSide];
END_METHOD

END_FUNCTION_BLOCK
