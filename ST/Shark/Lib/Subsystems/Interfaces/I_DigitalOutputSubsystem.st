INTERFACE I_DigitalOutputSubsystem

METHOD IsInitialized : BOOL
END_METHOD

METHOD GetOutputByIndex : I_DigitalOutput
VAR_INPUT
    nOutput : INT;
END_VAR
END_METHOD

METHOD GetStateByIndex : BOOL
VAR_INPUT
    nOutput : INT;
END_VAR
END_METHOD

METHOD GetCount : INT
END_METHOD

END_INTERFACE
