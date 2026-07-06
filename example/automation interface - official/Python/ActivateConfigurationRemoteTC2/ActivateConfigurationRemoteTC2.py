import System
import clr
import System.IO;

from System.IO import File, FileInfo, Directory, DirectoryInfo

tsmPath = Directory.GetCurrentDirectory() + "\Sample.tsm"

#Get the Specific System Manager Interface
sysManType = System.Type.GetTypeFromProgID("TCatSysManager.TcSysManager")
systemManager = System.Activator.CreateInstance(sysManType)

systemManager.OpenConfiguration(tsmPath)

systemManager.SetTargetNetId("10.1.128.21.1.1")

systemManager.ActivateConfiguration()