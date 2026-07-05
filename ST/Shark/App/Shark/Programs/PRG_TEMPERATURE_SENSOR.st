PROGRAM PRG_TEMPERATURE_SENSOR
(*
Shark temperature sensor wiring program.

  * 10 channels total (one per RTD+SSR pair) split across 8 zones.
  * Each channel keeps its existing A+B redundant RTDs wrapped in
    FB_DualSensor; the resulting I_Sensor is what the channel controller sees.
  * Channel ordering MUST match the order zones appear in E_HeatZone
    times the order channels appear within each zone. The subsystem
    relies on this ordering when slicing the flat channel array per zone.

Channel layout:
  HETR_HEAD   (1)      Shared head
  HETR_A_STR  (2)      A-side start (umbilical)
  HETR_A_CMB  (3)      A-side combiner / hot manifold
  HETR_A_B1   (4)      A-side bubbler 1
  HETR_A_B2   (5)      A-side bubbler 2
  HETR_A_B3   (6)      A-side bubbler 3
  HETR_B_STR  (7)      B-side start (umbilical)
  HETR_B_CMB  (8)      B-side combiner / hot manifold
  HETR_B_B1   (9)      B-side bubbler 1
  HETR_B_B2   (10)     B-side bubbler 2
  HETR_B_B3   (11)     B-side bubbler 3
  HETR_EXH    (12..13) Exhaust inner, outer
  HETR_CHK    (14..15) Chuck inner, outer
*)
VAR
    // --- Hardware IO (linked to EtherCAT process image) ---

    // HETR_HEAD (channel 1)
    HEAD_A   : T_Beckhoff_TempSensor_General;   // 1
    HEAD_B   : T_Beckhoff_TempSensor_General;   // 2
    // HETR_A_STR (channel 2)
    A_STR_A  : T_Beckhoff_TempSensor_General;   // 3
    A_STR_B  : T_Beckhoff_TempSensor_General;   // 4
    // HETR_A_CMB (channel 3)
    A_CMB_A  : T_Beckhoff_TempSensor_General;   // 5
    A_CMB_B  : T_Beckhoff_TempSensor_General;   // 6
    // HETR_A_B1 (channel 4)
    A_B1_A   : T_Beckhoff_TempSensor_General;   // 7
    A_B1_B   : T_Beckhoff_TempSensor_General;   // 8
    // HETR_A_B2 (channel 5)
    A_B2_A   : T_Beckhoff_TempSensor_General;   // 9
    A_B2_B   : T_Beckhoff_TempSensor_General;   // 10
    // HETR_A_B3 (channel 6)
    A_B3_A   : T_Beckhoff_TempSensor_General;   // 11
    A_B3_B   : T_Beckhoff_TempSensor_General;   // 12
    // HETR_B_STR (channel 7)
    B_STR_A  : T_Beckhoff_TempSensor_General;   // 13
    B_STR_B  : T_Beckhoff_TempSensor_General;   // 14
    // HETR_B_CMB (channel 8)
    B_CMB_A  : T_Beckhoff_TempSensor_General;   // 15
    B_CMB_B  : T_Beckhoff_TempSensor_General;   // 16
    // HETR_B_B1 (channel 9)
    B_B1_A   : T_Beckhoff_TempSensor_General;   // 17
    B_B1_B   : T_Beckhoff_TempSensor_General;   // 18
    // HETR_B_B2 (channel 10)
    B_B2_A   : T_Beckhoff_TempSensor_General;   // 19
    B_B2_B   : T_Beckhoff_TempSensor_General;   // 20
    // HETR_B_B3 (channel 11)
    B_B3_A   : T_Beckhoff_TempSensor_General;   // 21
    B_B3_B   : T_Beckhoff_TempSensor_General;   // 22
    // HETR_EXH (channels 12..13)
    EXH_IN_A    : T_Beckhoff_TempSensor_General;   // 23
    EXH_IN_B    : T_Beckhoff_TempSensor_General;   // 24
    EXH_OUT_A    : T_Beckhoff_TempSensor_General;   // 25
    EXH_OUT_B    : T_Beckhoff_TempSensor_General;   // 26 
    // HETR_CHK (channels 14..15) — single sensor per channel (A only)
    CHK_IN_A    : T_Beckhoff_TempSensor_Chuck;     // 27
    CHK_OUT_A    : T_Beckhoff_TempSensor_Chuck;    // 28

    // Stage temperature sensors (monitor only — no heater/PID)
    STG_X_TEMP  : T_Beckhoff_TempSensor_General;   // SENS_STAGE_X (2) — Stage X-axis temperature
    STG_Y_TEMP  : T_Beckhoff_TempSensor_General;   // SENS_STAGE_Y (3) — Stage Y-axis temperature
    AMBT_TEMP   : T_Beckhoff_AmbientSensor;         // SENS_CHAMBER (1) — TH sensor temperature channel
    SIDE_A_TEMP : T_Beckhoff_TempSensor_General;    // SENS_A_SIDE  (4) — A-side temperature
    SIDE_B_TEMP : T_Beckhoff_TempSensor_General;    // SENS_B_SIDE  (5) — B-side temperature

    // --- Monitor sensor FBs (one adapter+sensor per monitor zone) ---
    fbChamberAdapter  : FB_AnalogInputAdapter;  // SENS_CHAMBER (1)
    fbChamberSensor   : FB_AnalogSensor;
    fbStgXAdapter     : FB_AnalogInputAdapter;  // SENS_STAGE_X (2)
    fbStgXSensor      : FB_AnalogSensor;
    fbStgYAdapter     : FB_AnalogInputAdapter;  // SENS_STAGE_Y (3)
    fbStgYSensor      : FB_AnalogSensor;
    fbSideAAdapter    : FB_AnalogInputAdapter;  // SENS_A_SIDE  (4)
    fbSideASensor     : FB_AnalogSensor;
    fbSideBAdapter    : FB_AnalogInputAdapter;  // SENS_B_SIDE  (5)
    fbSideBSensor     : FB_AnalogSensor;

    // --- Adapters & Sensors (one A+B pair per channel) ---
    afbAdapters_A   : ARRAY[1..GVL_TemperatureZones.CHANNEL_MAX] OF FB_AnalogInputAdapter;
    afbAdapters_B   : ARRAY[1..GVL_TemperatureZones.CHANNEL_MAX] OF FB_AnalogInputAdapter;
    afbSensors_A    : ARRAY[1..GVL_TemperatureZones.CHANNEL_MAX] OF FB_AnalogSensor;    // EXH: channels 12..13; CHK: channels 14..15
    afbSensors_B    : ARRAY[1..GVL_TemperatureZones.CHANNEL_MAX] OF FB_AnalogSensor;
    afbDualSensors  : ARRAY[1..GVL_TemperatureZones.CHANNEL_MAX] OF FB_DualSensor;
    _abHasChannelB  : ARRAY[1..GVL_TemperatureZones.CHANNEL_MAX] OF BOOL;  // FALSE for single-sensor channels (e.g. CHK) — their B adapter/sensor are never initialized and must not be executed

    // --- Interface arrays consumed by FB_TemperatureSubsystem ---
    aipChannelSensors : ARRAY[1..GVL_TemperatureZones.CHANNEL_MAX] OF I_Sensor;
    aipMonitorSensors : ARRAY[1..GVL_TemperatureZones.MONITOR_ZONE_COUNT] OF I_Sensor;  // 1=SENS_CHAMBER, 2=SENS_STAGE_X, 3=SENS_STAGE_Y, 4=SENS_A_SIDE, 5=SENS_B_SIDE

    bInitialized : BOOL := FALSE;
    i : INT;
