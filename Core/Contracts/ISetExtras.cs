namespace BoscaliSummer.Core.Contracts
{
    /// <summary>Extra content another module adds to SET's PILOT console. Optional: with no provider SET is unchanged.</summary>
    internal interface ISetExtras
    {
        /// <summary>Called once per PILOT page (0 DISPLAY, 1 MAP, 2 COCKPIT, 3 IMMERSION, 4 PERF) after Command built it, appending at the end of the flow.</summary>
        void BuildPilotPage(int page, NOAvionics.AvFlow flow, NOAvionics.AvConsole console);

        /// <summary>The extra tab: label, title, and whether it shows now (polled at the Slow tick).</summary>
        string ExtraTabLabel { get; }
        string ExtraTabTitle { get; }
        bool ExtraTabShown { get; }

        /// <summary>Fills the extra tab's page (index 5). Called once at build, even while the tab is hidden.</summary>
        void BuildExtraPage(int page, NOAvionics.AvFlow flow, NOAvionics.AvConsole console);
    }
}
