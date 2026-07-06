using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using TCatSysManagerLib;
using System.IO;
using System.Reflection;

namespace LinkPLCProjectTC2
{
    class Program
    {
        private static string _tpyPath = Path.GetDirectoryName(Assembly.GetEntryAssembly().Location) + "\\Templates\\Sample.tpy";
        private static TcSysManager _sysManager;

        static void Main(string[] args)
        {
            try
            {
                _sysManager = new TcSysManager();
                _sysManager.NewConfiguration();

                ITcSmTreeItem plcNode = _sysManager.LookupTreeItem("TIPC");
                ITcSmTreeItem plc = plcNode.CreateChild(_tpyPath, 0, "", null);
                ITcSmTreeItem plcProject = _sysManager.LookupTreeItem("TIPC^Sample");

                _sysManager.SaveConfiguration(Path.GetDirectoryName(Assembly.GetEntryAssembly().Location) + "\\Templates\\Sample.tsm");
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.ToString());
            }
        }
    }
}
