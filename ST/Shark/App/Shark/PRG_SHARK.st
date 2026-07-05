(*
PRG_SHARK — Top-Level Orchestrator for Shark Machine

Execution order follows the data-flow rule:
  Simulation → Inputs/Sensors → Outputs → Configure dependencies → Subsystems → HardwareProvider → Telemetry

Shark-specific differences from PRO:
  - No PRG_MOTION, PRG_OZONE (not present on Shark hardware)
  - Pressure controllers are Modbus (PRG_BronkhorstPressure manages its own Modbus stack)
  - PRG_BronkhorstPressure injects controller interfaces into PRG_PRESSURE_SENSOR via Configure()
  - PRG_VALVE.Configure needs 6 DOs for chuck solenoid valves
  - Per-side mix valves (no shared VALV_AB_MIX)
*)
PROGRAM PRG_SHARK
VAR
    // 3 — Dependency configuration
    bDependenciesConfigured          : BOOL := FALSE;

    // 3b — Infrastructure managers (need DI/DO subsystems)
    fbSafetyManager                  : FB_SafetyManager;
    fbDoorsManager                   : FB_DoorsManager;
    fbChillerManager                 : FB_ChillerManager;
    fbSignalLightController          : FB_SignalLightController;
    bInfraInitialized                : BOOL := FALSE;

    // 4b — Exhaust controller (needs pressure subsystem)
    fbExhaustController              : FB_ExhaustController;
    bExhaustInitialized              : BOOL := FALSE;

    // 5 — Hardware provider (needs all subsystems)
    fbHardwareProvider               : FB_HardwareProvider;
    bHardwareProviderInit            : BOOL := FALSE;
    _bHardwareProviderInitFailLogged : BOOL := FALSE;

    // 8 — One-time system initialization
    bInitialized                     : BOOL := FALSE;
END_VAR

// =============================================================================
// 0. SIMULATION (Seed IO before anything reads it)
// =============================================================================
PRG_SIMULATION();

// =============================================================================
// 1. INPUTS & SENSORS (Read First - No Dependencies)
// =============================================================================
PRG_DIGITAL_INPUT();
PRG_HUMIDITY_SENSOR();

// =============================================================================
// 2. OUTPUTS (No Dependencies)
// =============================================================================
PRG_DIGITAL_OUTPUT();

// =============================================================================
// 3. CONFIGURE DEPENDENCIES (Once, after DI/DO are initialized)
// =============================================================================
IF NOT bDependenciesConfigured THEN
    // Wire solenoid valve DOs into PRG_VALVE — composition root injection
    PRG_VALVE.Configure(
        ipChuckLZ1  := PRG_DIGITAL_OUTPUT.fbDigitalOutputSubsystem.GetOutputByIndex(E_DigitalOutputIdentifier.VALV_CHK_LZ1),
        ipChuckLZ2  := PRG_DIGITAL_OUTPUT.fbDigitalOutputSubsystem.GetOutputByIndex(E_DigitalOutputIdentifier.VALV_CHK_LZ2),
        ipChuckLZ3  := PRG_DIGITAL_OUTPUT.fbDigitalOutputSubsystem.GetOutputByIndex(E_DigitalOutputIdentifier.VALV_CHK_LZ3),
        ipChuckVent := PRG_DIGITAL_OUTPUT.fbDigitalOutputSubsystem.GetOutputByIndex(E_DigitalOutputIdentifier.VALV_CHK_VENT),
        ipChuckWIn  := PRG_DIGITAL_OUTPUT.fbDigitalOutputSubsystem.GetOutputByIndex(E_DigitalOutputIdentifier.VALV_CHK_WIN),
        ipChuckWOut := PRG_DIGITAL_OUTPUT.fbDigitalOutputSubsystem.GetOutputByIndex(E_DigitalOutputIdentifier.VALV_CHK_WOUT)
    );

    bDependenciesConfigured := TRUE;
END_IF

