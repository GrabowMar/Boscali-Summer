using System;
using BoscaliSummer.Framework.Contracts;
using BoscaliSummer.Framework.Features;
using NOAvionics.Ui;
using UnityEngine;

namespace BoscaliSummer.Features.Command.Presentation.MapUi
{
    internal sealed partial class SettingsMfdPanel
    {
        private void BuildVisualsPage(RectTransform parent, Rect body)
        {
            ModServices.TryGet(out IVisualEnhancements visuals);
            parent = Page(5, parent, body, 7, 3, out var area);

            Func<bool> available = () => visuals != null && visuals.IsEnabled;
            Func<string> unavailable = () => "Visual enhancements module is disabled or unavailable.";

            Heading(parent, ref area, "01", "POST-PROCESSING", "CINEMATIC");
            Toggle(parent, TakeRow(ref area), "CINEMATIC POST-FX",
                "Enable cinematic URP post-processing (ACES tonemapping, bloom, calibrated color grading, subtle film grain).",
                () => visuals != null && visuals.CinematicPostFxEnabled,
                v => { if (visuals != null) visuals.CinematicPostFxEnabled = v; },
                available, unavailable);

            Func<bool> postFxOn = () => available() && visuals.CinematicPostFxEnabled;
            Func<string> postFxOff = () => "Enable CINEMATIC POST-FX first.";
            Percent(parent, TakeRow(ref area), "BLOOM INTENSITY",
                () => visuals?.BloomIntensity ?? 0.4f,
                v => { if (visuals != null) visuals.BloomIntensity = v; },
                0f, 1f, 0.05f,
                postFxOn, postFxOff);

            Heading(parent, ref area, "02", "FLIGHT DYNAMICS", "PILOT OPTICS");
            Toggle(parent, TakeRow(ref area), "G-FORCE EFFECTS",
                "Pilot physiological G-force visual effects (tunnel vision / blackout on high positive G, redout on negative G).",
                () => visuals != null && visuals.GForceEffectsEnabled,
                v => { if (visuals != null) visuals.GForceEffectsEnabled = v; },
                available, unavailable);

            Toggle(parent, TakeRow(ref area), "TRANSONIC BLUR",
                "Dynamic transonic camera motion blur at high speeds (Mach 0.85+) and high roll rates.",
                () => visuals != null && visuals.MotionBlurEnabled,
                v => { if (visuals != null) visuals.MotionBlurEnabled = v; },
                available, unavailable);

            Heading(parent, ref area, "03", "ENVIRONMENT", "TERRAIN");
            Toggle(parent, TakeRow(ref area), "FOLIAGE DYNAMICS",
                "Enhanced wind sway and ambient dynamics on terrain vegetation and trees.",
                () => visuals != null && visuals.FoliageDynamicsEnabled,
                v => { if (visuals != null) visuals.FoliageDynamicsEnabled = v; },
                available, unavailable);

            Func<bool> foliageOn = () => available() && visuals.FoliageDynamicsEnabled;
            Func<string> foliageOff = () => "Enable FOLIAGE DYNAMICS first.";
            Multiplier(parent, TakeRow(ref area), "SWAY STRENGTH",
                () => visuals?.FoliageSwayStrength ?? 1.0f,
                v => { if (visuals != null) visuals.FoliageSwayStrength = v; },
                0.2f, 2.5f, 0.1f,
                foliageOn, foliageOff);
        }

        private void Percent(RectTransform parent, Rect area, string title, Func<float> read, Action<float> write,
            float min, float max, float step, Func<bool> enabled, Func<string> reason)
        {
            Stepper(parent, area, title, () => read().ToString("P0"),
                d => write(Mathf.Clamp(Mathf.Round((read() + d * step) * 100f) / 100f, min, max)),
                () => read() > min + .001f, () => read() < max - .001f,
                "Adjust " + title.ToLowerInvariant() + ".", enabled, reason);
        }

        private void Multiplier(RectTransform parent, Rect area, string title, Func<float> read, Action<float> write,
            float min, float max, float step, Func<bool> enabled, Func<string> reason)
        {
            Stepper(parent, area, title, () => read().ToString("0.0") + "x",
                d => write(Mathf.Clamp(Mathf.Round((read() + d * step) * 10f) / 10f, min, max)),
                () => read() > min + .01f, () => read() < max - .01f,
                "Adjust " + title.ToLowerInvariant() + ".", enabled, reason);
        }
    }
}
