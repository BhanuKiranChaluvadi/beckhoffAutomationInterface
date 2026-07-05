(* Bronkhorst Needle Valve Pressure Controller. Modbus RTU driver for Bronkhorst mass flow controllers operating in pressure mode. *)
FUNCTION_BLOCK FB_BronkhorstModbusAdapter EXTENDS FB_BaseModbusDevice IMPLEMENTS I_BronkhorstController
VAR_OUTPUT
    rMeasuredValue : LREAL := 0.0;
    rSetpoint : LREAL := 0.0;
    rValveOpeningPercent : LREAL := 0.0;
    eControlMode : E_BronkhorstControlMode := E_BronkhorstControlMode.BUS_CONTROL;
    nFluidNumber : WORD := 0;
END_VAR

VAR
    _rFullScaleCapacity		: LREAL;
    _rMinimumValue			: LREAL;
    _rCommandedSetpoint		: LREAL := 0.0;
    _eCommandedMode			: E_BronkhorstControlMode := E_BronkhorstControlMode.BUS_CONTROL;
    _bWriteSetpoint			: BOOL := FALSE;
    _bWriteControlMode		: BOOL := FALSE;
    // Per-transaction snapshot of the live write requests above. Latched once in
    // OnBeginTransaction() so that the controller re-asserting _bWriteSetpoint/
    // _bWriteControlMode every scan cannot starve the write-drain/read sequence.
    _bTxWriteSetpoint		: BOOL := FALSE;
    _bTxWriteControlMode	: BOOL := FALSE;
    _nReadCounter			: USINT := 0;
    _eReadState				: (READ_PROCESS, READ_CONTROL_MODE, READ_FLUID_NUMBER, READ_VALVE_OUTPUT) := READ_PROCESS;
    _nLastReadRegister		: WORD;
    _wSetpointRawCounts		: WORD;
END_VAR

VAR CONSTANT
    PROCESS_READS_PER_DIAGNOSTIC : USINT := 10;

    // Modbus Register Addresses (Function Code 3/6 - Read/Write Holding Registers)
    REG_MEASURE : WORD := 32;             // Process value (pressure/flow) - WORD (0-32000 counts)
    REG_SETPOINT : WORD := 33;            // Setpoint register - WORD (0-32000 counts)
    REG_CONTROL_MODE : WORD := 36;        // Control mode - WORD
    REG_FLUID_NUMBER : WORD := 48;        // Fluid number - WORD
    REG_VALVE_OUTPUT : WORD := 61960;     // Valve output - 2 WORDs (UDINT)

    // Scaling constants
    RAW_COUNTS_MAX : WORD := 32000;        // Maximum raw counts value (0-32000)
    RAW_COUNTS_MIN : WORD := 0;            // Minimum raw counts value

    // Valve output scaling (4-byte UDINT, 0-16777215)
    VALVE_OUTPUT_RAW_COUNTS_MIN : UDINT := 0; // Minimum valve output raw counts
    VALVE_OUTPUT_RAW_COUNTS_MAX : UDINT := 16777215; // Maximum valve output raw counts
END_VAR

METHOD PUBLIC Init : BOOL
VAR_INPUT
    sInstanceName      : STRING(80);
    nSlaveAddress      : BYTE;
    ipModbusComm       : I_ModbusCommunication;
    rFullScaleCapacity : LREAL := 3.0;
    rMinimumValue      : LREAL := 0.0;
END_VAR
    IF nSlaveAddress = 0 OR
        nSlaveAddress > GVL_Modbus.MODBUS_MAX_SLAVE_ADDRESS OR
        rFullScaleCapacity <= rMinimumValue THEN
        RaiseError(E_ErrorId.ERR_GEN_INVALID_PARAM, 'Invalid slave address or capacity range');
        Init := FALSE;
        RETURN;
    END_IF

    _rFullScaleCapacity := rFullScaleCapacity;
    _rMinimumValue := rMinimumValue;

    SUPER^._Init(nUnitId := nSlaveAddress, ipModbusComm := ipModbusComm);
    SetName(sInstanceName);
    ClearError();
    Init := TRUE;
END_METHOD

    (* Abstract method implementation from FB_BaseModbusDevice *)
METHOD PROTECTED HandleSuccessfulRead
VAR_INPUT
    pData						: POINTER TO BYTE;
    nDataLength					: UINT;
END_VAR
VAR
    pReadWord					: POINTER TO WORD;
