import System
import clr
import System.IO;

from System.IO import File, FileInfo, Directory, DirectoryInfo

tsmPath = Directory.GetCurrentDirectory() + "\Sample.tsm"
tpyPath = Directory.GetCurrentDirectory() + "\Sample.tpy"

#Get the Specific System Manager Interface
sysManType = System.Type.GetTypeFromProgID("TCatSysManager.TcSysManager")
systemManager = System.Activator.CreateInstance(sysManType)

systemManager.OpenConfiguration(tsmPath)

systemManager.LinkVariables("TIPC^Sample^Standard^Outputs^MAIN.bOutput", "TIID^Device 1 (CX1100)^Box 1 (CX1100-BK)^Term 2 (KL2404)^Channel 1^Output")

systemManager.SaveConfiguration(tsmPath)