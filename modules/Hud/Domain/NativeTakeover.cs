namespace BoscaliSummer.Modules.Hud.Domain
{
    /// <summary>
    /// Whether a native top-right object (the weapon panel, the countermeasure box, the capacitor bar) stays hidden behind the board's STORES HEADER,
    /// pure. The one rule is FAIL OPEN: the native object is hidden only while everything that replaces it is up and really drawing, and shown again the
    /// same frame any of it is not. <paramref name="boardAlive"/>: the board is built and its root is alive and visible. <paramref name="painted"/>: the
    /// header's last paint finished without an exception. <paramref name="hudUp"/>: the native HUD's icon layer is live. <paramref name="batchMode"/>: no
    /// board is ever built in a batch run. <paramref name="elementDrawn"/>: the header drew THIS object's replacement (a countermeasure box is never hidden on an
    /// aircraft whose dispensers the board could not read; the capacitor bar stays on an airframe the board shows no capacitor for).
    /// <para>The vanilla <c>PlayerSettings.hudWeapons</c> is deliberately not an input: it only gates the legacy text <c>WeaponIndicator</c> and
    /// <c>CountermeasureIndicator</c> apps (off by default), never the top-right panel; reading it would hide the header by default.</para>
    /// </summary>
    internal static class NativeTakeover
    {
        public static bool Hide(bool boardAlive, bool painted, bool hudUp, bool batchMode, bool elementDrawn) =>
            boardAlive && painted && hudUp && !batchMode && elementDrawn;

        /// <summary>A board whose last present is older than this (unscaled seconds; it is presented every 0.1 s) has stopped updating: what it shows is frozen.</summary>
        public const float StaleSeconds = 0.5f;

        public static bool Stale(float now, float presentedAt) => now - presentedAt > StaleSeconds;
    }
}
