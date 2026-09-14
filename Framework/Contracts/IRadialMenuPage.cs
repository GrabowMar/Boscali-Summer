namespace BoscaliSummer.Framework.Contracts
{
    /// <summary>
    /// One optional page a module contributes to the native cockpit radial wheel. The
    /// wheel host (Autopilot) owns the wheel, appearance and lifecycle; the contributor
    /// owns only labels, per-entry availability and the action each entry runs. Entries
    /// are read on demand, never held past the page's display.
    /// </summary>
    internal interface IRadialMenuPage
    {
        /// <summary>Short page name shown on the wheel, e.g. TARGET PRESETS.</summary>
        string Title { get; }

        /// <summary>Entry count for the next display. The host caps how many it draws.</summary>
        int EntryCount { get; }

        string EntryLabel(int index);

        /// <summary>False greys the entry for this display; the host still draws it.</summary>
        bool EntryAllowed(int index);

        void InvokeEntry(int index);
    }
}
