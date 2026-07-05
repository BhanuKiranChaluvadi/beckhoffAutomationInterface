{attribute 'pack_mode' := '0'}
TYPE ST_MeasurementData :
STRUCT
    MeasNumber : UDINT;
    Peak1      : REAL;
    Quality1   : UDINT;
    Peak2      : REAL;
    Quality2   : UDINT;
    Peak3      : REAL;
    Quality3   : UDINT;
    Intensity  : UDINT;
    Flags      : UDINT;
    Layer1     : UDINT;
    Layer2     : UDINT;
    AckErr     : UDINT;
END_STRUCT
END_TYPE
