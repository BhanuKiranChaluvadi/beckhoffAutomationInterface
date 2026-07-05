(* Adjustable level (0..100 %) stage interface.

Typically combined with I_OnOffStage on the same FB: the on/off interface
gates power; the level interface sets the analog trim. A device that only
supports on/off does NOT implement I_LevelStage, so callers cannot
accidentally request a setpoint that has no effect. *)
INTERFACE I_LevelStage

METHOD SetLevel : BOOL
VAR_INPUT
    rPercentage : REAL;
END_VAR
END_METHOD

METHOD GetLevel : REAL
END_METHOD

END_INTERFACE
