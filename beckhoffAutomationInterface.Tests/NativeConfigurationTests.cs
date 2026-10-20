using System.Diagnostics;
using System.Text;
using System.Xml.Linq;
using BeckhoffAutomationInterface.Machines;
using Xunit;

namespace BeckhoffAutomationInterface.Tests
{
    /// <summary>The retained native configuration (port of Tools/native_configuration.py): apply,
    /// links and capture, with the same rules and the same bytes as the Python tool.</summary>
    public class NativeConfigurationTests
    {
        const string OldPrefix = "TIPC^Legacy^Legacy Instance";

        static string Config(string extra = "") =>
            "<?xml version='1.0' encoding='utf-8'?>\n<NativeConfiguration schemaVersion=\"1\" PlcPrefix=\"" + OldPrefix + "\">" +
            "<DataTypes><DataType><Name>T1</Name></DataType></DataTypes>" +
            "<ImageDatas><ImageData Id=\"1\" /></ImageDatas>" +
            "<Io><Device Id=\"1\"><Name>Device 1 (EtherCAT)</Name></Device></Io>" +
            "<Motion><NC><Name>NC-Task 1 SAF</Name></NC></Motion>" +
            "<Safety><Project Name=\"S\" /></Safety>" +
            "<Mappings>" +
            "<MappingInfo Identifier=\"x\" />" +
            "<OwnerA Name=\"" + OldPrefix + "\"><OwnerB Name=\"TIID^Device 1 (EtherCAT)^Term 2\"><Link VarA=\"PlcTask Inputs^GVL.bIn\" VarB=\"Channel 1^Input\" /></OwnerB></OwnerA>" +
            "<OwnerA Name=\"TIID^Device 1 (EtherCAT)\"><OwnerB Name=\"" + OldPrefix + "^PlcTask Outputs\"><Link VarA=\"x\" VarB=\"y\" /></OwnerB></OwnerA>" +
            "<OwnerA Name=\"" + OldPrefix + "2\" />" + // only the prefix itself or prefix^... is rebased
            "</Mappings>" + extra + "</NativeConfiguration>";

        static XDocument Project(string plc = "NFPro", string extra = "") => XDocument.Parse(
            "<TcSmProject xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\" xsi:noNamespaceSchemaLocation=\"x\"><Project><System />" +
            "<Plc><Project Name=\"" + plc + "\"><Instance><Name>" + plc + " Instance</Name></Instance></Project></Plc>" +
            "<Io><Device Id=\"9\" /></Io></Project>" + extra + "</TcSmProject>");

        static XDocument Native(string text) => NativeConfiguration.Parse(Encoding.UTF8.GetBytes(text), "test");

        [Fact]
        public void ApplyTo_PutsEachSectionInPlace_AndRebasesOnlyThePlcPrefix()
        {
            XDocument target = Project();
            Assert.Equal("NFPro", NativeConfiguration.ApplyTo(Native(Config()), target));
            XElement project = target.Root.Element("Project");
            Assert.Equal(new[] { "System", "Plc", "Io", "Motion", "Safety" }, project.Elements().Select(e => e.Name.LocalName));
            Assert.Equal(new[] { "Project", "DataTypes", "ImageDatas", "Mappings" }, target.Root.Elements().Select(e => e.Name.LocalName));
            Assert.Equal("1", (string)project.Element("Io").Element("Device").Attribute("Id")); // the old Io was replaced
            List<string> owners = target.Root.Element("Mappings").Descendants().Select(e => (string)e.Attribute("Name")).Where(n => n != null).ToList();
            Assert.Contains("TIPC^NFPro^NFPro Instance", owners);
            Assert.Contains("TIPC^NFPro^NFPro Instance^PlcTask Outputs", owners);
            Assert.Contains(OldPrefix + "2", owners);
            Assert.DoesNotContain(OldPrefix, owners);
        }

        [Theory]
        [InlineData("<NativeConfiguration schemaVersion=\"2\" PlcPrefix=\"TIPC^A^A Instance\" />", "Unsupported native configuration")]
        [InlineData("<Other schemaVersion=\"1\" />", "Unsupported native configuration")]
        public void ApplyTo_RefusesAnUnknownConfiguration(string config, string message)
        {
            var ex = Assert.Throws<InvalidOperationException>(() => NativeConfiguration.ApplyTo(Native(config), Project()));
            Assert.Contains(message, ex.Message);
        }

