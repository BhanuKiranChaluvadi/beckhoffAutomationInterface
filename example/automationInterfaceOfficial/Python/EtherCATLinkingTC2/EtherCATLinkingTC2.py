#Script Demonstration of the following TwinCAT XAE Features (Late binding)
# - loading Visual Studio 
# - creation of Solution and TwinCAT XAE Project 
# - Adding RT-Ethernet Device
# - Parametrization of Device (Setting Address) via XML Parametrization
#
# Precondition:

# -*- coding: utf-8 -*-

import clr
import System
import System.IO

clr.AddReference('System.Xml')

from System.IO import Path, Directory, File, FileInfo
from System.Threading import (ApartmentState, Thread, ThreadStart)

tsmPath = Directory.GetCurrentDirectory() + "\Sample.tsm"
tpyPath = Directory.GetCurrentDirectory() + "\Sample.tpy"

#Get the Specific System Manager Interface
sysManType = System.Type.GetTypeFromProgID("TCatSysManager.TcSysManager")
systemManager = System.Activator.CreateInstance(sysManType)

systemManager.NewConfiguration()

devices = systemManager.LookupTreeItem("TIID") # Getting IO-Configuration
plcConfig = systemManager.LookupTreeItem("TIPC") # Getting PLC-Configuration
ncConfig = systemManager.LookupTreeItem("TINC") # Getting NC Configuration
ncTask1 = ncConfig.CreateChild("NC-Task 1", 1) # Creating NC TAsk
ncAxes = systemManager.LookupTreeItem("TINC^NC-Task 1 SAF^Axes") # Getting Axes Folder
ncAxis1 = ncAxes.CreateChild("Axis 1", 1) # Create Axis

#create EtherCAT-Master
device = devices.CreateChild("EtherCAT Master (EtherCAT)", 94)
                
#Device Parametrization for "Local Area Connection 2"
device.ConsumeXml("<TreeItem><DeviceDef><AddressInfo><Pnp><DeviceDesc>Local Area Connection 2</DeviceDesc></Pnp></AddressInfo></DeviceDef></TreeItem>")

#create EK1100 A2P
a2p = device.CreateChild("A2P (EK1100)", 9099, "", "EK1100-0000-0001")
#create Terminals
device.CreateChild("100 (EL1014)", 9099, "", "EL1014-0000-0000")
device.CreateChild("101 (EL9400)", 9099, "", "EL9400")
device.CreateChild("102 (EL2004)", 9099, "", "EL2004-0000-0000")
device.CreateChild("103 (EL9100)", 9099, "", "EL9100")
device.CreateChild("104 (EL2004)", 9099, "", "EL2004-0000-0000")
device.CreateChild("105 (EL2004)", 9099, "", "EL2004-0000-0000")
device.CreateChild("106 (EL2004)", 9099, "", "EL2004-0000-0000")
device.CreateChild("107 (EL2004)", 9099, "", "EL2004-0000-0000")
device.CreateChild("108 (EL2004)", 9099, "", "EL2004-0000-0000")
device.CreateChild("109 (EL1004)", 9099, "", "EL1004-0000-0000")
device.CreateChild("110 (EL1014)", 9099, "", "EL1014-0000-0000")
device.CreateChild("111 (EK1110)", 9099, "", "EK1110-0000-0000")

#create EK1100 A3P
a3p = device.CreateChild("A3P (EK1100)", 9099, "", "EK1100-0000-0000")

#create Terminals
a3p.CreateChild("204 (EL6751)", 9099, "", "EL6751-0000-0000")
a3p.CreateChild("205 (EL6731)", 9099, "", "EL6731-0000-0000")
a3p.CreateChild("206 (EL9010)", 9099, "", "EL9011")

#create BK1120 A4P
a4p = device.CreateChild("A4P (BK1120)", 9099, "", "BK1120-0000-9995")

#create terminals
a4p.CreateChild("Term3 (KL2114)", 2114, "End Term (KL9010)")
a4p.CreateChild("Term4 (KL1104)", 1104, "End Term (KL9010)")
a4p.CreateChild("Term5 (KL1104)", 1104, "End Term (KL9010)")
a4p.CreateChild("Term6 (KL1408)", 1408, "End Term (KL9010)")
a4p.CreateChild("Term7 (KL1408)", 1408, "End Term (KL9010)")

#create CANopen Master Device
#create Profibus Master
canOpenMaster = devices.CreateChild("CANopen Master (EL6751)", 87)

#search button functionality of Profibus Master device
# set the EtherCATDeviceName to the path of the EL6751-Terminal (known from above, separated by #^#)
#  I/O Configuration      = "TIID"
#  + EtherCAT-Master name = "EtherCAT Master (EtherCAT)"
#  + EK1100 coupler name  = "A2P (EK1100)"
#  + EL6751 name          = "204 (EL6751)"
#  EL6751 path = "TIID^EtherCAT Master (EtherCAT)^A3P (EK1100)^204 (EL6751)" is the combination from
canOpenMaster.ConsumeXml("<TreeItem><DeviceDef><AddressInfo><Ecat><EtherCATDeviceName>TIID^EtherCAT Master (EtherCAT)^A3P (EK1100)^204 (EL6751)</EtherCATDeviceName></Ecat></AddressInfo></DeviceDef></TreeItem>")

#create CANopen Slaves

#create BK5150
box11 = canOpenMaster.CreateChild("Box11 (BK5150)", 5008)

###set node address
box11.ConsumeXml("<TreeItem><BoxDef><FieldbusAddress>11</FieldbusAddress></BoxDef></TreeItem>")

