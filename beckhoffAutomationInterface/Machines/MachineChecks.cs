using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;
using System.Xml;

namespace BeckhoffAutomationInterface.Machines
{
    /// <summary>The checks of a machine build and test that need no TwinCAT (pure, unit-tested).
    /// Ports of Assert-ReleaseManifests, Get-LinkBuildResult, Get-CompiledBuildExpectations,
    /// Write-BuildVerificationFile and Assert-ScenarioResults of Tools/build_checks.ps1.</summary>
    static class MachineChecks
    {
        /// <summary>A release build: no SIMULATION variant or group, and every EtherCAT adapter
        /// address is a real MAC (not empty, not a test address 02:00:00:00:xx:xx, not all 0/F).</summary>
        public static void AssertReleaseManifests(string machineFolder, bool native = false)
        {
            string variantsFile = Path.Combine(machineFolder, "variants.xml");
            if (native && !File.Exists(variantsFile))
                return; // a native family: no variants, and its I/O is the reviewed native configuration
            var variants = new XmlDocument { XmlResolver = null };
            variants.Load(variantsFile);
            foreach (XmlElement node in variants.SelectNodes("/Variants/Variant | /Variants/Group"))
                if (string.Equals(node.GetAttribute("Name"), "SIMULATION", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("SIMULATION appears in the release variants manifest.");

            XmlDocument io = IoOverlay.Resolve(Path.Combine(machineFolder, "io-devices.xml"));
            foreach (XmlElement node in io.SelectNodes("//Device[@Adapter] | //Adapter[@Mac]"))
            {
                string attribute = node.LocalName == "Adapter" ? "Mac" : "Adapter";
                string mac = Regex.Replace(node.GetAttribute(attribute), "[-:]", "");
                if (!Regex.IsMatch(mac, "^[0-9a-fA-F]{12}$") || Regex.IsMatch(mac, "^02000000[0-9a-fA-F]{4}$") ||
                    Regex.IsMatch(mac, "^(0{12}|[fF]{12})$"))
                    throw new InvalidOperationException(string.Format("Invalid or test adapter address: {0}={1}",
                        node.GetAttribute("Name"), node.GetAttribute(attribute)));
            }
        }

        /// <summary>Declared links of a resolved io-devices.xml (Links/Link).</summary>
        public static int DeclaredLinks(string ioManifest)
        {
            if (!File.Exists(ioManifest))
                return 0;
            var doc = new XmlDocument { XmlResolver = null };
            doc.Load(ioManifest);
            return doc.SelectNodes("//Links/Link").Count;
        }

        /// <summary>Links of a links.xml (a native family: its native mappings); 0 without the file.</summary>
        public static int DeclaredFileLinks(string linksXml) => Sync.VarLinksFile.CountLinks(linksXml);

        /// <summary>"PASS (n/n)", "none", or "FAIL (...)": every declared link must resolve. When one
        /// does not, the link stage disables the EtherCAT masters to keep the build green - then the
        /// build did NOT run with the I/O on, which is a failure here.</summary>
        public static string LinkResult(int declared, PipelineOutcome o)
        {
            if (o.DisabledMasters.Count > 0)
                return "FAIL (master disabled)";
            if (declared == 0)
                return "none";
            if (!o.LinkSyncRan)
                return "FAIL (no link result)";
            int linked = o.LinksLinked + o.FileLinks;
            if (linked != declared || o.LinksUnresolved != 0)
                return string.Format("FAIL ({0}/{1} linked, {2} unresolved)", linked, declared, o.LinksUnresolved);
            return string.Format("PASS ({0}/{1})", linked, declared);
        }

        /// <summary>The expected GVL_BuildInfo values, read from the compiled TMC of the build
        /// (never from the checkout or the live PLC: either could let a wrong build supply its own
        /// identity). Each entry: { symbol, type, equals }.</summary>
        public static List<Dictionary<string, object>> CompiledBuildExpectations(string tmcXml, string machine)
        {
            var doc = new XmlDocument { XmlResolver = null };
            doc.LoadXml(tmcXml);
            XmlNodeList types = doc.SelectNodes("//*[local-name()='DataType'][*[local-name()='Name']='GVL_BuildInfo']");
            if (types.Count != 1)
                throw new InvalidOperationException("Compiled TMC must contain exactly one GVL_BuildInfo.");
            var fields = new[] { ("sGitTag", "STRING(31)"), ("sGitCommit", "STRING(40)"), ("bDirty", "BOOL"), ("sMachine", "STRING(31)"), ("sBuildTime", "STRING(31)") };
            var result = new List<Dictionary<string, object>>();
            foreach ((string name, string type) in fields)
            {
                XmlNodeList nodes = types[0].SelectNodes("*[local-name()='SubItem'][*[local-name()='Name']='" + name + "']");
                if (nodes.Count != 1 || ((XmlElement)nodes[0]).SelectSingleNode("*[local-name()='Type']")?.InnerText != type)
                    throw new InvalidOperationException("Missing, duplicate or invalid compiled build field: " + name);
                string kind = name == "bDirty" ? "Bool" : "String";
                XmlNodeList defaults = nodes[0].SelectNodes("*[local-name()='Default']/*[local-name()='" + kind + "']");
                if (defaults.Count != 1)
                    throw new InvalidOperationException("Missing or duplicate compiled default: " + name);
                string text = defaults[0].InnerText;
                object value;
                if (name == "bDirty")
                {
                    if (!new[] { "true", "false", "TRUE", "FALSE", "0", "1" }.Contains(text))
                        throw new InvalidOperationException("Invalid compiled dirty flag.");
                    value = text == "true" || text == "TRUE" || text == "1";
                }
                else
                {
                    int limit = name == "sGitCommit" ? 40 : 31;
                    if (string.IsNullOrWhiteSpace(text) || text.Length > limit)
                        throw new InvalidOperationException("Empty or oversized compiled build field: " + name);
                    if (name == "sGitCommit" && !Regex.IsMatch(text, "^[0-9a-f]{40}$"))
                        throw new InvalidOperationException("Compiled commit is not a full git SHA1.");
                    if (name == "sMachine" && text != machine)
                        throw new InvalidOperationException("Compiled build belongs to a different machine.");
                    if (name == "sBuildTime" && !DateTime.TryParseExact(text, "yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
                        throw new InvalidOperationException("Invalid compiled UTC build time.");
                    value = text;
                }
                result.Add(new Dictionary<string, object> { ["symbol"] = "GVL_BuildInfo." + name, ["type"] = type, ["equals"] = value });
            }
            return result;
        }

        /// <summary>The machine's startup checks (expect.json) plus the compiled build identity, as
        /// one expect file for 'verify' with --variant &lt;machine&gt;.</summary>
        public static string StartupExpectations(string expectJson, string machine, List<Dictionary<string, object>> buildChecks)
        {
            var root = new JavaScriptSerializer().DeserializeObject(expectJson) as Dictionary<string, object>;
            object list = null;
            if (!(root != null && root.TryGetValue("variants", out object v) && v is Dictionary<string, object> variants &&
                  variants.TryGetValue(machine, out list) && list is object[] entries && entries.Length > 0))
                throw new InvalidOperationException("No startup checks for " + machine + " in expect.json.");
            var checks = ((object[])list).ToList();
            if (checks.OfType<Dictionary<string, object>>().Any(e => e.TryGetValue("symbol", out object s) && s is string sym && sym.StartsWith("GVL_BuildInfo.")))
                throw new InvalidOperationException("Startup file must not hard-code build identity; it comes from the compiled TMC.");
            checks.AddRange(buildChecks);
            var document = new Dictionary<string, object> { ["variants"] = new Dictionary<string, object> { [machine] = checks } };
            return new JavaScriptSerializer().Serialize(document);
        }

        /// <summary>Only the compiled build identity, as an expect file for 'verify' with --variant
        /// &lt;machine&gt; (a native family without expect.json).</summary>
        public static string IdentityExpectations(string machine, List<Dictionary<string, object>> buildChecks) =>
            new JavaScriptSerializer().Serialize(new Dictionary<string, object>
                { ["variants"] = new Dictionary<string, object> { [machine] = buildChecks } });

        /// <summary>The record of a passed 'machine test' (as validation.json of the PLC repository's
        /// NFPro test script) and its SHA-256 file. Returns the record file.</summary>
        public static string WriteValidationRecord(string file, MachineLayout l, string tags, int executed,
            List<NativeProject.FileEntry> scenarios, List<Dictionary<string, object>> identity, string tmc, string bai)
        {
            int scenarioFiles = scenarios.Count(s => s.Path.EndsWith(".json", StringComparison.OrdinalIgnoreCase));
            var record = new Dictionary<string, object>
            {
                ["status"] = "Passed",
                ["scope"] = l.Family.Name + " integration simulation",
                ["machine"] = l.Machine,
                ["executed"] = executed,
                ["tags"] = tags,
                ["physicalDeployment"] = false,
                ["completeScenarioSuite"] = string.IsNullOrEmpty(tags) && executed == scenarioFiles,
                ["project"] = l.Tsproj,
                ["baiSha256"] = NativeConfiguration.Sha256(bai).ToUpperInvariant(),
                ["compiledIdentity"] = identity,
                ["compiledTmcSha256"] = NativeConfiguration.Sha256(tmc).ToUpperInvariant(),
                ["scenarios"] = scenarios.Select(s => new Dictionary<string, object> { ["path"] = s.Path, ["bytes"] = s.Bytes, ["sha256"] = s.Sha256.ToUpperInvariant() }).ToList(),
                ["completedUtc"] = DateTime.UtcNow.ToString("o"),
            };
            File.WriteAllText(file, new JavaScriptSerializer().Serialize(record), new UTF8Encoding(false));
            File.WriteAllText(file + ".sha256", NativeConfiguration.Sha256(file).ToUpperInvariant() + Environment.NewLine, Encoding.ASCII);
            return file;
        }

        /// <summary>The JUnit report of 'test': present, no failure or error, at least one scenario
        /// executed. Returns the number executed.</summary>
        public static int AssertScenarioResults(string junitFile)
        {
            if (!File.Exists(junitFile))
                throw new InvalidOperationException("No JUnit result was produced.");
            var report = new XmlDocument { XmlResolver = null };
            report.Load(junitFile);
            if (report.SelectNodes("//failure | //error").Count > 0)
                throw new InvalidOperationException("JUnit contains failed scenarios.");
            foreach (XmlElement suite in report.SelectNodes("//testsuite | /testsuites"))
                foreach (string attribute in new[] { "failures", "errors" })
                {
                    string count = suite.GetAttribute(attribute);
                    if (count.Length > 0 && int.Parse(count, CultureInfo.InvariantCulture) != 0)
                        throw new InvalidOperationException(string.Format("JUnit reports {0}={1}.", attribute, count));
                }
            int executed = report.SelectNodes("//testcase[not(skipped)]").Count;
            if (executed == 0)
                throw new InvalidOperationException("No scenario executed (empty result or all scenarios skipped).");
            return executed;
        }

        /// <summary>--junit for one machine of several: 'results.xml' -> 'results-Google.xml'.</summary>
        public static string JUnitFor(string junit, string machine, bool several) =>
            !several ? Path.GetFullPath(junit)
                : Path.Combine(Path.GetDirectoryName(Path.GetFullPath(junit)),
                    Path.GetFileNameWithoutExtension(junit) + "-" + machine + Path.GetExtension(junit));

        /// <summary>The compiled TMC of the one PLC project below the TwinCAT project folder.</summary>
        public static string CompiledTmc(string tsprojFile)
        {
            string root = Path.GetDirectoryName(Path.GetFullPath(tsprojFile));
            string[] projects = Directory.GetFiles(root, "*.plcproj", SearchOption.AllDirectories);
            if (projects.Length != 1)
                throw new InvalidOperationException(string.Format("Expected one PLC project in {0}; found {1}.", root, projects.Length));
            return Path.Combine(Path.GetDirectoryName(projects[0]), Path.GetFileNameWithoutExtension(projects[0]) + ".tmc");
        }

        /// <summary>ESI files of the family (Machines/&lt;family&gt;/esi/*.xml) not installed in either
        /// TwinCAT ESI folder: bai cannot make the boxes of those devices.</summary>
        public static List<string> MissingEsi(string familyFolder, IEnumerable<string> esiFolders)
        {
            string esi = Path.Combine(familyFolder, "esi");
            if (!Directory.Exists(esi))
                return new List<string>();
            return Directory.GetFiles(esi, "*.xml").Select(Path.GetFileName)
                .Where(name => !esiFolders.Any(f => File.Exists(Path.Combine(f, name))))
                .ToList();
        }
    }
}
