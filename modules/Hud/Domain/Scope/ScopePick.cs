namespace BoscaliSummer.Modules.Hud.Domain
{
    /// <summary>What the board's three panels (RWR, FIRES, WING) and the left column's ROUTE face show (pure: it compiles into the unit tests). Nothing is chosen by the pilot: a panel is there while it has something to say.</summary>
    internal static class ScopePick
    {
        /// <summary>RWR draws its full scope while anything paints or chases us (the THREAT feed is live), and always with a missile inbound; else it is its header line alone (RWR ... CLEAR).</summary>
        public static bool RwrOpen(ushort liveMask, bool missileInbound) => missileInbound || CuePriority.Has(liveMask, ScopePage.Threat);

        /// <summary>FIRES hosts SHOT while own weapons fly (or a result still shows), else STRIKE while it is live; false: the panel is not drawn.</summary>
        public static bool Fires(ushort liveMask, out ScopePage host)
        {
            if (CuePriority.Has(liveMask, ScopePage.Shot)) host = ScopePage.Shot;
            else if (CuePriority.Has(liveMask, ScopePage.Strike)) host = ScopePage.Strike;
            else { host = ScopePage.Threat; return false; }
            return true;
        }

        /// <summary>The first live of APPROACH, DOCK, CARGO, OBJECTIVE; false when none.</summary>
        public static bool Route(ushort liveMask, out ScopePage face)
        {
            if (CuePriority.Has(liveMask, ScopePage.Approach)) face = ScopePage.Approach;
            else if (CuePriority.Has(liveMask, ScopePage.Dock)) face = ScopePage.Dock;
            else if (CuePriority.Has(liveMask, ScopePage.Cargo)) face = ScopePage.Cargo;
            else if (CuePriority.Has(liveMask, ScopePage.Objective)) face = ScopePage.Objective;
            else { face = ScopePage.Threat; return false; }
            return true;
        }
    }
}