#create terminals
box11.CreateChild("Term2 (KL1002)", 1002, "End Term (KL9010)")
box11.CreateChild("Term3 (KL2114)", 2114, "End Term (KL9010)")


#create Profibus Master Device
#create Profibus Master
profibusMaster = devices.CreateChild("Profibus Master (EL6731)", 86)

###assign Profibus Master device to EL6731 terminal
### set the EtherCATDeviceName to the path of the EL6731-Terminal (known from above, separated by #^#)
###  I/O Configuration      = "TIID"
###  + EtherCAT-Master name = "EtherCAT Master (EtherCAT)"
###  + EK1100 coupler name  = "A2P (EK1100)"
###  + EL6731 name          = "205 (EL6731)"
###  EL6731 path = "TIID^EtherCAT Master (EtherCAT)^A3P (EK1100)^205 (EL6731)" is the combination from
profibusMaster.ConsumeXml("<TreeItem><DeviceDef><AddressInfo><Ecat><EtherCATDeviceName>TIID^EtherCAT Master (EtherCAT)^A3P (EK1100)^205 (EL6731)</EtherCATDeviceName></Ecat></AddressInfo></DeviceDef></TreeItem>")

#create Profibus Slaves

#create Drive
profibusMaster.CreateChild("Screw A (AXIS_Screw_A_FC310x)", 1008)

#create Rod
#       nErr = item.CreateChild("MTS Rod 1: Injection A and Carriage A (MTS2)", 1003, "", "C:\TwinCAT\Io\Profibus\MTSE04C3.GSD")
#       #create TcPlug
#       nErr = item.CreateChild("Injection A Heats (TCPLUG)", 1003, "", "C:\TwinCAT\Io\Profibus\TcPlug.gsd")

#create BK3120
box22 = profibusMaster.CreateChild("Box22 (BK3120)", 1010)

#set DP-Address
box22.ConsumeXml("<TreeItem><BoxDef><FieldbusAddress>22</FieldbusAddress></BoxDef></TreeItem>")

#create terminals
box22.CreateChild("Term5 (KL2114)", 2114, "End Term (KL9010)")

#set DP-Address + Diasbled
screwA = systemManager.LookupTreeItem("TIID^Profibus Master (EL6731)^Screw A (AXIS_Screw_A_FC310x)")
screwA.ConsumeXml("<TreeItem><BoxDef><FieldbusAddress>2</FieldbusAddress></BoxDef></TreeItem>")
screwA.ConsumeXml("<TreeItem><Disabled>1</Disabled></TreeItem>")


#create Virtual USB Master Device
#create Profibus Master
usbDevice = devices.CreateChild("Virtual USB Interface (USB)", 57)

#set virtual device name
usbDevice.ConsumeXml("<TreeItem><USB><VirtualDeviceNames>1</VirtualDeviceNames></USB></TreeItem>")

#create USB Box
usbDevice.CreateChild("Box 0 (CPX8XX)", 9591)

#PLC configuration
plc = plcConfig.CreateChild(tpyPath, 0, "", None)

plcProject = systemManager.LookupTreeItem("TIPC^Sample")

#rescan plc project
#plc.ConsumeXml("<TreeItem><PlcDef><ProjectPath>" + PlcFile + "</ProjectPath><ReScan>1</ReScan></PlcDef></TreeItem>")

#create links
#link nc axis to drive
#search for nc axis
axis1 = systemManager.LookupTreeItem("TINC^NC-Task 1 SAF^Axes^Axis 1")

#link to drive
axis1.ConsumeXml("<TreeItem><NcAxisDef><AxisType>1</AxisType><AxisIoType>ProfibusMC</AxisIoType><IoItem><PathName>TIID^Profibus Master (EL6731)^Screw A (AXIS_Screw_A_FC310x)</PathName></IoItem></NcAxisDef></TreeItem>")

#link terminal channel to plc variable
systemManager.LinkVariables("TIPC^Sample^Standard^Inputs^MAIN.bIn", "TIID^EtherCAT Master (EtherCAT)^A2P (EK1100)^100 (EL1014)^Channel 1^Input")

#link terminal channel to plc variable
systemManager.LinkVariables("TIPC^Sample^Standard^Outputs^MAIN.bOut", "TIID^EtherCAT Master (EtherCAT)^A2P (EK1100)^102 (EL2004)^Channel 1^Output")

#link terminal channel to plc variable
systemManager.LinkVariables("TIPC^Sample^Standard^Outputs^MAIN.bOut", "TIID^EtherCAT Master (EtherCAT)^A4P (BK1120)^Term3 (KL2114)^Channel 1^Output")

#link terminal channel to plc variable
systemManager.LinkVariables("TIPC^Sample^Standard^Outputs^MAIN.bOut", "TIID^CANopen Master (EL6751)^Box11 (BK5150)^Term3 (KL2114)^Channel 1^Output")

#link terminal channel to plc variable
systemManager.LinkVariables("TIPC^Sample^Standard^Outputs^MAIN.bOut", "TIID^Profibus Master (EL6731)^Box22 (BK3120)^Term5 (KL2114)^Channel 1^Output")

#link Control Panel LED to plc variable
systemManager.LinkVariables("TIPC^Sample^Standard^Outputs^MAIN.bOut", "TIID^Virtual USB Interface (USB)^Box 0 (CPX8XX)^Outputs^LED 1")

print "Succeeded"

systemManager.SaveConfiguration(tsmPath)