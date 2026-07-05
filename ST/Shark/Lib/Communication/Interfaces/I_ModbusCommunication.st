(* Modbus communication interface. *)
INTERFACE I_ModbusCommunication

METHOD StartReadOperation : BOOL
VAR_INPUT
    nStartRegister : WORD;
    nQuantity : WORD;
END_VAR
END_METHOD

METHOD StartWriteOperation : BOOL
VAR_INPUT
    nStartRegister : WORD;
    nQuantity : WORD;
    pData : POINTER TO BYTE;
END_VAR
END_METHOD

METHOD Update : BOOL
END_METHOD

METHOD IsOperationComplete : BOOL
END_METHOD

METHOD GetOperationResult : BOOL
VAR_INPUT
    wErrorCode : REFERENCE TO WORD;
END_VAR
END_METHOD

METHOD GetReadData : BOOL
VAR_INPUT
    pData : REFERENCE TO POINTER TO BYTE;
    nDataLength : REFERENCE TO UINT;
END_VAR
END_METHOD

END_INTERFACE
