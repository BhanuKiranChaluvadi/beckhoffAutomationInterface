(* Simple on/off device backed by a single digital output.

Use for any actuator that has no analog trim: digital ozone generator,
UV lamp, fan, solenoid-driven valve cluster, etc. The Enable() command
is latched; Execute() drives the digital output each scan. *)
FUNCTION_BLOCK FB_OnOffStage IMPLEMENTS I_OnOffStage
VAR_OUTPUT
    bInitialized : BOOL := FALSE;
END_VAR
VAR
    _sInstanceName : STRING(80);
    _ipPowerOutput : I_DigitalOutput;
    _bEnabled      : BOOL := FALSE;
END_VAR

METHOD PUBLIC Init : BOOL
VAR_INPUT
    sInstanceName : STRING(80);
    ipPowerOutput : I_DigitalOutput;
END_VAR
    IF bInitialized THEN
        Init := TRUE;
        RETURN;
    END_IF

    IF ipPowerOutput = 0 THEN
        Init := FALSE;
        RETURN;
    END_IF

    _sInstanceName := sInstanceName;
    _ipPowerOutput := ipPowerOutput;
    _bEnabled := FALSE;

    bInitialized := TRUE;
    Init := TRUE;
END_METHOD

METHOD PUBLIC Execute : BOOL
    IF NOT bInitialized THEN
        Execute := FALSE;
        RETURN;
    END_IF

    IF _ipPowerOutput <> 0 THEN
        _ipPowerOutput.SetOutput(_bEnabled);
    END_IF

    Execute := TRUE;
END_METHOD

METHOD PUBLIC Enable : BOOL
VAR_INPUT
    bEnable : BOOL;
END_VAR
    _bEnabled := bEnable;
    Enable := TRUE;
END_METHOD

METHOD PUBLIC IsEnabled : BOOL
    IsEnabled := _bEnabled;
END_METHOD

METHOD PUBLIC GetInstanceName : STRING(80)
    GetInstanceName := _sInstanceName;
END_METHOD

END_FUNCTION_BLOCK
