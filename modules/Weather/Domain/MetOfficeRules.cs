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

    /// <summary>The enable and disable table of the brief (2.4) as one pure function, so a disabled control can never lack its reason.</summary>
    internal static class MetOfficeRules
    {
        public const string Client = "HOST CONTROLS THE WEATHER";
        public const string Waiting = "WAITING FOR MISSION ENVIRONMENT";
        public const string NoCamera = "NO CAMERA";
        public const string NoAnchor = "NO ANCHOR PLACED";

        public static MetState State(MetControl id, MetOfficeModel m)
        {
            MetControlInfo info = MetCatalog.Info(id);
            if (info.Avail == MetAvail.Tape4B) return new MetState(false, "ARRIVES WITH THE TAPE CONSOLE (4B)");
            if (info.Avail == MetAvail.Motion5) return new MetState(false, "ARRIVES WITH TAPE MOTION (S5)");
            if (!m.Host) return new MetState(false, Client);
            if (!m.Captured) return new MetState(false, Waiting);
            switch (id)
            {
                case MetControl.PaceDown: return m.Pace <= TapePace.MinPace ? new MetState(false, "SLOWEST PACE") : MetState.On;
                case MetControl.PaceUp: return m.Pace >= TapePace.MaxPace ? new MetState(false, "FASTEST PACE") : MetState.On;
                case MetControl.AnchorHere:
                case MetControl.AnchorAhead: return m.HasCamera ? MetState.On : new MetState(false, NoCamera);
                case MetControl.AnchorDefault: return m.HasAnchor ? MetState.On : new MetState(false, NoAnchor);
                default: return MetState.On;
            }
        }

        /// <summary>The footer line for a control: its help while enabled, its name and reason while not.</summary>
        public static string Tip(MetControl id, MetOfficeModel m)
        {
            MetState s = State(id, m);
            MetControlInfo info = MetCatalog.Info(id);
            // Controls that have not arrived already say so in their help; every other disabled control states its reason.
            if (s.Enabled || info.Avail != MetAvail.Live) return info.Help;
            return info.Name + " DISABLED: " + s.Why + ".";
        }

        /// <summary>The status line when no control is hovered.</summary>
        public static string Status(MetOfficeModel m)
        {
            if (!m.Host) return "READ ONLY: " + Client;
            if (!m.Captured) return Waiting;
            return "HOST // ALL PLAYERS · UNTIL THE TAPE LANDS EDITS FOLLOW THE LEGACY SKY STEP";
        }
    }
}