END_VAR
    pReadWord := pData;
    IF pReadWord = 0 THEN
        RETURN;
    END_IF

    IF _nLastReadRegister = REG_MEASURE THEN
        ProcessReadData(pReadWord);
    ELSE
        ProcessDiagnosticData(pReadWord);
    END_IF;
END_METHOD

METHOD PROTECTED HandleSuccessfulWrite
    // Clear the write that was just transmitted, mirroring GetWritePayload's
    // priority order (setpoint first, then control mode). Operates on the
    // transaction snapshot so a single turn drains its latched writes exactly once.
    IF _bTxWriteSetpoint THEN
        _bTxWriteSetpoint := FALSE;
    ELSIF _bTxWriteControlMode THEN
        _bTxWriteControlMode := FALSE;
    END_IF;
END_METHOD

(* Latch a snapshot of the pending write requests at transaction start and
   consume the live flags. The controller re-asserts _bWriteSetpoint/
   _bWriteControlMode every scan; without this snapshot the base FSM would loop
   START_WRITE <-> WAIT_FOR_WRITE forever and never read or return to IDLE. *)
METHOD PROTECTED OnBeginTransaction
    _bTxWriteSetpoint := _bWriteSetpoint;
    _bTxWriteControlMode := _bWriteControlMode;
    _bWriteSetpoint := FALSE;
    _bWriteControlMode := FALSE;
END_METHOD

METHOD PROTECTED GetReadPayload : BOOL
VAR_OUTPUT
    nStartingRegister			: WORD;
    nQuantity					: WORD;
END_VAR
    CASE _eReadState OF
        READ_PROCESS:
        nStartingRegister := REG_MEASURE;
        nQuantity := 2;
        _nLastReadRegister := nStartingRegister;
        GetReadPayload := TRUE;
        _nReadCounter := _nReadCounter + 1;
        IF _nReadCounter >= PROCESS_READS_PER_DIAGNOSTIC THEN
            _nReadCounter := 0;
            _eReadState := READ_CONTROL_MODE;
        END_IF;

        READ_CONTROL_MODE:
        nStartingRegister := REG_CONTROL_MODE;
        nQuantity := 1;
        _nLastReadRegister := nStartingRegister;
        GetReadPayload := TRUE;
        _eReadState := READ_FLUID_NUMBER;

        READ_FLUID_NUMBER:
        nStartingRegister := REG_FLUID_NUMBER;
        nQuantity := 1;
        _nLastReadRegister := nStartingRegister;
        GetReadPayload := TRUE;
        _eReadState := READ_VALVE_OUTPUT;

        READ_VALVE_OUTPUT:
        nStartingRegister := REG_VALVE_OUTPUT;
        nQuantity := 2;
        _nLastReadRegister := nStartingRegister;
        GetReadPayload := TRUE;
        _eReadState := READ_PROCESS;
    END_CASE;
END_METHOD

METHOD PROTECTED GetWritePayload : BOOL
VAR_OUTPUT
    nStartingRegister			: WORD;
    nQuantity					: WORD;
    pData						: POINTER TO BYTE;
END_VAR
    IF _bTxWriteSetpoint THEN
        _wSetpointRawCounts := F_EngineeringToRawCounts(_rCommandedSetpoint, _rMinimumValue, _rFullScaleCapacity, RAW_COUNTS_MAX);
        nStartingRegister := REG_SETPOINT;
        nQuantity := 1;
        pData := ADR(_wSetpointRawCounts);
        GetWritePayload := TRUE;
    ELSIF _bTxWriteControlMode THEN
        nStartingRegister := REG_CONTROL_MODE;
        nQuantity := 1;
        pData := ADR(_eCommandedMode);
        GetWritePayload := TRUE;
    ELSE
        GetWritePayload := FALSE;
    END_IF;
END_METHOD

METHOD PRIVATE ProcessReadData
VAR_INPUT
    pData						: POINTER TO WORD;
END_VAR
    rMeasuredValue := F_RawCountsToEngineering(pData[0], _rMinimumValue, _rFullScaleCapacity, RAW_COUNTS_MAX);
    rSetpoint := F_RawCountsToEngineering(pData[1], _rMinimumValue, _rFullScaleCapacity, RAW_COUNTS_MAX);

    ClearError();
END_METHOD

METHOD PRIVATE ProcessDiagnosticData
VAR_INPUT
    pData						: POINTER TO WORD;
END_VAR
VAR
    udRawCountsValue			: UDINT;
    nModeValue					: INT;
