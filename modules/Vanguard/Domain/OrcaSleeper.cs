using System.Globalization;
using UnityEngine;

namespace BoscaliSummer.Modules.Vanguard.Domain
{
    /// <summary>ORCA HUD events, sent server -> launcher's client (OrcaNet). Byte values are wire format.</summary>
    internal enum OrcaCall : byte { Running, Set, Awake, Hit, Expired }

    /// <summary>
    /// ORCA "SLEEPER": no ship to attack -> glide to the drop point, sink and wait as a mine for a hostile hull.
    /// </summary>
    internal static class OrcaSleeper
    {
        public const float AttackRadius = 2000f;  // drop point this close to a hostile hull = attack run, not a mine
        public const float DiveRange = 1500f;     // sleeper glide pitches down onto the drop point from here
        public const float SleepDepth = -35f;
        public const float SleepSeconds = 600f;
        public const float WakeRange = 1200f;     // flat metres from the sleeper to a hostile hull
        public const float ScanInterval = 1f;
        public const float SettleRate = 1.5f;     // 1/s: a 200 m/s splash coasts ~130 m before it hangs still

        /// <summary>No-lock drop point distance ahead of the launcher: farther glide from higher up.</summary>
        public static float DropRange(float launchAltitude) => Mathf.Clamp(launchAltitude * 2.5f, 3000f, 12000f);

        /// <summary>Sleep depth: -35 m, but 4 m clear of a shallow seabed and never above -8 m.</summary>
        public static float RestDepth(float seabedY) => Mathf.Min(-8f, Mathf.Max(SleepDepth, seabedY + 4f));

        public static bool Expired(float slept) => slept >= SleepSeconds;

        /// <summary>Horizontal coast after the splash decays exponentially to a hover.</summary>
        public static Vector3 Settle(Vector3 flatVelocity, float dt) => flatVelocity * Mathf.Exp(-SettleRate * dt);

        /// <summary>Pump-jet spin from the run speed; stopped while asleep.</summary>
        public static float RotorDegPerSec(float speed) => speed < 2f ? 0f : Mathf.Clamp(speed * 16f, 180f, 1440f);

        /// <summary>Surface bubble trail: only a torpedo actually running under water leaves one.</summary>
        public static bool Bubbles(float globalY, float speed) => globalY < 0f && speed > 20f;

        public static string Line(OrcaCall call, float km)
        {
            switch (call)
            {
                case OrcaCall.Set: return "SLEEPER SET " + km.ToString("F1", CultureInfo.InvariantCulture) + " KM";
                case OrcaCall.Awake: return "SLEEPER AWAKE — RUN";
                case OrcaCall.Hit: return "ORCA HIT";
                case OrcaCall.Expired: return "SLEEPER SCUTTLED";
                default: return "ORCA IN WATER — RUN";
            }
        }
    }
}
