(* Standard error information structure. *)
TYPE ST_Error :
STRUCT
    eErrorId         : E_ErrorId          := E_ErrorId.NO_ERROR;
    eSeverity        : E_TcEventSeverity  := E_TcEventSeverity.Error;
    sSource          : STRING(80);        // Component that currently owns the error
    sMessage         : STRING(255);       // Description (may include chained child sources)
    fCurrentValue    : REAL;              // Optional measured value (RaiseWithValues), exported in JSON
    fExpectedValue   : REAL;              // Optional limit/setpoint (RaiseWithValues), exported in JSON
    sUnits           : STRING(15);        // Optional units for the values (e.g. 'C', 'bar')
    tTimestamp       : DT;                // Time most recent propagation/raise occurred
    tOriginTimestamp : DT;                // Time the root cause first occurred (preserved across RaiseFromChild)
END_STRUCT
END_TYPE
