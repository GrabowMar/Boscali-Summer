using BepInEx.Configuration;

namespace BoscaliSummer.Modules.Immersion.Configuration
{
    /// <summary>
    /// Client-side cockpit feel and immersion configuration.
    /// Values can be adjusted mid-flight via configuration manager or MFD settings.
    /// </summary>
    internal sealed class ImmersionSettings
    {
        public ConfigEntry<bool> Enabled { get; }
        public ConfigEntry<bool> HeadMotionEnabled { get; }
        public ConfigEntry<float> HeadMotionStrength { get; }
        public ConfigEntry<bool> ExtraShakeEnabled { get; }
        public ConfigEntry<float> ShakeStrength { get; }
        public ConfigEntry<bool> ComfortMotionEnabled { get; }
        public ConfigEntry<bool> SunGlareEnabled { get; }
        public ConfigEntry<bool> MfdGlowEnabled { get; }
        public ConfigEntry<bool> AirframeAudioEnabled { get; }
        public ConfigEntry<bool> SurfaceImmersionEnabled { get; }
        public ConfigEntry<bool> WindAudioEnabled { get; }
        public ConfigEntry<bool> MachBuffetEnabled { get; }
        public ConfigEntry<bool> PilotBreathingEnabled { get; }
        public ConfigEntry<bool> HelmetVisorEnabled { get; }
        public ConfigEntry<float> HelmetCurvature { get; }
        public ConfigEntry<float> HelmetReflection { get; }
        public ConfigEntry<bool> PilotBodyEnabled { get; }
        public ConfigEntry<bool> PilotControlMotionEnabled { get; }
        public ConfigEntry<bool> PilotReflectionEnabled { get; }
        public ConfigEntry<int> KneeboardCard { get; }

        public ImmersionSettings(ConfigFile config)
        {
            const string section = "Immersion";

            Enabled = config.Bind(section, "Enabled", true,
                "Master switch for cockpit immersion: head motion, camera vibrations, sun glare, and audio effects.");

            HeadMotionEnabled = config.Bind(section, "HeadMotionEnabled", true,
                "Cockpit view: rotational head inertia under G-load, roll lead, yaw lead, and breathing motion.");

            HeadMotionStrength = config.Bind(section, "HeadMotionStrength", 1f,
                new ConfigDescription("Strength of head inertia under G-forces.",
                    new AcceptableValueRange<float>(0f, 2f)));

            ExtraShakeEnabled = config.Bind(section, "ExtraShakeEnabled", true,
                "Cockpit view: dynamic vibrations for gunfire recoil, touchdowns, and runway roll.");

            ShakeStrength = config.Bind(section, "ShakeStrength", 1f,
                new ConfigDescription("Strength of the extra camera vibrations.",
                    new AcceptableValueRange<float>(0f, 2f)));

            ComfortMotionEnabled = config.Bind(section, "ComfortMotionEnabled", false,
                "Reduce all added cockpit rotation to 25% and remove idle head breathing. Native camera motion is unchanged.");

            SunGlareEnabled = config.Bind(section, "SunGlareEnabled", true,
                "Glare and fine rays around the sun as the eye sees them, dimmer and redder at low sun, hidden by terrain, buildings and clouds.");

            MfdGlowEnabled = config.Bind(section, "MfdGlowEnabled", true,
                "Cockpit view: MFD glass night boost so instruments glow at dusk and night.");

            AirframeAudioEnabled = config.Bind(section, "AirframeAudioEnabled", true,
                "Cockpit view: procedural airframe creaks and structural groans under violent G onsets.");

            SurfaceImmersionEnabled = config.Bind(section, "SurfaceImmersionEnabled", true,
                "Cockpit view: subtle damage darkening on verified opaque cockpit material slots. Weather owns glass rain and cold moisture.");

            WindAudioEnabled = config.Bind(section, "WindAudioEnabled", true,
                "Cockpit view: procedural canopy wind rush and aerodynamic slipstream audio.");

            MachBuffetEnabled = config.Bind(section, "MachBuffetEnabled", true,
                "Cockpit view: transonic Mach aerodynamic buffeting vibrations (Mach 0.86 - 1.12) and sound barrier crossing bump.");

            PilotBreathingEnabled = config.Bind(section, "PilotBreathingEnabled", true,
                "Cockpit view: oxygen-mask breathing, anti-G straining grunts, G-suit hiss and a faint heartbeat near blackout, all paced by the game's own G-LOC model.");
            HelmetVisorEnabled = config.Bind(section, "HelmetVisorEnabled", true,
                "Cockpit view: near-future helmet-mounted display - curved visor, electrochromic sun tint, scratches lit by the sun, breath fog when breathing hard and visor reflections.");
            HelmetCurvature = config.Bind(section, "HelmetCurvature", .55f,
                new ConfigDescription("Cockpit view: spherical curvature of the helmet visor and the TAC BOARD on it. 0 is the old flat glass, 1 wraps hardest around the eye.",
                    new AcceptableValueRange<float>(0f, 1f)));
            HelmetReflection = config.Bind(section, "HelmetReflection", 1f,
                new ConfigDescription("Cockpit view: visor and canopy reflections - Fresnel rim on the visor, pilot reflection and sun glint on the windscreen. 0 turns reflections off.",
                    new AcceptableValueRange<float>(0f, 1f)));
            PilotBodyEnabled = config.Bind(section, "PilotBodyEnabled", true,
                "Show the native pilot's flight suit, arms and legs in first person. Local presentation only.");
            PilotControlMotionEnabled = config.Bind(section, "PilotControlMotionEnabled", true,
                "Enable rudder foot movement, restrained breathing and G-load bracing. Hand attachment stays active; Comfort Motion reduces body motion.");
            PilotReflectionEnabled = config.Bind(section, "PilotReflectionEnabled", true,
                "Faint pilot and helmet reflection on verified windscreen glass. Pilot-only 128/256 pixel capture capped at 10 Hz; works independently of body visibility.");
            KneeboardCard = config.Bind(section, "KneeboardCard", 0,
                "Cockpit kneeboard card: 0 writes the mission card, 1 and up pick a picture card. Choose it in SQD > STUDIO.");
        }
    }
}
