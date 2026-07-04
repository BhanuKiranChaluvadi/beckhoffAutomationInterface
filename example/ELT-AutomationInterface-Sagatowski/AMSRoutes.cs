// AMSRoutes — a simple collection of Target objects.
//
// Converted from: https://www.sagatowski.com/posts/the_elt_and_the_twincat_automation_interface_two/
//
// In a real application this list would typically be populated by parsing
// a configuration XML file containing the hostname/AmsNetId/IP/credentials
// for every PLC that needs an AMS route created.

using System.Collections;

namespace EltAutomationInterface
{
    public class AMSRoutes
    {
        public ArrayList items = new ArrayList();
    }
}
