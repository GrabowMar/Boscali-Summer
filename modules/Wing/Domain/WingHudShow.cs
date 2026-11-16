namespace BoscaliSummer.Modules.Wing.Domain
{
    /// <summary>When the Wing module's HUD strip is up: the roster rows repeat the TAC BOARD's WING page, so they give way to it; the autopilot line and the held-key Call Ladder do not.</summary>
    internal static class WingHudShow
    {
        public static bool ShouldShow(bool hudUp, int members, bool boardShowsWing, bool autopilot, bool chordHeld) =>
            hudUp && ((members > 0 && !boardShowsWing) || autopilot || chordHeld);
    }
}
