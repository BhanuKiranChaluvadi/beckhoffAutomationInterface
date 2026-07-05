(* Common hardware-access surface shared across all machines that use the
   dual-sided gas-delivery pattern (PRO, Shark, future variants).

   Contains ONLY the methods whose signatures are identical across all
   participating machines:
     - Side-abstracted getters keyed on E_GasSide (a Lib enum)
     - Zero-input getters for shared singletons (mix valve, exhaust pump,
       exhaust pressure sensor, etc.)
     - All thermal getters defined directly in this interface

   Per-machine variations (enums) are passed as INT to the generic getters
   below — enum-to-INT is implicit in IEC 61131-3. This eliminates the need
   for machine-specific interface extensions (I_PROHardwareProvider, etc.).

   Gas Flow Path (per side):
     Carrier Gas → MFC/Gas Valve → CM Bubbler Inlet → Bubbler →
     HM Bubbler Outlet → Hot Manifold → PCV → Start Valve → Nozzle *)
INTERFACE I_HardwareProvider EXTENDS __System.IQueryInterface

    // =========================================================================
    // VALVE GETTERS - Side-abstracted
    // =========================================================================
METHOD GetGasIsoValve : I_Valve
VAR_INPUT
    eSide : E_GasSide;
    nBubblerIndex : DINT;
END_VAR
END_METHOD

METHOD GetCMBubblerValve : I_Valve
VAR_INPUT
    eSide : E_GasSide;
    nBubblerIndex : DINT;
END_VAR
END_METHOD

METHOD GetHMBubblerValve : I_Valve
VAR_INPUT
    eSide : E_GasSide;
    nBubblerIndex : DINT;
END_VAR
END_METHOD

METHOD GetCMPurgeValve : I_Valve
VAR_INPUT
    eSide : E_GasSide;
END_VAR
END_METHOD

METHOD GetHMPurgeValve : I_Valve
VAR_INPUT
    eSide : E_GasSide;
END_VAR
END_METHOD

METHOD GetCMVacuumIsoValve : I_Valve
VAR_INPUT
    eSide : E_GasSide;
END_VAR
END_METHOD

METHOD GetHMVacuumIsoValve : I_Valve
VAR_INPUT
    eSide : E_GasSide;
END_VAR
END_METHOD

METHOD GetNozzleIsoValve : I_Valve
VAR_INPUT
    eSide : E_GasSide;
END_VAR
END_METHOD

    // =========================================================================
    // VALVE GETTERS - Common (no side)
    // =========================================================================
METHOD GetMixValve : I_Valve
END_METHOD

METHOD GetExhaustVacuumIsoValve : I_Valve
END_METHOD

    // =========================================================================
    // DIGITAL OUTPUT GETTERS - Common
    // =========================================================================
METHOD GetExhaustPump : I_DigitalOutput
END_METHOD

    // =========================================================================
    // SENSOR GETTERS - Side-abstracted
    // =========================================================================
METHOD GetCMPressureSensor : I_Sensor
VAR_INPUT
    eSide : E_GasSide;
END_VAR
END_METHOD

METHOD GetHMPressureSensor : I_Sensor
VAR_INPUT
    eSide : E_GasSide;
END_VAR
END_METHOD

METHOD GetMFCFlowSensor : I_Sensor
VAR_INPUT
    eSide : E_GasSide;
END_VAR
END_METHOD

    // =========================================================================
    // SENSOR GETTERS - Common
    // =========================================================================
METHOD GetExhaustPressureSensor : I_Sensor
END_METHOD

    // =========================================================================
    // CONTROLLER GETTERS - Side-abstracted
    // =========================================================================
METHOD GetMFCController : I_Controller
VAR_INPUT
    eSide : E_GasSide;
END_VAR
END_METHOD

METHOD GetPressureController : I_Controller
VAR_INPUT
    eSide : E_GasSide;
END_VAR
END_METHOD

    // =========================================================================
    // CONTROLLER GETTERS - Inert Gas (used by SpotDeposition)
    // =========================================================================
METHOD GetInertMFCController : I_Controller
END_METHOD

    // =========================================================================
    // VALVE GETTERS - Inert Gas (used by SpotDeposition)
    // =========================================================================
METHOD GetInertGasValve : I_Valve
END_METHOD

    // =========================================================================
    // SUBSYSTEM GETTERS
    // =========================================================================

    // Returns the machine-specific chuck-vacuum subsystem.
    // Lib chuck-vacuum sequences depend on this abstraction to remain
    // portable across machines with different pump/valve/sensor topologies.
METHOD GetChuckVacuumSystem : I_ChuckVacuumSystem
END_METHOD

    // =========================================================================
    // GENERIC GETTERS - Index-based (pass machine enum; implicit INT cast)
    // =========================================================================
METHOD GetValve : I_Valve
VAR_INPUT
    nIndex : INT;
END_VAR
END_METHOD

METHOD GetDigitalOutput : I_DigitalOutput
VAR_INPUT
    nIndex : INT;
END_VAR
END_METHOD

METHOD GetDigitalInput : I_DigitalInput
VAR_INPUT
    nIndex : INT;
END_VAR
END_METHOD

METHOD GetPressureSensor : I_Sensor
VAR_INPUT
    nIndex : INT;
END_VAR
END_METHOD

METHOD GetFlowController : I_Controller
VAR_INPUT
    nIndex : INT;
END_VAR
END_METHOD

METHOD GetTemperatureController : I_Controller
VAR_INPUT
    nIndex : INT;
END_VAR
END_METHOD

    // Individual heater CHANNEL controller addressed by its flat global channel
    // index (1..CHANNEL_MAX). Pass the machine E_HeatChannel enum (implicit INT).
METHOD GetTemperatureChannel : I_Controller
VAR_INPUT
    nGlobalIdx : INT;
END_VAR
END_METHOD

METHOD GetPressureControllerById : I_Controller
VAR_INPUT
    nIndex : INT;
END_VAR
END_METHOD

    // =========================================================================
    // UTILITY & CONFIGURATION
    // =========================================================================
METHOD IsGasMode : BOOL
VAR_INPUT
    eSide : E_GasSide;
    nBubblerIndex : DINT;
END_VAR
END_METHOD

METHOD GetMaxBubblerCount : DINT
VAR_INPUT
    eSide : E_GasSide;
END_VAR
END_METHOD

    // =========================================================================
    // TEMPERATURE CONTROLLER GETTERS
    // =========================================================================
METHOD GetHMTemperatureController : I_Controller
VAR_INPUT
    eSide : E_GasSide;
END_VAR
END_METHOD

METHOD GetUmbilicalTemperatureController : I_Controller
VAR_INPUT
    eSide : E_GasSide;
END_VAR
END_METHOD

METHOD GetBubblerTemperatureController : I_Controller
VAR_INPUT
    eSide : E_GasSide;
    nBubblerIndex : DINT;
END_VAR
END_METHOD

METHOD GetHeadTemperatureController : I_Controller
END_METHOD

METHOD GetExhaustTemperatureController : I_Controller
END_METHOD

METHOD GetChuckTemperatureController : I_Controller
END_METHOD

END_INTERFACE