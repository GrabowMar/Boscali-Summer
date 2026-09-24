namespace BoscaliSummer.Features.Progression.Domain
{
    /// <summary>
    /// A flavor title for the SQD pilot card, derived purely from this pilot's own score
    /// against the server's configured point budget. Display only: it grants nothing, is
    /// never sent over the wire, and never substitutes for vanilla rank or the perk budget
    /// it reads from.
    /// </summary>
    internal static class PilotTitleCatalog
    {
        private static readonly string[] Titles =
        {
            "ROOKIE",
            "SEASONED",
            "VETERAN",
            "ACE",
            "LEGEND",
        };

        /// <summary>
        /// Picks a title by how far this pilot's score sits inside the score span the budget
        /// represents (<paramref name="scorePerPoint"/> times <paramref name="maximumPoints"/>).
        /// A zero or unset budget reads as the base title rather than guessing a fraction.
        /// </summary>
        public static string TitleFor(int pilotScore, int scorePerPoint, int maximumPoints)
        {
            long span = (long)scorePerPoint * maximumPoints;
            if (span <= 0 || pilotScore <= 0) return Titles[0];

            float fraction = pilotScore / (float)span;
            if (fraction > 1f) fraction = 1f;

            int index = (int)(fraction * (Titles.Length - 1) + 0.5f);
            if (index < 0) index = 0;
            if (index >= Titles.Length) index = Titles.Length - 1;
            return Titles[index];
        }
    }
}
