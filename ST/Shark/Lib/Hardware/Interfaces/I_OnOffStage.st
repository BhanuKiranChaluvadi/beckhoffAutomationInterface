(* Generic on/off stage interface.

A device that can be enabled or disabled with no level/intensity control.
Examples: a digitally-switched ozone generator, a UV lamp, a heater fan
running at fixed speed.

Combine with I_LevelStage on the same FB if the device also accepts an
analog trim setpoint (see FB_OnOffLevelStage). *)
INTERFACE I_OnOffStage EXTENDS __System.IQueryInterface

METHOD Enable : BOOL
VAR_INPUT
    bEnable : BOOL;
END_VAR
END_METHOD

METHOD IsEnabled : BOOL
END_METHOD

METHOD Execute : BOOL
END_METHOD

END_INTERFACE