END_VAR
    CASE _nLastReadRegister OF
        REG_CONTROL_MODE:
        nModeValue := WORD_TO_INT(pData[0]);
        eControlMode := ConvertIntToControlMode(nModeValue);
        REG_FLUID_NUMBER:
        nFluidNumber := pData[0];
        REG_VALVE_OUTPUT:
        udRawCountsValue := CombineWordsToUDINT(pData[0], pData[1]);
        rValveOpeningPercent := ConvertValveOutputToPercent(udRawCountsValue);
    END_CASE;
END_METHOD

METHOD PRIVATE ConvertValveOutputToPercent : REAL
VAR_INPUT
    udRawCountsValue : UDINT;
END_VAR
    // Valve output: (raw counts - min) / (max - min) * 100 → 0-100%
    ConvertValveOutputToPercent := (UDINT_TO_REAL(udRawCountsValue - VALVE_OUTPUT_RAW_COUNTS_MIN) /
        UDINT_TO_REAL(VALVE_OUTPUT_RAW_COUNTS_MAX - VALVE_OUTPUT_RAW_COUNTS_MIN)) * 100.0;
END_METHOD

METHOD PRIVATE CombineWordsToUDINT : UDINT
VAR_INPUT
    wHighWord : WORD;
    wLowWord : WORD;
END_VAR
    // Combine two 16-bit words (MSB first) into 32-bit UDINT
    CombineWordsToUDINT := (SHL(WORD_TO_UDINT(wHighWord), 16)) OR WORD_TO_UDINT(wLowWord);
END_METHOD

METHOD PRIVATE ConvertIntToControlMode : E_BronkhorstControlMode
VAR_INPUT
    nValue : INT;
END_VAR
    CASE nValue OF
        E_BronkhorstControlMode.BUS_CONTROL:
        ConvertIntToControlMode := E_BronkhorstControlMode.BUS_CONTROL;
        E_BronkhorstControlMode.VALVE_CLOSED:
        ConvertIntToControlMode := E_BronkhorstControlMode.VALVE_CLOSED;
        E_BronkhorstControlMode.VALVE_PURGE:
        ConvertIntToControlMode := E_BronkhorstControlMode.VALVE_PURGE;
    ELSE
        ConvertIntToControlMode := E_BronkhorstControlMode.BUS_CONTROL;
    END_CASE;
END_METHOD

    // I_BronkhorstController Implementation

METHOD PUBLIC GetMeasuredValue : REAL
    GetMeasuredValue := LREAL_TO_REAL(rMeasuredValue);
END_METHOD

(* SIMULATION HOOK — Overwrites the polled Modbus value with an injected value.
   Production: Modbus polling continually updates rMeasuredValue.
   Simulation: PRG_SIMULATION calls this each cycle to overwrite with physics output.
   Symmetric to the EtherCAT path where simulation writes IO_Ctrl*.MeasuredValue directly. *)
METHOD PUBLIC InjectMeasuredValue
VAR_INPUT
    rValue : LREAL;
END_VAR
    rMeasuredValue := rValue;
END_METHOD

METHOD PUBLIC GetValveOutput : REAL
    GetValveOutput := LREAL_TO_REAL(rValveOpeningPercent);
END_METHOD

METHOD PUBLIC WriteSetpoint
VAR_INPUT
    rSetpoint : REAL;
END_VAR
    _rCommandedSetpoint := REAL_TO_LREAL(rSetpoint);
    _bWriteSetpoint := TRUE;
END_METHOD

METHOD PUBLIC WriteControlMode
VAR_INPUT
    nMode : USINT;
END_VAR
    _eCommandedMode := ConvertIntToControlMode(USINT_TO_INT(nMode));
    _bWriteControlMode := TRUE;
END_METHOD

METHOD PUBLIC WriteEnable
VAR_INPUT
    bEnable : BOOL;
END_VAR
    IF bEnable THEN
        SetControlMode(E_BronkhorstControlMode.BUS_CONTROL);
    ELSE
        SetControlMode(E_BronkhorstControlMode.VALVE_CLOSED);
    END_IF
END_METHOD

    // Bronkhorst-specific convenience methods

METHOD PUBLIC SetControlMode
VAR_INPUT
    eNewMode : E_BronkhorstControlMode;
END_VAR
    _eCommandedMode := eNewMode;
    _bWriteControlMode := TRUE;
END_METHOD

METHOD PUBLIC SetPIDMode
    SetControlMode(E_BronkhorstControlMode.BUS_CONTROL);
END_METHOD

END_FUNCTION_BLOCK

