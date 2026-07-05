{attribute 'pack_mode' := '0'}
TYPE ST_SpectrometerTelemetry :
STRUCT
    bConnected    : BOOL;
    bScanning     : BOOL;
    fPeakWavelength : REAL;
    fPeakIntensity  : REAL;
    nScanCount      : UDINT;
END_STRUCT
END_TYPE
