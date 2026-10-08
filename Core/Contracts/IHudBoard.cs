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
    /// feature owns only its own words. Since 2026-10-07 nothing about the element is
    /// configurable: it ships one tuned out-of-the-box presentation.
    /// </summary>
    internal interface IHudBoard
    {
        /// <summary>Name a feed. Kept so publishers stay unchanged; the board shows every feed.</summary>
        void DeclareChannel(string key, string label);

        /// <summary>
        /// The line for (owner, key), created on first call and the same line every call after,
        /// so a feature may call this every tick. Returns null once the board is at its ceiling
        /// of held lines.
        /// </summary>
        IHudLine Acquire(string owner, string channel, string key);

        /// <summary>
        /// Show one transient line for <see cref="HudLayout.NoticeSeconds"/>. Re-pushing the same
        /// channel and text refreshes the dwell instead of stacking a duplicate.
        /// </summary>
        void Notice(string channel, HudTone tone, string text, string detail = null);
    }
}
