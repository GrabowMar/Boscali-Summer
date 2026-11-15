namespace BoscaliSummer.Modules.Wing.Domain
{
    /// <summary>Each WMC sub-page's standing order: one short line on the console's bay cover when the page ends above the footer
    /// (<c>AvConsole.Orders</c>).</summary>
    internal static class GuideWords
    {
        public static readonly string[] Orders =
        {
            "Pick who listens first: a table row, ALL or an element letter. The footer names the scope.",
        };

        public static readonly string[] Formation =
        {
            "The shape is the scope element's; stack moves the whole wing.",
        };

        public static readonly string[] Route =
        {
            "DRAW, then right-click the map to drop points; SEND TO WING flies them.",
        };

        public static readonly string[] Behaviour =
        {
            "Tap a profile to fly it; TUNE changes the scope's AI at once.",
        };

        public static readonly string[] Supply =
        {
            "Tap an airframe tile, then work down: pilot, stores and fuel, launch base. Then REQUISITION at the top.",
        };
    }
}
