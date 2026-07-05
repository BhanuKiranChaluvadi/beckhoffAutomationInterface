(* On/off device with an analog level (trim) setpoint.

Two outputs:
  - power output (digital)   -> drives Enable()
  - level output (analog %)  -> drives SetLevel(), clamped to [rMinLevel..rMaxLevel]

When disabled, the level output is forced to 0 % regardless of the last
SetLevel value. Limits are supplied at Init from the calling App's GVL
(this FB never references any GVL).

Implements both I_OnOffStage and I_LevelStage so callers can hold the
device by either interface depending on capability needs. *)
FUNCTION_BLOCK FB_OnOffLevelStage IMPLEMENTS I_OnOffStage, I_LevelStage
VAR_OUTPUT
    bInitialized : BOOL := FALSE;
END_VAR
VAR
    _sInstanceName : STRING(80);
    _ipPowerOutput : I_DigitalOutput;
    _ipLevelOutput : I_AnalogOutput;
    _bEnabled      : BOOL := FALSE;
    _rLevel        : REAL := 0.0;
    _rMinLevel     : REAL := 0.0;
    _rMaxLevel     : REAL := 100.0;
END_VAR

METHOD PUBLIC Init : BOOL
VAR_INPUT
    sInstanceName : STRING(80);
    ipPowerOutput : I_DigitalOutput;
    ipLevelOutput : I_AnalogOutput;
    rMinLevel     : REAL;
    rMaxLevel     : REAL;
END_VAR
    IF bInitialized THEN
        Init := TRUE;
        RETURN;
    END_IF

    IF ipPowerOutput = 0 OR ipLevelOutput = 0 THEN
        Init := FALSE;
        RETURN;
    END_IF

    IF rMinLevel >= rMaxLevel THEN
        Init := FALSE;
        RETURN;
    END_IF

    _sInstanceName := sInstanceName;
    _ipPowerOutput := ipPowerOutput;
    _ipLevelOutput := ipLevelOutput;
    _rMinLevel     := rMinLevel;
    _rMaxLevel     := rMaxLevel;
    _rLevel        := rMinLevel;
    _bEnabled      := FALSE;

    bInitialized := TRUE;
    Init := TRUE;
END_METHOD

METHOD PUBLIC Execute : BOOL
    IF NOT bInitialized THEN
        Execute := FALSE;
        RETURN;
    END_IF

    // Power output mirrors the latched Enable command
    IF _ipPowerOutput <> 0 THEN
        _ipPowerOutput.SetOutput(_bEnabled);
    END_IF

    // Level output: commanded value when enabled, 0 % when disabled
    IF _ipLevelOutput <> 0 THEN
        IF _bEnabled THEN
            _ipLevelOutput.SetPercentage(_rLevel);
        ELSE
            _ipLevelOutput.SetPercentage(0.0);
        END_IF
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

METHOD PUBLIC SetLevel : BOOL
VAR_INPUT
    rPercentage : REAL;
END_VAR
    _rLevel := LIMIT(_rMinLevel, rPercentage, _rMaxLevel);
    SetLevel := TRUE;
END_METHOD

METHOD PUBLIC GetLevel : REAL
    GetLevel := _rLevel;
END_METHOD

METHOD PUBLIC GetInstanceName : STRING(80)
    GetInstanceName := _sInstanceName;
END_METHOD

END_FUNCTION_BLOCK
