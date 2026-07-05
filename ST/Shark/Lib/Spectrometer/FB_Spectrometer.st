(*
FB_Spectrometer — Hardware driver for spectrometer via EtherCAT registers.
Implements I_Spectrometer. Direct port from old FB_Spectrometer.

Communication: STROBE/ACK handshake.
  PLC sets register + STROBE=31 → device ACKs with AckErr=31 → PLC clears STROBE.

Dark calibration: Uses separate logic signals (not register-mapped) for handshake.
  PLC sets _outputLogicSignals[1]=1 → device mirrors to _inputLogicSignals[1] → PLC clears.
*)
FUNCTION_BLOCK FB_Spectrometer IMPLEMENTS I_Spectrometer
VAR
    _refOutput             : REFERENCE TO ST_OutputRegistersRaw;
    _refInput              : REFERENCE TO ST_InputRegistersRaw;
    _refInputLogicSignals  : REFERENCE TO ARRAY[1..4] OF USINT;
    _refOutputLogicSignals : REFERENCE TO ARRAY[1..4] OF USINT;
    _cmdQueue              : FB_CommandQueue;
    _tonCommandTimeout     : TON;
    _bSavingDark           : BOOL := FALSE;
    _bInitialized          : BOOL := FALSE;
END_VAR
VAR CONSTANT
    STROBE_EXECUTE         : UDINT := 31;
    PEAK_RAW_TO_WAVELENGTH : LREAL := 65536000.0;
    ADC_12BIT_MAX          : REAL  := 4095.0;
    DEVICE_SCALE_FACTOR    : REAL  := 10.0;
    RATIO_MAX              : UDINT := 1000;
    SCAN_RATE_MAX          : UDINT := 4;
    COMMAND_TIMEOUT        : TIME  := T#5S;
END_VAR


METHOD PUBLIC Init : BOOL
VAR_INPUT
    refOutput             : REFERENCE TO ST_OutputRegistersRaw;
    refInput              : REFERENCE TO ST_InputRegistersRaw;
    refInputLogicSignals  : REFERENCE TO ARRAY[1..4] OF USINT;
    refOutputLogicSignals : REFERENCE TO ARRAY[1..4] OF USINT;
END_VAR
    IF NOT __ISVALIDREF(refOutput) OR NOT __ISVALIDREF(refInput) THEN
        RETURN;
    END_IF

    IF NOT __ISVALIDREF(refInputLogicSignals) OR NOT __ISVALIDREF(refOutputLogicSignals) THEN
        RETURN;
    END_IF

    _refOutput              REF= refOutput;
    _refInput               REF= refInput;
    _refInputLogicSignals   REF= refInputLogicSignals;
    _refOutputLogicSignals  REF= refOutputLogicSignals;
    _bInitialized           := TRUE;
    Init                    := TRUE;
END_METHOD


METHOD PUBLIC Execute : BOOL
VAR
    stCmd : ST_SpecCommand;
END_VAR
    IF NOT _bInitialized THEN
        RETURN;
    END_IF

    // Update timeout timer
    _tonCommandTimeout(IN := (_refOutput.STROBE <> 0), PT := COMMAND_TIMEOUT);

    // Handle dark calibration mode separately
    IF _bSavingDark THEN
        IF _refInputLogicSignals[1] = _refOutputLogicSignals[1] THEN
            // Device acknowledged dark calibration
            _refOutputLogicSignals[1] := 0;
            _bSavingDark := FALSE;
        END_IF
        Execute := TRUE;
        RETURN;
    END_IF

    // Normal command processing
    stCmd := _cmdQueue.Peek();

    IF NOT _cmdQueue.IsEmpty AND _refOutput.STROBE = 0 THEN
        // Send next command
        _refOutput.STROBE := STROBE_EXECUTE;
    ELSIF (_refInput.AckErr = _refOutput.STROBE) AND (_refOutput.STROBE <> 0) THEN
        // Device acknowledged command — advance queue
        _cmdQueue.Dequeue();
        _refOutput.STROBE := 0;
    ELSIF _tonCommandTimeout.Q THEN
        // Timeout — abandon current command and advance queue
        _cmdQueue.Dequeue();
        _refOutput.STROBE := 0;
        GVL_ErrorLogger.Logger.LogError(
            sSource   := 'Spectrometer',
            eErrorId  := E_ErrorId.ERR_COMM_TIMEOUT,
            sContext  := 'command timeout',
            eSeverity := E_TcEventSeverity.Warning
        );
    END_IF

    Execute := TRUE;
