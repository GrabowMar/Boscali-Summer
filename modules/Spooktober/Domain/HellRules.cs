using System;

namespace BoscaliSummer.Modules.Spooktober.Domain
{
    /// <summary>What a Hell's Army wave is: each one has a character, announced on the HUD.</summary>
    internal enum HellTheme : byte { Scouts, Iron, Skyfire, Horde }

    /// <summary>
    /// Hell's Army's invasion plan and the fly-through test (pure; pinned by tests/BoscaliSummer.Tests/Features/Spooktober).
    /// The waves cycle SCOUTS (fast and light) -> IRON (armour, a gunship overhead) -> SKYFIRE (AA, rockets and gunships: stop
    /// loitering low) -> HORDE (a huge mixed push with jets, then a breather), two vehicles bigger each lap. A wave pours out of the
    /// gate one unit every <see cref="StreamSeconds"/>, the gate grows with every wave, and AA wardens ring it from the start.
    /// Wiping out every attacker on the field before the next wave makes the gate falter: the wave comes later.
    /// </summary>
    internal static class HellRules
    {
        public const float FirstWaveSeconds = 3f;
        public const float WaveSeconds = 35f;
        public const float BreatherSeconds = 25f;  // added after a HORDE
        public const float FalterSeconds = 15f;    // added once when the field is cleared between waves
        public const float StreamSeconds = 0.5f;   // one unit through the gate every half second
        public const int MaxAlive = 32;            // ground attackers on the field
        public const int MaxAir = 8;
        public const int MaxWave = 18;
        public const float PortalRadius = 70f;
        public const float MaxGateScale = 2.2f;
        public const int Wardens = 4;
        public const float WardenRing = 170f;
        public const int Anchors = 3;
        public const float EntryHeight = 45f;
        public const float DimensionRadius = 7000f;

        private static readonly int[] BaseSize = { 6, 8, 6, 12 };
        private static readonly int[] BaseAir = { 0, 1, 2, 3 };

        // Game unit keys per theme (HellGate skips any key this game build lacks; doubles weight the pick).
        private static readonly string[][] Keys =
        {
            new[] { "Linebreaker_IFV", "AFV8_IFV", "LightTruck1_AT", "AFV8_APC" },
            new[] { "MBT1", "MBT1", "MBT1", "6x6_1_AT", "AFV8_IFV" },
            new[] { "SPAAG1", "Truck2-MLRS", "LightTruck1_AA", "AFV8_SAM", "Truck2-MLRS" },
            new[] { "MBT1", "MBT1", "Linebreaker_IFV", "SPAAG1", "Truck2-MLRS", "6x6_1_AT", "AFV8_IFV" },
        };

        // Aircraft by a word of their name (the game's aircraft keys are not stable names): gunships early, fighters in the horde.
        private static readonly string[][] AirNames =
        {
            new string[0],
            new[] { "Tarantula" },
            new[] { "Tarantula" }, // the Ibis is a transport: no gun to point at anyone
            new[] { "Revoker", "Vortex", "Tarantula" },
        };

        /// <summary>The gate's own AA ring, by key (held in place round the gate).</summary>
        public static readonly string[] WardenKeys = { "SPAAG1", "AFV8_SAM", "SPAAG1", "LightTruck1_AA" };

        /// <summary>The theme of wave <paramref name="index"/> (0 = the first).</summary>
        public static HellTheme Theme(int index) => (HellTheme)(Math.Max(index, 0) % 4);

        private static int Lap(int index) => Math.Max(index, 0) / 4;

        /// <summary>How many ground vehicles wave <paramref name="index"/> wants: its theme's size, two more each lap, at most <see cref="MaxWave"/>.</summary>
        public static int Size(int index) => Math.Min(MaxWave, BaseSize[(int)Theme(index)] + 2 * Lap(index));

