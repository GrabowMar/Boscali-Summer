using System;
using BoscaliSummer.Features.Command.Configuration;
using BoscaliSummer.Framework.Lifecycle;
using NOAvionics;
using NOAvionics.Ui;
using UnityEngine;

namespace BoscaliSummer.Features.Command.Presentation.MapUi
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
            AvFxDriver.GlitchSource = ReadGlitch;
            Apply();
        }

        // The vanilla fallback face is resolved per scene (it comes from the scene's MFD label).
        public void ResetForScene() => AvType.ResetScene();

        private void OnChanged(object sender, EventArgs e) => Apply();

        private void Apply()
        {
            if (settings == null) return;
            if (AvStyleHost.Theme != settings.AvionicsTheme.Value || AvStyleHost.FuiGeneration == 0)
                AvStyleHost.SetTheme(settings.AvionicsTheme.Value);
            AvFxDriver.Configure(settings.AvionicsFxTier.Value, settings.AvionicsReducedMotion.Value);
            AvBlurSource.Enabled = settings.AvionicsBlurBehind.Value;
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

        private void OnDestroy()
        {
            if (settings == null) return;
            settings.AvionicsTheme.SettingChanged -= OnChanged;
            settings.AvionicsFxTier.SettingChanged -= OnChanged;
            settings.AvionicsBlurBehind.SettingChanged -= OnChanged;
            settings.AvionicsReducedMotion.SettingChanged -= OnChanged;
            if (AvFxDriver.GlitchSource == (Func<float>)ReadGlitch) AvFxDriver.GlitchSource = null;
        }
    }
}
