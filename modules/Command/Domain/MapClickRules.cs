namespace BoscaliSummer.Modules.Command.Domain
{
    /// <summary>The plain-click rules of the map, as pure functions.</summary>
    internal static class MapClickRules
    {
        /// <summary>
        /// True when a plain left click on bare map should clear the unit selection: the map is open, no other map control (an icon, an
        /// STR tag or card) took this frame's click, and shift is not held.
        /// </summary>
        public static bool ShouldClearSelection(bool open, int clickFrame, int frame, bool shift) =>
            open && clickFrame != frame && !shift;
    }
}
