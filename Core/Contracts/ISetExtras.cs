namespace BoscaliSummer.Core.Contracts
{
    /// <summary>One tab another module adds to the MISC console. Optional: with no provider MISC has no such tab.</summary>
    internal interface ISetExtras
    {
        string ExtraTabLabel { get; }
        string ExtraTabTitle { get; }

        /// <summary>Fills the MISC tab's page. Called once at build.</summary>
        void BuildExtraPage(int page, NOAvionics.AvFlow flow, NOAvionics.AvConsole console);
    }
}
