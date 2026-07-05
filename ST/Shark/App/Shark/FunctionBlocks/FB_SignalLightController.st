(*
FB_SignalLightController — Drives the three-color signal tower based on machine state.

Owned by PRG_SHARK; initialized once in section 8, called every scan in section 6b.
Signal lights are exclusively controlled by this FB — they are NOT manually settable
via FB_CommandReceiver.

State priority (highest first):
  ERROR  → RED   on, YELLOW off, GREEN off  (bError = TRUE)
  BUSY   → YELLOW on, GREEN  off, RED   off  (bBusy = TRUE, bError = FALSE)
  IDLE   → GREEN  on, YELLOW off, RED   off  (bBusy = FALSE, bError = FALSE)
*)
FUNCTION_BLOCK FB_SignalLightController
VAR
    _ipGreen  : I_DigitalOutput;
    _ipYellow : I_DigitalOutput;
    _ipRed          : I_DigitalOutput;
    _bInitialized   : BOOL := FALSE;
END_VAR


METHOD PUBLIC Init : BOOL
VAR_INPUT
    ipGreen  : I_DigitalOutput;
    ipYellow : I_DigitalOutput;
    ipRed    : I_DigitalOutput;
END_VAR
    IF ipGreen = 0 OR ipYellow = 0 OR ipRed = 0 THEN RETURN; END_IF
    _ipGreen  := ipGreen;
    _ipYellow := ipYellow;
    _ipRed    := ipRed;
    _bInitialized := TRUE;
    Init          := TRUE;
END_METHOD


METHOD PUBLIC Execute
VAR_INPUT
    stStatus : ST_SequenceStatus;
END_VAR
    IF NOT _bInitialized THEN RETURN; END_IF

    IF stStatus.bError THEN
        _ipRed.SetOutput(TRUE);
        _ipYellow.SetOutput(FALSE);
        _ipGreen.SetOutput(FALSE);
    ELSIF stStatus.bBusy THEN
        _ipYellow.SetOutput(TRUE);
        _ipGreen.SetOutput(FALSE);
        _ipRed.SetOutput(FALSE);
    ELSE
        _ipGreen.SetOutput(TRUE);
        _ipYellow.SetOutput(FALSE);
        _ipRed.SetOutput(FALSE);
    END_IF
END_METHOD

END_FUNCTION_BLOCK