// =============================================================================
// 3b. INFRASTRUCTURE INIT (Once, after DI/DO configured)
// =============================================================================
IF NOT bInfraInitialized AND bDependenciesConfigured THEN
    // Safety manager — reads GVL_Safety, controls MFCs
    fbSafetyManager.Init(refFlowSubsystem := PRG_MFC.fbFlowSubsystem);

    // Wire safety status into digital input program (HETR_VOLT_ON, SAFE_STO_ON, SAFE_OVERRIDE)
    PRG_DIGITAL_INPUT.Init(ipSafety := fbSafetyManager);

    // Door manager — wires 9 DI sensors + 2 DO locks
    fbDoorsManager.Init(
        ipDISub := PRG_DIGITAL_INPUT.fbDigitalInputSubsystem,
        ipDOSub := PRG_DIGITAL_OUTPUT.fbDigitalOutputSubsystem
    );

    // Chiller manager — 3 DOs: power, water in, water out
    fbChillerManager.Init(
        ipChillerPower  := PRG_DIGITAL_OUTPUT.fbDigitalOutputSubsystem.GetOutputByIndex(E_DigitalOutputIdentifier.CHIL_PWR),
        ipWaterInValve  := PRG_DIGITAL_OUTPUT.fbDigitalOutputSubsystem.GetOutputByIndex(E_DigitalOutputIdentifier.VALV_CHK_WIN),
        ipWaterOutValve := PRG_DIGITAL_OUTPUT.fbDigitalOutputSubsystem.GetOutputByIndex(E_DigitalOutputIdentifier.VALV_CHK_WOUT)
    );

    // Signal light controller — 3 DOs: green, yellow, red
    fbSignalLightController.Init(
        ipGreen  := PRG_DIGITAL_OUTPUT.fbDigitalOutputSubsystem.GetOutputByIndex(E_DigitalOutputIdentifier.LGHT_SGN_GRN),
        ipYellow := PRG_DIGITAL_OUTPUT.fbDigitalOutputSubsystem.GetOutputByIndex(E_DigitalOutputIdentifier.LGHT_SGN_YEL),
        ipRed    := PRG_DIGITAL_OUTPUT.fbDigitalOutputSubsystem.GetOutputByIndex(E_DigitalOutputIdentifier.LGHT_SGN_RED)
    );

    bInfraInitialized := TRUE;
END_IF

// =============================================================================
// 4. SUBSYSTEM LOGIC & CONTROL (Dependencies now configured)
// =============================================================================
PRG_VALVE();
PRG_MFC();

// Modbus pressure controllers — must run before PRG_PRESSURE_SENSOR
// (first call injects controller interfaces via PRG_PRESSURE_SENSOR.Configure)
PRG_BronkhorstPressure();
PRG_PRESSURE_SENSOR();

// Temperature: safety gate — heaters must not run without SafetyOk unless in simulation
PRG_TEMPERATURE_SENSOR();

IF fbSafetyManager.IsHeatingAllowed THEN
    PRG_HEAT_ZONE();
END_IF

// =============================================================================
// 4b. EXHAUST CONTROLLER INIT (Once, after HW provider and digital output ready)
// =============================================================================
IF NOT bExhaustInitialized AND bInfraInitialized AND bHardwareProviderInit THEN
    bExhaustInitialized := fbExhaustController.Init(
        ipExhaustPump          := PRG_DIGITAL_OUTPUT.fbDigitalOutputSubsystem.GetOutputByIndex(E_DigitalOutputIdentifier.PUMP_AB_EXH),
        ipIsoValve             := fbHardwareProvider.GetExhaustVacuumIsoValve(),
        ipPressureSensor       := fbHardwareProvider.GetExhaustPressureSensor(),
        bIsoValveBeforeSensor  := TRUE  // Shark: pump → iso valve → sensor; valve must open before pressure is meaningful
    );
END_IF

// Infrastructure cyclic: safety state machine + exhaust state machine
fbSafetyManager();
fbExhaustController();

