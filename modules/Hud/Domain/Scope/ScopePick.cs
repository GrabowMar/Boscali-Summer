namespace BoscaliSummer.Modules.Hud.Domain
{
    /// <summary>Which page SCOPE slot 0 shows in Auto mode (pure: it compiles into the unit tests). The caller keeps the shown page and when it came up.</summary>
    internal static class ScopePick
    {
        public const float MinShowS = 1.5f;

        /// <summary>THREAT if a missile is inbound; else the first live of APPROACH, SHOT, STRIKE; else THREAT.</summary>
        public static ScopePage Top(ushort liveMask, bool missileInbound)
        {
            if (missileInbound) return ScopePage.Threat;
            if (CuePriority.Has(liveMask, ScopePage.Approach)) return ScopePage.Approach;
            if (CuePriority.Has(liveMask, ScopePage.Shot)) return ScopePage.Shot;
            if (CuePriority.Has(liveMask, ScopePage.Strike)) return ScopePage.Strike;
            return ScopePage.Threat;
        }

        /// <summary>Keeps <paramref name="shown"/> until it has been up <see cref="MinShowS"/>; an inbound missile switches at once.</summary>
        public static ScopePage Hold(ScopePage shown, ScopePage wanted, float shownForS, bool missileInbound) =>
            missileInbound || shownForS >= MinShowS ? wanted : shown;

        /// <summary>The first live of DOCK, CARGO, OBJECTIVE; false when none.</summary>
        public static bool Route(ushort liveMask, out ScopePage face)
        {
            if (CuePriority.Has(liveMask, ScopePage.Dock)) face = ScopePage.Dock;
            else if (CuePriority.Has(liveMask, ScopePage.Cargo)) face = ScopePage.Cargo;
            else if (CuePriority.Has(liveMask, ScopePage.Objective)) face = ScopePage.Objective;
            else { face = ScopePage.Threat; return false; }
            return true;
        }
    }
}
