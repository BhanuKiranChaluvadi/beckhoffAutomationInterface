using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Web.Script.Serialization;

namespace BeckhoffAutomationInterface.Runtime
{
    /// <summary>A scenario file problem. The message is "file: step n: what is wrong".</summary>
    sealed class ScenarioFormatException : FormatException
    {
        public ScenarioFormatException(string message) : base(message) { }
    }

    /// <summary>Reads scenario files (see Scenario). Same JSON reader as expect.json (no NuGet).
    /// Every problem gives one clear message with the file name and the step number.</summary>
    static class ScenarioFile
    {
        static readonly string[] TopKeys = { "name", "tags", "requires", "timeoutS", "steps", "description", "comment" };
        static readonly string[] ComparisonKeys = { "equals", "notEquals", "lessThan", "greaterThan", "tolerance" };

        static readonly IReadOnlyDictionary<StepKind, string[]> StepKeys = new Dictionary<StepKind, string[]>
        {
            [StepKind.Send] = new[] { "command", "parameters", "expect", "timeoutS" },
            [StepKind.WaitUntil] = new[] { "symbol", "type", "timeoutS" }.Concat(ComparisonKeys).ToArray(),
            [StepKind.Check] = new[] { "symbol", "type" }.Concat(ComparisonKeys).ToArray(),
            [StepKind.Write] = new[] { "symbol", "type", "value" },
            [StepKind.Wait] = new[] { "seconds" },
        };

        /// <summary>One file, or every *.json below a folder (sorted by path). Returns the
        /// scenarios and the problems of the files that could not be read.</summary>
        public static List<Scenario> LoadAll(string path, List<string> problems)
        {
            List<string> files;
            if (File.Exists(path))
                files = new List<string> { path };
            else if (Directory.Exists(path))
                files = Directory.GetFiles(path, "*.json", SearchOption.AllDirectories)
                    .OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToList();
            else
                throw new ArgumentException("scenario file or folder not found: " + path);

            var scenarios = new List<Scenario>();
            foreach (string file in files)
            {
                try
                {
                    scenarios.Add(Parse(File.ReadAllText(file), file));
                }
                catch (ScenarioFormatException ex)
                {
                    problems.Add(ex.Message);
                }
            }
            return scenarios;
        }

        internal static Scenario Parse(string json, string file)
        {
            Dictionary<string, object> root;
            try
            {
                root = new JavaScriptSerializer().DeserializeObject(json) as Dictionary<string, object>;
            }
            catch (Exception ex) when (ex is ArgumentException || ex is InvalidOperationException)
            {
                throw new ScenarioFormatException(string.Format("{0}: not valid JSON: {1}", file, ex.Message));
            }
            var at = new Where(file, 0);
            if (root == null)
                throw at.Error("the root must be an object");
            at.OnlyKeys(root, TopKeys);

            var scenario = new Scenario
            {
                File = file,
                Name = at.RequiredString(root, "name"),
                Tags = at.OptionalNames(root, "tags"),
                Requires = at.OptionalNames(root, "requires"),
            };
            if (root.ContainsKey("timeoutS"))
                scenario.TimeoutS = at.Positive(root, "timeoutS");

            if (!root.TryGetValue("steps", out object steps))
                throw at.Error("\"steps\" is missing");
            if (!(steps is IEnumerable list) || steps is string || steps is IDictionary)
                throw at.Error("\"steps\" must be a list");
            int n = 0;
            foreach (object item in list)
                scenario.Steps.Add(ParseStep(item, new Where(file, ++n)));
            if (n == 0)
                throw at.Error("\"steps\" is empty");
            return scenario;
        }

