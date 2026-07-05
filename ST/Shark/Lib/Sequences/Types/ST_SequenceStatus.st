TYPE ST_SequenceStatus :
STRUCT
    eActiveSequence     : E_Sequence;       // Currently active sequence
    bBusy               : BOOL;             // Sequence is running
    bDone               : BOOL;             // Sequence completed successfully
    bError              : BOOL;             // Sequence failed
    rProgress           : REAL;             // Progress percentage 0.0 to 100.0
    sStatusMessage      : STRING(255);      // Human-readable status
    stLastError         : ST_Error;         // Populated when bError = TRUE
    nLastPurgedBubblerA : INT;              // Last bubbler purged on A-side (precursor)
    nLastPurgedBubblerB : INT;              // Last bubbler purged on B-side (reactant)
END_STRUCT
END_TYPE
