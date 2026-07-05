(* Generic PID tuning panel — machine-agnostic.

Operates polymorphically on an I_Tunable, which may be a whole heat zone
(all channels autotuned simultaneously) or a single channel. The owning PRG
resolves the HMI selection (zone idx + channel idx + whole-zone flag) into the
correct I_Tunable via the temperature subsystem and injects it via ipTunable.

Zone vs single-channel tuning is the operator's choice; default is whole-zone
(bTuneWholeZone := TRUE), so every channel in the selected zone tunes at once.

Workflow:
  1. HMI selects iSelectedZoneIdx (+ iSelectedChannelIdx, bTuneWholeZone).
  2. HMI sets rTuningSetpoint.
  3. HMI sets bEnableTuning := TRUE  -> panel latches the current target,
     switches it to TUNE mode, applies the setpoint, and enables it.
  4. Operator monitors stStatus until the autotune converges.
  5. HMI sets bEnableTuning := FALSE -> panel disables the target and
     restores ACTIVE mode.

The target is latched at enable time (_ipActive), so changing the selection
while a tune is running cannot orphan the controller being tuned: the latched
target keeps running until the operator disables tuning. *)
FUNCTION_BLOCK FB_BeckhoffTuningPanel
VAR_INPUT
    ipTunable : I_Tunable;   // Resolved by the owning PRG from the selection fields below
END_VAR
VAR
    // --- HMI selection (read by the owning PRG to resolve ipTunable) ---
    iSelectedZoneIdx    : INT := 1;      // 1..N zone index (maps 1:1 to E_HeatZone)
    iSelectedChannelIdx : INT := 1;      // 1..M channel within the zone (single-channel mode)
    bTuneWholeZone      : BOOL := TRUE;  // TRUE = tune every channel in the zone at once

    // --- HMI commands ---
    rTuningSetpoint     : LREAL := 0.0;  // Target temperature for tuning (°C)
    bEnableTuning       : BOOL;          // Level: TRUE = run autotune on the selection
    bCmdApply           : BOOL;          // One-shot: re-apply the setpoint
    bCmdReset           : BOOL;          // One-shot: reset the target controller

    // --- HMI status ---
    stStatus            : ST_ControllerStatus;  // Live status of the active (or preview) target
    bTuningActive       : BOOL;                 // TRUE while a tune is latched and running
    bReady              : BOOL;                 // TRUE when an I_Tunable is injected
    eActiveMode         : E_CTRL_MODE;          // Current mode of the active (or preview) target

    // --- Internal ---
    _ipActive           : I_Tunable;   // Latched target while tuning (prevents orphaning)
    _bLastEnableTuning  : BOOL;
END_VAR

    bReady := (ipTunable <> 0);

    // ----- Rising edge: latch target and enter TUNE -----
    IF bEnableTuning AND NOT _bLastEnableTuning AND bReady THEN
        _ipActive := ipTunable;
        _ipActive.SetControlMode(E_CTRL_MODE.eCTRL_MODE_TUNE);
        _ipActive.SetSetpoint(rTuningSetpoint);
        _ipActive.Enable(TRUE);
        bTuningActive := TRUE;
    END_IF

    // ----- Falling edge: exit TUNE and restore ACTIVE (keep enabled — use manual panel to disable) -----
    IF (NOT bEnableTuning) AND _bLastEnableTuning AND (_ipActive <> 0) THEN
        _ipActive.SetControlMode(E_CTRL_MODE.eCTRL_MODE_ACTIVE);
        _ipActive := 0;
        bTuningActive := FALSE;
    END_IF

    _bLastEnableTuning := bEnableTuning;

    // ----- One-shot commands: act on the latched target while tuning -----
    IF bTuningActive AND (_ipActive <> 0) THEN
        IF bCmdApply THEN
            _ipActive.SetSetpoint(rTuningSetpoint);
            bCmdApply := FALSE;
        END_IF
        IF bCmdReset THEN
            _ipActive.Reset();
            bCmdReset := FALSE;
        END_IF
    ELSE
        // Not tuning — clear one-shots so they cannot fire on the next enable
        bCmdApply := FALSE;
        bCmdReset := FALSE;
    END_IF

    // ----- Status: latched target while tuning, else live preview of the selection -----
    IF bTuningActive AND (_ipActive <> 0) THEN
        stStatus    := _ipActive.GetStatus();
        eActiveMode := _ipActive.GetControlMode();
    ELSIF bReady THEN
        stStatus    := ipTunable.GetStatus();
        eActiveMode := ipTunable.GetControlMode();
    END_IF

END_FUNCTION_BLOCK
