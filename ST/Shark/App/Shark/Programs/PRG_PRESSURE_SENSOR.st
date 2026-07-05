(*
Pressure Sensor Program - Shark System
Manages 4 analog pressure sensors with per-sensor scaling.
Bronkhorst pressure controllers are managed by PRG_BronkhorstPressure
and injected via Configure().

NOTE: PSNS_CHK_VAC has inverted physical mapping (higher raw = more vacuum = lower pressure).
      FB_AnalogSensor requires ascending ranges, so the reading is physically inverted.
      Consumers must compute: actual_pressure_bar = 1.0165 - sensor_reading
      TODO: Consider extending FB_AnalogSensor to support inverted scaling.
*)
PROGRAM PRG_PRESSURE_SENSOR
VAR
    // Hardware IO - Analog Pressure Sensors
    // Declared as VAR (not VAR_INPUT) so TwinCAT I/O linker exposes AT %I*/%Q*
    // members for EtherCAT PDO mapping. PRG_SIMULATION writes to these via
    // pointer/ADR — this works identically for VAR.
    IO_PressureSensors : ARRAY[1..GVL_PressureSystem.SENSOR_COUNT] OF T_Beckhoff_PressureSensor;
END_VAR
VAR
    // Subsystem and Manual Panel
    fbPressureSubsystem : FB_PressureSubsystem;
    fbManualPanel       : FB_PressureManualPanel;

    // Adapters and Sensors
    afbAdapters : ARRAY[1..GVL_PressureSystem.SENSOR_COUNT] OF FB_AnalogInputAdapter;
    afbSensors  : ARRAY[1..GVL_PressureSystem.SENSOR_COUNT] OF FB_AnalogSensor;

    // Interface arrays
    aipSensors     : ARRAY[1..GVL_PressureSystem.SENSOR_COUNT] OF I_Sensor;
    aipControllers : ARRAY[1..GVL_PressureSystem.CONTROLLER_COUNT] OF I_Controller;

    bConfigured     : BOOL := FALSE;
    bInitialized    : BOOL := FALSE;
    _bNotConfiguredLogged : BOOL := FALSE;
    i : INT;
END_VAR

    IF NOT bConfigured THEN
        IF NOT _bNotConfiguredLogged THEN
            GVL_ErrorLogger.Logger.LogError(
                sSource  := 'PRG_PRESSURE_SENSOR',
                eErrorId := E_ErrorId.ERR_GEN_NOT_CONFIGURED,
                sContext := 'PRG_PRESSURE_SENSOR executed before Configure(); pressure sensors will not run'
            );
            _bNotConfiguredLogged := TRUE;
        END_IF
        RETURN;
    END_IF

    IF NOT bInitialized THEN
        _Init();
    END_IF

    // Execute adapters and sensors
    FOR i := 1 TO GVL_PressureSystem.SENSOR_COUNT DO
        afbAdapters[i].Execute();
        afbSensors[i].Execute();
    END_FOR

    // Execute subsystem (error aggregation)
    fbPressureSubsystem.Execute();
    fbManualPanel(ipSubsystem := fbPressureSubsystem, nMaxSensor := GVL_PressureSystem.SENSOR_COUNT, nMaxCtrl := GVL_PressureSystem.CONTROLLER_COUNT);

    // Dependency injection - accepts controller interfaces from PRG_BronkhorstPressure
METHOD PUBLIC Configure
VAR_INPUT
    aipCtrl : ARRAY[1..GVL_PressureSystem.CONTROLLER_COUNT] OF I_Controller;
END_VAR
VAR
    iLocal : INT;
END_VAR
    IF bConfigured THEN RETURN;
    END_IF

    FOR iLocal := 1 TO GVL_PressureSystem.CONTROLLER_COUNT DO
        aipControllers[iLocal] := aipCtrl[iLocal];
    END_FOR

    bConfigured := TRUE;
END_METHOD

METHOD PRIVATE _Init
VAR
    eId : E_PressureSensorIdentifier;
END_VAR
    // PSNS_A_DST: A-side Distributor [0-3 bar]
    _InitSensor(
        eId       := E_PressureSensorIdentifier.PSNS_A_DST,
        rRawMin   := GVL_PressureSystem.PSNS_A_DST_RAW_MIN,
        rRawMax   := GVL_PressureSystem.PSNS_A_DST_RAW_MAX,
        rScaledMin := GVL_PressureSystem.PSNS_A_DST_SCALED_MIN,
        rScaledMax := GVL_PressureSystem.PSNS_A_DST_SCALED_MAX
    );

    // PSNS_B_DST: B-side Distributor [0-3 bar]
    _InitSensor(
        eId       := E_PressureSensorIdentifier.PSNS_B_DST,
        rRawMin   := GVL_PressureSystem.PSNS_B_DST_RAW_MIN,
        rRawMax   := GVL_PressureSystem.PSNS_B_DST_RAW_MAX,
        rScaledMin := GVL_PressureSystem.PSNS_B_DST_SCALED_MIN,
        rScaledMax := GVL_PressureSystem.PSNS_B_DST_SCALED_MAX
    );

    // PSNS_AB_EXH: Exhaust Vacuum [0-1 bar]
    _InitSensor(
        eId       := E_PressureSensorIdentifier.PSNS_AB_EXH,
        rRawMin   := GVL_PressureSystem.PSNS_AB_EXH_RAW_MIN,
        rRawMax   := GVL_PressureSystem.PSNS_AB_EXH_RAW_MAX,
        rScaledMin := GVL_PressureSystem.PSNS_AB_EXH_SCALED_MIN,
        rScaledMax := GVL_PressureSystem.PSNS_AB_EXH_SCALED_MAX
    );

    // PSNS_CHK_VAC: Chuck Vacuum [inverted: see NOTE above]
    _InitSensor(
        eId       := E_PressureSensorIdentifier.PSNS_CHK_VAC,
        rRawMin   := GVL_PressureSystem.PSNS_CHK_VAC_RAW_MIN,
        rRawMax   := GVL_PressureSystem.PSNS_CHK_VAC_RAW_MAX,
        rScaledMin := GVL_PressureSystem.PSNS_CHK_VAC_SCALED_MIN,
        rScaledMax := GVL_PressureSystem.PSNS_CHK_VAC_SCALED_MAX
    );

    // Initialize subsystem with sensors + injected controllers
    fbPressureSubsystem.Init(aipSensors := aipSensors, nSensorCount := GVL_PressureSystem.SENSOR_COUNT, aipControllers := aipControllers, nControllerCount := GVL_PressureSystem.CONTROLLER_COUNT);
    bInitialized := TRUE;
END_METHOD

METHOD PRIVATE _InitSensor
VAR_INPUT
    eId        : E_PressureSensorIdentifier;
    rRawMin    : LREAL;
    rRawMax    : LREAL;
    rScaledMin : LREAL;
    rScaledMax : LREAL;
END_VAR
VAR
    nIdx : INT;
END_VAR
    nIdx := eId;

    afbAdapters[nIdx].Link(ADR(IO_PressureSensors[nIdx]));
    afbSensors[nIdx].Init(
        sInstanceName := TO_STRING(eId),
        ipRawInput    := afbAdapters[nIdx],
        rRawMin       := rRawMin,
        rRawMax       := rRawMax,
        rScaledMin    := rScaledMin,
        rScaledMax    := rScaledMax
    );
    aipSensors[nIdx] := afbSensors[nIdx];
END_METHOD

END_PROGRAM