END_METHOD


METHOD PUBLIC CalibrateDark : BOOL
VAR_INPUT
    bExecute : BOOL;
END_VAR
    IF NOT _bInitialized THEN
        RETURN;
    END_IF

    IF bExecute AND NOT _bSavingDark THEN
        _refOutputLogicSignals[1] := 1;
        _bSavingDark := TRUE;
        CalibrateDark := TRUE;
    END_IF
END_METHOD


METHOD PUBLIC GetDeviceSettings : ST_DeviceSettings
VAR
    bAutoAdjust : BOOL;
    nStart      : UDINT;
    nStop       : UDINT;
END_VAR
    IF NOT _bInitialized THEN
        RETURN;
    END_IF

    GetDeviceSettings.ScanRate := F_ScanRateToHz(_refOutput.WP_SCAN_RATE);
    GetDeviceSettings.LightTargetPercentage := F_DecodeLightIntensity(
        nRegisterValue := _refOutput.WP_LAI_TARGET,
        bAutoAdjust    => bAutoAdjust
    );
    GetDeviceSettings.IntensityAutoAdjust := bAutoAdjust;
    GetDeviceSettings.SpectrumAverage := _refOutput.WP_SPC_AVE;
    F_DecodeSpectrumLimits(
        nRegisterValue := _refOutput.WP_SPECTRUM_LIMIT,
        nStartRatio    => nStart,
        nStopRatio     => nStop
    );
    GetDeviceSettings.SpectrumStart := nStart;
    GetDeviceSettings.SpectrumStop := nStop;
    GetDeviceSettings.QualityThresholdPercentage := TO_REAL(_refOutput.WP_QUALITY_THR) / DEVICE_SCALE_FACTOR;
    GetDeviceSettings.RoughMode := _refOutput.WP_ROUGH_MODE;
    GetDeviceSettings.Refract := _refOutput.WP_REFRACT;
    GetDeviceSettings.LayerIdM := _refOutput.WP_LAYER_ID_M;
    GetDeviceSettings.RefractM := _refOutput.WP_REFRACT_M;
END_METHOD


METHOD PUBLIC GetMeasurement : ST_MeasurementData
    IF NOT _bInitialized THEN
        RETURN;
    END_IF

    GetMeasurement.MeasNumber := _refInput.MeasNumber;
    GetMeasurement.Peak1      := F_PeakRawToReal(_refInput.Peak1, PEAK_RAW_TO_WAVELENGTH);
    GetMeasurement.Quality1   := _refInput.Quality1;
    GetMeasurement.Peak2      := F_PeakRawToReal(_refInput.Peak2, PEAK_RAW_TO_WAVELENGTH);
    GetMeasurement.Quality2   := _refInput.Quality2;
    GetMeasurement.Peak3      := F_PeakRawToReal(_refInput.Peak3, PEAK_RAW_TO_WAVELENGTH);
    GetMeasurement.Quality3   := _refInput.Quality3;
    GetMeasurement.Intensity  := _refInput.Intensity;
    GetMeasurement.Flags      := _refInput.Flags;
    GetMeasurement.Layer1     := _refInput.Layer1;
    GetMeasurement.Layer2     := _refInput.Layer2;
    GetMeasurement.AckErr     := _refInput.AckErr;
END_METHOD


METHOD PUBLIC SetFrequency : BOOL
VAR_INPUT
    ScanRate : UDINT;
