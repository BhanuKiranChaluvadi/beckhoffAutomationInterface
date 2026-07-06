import System
import clr
import System.IO;

from System.IO import File, FileInfo, Directory, DirectoryInfo

tsmPath = Directory.GetCurrentDirectory() + "\Sample.tsm"

#Get the Specific System Manager Interface
sysManType = System.Type.GetTypeFromProgID("TCatSysManager.TcSysManager")
systemManager = System.Activator.CreateInstance(sysManType)

systemManager.OpenConfiguration(tsmPath)

systemManager.ActivateConfiguration()

systemManager.StartRestartTwinCAT()

systemManager.SaveConfiguration(tsmPath)