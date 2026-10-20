using BeckhoffAutomationInterface.Machines;

namespace BeckhoffAutomationInterface.Tests
{
    /// <summary>A small PLC repository on disk, shaped like BeckhoffLib: Lib, App/Fam, Machines/Fam
    /// (shared manifests, Platform base I/O tree, machines M1 and M2), Safety, Tests, and a
    /// bai.machines.json. No TwinCAT is needed.</summary>
    sealed class MachineRepoFixture
    {
        public const string GvlBuildInfo =
            "(* Build identity *)\r\n{attribute 'qualified_only'}\r\nVAR_GLOBAL CONSTANT\r\n" +
            "    sGitTag    : STRING(31) := 'dev';\r\n    sGitCommit : STRING(40) := '';\r\n    bDirty     : BOOL       := TRUE;\r\n" +
            "    sMachine   : STRING(31) := '';\r\n    sBuildTime : STRING(31) := '';\r\nEND_VAR\r\n";

        public string Root { get; }
        public string Work { get; }
        public string ManifestFile => Path.Combine(Root, MachineManifest.FileName);

        public MachineRepoFixture(string manifestJson = null)
        {
            string top = TestProjects.NewTempDir("bai_machine");
            Root = Path.Combine(top, "repo");
            Work = Path.Combine(top, "work");
            Write("Lib/Math/F_Add.st", "FUNCTION F_Add : INT\nVAR_INPUT\n    a : INT;\nEND_VAR\nF_Add := a;\nEND_FUNCTION\n");
            Write("App/Fam/MAIN.st", "PROGRAM MAIN\nVAR\n    n : INT;\nEND_VAR\nn := F_Add(1);\nEND_PROGRAM\n");
            Write("App/Fam/Version/GVL_BuildInfo.st", GvlBuildInfo);
            Write("App/Other/Ignored.st", "PROGRAM Ignored\nEND_PROGRAM\n");
            Write("Machines/Fam/libraries.xml", "<Libraries />");
            Write("Machines/Fam/events.xml", "<Events />");
            Write("Machines/Fam/event-classes/E.xml", "<DataType />");
            Write("Machines/Fam/plc-data-types/EL3174.xml", "<DataType />");
            Write("Machines/Fam/Platform/io-devices.xml",
                "<IoTree><Adapter Name=\"A1\" Mac=\"000105A63801\" Ports=\"8\" /><Links><Link PlcVar=\"${PLC_INSTANCE}^GVL.x\" IoPath=\"TIID^A^B\" /></Links></IoTree>");
            Write("Machines/Fam/M1/variants.xml", "<Variants DefineMode=\"Implicit\"><Variant Name=\"M1\" /><Group Name=\"FEATURE_A\"><Member>M1</Member></Group></Variants>");
            Write("Machines/Fam/M1/io-devices.xml", "<IoTree Base=\"../Platform/io-devices.xml\" PlcInstance=\"Fam Instance\" />");
            Write("Machines/Fam/M1/tasks.xml", "<Tasks><Task Name=\"PlcTask\" CycleTimeUs=\"10000\" Priority=\"20\"><Call>MAIN</Call></Task></Tasks>");
            Write("Machines/Fam/M1/safety.xml", "<Safety Name=\"S\" Folder=\"safety/S\" Source=\"Safety/Fam/S\" TargetSystemConfig=\"safety/t.xml\" TargetSystemConfigSource=\"Safety/Fam/M1/T.xml\" />");
            Write("Machines/Fam/M1/expect.json", "{ \"variants\": { \"M1\": [ { \"symbol\": \"GVL_Simulation.bActive\", \"type\": \"BOOL\", \"equals\": true } ] } }");
            Write("Machines/Fam/M2/variants.xml", "<Variants DefineMode=\"Implicit\"><Variant Name=\"M2\" /></Variants>");
            Write("Machines/Fam/M2/io-devices.xml", "<IoTree><Adapter Name=\"A1\" Mac=\"02000000AA01\" Ports=\"8\" /></IoTree>");
            Write("Safety/Fam/S/S.splcproj", "safety bytes é\r\n");
            Write("Safety/Fam/S/sub/a.sal", "a");
            Write("Safety/Fam/M1/T.xml", "<TargetSystemConfig />");
            Write("Tests/Integration/Fam/smoke.json", "{}");
            File.WriteAllText(ManifestFile, manifestJson ?? Manifest(Work));
        }

        public static string Manifest(string work) =>
            "{ \"work\": \"" + work.Replace("\\", "/") + "\", \"lock\": \"" + work.Replace("\\", "/") + "/lock/umrt.lock\"," +
            "  \"preCheck\": [\"python\", \"Tools/check.py\"]," +
            "  \"families\": { \"Fam\": { \"plc\": \"Fam\", \"sources\": [\"Lib\", \"App/Fam\"], \"shared\": \"Machines/Fam\"," +
            "    \"buildInfo\": \"App/Fam/Version/GVL_BuildInfo.st\", \"tests\": \"Tests/Integration/Fam\", \"machines\": [\"M1\", \"M2\"]," +
            "    \"simulationGas\": { \"provenance\": \"Machines/provenance.json\", \"program\": \"PRG_GAS_SIMULATION\", \"cycleTimeUs\": 100000," +
            "                         \"generator\": [\"python\", \"Tools/gen.py\", \"{model}\", \"App/Fam\", \"{src}/App/Fam/GVL_Gas.st\"] } } } }";

        public MachineManifest Load() => MachineManifest.Load(ManifestFile);

        public MachineLayout Layout(string machine, bool simulation = false) => new MachineLayout(Load(), machine, simulation);

        public string Path_(string relative) => Path.Combine(Root, relative.Replace('/', '\\'));

        public void Write(string relative, string text)
        {
            string path = Path_(relative);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, text);
        }
    }
}