        [Fact]
        public void ApplyTo_RefusesAMissingOrDuplicateSection_TwoPlcs_AndNoPrefix()
        {
            Assert.Contains("exactly once", Assert.Throws<InvalidOperationException>(() =>
                NativeConfiguration.ApplyTo(Native(Config("<Io />")), Project())).Message);
            Assert.Contains("exactly once", Assert.Throws<InvalidOperationException>(() =>
                NativeConfiguration.ApplyTo(Native(Config().Replace("<ImageDatas><ImageData Id=\"1\" /></ImageDatas>", "")), Project())).Message);
            XDocument two = Project();
            two.Root.Element("Project").Element("Plc").Add(new XElement("Project", new XAttribute("Name", "Other")));
            Assert.Contains("exactly one named PLC", Assert.Throws<InvalidOperationException>(() => NativeConfiguration.ApplyTo(Native(Config()), two)).Message);
            Assert.Contains("PLC prefix", Assert.Throws<InvalidOperationException>(() =>
                NativeConfiguration.ApplyTo(Native(Config().Replace("PlcPrefix=\"" + OldPrefix + "\"", "PlcPrefix=\"Legacy\"")), Project())).Message);
        }

        [Fact]
        public void Apply_KeepsTheOriginalOnce_AndRefusesASecondApply()
        {
            string dir = TestProjects.NewTempDir("bai_native");
            string config = Path.Combine(dir, "configuration.xml"), project = Path.Combine(dir, "NFPro.tsproj");
            File.WriteAllText(config, Config());
            File.WriteAllText(project, Project().ToString());
            byte[] before = File.ReadAllBytes(project);
            Dictionary<string, object> result = NativeConfiguration.Apply(config, project);
            Assert.Equal("Applied", result["status"]);
            Assert.Equal("NFPro", result["plc"]);
            Assert.Equal(NativeConfiguration.Sha256(project), result["projectSha256"]);
            Assert.Equal(before, File.ReadAllBytes(project + ".before-native"));
            Assert.Contains("Backup already exists", Assert.Throws<InvalidOperationException>(() => NativeConfiguration.Apply(config, project)).Message);
        }

        [Fact]
        public void Links_CopyEachOwnerA_WithThePrefixOfThePlc()
        {
            XDocument links = NativeConfiguration.LinksOf(Native(Config()), "NFPro");
            Assert.Equal("VarLinks", links.Root.Name.LocalName);
            Assert.Equal(new[] { "TIPC^NFPro^NFPro Instance", "TIID^Device 1 (EtherCAT)", OldPrefix + "2" },
                links.Root.Elements("OwnerA").Select(e => (string)e.Attribute("Name")));
            Assert.Equal(2, NativeConfiguration.CountLinks(links));
            Assert.Empty(links.Root.Elements("MappingInfo"));
        }

        [Theory]
        [InlineData("NF^Pro", "Invalid PLC prefix/name")]
        [InlineData("", "Invalid PLC prefix/name")]
        public void Links_RefuseABadPlcName(string plc, string message) =>
            Assert.Contains(message, Assert.Throws<InvalidOperationException>(() => NativeConfiguration.LinksOf(Native(Config()), plc)).Message);

        [Fact]
        public void Links_RefuseNoLinks_AndReplacingTheConfiguration()
        {
            string noLinks = Config().Replace("<Link VarA=\"PlcTask Inputs^GVL.bIn\" VarB=\"Channel 1^Input\" />", "").Replace("<Link VarA=\"x\" VarB=\"y\" />", "");
            Assert.Contains("no links", Assert.Throws<InvalidOperationException>(() => NativeConfiguration.LinksOf(Native(noLinks), "P")).Message);
            string dir = TestProjects.NewTempDir("bai_native");
            string config = Path.Combine(dir, "configuration.xml");
            File.WriteAllText(config, Config());
            Assert.Contains("must not replace", Assert.Throws<InvalidOperationException>(() => NativeConfiguration.Links(config, "P", config)).Message);
            Assert.Equal(2, NativeConfiguration.Links(config, "P", Path.Combine(dir, "out", "links.xml")));
        }

        [Theory]
        [InlineData("<!DOCTYPE x [<!ENTITY e \"v\">]><x>&e;</x>")]
        [InlineData("<!entity e \"v\"><x />")]
        public void Parse_RefusesDtdsAndEntities(string xml) =>
            Assert.Contains("DTD/entity not permitted", Assert.Throws<InvalidOperationException>(() => Native(xml)).Message);

