PROGRAM PRG_HEAT_ZONE
(*
Shark heater output wiring + temperature subsystem entry point.

  * 10 SSR outputs (one per channel) split across 8 zones.
  * Channel ordering MUST match PRG_TEMPERATURE_SENSOR's channel layout.
*)
VAR
    // --- Heater Digital Outputs (linked to EtherCAT process image) ---
    // HETR_HEAD   (channel 1)
    HEAD_Htr          AT %Q* : BOOL;
    // HETR_A_STR  (channel 2)
    A_STR_Htr         AT %Q* : BOOL;
    // HETR_A_CMB  (channel 3)
    A_CMB_Htr         AT %Q* : BOOL;
    // HETR_A_B1   (channel 4)
    A_B1_Htr          AT %Q* : BOOL;
    // HETR_A_B2   (channel 5)
    A_B2_Htr          AT %Q* : BOOL;
    // HETR_A_B3   (channel 6)
    A_B3_Htr          AT %Q* : BOOL;
    // HETR_B_STR  (channel 7)
    B_STR_Htr         AT %Q* : BOOL;
    // HETR_B_CMB  (channel 8)
    B_CMB_Htr         AT %Q* : BOOL;
    // HETR_B_B1   (channel 9)
    B_B1_Htr          AT %Q* : BOOL;
    // HETR_B_B2   (channel 10)
    B_B2_Htr          AT %Q* : BOOL;
    // HETR_B_B3   (channel 11)
    B_B3_Htr          AT %Q* : BOOL;
    // HETR_EXH    (channels 12..13)
    EXH_Htr           AT %Q* : BOOL;
    EXH_E2_Htr        AT %Q* : BOOL;
    // HETR_CHK    (channels 14..15)
    CHK_Htr           AT %Q* : BOOL;
    CHK_E2_Htr        AT %Q* : BOOL;

    // --- Heater Adapters ---
    afbElementHeaters : ARRAY[1..GVL_TemperatureZones.CHANNEL_MAX] OF FB_DigitalOutput;

    // --- Interface array consumed by FB_TemperatureSubsystem ---
    aipChannelHeaters : ARRAY[1..GVL_TemperatureZones.CHANNEL_MAX] OF I_DigitalOutput;

    // --- Application ---
    fbTemperatureSubsystem : FB_TemperatureSubsystem;
    fbManualPanel          : FB_TemperatureManualPanel;
    fbTuningPanel          : FB_BeckhoffTuningPanel;

    bInitialized : BOOL := FALSE;
END_VAR

    IF NOT bInitialized THEN
        _Init();
    END_IF

    // Run all zones and channels
    fbTemperatureSubsystem.Execute();

    // HMI Panels
    fbManualPanel(ipSubsystem := fbTemperatureSubsystem, nMaxZone := GVL_TemperatureZones.CONTROLLABLE_ZONE_MAX);
    fbTuningPanel(
        ipTunable := fbTemperatureSubsystem.GetTunable(
            eZone       := fbTuningPanel.iSelectedZoneIdx,
            nChannelIdx := fbTuningPanel.iSelectedChannelIdx,
            bWholeZone  := fbTuningPanel.bTuneWholeZone)
    );

METHOD PRIVATE _Init
    // Initialize channel heaters in channel-order (matches PRG_TEMPERATURE_SENSOR).
    _InitElement(1,  HEAD_Htr);
    _InitElement(2,  A_STR_Htr);
    _InitElement(3,  A_CMB_Htr);
    _InitElement(4,  A_B1_Htr);
    _InitElement(5,  A_B2_Htr);
    _InitElement(6,  A_B3_Htr);
    _InitElement(7,  B_STR_Htr);
    _InitElement(8,  B_CMB_Htr);
    _InitElement(9,  B_B1_Htr);
    _InitElement(10, B_B2_Htr);
    _InitElement(11, B_B3_Htr);
    _InitElement(12, EXH_Htr);
    _InitElement(13, EXH_E2_Htr);
    _InitElement(14, CHK_Htr);
    _InitElement(15, CHK_E2_Htr);

    // Initialize subsystem (sensors come from PRG_TEMPERATURE_SENSOR)
    bInitialized := fbTemperatureSubsystem.Init(
        aipChannelSensors := PRG_TEMPERATURE_SENSOR.aipChannelSensors,
        aipMonitorSensors := PRG_TEMPERATURE_SENSOR.aipMonitorSensors,
        aipChannelHeaters := aipChannelHeaters
    );
END_METHOD

METHOD PRIVATE _InitElement
VAR_INPUT
    nGlobalIdx : INT;
END_VAR
VAR_IN_OUT
    bOutput : BOOL;
END_VAR
    afbElementHeaters[nGlobalIdx].Init(
        sInstanceName := CONCAT('HEATER_E', TO_STRING(nGlobalIdx)),
        ipOutputRef   := bOutput
    );
    aipChannelHeaters[nGlobalIdx] := afbElementHeaters[nGlobalIdx];
END_METHOD

END_PROGRAM
