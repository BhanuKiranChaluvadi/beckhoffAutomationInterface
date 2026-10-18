using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace BeckhoffAutomationInterface.Sync
{
    /// <summary>One real-time task of the PLC: its TIRT^&lt;Name&gt; task and the PLC task reference
    /// with the same name that calls the programs.</summary>
    sealed class TaskSpec
    {
        public string Name { get; }
        public int CycleTimeUs { get; }
        public int Priority { get; }

        /// <summary>The PROGRAMs the task calls, in call order.</summary>
        public IReadOnlyList<string> Calls { get; }

        public TaskSpec(string name, int cycleTimeUs, int priority, IReadOnlyList<string> calls)
        {
            Name = name;
            CycleTimeUs = cycleTimeUs;
            Priority = priority;
            Calls = calls;
        }

        /// <summary>TwinCAT keeps the cycle time in 100 ns units (TaskDef/CycleTime).</summary>
        public int CycleTime100ns => CycleTimeUs * 10;
    }

    /// <summary>
    /// The tasks of the PLC. Source of truth: an optional "tasks.xml" manifest next to the other
    /// manifests:
    /// <code>
    /// &lt;Tasks&gt;
    ///   &lt;Task Name="PlcTask" CycleTimeUs="10000" Priority="20"&gt;&lt;Call&gt;MAIN&lt;/Call&gt;&lt;/Task&gt;
    ///   &lt;Task Name="SpectrometerTask" CycleTimeUs="1000" Priority="2"&gt;&lt;Call&gt;PRG_Spectrometer&lt;/Call&gt;&lt;/Task&gt;
    /// &lt;/Tasks&gt;
    /// </code>
    /// No file = the project keeps the one task of the PLC template (PlcTask, 10 ms, priority 20,
    /// calls MAIN), as before. With a file, the file lists every task: write PlcTask in it too.
    /// A task of the project that is not in the file is reported, never deleted.
    /// Optional root attribute BaseTimeUs (default 1000 = the TwinCAT default base time): every
    /// cycle time must be a multiple of it. The tool does not change the base time.
    /// </summary>
    sealed class TaskManifest
    {
        public const int DefaultBaseTimeUs = 1000;

        public int BaseTimeUs { get; }
        public IReadOnlyList<TaskSpec> Tasks { get; }

        public TaskManifest(int baseTimeUs, IReadOnlyList<TaskSpec> tasks)
        {
            BaseTimeUs = baseTimeUs;
            Tasks = tasks;
        }
    }

    sealed class TaskManifestException : Exception
    {
        public TaskManifestException(string path, IReadOnlyList<string> problems)
            : base(string.Format("{0} is invalid:{1}  {2}", path, Environment.NewLine, string.Join(Environment.NewLine + "  ", problems)))
        {
        }
    }

    static class TaskManifestParser
    {
        // TwinCAT accepts more, but a task name is also a part of the process image names
        // ("<Task> Inputs") and of the link paths: keep it a plain identifier.
        static readonly Regex Identifier = new Regex(@"^[A-Za-z_][A-Za-z0-9_]*$");
        const int PriorityMin = 1;
        const int PriorityMax = 255;

        /// <summary>Null when the file does not exist (the project keeps its tasks, as before).
        /// Throws TaskManifestException listing every problem.</summary>
        public static TaskManifest Parse(string manifestFilePath)
        {
            if (!File.Exists(manifestFilePath))
                return null;
            return Parse(XDocument.Load(manifestFilePath, LoadOptions.SetLineInfo), manifestFilePath);
        }

        internal static TaskManifest Parse(XDocument doc, string pathForMessages)
        {
            XElement root = doc.Root;
            if (root == null || root.Name.LocalName != "Tasks")
                throw new TaskManifestException(pathForMessages, new[] { "root element must be <Tasks>" });

            var problems = new List<string>();
            int baseTimeUs = TaskManifest.DefaultBaseTimeUs;
            if (root.Attribute("BaseTimeUs") != null && !TryPositive(root, "BaseTimeUs", problems, out baseTimeUs))
                baseTimeUs = TaskManifest.DefaultBaseTimeUs;

            var tasks = new List<TaskSpec>();
            var names = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var priorities = new Dictionary<int, string>();
            var callers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (XElement e in root.Elements())
            {
                if (e.Name.LocalName != "Task")
                {
                    problems.Add(string.Format("line {0}: unknown element <{1}> (use <Task>)", Line(e), e.Name.LocalName));
                    continue;
                }
                string name = ((string)e.Attribute("Name") ?? "").Trim();
                if (name.Length == 0)
                    problems.Add(string.Format("line {0}: Name is missing", Line(e)));
                else if (!Identifier.IsMatch(name))
                    problems.Add(string.Format("line {0}: Name '{1}' is not valid (letters, digits and '_' only, not starting with a digit)", Line(e), name));
                else if (names.TryGetValue(name, out int firstLine))
                    problems.Add(string.Format("line {0}: task '{1}' is already defined on line {2}", Line(e), name, firstLine));
                else
                    names[name] = Line(e);

                bool cycleOk = TryPositive(e, "CycleTimeUs", problems, out int cycleTimeUs);
                if (cycleOk && cycleTimeUs % baseTimeUs != 0)
                    problems.Add(string.Format("line {0}: CycleTimeUs {1} is not a multiple of the base time {2} µs", Line(e), cycleTimeUs, baseTimeUs));

                bool priorityOk = TryPositive(e, "Priority", problems, out int priority);
                if (priorityOk && (priority < PriorityMin || priority > PriorityMax))
                    problems.Add(string.Format("line {0}: Priority {1} is out of range {2}..{3}", Line(e), priority, PriorityMin, PriorityMax));
                else if (priorityOk && priorities.TryGetValue(priority, out string other))
                    problems.Add(string.Format("line {0}: Priority {1} is already used by task '{2}' (each task needs its own priority)", Line(e), priority, other));
                else if (priorityOk)
                    priorities[priority] = name;

                var calls = new List<string>();
                foreach (XElement c in e.Elements())
                {
                    if (c.Name.LocalName != "Call")
                    {
                        problems.Add(string.Format("line {0}: unknown element <{1}> in <Task> (use <Call>)", Line(c), c.Name.LocalName));
                        continue;
                    }
                    string program = c.Value.Trim();
                    if (!Identifier.IsMatch(program))
                        problems.Add(string.Format("line {0}: <Call> '{1}' is not a program name", Line(c), program));
                    else if (callers.TryGetValue(program, out string caller))
                        problems.Add(string.Format("line {0}: program '{1}' is already called by task '{2}' (a program runs in one task only)", Line(c), program, caller));
                    else
                    {
                        callers[program] = name;
                        calls.Add(program);
                    }
                }
                if (calls.Count == 0 && !e.Elements("Call").Any())
                    problems.Add(string.Format("line {0}: task '{1}' has no <Call> (a task calls at least one program)", Line(e), name));

                tasks.Add(new TaskSpec(name, cycleTimeUs, priority, calls));
            }
            if (tasks.Count == 0 && problems.Count == 0)
                problems.Add("no <Task> (delete the file to keep the tasks of the PLC template)");

            if (problems.Count > 0)
                throw new TaskManifestException(pathForMessages, problems);
            return new TaskManifest(baseTimeUs, tasks);
        }

        /// <summary>Problems with the program names: each &lt;Call&gt; must name a PROGRAM of the
        /// source (case-insensitive, as TwinCAT). Empty = all calls are known programs.</summary>
        public static List<string> CheckPrograms(TaskManifest manifest, IEnumerable<string> programNames, string pathForMessages)
        {
            var programs = new HashSet<string>(programNames, StringComparer.OrdinalIgnoreCase);
            return manifest.Tasks
                .SelectMany(t => t.Calls.Where(c => !programs.Contains(c))
                    .Select(c => string.Format("{0}: task '{1}' calls '{2}', but the source has no PROGRAM '{2}'", pathForMessages, t.Name, c)))
                .ToList();
        }

        static bool TryPositive(XElement e, string attribute, List<string> problems, out int value)
        {
            string text = ((string)e.Attribute(attribute) ?? "").Trim();
            if (text.Length == 0)
            {
                problems.Add(string.Format("line {0}: {1} is missing", Line(e), attribute));
                value = 0;
                return false;
            }
            if (!int.TryParse(text, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out value) || value <= 0)
            {
                problems.Add(string.Format("line {0}: {1} '{2}' must be a whole number > 0", Line(e), attribute, text));
                value = 0;
                return false;
            }
            return true;
        }

        static int Line(XObject node) => ((IXmlLineInfo)node).HasLineInfo() ? ((IXmlLineInfo)node).LineNumber : 0;
    }
}