END_VAR

    IF NOT bInitialized THEN
        _Init();
    END_IF

    // Execute every channel's A + B + dual composite.
    // Single-sensor channels (B pointer null at init) have no B adapter/sensor —
    // skip them, otherwise the never-initialized B FB raises 'Sensor not
    // initialized' (with an empty source) on every scan cycle.
    FOR i := 1 TO GVL_TemperatureZones.CHANNEL_MAX DO
        afbAdapters_A[i].Execute();
        afbSensors_A[i].Execute();
        IF _abHasChannelB[i] THEN
            afbAdapters_B[i].Execute();
            afbSensors_B[i].Execute();
        END_IF
        afbDualSensors[i].Execute();
    END_FOR

    // Execute monitor sensors (one adapter+sensor pair per zone)
    fbChamberAdapter.Execute();  fbChamberSensor.Execute();
    fbStgXAdapter.Execute();     fbStgXSensor.Execute();
    fbStgYAdapter.Execute();     fbStgYSensor.Execute();
    fbSideAAdapter.Execute();    fbSideASensor.Execute();
    fbSideBAdapter.Execute();    fbSideBSensor.Execute();

METHOD PRIVATE _Init
    // Wire each channel's A+B raw RTDs into a dual sensor.
    // Order MUST match the channel layout in the file header.
    _InitElement(1,  ADR(HEAD_A),  ADR(HEAD_B));
    _InitElement(2,  ADR(A_STR_A), ADR(A_STR_B));
    _InitElement(3,  ADR(A_CMB_A), ADR(A_CMB_B));
    _InitElement(4,  ADR(A_B1_A),  ADR(A_B1_B));
    _InitElement(5,  ADR(A_B2_A),  ADR(A_B2_B));
    _InitElement(6,  ADR(A_B3_A),  ADR(A_B3_B));
    _InitElement(7,  ADR(B_STR_A), ADR(B_STR_B));
    _InitElement(8,  ADR(B_CMB_A), ADR(B_CMB_B));
    _InitElement(9,  ADR(B_B1_A),  ADR(B_B1_B));
    _InitElement(10, ADR(B_B2_A),  ADR(B_B2_B));
    _InitElement(11, ADR(B_B3_A),  ADR(B_B3_B));
    _InitElement(12, ADR(EXH_IN_A),   ADR(EXH_IN_B));      // EXH: Inner
    _InitElement(13, ADR(EXH_OUT_A),   ADR(EXH_OUT_B));     // EXH: Outer
    _InitElement(14, ADR(CHK_IN_A),    0);          // CHK: single sensor per channel
    _InitElement(15, ADR(CHK_OUT_A),   0);          // CHK: single sensor per channel

    // Wire monitor sensors (one per E_TemperatureMonitorZone entry)
    _InitMonitorSensor(
        eZone      := E_TemperatureMonitorZone.SENS_CHAMBER,
        sName      := 'AMBT_TEMP',
        refAdapter := fbChamberAdapter,
        refSensor  := fbChamberSensor,
        pChannel   := ADR(AMBT_TEMP),
        rRawMin    := GVL_TemperatureSystem.AMBT_TEMP_RAW_MIN,
        rRawMax    := GVL_TemperatureSystem.AMBT_TEMP_RAW_MAX,
        rScaledMin := GVL_TemperatureSystem.AMBT_TEMP_SCALED_MIN,
        rScaledMax := GVL_TemperatureSystem.AMBT_TEMP_SCALED_MAX
    );
    _InitMonitorSensor(
        eZone      := E_TemperatureMonitorZone.SENS_STAGE_X,
        sName      := 'STG_X_TEMP',
        refAdapter := fbStgXAdapter,
        refSensor  := fbStgXSensor,
        pChannel   := ADR(STG_X_TEMP),
        rRawMin    := 0.0,
        rRawMax    := GVL_TemperatureSystem.TEMP_RAW_AT_MAX_SENSOR,
        rScaledMin := 0.0,
        rScaledMax := GVL_TemperatureSystem.SENSOR_MAX_TEMP
    );
    _InitMonitorSensor(
        eZone      := E_TemperatureMonitorZone.SENS_STAGE_Y,
        sName      := 'STG_Y_TEMP',
        refAdapter := fbStgYAdapter,
        refSensor  := fbStgYSensor,
        pChannel   := ADR(STG_Y_TEMP),
        rRawMin    := 0.0,
        rRawMax    := GVL_TemperatureSystem.TEMP_RAW_AT_MAX_SENSOR,
        rScaledMin := 0.0,
        rScaledMax := GVL_TemperatureSystem.SENSOR_MAX_TEMP
    );
    _InitMonitorSensor(
        eZone      := E_TemperatureMonitorZone.SENS_A_SIDE,
        sName      := 'SIDE_A_TEMP',
        refAdapter := fbSideAAdapter,
        refSensor  := fbSideASensor,
        pChannel   := ADR(SIDE_A_TEMP),
        rRawMin    := 0.0,
        rRawMax    := GVL_TemperatureSystem.TEMP_RAW_AT_MAX_SENSOR,
        rScaledMin := 0.0,
        rScaledMax := GVL_TemperatureSystem.SENSOR_MAX_TEMP
    );
    _InitMonitorSensor(
        eZone      := E_TemperatureMonitorZone.SENS_B_SIDE,
        sName      := 'SIDE_B_TEMP',
        refAdapter := fbSideBAdapter,
        refSensor  := fbSideBSensor,
        pChannel   := ADR(SIDE_B_TEMP),
        rRawMin    := 0.0,
        rRawMax    := GVL_TemperatureSystem.TEMP_RAW_AT_MAX_SENSOR,
        rScaledMin := 0.0,
        rScaledMax := GVL_TemperatureSystem.SENSOR_MAX_TEMP
    );

    bInitialized := TRUE;
