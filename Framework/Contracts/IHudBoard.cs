using System.Collections.Generic;

namespace BoscaliSummer.Framework.Contracts
{
    /// <summary>
    /// One named feed on the common HUD element. Declared by its owning feature and switched
    /// on and off by the pilot from SET.
    /// </summary>
    internal interface IHudChannel
    {
        string Key { get; }
        string Label { get; }
        bool Enabled { get; }
        void Toggle();
    }

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
    /// and transient notices through. The board owns the canvas, the independent typography and
    /// palette, the stacking, the bounds and every presentation setting; a feature owns only
    /// its own words and its own cell art.
    /// </summary>
    internal interface IHudBoard
    {
        /// <summary>
        /// Declare a feed. Idempotent; the first writer's label wins. A consumer calls this
        /// before acquiring a line on the channel.
        /// </summary>
        void DeclareChannel(string key, string label);

        /// <summary>
        /// The line for (owner, key), created on first call and the same line every call after,
        /// so a feature may call this every tick. Returns null once the board is at its ceiling
        /// of held lines.
        /// </summary>
        IHudLine Acquire(string owner, string channel, string key);

        /// <summary>
        /// Show one transient line for the pilot's notice time. Re-pushing the same channel and
        /// text refreshes the dwell instead of stacking a duplicate.
        /// </summary>
        void Notice(string channel, HudTone tone, string text, string detail = null);

        /// <summary>Drop every line one feature owns. Called by the owner on scene reset.</summary>
        void ReleaseOwner(string owner);

        // ------------------------------------------------------------------ presentation
        /// <summary>Draw the element at all. Off keeps every consumer running, silently.</summary>
        bool Enabled { get; set; }

        HudAnchor Anchor { get; set; }
        int ScaleStep { get; set; }
        int OpacityStep { get; set; }
        int MaxRows { get; set; }
        bool NoticesEnabled { get; set; }
        float NoticeSeconds { get; set; }
        int Contrast { get; set; }
        bool ShowDetails { get; set; }
        int OffsetX { get; set; }
        int OffsetY { get; set; }
        void ResetLayout();

        /// <summary>Every declared feed, in declaration order, for the settings page to list.</summary>
        IReadOnlyList<IHudChannel> Channels { get; }
    }
}
