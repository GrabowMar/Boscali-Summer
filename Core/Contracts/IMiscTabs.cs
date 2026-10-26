namespace BoscaliSummer.Core.Contracts
{
    /// <summary>
    /// Tabs another module adds to the MISC console, in order, ahead of DEBUG (ARES: SPAWN, UNITS, POWERS, CHEATS). Optional: with no
    /// provider MISC shows only its own tabs.
    /// </summary>
    internal interface IMiscTabs
    {
        int Count { get; }
        NOAvionics.AvIcon Icon(int tab);
        string Label(int tab);
        string Title(int tab);
        string Help(int tab);

        /// <summary>Fills <paramref name="tab"/>'s page (console page index <paramref name="page"/>). Called once at build.</summary>
        void Build(int tab, int page, NOAvionics.AvFlow flow, NOAvionics.AvConsole console);
    }
}