END_VAR
    IF NOT _bInitialized THEN
        RETURN;
    END_IF

    IF ScanRate > SCAN_RATE_MAX THEN
        RETURN;
    END_IF
    _refOutput.WP_SCAN_RATE := ScanRate;
    _cmdQueue.Enqueue(nValue := ScanRate, eBitIndex := E_CommandBit.FREQUENCY);
    SetFrequency := TRUE;
END_METHOD


METHOD PUBLIC SetLightIntensity : BOOL
VAR_INPUT
    TargetLight : REAL;
    AutoAdjust  : BOOL;
END_VAR
VAR
    nEncoded : UDINT;
END_VAR
    IF NOT _bInitialized THEN
        RETURN;
    END_IF

    IF TargetLight < 0.0 OR TargetLight > 100.0 THEN
        SetLightIntensity := FALSE;
        RETURN;
    END_IF

    nEncoded := F_EncodeLightIntensity(fPercentage := TargetLight, bAutoAdjust := AutoAdjust);
    _refOutput.WP_LAI_TARGET := nEncoded;
    _cmdQueue.Enqueue(nValue := nEncoded, eBitIndex := E_CommandBit.LIGHT);
    SetLightIntensity := TRUE;
END_METHOD


METHOD PUBLIC SetAverageSample : BOOL
VAR_INPUT
    AvgSamples : UDINT;
END_VAR
    IF NOT _bInitialized THEN
        RETURN;
    END_IF

    IF AvgSamples < 1 THEN
        SetAverageSample := FALSE;
        RETURN;
    END_IF

    _refOutput.WP_SPC_AVE := AvgSamples;
    _cmdQueue.Enqueue(nValue := AvgSamples, eBitIndex := E_CommandBit.AVERAGE);
    SetAverageSample := TRUE;
END_METHOD


METHOD PUBLIC SetSpectrumLimits : BOOL
VAR_INPUT
    nStartRatio  : UDINT;
    nStopRatio   : UDINT;
    nTotalPixels : UDINT;
END_VAR
VAR
    nEncoded : UDINT;
END_VAR
    IF NOT _bInitialized THEN
        RETURN;
    END_IF

    IF nStartRatio >= nStopRatio THEN
        SetSpectrumLimits := FALSE;
        RETURN;
    END_IF

    IF nStopRatio > RATIO_MAX THEN
        SetSpectrumLimits := FALSE;
        RETURN;
    END_IF

    nEncoded := F_EncodeSpectrumLimits(
        nStartRatio  := nStartRatio,
        nStopRatio   := nStopRatio,
        nTotalPixels := nTotalPixels
    );
    _refOutput.WP_SPECTRUM_LIMIT := nEncoded;
    _cmdQueue.Enqueue(nValue := nEncoded, eBitIndex := E_CommandBit.SPECTRUM);
    SetSpectrumLimits := TRUE;
END_METHOD


METHOD PUBLIC SetQualityTreshold : BOOL
VAR_INPUT
    TargetQuality : REAL;
END_VAR
VAR
    nEncoded : UDINT;
END_VAR
    IF NOT _bInitialized THEN
        RETURN;
    END_IF

    IF TargetQuality < 0.0 OR TargetQuality > 100.0 THEN
        SetQualityTreshold := FALSE;
        RETURN;
    END_IF

    nEncoded := TO_UDINT(TargetQuality * DEVICE_SCALE_FACTOR);
    _refOutput.WP_QUALITY_THR := nEncoded;
    _cmdQueue.Enqueue(nValue := nEncoded, eBitIndex := E_CommandBit.QUALITY);
    SetQualityTreshold := TRUE;
END_METHOD


PROPERTY PUBLIC IsCommandPending : BOOL
GET
    IsCommandPending := NOT _cmdQueue.IsEmpty;
END_GET
END_PROPERTY


PROPERTY PUBLIC QueueCount : INT
GET
    QueueCount := _cmdQueue.Count;
END_GET
END_PROPERTY

END_FUNCTION_BLOCK
