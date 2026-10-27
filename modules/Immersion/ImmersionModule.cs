using System;
using BoscaliSummer.Modules.Immersion.Patches;
using BoscaliSummer.Modules.Immersion.Runtime;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Core.Modules;
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
            typeof(GunShotShakePatch),
            typeof(PlayerHitPatch),
            typeof(RadarToneCapturePatch),
            typeof(RadarToneSwapPatch),
            typeof(MissileAlarmSoftPatch),
            typeof(InterfaceSoftPatch),
            typeof(StallHornPatch),
            typeof(RpmWarningSoftPatch),
            typeof(VrsWarningSoftPatch),
            typeof(NearMissCrackPatch),
            typeof(ExplosionMufflePatch),
            typeof(ParkedEnterPatch),
            typeof(ParkedLeavePatch),
            typeof(ParkedNavLightPatch)
        };

        public ModuleMetadata Metadata => Module;
        public Type[] PatchTypes => Patches;

        public void Install(ModuleContext context)
        {
            ImmersionManager immersion = context.AddSceneService<ImmersionManager>(47);
            immersion.Configure(context.Settings.Immersion);
            context.AddClientEffect(immersion);
            context.AddService<IImmersionSettings>(immersion);
            CockpitPilot pilot = context.AddSceneService<CockpitPilot>(48);
            pilot.Configure(context.Settings.Immersion, context.Logger);
            context.AddClientEffect(pilot);
            var gallery = new Visuals.KneeboardGallery(context.Settings.Immersion,
                () => { pilot.DebugRefreshKneeboard(); ImmersionManager.Live?.PageFlip(); });
            pilot.SetKneeboardGallery(gallery);
            context.AddService<IKneeboardGallery>(gallery);
            WorldFx world = context.AddSceneService<WorldFx>(49);
            world.Configure(context.Settings.Immersion);
            context.AddClientEffect(world);

            context.AddClientSetting("IMMERSION", "IMMERSION MASTER",
                "Master switch for all client-side cockpit immersion features.",
                context.Settings.Immersion.Enabled);
            context.AddClientSetting("IMMERSION", "HEAD MOTION",
                "Cockpit view: rotational head inertia under G-load, roll lead, yaw lead, and breathing motion.",
                context.Settings.Immersion.HeadMotionEnabled);
            context.AddClientSetting("IMMERSION", "COMFORT MOTION",
                "Reduce all added cockpit rotation to 25% and remove idle breathing.",
                context.Settings.Immersion.ComfortMotionEnabled);
            context.AddClientSetting("IMMERSION", "EXTRA SHAKE",
                "Cockpit view: dynamic vibrations for gunfire recoil, touchdowns, and runway roll.",
                context.Settings.Immersion.ExtraShakeEnabled);
            context.AddClientSetting("IMMERSION", "MACH BUFFET",
                "Cockpit view: transonic Mach aerodynamic buffeting vibrations (Mach 0.86 - 1.12) and sound barrier crossing bump.",
                context.Settings.Immersion.MachBuffetEnabled);
            context.AddClientSetting("IMMERSION", "SUN GLARE",
                "Glare and fine rays around the sun as the eye sees them, dimmer and redder at low sun, hidden by terrain, buildings and clouds.",
                context.Settings.Immersion.SunGlareEnabled, ClientPreset.Light);
            context.AddClientSetting("IMMERSION", "MFD NIGHT GLOW",
                "Cockpit view: MFD glass night boost so instruments glow at dusk and night.",
                context.Settings.Immersion.MfdGlowEnabled, ClientPreset.Light);
            context.AddClientSetting("IMMERSION", "HELMET VISOR",
                "Cockpit view: near-future helmet-mounted display - curved visor, electrochromic sun tint, breath fog, sun-lit scratches, visor and canopy reflections.",
                context.Settings.Immersion.HelmetVisorEnabled, ClientPreset.Light);
            context.AddClientSetting("IMMERSION", "SURFACE IMMERSION",
                "Cockpit view: subtle damage shading on supported opaque cockpit materials. Weather owns glass moisture.",
                context.Settings.Immersion.SurfaceImmersionEnabled, ClientPreset.Light);
            context.AddClientSetting("IMMERSION", "AIRFRAME CREAKS",
                "Cockpit view: procedural airframe creaks and structural groans under violent G onsets.",
                context.Settings.Immersion.AirframeAudioEnabled);
            context.AddClientSetting("IMMERSION", "CANOPY WIND",
                "Cockpit view: procedural canopy wind rush and aerodynamic slipstream audio.",
                context.Settings.Immersion.WindAudioEnabled);
            SoftTones.Master = context.Settings.Immersion.Enabled;
            SoftTones.Soft = context.Settings.Immersion.SoftTonesEnabled;
            context.AddClientSetting("IMMERSION", "COCKPIT ALARMS",
                "Modern missile lock, track and launch alarms and stall horn; soft radar bell and tick; rounder clicks and RPM/VRS warnings. Alarm changes apply from the next aircraft.",
                context.Settings.Immersion.SoftTonesEnabled);
            context.AddClientSetting("IMMERSION", "VOICE WARNINGS",
                "PULL UP with a GPWS whoop, TERRAIN, OVER G and BINGO FUEL for your own aircraft, in every view.",
                context.Settings.Immersion.VoiceWarningsEnabled);
            context.AddClientSetting("IMMERSION", "PILOT BREATHING",
                "Cockpit view: oxygen-mask breathing, anti-G straining grunts, G-suit hiss and a faint heartbeat near blackout, paced by the game's G-LOC model.",
                context.Settings.Immersion.PilotBreathingEnabled);
            context.AddClientSetting("IMMERSION", "PILOT BODY",
                "First-person flight suit, arms and legs using the native pilot model.", context.Settings.Immersion.PilotBodyEnabled, ClientPreset.Light);
            context.AddClientSetting("IMMERSION", "PILOT CONTROL MOTION",
                "Hands follow reachable stick/throttle/collective controls; rudder feet, breathing and G-load bracing. Comfort Motion applies.",
                context.Settings.Immersion.PilotControlMotionEnabled);
            context.AddClientSetting("IMMERSION", "PILOT REFLECTION",
                "Faint pilot and helmet reflection on the windscreen; small pilot-only capture at most ten times per second.",
                context.Settings.Immersion.PilotReflectionEnabled, ClientPreset.Heavy);
            context.AddClientSetting("IMMERSION", "NEAR-MISS CRACK",
                "Supersonic rounds passing within 40 m snap with a sharp crack; heavy cannon rounds also jolt the view.",
                context.Settings.Immersion.NearMissCrackEnabled);
            context.AddClientSetting("IMMERSION", "CANOPY MUFFLE",
                "Cockpit view: outside explosions are heard through the canopy, with their top end cut.",
                context.Settings.Immersion.CockpitMuffleEnabled);
            context.AddClientSetting("IMMERSION", "CONTRAILS",
                "Jet engines leave long white contrails above about 7.5 km, spreading and drifting with the wind.",
                context.Settings.Immersion.ContrailsEnabled, ClientPreset.Light);
            context.AddClientSetting("IMMERSION", "DUST WAKE",
                "Aircraft fast and very low over land kick up a dust wake behind them.",
                context.Settings.Immersion.DustWakeEnabled, ClientPreset.Light);
            context.AddClientSetting("IMMERSION", "QUIET PARKED AI",
                "Parked AI aircraft switch navigation lights off and leave the cockpit empty until they start up.",
                context.Settings.Immersion.ParkedAircraftEnabled);
        }
    }
}
