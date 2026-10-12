namespace BoscaliSummer.Core.Contracts
{
    /// <summary>
    /// The common HUD element's fixed presentation bounds, shared by the seam and its
    /// implementation. These bounds are fixed; the pilot's options are <c>ITacBoardSettings</c>.
    /// </summary>
    internal static class HudLayout
    {
        /// <summary>Feed cells shown at once, most severe first.</summary>
        public const int MaxRows = 4;
    }
}
