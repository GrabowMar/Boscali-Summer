using UnityEngine;

namespace BoscaliSummer.Modules.Vanguard.Domain
{
    /// <summary>Signal quality, callsigns and sensor readouts for the drone link.</summary>
    internal static class DroneLink
    {
        public const float FullRangeM = 12000f, MaxRangeM = 40000f;
        /// <summary>Sensor ball depression in degrees: hand-flown it looks just under the horizon, on its own job it stares down ahead.</summary>
        public const float ForwardDeg = 8f, StareDeg = 25f;

        /// <summary>1 inside <see cref="FullRangeM"/>, falling linearly to 0 at <see cref="MaxRangeM"/>.</summary>
        public static float Quality(float rangeM, float scale = 1f) => Mathf.Clamp01((MaxRangeM * scale - rangeM) / ((MaxRangeM - FullRangeM) * scale));

        /// <summary><paramref name="scale"/> stretches both link distances (<see cref="PodRules.LinkScale"/>, the WATCHTOWER relay).</summary>
        public static bool InRange(float rangeM, float scale = 1f) => rangeM < MaxRangeM * scale;

        /// <summary>Signal bars 0..5; any live link shows at least one.</summary>
        public static int Bars(float quality) => quality <= 0f ? 0 : Mathf.Clamp(Mathf.CeilToInt(quality * 5f), 1, 5);

        public static string Type(VanguardRole role)
        {
            switch (role)
            {
                case VanguardRole.Decoy: return "MALD-X";
                case VanguardRole.Jammer: return "MALD-J";
                case VanguardRole.Drone: return "REMORA";
                case VanguardRole.Glider: return "HAWC-X";
                case VanguardRole.Sentinel: return "GLAIVE-2A";
                case VanguardRole.Gunship: return "GLAIVE-2S";
                case VanguardRole.Scout: return "KESTREL";
                case VanguardRole.Lander: return "HAMILCAR";
                default: return "VANGUARD";
            }
        }

        /// <summary>The loadout's gimmick name (the remake codenames), shown as the asset's job.</summary>
        public static string Job(VanguardRole role)
        {
            switch (role)
            {
                case VanguardRole.Decoy: return "BAIT";
                case VanguardRole.Jammer: return "SHADOW";
                case VanguardRole.Drone: return "ARCHER";
                case VanguardRole.Glider: return "SKIP";
                case VanguardRole.Sentinel: return "SENTINEL";
                case VanguardRole.Gunship: return "SPECTRE";
                case VanguardRole.Scout: return "SPOTTER";
                case VanguardRole.Lander: return "FREIGHT";
                default: return "";
            }
        }

        public static string Callsign(VanguardRole role, int ordinal) => Type(role) + " " + Mathf.Max(1, ordinal);

        /// <summary>The XQ-90 MANTA shares the REMORA's role but is its own type with its own numbering.</summary>
        public static string Callsign(string jsonKey, VanguardRole role, int ordinal) =>
            jsonKey == VanguardKeys.Manta ? "MANTA " + Mathf.Max(1, ordinal) : Callsign(role, ordinal);

        /// <summary>True bearing in degrees 0..360 (north = +z, east = +x).</summary>
        public static float Bearing(Vector3 from, Vector3 to)
        {
            float deg = Mathf.Atan2(to.x - from.x, to.z - from.z) * Mathf.Rad2Deg;
            return deg < 0f ? deg + 360f : deg;
        }

        /// <summary>
        /// Where the horizon sits in the feed, in half-heights above the centre (negative = below), for a stabilised lens at
        /// <paramref name="elevationDeg"/> (down negative) with a vertical field of view of <paramref name="fovDeg"/>.
        /// </summary>
        public static float HorizonLift(float elevationDeg, float fovDeg) =>
            Mathf.Tan(-elevationDeg * Mathf.Deg2Rad) / Mathf.Tan(Mathf.Max(fovDeg, 0.1f) * 0.5f * Mathf.Deg2Rad);

        /// <summary>A ten-metre grid reference inside the 100 km square ("0412 7731"): global x east, z north, wrapped.</summary>
        public static string Grid(float x, float z) => Digits(x) + " " + Digits(z);

        private static string Digits(float metres) => (((int)Mathf.Floor(metres / 10f) % 10000 + 10000) % 10000).ToString("0000");

        /// <summary>Link latency the feed prints, ms: 40 on a full link, rising to 600 at its edge.</summary>
        public static int LatencyMs(float quality) => Mathf.RoundToInt(40f + (1f - Mathf.Clamp01(quality)) * 560f);

        public static string Clock(float seconds)
        {
            int s = Mathf.Max(0, Mathf.FloorToInt(seconds));
            return (s / 60).ToString("00") + ":" + (s % 60).ToString("00");
        }
    }
}
