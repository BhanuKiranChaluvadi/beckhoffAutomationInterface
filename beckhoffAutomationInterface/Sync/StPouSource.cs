namespace BeckhoffAutomationInterface.Sync
{
    using System.Linq;
    using System.Text.RegularExpressions;

    /// <summary>
    /// A single PLC object's desired state, parsed from a .st source file:
    /// a PROGRAM, FUNCTION_BLOCK, FUNCTION, METHOD (of a FUNCTION_BLOCK), or a
    /// DUT (ENUM/STRUCT).
    /// </summary>
    class StPouSource
    {
        public string Name { get; }
        public PouKind Kind { get; }

        /// <summary>For Kind == Method: the name of the owning FUNCTION_BLOCK. Null otherwise.</summary>
        public string OwnerName { get; }

        public string DeclarationText { get; }

        /// <summary>Null for DUTs (ENUM/STRUCT/ALIAS), which have no implementation section.</summary>
        public string ImplementationText { get; }

        /// <summary>
        /// For Kind == AliasDut: the aliased base type (e.g. "LREAL"), required by
        /// TwinCAT's CreateChild as the vInfo parameter. For Kind == FunctionBlock
        /// or Interface: the EXTENDS target, if any (null otherwise).
        /// </summary>
        public string BaseType { get; }

        /// <summary>
        /// Source-relative folder path (forward-slash separated, e.g.
        /// "App/Shark/FunctionBlocks"), mirrored as PLC folders under the project
        /// root. Empty string for files directly in the source root. Set by the
        /// parser after construction.
        /// </summary>
        public string RelativeFolder { get; set; } = "";

        /// <summary>Bare file name of the originating .st file (e.g. "FB_Motor.st").
        /// Set by StFileParser.ParseFile on every source it emits — provenance for
        /// mapping build errors back to .st source (tasks/todo.md Tasks 5-6).</summary>
        public string SourceFileName { get; set; } = "";

        /// <summary>1-based line in the source file where this object's section header
        /// keyword (FUNCTION_BLOCK/METHOD/PROPERTY/PROGRAM/...) sits. 1 for whole-file
        /// kinds (DUT/GVL). Set by StFileParser.</summary>
        public int DeclarationStartLine { get; set; } = 1;

        /// <summary>1-based line in the source file where this object's implementation
        /// text begins (the first line after the declaration/END_VAR split). Null when
        /// the object has no implementation section (DUTs, GVLs, interfaces,
        /// properties, or an empty body). Set by StFileParser.</summary>
        public int? ImplementationStartLine { get; set; }

        /// <summary>For Kind == Property: 1-based line where the GET accessor's body
        /// begins (the line after the GET keyword). Null when there is no getter.</summary>
        public int? GetStartLine { get; set; }

        /// <summary>For Kind == Property: 1-based line where the SET accessor's body
        /// begins. Null when there is no setter.</summary>
        public int? SetStartLine { get; set; }

        /// <summary>Source-root-relative path of the originating file, forward-slash
        /// separated (e.g. "App/Shark/FunctionBlocks/FB_Motor.st").</summary>
        public string SourceRelativePath =>
            string.IsNullOrEmpty(RelativeFolder) ? SourceFileName : RelativeFolder + "/" + SourceFileName;

        /// <summary>For Kind == Property: the GET accessor body (null if the property has no
        /// getter). Terminators (GET/END_GET) are already stripped by the parser.</summary>
        public string GetText { get; }

        /// <summary>For Kind == Property: the SET accessor body (null if the property has no
        /// setter).</summary>
        public string SetText { get; }

        public StPouSource(string name, PouKind kind, string ownerName, string declarationText, string implementationText, string baseType = null,
            string getText = null, string setText = null)
        {
            Name = name;
            Kind = kind;
            OwnerName = ownerName;
            DeclarationText = StripPouTerminators(declarationText);
            ImplementationText = StripPouTerminators(implementationText);
            BaseType = baseType;
            GetText = getText;
            SetText = setText;
        }

        // The outer POU/METHOD terminator keywords are part of a whole-file textual view but
        // are NOT stored in TwinCAT's separated declaration/implementation model (they're
        // implicit) — leaving them in the text is a syntax error. END_VAR / END_STRUCT /
        // END_TYPE and control-flow terminators (END_IF/END_CASE/END_WHILE/END_FOR/
        // END_REPEAT) are real ST and must be kept, so only these POU-level ones are removed.
        static readonly Regex PouTerminatorLine = new Regex(
            @"^\s*END_(?:FUNCTION_BLOCK|INTERFACE|METHOD|FUNCTION|PROGRAM|ACTION|PROPERTY)\s*$",
            RegexOptions.IgnoreCase | RegexOptions.Multiline);

        static string StripPouTerminators(string text)
        {
            if (text == null) return null;
            string[] kept = text.Replace("\r\n", "\n").Split('\n')
                .Where(line => !PouTerminatorLine.IsMatch(line))
                .ToArray();
            return string.Join("\r\n", kept).Trim();
        }

        public bool IsDut => Kind == PouKind.EnumDut || Kind == PouKind.StructDut || Kind == PouKind.AliasDut;
        public bool IsMethod => Kind == PouKind.Method;
        public bool IsProperty => Kind == PouKind.Property;
        public bool IsGvl => Kind == PouKind.Gvl;
    }
}
