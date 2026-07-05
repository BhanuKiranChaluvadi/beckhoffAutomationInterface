(* 
PRG_SIMULATION — I/O Hijacking Simulator for Shark Machine

Writes directly to the bottom-most layer:
  - Beckhoff sensor structs (T_Beckhoff_TempSensor_*, T_Beckhoff_PressureSensor)
    via raw INT pointer writes at offset 2.
  - Bronkhorst Modbus adapters via InjectMeasuredValue() (overwrites the polled
    rMeasuredValue, symmetric to PRO's EtherCAT IO struct hijack).
  - EtherCAT MFC IO fields (ST_BronkhorstController_IO.MeasuredValue) directly.

Everything above (FB_AnalogInputAdapter → FB_AnalogSensor → controllers → UI)
works naturally without knowing it's simulated.

Shark has 10 heater zones (no B-side zones 7-11), 4 pressure sensors,
3 EtherCAT MFCs, and Modbus-based pressure controllers.

PHYSICS ENGINE:
  FB_GasPhysicsSimulator reads valve states + pump state from the previous cycle,
  computes CM/HM pressures and MFC flow, and outputs are written to sensor IO.
*)
PROGRAM PRG_SIMULATION
VAR
    // Hardware provider — injected via Configure() from PRG_SHARK
    _ipProvider  : I_HardwareProvider;
    _bConfigured : BOOL := FALSE;

    // Gas system physics — pressure + flow for both sides
    fbGasPhysics : FB_GasPhysicsSimulator;

    // Temperature physics — one per channel channel
    aSimHeaters : ARRAY[1..GVL_TemperatureZones.CHANNEL_MAX] OF FB_SimulateHeaterZone;

    // Inert MFC simulation (not part of gas side physics)
    fbSimMFC_Inert : FB_SimulateAnalogActuator;

    // Working
    bInitialized : BOOL := FALSE;
END_VAR

IF NOT GVL_Simulation.bActive THEN
    RETURN;
END_IF

IF NOT bInitialized THEN
    _Init();
END_IF

// =============================================================================
// 0. SAFETY — Force safe state so subsystems can run
// =============================================================================
GVL_Safety.SafetyOk := TRUE;
GVL_Safety.HeaterVoltage := 230.0;

// =============================================================================
// 1. TEMPERATURE — Write directly to T_Beckhoff_TempSensor structs (no provider)
// =============================================================================
_SimulateTemperature();

// =============================================================================
// 2. GAS PHYSICS + PRESSURE + MFC (requires provider — first cycle is a no-op,
//    _Init atmospheric seed is preserved until provider is wired on cycle 2)
// =============================================================================
IF _bConfigured THEN
    _SimulateGasPhysics();
    _SimulatePressureSensors();
    _SimulateMFCs();
    _SimulateModbusPressureControllers();
END_IF

// =============================================================================
// CONFIGURATION
// =============================================================================

METHOD PUBLIC Configure
VAR_INPUT
    ipProvider : I_HardwareProvider;
END_VAR
    _ipProvider  := ipProvider;
    _bConfigured := _ipProvider <> 0;
END_METHOD

// =============================================================================
// INITIALIZATION
// =============================================================================

METHOD PRIVATE _Init
VAR
    iZone      : INT;
    e          : INT;
    nGlobalIdx : INT;
    nCount     : INT;
    iSensor    : INT;
END_VAR
    // Temperature: init physics for every channel of every populated zone
    nGlobalIdx := 0;
    FOR iZone := 1 TO GVL_TemperatureZones.CONTROLLABLE_ZONE_MAX DO
        nCount := GVL_TemperatureZoneConfig.ZONE_CONFIG[iZone].nChannelCount;
        FOR e := 1 TO nCount DO
            nGlobalIdx := nGlobalIdx + 1;
            IF nGlobalIdx > GVL_TemperatureZones.CHANNEL_MAX THEN EXIT; END_IF
            IF GVL_TemperatureZoneConfig.ZONE_CONFIG[iZone].bPopulated THEN
                aSimHeaters[nGlobalIdx].Init(
                    rAmbient     := GVL_SimulationConfig.AMBIENT_TEMP,
                    rHeatingRate := GVL_SimulationConfig.HEATING_RATE,
                    rCoolingRate := GVL_SimulationConfig.COOLING_RATE,
                    rMaxTemp     := LREAL_TO_REAL(GVL_TemperatureZoneConfig.ZONE_CONFIG[iZone].rMaxSetpoint)
                );
            END_IF
        END_FOR
    END_FOR

    // Pressure: seed sensors with atmospheric so first-cycle reads are valid
    FOR iSensor := 1 TO GVL_PressureSystem.SENSOR_COUNT DO
        _WriteSensorRaw(
            ADR(PRG_PRESSURE_SENSOR.IO_PressureSensors[iSensor]),
            _PressureToRaw(GVL_SimulationConfig.ATMOSPHERIC_PRESSURE, iSensor));
    END_FOR

    bInitialized := TRUE;
END_METHOD

// =============================================================================
// TEMPERATURE SIMULATION
// =============================================================================

METHOD PRIVATE _SimulateTemperature
    // Channel 1: HETR_HEAD
    _SimulateElement(1,  PRG_HEAT_ZONE.HEAD_Htr,
        ADR(PRG_TEMPERATURE_SENSOR.HEAD_A),
        ADR(PRG_TEMPERATURE_SENSOR.HEAD_B));

    // Channels 2-6: A-side STR/CMB/B1/B2/B3
    _SimulateElement(2,  PRG_HEAT_ZONE.A_STR_Htr,
        ADR(PRG_TEMPERATURE_SENSOR.A_STR_A),
        ADR(PRG_TEMPERATURE_SENSOR.A_STR_B));
    _SimulateElement(3,  PRG_HEAT_ZONE.A_CMB_Htr,
        ADR(PRG_TEMPERATURE_SENSOR.A_CMB_A),
        ADR(PRG_TEMPERATURE_SENSOR.A_CMB_B));
    _SimulateElement(4,  PRG_HEAT_ZONE.A_B1_Htr,
        ADR(PRG_TEMPERATURE_SENSOR.A_B1_A),
        ADR(PRG_TEMPERATURE_SENSOR.A_B1_B));
    _SimulateElement(5,  PRG_HEAT_ZONE.A_B2_Htr,
        ADR(PRG_TEMPERATURE_SENSOR.A_B2_A),
        ADR(PRG_TEMPERATURE_SENSOR.A_B2_B));
    _SimulateElement(6,  PRG_HEAT_ZONE.A_B3_Htr,
        ADR(PRG_TEMPERATURE_SENSOR.A_B3_A),
        ADR(PRG_TEMPERATURE_SENSOR.A_B3_B));

    // Channels 7-11: B-side STR/CMB/B1/B2/B3
    _SimulateElement(7,  PRG_HEAT_ZONE.B_STR_Htr,
        ADR(PRG_TEMPERATURE_SENSOR.B_STR_A),
        ADR(PRG_TEMPERATURE_SENSOR.B_STR_B));
    _SimulateElement(8,  PRG_HEAT_ZONE.B_CMB_Htr,
        ADR(PRG_TEMPERATURE_SENSOR.B_CMB_A),
        ADR(PRG_TEMPERATURE_SENSOR.B_CMB_B));
    _SimulateElement(9,  PRG_HEAT_ZONE.B_B1_Htr,
        ADR(PRG_TEMPERATURE_SENSOR.B_B1_A),
        ADR(PRG_TEMPERATURE_SENSOR.B_B1_B));
    _SimulateElement(10, PRG_HEAT_ZONE.B_B2_Htr,
        ADR(PRG_TEMPERATURE_SENSOR.B_B2_A),
        ADR(PRG_TEMPERATURE_SENSOR.B_B2_B));
    _SimulateElement(11, PRG_HEAT_ZONE.B_B3_Htr,
        ADR(PRG_TEMPERATURE_SENSOR.B_B3_A),
        ADR(PRG_TEMPERATURE_SENSOR.B_B3_B));

    // Channels 12-13: HETR_EXH (inner + outer)
    _SimulateElement(12, PRG_HEAT_ZONE.EXH_Htr,
        ADR(PRG_TEMPERATURE_SENSOR.EXH_IN_A),
        ADR(PRG_TEMPERATURE_SENSOR.EXH_IN_B));
    _SimulateElement(13, PRG_HEAT_ZONE.EXH_E2_Htr,
        ADR(PRG_TEMPERATURE_SENSOR.EXH_OUT_A),
        ADR(PRG_TEMPERATURE_SENSOR.EXH_OUT_B));

    // Channels 14-15: HETR_CHK (inner + outer, single sensor per channel)
    _SimulateElement(14, PRG_HEAT_ZONE.CHK_Htr,
        ADR(PRG_TEMPERATURE_SENSOR.CHK_IN_A),
        0);
    _SimulateElement(15, PRG_HEAT_ZONE.CHK_E2_Htr,
        ADR(PRG_TEMPERATURE_SENSOR.CHK_OUT_A),
        0);
END_METHOD

// =============================================================================
// GAS PHYSICS SIMULATION
// =============================================================================

(* Read valve/pump states from previous cycle, run physics, produce pressures + flows *)
METHOD PRIVATE _SimulateGasPhysics
VAR
    ipOutput : I_DigitalOutput;
    ipValve  : I_Valve;
END_VAR
    // System state — null-safe (provider may transiently return 0 during init)
    ipOutput := _ipProvider.GetExhaustPump();
    fbGasPhysics.bPumpOn := (ipOutput <> 0) AND_THEN ipOutput.GetOutput();
    ipValve := _ipProvider.GetExhaustVacuumIsoValve();
    fbGasPhysics.bExhaustVacuumIsoOpen := (ipValve <> 0) AND_THEN ipValve.IsOpen();

    // Per-side valve + controller states
    fbGasPhysics.stASide := _ReadSideState(E_GasSide.A_SIDE);
    fbGasPhysics.stBSide := _ReadSideState(E_GasSide.B_SIDE);

    fbGasPhysics.Execute();
END_METHOD

(* Build ST_GasSideState from domain getters — eliminates A/B duplication.
   All provider calls are null-safe via AND_THEN to tolerate partial init. *)
METHOD PRIVATE _ReadSideState : ST_GasSideState
VAR_INPUT
    eSide : E_GasSide;
END_VAR
VAR
    ipValve  : I_Valve;
    ipCtrl   : I_Controller;
    stStatus : ST_ControllerStatus;
END_VAR
    // Gas input
    ipValve := _ipProvider.GetGasIsoValve(eSide, 1);
    _ReadSideState.bGasIsoOpen      := (ipValve <> 0) AND_THEN ipValve.IsOpen();
    ipCtrl := _ipProvider.GetMFCController(eSide);
    IF ipCtrl <> 0 THEN
        stStatus := ipCtrl.GetStatus();
        _ReadSideState.rMFCSetpoint := stStatus.rSetpoint;
    END_IF

    // Cold manifold
    ipValve := _ipProvider.GetCMBubblerValve(eSide, 1);
    _ReadSideState.bCMBubbler1Open  := (ipValve <> 0) AND_THEN ipValve.IsOpen();
    ipValve := _ipProvider.GetCMBubblerValve(eSide, 2);
    _ReadSideState.bCMBubbler2Open  := (ipValve <> 0) AND_THEN ipValve.IsOpen();
    ipValve := _ipProvider.GetCMBubblerValve(eSide, 3);
    _ReadSideState.bCMBubbler3Open  := (ipValve <> 0) AND_THEN ipValve.IsOpen();
    ipValve := _ipProvider.GetCMPurgeValve(eSide);
    _ReadSideState.bCMPurgeOpen     := (ipValve <> 0) AND_THEN ipValve.IsOpen();
    ipValve := _ipProvider.GetCMVacuumIsoValve(eSide);
    _ReadSideState.bCMVacuumIsoOpen := (ipValve <> 0) AND_THEN ipValve.IsOpen();

    // Hot manifold
    ipValve := _ipProvider.GetHMBubblerValve(eSide, 1);
    _ReadSideState.bHMBubbler1Open  := (ipValve <> 0) AND_THEN ipValve.IsOpen();
    ipValve := _ipProvider.GetHMBubblerValve(eSide, 2);
    _ReadSideState.bHMBubbler2Open  := (ipValve <> 0) AND_THEN ipValve.IsOpen();
    ipValve := _ipProvider.GetHMBubblerValve(eSide, 3);
    _ReadSideState.bHMBubbler3Open  := (ipValve <> 0) AND_THEN ipValve.IsOpen();
    ipValve := _ipProvider.GetHMPurgeValve(eSide);
    _ReadSideState.bHMPurgeOpen     := (ipValve <> 0) AND_THEN ipValve.IsOpen();
    ipValve := _ipProvider.GetHMVacuumIsoValve(eSide);
    _ReadSideState.bHMVacuumIsoOpen := (ipValve <> 0) AND_THEN ipValve.IsOpen();

    // Output
    ipValve := _ipProvider.GetNozzleIsoValve(eSide);
    _ReadSideState.bNozzleIsoOpen   := (ipValve <> 0) AND_THEN ipValve.IsOpen();
    ipCtrl := _ipProvider.GetPressureController(eSide);
    IF ipCtrl <> 0 THEN
        stStatus := ipCtrl.GetStatus();
        _ReadSideState.rPCVSetpoint := stStatus.rSetpoint;
    END_IF
END_METHOD

// =============================================================================
// PRESSURE SENSOR SIMULATION
// =============================================================================

(* Write physics-computed pressures to analog pressure sensor raw IO *)
METHOD PRIVATE _SimulatePressureSensors
    // A-side distributor (cold manifold) pressure
    _WriteSensorRaw(
        ADR(PRG_PRESSURE_SENSOR.IO_PressureSensors[E_PressureSensorIdentifier.PSNS_A_DST]),
        _PressureToRaw(LREAL_TO_REAL(fbGasPhysics.stASideOut.rCMPressure), E_PressureSensorIdentifier.PSNS_A_DST));

    // B-side distributor (cold manifold) pressure
    _WriteSensorRaw(
        ADR(PRG_PRESSURE_SENSOR.IO_PressureSensors[E_PressureSensorIdentifier.PSNS_B_DST]),
        _PressureToRaw(LREAL_TO_REAL(fbGasPhysics.stBSideOut.rCMPressure), E_PressureSensorIdentifier.PSNS_B_DST));

    // Exhaust vacuum pressure
    _WriteSensorRaw(
        ADR(PRG_PRESSURE_SENSOR.IO_PressureSensors[E_PressureSensorIdentifier.PSNS_AB_EXH]),
        _PressureToRaw(LREAL_TO_REAL(fbGasPhysics.rExhaustPressure), E_PressureSensorIdentifier.PSNS_AB_EXH));

    // Chuck vacuum pressure � driven by dedicated PUMP_CHK_VAC state
    _WriteSensorRaw(
        ADR(PRG_PRESSURE_SENSOR.IO_PressureSensors[E_PressureSensorIdentifier.PSNS_CHK_VAC]),
        _PressureToRaw(_GetChuckVacuumPressure(), E_PressureSensorIdentifier.PSNS_CHK_VAC));
END_METHOD

// =============================================================================
// MFC SIMULATION
// =============================================================================

(* Write physics-computed MFC flow to EtherCAT IO. Inert MFC uses simple ramp.
   Direct assignment to VAR members of another PROGRAM is prohibited (C0037),
   so pointer writes are used — identical pattern to _WriteSensorRaw.
   MeasuredValue is UINT (0-32000 raw counts); convert from engineering units. *)
METHOD PRIVATE _SimulateMFCs
VAR
    _pUINT : POINTER TO UINT;
END_VAR
    // A-side Precursor — from gas physics
    _pUINT := ADR(PRG_MFC.IO_MFC_Precursor.MeasuredValue);
    _pUINT^ := WORD_TO_UINT(F_EngineeringToRawCounts(
        fbGasPhysics.stASideOut.rMFCActual,
        REAL_TO_LREAL(GVL_FlowSystem.MIN_FLOW_PRECURSOR_SCCM),
        REAL_TO_LREAL(GVL_FlowSystem.MAX_FLOW_PRECURSOR_SCCM), 32000));

    // B-side Reactant — from gas physics
    _pUINT := ADR(PRG_MFC.IO_MFC_Reactant.MeasuredValue);
    _pUINT^ := WORD_TO_UINT(F_EngineeringToRawCounts(
        fbGasPhysics.stBSideOut.rMFCActual,
        REAL_TO_LREAL(GVL_FlowSystem.MIN_FLOW_REACTANT_SCCM),
        REAL_TO_LREAL(GVL_FlowSystem.MAX_FLOW_REACTANT_SCCM), 32000));

    // Inert — independent, simple ramp (not part of gas side physics)
    // CommandedSetpoint is UINT; convert to engineering units for the ramp simulator.
    fbSimMFC_Inert(
        rSetpoint := LREAL_TO_REAL(F_RawCountsToEngineering(
            PRG_MFC.IO_MFC_Inert.CommandedSetpoint,
            REAL_TO_LREAL(GVL_FlowSystem.MIN_FLOW_INERT_SCCM),
            REAL_TO_LREAL(GVL_FlowSystem.MAX_FLOW_INERT_SCCM), 32000)),
        rRampRate := GVL_SimulationConfig.MFC_RAMP_RATE
    );
    _pUINT := ADR(PRG_MFC.IO_MFC_Inert.MeasuredValue);
    _pUINT^ := WORD_TO_UINT(F_EngineeringToRawCounts(
        REAL_TO_LREAL(fbSimMFC_Inert.rActualFeedback),
        REAL_TO_LREAL(GVL_FlowSystem.MIN_FLOW_INERT_SCCM),
        REAL_TO_LREAL(GVL_FlowSystem.MAX_FLOW_INERT_SCCM), 32000));
END_METHOD

// =============================================================================
// MODBUS PRESSURE CONTROLLER SIMULATION
// =============================================================================

(* Inject physics-computed HM pressure into Modbus pressure controller adapters.
   Symmetric to PRO's EtherCAT path which writes IO_CtrlASide.MeasuredValue directly:
   InjectMeasuredValue() overwrites the value that Modbus polling would normally update. *)
METHOD PRIVATE _SimulateModbusPressureControllers
    PRG_BronkhorstPressure.aDrivers[E_PressureController.A_Side].InjectMeasuredValue(fbGasPhysics.stASideOut.rHMPressure);
    PRG_BronkhorstPressure.aDrivers[E_PressureController.B_Side].InjectMeasuredValue(fbGasPhysics.stBSideOut.rHMPressure);
END_METHOD

// =============================================================================
// PRIVATE HELPERS
// =============================================================================

(* Run physics for one channel and write result to both A+B redundant sensor channels *)
METHOD PRIVATE _SimulateElement
VAR_INPUT
    nGlobalIdx : INT;
    bHeaterOn  : BOOL;
    pSensorA   : POINTER TO BYTE;
    pSensorB   : POINTER TO BYTE;
END_VAR
VAR
    nRawValue : INT;
END_VAR
    IF nGlobalIdx < 1 OR nGlobalIdx > GVL_TemperatureZones.CHANNEL_MAX THEN RETURN; END_IF
    aSimHeaters[nGlobalIdx](bHeaterOn := bHeaterOn);
    nRawValue := _TempToRaw(aSimHeaters[nGlobalIdx].rCurrentTemp);
    _WriteSensorRaw(pSensorA, nRawValue);
    _WriteSensorRaw(pSensorB, nRawValue);
END_METHOD

(* Return the simulated chuck vacuum pressure based on PUMP_CHK_VAC output state.
   PUMP_CHK_VAC is the dedicated chuck vacuum pump (not shared with exhaust).
   Pump ON  ? vacuum achieved: CHUCK_VACUUM_PRESSURE (0.01 bar)
   Pump OFF ? atmospheric: ATMOSPHERIC_PRESSURE (1.0 bar) *)
METHOD PRIVATE _GetChuckVacuumPressure : REAL
VAR
    ipPump : I_DigitalOutput;
END_VAR
    ipPump := _ipProvider.GetDigitalOutput(E_DigitalOutputIdentifier.PUMP_CHK_VAC);
    _GetChuckVacuumPressure := SEL(
        (ipPump <> 0) AND_THEN ipPump.GetOutput(),
        GVL_SimulationConfig.ATMOSPHERIC_PRESSURE,
        GVL_SimulationConfig.CHUCK_VACUUM_PRESSURE);
END_METHOD

(* Convert °C to Beckhoff EL32xx raw INT. Scale: 0.1°C per count → 25.0°C = 250 *)
METHOD PRIVATE _TempToRaw : INT
VAR_INPUT
    rTempC : REAL;
END_VAR
    _TempToRaw := REAL_TO_INT(rTempC / LREAL_TO_REAL(GVL_TemperatureSystem.SCALE_BECKHOFF_TEMP));
END_METHOD

(* Convert bar to Beckhoff EL3048 raw INT using per-sensor scaling from GVL_PressureSystem.
   Each sensor has different raw/scaled ranges. *)
METHOD PRIVATE _PressureToRaw : INT
VAR_INPUT
    rBar     : REAL;
    nSensor  : INT;
END_VAR
    CASE nSensor OF
    1: // PSNS_A_DST: 0-30518 raw → 0-3 bar
        _PressureToRaw := F_ScaleRealToInt(rBar,
            LREAL_TO_REAL(GVL_PressureSystem.PSNS_A_DST_SCALED_MIN),
            LREAL_TO_REAL(GVL_PressureSystem.PSNS_A_DST_SCALED_MAX),
            LREAL_TO_INT(GVL_PressureSystem.PSNS_A_DST_RAW_MIN),
            LREAL_TO_INT(GVL_PressureSystem.PSNS_A_DST_RAW_MAX));
    2: // PSNS_B_DST: 0-30518 raw → 0-3 bar
        _PressureToRaw := F_ScaleRealToInt(rBar,
            LREAL_TO_REAL(GVL_PressureSystem.PSNS_B_DST_SCALED_MIN),
            LREAL_TO_REAL(GVL_PressureSystem.PSNS_B_DST_SCALED_MAX),
            LREAL_TO_INT(GVL_PressureSystem.PSNS_B_DST_RAW_MIN),
            LREAL_TO_INT(GVL_PressureSystem.PSNS_B_DST_RAW_MAX));
    3: // PSNS_AB_EXH: 0-30518 raw → 0-1 bar
        _PressureToRaw := F_ScaleRealToInt(rBar,
            LREAL_TO_REAL(GVL_PressureSystem.PSNS_AB_EXH_SCALED_MIN),
            LREAL_TO_REAL(GVL_PressureSystem.PSNS_AB_EXH_SCALED_MAX),
            LREAL_TO_INT(GVL_PressureSystem.PSNS_AB_EXH_RAW_MIN),
            LREAL_TO_INT(GVL_PressureSystem.PSNS_AB_EXH_RAW_MAX));
    4: // PSNS_CHK_VAC: 3052-15259 raw → 0.00325-1.01325 bar (inverted physical)
        _PressureToRaw := F_ScaleRealToInt(rBar,
            LREAL_TO_REAL(GVL_PressureSystem.PSNS_CHK_VAC_SCALED_MIN),
            LREAL_TO_REAL(GVL_PressureSystem.PSNS_CHK_VAC_SCALED_MAX),
            LREAL_TO_INT(GVL_PressureSystem.PSNS_CHK_VAC_RAW_MIN),
            LREAL_TO_INT(GVL_PressureSystem.PSNS_CHK_VAC_RAW_MAX));
    END_CASE
END_METHOD

(* Write raw INT to a Beckhoff sensor struct at offset 2.
   All EL3xxx structs share the same memory layout:
   Offset 0-1: Status WORD, Offset 2-3: Value INT.
   This matches how FB_AnalogInputAdapter reads — same offset, reverse direction. *)
METHOD PRIVATE _WriteSensorRaw
VAR_INPUT
    pSensor   : POINTER TO BYTE;
    nRawValue : INT;
END_VAR
VAR
    pValue : POINTER TO INT;
END_VAR
    IF pSensor = 0 THEN RETURN; END_IF
    pValue := pSensor + 2;
    pValue^ := nRawValue;
END_METHOD

END_PROGRAM