        /// <summary>How many aircraft wave <paramref name="index"/> wants: none with the scouts, one more every second lap.</summary>
        public static int AirSize(int index) => Theme(index) == HellTheme.Scouts ? 0 : BaseAir[(int)Theme(index)] + Lap(index) / 2;

        /// <summary>How many ground vehicles to send out now: the wave's size, never past the cap on the field.</summary>
        public static int ToSpawn(int index, int alive) => Math.Max(0, Math.Min(Size(index), MaxAlive - alive));

        /// <summary>How many aircraft to send out now, never past the air cap.</summary>
        public static int AirToSpawn(int index, int airborne) => Math.Max(0, Math.Min(AirSize(index), MaxAir - airborne));

        /// <summary>Seconds from wave <paramref name="index"/> to the next one: the beat, plus a breather after a HORDE.</summary>
        public static float Delay(int index) => WaveSeconds + (Theme(index) == HellTheme.Horde ? BreatherSeconds : 0f);

        /// <summary>Captured bases sustain reinforcements; breaking an anchor permanently slows the invasion.</summary>
        public static float InvasionDelay(int index, int heldBases, int brokenAnchors) =>
            Math.Max(18f, Delay(index) - Math.Min(heldBases, 4) * 4f + Math.Max(0, brokenAnchors) * 10f);

        public static int InvasionToSpawn(int index, int alive, int heldBases, int brokenAnchors) =>
            Math.Max(0, Math.Min(MaxAlive - alive, Math.Min(MaxWave, Size(index) + Math.Min(4, Math.Max(0, heldBases)) * 2) - Math.Max(0, brokenAnchors) * 2));

        /// <summary>Descend into the ground rift before hitting its floor. Horizontal overflights and teleport jumps do not count.</summary>
        public static bool EnteredRift(float previousHeight, float height, float across, float radius) =>
            previousHeight > EntryHeight && height <= EntryHeight && height >= -10f &&
            previousHeight - height < 500f && across < radius * 0.9f;

        public static bool DimensionWon(int brokenAnchors) => brokenAnchors >= Anchors;

        /// <summary>Volcanic atmosphere fades across the boundary and above the terrain, never staining distant overflights.</summary>
        public static float CorruptionWeight(float distance, float height, float radius)
        {
            if (!float.IsFinite(distance) || !float.IsFinite(height) || !float.IsFinite(radius) || radius <= 0f) return 0f;
            return (1f - Smooth((Math.Max(0f, distance) / radius - .55f) / .85f)) * (1f - Smooth((Math.Abs(height) - 250f) / 1350f));
        }

        private static float Smooth(float value)
        {
            value = Math.Max(0f, Math.Min(1f, value));
            return value * value * (3f - 2f * value);
        }

        /// <summary>The gate's size after <paramref name="waves"/> waves came through: 12 % bigger each, up to <see cref="MaxGateScale"/>.</summary>
        public static float GateScale(int waves) => Math.Min(MaxGateScale, 1f + 0.12f * Math.Max(waves, 0));

        public static string[] Roster(HellTheme theme) => Keys[(int)theme];

        public static string[] AirRoster(HellTheme theme) => AirNames[(int)theme];

        /// <summary>The HUD line for wave <paramref name="index"/>.</summary>
        public static string Banner(int index) => "WAVE " + (index + 1) + " · " + Theme(index).ToString().ToUpperInvariant();

        /// <summary>
        /// Legacy vertical-disc crossing helper; ground rifts use <see cref="EnteredRift"/>. <paramref name="wasAlong"/> and <paramref name="along"/> are the
        /// signed distances along the disc's normal before and now, <paramref name="across"/> its distance from the axis now. It must
        /// change side within the radius; a jump of 500 m or more in one sample is a teleport (floating origin, respawn), not a pass.
        /// </summary>
        public static bool Crossed(float wasAlong, float along, float across, float radius) =>
            across <= radius && (wasAlong > 0f) != (along > 0f) && Math.Abs(along - wasAlong) < 500f;
    }
}
