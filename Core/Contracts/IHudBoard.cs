namespace BoscaliSummer.Core.Contracts
{
    /// <summary>
    /// One line held on the common HUD element for as long as its condition is true. The owning
    /// feature refreshes it from its own tick and releases it when the condition ends.
    /// </summary>
    internal interface IHudLine
    {
        /// <summary>
        /// Push this tick's reading. A line that stops being set goes stale and drops off the
        /// element, so a feature whose condition ends simply stops calling this.
        /// <paramref name="detail"/> and <paramref name="bar"/> are optional: null and 0.
        /// </summary>
        void Set(HudTone tone, string text, string detail, float bar);

        /// <summary>Take the line off the element. Idempotent, and safe on a stale line.</summary>
        void Release();
    }

    /// <summary>
    /// The one cockpit HUD element Boscali presentation features draw their held status lines
    /// and transient notices through. The board owns the look, the stacking and the bounds; a
    /// feature owns only its own words. How the board looks and what it shows is the pilot's
    /// (<c>ITacBoardSettings</c>); a feature never sees those options.
    /// </summary>
    internal interface IHudBoard
    {
        /// <summary>
        /// The line for (owner, key), created on first call and the same line every call after,
        /// so a feature may call this every tick. Returns null once the board is at its ceiling
        /// of held lines.
        /// </summary>
        IHudLine Acquire(string owner, string channel, string key);

        /// <summary>
        /// Show one transient notice on the board's one-line ticker. How long it stays depends on its tone (a Warning is
        /// sticky), and re-pushing the same channel and text bumps a count ("x3") and refreshes the dwell instead of
        /// stacking a duplicate.
        /// </summary>
        void Notice(string channel, HudTone tone, string text, string detail = null);

        /// <summary>
        /// The box the board covers on the screen, in device pixels from the screen's top-left corner (y down): false while the board is not drawn. A banner of another module that would sit across it steps
        /// aside (the ace-hunt panel does).
        /// </summary>
        bool TryGetScreenBox(out float left, out float top, out float right, out float bottom);
    }
}
