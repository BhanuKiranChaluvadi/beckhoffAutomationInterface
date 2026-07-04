namespace BeckhoffAutomationInterface.Sync
{
    /// <summary>A single PLC library reference: Name/Version/Company, as required by
    /// ITcPlcLibraryManager.AddLibrary/RemoveReference.</summary>
    class LibraryReference
    {
        public string Name { get; }
        public string Version { get; }
        public string Company { get; }

        public LibraryReference(string name, string version, string company)
        {
            Name = name;
            Version = version;
            Company = company;
        }
    }
}
