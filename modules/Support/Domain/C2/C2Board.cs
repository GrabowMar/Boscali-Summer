using System.Globalization;

namespace BoscaliSummer.Modules.Support.Domain.C2
{
    /// <summary>The words of the [5] BOARD page. Pure; no game types.</summary>
    internal static class C2Board
    {
        public const int Rows896 = 8, Rows596 = 5;

        public static string Title(int live, int stale) =>
            "LIVE POSTS · " + live.ToString(CultureInfo.InvariantCulture) + (stale > 0 ? " · STALE " + stale.ToString(CultureInfo.InvariantCulture) : "");

        public static string Meta(int hidden) => hidden > 0 ? "FIRST CLAIM WINS \u00B7 +" + hidden.ToString(CultureInfo.InvariantCulture) + " MORE" : "FIRST CLAIM WINS";

        public const string Empty = "NO LIVE POSTS · POSTED TASKED CALLS APPEAR HERE";

        public static string QuietWord(bool quiet) => quiet ? "QUIET MODE · ON" : "QUIET MODE · OFF";

        public static string QuietHelp(bool quiet) => quiet
            ? "TASKED and ENEMY INTENT notices are silent. Inbound warnings are never silenced."
            : "TASKED and ENEMY INTENT notices show a line and a chime. Press to silence them. Inbound warnings stay audible.";

        public static string QuietButton(bool quiet) => quiet ? "NOTIFY" : "QUIET";

        /// <summary>The footer words when nothing more urgent is said: a ready board and how a claim works.</summary>
        public static string Ready(bool any) => any ? "FIRST CLAIM WINS · CLAIM ARMS, EXECUTE FIRES" : "BOARD CLEAR · WAITING FOR A POSTED CALL";
    }
}
