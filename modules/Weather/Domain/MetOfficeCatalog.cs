namespace BoscaliSummer.Modules.Weather.Domain
{
    /// <summary>What the host can do in the weather console: set a sky (a tile, or the previous / next sky of the class on show), hand the weather back to the mission, re-roll the layout.</summary>
    internal enum MetControl : byte { Pin, SkyPrev, SkyNext, Auto, Reroll }

    /// <summary>The fixed words of the window: control names and help sentences (one footer line each), class codes and the read-out tips.</summary>
    internal static class MetCatalog
    {
        public const string DtgHelp = "DTG is the real UTC clock (cosmetic). T+ is the mission clock.";
        public const string RoleHostHelp = "HOST // ALL PLAYERS: a sky you set reaches every player at once.";
        public const string RoleClientHelp = "READ ONLY: only the host steers the weather. You see the same window with every control off.";
        public const string ClassHelp = "CLASS: shows the skies of this class. Browsing changes nothing; click a sky to set it.";

        /// <summary>Class codes and words, in <see cref="WeatherRegimeType"/> order.</summary>
        public static readonly string[] ClassCodes = { "CLR", "FEW", "SCT", "BKN", "OVC", "RA+", "TS" };
        public static readonly string[] ClassWords = { "CLEAR", "FAIR", "SCATTERED", "BROKEN", "OVERCAST", "RAIN", "STORM" };

        public static string Name(MetControl id) => id switch
        {
            MetControl.Pin => "SKY",
            MetControl.SkyPrev => "PREVIOUS SKY",
            MetControl.SkyNext => "NEXT SKY",
            MetControl.Auto => "AUTO",
            _ => "RE-ROLL",
        };

        public static string Help(MetControl id) => id switch
        {
            MetControl.Pin => "SKY: click to set this sky over the whole theater for every player. Rain, wind and storms follow the sky. AUTO hands it back.",
            MetControl.SkyPrev => "PREVIOUS SKY of the class on show, set at once (key: left arrow).",
            MetControl.SkyNext => "NEXT SKY of the class on show, set at once (key: right arrow).",
            MetControl.Auto => "AUTO: the mission's own weather again. Clears a set sky and anything a mission script forced (key: backspace).",
            _ => "RE-ROLL: same weather, new places for the clouds and storms (key: R).",
        };
    }
}