END_METHOD

METHOD PRIVATE _InitElement
VAR_INPUT
    nGlobalIdx : INT;
    pChannel_A : POINTER TO BYTE;
    pChannel_B : POINTER TO BYTE;  // NULL (0) for single-sensor channels
END_VAR
VAR
    ipSecondary : I_Sensor;  // NULL when pChannel_B is null (triggers FB_DualSensor single-mode)
END_VAR
    // Channel A (always present)
    afbAdapters_A[nGlobalIdx].Link(pChannel_A);
    afbSensors_A[nGlobalIdx].Init(
        sInstanceName := CONCAT('TEMP_E', CONCAT(TO_STRING(nGlobalIdx), '_A')),
        ipRawInput    := afbAdapters_A[nGlobalIdx],
        rRawMin       := 0.0,
        rRawMax       := GVL_TemperatureSystem.TEMP_RAW_AT_MAX_SENSOR,
        rScaledMin    := 0.0,
        rScaledMax    := GVL_TemperatureSystem.SENSOR_MAX_TEMP
    );
    
    // Channel B (optional - skip if null pointer provided)
    IF pChannel_B <> 0 THEN
        afbAdapters_B[nGlobalIdx].Link(pChannel_B);
        afbSensors_B[nGlobalIdx].Init(
            sInstanceName := CONCAT('TEMP_E', CONCAT(TO_STRING(nGlobalIdx), '_B')),
            ipRawInput    := afbAdapters_B[nGlobalIdx],
            rRawMin       := 0.0,
            rRawMax       := GVL_TemperatureSystem.TEMP_RAW_AT_MAX_SENSOR,
            rScaledMin    := 0.0,
            rScaledMax    := GVL_TemperatureSystem.SENSOR_MAX_TEMP
        );
        ipSecondary := afbSensors_B[nGlobalIdx];
        _abHasChannelB[nGlobalIdx] := TRUE;
    ELSE
        // Single-sensor mode: secondary remains NULL, triggering FB_DualSensor single-mode
        ipSecondary := 0;
        _abHasChannelB[nGlobalIdx] := FALSE;
    END_IF
    
    // Dual composite (single-mode when ipSecondary = 0)
    afbDualSensors[nGlobalIdx].Init(
        sInstanceName := CONCAT('DUAL_E', TO_STRING(nGlobalIdx)),
        ipPrimary     := afbSensors_A[nGlobalIdx],
        ipSecondary   := ipSecondary,
        rMaxDeviation := GVL_TemperatureZoneConfig.CHANNEL_MAX_DEVIATION[nGlobalIdx]
    );
    aipChannelSensors[nGlobalIdx] := afbDualSensors[nGlobalIdx];
END_METHOD

METHOD PRIVATE _InitMonitorSensor
VAR_INPUT
    eZone      : E_TemperatureMonitorZone;
    sName      : STRING;
    pChannel   : POINTER TO BYTE;
    rRawMin    : LREAL;
    rRawMax    : LREAL;
    rScaledMin : LREAL;
    rScaledMax : LREAL;
END_VAR
VAR_IN_OUT
    refAdapter : FB_AnalogInputAdapter;
    refSensor  : FB_AnalogSensor;
END_VAR
    refAdapter.Link(pChannel);
    refSensor.Init(
        sInstanceName := sName,
        ipRawInput    := refAdapter,
        rRawMin       := rRawMin,
        rRawMax       := rRawMax,
        rScaledMin    := rScaledMin,
        rScaledMax    := rScaledMax
    );
    aipMonitorSensors[eZone] := refSensor;
END_METHOD

END_PROGRAM
