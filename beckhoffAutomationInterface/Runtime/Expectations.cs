using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

namespace BeckhoffAutomationInterface.Runtime
{
    /// <summary>One expected live value, e.g.
    /// { "symbol": "MAIN.rMaxPressure", "type": "REAL", "equals": 3.0, "tolerance": 0.001 }.</summary>
    sealed class Expectation
    {
        public string Symbol { get; set; }
        public string Type { get; set; }
        public object EqualsValue { get; set; }   // "equals"
        public double? Tolerance { get; set; }
        public bool Increases { get; set; }       // a counter that must move: read twice, second > first
        public bool AbsentOrZero { get; set; }    // must not run: symbol not in the program, or 0

        public string ExpectedText =>
            AbsentOrZero ? "not in program or 0"
            : Increases ? "increasing"
            : Convert.ToString(EqualsValue, CultureInfo.InvariantCulture);
    }

    sealed class CheckResult
    {
        public string Symbol { get; set; }
        public bool Passed { get; set; }
        public string Actual { get; set; }
        public string Expected { get; set; }
    }

    /// <summary>
    /// expect.json — the live values each variant must show after activation:
    /// <code>
    /// { "variants": {
    ///     "MachineA": [ { "symbol": "MAIN.sMachine", "type": "STRING(16)", "equals": "MachineA" },
    ///                   { "symbol": "PRG_PcvModbus.nCycles", "type": "UDINT", "increases": true },
    ///                   { "symbol": "PRG_PcvEtherCAT.nCycles", "type": "UDINT", "absentOrZero": true } ] },
    ///   "default": [ ... ] }      // for a project without variants
    /// </code>
    /// </summary>
    static class ExpectationFile
    {
        public const string DefaultKey = "default";

        /// <summary>Variant name (or "default") -> expectations.</summary>
        public static Dictionary<string, List<Expectation>> Load(string path) => Parse(File.ReadAllText(path));

        internal static Dictionary<string, List<Expectation>> Parse(string json)
        {
            var root = new JavaScriptSerializer().DeserializeObject(json) as Dictionary<string, object>;
            if (root == null)
                throw new FormatException("expect.json: the root must be an object.");

            var result = new Dictionary<string, List<Expectation>>(StringComparer.OrdinalIgnoreCase);
            if (root.TryGetValue("variants", out object variants))
            {
                if (!(variants is Dictionary<string, object> byVariant))
                    throw new FormatException("expect.json: \"variants\" must be an object of variant -> list.");
                foreach (KeyValuePair<string, object> v in byVariant)
                    result[v.Key] = ParseList(v.Value, v.Key);
            }
            if (root.TryGetValue(DefaultKey, out object defaults))
                result[DefaultKey] = ParseList(defaults, DefaultKey);
            return result;
        }

        static List<Expectation> ParseList(object value, string owner)
        {
            if (!(value is IEnumerable items) || value is string)
                throw new FormatException(string.Format("expect.json: \"{0}\" must be a list.", owner));

            var list = new List<Expectation>();
            foreach (object item in items)
            {
                if (!(item is Dictionary<string, object> e))
                    throw new FormatException(string.Format("expect.json: \"{0}\" entries must be objects.", owner));
                var x = new Expectation
                {
                    Symbol = Get<string>(e, "symbol"),
                    Type = Get<string>(e, "type"),
                    Increases = e.ContainsKey("increases") && Convert.ToBoolean(e["increases"]),
                    AbsentOrZero = e.ContainsKey("absentOrZero") && Convert.ToBoolean(e["absentOrZero"]),
                    Tolerance = e.ContainsKey("tolerance") ? Convert.ToDouble(e["tolerance"], CultureInfo.InvariantCulture) : (double?)null,
                };
                if (e.ContainsKey("equals"))
                    x.EqualsValue = e["equals"];
                if (string.IsNullOrEmpty(x.Symbol) || string.IsNullOrEmpty(x.Type))
                    throw new FormatException(string.Format("expect.json: every \"{0}\" entry needs \"symbol\" and \"type\".", owner));
                int kinds = (x.EqualsValue != null ? 1 : 0) + (x.Increases ? 1 : 0) + (x.AbsentOrZero ? 1 : 0);
                if (kinds != 1)
                    throw new FormatException(string.Format("expect.json: '{0}' needs exactly one of equals / increases / absentOrZero.", x.Symbol));
                PlcValue.SizeOf(x.Type); // validates the type
                list.Add(x);
            }
            return list;
        }

