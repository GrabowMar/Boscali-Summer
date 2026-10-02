using System;
using UnityEngine;

namespace NOAvionics
{
    /// <summary>
    /// Shared plumbing of the kit's live <c>.avss</c> sheet (the sheet itself, its themes and their overrides, is
    /// <c>AvStyleHost.Fui.cs</c>): where the player-editable overrides live, the loggers, and the turn of a declared paint into a
    /// Unity colour against the live game theme. Nothing here throws.
    /// </summary>
    public static partial class AvStyleHost
    {
        private static string configDir;
        private static Action<string> log;
        private static Action<string> warn;

        /// <summary>
        /// Point the host at this plugin's config directory and loggers.
        ///
        /// Called once at startup by each mod. Both mods may call it; last writer wins and
        /// they agree on the path, which is the point — one sheet, both panels.
        /// </summary>
        public static void Configure(string bepInExConfigDir, Action<string> info, Action<string> warning)
        {
            log = info;
            warn = warning;
            configDir = string.IsNullOrEmpty(bepInExConfigDir) ? null : bepInExConfigDir;
        }

        private static void Info(string message)
        {
            if (log != null) log(message);
        }

        private static void Warn(string message)
        {
            if (warn != null) warn(message);
            else if (log != null) log(message);
        }

        // ---------------------------------------------------------------- resolution

        /// <summary>
        /// Turn a declared paint into a colour, resolving live theme references against
        /// whatever the player's mission theme currently is.
        /// </summary>
        public static Color Resolve(AvPaint paint, Color fallback)
        {
            switch (paint.Kind)
            {
                case AvColorRef.Fixed:
                    return AvTheme.Unity(paint.Value);

                case AvColorRef.Accent: return WithAlpha(AvTheme.Accent, paint.Alpha);
                case AvColorRef.Friendly: return WithAlpha(AvTheme.Friendly, paint.Alpha);
                case AvColorRef.Warning: return WithAlpha(AvTheme.Warning, paint.Alpha);
                case AvColorRef.Alert: return WithAlpha(AvTheme.Alert, paint.Alpha);
                case AvColorRef.Hostile: return WithAlpha(AvTheme.Hostile, paint.Alpha);
                case AvColorRef.Neutral: return WithAlpha(AvTheme.Neutral, paint.Alpha);
                case AvColorRef.Selected: return WithAlpha(AvTheme.Selected, paint.Alpha);

                default:
                    return fallback;
            }
        }

        private static Color WithAlpha(Color c, float a) => new Color(c.r, c.g, c.b, a);
    }
}
