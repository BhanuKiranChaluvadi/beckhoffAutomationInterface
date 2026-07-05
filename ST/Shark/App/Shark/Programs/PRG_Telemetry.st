(*
PRG_Telemetry — Orchestrates telemetry publication and command dispatch.
Called every scan from PRG_SHARK after all subsystems have been updated.

Publishes the detailed (per-channel) telemetry view from the subsystem
references.

ADS Symbol Mapping:
  READ:  PRG_Telemetry.fbTelemetryPublisher.stTelemetry               (ST_MachineTelemetry)
  WRITE: PRG_Telemetry.fbCommandReceiver.AdsStream                    (ARRAY[0..4095] OF BYTE)
  READ:  PRG_Telemetry.fbCommandReceiver.nLastCommandIndex            (DINT)
*)
PROGRAM PRG_Telemetry
VAR
    fbTelemetryPublisher        : FB_TelemetryPublisher;
    fbCommandReceiver           : FB_CommandReceiver;
    _bInitialized               : BOOL := FALSE;
END_VAR

    IF NOT _bInitialized THEN
        _Init();
        RETURN;
    END_IF

    _Execute();

METHOD PRIVATE _Init
VAR
    bOk : BOOL;
END_VAR
    bOk := fbTelemetryPublisher.Init(
        refValveSub    := PRG_VALVE.fbValveSubsystem,
        refFlowSub     := PRG_MFC.fbFlowSubsystem,
        refPressureSub := PRG_PRESSURE_SENSOR.fbPressureSubsystem,
        refTempSub     := PRG_HEAT_ZONE.fbTemperatureSubsystem,
        refDISub       := PRG_DIGITAL_INPUT.fbDigitalInputSubsystem,
        refDOSub       := PRG_DIGITAL_OUTPUT.fbDigitalOutputSubsystem
    );
    IF NOT bOk THEN RETURN; END_IF

    bOk := fbCommandReceiver.Init(
        ipHardware            := PRG_SHARK.fbHardwareProvider,
        ipSpectrometer        := PRG_Spectrometer.fbSpectrometer,
        rTempDisableThreshold := 0.0,
        refOrchestrator       := PRG_Sequence.fbOrchestrator
    );
    IF NOT bOk THEN RETURN; END_IF

    _bInitialized := TRUE;
END_METHOD

METHOD PRIVATE _Execute
    // Update telemetry struct (read by clients via ADS)
    fbTelemetryPublisher();

    // Poll for incoming commands (written by clients via ADS)
    fbCommandReceiver();

    // Echo last command index into the telemetry struct for client-side sync
    fbTelemetryPublisher.SetCommandIndex(nCommandIndex := fbCommandReceiver.nLastCommandIndex);
END_METHOD

END_PROGRAM
