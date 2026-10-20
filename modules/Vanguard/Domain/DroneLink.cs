using UnityEngine;

namespace BoscaliSummer.Modules.Vanguard.Domain
{
    /// <summary>What a linked drone flies on its pilot's order (host-owned). AUTO = its own job.</summary>
    internal enum DroneMode : byte { Auto, Hand, Waypoint }

    /// <summary>
    /// VANGUARD drone link rules, shared by the host (orders, link loss) and every peer's UAV terminal, UAV MFD and feed.
    /// HAND mirrors the pilot's nose (the drone flies the way the pilot points), WPT flies to a point and circles it.
    /// </summary>
    internal static class DroneLink
    {
        public const float FullRangeM = 12000f, MaxRangeM = 40000f;
        public const float OrbitRadiusM = 1200f, HandLookaheadM = 3000f, FloorAglM = 150f;
        private const float OrbitLeadDeg = 40f;
        private static readonly float[] PlotSteps = { 5f, 10f, 20f, 40f };

        /// <summary>1 inside <see cref="FullRangeM"/>, falling linearly to 0 at <see cref="MaxRangeM"/>.</summary>
        public static float Quality(float rangeM) => Mathf.Clamp01((MaxRangeM - rangeM) / (MaxRangeM - FullRangeM));

        public static bool InRange(float rangeM) => rangeM < MaxRangeM;

        /// <summary>Signal bars 0..5; any live link shows at least one.</summary>
        public static int Bars(float quality) => quality <= 0f ? 0 : Mathf.Clamp(Mathf.CeilToInt(quality * 5f), 1, 5);

        /// <summary>The index <paramref name="step"/> places from <paramref name="current"/>, wrapping; -1 with nothing to pick.</summary>
        public static int Step(int count, int current, int step)
        {
            if (count <= 0) return -1;
            if (current < 0 || current >= count) return step >= 0 ? 0 : count - 1;
            return ((current + step) % count + count) % count;
        }

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

        /// <summary>What the asset does on its own (AUTO), in a few words.</summary>
        public static string Task(VanguardRole role)
        {
            switch (role)
            {
                case VanguardRole.Decoy: return "RADAR BAIT · CHAFF SCREEN";
                case VanguardRole.Jammer: return "STAND-IN RADAR JAMMING";
                case VanguardRole.Drone: return "ESCORT · STRIKE ON DESIGNATION";
                case VanguardRole.Glider: return "HYPERSONIC GLIDE STRIKE";
                case VanguardRole.Sentinel: return "HOVER GUARD · GUN";
                case VanguardRole.Gunship: return "PYLON-TURN GUN ORBIT";
                case VanguardRole.Scout: return "SPOT · LASER DESIGNATE";
                case VanguardRole.Lander: return "CARGO GLIDER · LANDING";
                default: return "";
            }
        }

        public static string Callsign(VanguardRole role, int ordinal) => Type(role) + " " + Mathf.Max(1, ordinal);

        public static string Word(DroneMode mode) => mode == DroneMode.Hand ? "HAND" : mode == DroneMode.Waypoint ? "WPT" : "AUTO";

        /// <summary>HAND: a point <see cref="HandLookaheadM"/> out from the drone along the pilot's nose, nosed up near the ground.</summary>
        public static Vector3 HandAim(Vector3 drone, Vector3 pilotForward, float droneAgl)
        {
            Vector3 dir = pilotForward.sqrMagnitude > 1e-6f ? pilotForward.normalized : Vector3.forward;
            if (droneAgl < FloorAglM) dir.y = Mathf.Max(dir.y, 0.3f * (1f - droneAgl / FloorAglM));
            return drone + dir.normalized * HandLookaheadM;
        }

        /// <summary>WPT: straight at the point while far out, then a left-hand circle of <paramref name="radius"/> round it at its height.</summary>
        public static Vector3 OrbitAim(Vector3 drone, Vector3 centre, float radius)
        {
            Vector3 flat = drone - centre;
            flat.y = 0f;
            if (flat.magnitude > radius * 2f) return centre;
            float angle = Mathf.Atan2(flat.z, flat.x) + OrbitLeadDeg * Mathf.Deg2Rad; // counter-clockwise from above = turning left
            return centre + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * radius;
        }

        /// <summary>True bearing in degrees 0..360 (north = +z, east = +x).</summary>
        public static float Bearing(Vector3 from, Vector3 to)
        {
            float deg = Mathf.Atan2(to.x - from.x, to.z - from.z) * Mathf.Rad2Deg;
            return deg < 0f ? deg + 360f : deg;
        }

        /// <summary>Plot radius in km: the smallest ring step that holds the farthest drone with a margin, capped at the link limit.</summary>
        public static float PlotRangeKm(float farthestKm)
        {
            foreach (float step in PlotSteps) if (farthestKm * 1.15f <= step) return step;
            return PlotSteps[PlotSteps.Length - 1];
        }

        public static string Clock(float seconds)
        {
            int s = Mathf.Max(0, Mathf.FloorToInt(seconds));
            return (s / 60).ToString("00") + ":" + (s % 60).ToString("00");
        }
    }
}
