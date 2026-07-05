(* Modbus hardware communication handler. Wraps Beckhoff ModbusRtuMasterV2_KL6x22B. *)
FUNCTION_BLOCK FB_ModbusHardwareHandler IMPLEMENTS I_ModbusCommunication
VAR
    _fbError            : FB_ErrorHandler;
    _pModbusMaster      : POINTER TO ModbusRtuMasterV2_KL6x22B;
    _nSlaveAddress      : BYTE;
    _bOperationComplete : BOOL := FALSE;
    _wError             : WORD := 0;
    _aDataBuffer        : ARRAY[0..GVL_Modbus.MODBUS_DATA_BUFFER_SIZE-1] OF BYTE;
    _aReadBuffer        : ARRAY[0..GVL_Modbus.MODBUS_READ_BUFFER_SIZE-1] OF WORD;
    _nLastQuantity      : WORD;
    _nLastRegister      : WORD;
    _eLastOperation     : E_ModbusOperationType := E_ModbusOperationType.NONE;
    _bExecuteFlag       : BOOL := FALSE;
END_VAR

METHOD PRIVATE ConvertModbusBytesToWords : BOOL
VAR_INPUT
    pByteData : POINTER TO BYTE;
    nByteCount : WORD;
    pWordBuffer : POINTER TO WORD;
    nMaxWords : WORD;
END_VAR
VAR
    nWordsToConvert : WORD;
END_VAR
    IF pByteData = 0 THEN
        RETURN;
    END_IF

    IF pWordBuffer = 0 THEN
        RETURN;
    END_IF

    IF nByteCount = 0 THEN
        RETURN;
    END_IF

    IF (nByteCount MOD GVL_Modbus.BYTES_PER_REGISTER) <> 0 THEN
        RETURN;
    END_IF

    nWordsToConvert := nByteCount / GVL_Modbus.BYTES_PER_REGISTER;
    IF nWordsToConvert > nMaxWords THEN
        nWordsToConvert := nMaxWords;
    END_IF

    // Bulk copy — data is already in native (little-endian) byte order
    MEMCPY(pWordBuffer, pByteData, nWordsToConvert * SIZEOF(WORD));

    ConvertModbusBytesToWords := TRUE;
END_METHOD

METHOD PRIVATE _ValidateForOperation : BOOL
VAR_INPUT
    nQuantity : WORD;
END_VAR
    IF _pModbusMaster = 0 THEN
        _wError := GVL_Modbus.MODBUS_ERROR_INVALID_MASTER;
        _fbError.Raise(eErrorId := E_ErrorId.ERR_GEN_NULL_POINTER, sMessage := 'Modbus master not initialized');
        RETURN;
    END_IF

    IF _pModbusMaster^.BUSY THEN
        RETURN;
    END_IF

    IF nQuantity = 0 OR nQuantity > GVL_Modbus.MAX_REGISTERS_PER_OPERATION THEN
        _wError := GVL_Modbus.MODBUS_ERROR_INVALID_QUANTITY;
        _fbError.Raise(eErrorId := E_ErrorId.ERR_GEN_INVALID_PARAM, sMessage := 'Invalid register quantity');
        RETURN;
    END_IF

    _ValidateForOperation := TRUE;
END_METHOD

METHOD PUBLIC Init : BOOL
VAR_INPUT
    sInstanceName    : STRING(80);
    pModbusRtuMaster : POINTER TO ModbusRtuMasterV2_KL6x22B;
    nSlaveAddress    : BYTE;
END_VAR
    _fbError.Init(sSourceName := sInstanceName);

    _pModbusMaster := pModbusRtuMaster;
    _nSlaveAddress := nSlaveAddress;

    IF pModbusRtuMaster = 0 THEN
        _fbError.Raise(eErrorId := E_ErrorId.ERR_GEN_NULL_POINTER, sMessage := 'Modbus master pointer is null');
        Init := FALSE;
    ELSE
        _fbError.Clear();
        Init := TRUE;
    END_IF
END_METHOD

METHOD PUBLIC StartReadOperation : BOOL
VAR_INPUT
    nStartRegister : WORD;
    nQuantity : WORD;
END_VAR
    IF NOT _ValidateForOperation(nQuantity := nQuantity) THEN
        RETURN;
    END_IF

    _nLastQuantity := nQuantity;
    _nLastRegister := nStartRegister;
    _eLastOperation := E_ModbusOperationType.READ;
    _bOperationComplete := FALSE;
    _wError := 0;
    _bExecuteFlag := TRUE;
    _fbError.Clear();

    StartReadOperation := TRUE;
END_METHOD

METHOD PUBLIC StartWriteOperation : BOOL
VAR_INPUT
    nStartRegister : WORD;
    nQuantity : WORD;
    pData : POINTER TO BYTE;
END_VAR
    IF NOT _ValidateForOperation(nQuantity := nQuantity) THEN
        RETURN;
    END_IF

    IF pData = 0 THEN
        _wError := GVL_Modbus.MODBUS_ERROR_NULL_POINTER;
        _fbError.Raise(eErrorId := E_ErrorId.ERR_GEN_NULL_POINTER, sMessage := 'Data pointer is null');
        RETURN;
    END_IF

    // Bulk copy — native byte order is little-endian, matching Modbus register layout
    MEMCPY(ADR(_aDataBuffer), pData, nQuantity * GVL_Modbus.BYTES_PER_REGISTER);

    _nLastQuantity := nQuantity;
    _nLastRegister := nStartRegister;
    _eLastOperation := E_ModbusOperationType.WRITE;
    _bOperationComplete := FALSE;
    _wError := 0;
    _bExecuteFlag := TRUE;
    _fbError.Clear();

    StartWriteOperation := TRUE;
