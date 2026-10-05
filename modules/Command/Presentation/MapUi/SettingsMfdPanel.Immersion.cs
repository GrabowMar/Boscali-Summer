using NOAvionics;
using System;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Core.Modules;
using BoscaliSummer.Core.Services;
using BoscaliSummer.Core.Ui;
using UnityEngine;

namespace BoscaliSummer.Modules.Command.Presentation.MapUi
{
    internal sealed partial class SettingsMfdPanel
    {
        private void BuildImmersionPage(AvFlow flow, int page)
        {
            IImmersionSettings immersion = ModuleServices.TryGet(out IImmersionSettings svc) ? svc : null;
            if (immersion == null)
            {
                flow.Section(AvIcon.WaveSine, "COCKPIT IMMERSION", "UNAVAILABLE");
                flow.Add(new NoteLine(flow.Content)).Set("IMMERSION MODULE NOT INSTALLED");
                return;
            }

            Func<bool> masterOn = () => immersion.IsEnabled;
            Func<string> needMaster = () => "Turn on IMMERSION MASTER first.";

            flow.Section(AvIcon.WaveSine, "COCKPIT IMMERSION", "MASTER SWITCH");
            AvCellGrid masterGrid = flow.Grid(2);
            ToggleCell(flow, masterGrid, page, "IMMERSION MASTER",
                "Master switch for all client-side cockpit immersion features (physics, vibrations, shaders, and audio).",
                () => immersion.IsEnabled, v => immersion.IsEnabled = v);
            ToggleCell(flow, masterGrid, page, "COMFORT MOTION",
                "Reduce all added cockpit rotation to 25% and remove idle breathing. Individual switches still work.",
                () => immersion.ComfortMotionEnabled, v => immersion.ComfortMotionEnabled = v,
                masterOn, needMaster);

            flow.Section(AvIcon.Activity, "COCKPIT MOTION", "HEAD · SHAKES · BUFFET");
            AvCellGrid motionGrid = flow.Grid(3);
            ToggleCell(flow, motionGrid, page, "HEAD MOTION",
                "Cockpit view: rotational head inertia under G-load, roll lead, yaw lead, and breathing motion.",
                () => immersion.HeadMotionEnabled, v => immersion.HeadMotionEnabled = v,
                masterOn, needMaster);
            ToggleCell(flow, motionGrid, page, "EXTRA SHAKE",
                "Cockpit view: dynamic vibrations for gunfire recoil, touchdowns, and runway roll.",
                () => immersion.ExtraShakeEnabled, v => immersion.ExtraShakeEnabled = v,
                masterOn, needMaster);
            ToggleCell(flow, motionGrid, page, "MACH BUFFET",
                "Cockpit view: transonic Mach aerodynamic buffeting vibrations (Mach 0.86 - 1.12) and sound barrier bump.",
                () => immersion.MachBuffetEnabled, v => immersion.MachBuffetEnabled = v,
                masterOn, needMaster);

            var motionRings = flow.Add(new SetRingRow(flow.Content));
            Ring(flow, motionRings, page, "HEAD STRENGTH",
                () => AvNum.Percent(immersion.HeadMotionStrength),
                () => Mathf.Clamp01(immersion.HeadMotionStrength / 2f),
                d => immersion.HeadMotionStrength = Mathf.Clamp(Mathf.Round((immersion.HeadMotionStrength + d * 0.1f) * 10f) / 10f, 0f, 2.0f),
                () => immersion.HeadMotionStrength > 0.001f,
                () => immersion.HeadMotionStrength < 1.999f,
                "Strength of cockpit head inertia under G-forces and angular rates.",
                () => immersion.IsEnabled && immersion.HeadMotionEnabled,
                () => !immersion.IsEnabled ? needMaster() : "Turn on HEAD MOTION first.");
            Ring(flow, motionRings, page, "SHAKE STRENGTH",
                () => AvNum.Percent(immersion.ShakeStrength),
                () => Mathf.Clamp01(immersion.ShakeStrength / 2f),
                d => immersion.ShakeStrength = Mathf.Clamp(Mathf.Round((immersion.ShakeStrength + d * 0.1f) * 10f) / 10f, 0f, 2.0f),
                () => immersion.ShakeStrength > 0.001f,
                () => immersion.ShakeStrength < 1.999f,
                "Strength of extra camera vibrations for gunfire, touchdowns, and Mach buffet.",
                () => immersion.IsEnabled && (immersion.ExtraShakeEnabled || immersion.MachBuffetEnabled),
                () => !immersion.IsEnabled ? needMaster() : "Turn on EXTRA SHAKE or MACH BUFFET first.");

            flow.Section(AvIcon.Eye, "COCKPIT VISUALS", "GLARE · MFD GLOW · G-VIGNETTE");
            AvCellGrid visualGrid = flow.Grid(2);
            ToggleCell(flow, visualGrid, page, "SUN GLARE",
                "Data-driven lens flare when looking towards the sun, occluded by terrain, buildings, and clouds.",
                () => immersion.SunGlareEnabled, v => immersion.SunGlareEnabled = v,
                masterOn, needMaster);
            ToggleCell(flow, visualGrid, page, "MFD NIGHT GLOW",
                "Cockpit view: MFD glass night boost so instruments glow at dusk and night.",
                () => immersion.MfdGlowEnabled, v => immersion.MfdGlowEnabled = v,
                masterOn, needMaster);
            ToggleCell(flow, visualGrid, page, "G-VIGNETTE",
                "Physiological G-force visual effects: peripheral tunnel vision greyout under +G, redout under -G.",
                () => immersion.GVignetteEnabled, v => immersion.GVignetteEnabled = v,
                masterOn, needMaster);
            ToggleCell(flow, visualGrid, page, "DAMAGE SURFACES",
                "Subtle damage shading on supported opaque cockpit material slots. Weather owns glass rain and cold moisture.",
                () => immersion.SurfaceImmersionEnabled, v => immersion.SurfaceImmersionEnabled = v,
                masterOn, needMaster);

            flow.Section(AvIcon.Volume, "COCKPIT AUDIO", "AIRFRAME · WIND · G-FILTER · AGSM");
            AvCellGrid audioGrid = flow.Grid(2);
            ToggleCell(flow, audioGrid, page, "AIRFRAME CREAKS",
                "Cockpit view: procedural airframe creaks and structural groans under violent G onsets.",
                () => immersion.AirframeAudioEnabled, v => immersion.AirframeAudioEnabled = v,
                masterOn, needMaster);
            ToggleCell(flow, audioGrid, page, "G-AUDIO FILTER",
                "Cockpit view: dynamic auditory narrowing and helmet audio low-pass filtering under high G-forces.",
                () => immersion.GForceAudioEnabled, v => immersion.GForceAudioEnabled = v,
                masterOn, needMaster);
            ToggleCell(flow, audioGrid, page, "CANOPY WIND",
                "Cockpit view: procedural canopy wind rush and aerodynamic slipstream audio loop.",
                () => immersion.WindAudioEnabled, v => immersion.WindAudioEnabled = v,
                masterOn, needMaster);
            ToggleCell(flow, audioGrid, page, "PILOT STRAIN",
                "Cockpit view: pilot Anti-G Straining Maneuver (AGSM) pressurized breathing sounds under sustained high G.",
                () => immersion.PilotStrainAudioEnabled, v => immersion.PilotStrainAudioEnabled = v,
                masterOn, needMaster);

            flow.Section(AvIcon.Refresh, "IMMERSION ACTIONS", "RESET TO DEFAULTS");
            AvControl resetBtn = flow.Buttons(new AvControl.Spec("RESET IMMERSION SETTINGS", () =>
            {
                immersion.IsEnabled = true;
                immersion.HeadMotionEnabled = true;
                immersion.HeadMotionStrength = 1f;
                immersion.ExtraShakeEnabled = true;
                immersion.ShakeStrength = 1f;
                immersion.ComfortMotionEnabled = false;
                immersion.MachBuffetEnabled = true;
                immersion.SunGlareEnabled = true;
                immersion.MfdGlowEnabled = true;
                immersion.GVignetteEnabled = true;
                immersion.SurfaceImmersionEnabled = true;
                immersion.AirframeAudioEnabled = true;
                immersion.GForceAudioEnabled = true;
                immersion.WindAudioEnabled = true;
                immersion.PilotStrainAudioEnabled = true;
                Echo("Immersion settings reset to defaults.");
                Changed();
            }, AvButtonStyle.Default, AvIcon.Refresh)).Controls[0];
            resetBtn.Help = "Restore all cockpit immersion toggles and sliders to recommended defaults.";
        }
    }
}
