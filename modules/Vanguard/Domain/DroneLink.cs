using UnityEngine;

namespace BoscaliSummer.Modules.Vanguard.Domain
{
    /// <summary>What a linked drone flies on its pilot's order (host-owned). AUTO = its own job.</summary>
    internal enum DroneMode : byte { Auto, Hand, Waypoint }

    /// <summary>
    /// VANGUARD drone link rules, shared by the host (orders, link loss) and every peer's UAV terminal, UAV MFD and feed.
    /// HAND holds a commanded course and altitude (set by clicking the feed, the wheel, or MATCH MY NOSE), WPT flies to a point and circles it.
    /// </summary>
    internal static class DroneLink
    {
        public const float FullRangeM = 12000f, MaxRangeM = 40000f;
        public const float OrbitRadiusM = 1200f, HandLookaheadM = 3000f, FloorAglM = 150f;
        /// <summary>Sensor ball depression in degrees: hand-flown it looks just under the horizon, on its own job it stares down ahead.</summary>
        public const float ForwardDeg = 8f, StareDeg = 25f;
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

        /// <summary>
        /// HAND (2026-10-20, replaces mirroring the pilot's nose): the drone holds a commanded course and altitude. A point
        /// <see cref="HandLookaheadM"/> out along <paramref name="courseDeg"/>, at most <see cref="HandClimbStepM"/> above or below the
        /// drone (a gentle climb or dive toward <paramref name="altitude"/>, global y), nosed up near the ground.
        /// </summary>
        public static Vector3 HandAim(Vector3 drone, float courseDeg, float altitude, float droneAgl)
        {
            float r = courseDeg * Mathf.Deg2Rad;
            float climb = Mathf.Clamp(altitude - drone.y, -HandClimbStepM, HandClimbStepM);
            if (droneAgl < FloorAglM) climb = Mathf.Max(climb, 0.3f * (1f - droneAgl / FloorAglM) * HandLookaheadM);
            return drone + new Vector3(Mathf.Sin(r) * HandLookaheadM, climb, Mathf.Cos(r) * HandLookaheadM);
        }

        public const float HandClimbStepM = 1000f, AltPerDeg = 60f, MinAltM = 150f, MaxAltM = 12000f;

        /// <summary>The HAND order's point carries the course in x (degrees) and the altitude in y (global metres); z is unused.</summary>
        public static Vector3 HandPoint(float courseDeg, float altitude) =>
            new Vector3(Mathf.Repeat(courseDeg, 360f), Mathf.Clamp(altitude, MinAltM, MaxAltM), 0f);

        /// <summary>The world direction through a feed point (<paramref name="viewport"/> 0..1 from the bottom left) of a lens.</summary>
        public static Vector3 LookDirection(Quaternion lens, float fovDeg, float aspect, Vector2 viewport)
        {
            float t = Mathf.Tan(fovDeg * 0.5f * Mathf.Deg2Rad);
            return (lens * new Vector3((viewport.x * 2f - 1f) * t * aspect, (viewport.y * 2f - 1f) * t, 1f)).normalized;
        }

        /// <summary>
        /// Click-to-steer: the course is the bearing of the clicked direction; the altitude moves <see cref="AltPerDeg"/> per degree the
        /// click sits above or below the lens's own line (a click on the cross holds the height).
        /// </summary>
        public static Vector3 SteerTo(Vector3 direction, Vector3 lensForward, float droneAlt)
        {
            float pitch = Mathf.Asin(Mathf.Clamp(direction.y, -1f, 1f)) * Mathf.Rad2Deg;
            float lensPitch = Mathf.Asin(Mathf.Clamp(lensForward.y, -1f, 1f)) * Mathf.Rad2Deg;
            float change = Mathf.Clamp((pitch - lensPitch) * AltPerDeg, -1500f, 1500f);
            return HandPoint(Bearing(Vector3.zero, direction), droneAlt + change);
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
