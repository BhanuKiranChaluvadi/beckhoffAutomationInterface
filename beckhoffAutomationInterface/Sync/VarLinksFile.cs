using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace BeckhoffAutomationInterface.Sync
{
    /// <summary>One &lt;Link&gt; entry from a "links.xml" (TwinCAT-native &lt;VarLinks&gt; format —
    /// see VarLinksFile's doc comment).</summary>
    class VarLinkEntry
    {
        public string PlcInstancePrefix { get; } // OwnerA.Prefix, e.g. "TIPC^Spectrometer^Spectrometer Instance"
        public string Group { get; }             // GrpA, e.g. "PlcTask Inputs"
        public string VarA { get; }              // e.g. "GVL_Shark.bMotorRunSensor" or "MAIN.fbSpec.inLogicSig[1]"
        public string IoOwnerName { get; }        // OwnerB.Name, e.g. "TIID^Device 6 (EtherCAT)^Box 18 (...)"
        public string VarB { get; }               // e.g. "Transmit PDO Mapping^OUT 001"

        /// <summary>Non-null only when VarA is a direct "&lt;GVL/PROGRAM&gt;.&lt;var&gt;" reference
        /// (exactly one '.', no further '.'/'^' nesting) — matches DeclaredIoVariable.Key
        /// (Sync/LinkChecker.cs). Null for FUNCTION_BLOCK-instance/struct-nested VarA (e.g.
        /// "MAIN.fbSpec.inLogicSig[1]" or "MAIN.fbSpec.stWatchesRaw^AckErr"), which can't be
        /// statically resolved without knowing the whole instantiation graph.</summary>
        public string SimpleKey { get; }

        public VarLinkEntry(string plcInstancePrefix, string group, string varA, string ioOwnerName, string varB)
        {
            // Some hand-authored/older samples fold the "PlcTask Inputs"/"PlcTask Outputs"
            // group directly into VarA (joined by '^') instead of a separate GrpA attribute
            // -- confirmed in Beckhoff's own official CodeGenerationDemo sample
            // (example/TC_AI_DOTNET_Samples/.../Templates/MachineTypeA/Links.xml), e.g.
            // VarA="PlcTask Outputs^MAIN.bError" with no GrpA at all. Normalize both shapes
            // so SimpleKey works either way.
            if (group == null && varA != null)
            {
                int caret = varA.IndexOf('^');
                if (caret >= 0)
                {
                    group = varA.Substring(0, caret);
                    varA = varA.Substring(caret + 1);
                }
            }

            PlcInstancePrefix = plcInstancePrefix;
            Group = group;
            VarA = varA;
            IoOwnerName = ioOwnerName;
            VarB = varB;
            SimpleKey = ComputeSimpleKey(varA);
        }

        static string ComputeSimpleKey(string varA)
        {
            if (varA.Contains("^"))
                return null;
            int firstDot = varA.IndexOf('.');
            if (firstDot < 0 || varA.IndexOf('.', firstDot + 1) >= 0)
                return null; // no dot, or more than one — nested through an FB instance
            return varA;
        }
    }

    /// <summary>
    /// Reads "links.xml" — the TwinCAT-native &lt;VarLinks&gt; format, the same schema produced
    /// by the XAE IDE's own "Export Variable Mapping" feature and consumed by its "Import
    /// Variable Mapping" counterpart. Confirmed against a real working reference project's own
    /// exported file (workspace root of PLC_NFL_SHARK_V2, "Spectrometer Instance Mappings.xml"):
    /// <code>
    /// &lt;VarLinks&gt;
    ///   &lt;OwnerA Name="InputDst" Prefix="TIPC^Spectrometer^Spectrometer Instance" Type="1"&gt;
    ///     &lt;OwnerB Name="TIID^Device 6 (EtherCAT)^Box 18 (MARPOSS P3XF STANDARD)"&gt;
    ///       &lt;Link VarA="MAIN.fbSpec.inLogicSig[1]" GrpA="PlcTask Inputs" TypeA="USINT"
    ///             InOutA="0" GuidA="{...}" VarB="Transmit PDO Mapping^OUT 001"/&gt;
    ///     &lt;/OwnerB&gt;
    ///   &lt;/OwnerA&gt;
    /// &lt;/VarLinks&gt;
    /// </code>
    /// OwnerA groups by PLC instance + direction (Type="1" = inputs into the PLC, "2" = outputs);
    /// OwnerB groups by IO box/device; each Link is one PLC-variable-to-PDO-entry pair.
    ///
    /// A second, older/hand-authored shape also exists in the wild (Beckhoff's own official
    /// CodeGenerationDemo sample, Templates/MachineTypeA/Links.xml): OwnerA has only a Name
    /// attribute (used as the prefix directly, no separate Prefix/Type), and Link has no GrpA
    /// -- the group is folded straight into VarA instead, e.g.
    /// VarA="PlcTask Outputs^MAIN.bError". Parse() below normalizes both shapes.
    ///
    /// Deliberately a thin reader: the file is applied to a live project by handing its RAW text
    /// straight to ITcSysManager.ConsumeMappingInfo (see Sync/VariableLinkEngine.cs's
    /// ApplyFromFile) — TwinCAT parses/applies the whole blob itself in one COM call, so this
    /// class's own Parse() exists only to support Sync/LinkChecker.cs's read-only cross-reference,
    /// not the sync path.
    /// </summary>
    static class VarLinksFile
    {
        // Native mapping import can silently discard unresolved variables. Read every
        // requested connection back, including offsets for packed digital outputs.
        internal static List<string> MissingMappings(string expected, string actual)
        {
            var have = MappingRows(XDocument.Parse(actual)).ToArray();
            var missing = new List<string>();
            foreach (var want in MappingRows(XDocument.Parse(expected)))
            {
                // TwinCAT can serialize a connection in either direction. Offsets
                // belong to endpoints and must move with them when A/B are exchanged.
                bool found = have.Any(row => (want.Size == null || row.Size == want.Size)
                    && ((row.A == want.A && row.B == want.B
                         && row.OffsetA == want.OffsetA && row.OffsetB == want.OffsetB)
                        || (row.A == want.B && row.B == want.A
                            && row.OffsetA == want.OffsetB && row.OffsetB == want.OffsetA)));
                if (!found) missing.Add(want.A + " <-> " + want.B);
            }
            return missing;
        }

        static IEnumerable<(string A, string B, int OffsetA, int OffsetB, int? Size)> MappingRows(XDocument doc)
        {
            if (doc.Root == null || doc.Root.Name != "VarLinks")
                throw new InvalidDataException("Expected a VarLinks mapping document.");
            foreach (var ownerA in doc.Root.Elements("OwnerA"))
                foreach (var ownerB in ownerA.Elements("OwnerB"))
                    foreach (var link in ownerB.Elements("Link"))
                    {
                        string prefix = (string)ownerA.Attribute("Prefix") ?? (string)ownerA.Attribute("Name");
                        string group = (string)link.Attribute("GrpA");
                        string a = (string)link.Attribute("VarA"), b = (string)link.Attribute("VarB");
                        string io = (string)ownerB.Attribute("Name");
                        if (new[] { prefix, io, a, b }.Any(string.IsNullOrWhiteSpace))
                            throw new System.IO.InvalidDataException("Mapping has an empty endpoint.");
                        yield return (prefix + "^" + (group == null ? "" : group + "^") + a, io + "^" + b,
                            (int?)link.Attribute("OffsA") ?? 0, (int?)link.Attribute("OffsB") ?? 0, (int?)link.Attribute("Size"));
                    }
        }

        /// <summary>Raw file content, unparsed — exactly what ConsumeMappingInfo expects. Null if
        /// "links.xml" doesn't exist (no-op for callers, same as an empty &lt;Links&gt; section).</summary>
        public static string LoadRawXml(string path) =>
            File.Exists(path) ? File.ReadAllText(path) : null;

        /// <summary>Empty (never null) list if "links.xml" doesn't exist.</summary>
        public static List<VarLinkEntry> Parse(string path)
        {
            var entries = new List<VarLinkEntry>();
            if (!File.Exists(path))
                return entries;

            XDocument doc = XDocument.Load(path);
            foreach (XElement ownerA in doc.Root.Elements("OwnerA"))
            {
                string prefix = (string)ownerA.Attribute("Prefix") ?? (string)ownerA.Attribute("Name");
                foreach (XElement ownerB in ownerA.Elements("OwnerB"))
                {
                    string ioOwnerName = (string)ownerB.Attribute("Name");
                    foreach (XElement link in ownerB.Elements("Link"))
                    {
                        entries.Add(new VarLinkEntry(
                            prefix,
                            (string)link.Attribute("GrpA"),
                            (string)link.Attribute("VarA"),
                            ioOwnerName,
                            (string)link.Attribute("VarB")));
                    }
                }
            }
            return entries;
        }
    }
}
