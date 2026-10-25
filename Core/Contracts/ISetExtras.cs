namespace BoscaliSummer.Core.Contracts
{
    /// <summary>
    /// Extra content another module adds: rows at the end of SET's PILOT pages, and one tab of its own on the MISC console.
    /// Optional: with no provider SET is unchanged and MISC has no such tab.
    /// </summary>
    internal interface ISetExtras
    {
        /// <summary>Called once per PILOT page (0 DISPLAY, 1 MAP, 2 COCKPIT, 3 IMMERSION, 4 PERF) after Command built it, appending at the end of the flow.</summary>
        void BuildPilotPage(int page, NOAvionics.AvFlow flow, NOAvionics.AvConsole console);

        /// <summary>The MISC tab: label, title, and whether it shows now (polled at the Slow tick).</summary>
        string ExtraTabLabel { get; }
        string ExtraTabTitle { get; }
        bool ExtraTabShown { get; }

        /// <summary>Fills the MISC tab's page. Called once at build, even while the tab is hidden.</summary>
        void BuildExtraPage(int page, NOAvionics.AvFlow flow, NOAvionics.AvConsole console);
    }
}
