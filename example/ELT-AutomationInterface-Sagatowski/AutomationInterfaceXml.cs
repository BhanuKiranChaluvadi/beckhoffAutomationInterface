// AutomationInterfaceXml — builds the XML string consumed by the TwinCAT
// Automation Interface to create AMS routes.
//
// Converted from: https://www.sagatowski.com/posts/the_elt_and_the_twincat_automation_interface_two/
//
// The resulting XML is passed to ITcSmTreeItem.ConsumeXml() on the
// "TIRR" (Real-Time Configuration ^ Route Settings) tree item to create one
// or more AMS routes in a single call.

using System;
using System.Xml;

namespace EltAutomationInterface
{
    // Singleton — only exposes a static helper method, but kept as a
    // singleton class to match the structure used in the article.
    class AutomationInterfaceXml
    {
        private AutomationInterfaceXml()
        {
        }

        /// <summary>
        /// This will produce a XML-string that can be consumed for creation of AMS-routes
        /// provided by the routesList input argument
        /// </summary>
        /// <param name="routesList">The list of AMS-routes</param>
        /// <returns>
        /// An XML-string that can be consumed by the TwinCAT automation interface for creation of all the AMS-routes
        /// </returns>
        public static string CreateRoutesXMLString(AMSRoutes routesList)
        {
            XmlDocument xmlDoc = new XmlDocument();

            // Create the root element
            // <TreeItem>
            XmlNode treeItemElement = xmlDoc.CreateElement("TreeItem");
            xmlDoc.AppendChild(treeItemElement);

            // <ItemName>
            XmlNode itemNameElement = xmlDoc.CreateElement("ItemName");
            itemNameElement.InnerText = "Route Settings";
            treeItemElement.AppendChild(itemNameElement);

            // <PathName>
            XmlNode pathNameElement = xmlDoc.CreateElement("PathName");
            pathNameElement.InnerText = "TIRR";
            treeItemElement.AppendChild(pathNameElement);

            // <RoutePrj>
            XmlNode routePrjElement = xmlDoc.CreateElement("RoutePrj");

            // <TargetList><BroadcastSearch>
            XmlNode targetListElement = xmlDoc.CreateElement("TargetList");
            XmlNode broadcastSearchElement = xmlDoc.CreateElement("BroadcastSearch");
            broadcastSearchElement.InnerText = "false";
            targetListElement.AppendChild(broadcastSearchElement);
            routePrjElement.AppendChild(targetListElement);

            // For every route
            foreach (Target target in routesList.items)
            {
                // <AddRoute>
                XmlNode addRouteElement = xmlDoc.CreateElement("AddRoute");

                // <RemoteName>
                XmlNode remoteNameElement = xmlDoc.CreateElement("RemoteName");
                remoteNameElement.InnerText = target.hostName;
                addRouteElement.AppendChild(remoteNameElement);

                // <RemoteNetId>
                XmlNode remoteNetIdElement = xmlDoc.CreateElement("RemoteNetId");
                remoteNetIdElement.InnerText = target.netId;
                addRouteElement.AppendChild(remoteNetIdElement);

                // <RemoteIpAddr>
                XmlNode remoteIpAddrElement = xmlDoc.CreateElement("RemoteIpAddr");
                remoteIpAddrElement.InnerText = target.ipAddr;
                addRouteElement.AppendChild(remoteIpAddrElement);

                // <UserName>
                XmlNode userNameElement = xmlDoc.CreateElement("UserName");
                userNameElement.InnerText = target.username;
                addRouteElement.AppendChild(userNameElement);

                // <Password>
                XmlNode passwordElement = xmlDoc.CreateElement("Password");
                passwordElement.InnerText = target.password;
                addRouteElement.AppendChild(passwordElement);

                // <NoEncryption/>
                XmlNode noEncryptionElement = xmlDoc.CreateElement("NoEncryption");
                addRouteElement.AppendChild(noEncryptionElement);

                routePrjElement.AppendChild(addRouteElement);
            }

            treeItemElement.AppendChild(routePrjElement);
            return xmlDoc.OuterXml;
        }
    }
}
