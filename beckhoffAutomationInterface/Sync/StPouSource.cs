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

        /// <summary>Null for DUTs (ENUM/STRUCT), which have no implementation section.</summary>
        public string ImplementationText { get; }

        public StPouSource(string name, PouKind kind, string ownerName, string declarationText, string implementationText)
        {
            Name = name;
            Kind = kind;
            OwnerName = ownerName;
            DeclarationText = declarationText;
            ImplementationText = implementationText;
        }

        public bool IsDut => Kind == PouKind.EnumDut || Kind == PouKind.StructDut;
        public bool IsMethod => Kind == PouKind.Method;
    }
}
