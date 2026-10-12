using NOAvionics;
using System;
using BoscaliSummer.Modules.Command.Configuration;
using BoscaliSummer.Core.Game;
using BoscaliSummer.Core.Lifecycle;
using UnityEngine;

namespace BoscaliSummer.Modules.Command.Presentation.MapUi
{
    /// <summary>
    /// Applies the Avionics.* keys (theme, FX tier, blur-behind, reduced motion) to the shared kit and feeds
    /// the EMP/jam panel glitch from the local aircraft's radar jamming. Owned by Command, which owns the SET
    /// console and the display-glass settings. A scene service so rollback destroys it and it unsubscribes.
    /// </summary>
    internal sealed class AvionicsSettingsBridge : MonoBehaviour, ISceneService
    {
        private CommandSettings settings;
        private float jam;
        private float nextPoll;

        public void Configure(CommandSettings commandSettings)
        {
            settings = commandSettings;
            settings.AvionicsTheme.SettingChanged += OnChanged;
            settings.AvionicsFxTier.SettingChanged += OnChanged;
            settings.AvionicsBlurBehind.SettingChanged += OnChanged;
            settings.AvionicsReducedMotion.SettingChanged += OnChanged;
            settings.AvionicsCustomKeyHue.SettingChanged += OnChanged;
            settings.AvionicsCustomGlassHue.SettingChanged += OnChanged;
            settings.AvionicsCustomGlassTint.SettingChanged += OnChanged;
            settings.DisplayLife.SettingChanged += OnChanged;
            AvFxDriver.GlitchSource = ReadGlitch;
            AvAmbient.Maker = "BOSCALI AVIONICS WORKS";
            AvAmbient.Status = ReadStatus;
            Apply();
        }

        // The vanilla fallback face is resolved per scene (it comes from the scene's MFD label).
        public void ResetForScene() => AvType.ResetScene();

        private void OnChanged(object sender, EventArgs e) => Apply();

        private void Apply()
        {
            if (settings == null) return;
            string custom = AvThemes.CustomPalette(settings.AvionicsCustomKeyHue.Value,
                settings.AvionicsCustomGlassHue.Value, settings.AvionicsCustomGlassTint.Value);
            bool customChanged = custom != AvStyleHost.CustomPalette;
            AvStyleHost.CustomPalette = custom;
            AvThemeId theme = settings.AvionicsTheme.Value;
            if (AvStyleHost.Theme != theme || AvStyleHost.FuiGeneration == 0 || (customChanged && theme == AvThemeId.Custom))
                AvStyleHost.SetTheme(theme);
            AvFxDriver.Configure(settings.AvionicsFxTier.Value, settings.AvionicsReducedMotion.Value);
            AvBlurSource.Enabled = settings.AvionicsBlurBehind.Value;
            AvDisplayGlass.ConfigureLife(settings.DisplayLife.Value);
        }

        // Called once per frame by AvFxDriver's updater; polls the radar at 10 Hz and decays smoothly.
        private float ReadGlitch()
        {
            if (Time.unscaledTime >= nextPoll)
            {
                nextPoll = Time.unscaledTime + 0.1f;
                bool jammed = GameManager.GetLocalAircraft(out Aircraft aircraft) && aircraft != null
                              && aircraft.radar is Radar radar && radar != null && radar.IsJammed();
                jam = jammed ? 1f : Mathf.Max(0f, jam - 0.25f);
            }
            return jam * 0.6f;
        }

        // Idle footer column on every console: mission clock, local time of day, and which end of the link this is.
        private static string ReadStatus(int line)
        {
            switch (line)
            {
                case 0:
                    MissionManager mission = NetworkSceneSingleton<MissionManager>.i;
                    return mission != null ? "T+" + AvNum.Clock(mission.MissionTime) : "";
                case 1:
                    LevelInfo level = NetworkSceneSingleton<LevelInfo>.i;
                    if (level == null) return "";
                    float hours = Mathf.Repeat(level.timeOfDay, 24f);
                    int whole = (int)hours;
                    return whole.ToString("00") + ":" + ((int)((hours - whole) * 60f)).ToString("00") + " LOCAL";
                default:
                    return GameAccess.IsServer() ? "NET HOST" : "NET CLIENT";
            }
        }

        private void OnDestroy()
        {
            if (AvAmbient.Status == (Func<int, string>)ReadStatus) AvAmbient.Status = null;
            if (settings == null) return;
            settings.AvionicsTheme.SettingChanged -= OnChanged;
            settings.AvionicsFxTier.SettingChanged -= OnChanged;
            settings.AvionicsBlurBehind.SettingChanged -= OnChanged;
            settings.AvionicsReducedMotion.SettingChanged -= OnChanged;
            settings.AvionicsCustomKeyHue.SettingChanged -= OnChanged;
            settings.AvionicsCustomGlassHue.SettingChanged -= OnChanged;
            settings.AvionicsCustomGlassTint.SettingChanged -= OnChanged;
            settings.DisplayLife.SettingChanged -= OnChanged;
            if (AvFxDriver.GlitchSource == (Func<float>)ReadGlitch) AvFxDriver.GlitchSource = null;
        }
    }
}
