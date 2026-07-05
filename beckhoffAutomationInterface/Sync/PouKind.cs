namespace BeckhoffAutomationInterface.Sync
{
    /// <summary>The kind of PLC object a .st source file represents.</summary>
    enum PouKind
    {
        Program,
        FunctionBlock,
        Function,
        Interface,
        Method,
        Property,
        EnumDut,
        StructDut,
        AliasDut,
        Gvl
    }
}
