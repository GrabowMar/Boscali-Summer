namespace BoscaliSummer.Modules.Command.Domain
{
    /// <summary>Which contacts a selection box picks up.</summary>
    internal enum MapBoxSide { All, Hostile, Friendly }

    /// <summary>The plain-click rules of the map, as pure functions.</summary>
    internal static class MapClickRules
    {
        /// <summary>
        /// True when a plain left click on bare map should clear the unit selection: the map is open, no other map control (an icon, an
        /// STR tag or card) took this frame's click, and shift is not held.
        /// </summary>
        public static bool ShouldClearSelection(bool open, int clickFrame, int frame, bool shift) =>
            open && clickFrame != frame && !shift;

        /// <summary>
        /// True when a selection box set to <paramref name="side"/> takes a contact. A contact with no faction is on
        /// nobody's side: only ALL takes it.
        /// </summary>
        public static bool BoxTakes(MapBoxSide side, bool hasFaction, bool own) =>
            side == MapBoxSide.All || (hasFaction && own == (side == MapBoxSide.Friendly));
    }
}
