(*
EL3314 Thermocouple Channel Adapter for Beckhoff EL3314-xxxx terminals.

Beckhoff auto-generates types with GUID suffixes (e.g., MDP5001_330_88CAB57D) that
change on hardware updates. This adapter uses fixed memory offsets to read channel
data, isolating type dependencies to variable declarations only.

Unlike FB_AnalogInputAdapter (16-bit INT value at offset 2), the EL3314 channel
PDO carries a 32-bit DINT value preceded by a reserved word:

Memory layout (EL3314 channel, MDP5001_330_C_I_D7C99110):
Offset 0-1: Status WORD (Underrange=0, Overrange=1, Error=6)
Offset 2-3: _reserved UINT
Offset 4-7: Value DINT

Usage:
_fbAdapter.Link(pChannel := ADR(_stTerminal.In.TC_Inputs_Channel_1));
_fbAdapter.Execute();  // Call each cycle
*)
FUNCTION_BLOCK FB_TcInputAdapter IMPLEMENTS I_RawAnalogInput
VAR
    _pChannelData : POINTER TO BYTE;    // Pointer to the hardware channel structure
    _bLinked      : BOOL := FALSE;

    // Cached values (updated in Execute)
    _nRawValue    : DINT;               // 32-bit thermocouple counts
    _bError       : BOOL;
    _bUnderrange  : BOOL;
    _bOverrange   : BOOL;
END_VAR
VAR CONSTANT
    // Memory offsets for EL3314 channel structure
    OFFSET_STATUS : UINT := 0;          // Status WORD at offset 0
    OFFSET_VALUE  : UINT := 4;          // Value DINT at offset 4 (after status + reserved word)

    // Status bit positions (within the 16-bit status word)
    BIT_UNDERRANGE : BYTE := 0;
    BIT_OVERRANGE  : BYTE := 1;
    BIT_ERROR      : BYTE := 6;
END_VAR

    (* Links adapter to the thermocouple channel input structure.
    Pass ADR of the channel member (e.g., ADR(stTerminal.In.TC_Inputs_Channel_1)) *)
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
    pValue  : POINTER TO DINT;
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

    // Read 32-bit value from fixed offset
    pValue := _pChannelData + OFFSET_VALUE;
    _nRawValue := pValue^;

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
