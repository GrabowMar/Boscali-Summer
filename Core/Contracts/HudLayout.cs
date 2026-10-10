namespace BoscaliSummer.Core.Contracts
{
    /// <summary>
    /// The common HUD element's fixed presentation bounds, shared by the seam and its
    /// implementation. Nothing here is pilot-configurable since 2026-10-07.
    /// </summary>
    internal static class HudLayout
    {
        /// <summary>Feed cells shown at once, most severe first.</summary>
        public const int MaxRows = 4;
    }
}
