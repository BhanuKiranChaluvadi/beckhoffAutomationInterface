(*
FB_ChillerManager — Controls chiller power and water valves.
Migrated from old FB_ChillerManager. Uses I_DigitalOutput from DO subsystem.
3 outputs: ChillerPower, ChuckWaterIn valve, ChuckWaterOut valve.
*)
FUNCTION_BLOCK FB_ChillerManager
VAR
    _ipChillerPower   : I_DigitalOutput;
    _ipWaterInValve   : I_DigitalOutput;
    _ipWaterOutValve  : I_DigitalOutput;
    _bInitialized     : BOOL := FALSE;
END_VAR


METHOD PUBLIC Init : BOOL
VAR_INPUT
    ipChillerPower  : I_DigitalOutput;
    ipWaterInValve  : I_DigitalOutput;
    ipWaterOutValve : I_DigitalOutput;
END_VAR
    IF ipChillerPower = 0 OR ipWaterInValve = 0 OR ipWaterOutValve = 0 THEN
        GVL_ErrorLogger.Logger.LogError(
            sSource  := 'ChillerManager.Init',
            eErrorId := E_ErrorId.ERR_GEN_NULL_POINTER,
            sContext := 'null interface pointer'
        );
        RETURN;
    END_IF
    _ipChillerPower := ipChillerPower;
    _ipWaterInValve := ipWaterInValve;
    _ipWaterOutValve := ipWaterOutValve;
    _bInitialized := TRUE;
    Init := TRUE;
END_METHOD


METHOD PUBLIC SetChillerPower
VAR_INPUT
    bOn : BOOL;
END_VAR
    _SetOutput(ipOutput := _ipChillerPower, bValue := bOn);
END_METHOD


METHOD PUBLIC SetWaterInValve
VAR_INPUT
    bOpen : BOOL;
END_VAR
    _SetOutput(ipOutput := _ipWaterInValve, bValue := bOpen);
END_METHOD


METHOD PUBLIC SetWaterOutValve
VAR_INPUT
    bOpen : BOOL;
END_VAR
    _SetOutput(ipOutput := _ipWaterOutValve, bValue := bOpen);
END_METHOD


METHOD PRIVATE _SetOutput
VAR_INPUT
    ipOutput : I_DigitalOutput;
    bValue   : BOOL;
END_VAR
    IF NOT _bInitialized THEN RETURN; END_IF
    ipOutput.SetOutput(bValue);
END_METHOD


PROPERTY PUBLIC IsChillerOn : BOOL
    GET
        IF _bInitialized AND_THEN _ipChillerPower <> 0 THEN
            IsChillerOn := _ipChillerPower.GetOutput();
        END_IF
    END_GET
END_PROPERTY

END_FUNCTION_BLOCK
