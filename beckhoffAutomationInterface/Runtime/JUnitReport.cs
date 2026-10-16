using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace BeckhoffAutomationInterface.Runtime
{
    /// <summary>The results of 'test' as JUnit XML (the format CI systems and test viewers read):
    /// one testsuite, one testcase per scenario, with a failure or skipped element.</summary>
    static class JUnitReport
    {
        public static XDocument Build(string suiteName, IReadOnlyList<ScenarioResult> results, string scenarioRoot, DateTime started)
        {
            var suite = new XElement("testsuite",
                new XAttribute("name", suiteName),
                new XAttribute("tests", results.Count),
                new XAttribute("failures", results.Count(r => r.Status == ScenarioStatus.Failed)),
                new XAttribute("errors", 0),
                new XAttribute("skipped", results.Count(r => r.Status == ScenarioStatus.Skipped)),
                new XAttribute("time", Time(results.Sum(r => r.Seconds))),
                new XAttribute("timestamp", started.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture)));

            foreach (ScenarioResult r in results)
            {
                var testcase = new XElement("testcase",
                    new XAttribute("classname", ClassName(scenarioRoot, r.Scenario.File)),
                    new XAttribute("name", r.Scenario.Name),
                    new XAttribute("time", Time(r.Seconds)));
                if (r.Status == ScenarioStatus.Failed)
                    testcase.Add(new XElement("failure",
                        new XAttribute("message", r.Message ?? ""),
                        string.Join(Environment.NewLine, new[] { r.Message }.Concat(r.Details))));
                else if (r.Status == ScenarioStatus.Skipped)
                    testcase.Add(new XElement("skipped", new XAttribute("message", r.Message ?? "")));
                suite.Add(testcase);
            }

            return new XDocument(new XDeclaration("1.0", "utf-8", null),
                new XElement("testsuites",
                    new XAttribute("name", suiteName),
                    new XAttribute("tests", suite.Attribute("tests").Value),
                    new XAttribute("failures", suite.Attribute("failures").Value),
                    new XAttribute("errors", 0),
                    new XAttribute("skipped", suite.Attribute("skipped").Value),
                    new XAttribute("time", suite.Attribute("time").Value),
                    suite));
        }

        public static void Write(string path, XDocument report)
        {
            string folder = Path.GetDirectoryName(Path.GetFullPath(path));
            Directory.CreateDirectory(folder);
            report.Save(path);
        }

        /// <summary>"sequences.heating" for &lt;root&gt;\sequences\heating.json: the path below the
        /// scenario folder, without the extension, with dots.</summary>
        internal static string ClassName(string scenarioRoot, string file)
        {
            string full = Path.GetFullPath(file);
            string root = scenarioRoot == null ? null : Path.GetFullPath(scenarioRoot).TrimEnd('\\', '/') + Path.DirectorySeparatorChar;
            string relative = root != null && full.StartsWith(root, StringComparison.OrdinalIgnoreCase)
                ? full.Substring(root.Length)
                : Path.GetFileName(full);
            string withoutExtension = Path.Combine(Path.GetDirectoryName(relative) ?? "", Path.GetFileNameWithoutExtension(relative));
            return withoutExtension.Replace('\\', '.').Replace('/', '.');
        }

        static string Time(double seconds) => seconds.ToString("0.000", CultureInfo.InvariantCulture);
    }
}
