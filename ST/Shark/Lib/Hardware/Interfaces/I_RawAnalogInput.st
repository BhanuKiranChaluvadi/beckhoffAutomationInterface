(* Abstracts the raw hardware input for analog sensors.

   nRawValue is DINT so a single interface covers both 16-bit terminals
   (EL32xx RTD / EL31xx, INT sign-extended to DINT) and 32-bit terminals
   (EL3314 thermocouple, native DINT). DINT is a lossless superset of INT. *)
INTERFACE I_RawAnalogInput
METHOD GetChannelState
VAR_OUTPUT
    nRawValue   : DINT;
    bError      : BOOL;
    bUnderrange : BOOL;
    bOverrange  : BOOL;
END_VAR
END_METHOD
END_INTERFACE