        static T Get<T>(Dictionary<string, object> e, string key) where T : class =>
            e.TryGetValue(key, out object v) ? v as T : null;
    }

    /// <summary>PLC value sizes and decoding (TwinCAT little-endian layout).</summary>
    static class PlcValue
    {
        static readonly Regex StringType = new Regex(@"^STRING(?:\((\d+)\))?$", RegexOptions.IgnoreCase);

        public static int SizeOf(string type)
        {
            Match m = StringType.Match(type.Trim());
            if (m.Success)
                return (m.Groups[1].Success ? int.Parse(m.Groups[1].Value) : 80) + 1; // + terminating NUL
            switch (type.Trim().ToUpperInvariant())
            {
                case "BOOL": case "BYTE": case "USINT": case "SINT": return 1;
                case "INT": case "UINT": case "WORD": return 2;
                case "DINT": case "UDINT": case "DWORD": case "REAL": return 4;
                case "LINT": case "ULINT": case "LREAL": return 8;
                default: throw new FormatException("unsupported PLC type '" + type + "'");
            }
        }

        /// <summary>bool, double (all numbers) or string.</summary>
        public static object Decode(string type, byte[] b)
        {
            if (StringType.IsMatch(type.Trim()))
            {
                int end = Array.IndexOf(b, (byte)0);
                return Encoding.GetEncoding(1252).GetString(b, 0, end < 0 ? b.Length : end);
            }
            switch (type.Trim().ToUpperInvariant())
            {
                case "BOOL": return b[0] != 0;
                case "BYTE": case "USINT": return (double)b[0];
                case "SINT": return (double)(sbyte)b[0];
                case "INT": return (double)BitConverter.ToInt16(b, 0);
                case "UINT": case "WORD": return (double)BitConverter.ToUInt16(b, 0);
                case "DINT": return (double)BitConverter.ToInt32(b, 0);
                case "UDINT": case "DWORD": return (double)BitConverter.ToUInt32(b, 0);
                case "LINT": return (double)BitConverter.ToInt64(b, 0);
                case "ULINT": return (double)BitConverter.ToUInt64(b, 0);
                case "REAL": return (double)BitConverter.ToSingle(b, 0);
                case "LREAL": return BitConverter.ToDouble(b, 0);
                default: throw new FormatException("unsupported PLC type '" + type + "'");
            }
        }

        public static bool IsString(string type) => StringType.IsMatch(type.Trim());

        public static bool IsBool(string type) => type.Trim().Equals("BOOL", StringComparison.OrdinalIgnoreCase);

        /// <summary>Raw bytes for a write (the inverse of Decode). value: bool for BOOL, string for
        /// STRING(n), a number for the others. Throws FormatException when the value does not fit.</summary>
        public static byte[] Encode(string type, object value)
        {
            int size = SizeOf(type);
            if (IsString(type))
            {
                if (!(value is string s))
                    throw new FormatException(string.Format("a {0} value must be a string", type));
                byte[] text = Encoding.GetEncoding(1252).GetBytes(s);
                if (text.Length > size - 1)
                    throw new FormatException(string.Format("'{0}' is longer than {1} characters", s, size - 1));
                var data = new byte[size];
                Array.Copy(text, data, text.Length);
                return data;
            }
            if (IsBool(type))
            {
                if (!(value is bool b))
                    throw new FormatException("a BOOL value must be true or false");
                return new[] { b ? (byte)1 : (byte)0 };
            }
            if (!(value is double d))
                throw new FormatException(string.Format("a {0} value must be a number", type));

            string t = type.Trim().ToUpperInvariant();
            if (t == "REAL")
                return BitConverter.GetBytes((float)d);
            if (t == "LREAL")
                return BitConverter.GetBytes(d);

            if (d != Math.Floor(d))
                throw new FormatException(string.Format("a {0} value must be a whole number, not {1}", type, Format(d)));
            IntegerRange(t, out double min, out double max);
            if (d < min || d > max)
                throw new FormatException(string.Format("{0} is outside the {1} range ({2} to {3})", Format(d), type, Format(min), Format(max)));
            byte[] all = t == "ULINT" ? BitConverter.GetBytes((ulong)d) : BitConverter.GetBytes((long)d);
            var result = new byte[size];
            Array.Copy(all, result, size); // little-endian: the low bytes come first
            return result;
        }

