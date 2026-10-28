namespace BoscaliSummer.Modules.Wing.Domain
{
    /// <summary>INSPECT's words (spec bezel v2 §5 INSPECT): the aircraft's title, its pilot line, the member chips and the line a
    /// page with nothing to inspect shows.</summary>
    internal static class InspectWords
    {
        public static string Chip(int slot) => WingRows.Number(slot);

        public static string Title(int slot, string callsign, string type)
        {
            string s = WingRows.Number(slot);
            if (!string.IsNullOrEmpty(callsign)) s += " " + callsign;
            if (!string.IsNullOrEmpty(type)) s += " · " + type;
            return s;
        }

        public static string Empty(bool client) => client
            ? "Wingman details come from the host."
            : "NO AIRCRAFT · INSPECT › ON TACTICAL, OR A MEMBER CHIP ABOVE";
    }
}
