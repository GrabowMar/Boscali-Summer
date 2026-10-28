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

            // SET > COCKPIT is built from these rows (WORLD FX lands on SET > WORLD). Every row waits on the master.
            var s = context.Settings.Immersion;
            const string Motion = "COCKPIT MOTION", Look = "COCKPIT VISUALS", Sound = "COCKPIT AUDIO", World = "WORLD FX";
            ClientSettingToggle master = context.AddClientSetting("IMMERSION", "IMMERSION MASTER",
                "Master switch for every cockpit immersion feature on this page and the world effects.", s.Enabled);
            context.AddClientSetting("IMMERSION", "COMFORT MOTION",
                "Reduce all added cockpit rotation to 25% and remove idle breathing. Individual switches still work.",
                s.ComfortMotionEnabled, requires: master);

            ClientSettingToggle head = context.AddClientSetting(Motion, "HEAD MOTION",
                "Cockpit view: rotational head inertia under G-load, roll lead, yaw lead, and breathing motion.",
                s.HeadMotionEnabled, requires: master);
            context.AddClientSetting(Motion, "EXTRA SHAKE",
                "Cockpit view: dynamic vibrations for gunfire recoil, touchdowns, and runway roll.",
                s.ExtraShakeEnabled, requires: master);
            context.AddClientSetting(Motion, "MACH BUFFET",
                "Cockpit view: transonic Mach aerodynamic buffeting vibrations (Mach 0.86 - 1.12) and sound barrier crossing bump.",
                s.MachBuffetEnabled, requires: master);
            context.AddClientSetting(Motion, "HAND MOTION",
                "Hands follow reachable stick/throttle/collective controls; rudder feet, breathing and G-load bracing. Comfort Motion applies.",
                s.PilotControlMotionEnabled, requires: master);
            context.AddClientLevel(Motion, "HEAD STRENGTH", "Strength of cockpit head inertia under G-forces and angular rates.",
                s.HeadMotionStrength, 0f, 2f, .1f, requires: head);
            context.AddClientLevel(Motion, "SHAKE STRENGTH", "Strength of extra camera vibrations for gunfire, touchdowns, and Mach buffet.",
                s.ShakeStrength, 0f, 2f, .1f, requires: master);

            context.AddClientSetting(Look, "SUN GLARE",
                "Glare and fine rays around the sun as the eye sees them, dimmer and redder at low sun, hidden by terrain, buildings and clouds.",
                s.SunGlareEnabled, ClientPreset.Light, master);
            context.AddClientSetting(Look, "MFD NIGHT GLOW",
                "Cockpit view: MFD glass night boost so instruments glow at dusk and night.",
                s.MfdGlowEnabled, ClientPreset.Light, master);
            ClientSettingToggle visor = context.AddClientSetting(Look, "HELMET VISOR",
                "Cockpit view: near-future helmet-mounted display - curved visor, electrochromic sun tint, breath fog, sun-lit scratches, visor and canopy reflections.",
                s.HelmetVisorEnabled, ClientPreset.Light, master);
            context.AddClientSetting(Look, "DAMAGE SURFACES",
                "Cockpit view: subtle damage shading on supported opaque cockpit materials. Weather owns glass moisture.",
                s.SurfaceImmersionEnabled, ClientPreset.Light, master);
            context.AddClientSetting(Look, "PILOT BODY",
                "First-person flight suit, arms and legs using the native pilot model.", s.PilotBodyEnabled, ClientPreset.Light, master);
            context.AddClientSetting(Look, "PILOT REFLECTION",
                "Faint pilot and helmet reflection on the windscreen; small pilot-only capture at most ten times per second.",
                s.PilotReflectionEnabled, ClientPreset.Heavy, master);
            context.AddClientLevel(Look, "VISOR CURVE", "Spherical curvature of the visor and the TAC BOARD on it. 0% is flat glass.",
                s.HelmetCurvature, 0f, 1f, .05f, requires: visor);
            context.AddClientLevel(Look, "REFLECTIONS", "Visor rim, pilot reflection and sun glint on the windscreen. 0% turns them off.",
                s.HelmetReflection, 0f, 1f, .1f, requires: visor);

            context.AddClientSetting(Sound, "AIRFRAME CREAKS",
                "Cockpit view: procedural airframe creaks and structural groans under violent G onsets.",
                s.AirframeAudioEnabled, requires: master);
            context.AddClientSetting(Sound, "CANOPY WIND",
                "Cockpit view: procedural canopy wind rush and aerodynamic slipstream audio.",
                s.WindAudioEnabled, requires: master);
            context.AddClientSetting(Sound, "PILOT BREATHING",
                "Cockpit view: oxygen-mask breathing, anti-G straining grunts, G-suit hiss and a faint heartbeat near blackout, paced by the game's G-LOC model.",
                s.PilotBreathingEnabled, requires: master);
            SoftTones.Master = s.Enabled;
            SoftTones.Soft = s.SoftTonesEnabled;
            context.AddClientSetting(Sound, "COCKPIT ALARMS",
                "Modern missile lock, track and launch alarms and stall horn; soft radar bell and tick; rounder clicks and RPM/VRS warnings. Alarm changes apply from the next aircraft.",
                s.SoftTonesEnabled, requires: master);
            context.AddClientSetting(Sound, "VOICE WARNINGS",
                "PULL UP with a GPWS whoop, TERRAIN, OVER G and BINGO FUEL for your own aircraft, in every view.",
                s.VoiceWarningsEnabled, requires: master);
            context.AddClientSetting(Sound, "NEAR-MISS CRACK",
                "Supersonic rounds passing within 40 m snap with a sharp crack; heavy cannon rounds also jolt the view.",
                s.NearMissCrackEnabled, requires: master);
            context.AddClientSetting(Sound, "CANOPY MUFFLE",
                "Cockpit view: outside explosions are heard through the canopy, with their top end cut.",
                s.CockpitMuffleEnabled, requires: master);

            context.AddClientSetting(World, "CONTRAILS",
                "Jet engines leave long white contrails above about 7.5 km, spreading and drifting with the wind.",
                s.ContrailsEnabled, ClientPreset.Light, master);
            context.AddClientSetting(World, "DUST WAKE",
                "Aircraft fast and very low over land kick up a dust wake behind them.",
                s.DustWakeEnabled, ClientPreset.Light, master);
            context.AddClientSetting(World, "QUIET PARKED AI",
                "Parked AI aircraft switch navigation lights off and leave the cockpit empty until they start up.",
                s.ParkedAircraftEnabled, requires: master);
        }
    }
}
