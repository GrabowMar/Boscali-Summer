using System;
using BoscaliSummer.Modules.Immersion.Patches;
using BoscaliSummer.Modules.Immersion.Runtime;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Core.Modules;
using BoscaliSummer.Core.Services;
using BoscaliSummer.Core.Ui;

namespace BoscaliSummer.Modules.Immersion
{
    internal sealed class ImmersionModule : IModule
    {
        private static readonly ModuleMetadata Module =
            new ModuleMetadata("immersion", "Immersion");

        private static readonly Type[] Patches =
        {
            typeof(CockpitHeadRotationPatch),
            typeof(CockpitHeadResetPatch),
            typeof(GunShotShakePatch)
        };

        public ModuleMetadata Metadata => Module;
        public Type[] PatchTypes => Patches;

        public void Install(ModuleContext context)
        {
            ImmersionManager immersion = context.AddSceneService<ImmersionManager>(47);
            immersion.Configure(context.Settings.Immersion, context.Logger);
            context.AddClientEffect(immersion);
            context.AddService<IImmersionSettings>(immersion);

            context.AddClientSetting("IMMERSION", "IMMERSION MASTER",
                "Master switch for all client-side cockpit immersion features.",
                context.Settings.Immersion.Enabled);
            context.AddClientSetting("IMMERSION", "HEAD MOTION",
                "Cockpit view: rotational head inertia under G-load, roll lead, yaw lead, and breathing motion.",
                context.Settings.Immersion.HeadMotionEnabled);
            context.AddClientSetting("IMMERSION", "EXTRA SHAKE",
                "Cockpit view: dynamic vibrations for gunfire recoil, touchdowns, and runway roll.",
                context.Settings.Immersion.ExtraShakeEnabled);
            context.AddClientSetting("IMMERSION", "MACH BUFFET",
                "Cockpit view: transonic Mach aerodynamic buffeting vibrations (Mach 0.86 - 1.12) and sound barrier crossing bump.",
                context.Settings.Immersion.MachBuffetEnabled);
            context.AddClientSetting("IMMERSION", "SUN GLARE",
                "Data-driven lens flare when looking towards the sun, occluded by terrain, buildings, and clouds.",
                context.Settings.Immersion.SunGlareEnabled);
            context.AddClientSetting("IMMERSION", "MFD NIGHT GLOW",
                "Cockpit view: MFD glass night boost so instruments glow at dusk and night.",
                context.Settings.Immersion.MfdGlowEnabled);
            context.AddClientSetting("IMMERSION", "G-VIGNETTE",
                "Cockpit view: physiological G-force visual effects (tunnel vision greyout under high G, redout under negative G).",
                context.Settings.Immersion.GVignetteEnabled);
            context.AddClientSetting("IMMERSION", "SURFACE IMMERSION",
                "Cockpit view: dynamic canopy and airframe surface effects (rain wetness, high-altitude frost, scorch, dirt).",
                context.Settings.Immersion.SurfaceImmersionEnabled);
            context.AddClientSetting("IMMERSION", "AIRFRAME CREAKS",
                "Cockpit view: procedural airframe creaks and structural groans under violent G onsets.",
                context.Settings.Immersion.AirframeAudioEnabled);
            context.AddClientSetting("IMMERSION", "G-AUDIO FILTER",
                "Cockpit view: dynamic auditory narrowing and helmet audio low-pass filtering under high G-forces.",
                context.Settings.Immersion.GForceAudioEnabled);
            context.AddClientSetting("IMMERSION", "CANOPY WIND",
                "Cockpit view: procedural canopy wind rush and aerodynamic slipstream audio.",
                context.Settings.Immersion.WindAudioEnabled);
            context.AddClientSetting("IMMERSION", "PILOT STRAIN",
                "Cockpit view: pilot Anti-G Straining Maneuver (AGSM) pressurized breathing sounds under sustained high G.",
                context.Settings.Immersion.PilotStrainAudioEnabled);
        }
    }
}
