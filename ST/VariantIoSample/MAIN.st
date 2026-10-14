PROGRAM MAIN
VAR
    sMachine     : STRING(16);
    rMaxPressure : REAL;
    bDoorsFitted : BOOL;
    sPcvDriver   : STRING(16);
END_VAR

{IF defined (MachineA)}
{warning 'VARIANT CHECK: MachineA branch compiled'}
{ELSIF defined (MachineB)}
{warning 'VARIANT CHECK: MachineB branch compiled'}
{ELSE}
{error 'No machine variant selected (MachineA or MachineB)'}
{END_IF}

// Values: plain constants from the machine configuration.
sMachine     := GVL_MachineConfig.MACHINE_NAME;
rMaxPressure := GVL_MachineConfig.MAX_PRESSURE_BAR;
bDoorsFitted := GVL_MachineConfig.DOORS_FITTED;

// Structure: a feature define selects the driver.
{IF defined (PCV_MODBUS)}
{warning 'FEATURE CHECK: PCV_MODBUS driver compiled'}
PRG_PcvModbus();
sPcvDriver := PRG_PcvModbus.sName;
{ELSIF defined (PCV_ETHERCAT)}
{warning 'FEATURE CHECK: PCV_ETHERCAT driver compiled'}
PRG_PcvEtherCAT();
sPcvDriver := PRG_PcvEtherCAT.sName;
{ELSE}
{error 'No PCV feature selected (PCV_MODBUS or PCV_ETHERCAT)'}
{END_IF}