        static ScenarioStep ParseStep(object item, Where at)
        {
            if (!(item is Dictionary<string, object> e))
                throw at.Error("a step must be an object");
            string kindName = at.RequiredString(e, "do");
            KeyValuePair<StepKind, string> kind = ScenarioStep.Names.FirstOrDefault(k => k.Value == kindName);
            if (kind.Value == null)
                throw at.Error(string.Format("unknown \"do\": '{0}' (use {1})", kindName, string.Join(", ", ScenarioStep.Names.Values)));
            at.OnlyKeys(e, StepKeys[kind.Key].Concat(new[] { "do", "comment" }));

            var step = new ScenarioStep { Number = at.Step, Kind = kind.Key };
            switch (kind.Key)
            {
                case StepKind.Send:
                    step.Command = at.Integer(e, "command", short.MinValue, short.MaxValue);
                    if (e.ContainsKey("parameters"))
                    {
                        step.Parameters = e["parameters"] as Dictionary<string, object>;
                        if (step.Parameters == null)
                            throw at.Error("\"parameters\" must be an object");
                    }
                    if (e.ContainsKey("expect"))
                    {
                        string expect = e["expect"] as string;
                        if (expect != "accepted" && expect != "rejected")
                            throw at.Error("\"expect\" must be \"accepted\" or \"rejected\"");
                        step.ExpectAccepted = expect == "accepted";
                    }
                    if (e.ContainsKey("timeoutS"))
                        step.TimeoutS = at.Positive(e, "timeoutS");
                    int size = CommandJson.Build(step.Command, int.MaxValue, step.Parameters).Length;
                    if (size > CommandJson.MaxLength)
                        throw at.Error(string.Format("the command JSON is {0} bytes; AdsStream takes at most {1}", size, CommandJson.MaxLength));
                    break;

                case StepKind.WaitUntil:
                case StepKind.Check:
                    step.Symbol = at.RequiredString(e, "symbol");
                    step.Type = at.Type(e, e.ContainsKey("equals") ? e["equals"] : e.ContainsKey("notEquals") ? e["notEquals"] : null);
                    step.Condition = at.Comparison(e, step.Type);
                    if (e.ContainsKey("timeoutS"))
                        step.TimeoutS = at.Positive(e, "timeoutS");
                    break;

                case StepKind.Write:
                    step.Symbol = at.RequiredString(e, "symbol");
                    if (!e.ContainsKey("value"))
                        throw at.Error("\"value\" is missing");
                    step.Type = at.Type(e, e["value"]);
                    step.Value = at.Value(e, "value", step.Type);
                    try
                    {
                        PlcValue.Encode(step.Type, step.Value);
                    }
                    catch (FormatException ex)
                    {
                        throw at.Error("\"value\": " + ex.Message);
                    }
                    break;

                case StepKind.Wait:
                    if (!e.ContainsKey("seconds"))
                        throw at.Error("\"seconds\" is missing");
                    step.Seconds = at.Number(e, "seconds");
                    if (step.Seconds < 0)
                        throw at.Error("\"seconds\" must not be negative");
                    break;
            }
            return step;
        }

        /// <summary>The place in the file, and the field readers that report it.</summary>
        sealed class Where
        {
            readonly string _file;
            public int Step { get; }

            public Where(string file, int step)
            {
                _file = file;
                Step = step;
            }

            public ScenarioFormatException Error(string message) =>
                new ScenarioFormatException(Step == 0
                    ? string.Format("{0}: {1}", _file, message)
                    : string.Format("{0}: step {1}: {2}", _file, Step, message));

            public void OnlyKeys(Dictionary<string, object> e, IEnumerable<string> allowed)
            {
                var known = new HashSet<string>(allowed, StringComparer.Ordinal);
                string unknown = e.Keys.FirstOrDefault(k => !known.Contains(k));
                if (unknown != null)
                    throw Error(string.Format("unknown field \"{0}\" (permitted: {1})", unknown,
                        string.Join(", ", known.Where(k => k != "comment" && k != "description" && k != "do"))));
            }

            public string RequiredString(Dictionary<string, object> e, string key)
            {
                if (!e.TryGetValue(key, out object v))
                    throw Error(string.Format("\"{0}\" is missing", key));
                if (!(v is string s) || s.Trim().Length == 0)
                    throw Error(string.Format("\"{0}\" must be a text that is not empty", key));
                return s.Trim();
            }

            public List<string> OptionalNames(Dictionary<string, object> e, string key)
            {
                if (!e.TryGetValue(key, out object v))
                    return new List<string>();
                if (!(v is IEnumerable items) || v is string || v is IDictionary)
                    throw Error(string.Format("\"{0}\" must be a list of names", key));
                var names = new List<string>();
                foreach (object item in items)
                {
                    if (!(item is string s) || s.Trim().Length == 0)
                        throw Error(string.Format("\"{0}\" must be a list of names", key));
                    names.Add(s.Trim());
                }
                return names;
            }

            public double Number(Dictionary<string, object> e, string key)
            {
                if (!IsNumber(e[key]))
                    throw Error(string.Format("\"{0}\" must be a number", key));
                return Convert.ToDouble(e[key], CultureInfo.InvariantCulture);
            }

            public double Positive(Dictionary<string, object> e, string key)
            {
                double d = Number(e, key);
                if (d <= 0)
                    throw Error(string.Format("\"{0}\" must be more than 0", key));
                return d;
            }

            public int Integer(Dictionary<string, object> e, string key, int min, int max)
            {
                if (!e.ContainsKey(key))
                    throw Error(string.Format("\"{0}\" is missing", key));
                double d = Number(e, key);
                if (d != Math.Floor(d) || d < min || d > max)
                    throw Error(string.Format("\"{0}\" must be a whole number from {1} to {2}", key, min, max));
                return (int)d;
            }