        [Fact]
        public void Text_IsWrittenAsElementTreeWritesItOnWindows()
        {
            XDocument doc = Native("<?xml version=\"1.0\"?><!-- dropped --><a k=\"1 &amp; &lt;2&gt; &quot;q&quot;\">\n  <b>x &gt; y &amp; z</b>\n  <c></c><d/><e><![CDATA[null]]></e><f> </f>\n</a>");
            Assert.Equal(
                "<?xml version='1.0' encoding='utf-8'?>\r\n<a k=\"1 &amp; &lt;2&gt; &quot;q&quot;\">\r\n  <b>x &gt; y &amp; z</b>\r\n  <c />\r\n  <d />\r\n  <e>null</e>\r\n  <f> </f>\r\n</a>",
                NativeConfiguration.Text(doc));
        }

        [Fact]
        public void Text_PutsNamespaceDeclarationsFirst()
        {
            XDocument doc = Native("<r A=\"1\" xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\" xsi:n=\"v\"><c /></r>");
            Assert.StartsWith("<?xml version='1.0' encoding='utf-8'?>\r\n<r xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\" A=\"1\" xsi:n=\"v\">", NativeConfiguration.Text(doc));
        }

        // ------------------------------------------------------------------ capture

        const string Source =
            "<TcSmProject><DataTypes><DataType /></DataTypes><ImageDatas /><Project>" +
            "<Plc><Project Name=\"Legacy\" /></Plc>" +
            "<Io><Device Id=\"1\"><Box File=\"Box 30.xti\" Id=\"30\"><EtherCAT><PortABoxInfo>#x01</PortABoxInfo></EtherCAT></Box></Device></Io>" +
            "<Motion /><Safety /></Project><Mappings /></TcSmProject>";

        const string Xti =
            "<TcSmItem><Box Id=\"99\" BoxType=\"9099\"><Name>__FILENAME__</Name><EtherCAT><PortABoxInfo>#x00</PortABoxInfo><Pdo>a</Pdo></EtherCAT></Box></TcSmItem>";

        static XDocument CaptureOf(string source, string xti, List<KeyValuePair<string, string>> files = null) =>
            NativeConfiguration.CaptureFrom(Native(source), name =>
            {
                string dir = TestProjects.NewTempDir("bai_xti");
                string path = Path.Combine(dir, name);
                File.WriteAllText(path, xti);
                return (path, Native(xti));
            }, files ?? new List<KeyValuePair<string, string>>());

        [Fact]
        public void Capture_ResolvesXtiReferences_TheReferenceOwnsIdsAndLocalSettings()
        {
            var files = new List<KeyValuePair<string, string>>();
            XElement root = CaptureOf(Source, Xti, files).Root;
            Assert.Equal("TIPC^Legacy^Legacy Instance", (string)root.Attribute("PlcPrefix"));
            Assert.Equal(new[] { "DataTypes", "ImageDatas", "Io", "Motion", "Safety", "Mappings" }, root.Elements().Select(e => e.Name.LocalName));
            XElement box = root.Element("Io").Element("Device").Element("Box");
            Assert.Null(box.Attribute("File"));
            Assert.Equal("30", (string)box.Attribute("Id"));
            Assert.Equal("9099", (string)box.Attribute("BoxType"));
            Assert.Equal("Box 30", (string)box.Element("Name"));
            Assert.Equal("#x01", (string)box.Element("EtherCAT").Element("PortABoxInfo"));
            Assert.Equal("a", (string)box.Element("EtherCAT").Element("Pdo"));
            Assert.Single(files);
        }

        [Theory]
        [InlineData(Source, "<TcSmItem><Box /><Box /></TcSmItem>", "Unexpected XTI structure")]
        [InlineData(Source, "<TcSmItem><Box><EtherCAT /><EtherCAT /></Box></TcSmItem>", "Ambiguous XTI override")]
        [InlineData(Source, "<TcSmItem><Box><Term File=\"t.xti\" /></Box></TcSmItem>", "Nested external references")]
        [InlineData("<TcSmProject><DataTypes /><ImageDatas /><Project><Plc><Project Name=\"L\" /></Plc><Io /><Safety /></Project><Mappings /></TcSmProject>", Xti, "Missing native section: Project/Motion")]
        [InlineData("<TcSmProject><Project><Plc /></Project></TcSmProject>", Xti, "exactly one PLC")]
        public void Capture_Refuses(string source, string xti, string message) =>
            Assert.Contains(message, Assert.Throws<InvalidOperationException>(() => CaptureOf(source, xti)).Message);

