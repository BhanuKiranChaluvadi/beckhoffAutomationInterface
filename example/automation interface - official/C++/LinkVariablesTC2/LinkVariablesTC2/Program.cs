using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using TCatSysManagerLib;
using System.IO;
using System.Reflection;

namespace LinkVariablesTC2
{
    class Program
    {
        private static string _tsmPath = Path.GetDirectoryName(Assembly.GetEntryAssembly().Location) + "\\Templates\\Sample.tsm";
        private static TcSysManager _sysManager;

        static void Main(string[] args)
        {
            _sysManager = new TcSysManager();
            _sysManager.OpenConfiguration(_tsmPath);

            _sysManager.LinkVariables("TIPC^Sample^Standard^Outputs^MAIN.bOutput", "TIID^Device 1 (CX1100)^Box 1 (CX1100-BK)^Term 2 (KL2404)^Channel 1^Output");

            _sysManager.SaveConfiguration(_tsmPath);
        }
    }
}
