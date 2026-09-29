using BepInEx.Configuration;
using BoscaliSummer.Framework.Contracts;
using BoscaliSummer.Features.Hud.Domain;

namespace BoscaliSummer.Features.Hud.Configuration
{
    /// <summary>
    /// The common HUD element's pilot-facing knobs. Client-local presentation only: nothing
    /// here changes what the element is told, who is authoritative, or anything on the wire.
    /// Since the 2026-09-28 minimal rebuild the element hangs at one fixed dock below the
    /// native weapon panel, so there is no anchor/corner/declutter/classic ladder here any more.
    /// </summary>
    internal sealed class HudSettings
    {
        public readonly ConfigEntry<bool> Enabled;
        public readonly ConfigEntry<int> ScaleStep;
        public readonly ConfigEntry<int> OpacityStep;
        public readonly ConfigEntry<int> MaxRows;
        public readonly ConfigEntry<bool> Notices;
        public readonly ConfigEntry<float> NoticeSeconds;
        public readonly ConfigEntry<int> Contrast;
        public readonly ConfigEntry<bool> ShowDetails;
        public readonly ConfigEntry<int> OffsetX, OffsetY;

        /// <summary>
        /// The feeds this pilot has switched off, comma separated. Stored as one key rather
        /// than one key per feed because the feeds are declared by whichever features are
        /// installed, and a config key per feature would hard-code this module's consumer list.
        ///
        /// ponytail: comma-separated string, swap for a bounded key list if a feed key ever
        /// needs a comma. Feed keys are plain identifiers today.
        /// </summary>
        public readonly ConfigEntry<string> DisabledChannels;

        /// <summary>Show the native target camera feed inset while a target is selected. Same
        /// key as the retired third-person HUD so pilot preference survives the module move.</summary>
        public readonly ConfigEntry<bool> ThirdPersonCameraEnabled;

        /// <summary>Force the vanilla flight HUD (and, outside a maximized map, the minimap)
        /// visible while the local pilot's own live aircraft is viewed in the orbit or chase
        /// camera. Honoured by <c>ExternalHudEnabler</c>.</summary>
        public readonly ConfigEntry<bool> ExternalHud;

        /// <summary>Wingview: adjust the resting pose of the vanilla orbit camera for the local
        /// player's own aircraft (2026-09-28 wingview-third-person design). Orbit only; chase
        /// stays vanilla. Off restores stock orbit behaviour entirely.</summary>
        public readonly ConfigEntry<bool> Wingview;

        /// <summary>Turn-rate look-ahead on top of Wingview's own pose. Off holds the composition
        /// centred on the aircraft/course direction alone.</summary>
        public readonly ConfigEntry<bool> WingviewLookAhead;

        private readonly ChannelFilter channelFilter = new ChannelFilter();

        public HudSettings(ConfigFile config)
        {
            Enabled = config.Bind("Hud", "Enabled", true,
                "Draw the common cockpit HUD element: feature status lines and notices. " +
                "The features keep running with it off; only " +
                "their on-screen lines go away.");

            ScaleStep = config.Bind("Hud", "ScaleStep", 1,
                new ConfigDescription(
                    "Text size, from COMPACT to HUGE, using the independent overlay " +
                    "scale.",
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

            Contrast = config.Bind("Hud", "Contrast", 1,
                new ConfigDescription("Status backdrop: clear, glass, solid.", new AcceptableValueRange<int>(0, 2)));

            ShowDetails = config.Bind("Hud", "ShowDetails", true,
                "Show supporting status text and progress bars.");

            OffsetX = config.Bind("Hud", "OffsetX", 0,
                new ConfigDescription("Status horizontal adjustment in reference pixels.", new AcceptableValueRange<int>(-600, 600)));
            OffsetY = config.Bind("Hud", "OffsetY", 0,
                new ConfigDescription("Status vertical adjustment in reference pixels.", new AcceptableValueRange<int>(-600, 600)));

            DisabledChannels = config.Bind("Hud", "DisabledChannels", "",
                "Feeds switched off from the HUD settings page. Managed by the panel; edit " +
                "only if you want to force a feed back on.");

            // Retain the existing key so moving ownership onto IHudBoard does not reset preference.
            ThirdPersonCameraEnabled = config.Bind("Avionics", "ThirdPersonCameraEnabled", true,
                "Show the native target camera feed inset on the status panel while a target is selected.");

            ExternalHud = config.Bind("Hud", "ExternalHud", true,
                "Show the vanilla HUD and the status panel in orbit and chase views.");

            Wingview = config.Bind("Hud", "Wingview", true,
                "Adjust the orbit camera's resting pose to lead the aircraft's course and turns. " +
                "Off restores the stock orbit camera exactly.");

            WingviewLookAhead = config.Bind("Hud", "WingviewLookAhead", true,
                "Lead the Wingview composition into a turn by the aircraft's yaw rate.");

            channelFilter.Parse(DisabledChannels.Value);
            // A config saved before the "Weather"→"weather" channel rename must keep muting it.
            if (ContainsExactToken(DisabledChannels.Value, "Weather"))
                DisabledChannels.Value = channelFilter.With("weather", false);
            DisabledChannels.SettingChanged += (_, __) => channelFilter.Parse(DisabledChannels.Value);
        }

        private static bool ContainsExactToken(string csv, string token)
        {
            if (string.IsNullOrEmpty(csv)) return false;
            string[] parts = csv.Split(',');
            for (int i = 0; i < parts.Length; i++)
                if (string.Equals(parts[i].Trim(), token, System.StringComparison.Ordinal)) return true;
            return false;
        }

        public bool ChannelEnabled(string key) => channelFilter.Enabled(key);

        public void SetChannel(string key, bool enabled)
        {
            if (string.IsNullOrEmpty(key)) return;
            string value = channelFilter.With(key, enabled);
            if (DisabledChannels.Value == value) return;
            DisabledChannels.Value = value;
        }
    }
}
