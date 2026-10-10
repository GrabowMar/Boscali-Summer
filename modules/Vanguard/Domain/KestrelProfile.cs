using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace BoscaliSummer.Modules.Vanguard.Domain
{
    internal enum KestrelPhase : byte { Transit, Station, Depart }

    /// <summary>
    /// KS-01 KESTREL: unarmed recon glider released from a cargo hold. Flies to a ground point, holds GLAIVE's left-hand
    /// pylon turn on a wider and higher circle, keeps the enemy ground units and ships around the centre on its faction's
    /// picture and laser-marks one of them. It is removed quietly when its station time or its hard cap runs out.
    /// </summary>
    internal static class KestrelProfile
    {
        public const float CruiseSpeed = 80f;
        public const float OrbitRadius = 800f;
        public const float OrbitHeight = 1000f;       // above the orbit target
        public const float MinAgl = 300f;             // terrain floor under the glider
        public const float MaxDive = .2f, MaxClimb = .15f; // height change per metre flown: no overspeed, no stall
        public const float ClearSeconds = 2.5f;       // out of the hold: intangible and unsteered until then
        public const float StationSeconds = 300f;     // counted from orbit entry
        public const float HardCap = 900f;            // seconds after launch
        public const float DepartSeconds = 8f;        // straight leg before the quiet removal
        public const float SpotRadius = 5000f;        // flat, around the orbit centre
        public const float SpotInterval = 2f;         // a spot stays fresh for 4 s
        public const int SpotCap = 48;                // units refreshed per pass

        // GLAIVE's orbit rules are relative to its own 450 m circle: ranges are scaled onto it instead of copying the rules.
        private const float Scale = GlaiveProfile.OrbitRadius / OrbitRadius;

        /// <summary>Waypoint offset (GlaiveProfile.Lookahead long) that holds the 800 m counter-clockwise orbit.</summary>
        public static Vector3 OrbitOffset(Vector3 radial, Vector3 forward) => GlaiveProfile.OrbitOffset(radial * Scale, forward);

        /// <summary>Orbit entry: inside two radii of the centre, flying, clear of the launch.</summary>
        public static bool Capture(float range, float agl, float age, float speed) => GlaiveProfile.Capture(range * Scale, agl, age, speed);

        public static float Throttle(float speed) => GlaiveProfile.Throttle(speed + GlaiveProfile.CruiseSpeed - CruiseSpeed);

        /// <summary>
        /// Height of the next waypoint (one Lookahead on): OrbitHeight above the target, never under MinAgl over the
        /// ground below the glider, reached no steeper than MaxDive / MaxClimb.
        /// </summary>
        public static float AimHeight(float targetY, float groundY, float hereY) => Mathf.Clamp(
            Mathf.Max(targetY + OrbitHeight, groundY + MinAgl),
            hereY - GlaiveProfile.Lookahead * MaxDive, hereY + GlaiveProfile.Lookahead * MaxClimb);

        /// <summary>Transit until the orbit is captured, StationSeconds there, then Depart; Depart also starts in time for the hard cap.</summary>
        public static KestrelPhase Next(KestrelPhase phase, bool captured, float onStation, float age)
        {
            if (phase == KestrelPhase.Depart || age >= HardCap - DepartSeconds) return KestrelPhase.Depart;
            if (phase == KestrelPhase.Station) return onStation >= StationSeconds ? KestrelPhase.Depart : phase;
            return captured ? KestrelPhase.Station : phase;
        }

        /// <summary>Seconds of station time still to come: the full clock in transit, cut short by the hard cap.</summary>
        public static float StationLeft(KestrelPhase phase, float onStation, float age) => phase == KestrelPhase.Depart ? 0f
            : Mathf.Max(0f, Mathf.Min(StationSeconds - (phase == KestrelPhase.Station ? onStation : 0f), HardCap - DepartSeconds - age));

        /// <summary>Spot filter for a hostile unit: ground vehicles and ships inside the radius, never aircraft or missiles.</summary>
        public static bool MaySpot(float flatRange, bool vehicle, bool ship) => (vehicle || ship) && flatRange <= SpotRadius;

        /// <summary>
        /// Which of this pass's candidates to mark: <paramref name="current"/> (the index of the unit already marked, -1 when
        /// it is not among them) while it is still there, else the nearest to the orbit centre; -1 when there is none.
        /// </summary>
        public static int LasePick(int current, List<float> ranges)
        {
            if (current >= 0 && current < ranges.Count) return current;
            int best = -1;
            for (int i = 0; i < ranges.Count; i++)
                if (best < 0 || ranges[i] < ranges[best]) best = i;
            return best;
        }

        // One string per second on station, built on first use: the launcher HUD asks at 5 Hz.
        private static readonly string[] OnStation = new string[(int)StationSeconds + 1];

        public static string HudText(KestrelPhase phase, float stationLeft)
        {
            if (phase == KestrelPhase.Transit) return "KESTREL INBOUND";
            if (phase == KestrelPhase.Depart) return "KESTREL OFF STATION";
            int seconds = Mathf.Clamp(Mathf.CeilToInt(stationLeft), 0, OnStation.Length - 1);
            return OnStation[seconds] ??= "KESTREL ON STATION " + seconds / 60 + ":" + (seconds % 60).ToString("00", CultureInfo.InvariantCulture);
        }

        public static string HudDetail(int spotted, string lased) =>
            "SPOT " + spotted.ToString(CultureInfo.InvariantCulture) + (lased != null ? "  LASE " + lased.ToUpperInvariant() : "");
    }
}
