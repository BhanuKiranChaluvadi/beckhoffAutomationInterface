{attribute 'pack_mode' := '0'}
TYPE ST_DeviceSettings :
STRUCT
    Command                    : INT;
    Index                      : DINT;
    ScanRate                   : UDINT;
    LightTargetPercentage      : REAL;
    IntensityAutoAdjust        : BOOL;
    SpectrumAverage            : UDINT;
    SpectrumStart              : UDINT;
    SpectrumStop               : UDINT;
    QualityThresholdPercentage : REAL;
    RoughMode                  : UDINT;
    Refract                    : UDINT;
    LayerIdM                   : UDINT;
    RefractM                   : UDINT;
END_STRUCT
END_TYPE
