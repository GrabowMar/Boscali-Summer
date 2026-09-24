namespace BoscaliSummer.Framework.Contracts
{
    internal struct HudBounds
    {
        public float X, Y, Width, Height;
        public bool Visible => Width > 0 && Height > 0;
    }
    /// <summary>
    /// How loud one HUD line is. Decides its colour and where it sits in the stack.
    /// Kept free of Unity types so the layout and queue rules compile into the test runner.
    /// </summary>
    internal enum HudTone
    {
        /// <summary>Routine state the pilot asked to see. Vanilla's all-clear colour.</summary>
        Info,

        /// <summary>A condition worth acting on. Vanilla's warning colour.</summary>
        Caution,

        /// <summary>Immediate threat. Vanilla's alert colour, and it breathes.</summary>
        Warning,
    }

    /// <summary>Where the common HUD element hangs. Pure values; the board maps them to Unity.</summary>
    internal enum HudAnchor
    {
        /// <summary>
        /// Right-aligned directly under the vanilla weapon and capacitor column, at the column's
        /// own width. The default: it is the one place on the cockpit HUD where a short stack of
        /// lines can grow without ever meeting the pitch ladder.
        /// </summary>
        UnderWeapons,

        TopCentre,
        TopRight,
        TopLeft,
        MiddleRight,
        MiddleLeft,
        BottomRight,
        BottomLeft,
    }
}
