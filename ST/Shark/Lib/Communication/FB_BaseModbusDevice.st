(* Base Modbus device handler. Provides FSM for Modbus read/write operations. *)
FUNCTION_BLOCK ABSTRACT FB_BaseModbusDevice IMPLEMENTS I_ModbusSlave
VAR
    _eFSMState 					: E_ModbusDeviceState := E_ModbusDeviceState.IDLE;
    _nUnitId					: BYTE := 0;
    _ipModbusComm				: I_ModbusCommunication := 0;
    _sName						: STRING(80) := 'FB_BaseModbusDevice';
    _fbError					: FB_ErrorHandler;
END_VAR

METHOD PROTECTED SetName
VAR_INPUT
    sName : STRING(80);
END_VAR
    _sName := sName;
    _fbError.Init(sName);
END_METHOD

METHOD PROTECTED RaiseError
VAR_INPUT
    eErrorId : E_ErrorId;
    sMessage : STRING(255);
END_VAR
    _fbError.Raise(eErrorId, sMessage);
END_METHOD

METHOD PROTECTED ClearError
    _fbError.Clear();
END_METHOD

METHOD PUBLIC HandleRequest : BOOL
    IF _eFSMState <> E_ModbusDeviceState.IDLE THEN
        HandleRequest := FALSE;
        RETURN;
    END_IF;

    // NOTE: Do NOT pre-clear the error here. The previous cycle's error stays
    // visible to callers until the next operation actually succeeds, matching
    // the self-healing pattern used by sensors and other controllers.

    // Template-method hook: let the concrete device latch a snapshot of its
    // pending write commands at the start of the transaction. This bounds the
    // write-drain loop even when an upstream controller re-asserts write
    // requests every scan (otherwise the FSM never reaches START_READ/IDLE).
    OnBeginTransaction();

    _eFSMState := E_ModbusDeviceState.START_WRITE;
    HandleRequest := TRUE;
END_METHOD

METHOD PUBLIC Reset : BOOL
    ClearError();
    _eFSMState := E_ModbusDeviceState.IDLE;
    Reset := TRUE;
END_METHOD

METHOD PUBLIC Update : BOOL
    IF _ipModbusComm = 0 THEN
        _eFSMState := E_ModbusDeviceState.IDLE;
        Update := FALSE;
        RETURN;
    END_IF;

    _ipModbusComm.Update();

    CASE _eFSMState OF
        E_ModbusDeviceState.IDLE:
        Update := TRUE;
        RETURN;

        E_ModbusDeviceState.START_WRITE:
        _HandleStartWrite();

        E_ModbusDeviceState.START_READ:
        _HandleStartRead();

        E_ModbusDeviceState.WAIT_FOR_WRITE:
        _HandleWaitForWrite();

        E_ModbusDeviceState.WAIT_FOR_READ:
        _HandleWaitForRead();
    END_CASE;

    Update := TRUE;
END_METHOD

METHOD PRIVATE _HandleStartWrite
VAR
    nWriteStartRegister, nWriteQuantity : WORD;
    pWriteData : POINTER TO BYTE;
END_VAR
    IF GetWritePayload(nStartingRegister => nWriteStartRegister, nQuantity => nWriteQuantity, pData => pWriteData) THEN
        IF _ipModbusComm.StartWriteOperation(nStartRegister := nWriteStartRegister, nQuantity := nWriteQuantity, pData := pWriteData) THEN
            _eFSMState := E_ModbusDeviceState.WAIT_FOR_WRITE;
        END_IF;
    ELSE
        _eFSMState := E_ModbusDeviceState.START_READ;
    END_IF;
END_METHOD

METHOD PRIVATE _HandleStartRead
VAR
    nReadStartRegister, nReadQuantity : WORD;
