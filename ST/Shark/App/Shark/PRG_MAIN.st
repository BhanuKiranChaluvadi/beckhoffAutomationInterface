PROGRAM PRG_MAIN
VAR
    // === LEGACY CLIENT COMPAT ===
    // Exposes ADS symbol contract MAIN.fbDALP.stTelemetry / .AdsStream that
    // the existing C# DALP Manager expects unchanged. Delete this var, the
    // call below, and the App/Shark/Telemetry/LegacyClientCompat/ folder
    // when the client has been migrated to the new symbol paths.
    fbDALP : FB_DALP;
    _bDALPInited : BOOL := FALSE;
END_VAR
    PRG_SHARK();

    // Wire the legacy wrapper once subsystems + publisher + receiver are ready.
    IF NOT _bDALPInited AND PRG_Telemetry._bInitialized THEN
        _bDALPInited := fbDALP.Init(
            refPublisher := PRG_Telemetry.fbTelemetryPublisher,
            refReceiver  := PRG_Telemetry.fbCommandReceiver,
            refTempSub   := PRG_HEAT_ZONE.fbTemperatureSubsystem
        );
    END_IF

    fbDALP();
END_PROGRAM
