// Converts E_ScanRate enum index to frequency in Hz
FUNCTION F_ScanRateToHz : UDINT
VAR_INPUT
    nScanRate : UDINT;
END_VAR
    CASE nScanRate OF
        0: F_ScanRateToHz := 32;
        1: F_ScanRateToHz := 100;
        2: F_ScanRateToHz := 320;
        3: F_ScanRateToHz := 1000;
        4: F_ScanRateToHz := 2000;
    ELSE
        F_ScanRateToHz := 0;
    END_CASE
END_FUNCTION
