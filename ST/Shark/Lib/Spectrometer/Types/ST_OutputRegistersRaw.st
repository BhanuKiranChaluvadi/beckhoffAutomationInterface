{attribute 'pack_mode' := '0'}
TYPE ST_OutputRegistersRaw :
STRUCT
    WP_SCAN_RATE      : UDINT;
    WP_LAI_TARGET     : UDINT;
    WP_SPC_AVE        : UDINT;
    WP_SPECTRUM_LIMIT : UDINT;
    WP_QUALITY_THR    : UDINT;
    WP_ROUGH_MODE     : UDINT;
    WP_REFRACT        : UDINT;
    WP_LAYER_ID_M     : UDINT;
    WP_REFRACT_M      : UDINT;
    STROBE            : UDINT;
END_STRUCT
END_TYPE
