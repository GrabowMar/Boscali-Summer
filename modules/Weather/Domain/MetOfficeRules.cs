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

        /// <summary>The story the STORY stepper moves to: from AUTO the first (next) or the last (previous), otherwise one step with wrap-around.</summary>
        public static int StoryStep(int pick, int count, int direction)
        {
            int n = count < 1 ? 1 : count;
            if (pick < 0) return direction > 0 ? 0 : n - 1;
            return ((pick + direction) % n + n) % n;
        }

        public static MetState State(MetControl id, MetOfficeModel m)
        {
            MetControlInfo info = MetCatalog.Info(id);
            if (info.Avail == MetAvail.Motion5) return new MetState(false, "ARRIVES WITH TAPE MOTION (S5)");
            if (!m.Host) return new MetState(false, Client);
            if (!m.Captured) return new MetState(false, Waiting);
            return id switch
            {
                MetControl.PaceDown => m.Pace <= TapePace.MinPace ? new MetState(false, "SLOWEST PACE") : MetState.On,
                MetControl.PaceUp => m.Pace >= TapePace.MaxPace ? new MetState(false, "FASTEST PACE") : MetState.On,
                MetControl.AnchorHere or MetControl.AnchorAhead => m.HasCamera ? MetState.On : new MetState(false, NoCamera),
                MetControl.AnchorDefault => m.HasAnchor ? MetState.On : new MetState(false, NoAnchor),
                _ => MetState.On,
            };
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
            return "HOST // ALL PLAYERS · THE TAPE IS HELD UNTIL TAPE MOTION (S5)";
        }
    }
}