            /// <summary>"type", or BOOL when it is not given and the value is true or false.</summary>
            public string Type(Dictionary<string, object> e, object value)
            {
                if (!e.TryGetValue("type", out object v))
                {
                    if (value is bool)
                        return "BOOL";
                    throw Error("\"type\" is missing (for example INT, DINT, REAL, LREAL or STRING(255); only a true/false value may leave it out)");
                }
                if (!(v is string type))
                    throw Error("\"type\" must be a text");
                try
                {
                    PlcValue.SizeOf(type);
                }
                catch (FormatException ex)
                {
                    throw Error("\"type\": " + ex.Message);
                }
                return type.Trim();
            }

            /// <summary>A value of the given PLC type: bool, double or string.</summary>
            public object Value(Dictionary<string, object> e, string key, string type)
            {
                object v = e[key];
                if (PlcValue.IsBool(type))
                {
                    if (!(v is bool))
                        throw Error(string.Format("\"{0}\" must be true or false for type BOOL", key));
                    return v;
                }
                if (PlcValue.IsString(type))
                {
                    if (!(v is string))
                        throw Error(string.Format("\"{0}\" must be a text for type {1}", key, type));
                    return v;
                }
                if (!IsNumber(v))
                    throw Error(string.Format("\"{0}\" must be a number for type {1}", key, type));
                return Convert.ToDouble(v, CultureInfo.InvariantCulture);
            }

            public Comparison Comparison(Dictionary<string, object> e, string type)
            {
                bool hasEquals = e.ContainsKey("equals"), hasNot = e.ContainsKey("notEquals");
                bool hasLess = e.ContainsKey("lessThan"), hasGreater = e.ContainsKey("greaterThan");
                if (!hasEquals && !hasNot && !hasLess && !hasGreater)
                    throw Error("needs a condition: \"equals\", \"notEquals\", \"lessThan\" or \"greaterThan\"");
                if ((hasEquals ? 1 : 0) + (hasNot ? 1 : 0) + (hasLess || hasGreater ? 1 : 0) > 1)
                    throw Error("use \"equals\" or \"notEquals\" alone; only \"lessThan\" and \"greaterThan\" go together");

                bool numeric = !PlcValue.IsBool(type) && !PlcValue.IsString(type);
                var c = new Comparison();
                if (hasEquals)
                    c.EqualsValue = Value(e, "equals", type);
                if (hasNot)
                    c.NotEqualsValue = Value(e, "notEquals", type);
                if (hasLess || hasGreater)
                {
                    if (!numeric)
                        throw Error(string.Format("\"lessThan\" and \"greaterThan\" need a number type, not {0}", type));
                    if (hasLess)
                        c.LessThan = Number(e, "lessThan");
                    if (hasGreater)
                        c.GreaterThan = Number(e, "greaterThan");
                }
                if (e.ContainsKey("tolerance"))
                {
                    if (!numeric || !(hasEquals || hasNot))
                        throw Error("\"tolerance\" goes only with \"equals\" or \"notEquals\" of a number type");
                    c.Tolerance = Number(e, "tolerance");
                    if (c.Tolerance < 0)
                        throw Error("\"tolerance\" must not be negative");
                }
                return c;
            }

            static bool IsNumber(object v) => v is int || v is long || v is decimal || v is double || v is float;
        }
    }

    /// <summary>The command text the PLC reads from AdsStream, in the client's format:
    /// {"command":200,"index":7,"parameters":{...}}.</summary>
    static class CommandJson
    {
        /// <summary>AdsStream is ARRAY[0..4095] OF BYTE; one byte stays 0 as the end of the text.</summary>
        public const int MaxLength = 4095;
        public const int StreamSize = 4096;

        public static string Build(int command, int index, Dictionary<string, object> parameters) =>
            string.Format(CultureInfo.InvariantCulture, "{{\"command\":{0},\"index\":{1},\"parameters\":{2}}}",
                command, index, new JavaScriptSerializer().Serialize(parameters ?? new Dictionary<string, object>()));

        /// <summary>The text as the bytes for AdsStream, zero padded to 4096.</summary>
        public static byte[] ToStream(string json)
        {
            byte[] text = System.Text.Encoding.GetEncoding(1252).GetBytes(json);
            if (text.Length > MaxLength)
                throw new FormatException(string.Format("the command JSON is {0} bytes; AdsStream takes at most {1}", text.Length, MaxLength));
            var data = new byte[StreamSize];
            Array.Copy(text, data, text.Length);
            return data;
        }
    }
}