        static void IntegerRange(string type, out double min, out double max)
        {
            switch (type)
            {
                case "BYTE": case "USINT": min = byte.MinValue; max = byte.MaxValue; return;
                case "SINT": min = sbyte.MinValue; max = sbyte.MaxValue; return;
                case "INT": min = short.MinValue; max = short.MaxValue; return;
                case "UINT": case "WORD": min = ushort.MinValue; max = ushort.MaxValue; return;
                case "DINT": min = int.MinValue; max = int.MaxValue; return;
                case "UDINT": case "DWORD": min = uint.MinValue; max = uint.MaxValue; return;
                case "LINT": min = long.MinValue; max = long.MaxValue; return;
                case "ULINT": min = ulong.MinValue; max = ulong.MaxValue; return;
                default: throw new FormatException("unsupported PLC type '" + type + "'");
            }
        }

        public static string Format(object value) =>
            value is double d ? d.ToString("0.######", CultureInfo.InvariantCulture)
            : value is bool bo ? (bo ? "TRUE" : "FALSE")
            : Convert.ToString(value, CultureInfo.InvariantCulture);
    }

    /// <summary>Compares live values with expectations. The reader returns (ADS error, bytes),
    /// so the logic is testable without a runtime.</summary>
    static class ExpectationChecker
    {
        public delegate int Reader(string symbol, int size, out byte[] data);

        public static List<CheckResult> Check(IEnumerable<Expectation> expectations, Reader read, Action<int> sleepMs)
        {
            var results = new List<CheckResult>();
            foreach (Expectation x in expectations)
                results.Add(CheckOne(x, read, sleepMs));
            return results;
        }

        static CheckResult CheckOne(Expectation x, Reader read, Action<int> sleepMs)
        {
            var r = new CheckResult { Symbol = x.Symbol, Expected = x.ExpectedText };
            int size = PlcValue.SizeOf(x.Type);
            int err = read(x.Symbol, size, out byte[] data);

            if (err == AdsClient.ErrorSymbolNotFound)
            {
                r.Actual = "not in program";
                r.Passed = x.AbsentOrZero;
                return r;
            }
            if (err != 0)
            {
                r.Actual = "ADS error " + err;
                return r;
            }

            object value = PlcValue.Decode(x.Type, data);
            if (x.AbsentOrZero)
            {
                r.Actual = PlcValue.Format(value);
                r.Passed = value is double d0 && d0 == 0 || value is bool b0 && !b0;
                return r;
            }
            if (x.Increases)
            {
                sleepMs(500);
                int err2 = read(x.Symbol, size, out byte[] data2);
                if (err2 != 0)
                {
                    r.Actual = "ADS error " + err2 + " on 2nd read";
                    return r;
                }
                object value2 = PlcValue.Decode(x.Type, data2);
                r.Actual = PlcValue.Format(value) + " -> " + PlcValue.Format(value2);
                r.Passed = value is double a && value2 is double b && b > a;
                return r;
            }

            r.Actual = PlcValue.Format(value);
            r.Passed = Matches(value, x.EqualsValue, x.Tolerance);
            return r;
        }

        static bool Matches(object actual, object expected, double? tolerance)
        {
            if (actual is string s)
                return s == Convert.ToString(expected, CultureInfo.InvariantCulture);
            if (actual is bool b)
                return expected is bool eb ? b == eb : false;
            if (actual is double d)
            {
                double e;
                try { e = Convert.ToDouble(expected, CultureInfo.InvariantCulture); }
                catch (Exception) { return false; }
                return Math.Abs(d - e) <= (tolerance ?? 1e-6);
            }
            return false;
        }
    }
}
