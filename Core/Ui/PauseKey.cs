using System.Collections.Generic;

namespace BoscaliSummer.Core.Ui
{
    /// <summary>
    /// The game's pause key, held off by named owners (WMC's text fields and map tool, Command's armed map tools and card) so Esc
    /// cancels what they show instead of pausing. One saved value for all of them: the first holder in saves what the player had,
    /// the last one out puts it back, whatever order they come and go in (two save/restore holders interleaving left it off for
    /// good, 2026-10-29 review). Re-asserted on every hold call while anyone holds: the game turns it back on when a menu closes.
    /// ponytail: the full-screen windows (WindowInput, Met Office, contract desk) still save and restore on their own; join them
    /// here if one ever overlaps a map tool.
    /// </summary>
    internal static class PauseKey
    {
        private static readonly List<string> holders = new List<string>(4);
        private static bool applied, was;

        internal static void Hold(string owner, bool on)
        {
            int at = holders.IndexOf(owner);
            if (on && at < 0) holders.Add(owner);
            else if (!on && at >= 0) holders.RemoveAt(at);
            if (holders.Count > 0)
            {
                if (!applied)
                {
                    was = GameplayUI.AllowPauseKeybind;
                    applied = true;
                }
                GameplayUI.AllowPauseKeybind = false;
                return;
            }
            if (!applied) return;
            applied = false;
            GameplayUI.AllowPauseKeybind = was;
        }
    }
}
