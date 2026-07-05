(*
Generic Analog Channel Adapter for Beckhoff EL3xxx terminals.

Beckhoff auto-generates types with GUID suffixes (e.g., MDP5001_320_5D7E181C) that
change on hardware updates. This adapter uses fixed memory offsets to read channel
data, isolating type dependencies to variable declarations only.

Supported terminals: EL32xx (RTD), EL31xx (voltage/current), EL3174 (pressure)

Memory layout (EL3xxx standard):
Offset 0-1: Status WORD (Underrange=0, Overrange=1, Error=6)
Offset 2-3: Value INT

Usage:
_fbAdapter.Link(pChannel := ADR(_stChannel.MDP5001_xxx_Input));
_fbAdapter.Execute();  // Call each cycle
*)
FUNCTION_BLOCK FB_AnalogInputAdapter IMPLEMENTS I_RawAnalogInput
VAR
    _pChannelData : POINTER TO BYTE;    // Pointer to the hardware channel structure
    _bLinked      : BOOL := FALSE;

    // Cached values (updated in Execute)
    _nRawValue    : DINT;               // 16-bit hardware INT sign-extended to DINT
    _bError       : BOOL;
    _bUnderrange  : BOOL;
    _bOverrange   : BOOL;
END_VAR
VAR CONSTANT
    // Memory offsets for analog channel structure (EL3xxx standard layout)
    OFFSET_STATUS : UINT := 0;          // Status WORD at offset 0
    OFFSET_VALUE  : UINT := 2;          // Value INT at offset 2 (after 2-byte status)

    // Status bit positions (within the 16-bit status word)
    BIT_UNDERRANGE : BYTE := 0;
    BIT_OVERRANGE  : BYTE := 1;
    BIT_ERROR      : BYTE := 6;
END_VAR

    (* Links adapter to the analog channel input structure.
    Pass ADR of the Input member (e.g., ADR(stChannel.MDP5001_xxx_Input)) *)
METHOD PUBLIC Link : BOOL
VAR_INPUT
    pChannel : POINTER TO BYTE;         // Pointer to channel input structure
END_VAR
    IF pChannel = 0 THEN
        _bLinked := FALSE;
        Link := FALSE;
        RETURN;
    END_IF

    _pChannelData := pChannel;
    _bLinked := TRUE;
    Link := TRUE;
END_METHOD

    (* Reads status and value from hardware. Call once per cycle before using GetChannelState. *)
METHOD PUBLIC Execute : BOOL
VAR
    wStatus : WORD;
    pStatus : POINTER TO WORD;
    pValue  : POINTER TO INT;
END_VAR
    IF NOT _bLinked OR _pChannelData = 0 THEN
        _nRawValue := 0;
        _bError := TRUE;
        _bUnderrange := FALSE;
        _bOverrange := FALSE;
        Execute := FALSE;
        RETURN;
    END_IF

    // Read status word from fixed offset
    pStatus := _pChannelData + OFFSET_STATUS;
    wStatus := pStatus^;

    // Extract status bits
    _bUnderrange := (wStatus AND SHL(WORD#1, BIT_UNDERRANGE)) <> 0;
    _bOverrange  := (wStatus AND SHL(WORD#1, BIT_OVERRANGE)) <> 0;
    _bError      := (wStatus AND SHL(WORD#1, BIT_ERROR)) <> 0;

    // Read value from fixed offset (16-bit INT, sign-extended to DINT)
    pValue := _pChannelData + OFFSET_VALUE;
    _nRawValue := INT_TO_DINT(pValue^);

    Execute := TRUE;
END_METHOD

    (* Returns cached channel state. Call Execute() first each cycle. *)
METHOD PUBLIC GetChannelState
VAR_OUTPUT
    nRawValue   : DINT;
    bError      : BOOL;
    bUnderrange : BOOL;
    bOverrange  : BOOL;
END_VAR
    IF _bLinked THEN
        nRawValue   := _nRawValue;
        bError      := _bError;
        bUnderrange := _bUnderrange;
        bOverrange  := _bOverrange;
    ELSE
        nRawValue   := 0;
        bError      := TRUE;
        bUnderrange := FALSE;
        bOverrange  := FALSE;
    END_IF
END_METHOD

    (* Returns TRUE if adapter is linked to hardware. *)
METHOD PUBLIC IsLinked : BOOL
    IsLinked := _bLinked;
END_METHOD

END_FUNCTION_BLOCK
