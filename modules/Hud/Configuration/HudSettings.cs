using BepInEx.Configuration;
using BoscaliSummer.Framework.Contracts;

namespace BoscaliSummer.Features.Hud.Configuration
{
    /// <summary>
    /// The common HUD element's pilot-facing knobs. Client-local presentation only: nothing
    /// here changes what the element is told, who is authoritative, or anything on the wire.
    /// </summary>
    internal sealed class HudSettings
    {
        public readonly ConfigEntry<bool> Enabled;
        public readonly ConfigEntry<int> Anchor;
        public readonly ConfigEntry<int> ScaleStep;
        public readonly ConfigEntry<int> OpacityStep;
        public readonly ConfigEntry<int> MaxRows;
        public readonly ConfigEntry<bool> Notices;
        public readonly ConfigEntry<float> NoticeSeconds;

        /// <summary>
        /// The feeds this pilot has switched off, comma separated. Stored as one key rather
        /// than one key per feed because the feeds are declared by whichever features are
        /// installed, and a config key per feature would hard-code this module's consumer list.
        ///
        /// ponytail: comma-separated string, swap for a bounded key list if a feed key ever
        /// needs a comma. Feed keys are plain identifiers today.
        /// </summary>
        public readonly ConfigEntry<string> DisabledChannels;

        public HudSettings(ConfigFile config)
        {
            Enabled = config.Bind("Hud", "Enabled", true,
                "Draw the common cockpit HUD element: feature status lines and notices. " +
                "The features keep running with it off; only " +
                "their on-screen lines go away.");

            Anchor = config.Bind("Hud", "Anchor", 0,
                new ConfigDescription(
                    "Where the element hangs: " + AnchorChoices() + ".",
                    new AcceptableValueRange<int>(0, HudLayout.AnchorCount - 1)));

            ScaleStep = config.Bind("Hud", "ScaleStep", 1,
                new ConfigDescription(
                    "Text size, from COMPACT to HUGE, as a multiple of the game's own overlay " +
                    "text size setting.",
                    new AcceptableValueRange<int>(0, HudLayout.ScaleCount - 1)));

            OpacityStep = config.Bind("Hud", "OpacityStep", 1,
                new ConfigDescription(
                    "Opacity: FULL, HIGH, LOW, or OFF to hide the element without unloading it.",
                    new AcceptableValueRange<int>(0, HudLayout.OpacityCount - 1)));

            MaxRows = config.Bind("Hud", "MaxRows", 4,
                new ConfigDescription(
                    "How many lines the element may show at once.",
                    new AcceptableValueRange<int>(HudLayout.MinRows, HudLayout.MaxRows)));

            Notices = config.Bind("Hud", "Notices", true,
                "Show transient notices (ace hunt started, entering or leaving a contract area) " +
                "in the element.");

            NoticeSeconds = config.Bind("Hud", "NoticeSeconds", HudLayout.DefaultNoticeSeconds,
                new ConfigDescription(
                    "How long a transient notice stays up.",
                    new AcceptableValueRange<float>(HudLayout.MinNoticeSeconds, HudLayout.MaxNoticeSeconds)));

            DisabledChannels = config.Bind("Hud", "DisabledChannels", "",
                "Feeds switched off from the HUD settings page. Managed by the panel; edit " +
                "only if you want to force a feed back on.");
        }

        private static string AnchorChoices()
        {
            var text = new System.Text.StringBuilder();
            for (int i = 0; i < HudLayout.AnchorCount; i++)
            {
                if (i > 0) text.Append(", ");
                text.Append(i).Append('=').Append(HudLayout.AnchorName(i));
            }
            return text.ToString();
        }

        public HudAnchor ResolvedAnchor() => (HudAnchor)HudLayout.ClampAnchor(Anchor.Value);

        public bool ChannelEnabled(string key)
        {
            if (string.IsNullOrEmpty(key)) return true;
            string disabled = DisabledChannels.Value;
            if (string.IsNullOrEmpty(disabled)) return true;
            string[] parts = disabled.Split(',');
            for (int i = 0; i < parts.Length; i++)
                if (string.Equals(parts[i].Trim(), key, System.StringComparison.Ordinal)) return false;
            return true;
        }

        public void SetChannel(string key, bool enabled)
        {
            if (string.IsNullOrEmpty(key)) return;
            var kept = new System.Text.StringBuilder();
            string disabled = DisabledChannels.Value;
            if (!string.IsNullOrEmpty(disabled))
            {
                string[] parts = disabled.Split(',');
                for (int i = 0; i < parts.Length; i++)
                {
                    string part = parts[i].Trim();
                    if (part.Length == 0 || string.Equals(part, key, System.StringComparison.Ordinal)) continue;
                    if (kept.Length > 0) kept.Append(',');
                    kept.Append(part);
                }
            }
            if (!enabled)
            {
                if (kept.Length > 0) kept.Append(',');
                kept.Append(key);
            }
            string value = kept.ToString();
            if (DisabledChannels.Value == value) return;
            DisabledChannels.Value = value;
        }
    }
}
