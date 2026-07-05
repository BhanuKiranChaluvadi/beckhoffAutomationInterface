(*
FB_DALP — Legacy client compatibility wrapper.

Exposes the exact ADS symbol contract the old C# DALP Manager expects:
  * MAIN.fbDALP.stTelemetry  (ST_Atlant3d_Telemetry, alias of ST_MachineTelemetry)
  * MAIN.fbDALP.AdsStream    (ARRAY[0..4095] OF BYTE, command write path)

This FB does not own any subsystem logic. It is a thin pass-through:
  1. Each scan, copy the new publisher's stTelemetry into our own
     stTelemetry (struct types alias each other, so the assignment is
     byte-for-byte equivalent).
  2. Overwrite the HETR_ array per-CHANNEL (not per-zone) so the legacy
     client's E_HeatZones iteration (15 entries) gets the right data.
  3. Forward any bytes the client wrote into AdsStream to the receiver's
     buffer, then clear the local buffer.

DELETE THIS FOLDER ONCE THE CLIENT IS UPDATED. See README.md.
*)
FUNCTION_BLOCK FB_DALP
VAR
    stTelemetry       : ST_Atlant3d_Telemetry;
    AdsStream         : ARRAY[0..4095] OF BYTE;
    _ZeroArray        : ARRAY[0..4095] OF BYTE;

    _refPublisher     : REFERENCE TO FB_TelemetryPublisher;
    _refReceiver      : REFERENCE TO FB_CommandReceiver;
    _refTempSub       : REFERENCE TO FB_TemperatureSubsystem;

    _bInitialized     : BOOL := FALSE;
END_VAR

    IF NOT _bInitialized THEN
        RETURN;
    END_IF

    // 1. Mirror the new publisher's full telemetry (identical struct shape via alias)
    stTelemetry := _refPublisher.stTelemetry;

    // 2. Re-fill HETR_ per-CHANNEL for the legacy client (15 entries on Shark)
    _UpdateHeatersPerChannel();

    // 3. Forward any client-written command bytes to the receiver
    IF MEMCMP(ADR(AdsStream), ADR(_ZeroArray), SIZEOF(AdsStream)) <> 0 THEN
        MEMCPY(
            destAddr := ADR(_refReceiver.AdsStream),
            srcAddr  := ADR(AdsStream),
            n        := SIZEOF(AdsStream)
        );
        MEMSET(ADR(AdsStream), 0, SIZEOF(AdsStream));
    END_IF


METHOD PUBLIC Init : BOOL
VAR_INPUT
    refPublisher : REFERENCE TO FB_TelemetryPublisher;
    refReceiver  : REFERENCE TO FB_CommandReceiver;
    refTempSub   : REFERENCE TO FB_TemperatureSubsystem;
END_VAR
    IF NOT __ISVALIDREF(refPublisher) THEN RETURN; END_IF
    IF NOT __ISVALIDREF(refReceiver)  THEN RETURN; END_IF
    IF NOT __ISVALIDREF(refTempSub)   THEN RETURN; END_IF

    _refPublisher REF= refPublisher;
    _refReceiver  REF= refReceiver;
    _refTempSub   REF= refTempSub;
    _bInitialized := TRUE;
    Init := TRUE;
END_METHOD


(* Per-CHANNEL heater telemetry for the legacy client.
   Iterates every (zone, channel) pair and writes HETR_[globalIdx]. *)
{attribute 'estimated-stack-usage' := '500'}
METHOD PRIVATE _UpdateHeatersPerChannel
VAR
    iZone       : INT;
    c           : INT;
    nCount      : INT;
    nGlobalIdx  : INT;
    ipChannel   : I_Controller;
    stCtrl      : ST_ControllerStatus;
END_VAR
    FOR iZone := 1 TO GVL_TemperatureZones.CONTROLLABLE_ZONE_MAX DO
        nCount := GVL_TemperatureZoneConfig.ZONE_CONFIG[iZone].nChannelCount;
        FOR c := 1 TO nCount DO
            nGlobalIdx := _refTempSub.GetChannelGlobalIndex(eZone := iZone, nChannelIdx := c);
            IF nGlobalIdx < 1 THEN CONTINUE; END_IF

            ipChannel := _refTempSub.GetChannel(eZone := iZone, nChannelIdx := c);
            IF ipChannel = 0 THEN CONTINUE; END_IF

            stCtrl := ipChannel.GetStatus();
            stTelemetry.HETR_[nGlobalIdx].Enable             := stCtrl.bEnabled;
            stTelemetry.HETR_[nGlobalIdx].TempAvg            := TO_REAL(stCtrl.rMeasuredValue);
            stTelemetry.HETR_[nGlobalIdx].SP                 := TO_REAL(stCtrl.rSetpoint);
            stTelemetry.HETR_[nGlobalIdx].TemperatureReached := stCtrl.bAtSetpoint;
            stTelemetry.HETR_[nGlobalIdx].Status             := SEL(
                stCtrl.stError.eErrorId = E_ErrorId.NO_ERROR, INT#1, INT#0);
        END_FOR
    END_FOR
END_METHOD

END_FUNCTION_BLOCK
