using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using TCatSysManagerLib;
using System.IO;
using System.Reflection;

namespace ActivateConfigurationRemote
{
    class Program
    {
        private static string _tsmPath = Path.GetDirectoryName(Assembly.GetEntryAssembly().Location) + "\\Templates\\Sample.tsm";
        private static TcSysManager _sysManager;

        static void Main(string[] args)
        {
            _sysManager = new TcSysManager();
            ITcSysManager2 _sysManager2 = (ITcSysManager2) _sysManager;

            _sysManager2.OpenConfiguration(_tsmPath);

            _sysManager2.SetTargetNetId("127.0.0.1.1.1");
            
            /* Put additional System Manager configuration here
             * ...
             * ...
             */

            _sysManager2.ActivateConfiguration();
        }
    }
}
