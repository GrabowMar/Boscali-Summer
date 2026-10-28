namespace BoscaliSummer.Modules.Weather.Domain
{
    /// <summary>Whether a control can be pressed now and, when not, the one phrase that says why (the footer shows it on hover).</summary>
    internal readonly struct MetState
    {
        public readonly bool Enabled;
        public readonly string Why;
        public MetState(bool enabled, string why) { Enabled = enabled; Why = why ?? ""; }
        public static readonly MetState On = new MetState(true, "");
    }

    /// <summary>The enable table as one pure function, so a disabled control can never lack its reason.</summary>
    internal static class MetOfficeRules
    {
        public const string Client = "HOST CONTROLS THE WEATHER";
        public const string Waiting = "WAITING FOR MISSION ENVIRONMENT";

        public static MetState State(MetControl id, MetOfficeModel m)
        {
            if (!m.Host) return new MetState(false, Client);
            if (!m.Captured) return new MetState(false, Waiting);
            return id == MetControl.Auto && m.PinnedSky < 0 && !m.Forced ? new MetState(false, "THE MISSION'S WEATHER IS ON") : MetState.On;
        }

        /// <summary>The footer line for a control: its help while enabled, its name and reason while not.</summary>
        public static string Tip(MetControl id, MetOfficeModel m)
        {
            MetState s = State(id, m);
            return s.Enabled ? MetCatalog.Help(id) : MetCatalog.Name(id) + " DISABLED: " + s.Why + ".";
        }

        /// <summary>The status line when no control is hovered.</summary>
        public static string Status(MetOfficeModel m)
        {
            if (!m.Host) return "READ ONLY: " + Client;
            if (!m.Captured) return Waiting;
            return m.PinnedSky >= 0 ? "SKY #" + (m.PinnedSky + 1) + " SET FOR ALL PLAYERS · AUTO HANDS IT BACK TO THE MISSION" : "MISSION WEATHER · CLICK A SKY TO SET IT FOR ALL PLAYERS";
        }
    }
}
