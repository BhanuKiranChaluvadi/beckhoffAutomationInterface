INTERFACE I_Spectrometer

METHOD CalibrateDark : BOOL
VAR_INPUT
    bExecute : BOOL;
END_VAR
END_METHOD

METHOD Execute : BOOL
END_METHOD

METHOD GetDeviceSettings : ST_DeviceSettings
END_METHOD

METHOD GetMeasurement : ST_MeasurementData
END_METHOD

METHOD SetAverageSample : BOOL
VAR_INPUT
    AvgSamples : UDINT;
END_VAR
END_METHOD

METHOD SetFrequency : BOOL
VAR_INPUT
    ScanRate : UDINT;
END_VAR
END_METHOD

METHOD SetLightIntensity : BOOL
VAR_INPUT
    TargetLight : REAL;
    AutoAdjust  : BOOL;
END_VAR
END_METHOD

METHOD SetQualityTreshold : BOOL
VAR_INPUT
    TargetQuality : REAL;
END_VAR
END_METHOD

METHOD SetSpectrumLimits : BOOL
VAR_INPUT
    nStartRatio  : UDINT;
    nStopRatio   : UDINT;
    nTotalPixels : UDINT;
END_VAR
END_METHOD

END_INTERFACE
