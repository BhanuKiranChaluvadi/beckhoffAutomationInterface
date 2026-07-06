using System;
using System.Collections.Generic;
using System.Text;

using TCatSysManagerLib;
using System.IO;
using System.Reflection;

/* =====================================================
 * The following sample code creates an ADS route to a remote
 * ADS device. The following XML structure will be consumed
 * on the System Manager node SYSTEM\Route Settings:
 * 
 * <TreeItem>
	<ItemName>Route Settings</ItemName>
	<PathName>TIRR</PathName>
	<RoutePrj>
		<TargetList>
			<BroadcastSearch>true</BroadcastSearch>
		</TargetList>
		<AddRoute>
			<RemoteName>RouteName</RemoteName>
			<RemoteNetId>10.1.128.217.1.1</RemoteNetId>
			<RemoteIpAddr>10.1.128.217</RemoteIpAddr>
			<UserName>Administrator</UserName>
			<Password>1</Password>
			<NoEncryption></NoEncryption>
		</AddRoute>
	</RoutePrj>
   </TreeItem>
 * 
 * This will create a new ADS route to the remote target
 * with NetID 10.1.128.217.1.1 and IP 10.1.128.217. By
 * specifying the username/password combination of the remote
 * target, the corresponding return route will be created
 * on the remote device.
 * ===================================================== */
namespace AddingRoutesTC2
{
    class Program
    {
        private static string _tsmPath = Path.GetDirectoryName(Assembly.GetEntryAssembly().Location) + "\\Templates\\Sample.tsm";
        private static TcSysManager _sysManager;

        static void Main(string[] args)
        {
            _sysManager = new TcSysManager();
            _sysManager.NewConfiguration();

            /* ==============================================
             * XML String as shown above
             * ============================================== */
            string xmlString = "<TreeItem><ItemName>Route Settings</ItemName><PathName>TIRR</PathName><RoutePrj><TargetList><BroadcastSearch>true</BroadcastSearch></TargetList><AddRoute><RemoteName>RouteName</RemoteName><RemoteNetId>10.1.128.217.1.1</RemoteNetId><RemoteIpAddr>10.1.128.217</RemoteIpAddr><UserName>Administrator</UserName><Password>1</Password><NoEncryption></NoEncryption></AddRoute></RoutePrj></TreeItem>";

            /* ==============================================
             * Lookup System Manager node "SYSTEM^Route Settings" using Shortcut "TIRR"
             * ============================================== */
            ITcSmTreeItem routesNode = _sysManager.LookupTreeItem("TIRR");
            routesNode.ConsumeXml(xmlString);

            /* ==============================================
            /* Save configuration
            /* ============================================== */
            _sysManager.SaveConfiguration(_tsmPath);
        }
    }
}
