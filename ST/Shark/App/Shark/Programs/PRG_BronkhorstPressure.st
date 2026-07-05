(* Bronkhorst pressure controller application (Modbus needle valves).
   Creates Modbus hardware stack and controllers, then injects
   controller interfaces into PRG_PRESSURE_SENSOR via Configure(). *)
PROGRAM PRG_BronkhorstPressure
VAR
    // Modbus Hardware Stack
    fbModbusMaster       : ModbusRtuMasterV2_KL6x22B;
    fbNetworkCoordinator : FB_ModbusNetworkCoordinator;

    // Per-controller hardware
    aHardwareHandlers : ARRAY[1..GVL_PressureSystem.CONTROLLER_COUNT] OF FB_ModbusHardwareHandler;
    aDrivers          : ARRAY[1..GVL_PressureSystem.CONTROLLER_COUNT] OF FB_BronkhorstModbusAdapter;

    // Controllers
    aControllers : ARRAY[1..GVL_PressureSystem.CONTROLLER_COUNT] OF FB_BronkhorstController;
    aInterfaces  : ARRAY[1..GVL_PressureSystem.CONTROLLER_COUNT] OF I_Controller;

    bInitialized : BOOL := FALSE;
    i : INT;
END_VAR

    IF NOT bInitialized THEN
        _Init();
    END_IF

    // Execute Modbus communication
    fbModbusMaster();
    fbNetworkCoordinator.Execute();

    // Execute controllers
    FOR i := 1 TO GVL_PressureSystem.CONTROLLER_COUNT DO
        aControllers[i].Execute();
    END_FOR

METHOD PRIVATE _Init
    // PrecursorHot (slave 1)
    _InitController(
        eId           := E_PressureController.A_Side,
        nSlaveAddress := GVL_PressureSystem.MODBUS_FIRST_SLAVE_ADDRESS
    );

    // ReactantHot (slave 2)
    _InitController(
        eId           := E_PressureController.B_Side,
        nSlaveAddress := GVL_PressureSystem.MODBUS_FIRST_SLAVE_ADDRESS + 1
    );

    fbNetworkCoordinator.Init();

    // Inject controllers into pressure subsystem
    PRG_PRESSURE_SENSOR.Configure(aipCtrl := aInterfaces);

    bInitialized := TRUE;
END_METHOD

METHOD PRIVATE _InitController
VAR_INPUT
    eId           : E_PressureController;
    nSlaveAddress : BYTE;
END_VAR
VAR
    nIdx : INT;
END_VAR
    nIdx := eId;

    aHardwareHandlers[nIdx].Init(
        sInstanceName    := CONCAT('MODBUS_PRES_', TO_STRING(eId)),
        pModbusRtuMaster := ADR(fbModbusMaster),
        nSlaveAddress    := nSlaveAddress
    );
    aDrivers[nIdx].Init(
        sInstanceName      := CONCAT('DRV_', TO_STRING(eId)),
        nSlaveAddress      := nSlaveAddress,
        ipModbusComm       := aHardwareHandlers[nIdx],
        rFullScaleCapacity := GVL_PressureSystem.MAX_PRESSURE_CTRL_BAR,
        rMinimumValue      := GVL_PressureSystem.MIN_PRESSURE_CTRL_BAR
    );

    fbNetworkCoordinator.AddSlave(aDrivers[nIdx]);
    aDrivers[nIdx].SetPIDMode();

    aControllers[nIdx].Init(
        sInstanceName      := TO_STRING(eId),
        ipHardware         := aDrivers[nIdx],
        rMinValue          := GVL_PressureSystem.MIN_PRESSURE_CTRL_BAR,
        rMaxValue          := GVL_PressureSystem.MAX_PRESSURE_CTRL_BAR,
        bInverseControl    := TRUE,
        rBoundaryTolerance := GVL_PressureSystem.SETPOINT_TOLERANCE
    );

    aInterfaces[nIdx] := aControllers[nIdx];
END_METHOD

END_PROGRAM



