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
            ModServices.TryGet(out IImmersionSettings immersion);
            parent = Page(5, parent, body, 12, 4, out var area);

            Func<bool> available = () => visuals != null && visuals.IsEnabled;
            Func<string> unavailable = () => "Visual enhancements module is off (F1 > Modules).";

            Heading(parent, ref area, "01", "IMAGE", "SCREEN");
            Toggle(parent, TakeRow(ref area), "CINEMATIC GRADE",
                "Filmic grade over the game's own tonemapping: more contrast, cool shadows, warm highlights, fine grain.",
                () => visuals != null && visuals.CinematicPostFxEnabled,
                v => { if (visuals != null) visuals.CinematicPostFxEnabled = v; },
                available, unavailable);

            Func<bool> gradeOn = () => available() && visuals.CinematicPostFxEnabled;
            Func<string> gradeOff = () => "Enable CINEMATIC GRADE first.";
            Percent(parent, TakeRow(ref area), "BLOOM BOOST",
                () => visuals?.BloomBoost ?? 1f,
                v => { if (visuals != null) visuals.BloomBoost = v; },
                0.5f, 2.5f, 0.05f,
                gradeOn, gradeOff);

            Toggle(parent, TakeRow(ref area), "SHARPEN",
                "AMD contrast-adaptive sharpening at native resolution: crisper distant aircraft and cockpit text.",
                () => visuals != null && visuals.SharpenEnabled,
                v => { if (visuals != null) visuals.SharpenEnabled = v; },
                available, unavailable);

            Func<bool> sharpenOn = () => available() && visuals.SharpenEnabled;
            Func<string> sharpenOff = () => "Enable SHARPEN first.";
            Percent(parent, TakeRow(ref area), "SHARPEN STRENGTH",
                () => visuals?.SharpenStrength ?? 0f,
                v => { if (visuals != null) visuals.SharpenStrength = v; },
                0.1f, 1f, 0.1f,
                sharpenOn, sharpenOff);

            Heading(parent, ref area, "02", "FLIGHT", "PILOT OPTICS");
            Toggle(parent, TakeRow(ref area), "G EFFECTS",
                "Cockpit view: red-out under negative G and lens fringing under heavy G, on top of the game's own blackout.",
                () => visuals != null && visuals.GForceEffectsEnabled,
                v => { if (visuals != null) visuals.GForceEffectsEnabled = v; },
                available, unavailable);

            Heading(parent, ref area, "03", "ENVIRONMENT", "WIND");
            Toggle(parent, TakeRow(ref area), "TREE & GRASS SWAY",
                "Trees sway and grass waves with the map's wind.",
                () => visuals != null && visuals.FoliageDynamicsEnabled,
                v => { if (visuals != null) visuals.FoliageDynamicsEnabled = v; },
                available, unavailable);

            Func<bool> foliageOn = () => available() && visuals.FoliageDynamicsEnabled;
            Func<string> foliageOff = () => "Enable TREE & GRASS SWAY first.";
            Multiplier(parent, TakeRow(ref area), "SWAY STRENGTH",
                () => visuals?.FoliageSwayStrength ?? 1.0f,
                v => { if (visuals != null) visuals.FoliageSwayStrength = v; },
                0.2f, 2.5f, 0.1f,
                foliageOn, foliageOff);

            BuildImmersionRows(parent, ref area, immersion);
        }

        private void BuildImmersionRows(RectTransform parent, ref Rect area, IImmersionSettings immersion)
        {
            Func<bool> available = () => immersion != null && immersion.IsEnabled;
            Func<string> unavailable = () => "Immersion module is off (F1 > Modules).";

            Heading(parent, ref area, "04", "COCKPIT FEEL", "IMMERSION");
            Toggle(parent, TakeRow(ref area), "HEAD MOTION",
                "Cockpit view: the head dips under G, leans with side force and leads into rolls and turns.",
                () => immersion != null && immersion.HeadMotionEnabled,
                v => { if (immersion != null) immersion.HeadMotionEnabled = v; },
                available, unavailable);

            Func<bool> headOn = () => available() && immersion.HeadMotionEnabled;
            Func<string> headOff = () => "Enable HEAD MOTION first.";
            Multiplier(parent, TakeRow(ref area), "HEAD STRENGTH",
                () => immersion?.HeadMotionStrength ?? 1f,
                v => { if (immersion != null) immersion.HeadMotionStrength = v; },
                0.2f, 2f, 0.1f,
                headOn, headOff);

            Toggle(parent, TakeRow(ref area), "GUN & GROUND SHAKE",
                "Cockpit view: your own guns, touchdowns and the runway roll shake the camera.",
                () => immersion != null && immersion.ExtraShakeEnabled,
                v => { if (immersion != null) immersion.ExtraShakeEnabled = v; },
                available, unavailable);

            Func<bool> shakeOn = () => available() && immersion.ExtraShakeEnabled;
            Func<string> shakeOff = () => "Enable GUN & GROUND SHAKE first.";
            Multiplier(parent, TakeRow(ref area), "SHAKE STRENGTH",
                () => immersion?.ShakeStrength ?? 1f,
                v => { if (immersion != null) immersion.ShakeStrength = v; },
                0.2f, 2f, 0.1f,
                shakeOn, shakeOff);

            Toggle(parent, TakeRow(ref area), "SUN GLARE",
                "Lens flare when looking towards the sun; hidden behind terrain and cloud.",
                () => immersion != null && immersion.SunGlareEnabled,
                v => { if (immersion != null) immersion.SunGlareEnabled = v; },
                available, unavailable);
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
