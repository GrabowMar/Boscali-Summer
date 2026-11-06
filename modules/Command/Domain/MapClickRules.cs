namespace BoscaliSummer.Modules.Command.Domain
{
    /// <summary>Which contacts a selection box picks up.</summary>
    internal enum MapBoxSide { All, Hostile, Friendly }

    /// <summary>What a left drag on the map does to the selection.</summary>
    internal enum MapBoxMode { None, Add, Drop }

    /// <summary>Who a right click on the map belongs to, first claim first (rule B, 2026-10-29).</summary>
    internal enum MapRightClick { Tool, Wing, Native, Card }

    /// <summary>The plain-click rules of the map, as pure functions.</summary>
    internal static class MapClickRules
    {
        /// <summary>Ctrl drags a box that drops; Shift drags one that adds. With DRAG set to BOXES a plain drag boxes and Shift pans.</summary>
        public static MapBoxMode BoxMode(bool ctrl, bool shift, bool dragBoxes) =>
            ctrl ? MapBoxMode.Drop : shift != dragBoxes ? MapBoxMode.Add : MapBoxMode.None;

        /// <summary>An armed tool places, then a WMC wingman selection moves, then a friendly commandable selection takes the
        /// game's own waypoint; only a right click nobody claims opens the context card.</summary>
        public static MapRightClick RightClick(bool toolArmed, bool wingTakes, bool nativeCommand) =>
            toolArmed ? MapRightClick.Tool : wingTakes ? MapRightClick.Wing : nativeCommand ? MapRightClick.Native : MapRightClick.Card;

        /// <summary>
        /// True when a selection box set to <paramref name="side"/> takes a contact. A contact with no faction is on
        /// nobody's side: only ALL takes it.
        /// </summary>
        public static bool BoxTakes(MapBoxSide side, bool hasFaction, bool own) =>
            side == MapBoxSide.All || (hasFaction && own == (side == MapBoxSide.Friendly));
    }
}