END_VAR
    IF GetReadPayload(nStartingRegister => nReadStartRegister, nQuantity => nReadQuantity) THEN
        IF _ipModbusComm.StartReadOperation(nStartRegister := nReadStartRegister, nQuantity := nReadQuantity) THEN
            _eFSMState := E_ModbusDeviceState.WAIT_FOR_READ;
        END_IF;
    ELSE
        _eFSMState := E_ModbusDeviceState.IDLE;
    END_IF;
END_METHOD

METHOD PRIVATE _HandleWaitForWrite
VAR
    bSuccess : BOOL;
    wErrorCode : WORD;
END_VAR
    IF NOT _ipModbusComm.IsOperationComplete() THEN
        RETURN;
    END_IF;

    bSuccess := _ipModbusComm.GetOperationResult(wErrorCode);

    IF bSuccess THEN
        ClearError();
        HandleSuccessfulWrite();
    ELSE
        _HandleFailedOperation(wErrorCode);
    END_IF;

    _eFSMState := E_ModbusDeviceState.START_WRITE;
END_METHOD

METHOD PRIVATE _HandleWaitForRead
VAR
    bSuccess : BOOL;
    wErrorCode : WORD;
    pData : POINTER TO BYTE;
    nDataLength : UINT;
END_VAR
    IF NOT _ipModbusComm.IsOperationComplete() THEN
        RETURN;
    END_IF;

    bSuccess := _ipModbusComm.GetOperationResult(wErrorCode);

    IF bSuccess THEN
        ClearError();
        _ipModbusComm.GetReadData(pData, nDataLength);
        HandleSuccessfulRead(pData, nDataLength);
    ELSE
        _HandleFailedOperation(wErrorCode);
    END_IF;

    _eFSMState := E_ModbusDeviceState.IDLE;
END_METHOD

METHOD PRIVATE _HandleFailedOperation
VAR_INPUT
    wErrorCode : WORD;
END_VAR
    RaiseError(E_ErrorId.ERR_COMM_TIMEOUT, CONCAT('Modbus operation failed, code: ', WORD_TO_STRING(wErrorCode)));
END_METHOD

METHOD PUBLIC IsBusy : BOOL
    IsBusy := _eFSMState <> E_ModbusDeviceState.IDLE;
END_METHOD

METHOD PROTECTED _Init : BOOL
VAR_INPUT
    nUnitId						: BYTE;
    ipModbusComm				: I_ModbusCommunication;
END_VAR
    IF ipModbusComm = 0 THEN
        RaiseError(E_ErrorId.ERR_GEN_NULL_POINTER, 'Communication handler is null');
        RETURN;
    END_IF

    _nUnitId := nUnitId;
    _ipModbusComm := ipModbusComm;
    _Init := TRUE;
END_METHOD

METHOD PUBLIC GetUnitId : BYTE
    GetUnitId := _nUnitId;
END_METHOD

METHOD PROTECTED ABSTRACT HandleSuccessfulRead
VAR_INPUT
    pData						: POINTER TO BYTE;
    nDataLength					: UINT;
END_VAR
END_METHOD

METHOD PROTECTED ABSTRACT GetReadPayload : BOOL
VAR_OUTPUT
    nStartingRegister			: WORD;
    nQuantity					: WORD;
END_VAR
END_METHOD

METHOD PROTECTED ABSTRACT GetWritePayload : BOOL
VAR_OUTPUT
    nStartingRegister			: WORD;
    nQuantity					: WORD;
    pData						: POINTER TO BYTE;
END_VAR
END_METHOD

METHOD PROTECTED ABSTRACT HandleSuccessfulWrite
END_METHOD

(* Template-method hook called once at the start of each round-robin transaction
   (IDLE -> START_WRITE). The default implementation is a no-op. Devices whose
   write commands are re-asserted every scan should override this to snapshot the
   pending write requests, so a single transaction stays bounded:
   drain snapshotted writes -> read -> IDLE. *)
METHOD PROTECTED OnBeginTransaction
END_METHOD

END_FUNCTION_BLOCK
