namespace BoscaliSummer.Modules.Wing.Domain
{
    /// <summary>WING's AIRCRAFT words (spec bezel v2 §5): the aircraft's title, the member chips, the footer hint and the lines a page
    /// with nothing to show says.</summary>
    internal static class InspectWords
    {
        public const string Lost = "AIRCRAFT LOST OR LEFT THE WING · PICK ANOTHER WITH A MEMBER CHIP ABOVE";

        public static string Chip(int slot) => WingRows.Number(slot);

        public static string Title(int slot, string type)
        {
            string s = WingRows.Number(slot);
            if (!string.IsNullOrEmpty(type)) s += " · " + type;
            return s;
        }

        public static string Empty(bool client) => client
            ? "Wingman details come from the host."
            : "NO AIRCRAFT · PICK A WINGMAN ON TACTICAL, OR A MEMBER CHIP ABOVE";

        public static string Hint(bool client) => client
            ? "Wingman details come from the host."
            : "A chip picks the aircraft; CENTER finds it on the map; DOSSIER › opens its pilot's file.";
    }
}
