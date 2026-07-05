(*
FB_CommandQueue — FIFO queue for spectrometer hardware register writes.
Fixed capacity of 10 commands.
*)
FUNCTION_BLOCK FB_CommandQueue
VAR
    _aBuffer : ARRAY[0..9] OF ST_SpecCommand;
    _nHead   : INT := 0;
    _nTail   : INT := 0;
    _nCount  : INT := 0;
END_VAR
VAR CONSTANT
    CAPACITY : INT := 10;
END_VAR


METHOD PUBLIC Enqueue : BOOL
VAR_INPUT
    nValue   : UDINT;
    eBitIndex : E_CommandBit;
END_VAR
    IF _nCount >= CAPACITY THEN
        GVL_ErrorLogger.Logger.LogError(
            sSource   := 'CommandQueue.Enqueue',
            eErrorId  := E_ErrorId.ERR_COMM_BUFFER_OVERFLOW,
            sContext  := 'queue full',
            eSeverity := E_TcEventSeverity.Warning
        );
        Enqueue := FALSE;
        RETURN;
    END_IF
    _aBuffer[_nTail].Value    := nValue;
    _aBuffer[_nTail].BitIndex := eBitIndex;
    _nTail := (_nTail + 1) MOD CAPACITY;
    _nCount := _nCount + 1;
    Enqueue := TRUE;
END_METHOD


METHOD PUBLIC Dequeue : ST_SpecCommand
    IF _nCount <= 0 THEN
        RETURN;
    END_IF
    Dequeue := _aBuffer[_nHead];
    _nHead := (_nHead + 1) MOD CAPACITY;
    _nCount := _nCount - 1;
END_METHOD


METHOD PUBLIC Peek : ST_SpecCommand
    IF _nCount > 0 THEN
        Peek := _aBuffer[_nHead];
    END_IF
END_METHOD


PROPERTY PUBLIC Count : INT
    GET
        Count := _nCount;
    END_GET
END_PROPERTY


PROPERTY PUBLIC IsEmpty : BOOL
    GET
        IsEmpty := _nCount <= 0;
    END_GET
END_PROPERTY


PROPERTY PUBLIC IsFull : BOOL
    GET
        IsFull := _nCount >= CAPACITY;
    END_GET
END_PROPERTY

END_FUNCTION_BLOCK