        [Fact]
        public void Capture_WritesConfigurationAndProvenance_InANewFolderOnly()
        {
            string dir = TestProjects.NewTempDir("bai_capture");
            string project = Path.Combine(dir, "legacy", "Legacy.tsproj");
            Directory.CreateDirectory(Path.Combine(dir, "legacy", "_Config", "IO"));
            File.WriteAllText(project, Source);
            File.WriteAllText(Path.Combine(dir, "legacy", "_Config", "IO", "Box 30.xti"), Xti);
            string output = Path.Combine(dir, "capture");
            NativeConfiguration.Capture(project, output);
            string provenance = File.ReadAllText(Path.Combine(output, "provenance.json"));
            Assert.StartsWith("{\r\n  \"schemaVersion\": 1,\r\n  \"sourceProject\": \"" + project.Replace("\\", "\\\\") + "\",\r\n  \"sourceFiles\": {\r\n", provenance);
            Assert.Contains("Box 30.xti\": \"" + NativeConfiguration.Sha256(Path.Combine(dir, "legacy", "_Config", "IO", "Box 30.xti")) + "\"", provenance);
            Assert.EndsWith("\"configurationSha256\": \"" + NativeConfiguration.Sha256(Path.Combine(output, "configuration.xml")) + "\",\r\n  \"physicalAcceptance\": false\r\n}\r\n", provenance);
            Assert.Contains("new capture directory", Assert.Throws<InvalidOperationException>(() => NativeConfiguration.Capture(project, output)).Message);
        }

        [Fact]
        public void JsonString_EscapesAsPython() =>
            Assert.Equal("\"a\\\\b \\\"q\\\" \\u00e9\\n\"", NativeConfiguration.JsonString("a\\b \"q\" é\n"));

        // ------------------------------------------------------------------ the real configuration

        static string PlcRepo => Environment.GetEnvironmentVariable("BAI_PLC_REPO")
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), @"workspace\Active\BeckhoffLib");

        /// <summary>Runs the Python tool of the PLC repository; null when Python or the tool is missing.</summary>
        static int? Python(params string[] args)
        {
            string tool = Path.Combine(PlcRepo, @"Tools\native_configuration.py");
            if (!File.Exists(tool))
                return null;
            var psi = new ProcessStartInfo("python", "-I " + string.Join(" ", new[] { tool }.Concat(args).Select(BuildInfo.Quote)))
            { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
            try
            {
                using (Process p = Process.Start(psi))
                {
                    p.StandardOutput.ReadToEnd();
                    p.StandardError.ReadToEnd();
                    p.WaitForExit();
                    return p.ExitCode;
                }
            }
            catch (System.ComponentModel.Win32Exception) { return null; }
        }

        /// <summary>The real NFPro configuration.xml and a project as bai init makes it: the C# port
        /// gives the same links.xml (436 links) and the same applied .tsproj, byte for byte, as the
        /// Python tool. Skipped (passes) without the PLC repository or Python.</summary>
        [Fact]
        public void RealConfiguration_SameBytesAsThePythonTool()
        {
            string config = Path.Combine(PlcRepo, @"Machines\NFPro\native\configuration.xml");
            if (!File.Exists(config))
                return;
            string dir = TestProjects.NewTempDir("bai_native_real");
            string py = Path.Combine(dir, "py"), cs = Path.Combine(dir, "cs");
            Directory.CreateDirectory(py);
            Directory.CreateDirectory(cs);
            string bootstrap = Path.Combine(AppContext.BaseDirectory, @"TestData\native\bootstrap.tsproj");
            File.Copy(bootstrap, Path.Combine(py, "NFPro.tsproj"));
            File.Copy(bootstrap, Path.Combine(cs, "NFPro.tsproj"));

            XDocument retained = NativeConfiguration.Load(config);
            Assert.Equal(0, NativeConfiguration.SimulatedAxes(retained));
            Assert.Equal(2, NativeConfiguration.SimulateAxes(retained)); // the two NFPro actuators
            Assert.Equal(436, NativeConfiguration.Links(config, "NFPro", Path.Combine(cs, "links.xml")));
            NativeConfiguration.Apply(config, Path.Combine(cs, "NFPro.tsproj"));
            Assert.Equal(File.ReadAllBytes(Path.Combine(cs, "links.xml")), Encoding.UTF8.GetBytes(NativeConfiguration.LinksText(config, "NFPro")));

            int? links = Python("links", "--configuration", config, "--plc", "NFPro", "--output", Path.Combine(py, "links.xml"));
            if (links == null)
                return;
            Assert.Equal(0, links);
            Assert.Equal(0, Python("apply", "--configuration", config, "--project", Path.Combine(py, "NFPro.tsproj")));
            Assert.Equal(File.ReadAllBytes(Path.Combine(py, "links.xml")), File.ReadAllBytes(Path.Combine(cs, "links.xml")));
            Assert.Equal(File.ReadAllBytes(Path.Combine(py, "NFPro.tsproj")), File.ReadAllBytes(Path.Combine(cs, "NFPro.tsproj")));
        }
    }
}
