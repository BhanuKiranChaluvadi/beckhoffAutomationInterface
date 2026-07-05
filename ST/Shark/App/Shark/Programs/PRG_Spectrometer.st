(*
PRG_Spectrometer — Runs the spectrometer hardware driver at 1ms cycle.
*)
PROGRAM PRG_Spectrometer
VAR
    stOutputRegisters      AT%Q* : ST_OutputRegistersRaw;
    stInputRegisters       AT%I* : ST_InputRegistersRaw;
    aInputLogicSignals     AT%I* : ARRAY[1..4] OF USINT;
    aOutputLogicSignals    AT%Q* : ARRAY[1..4] OF USINT;
    fbSpectrometer               : FB_Spectrometer;
    stTelemetry                  : ST_SpectrometerTelemetryData;
    stDebugControls              : ST_DebugControls;
    _bInitialized                : BOOL := FALSE;
END_VAR

    IF NOT _bInitialized THEN
        _bInitialized := fbSpectrometer.Init(
            refOutput             := stOutputRegisters,
            refInput              := stInputRegisters,
            refInputLogicSignals  := aInputLogicSignals,
            refOutputLogicSignals := aOutputLogicSignals
        );
    END_IF

    IF NOT _bInitialized THEN
        RETURN;
    END_IF

    // 1. Execute hardware handshake (STROBE/ACK)
    fbSpectrometer.Execute();

    // 2. Publish telemetry (read by DALP Manager via ADS at 1ms)
    stTelemetry.Measurement := fbSpectrometer.GetMeasurement();
    stTelemetry.Settings    := fbSpectrometer.GetDeviceSettings();

    // 3. Manual debug overrides
    IF stDebugControls.bApplyLimits THEN
        fbSpectrometer.SetSpectrumLimits(
            nStartRatio  := stDebugControls.StartRatio,
            nStopRatio   := stDebugControls.StopRatio,
            nTotalPixels := stDebugControls.nTotalPixels
        );
        stDebugControls.bApplyLimits := FALSE;
    END_IF

    IF stDebugControls.bApplyFreq THEN
        fbSpectrometer.SetFrequency(ScanRate := TO_UDINT(stDebugControls.Frequency));
        stDebugControls.bApplyFreq := FALSE;
    END_IF

    IF stDebugControls.bApplyLight THEN
        fbSpectrometer.SetLightIntensity(
            TargetLight := stDebugControls.TargetLight,
            AutoAdjust  := TRUE
        );
        stDebugControls.bApplyLight := FALSE;
    END_IF

END_PROGRAM
