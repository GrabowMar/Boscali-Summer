namespace BoscaliSummer.Framework.Contracts
{
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
}
