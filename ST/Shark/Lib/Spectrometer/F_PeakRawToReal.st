// Converts raw peak count from spectrometer register to wavelength in nm
FUNCTION F_PeakRawToReal : REAL
VAR_INPUT
    nRawPeak          : UDINT;
    fConversionFactor : LREAL := 65536000.0;
END_VAR
    F_PeakRawToReal := TO_REAL(TO_LREAL(nRawPeak) / fConversionFactor);
END_FUNCTION
