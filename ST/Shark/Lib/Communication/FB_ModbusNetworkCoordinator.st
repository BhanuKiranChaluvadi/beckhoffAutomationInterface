(* Modbus network coordinator. Round-robin scheduler for shared-bus Modbus slaves. *)
FUNCTION_BLOCK FB_ModbusNetworkCoordinator
VAR_OUTPUT
    nCurrentSlaveIndex : BYTE := 0;
END_VAR
VAR
    _aSlaves : ARRAY[1..GVL_Modbus.MODBUS_MAX_NETWORK_DEVICES] OF I_ModbusSlave;
    _ipActiveSlave : I_ModbusSlave;
    _tonNetworkSettling : TON;
    _tSettlingTime : TIME := T#5MS;
    _eNetworkState : E_NetworkState := E_NetworkState.SELECT_SLAVE;
    _nNumSlaves : BYTE := 0;
    _bInitialized : BOOL := FALSE;
END_VAR

VAR CONSTANT
    MIN_SLAVE_INDEX : BYTE := 1;
END_VAR

METHOD PUBLIC AddSlave : BOOL
VAR_INPUT
    ipSlave : I_ModbusSlave;
END_VAR
VAR
    i : BYTE;
END_VAR
    IF ipSlave = 0 THEN
        RETURN;
    END_IF

    IF _nNumSlaves >= GVL_Modbus.MODBUS_MAX_NETWORK_DEVICES THEN
        RETURN;
    END_IF

    FOR i := 1 TO _nNumSlaves DO
        IF _aSlaves[i] = ipSlave THEN
            RETURN;
        END_IF
    END_FOR

    _nNumSlaves := _nNumSlaves + 1;
    _aSlaves[_nNumSlaves] := ipSlave;
    AddSlave := TRUE;
END_METHOD

METHOD PUBLIC Init : BOOL
VAR_INPUT
    tSettlingTime : TIME := T#5MS;
END_VAR
    IF _nNumSlaves = 0 THEN
        RETURN;
    END_IF

    _tSettlingTime := tSettlingTime;
    nCurrentSlaveIndex := MIN_SLAVE_INDEX;
    _eNetworkState := E_NetworkState.SELECT_SLAVE;
    _tonNetworkSettling(IN := FALSE);
    _ipActiveSlave := 0;
    _bInitialized := TRUE;
    Init := TRUE;
END_METHOD

METHOD PUBLIC Execute : BOOL
    IF NOT _bInitialized THEN
        RETURN;
    END_IF

    CASE _eNetworkState OF
        E_NetworkState.SELECT_SLAVE:
            IF nCurrentSlaveIndex = 0 OR nCurrentSlaveIndex > _nNumSlaves THEN
                nCurrentSlaveIndex := MIN_SLAVE_INDEX;
            END_IF
            _ipActiveSlave := _aSlaves[nCurrentSlaveIndex];
            _eNetworkState := E_NetworkState.INITIATE_REQUEST;

        E_NetworkState.INITIATE_REQUEST:
            IF _ipActiveSlave <> 0 AND_THEN NOT _ipActiveSlave.IsBusy() THEN
                _ipActiveSlave.HandleRequest();
                _eNetworkState := E_NetworkState.AWAIT_COMPLETION;
            ELSE
                _eNetworkState := E_NetworkState.SETTLING;
                _tonNetworkSettling(IN := TRUE, PT := _tSettlingTime);
            END_IF

        E_NetworkState.AWAIT_COMPLETION:
            IF _ipActiveSlave <> 0 THEN
                _ipActiveSlave.Update();
                IF NOT _ipActiveSlave.IsBusy() THEN
                    _eNetworkState := E_NetworkState.SETTLING;
                    _tonNetworkSettling(IN := TRUE, PT := _tSettlingTime);
                END_IF
            ELSE
                _eNetworkState := E_NetworkState.SETTLING;
                _tonNetworkSettling(IN := TRUE, PT := _tSettlingTime);
            END_IF

        E_NetworkState.SETTLING:
            _tonNetworkSettling();
            IF _tonNetworkSettling.Q THEN
                _tonNetworkSettling(IN := FALSE);
                _ipActiveSlave := 0;
                nCurrentSlaveIndex := (nCurrentSlaveIndex MOD _nNumSlaves) + 1;
                _eNetworkState := E_NetworkState.SELECT_SLAVE;
            END_IF
    END_CASE

    Execute := TRUE;
END_METHOD

METHOD PUBLIC GetSlaveCount : BYTE
    GetSlaveCount := _nNumSlaves;
END_METHOD

METHOD PUBLIC GetNetworkState : E_NetworkState
    GetNetworkState := _eNetworkState;
END_METHOD

END_FUNCTION_BLOCK
