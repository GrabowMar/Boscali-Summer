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
            "In the cockpit: wing key + a key's letter gives that order; wing key + 1-6 switches stance.",
            "SHIFT + a map order queues a step; BEHAVIOUR › SORTIE runs the queue as a plan.",
        };

        public static readonly string[] Formation =
        {
            "The shape is the scope element's; spacing and stack move the whole wing.",
            "MEMBERS shows each slot's error and closure; phases run CUTOFF › PRE-SLOT › IN SLOT.",
            "ALL SHAPES › opens every shape card. Spacing ticks are the old presets.",
        };

        public static readonly string[] Route =
        {
            "DRAW, then right-click the map to drop points. UNDO takes the last one back.",
            "Select a point to set its altitude, speed and action; AUTO lets the wing choose.",
            "ONCE, LOOP or PING-PONG sets how the route repeats; SEND TO WING flies it.",
            "SAVE keeps a route for another sortie; NAV flies your own aircraft along it.",
        };

        public static readonly string[] Stances =
        {
            "A stance is a whole rulebook: what to hit, how far, which weapons, radar, defence and after-action.",
            "Pick one, read its card, APPLY. Built-ins are read-only: SAVE AS makes a copy you can edit.",
            "Slots W+1 to W+6 are the cockpit keys: wing key + number switches stance in flight.",
            "LIVE DOCTRINE edits what the scope flies right now, without touching the book.",
        };

        public static readonly string[] Sortie =
        {
            "Build the plan on ORDERS (SHIFT + a map order adds a step), then EXECUTE here.",
            "Dashed bars are the plan, solid bars what really happened. Tap a bar for its step.",
            "SKIP STEP moves the wing on; ABORT drops the plan. EVENTS is the radio log per element.",
        };

        public static readonly string[] Supply =
        {
            "Work top to bottom: pilot, airframe, fit and fuel, launch base. Then REQUISITION.",
            "A requisitioned aircraft joins the wing once it takes off; the strip counts the inbound.",
            "FIT AUTO picks stores for the role; YOUR LOADOUT copies yours; EDIT › opens LOADOUT.",
            "Airframes and fields list once you are flying. In multiplayer the host requisitions.",
        };

        public static readonly string[] Loadout =
        {
            "A template is a saved fit for one airframe, not the aircraft in the air.",
            "Pick an airframe, NEW a template, then click a hardpoint to fit a store.",
            "SUPPLY step 3 flies a template. Templates are saved on this PC.",
        };

        public static readonly string[] Roster =
        {
            "Pilots keep rank, ribbons and perks between missions; XP opens the four perk slots.",
            "‹ › pages the roster; RECRUIT adds a pilot, STUDIO changes their look and identity.",
            "A downed pilot: AIR SAR sends a helicopter, LOCAL SAR waits them out. RELEASE lets one go.",
        };

        public static readonly string[] Aircraft =
        {
            "#2 to #7 pick one wingman: damage, fuel, stores and what it is doing now.",
            "CENTER puts the picked aircraft in the middle of the map.",
            "The host reports wingman details; a client sees them once they arrive.",
        };

        public static readonly string[] Studio =
        {
            "NEW starts a pilot; ‹ › picks one. SAVE keeps identity and look for every mission.",
            "LOOK steps the portrait; RANDOM rolls a callsign or name; GENERATE writes a bio.",
            "IMPORT, EXPORT and FOLDER share pilot files. Every pilot starts a mission a ROOKIE.",
        };
    }
}
