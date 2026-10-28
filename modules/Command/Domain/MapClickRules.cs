namespace BoscaliSummer.Modules.Command.Domain
{
    /// <summary>Which contacts a selection box picks up.</summary>
    internal enum MapBoxSide { All, Hostile, Friendly }

    /// <summary>The plain-click rules of the map, as pure functions.</summary>
    internal static class MapClickRules
    {

        /// <summary>
        /// True when a selection box set to <paramref name="side"/> takes a contact. A contact with no faction is on
        /// nobody's side: only ALL takes it.
        /// </summary>
        public static bool BoxTakes(MapBoxSide side, bool hasFaction, bool own) =>
            side == MapBoxSide.All || (hasFaction && own == (side == MapBoxSide.Friendly));
    }
}
