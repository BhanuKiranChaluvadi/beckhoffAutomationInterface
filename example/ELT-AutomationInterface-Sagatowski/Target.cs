// Target — represents a single PLC deployment target.
//
// Converted from: https://www.sagatowski.com/posts/the_elt_and_the_twincat_automation_interface_two/
//
// Holds the data needed to create an AMS route to one PLC: its hostname
// (RemoteName), AmsNetId, IP address, and the credentials used to
// authenticate the route creation.

namespace EltAutomationInterface
{
    class Target
    {
        public string hostName;
        public string netId;
        public string ipAddr;
        public string username;
        public string password;
    }
}
