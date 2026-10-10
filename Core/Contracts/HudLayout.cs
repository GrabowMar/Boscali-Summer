namespace BoscaliSummer.Core.Contracts
{
    /// <summary>
    /// The common HUD element's fixed presentation bounds, shared by the seam and its
    /// implementation. Nothing here is pilot-configurable since 2026-10-07.
    /// </summary>
    internal static class HudLayout
    {
        /// <summary>Lines shown at once. Notices and held lines share them, most severe first.</summary>
        public const int MaxRows = 4;

        /// <summary>How long a transient notice stays up.</summary>
        public const float NoticeSeconds = 8f;
    }
}
