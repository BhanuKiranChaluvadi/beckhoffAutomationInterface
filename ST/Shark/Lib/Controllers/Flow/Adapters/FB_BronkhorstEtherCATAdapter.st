FUNCTION_BLOCK FB_BronkhorstEtherCATAdapter IMPLEMENTS I_BronkhorstController
VAR
    // Hardware references — UINT matches EtherCAT PDO type (0-32000 raw counts)
    _refMeasuredValue     : REFERENCE TO UINT;
    _refActualSetpoint    : REFERENCE TO UINT;
    _refValveOutput       : REFERENCE TO UDINT;

    _refCommandedSetpoint : REFERENCE TO UINT;
    _refControlByte       : REFERENCE TO USINT;
    _refControlMode       : REFERENCE TO USINT;

    // Engineering range — stored at Link() time for UINT↔REAL conversion
    _rMinValue : LREAL;
    _rMaxValue : LREAL;

    _bLinked : BOOL := FALSE;
END_VAR

// Link hardware IO references and store engineering scale for conversion.
// rMinValue/rMaxValue are the engineering min/max of the device (e.g. 0.0/25.0 sccm).
METHOD PUBLIC Link : BOOL
VAR_INPUT
    refMeasuredValue     : REFERENCE TO UINT;
    refActualSetpoint    : REFERENCE TO UINT;
    refValveOutput       : REFERENCE TO UDINT;
    refCommandedSetpoint : REFERENCE TO UINT;
    refControlByte       : REFERENCE TO USINT;
    refControlMode       : REFERENCE TO USINT;
    rMinValue            : LREAL;
    rMaxValue            : LREAL;
END_VAR
    IF NOT __ISVALIDREF(refMeasuredValue) OR NOT __ISVALIDREF(refActualSetpoint) OR
       NOT __ISVALIDREF(refValveOutput) OR NOT __ISVALIDREF(refCommandedSetpoint) OR
       NOT __ISVALIDREF(refControlByte) OR NOT __ISVALIDREF(refControlMode) THEN
        RETURN;
    END_IF

    _refMeasuredValue     REF= refMeasuredValue;
    _refActualSetpoint    REF= refActualSetpoint;
    _refValveOutput       REF= refValveOutput;
    _refCommandedSetpoint REF= refCommandedSetpoint;
    _refControlByte       REF= refControlByte;
    _refControlMode       REF= refControlMode;
    _rMinValue := rMinValue;
    _rMaxValue := rMaxValue;
    _bLinked := TRUE;
    Link := TRUE;
END_METHOD

// Returns measured value in engineering units (sccm, bar, etc.)
METHOD PUBLIC GetMeasuredValue : REAL
    IF _bLinked THEN
        GetMeasuredValue := LREAL_TO_REAL(
            F_RawCountsToEngineering(_refMeasuredValue, _rMinValue, _rMaxValue, 32000)
        );
    END_IF
END_METHOD

METHOD PUBLIC GetValveOutput : REAL
    IF _bLinked THEN
        // Convert UDINT (0-16777215) to 0-100%
        GetValveOutput := UDINT_TO_REAL(_refValveOutput) / 167772.15;
    END_IF
END_METHOD

// Accepts setpoint in engineering units; converts to raw counts before writing.
METHOD PUBLIC WriteSetpoint
VAR_INPUT
    rSetpoint : REAL;
END_VAR
    IF _bLinked THEN
        _refCommandedSetpoint := WORD_TO_UINT(
            F_EngineeringToRawCounts(REAL_TO_LREAL(rSetpoint), _rMinValue, _rMaxValue, 32000)
        );
    END_IF
END_METHOD

METHOD PUBLIC WriteEnable
VAR_INPUT
    bEnable : BOOL;
END_VAR
    IF _bLinked THEN _refControlByte := BOOL_TO_USINT(bEnable);
    END_IF
END_METHOD

METHOD PUBLIC WriteControlMode
VAR_INPUT
    nMode : USINT;
END_VAR
    IF _bLinked THEN _refControlMode := nMode;
    END_IF
END_METHOD
END_FUNCTION_BLOCK