// =============================================================================
// 5. HARDWARE PROVIDER INITIALIZATION (After subsystems ready)
// =============================================================================
IF NOT bHardwareProviderInit THEN
    bHardwareProviderInit := fbHardwareProvider.Init(
        ipValveSubsystem         := PRG_VALVE.fbValveSubsystem,
        ipPressureSubsystem      := PRG_PRESSURE_SENSOR.fbPressureSubsystem,
        ipFlowSubsystem          := PRG_MFC.fbFlowSubsystem,
        ipDigitalOutputSubsystem := PRG_DIGITAL_OUTPUT.fbDigitalOutputSubsystem,
        ipDigitalInputSubsystem  := PRG_DIGITAL_INPUT.fbDigitalInputSubsystem,
        ipTemperatureSubsystem   := PRG_HEAT_ZONE.fbTemperatureSubsystem
    );

    // Configure sequences ONCE when provider is ready
    IF bHardwareProviderInit THEN
        PRG_Sequence.Configure(ipProvider := fbHardwareProvider);
        PRG_SIMULATION.Configure(ipProvider := fbHardwareProvider);
    ELSIF NOT _bHardwareProviderInitFailLogged THEN
        // Fail fast — Init returns FALSE only on invalid REFERENCE TO (a wiring bug,
        // not a transient). Without this log, sequences would silently never run.
        GVL_ErrorLogger.Logger.LogError(
            sSource := 'PRG_SHARK',
            eErrorId := E_ErrorId.ERR_GEN_INITIALIZATION,
            sContext := 'fbHardwareProvider.Init returned FALSE — subsystem REFERENCE TO invalid; sequences will not run'
        );
        _bHardwareProviderInitFailLogged := TRUE;
    END_IF
END_IF

// =============================================================================
// 6. SEQUENCES (Runs every cycle after provider ready)
// =============================================================================
IF bHardwareProviderInit THEN
    PRG_Sequence();
END_IF

// =============================================================================
// 6b. STATUS LIGHTS (Driven by sequence state)
// =============================================================================
fbSignalLightController.Execute(stStatus := PRG_Sequence.GetStatus());

// =============================================================================
// 7. TELEMETRY & SPECTROMETER (After all subsystems updated)
// =============================================================================
PRG_Telemetry();
PRG_Spectrometer();

// =============================================================================
// 8. ONE-TIME INITIALIZATION (After all subsystems and provider are running)
// =============================================================================
IF NOT bInitialized THEN
    _EnableAllControllers();
    _InitializePressureSetpoints();
    _StartDefaultOutputs();
    bInitialized := TRUE;
END_IF

// Update global system state flags
GVL_System.bSystemInitialized := bInitialized AND bHardwareProviderInit AND bInfraInitialized;
GVL_System.bSystemHealthy := GVL_System.bGlobalSafetyOK AND fbExhaustController.IsReady;

// =============================================================================
// PRIVATE METHODS
// =============================================================================

METHOD PRIVATE _EnableAllControllers
VAR
    i : INT;
    ipController : I_Controller;
END_VAR
    // Enable all pressure controllers
    FOR i := 1 TO GVL_PressureSystem.CONTROLLER_COUNT DO
        ipController := PRG_PRESSURE_SENSOR.fbPressureSubsystem.GetControllerByIndex(i);
        IF ipController <> 0 THEN
            ipController.Enable(TRUE);
        END_IF
    END_FOR

    // Enable all flow controllers
    FOR i := 1 TO GVL_FlowSystem.FLOW_ZONE_COUNT DO
        ipController := PRG_MFC.fbFlowSubsystem.GetControllerByIndex(i);
        IF ipController <> 0 THEN
            ipController.Enable(TRUE);
        END_IF
    END_FOR
END_METHOD

METHOD PRIVATE _InitializePressureSetpoints
VAR
    i : INT;
    ipController : I_Controller;
END_VAR
    // Set all pressure controllers to max bar (fully closed)
    FOR i := 1 TO GVL_PressureSystem.CONTROLLER_COUNT DO
        ipController := PRG_PRESSURE_SENSOR.fbPressureSubsystem.GetControllerByIndex(i);
        IF ipController <> 0 THEN
            ipController.SetSetpoint(GVL_PressureSystem.MAX_PRESSURE_CTRL_BAR);
        END_IF
    END_FOR
END_METHOD

METHOD PRIVATE _StartDefaultOutputs
VAR
    i        : INT;
    ipOutput : I_DigitalOutput;
END_VAR
    FOR i := 1 TO GVL_DIOConfig.AUTO_START_DO_COUNT DO
        ipOutput := PRG_DIGITAL_OUTPUT.fbDigitalOutputSubsystem.GetOutputByIndex(
                        nOutput := GVL_DIOConfig.AUTO_START_DO_OUTPUTS[i]);
        IF ipOutput <> 0 THEN
            ipOutput.SetOutput(TRUE);
        END_IF
    END_FOR
END_METHOD

END_PROGRAM
