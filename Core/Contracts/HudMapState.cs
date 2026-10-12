namespace BoscaliSummer.Core.Contracts
{
    /// <summary>
    /// What the HUD's bottom-left corner shows, shared by the module that draws the minimised map's frame (Hud: the TAC MAP brackets, caption and north tick) and the one that may put a larger map hologram in that corner
    /// (Command). A plain flag the two read and write on the main thread; nothing is persisted. Client-local.
    /// </summary>
    internal static class HudMapState
    {
        /// <summary>
        /// A map hologram has taken over the bottom-left corner: the TAC MAP dressing (brackets, caption shelf, north tick) is hidden while this is true. Default false. The owner of the hologram sets it when the hologram
        /// is up and clears it when it goes; the dressing reads it every frame, so the corner is never dressed twice.
        /// </summary>
        public static bool HoloActive;
    }
}
