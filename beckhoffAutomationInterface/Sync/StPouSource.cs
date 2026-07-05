namespace BeckhoffAutomationInterface.Sync
{
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

        public StPouSource(string name, PouKind kind, string ownerName, string declarationText, string implementationText, string baseType = null)
        {
            Name = name;
            Kind = kind;
            OwnerName = ownerName;
            DeclarationText = declarationText;
            ImplementationText = implementationText;
            BaseType = baseType;
        }

        public bool IsDut => Kind == PouKind.EnumDut || Kind == PouKind.StructDut || Kind == PouKind.AliasDut;
        public bool IsMethod => Kind == PouKind.Method;
        public bool IsGvl => Kind == PouKind.Gvl;
    }
}
