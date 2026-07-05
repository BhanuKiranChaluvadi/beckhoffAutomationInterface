{attribute 'pack_mode' := '0'}
TYPE ST_InputRegistersRaw :
STRUCT
    MeasNumber : UDINT;
    Peak1      : UDINT;
    Quality1   : UDINT;
    Peak2      : UDINT;
    Quality2   : UDINT;
    Peak3      : UDINT;
    Quality3   : UDINT;
    Intensity  : UDINT;
    Flags      : UDINT;
    Layer1     : UDINT;
    Layer2     : UDINT;
    AckErr     : UDINT;
END_STRUCT
END_TYPE