END_METHOD

METHOD PUBLIC Update : BOOL
    IF _pModbusMaster = 0 THEN
        _bOperationComplete := TRUE;
        _wError := GVL_Modbus.MODBUS_ERROR_INVALID_MASTER;
        _fbError.Raise(eErrorId := E_ErrorId.ERR_GEN_NULL_POINTER, sMessage := 'Modbus master not initialized');
        Update := TRUE;
        RETURN;
    END_IF

    IF _eLastOperation = E_ModbusOperationType.NONE THEN
        Update := _bOperationComplete;
        RETURN;
    END_IF

    _CallMaster();

    IF NOT _bOperationComplete AND_THEN NOT _pModbusMaster^.BUSY THEN
        _bOperationComplete := TRUE;
        _HandleCompletion();
        _eLastOperation := E_ModbusOperationType.NONE;
    END_IF

    Update := _bOperationComplete;
END_METHOD

METHOD PRIVATE _CallMaster
VAR
    nByteLength : WORD;
END_VAR
    nByteLength := _nLastQuantity * GVL_Modbus.BYTES_PER_REGISTER;

    IF _eLastOperation = E_ModbusOperationType.READ THEN
        _pModbusMaster^.ReadRegs(
            UnitID := _nSlaveAddress,
            Quantity := _nLastQuantity,
            MBAddr := _nLastRegister,
            cbLength := nByteLength,
            pMemoryAddr := ADR(_aDataBuffer),
            Execute := _bExecuteFlag,
            Timeout := GVL_Modbus.MODBUS_REQUEST_TIMEOUT
        );
    ELSIF _eLastOperation = E_ModbusOperationType.WRITE THEN
        _pModbusMaster^.WriteRegs(
            UnitID := _nSlaveAddress,
            Quantity := _nLastQuantity,
            MBAddr := _nLastRegister,
            cbLength := nByteLength,
            pMemoryAddr := ADR(_aDataBuffer),
            Execute := _bExecuteFlag,
            Timeout := GVL_Modbus.MODBUS_REQUEST_TIMEOUT
        );
    END_IF

    _bExecuteFlag := FALSE;
END_METHOD

METHOD PRIVATE _HandleCompletion
VAR
    nActualBytes : WORD;
END_VAR
    IF _pModbusMaster^.Error THEN
        _wError := INT_TO_WORD(_pModbusMaster^.ErrorId);
        _fbError.Raise(eErrorId := E_ErrorId.ERR_COMM_PROTOCOL,
            sMessage := CONCAT('Modbus error code: ', WORD_TO_STRING(_wError)));
        RETURN;
    END_IF

    IF _eLastOperation <> E_ModbusOperationType.READ THEN
        RETURN;
    END_IF

    nActualBytes := UDINT_TO_WORD(_pModbusMaster^.cbRead);

    IF nActualBytes = 0 THEN
        _wError := GVL_Modbus.MODBUS_ERROR_CONVERSION;
        _fbError.Raise(eErrorId := E_ErrorId.ERR_COMM_INVALID_RESPONSE, sMessage := 'No data received');
    ELSIF nActualBytes > GVL_Modbus.MODBUS_DATA_BUFFER_SIZE THEN
        _wError := GVL_Modbus.MODBUS_ERROR_CONVERSION;
        _fbError.Raise(eErrorId := E_ErrorId.ERR_COMM_BUFFER_OVERFLOW, sMessage := 'Response exceeds buffer size');
    ELSIF NOT ConvertModbusBytesToWords(
        pByteData := ADR(_aDataBuffer),
        nByteCount := nActualBytes,
        pWordBuffer := ADR(_aReadBuffer),
        nMaxWords := GVL_Modbus.MODBUS_READ_BUFFER_SIZE
    ) THEN
        _wError := GVL_Modbus.MODBUS_ERROR_CONVERSION;
        _fbError.Raise(eErrorId := E_ErrorId.ERR_COMM_DATA_CONVERSION, sMessage := 'Byte to word conversion failed');
    END_IF
END_METHOD

METHOD PUBLIC IsOperationComplete : BOOL
    IsOperationComplete := _bOperationComplete;
END_METHOD

METHOD PUBLIC GetOperationResult : BOOL
VAR_INPUT
    wErrorCode : REFERENCE TO WORD;
END_VAR
    wErrorCode := _wError;
    GetOperationResult := (_wError = 0);
END_METHOD

METHOD PUBLIC GetReadData : BOOL
VAR_INPUT
    pData : REFERENCE TO POINTER TO BYTE;
    nDataLength : REFERENCE TO UINT;
END_VAR
    pData := ADR(_aReadBuffer);
    nDataLength := WORD_TO_UINT(_nLastQuantity * GVL_Modbus.BYTES_PER_REGISTER);
    GetReadData := TRUE;
END_METHOD

METHOD PUBLIC GetSlaveAddress : BYTE
    GetSlaveAddress := _nSlaveAddress;
END_METHOD

END_FUNCTION_BLOCK
