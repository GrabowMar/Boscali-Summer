using BepInEx.Configuration;

namespace BoscaliSummer.Modules.CombatAdjust.Configuration
{
    /// <summary>
    /// Combat adjustments: less arcade, same rules for both sides. Every switch below except <see cref="Enabled"/> is host-authoritative
    /// (HostAuthority) and read live; spec docs/superpowers/specs/2026-10-27-combat-adjustments-design.md.
    /// </summary>
    internal sealed class CombatAdjustSettings
    {
        public ConfigEntry<bool> Enabled { get; }
        public ConfigEntry<bool> LinkDiscipline { get; }
        public ConfigEntry<float> LinkChance { get; }
        public ConfigEntry<float> LinkCloseRangeKm { get; }
        public ConfigEntry<bool> TrackFog { get; }
        public ConfigEntry<float> FogScale { get; }
        public ConfigEntry<float> TrackExpirySeconds { get; }
        public ConfigEntry<float> MissileFailChance { get; }
        public ConfigEntry<float> CountermeasureScale { get; }
        public ConfigEntry<bool> ArmourSmoke { get; }
        public ConfigEntry<bool> ShipDecoys { get; }
        public ConfigEntry<bool> SamEmcon { get; }

        public CombatAdjustSettings(ConfigFile config)
        {
            const string S = "CombatAdjust";
            Enabled = config.Bind(S, "Enabled", true,
                "Install combat adjustments at startup: fewer sightings on the shared picture, drifting old contacts, missiles that can fail, smoke, ship decoys and SAM radars that go dark.");
            LinkDiscipline = config.Bind(S, "LinkDiscipline", true,
                "Only radars and aircraft put what they see on the shared picture straight away. Ground and ship lookouts report distant contacts only now and then; close contacts always.");
            LinkChance = config.Bind(S, "LinkChance", 0.25f, new ConfigDescription(
                "Chance that a lookout's sighting of a distant contact reaches the shared picture on each scan.", new AcceptableValueRange<float>(0f, 1f)));
            LinkCloseRangeKm = config.Bind(S, "LinkCloseRangeKm", 3f, new ConfigDescription(
                "A lookout always reports a contact closer than this (km).", new AcceptableValueRange<float>(0f, 10f)));
            TrackFog = config.Bind(S, "TrackFog", true,
                "A contact nobody is reporting drifts on the map and in the AI's picture, further the longer it goes unseen. Contacts being reported stay exact.");
            FogScale = config.Bind(S, "FogScale", 1f, new ConfigDescription(
                "How far unreported contacts drift (1 = ground 3 m/s up to 600 m, ships 5 m/s, aircraft 30 m/s up to 1000 m).", new AcceptableValueRange<float>(0f, 3f)));
            TrackExpirySeconds = config.Bind(S, "TrackExpirySeconds", 180f, new ConfigDescription(
                "A moving ground or sea contact nobody has seen for this long leaves the picture; aircraft after a third of it. 0 keeps contacts forever (vanilla).", new AcceptableValueRange<float>(0f, 600f)));
            MissileFailChance = config.Bind(S, "MissileFailChance", 0.08f, new ConfigDescription(
                "Share of guided missiles that fail on their own: half with a dud proximity fuse (direct hits only), half with a seeker that wanders off.", new AcceptableValueRange<float>(0f, 0.3f)));
            CountermeasureScale = config.Bind(S, "CountermeasureScale", 1.5f, new ConfigDescription(
                "How much stronger flares, chaff and jamming are against missiles (1 = vanilla).", new AcceptableValueRange<float>(1f, 3f)));
            ArmourSmoke = config.Bind(S, "ArmourSmoke", true,
                "Armoured vehicles pop smoke at laser, TV and IR guided weapons coming for them (2 charges). Smoke breaks those locks and hides the vehicle from lookouts.");
            ShipDecoys = config.Bind(S, "ShipDecoys", true,
                "Ships fire chaff and decoy salvos at guided missiles coming for them (6 salvos).");
            SamEmcon = config.Bind(S, "SamEmcon", true,
                "Ground radars switch off when an anti-radiation missile comes for them, then back on once it is gone. The site is blind while it hides.");
        }
    }
}
