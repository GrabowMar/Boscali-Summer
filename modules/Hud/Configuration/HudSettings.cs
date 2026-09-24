using BepInEx.Configuration;
using UnityEngine;
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

        public ConfigEntry<bool> ModifyVanillaHud { get; }
        public ConfigEntry<bool> ThirdPersonHudEnabled { get; }
        public ConfigEntry<KeyCode> ThirdPersonHudKey { get; }
        public ConfigEntry<bool> ThirdPersonHidePitchLadder { get; }
        public ConfigEntry<bool> ThirdPersonCameraEnabled { get; }
        public ConfigEntry<bool> ThirdPersonFlightCameraEnabled { get; }
        public ConfigEntry<bool> ThirdPersonShotsEnabled { get; }

        public readonly ConfigEntry<int> FlightScaleStep, FlightOpacityStep, FlightContrast;
        public readonly ConfigEntry<bool> BoardEnabled, AirframeEnabled, MarkEnabled, ShowDetails;
        public readonly ConfigEntry<int> BoardCorner, BoardScaleStep, BoardOpacityStep, BoardContrast;
        public readonly ConfigEntry<int> BoardInsetX, BoardInsetY, Contrast, OffsetX, OffsetY;

        public HudSettings(ConfigFile config)
        {
            ModifyVanillaHud = config.Bind("Hud", "ModifyVanillaHud", false, "Opt in to replacement flight instruments, native HUD visibility changes and marker reprojection in external view. Independent mod panels remain available with this off.");
            FlightScaleStep = Step(config, "FlightScaleStep", 1, 3, "Flight readout size: compact, normal, large, huge.");
            FlightOpacityStep = Step(config, "FlightOpacityStep", 0, 3, "Flight readout opacity: full, high, low, off.");
            FlightContrast = Step(config, "FlightContrast", 1, 2, "Flight readout backing: clear, glass, solid.");
            BoardEnabled = config.Bind("Hud", "BoardEnabled", true, "Show the external-view instrument board.");
            AirframeEnabled = config.Bind("Hud", "AirframeEnabled", true, "Show factual counts of damaged/detached parts and active native failure indications. No aggregate health score.");
            MarkEnabled = config.Bind("Hud", "MarkEnabled", true, "Show the current camera observation summary on the board.");
            ShowDetails = config.Bind("Hud", "ShowDetails", true, "Show supporting status text and progress bars.");
            BoardCorner = Step(config, "BoardCorner", 0, 3, "0 bottom right, 1 bottom left, 2 top right, 3 top left.");
            BoardScaleStep = Step(config, "BoardScaleStep", 1, 3, "Board size: compact, normal, large, huge.");
            BoardOpacityStep = Step(config, "BoardOpacityStep", 0, 3, "Board opacity: full, high, low, off.");
            BoardContrast = Step(config, "BoardContrast", 1, 2, "Backdrop: clear, glass, solid.");
            Contrast = Step(config, "Contrast", 1, 2, "Status backdrop: clear, glass, solid.");
            BoardInsetX = Step(config, "BoardInsetX", 0, 600, "Move the board inward horizontally, in reference pixels.");
            BoardInsetY = Step(config, "BoardInsetY", 0, 600, "Move the board inward vertically, in reference pixels.");
            OffsetX = config.Bind("Hud", "OffsetX", 0, new ConfigDescription("Status horizontal adjustment in reference pixels.", new AcceptableValueRange<int>(-600, 600)));
            OffsetY = config.Bind("Hud", "OffsetY", 0, new ConfigDescription("Status vertical adjustment in reference pixels.", new AcceptableValueRange<int>(-600, 600)));

            // Retain the existing keys so moving ownership does not reset user preferences.
            ThirdPersonHudEnabled = config.Bind("Avionics", "ThirdPersonHudEnabled", true,
                "Show mod-owned HUD panels in external orbit and chase camera views.");
            ThirdPersonCameraEnabled = config.Bind("Avionics", "ThirdPersonCameraEnabled", true,
                "Show the native target camera feed only while targets are selected in third person; no additional world rendering.");
            ThirdPersonFlightCameraEnabled = config.Bind("Avionics", "ThirdPersonFlightCameraEnabled", true,
                "Smooth aircraft-relative orbit/rear chase framing with a steady horizon and room above the aircraft for aiming. Native zoom, look-at and other chase presets remain available.");
            ThirdPersonShotsEnabled = config.Bind("Avionics", "ThirdPersonShotsEnabled", true,
                "Show live missile shots on the third-person board: own missiles with range and ETA to their targets, plus inbound missiles from the aircraft's own warning system.");
            ThirdPersonHudKey = config.Bind("Avionics", "ThirdPersonHudKey", KeyCode.F7,
                "Hotkey to toggle third-person HUD visibility on the fly.");
            ThirdPersonHidePitchLadder = config.Bind("Avionics", "ThirdPersonHidePitchLadder", true,
                "Declutter: hide the floating pitch ladder in third person while keeping reticle, ammo, and radar.");

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

            DisabledChannels = config.Bind("Hud", "DisabledChannels", "",
                "Feeds switched off from the HUD settings page. Managed by the panel; edit " +
                "only if you want to force a feed back on.");
        }

        private static ConfigEntry<int> Step(ConfigFile config, string key, int value, int max, string help) =>
            config.Bind("Hud", key, value, new ConfigDescription(help, new AcceptableValueRange<int>(0, max)));

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
