namespace BoscaliSummer.Modules.Wing.Domain
{
    /// <summary>Each WMC sub-page's standing orders: how to use the page, most important first. They print on the console's bay
    /// cover when the page ends above the footer (<c>AvConsole.Orders</c>), which drops trailing lines that do not fit.</summary>
    internal static class GuideWords
    {
        public static readonly string[] Orders =
        {
            "Pick who listens first: a table row, ALL or an element letter. The footer names the scope.",
            "Rows run ATK · DEF · MOVE · AREA · SUP · WING. TGT keys want an enemy, PT a map point, AREA an area.",
            "In the cockpit: wing key + a key's letter gives that order; wing key + 1-6 switches profile.",
            "SHIFT + a map order queues a step; RUN on the QUEUE row flies the queue as a plan.",
        };

        public static readonly string[] Formation =
        {
            "The shape is the scope element's; spacing and stack move the whole wing.",
            "MEMBERS shows each slot's error in metres; phases run CUTOFF · PRE-SLOT · IN SLOT.",
            "ALL SHAPES › opens every shape card. Spacing ticks are the old presets.",
        };

        public static readonly string[] Route =
        {
            "DRAW, then right-click the map to drop points. UNDO takes the last one back.",
            "Select a point to set its altitude, speed and action; AUTO lets the wing choose.",
            "ONCE, LOOP or PING-PONG sets how the route repeats; SEND TO WING flies it.",
            "NAV flies your own aircraft along the route; AP OFF hands it back.",
        };

        public static readonly string[] Behaviour =
        {
            "Tap a profile to fly it: STRIKE for a bombing run, INTERCEPT for bandits, SILENT to slip in unseen.",
            "TUNE changes the scope's AI at once; the outlined choice is the profile's own. SAVE NEW keeps your tuning.",
            "Wing key + 1-6 flies a profile from the cockpit; give the lit one a key with WING KEY.",
        };

        public static readonly string[] Supply =
        {
            "Tap an airframe tile, then work down: pilot, stores and fuel, launch base. Then REQUISITION at the top.",
            "A requisitioned aircraft joins the wing once it takes off; the strip counts the inbound.",
            "STORES: AUTO arms it for the role, YOUR LOADOUT copies yours, CUSTOM is the LOADOUT build (EDIT LOADOUT ›).",
            "Airframes and fields list once you are flying. In multiplayer the host requisitions.",
        };

        public static readonly string[] Loadout =
        {
            "Tap any airframe tile to edit its one custom loadout (CUSTOM tiles already have one).",
            "Click a hardpoint, then a store; the first change saves the standard stores as your own.",
            "FLY IT ON SUPPLY sends this airframe and loadout to SUPPLY. Saved on this PC; in multiplayer only the host edits.",
            "MY AIRCRAFT is the jet you fly: choose an engine map, then land to apply it.",
        };
    }
}
