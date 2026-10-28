using System.Collections.Generic;
using UnityEngine;

namespace BoscaliSummer.Modules.Vanguard.Domain
{
    /// <summary>What a MANTA strikes, best first: the pilot's designation, a ground shooter that fired at the launcher, the nearest hostile near the point it was sent to.</summary>
    internal enum GroundCue : byte { Designated, Shooter, Area }

    /// <summary>
    /// XQ-90 MANTA (2026-10-27: the ground striker of the pair; the REMORA does the air): 2.3x the REMORA's airframe, four
    /// fire-and-forget AGMs (bit per round, L R L R), shoot-look-shoot, spread over targets. It strikes the pilot's ground
    /// designation, any ground launcher that fires a missile at its launcher (SEAD escort), and, sent to a point (WPT), hostile
    /// vehicles and ships around it. Empty, the designation is bombed with the airframe's own 150 kg warhead. It never takes an
    /// air target; it keeps twelve flare bursts, radar seduction and a passive sensor that reveals ground units.
    /// </summary>
    internal static class MantaRules
    {
        // Airframe (RuntimeStores clone of the REMORA): thrust and fin area scale with the frontal area, fuel with the mass.
        public const float Scale = 2.3f;
        public const float Mass = 1500f;
        public const float Thrust = 22200f;     // 4200 N x Scale^2, so the speed stays the REMORA's
        public const float BurnTime = 900f;
        public const float FuelFraction = 0.4f; // of the mass, as the bundle caps every Vanguard motor
        public const float RadarSize = 0.2f;
        public const float GLimit = 9f;
        public const float TurnRate = 24f;
        public const float BlastYield = 150f;   // the ram on a ground designation: REMORA's 60 kg / 300 pierce x 2.5
        public const float PierceDamage = 750f;
        public const float Lifetime = 900f;
        public const int PrefabHash = 0x56474D54; // "VGMT": identical on every peer (Mirage prefab identity)

        // Fire control.
        public const int Rounds = 4;
        public const float FireRange = 9000f;
        public const float FireCone = 60f;      // deg off the nose
        public const float FireInterval = 1.5f;
        public const float LookSeconds = 10f;   // shoot-look-shoot: a second round at the same target waits this long for the first
        public const float ShooterRange = 20000f; // a ground launcher whose missile homes on the launcher, this close to the drone, is struck
        public const float AreaRange = 4000f;   // sent to a point (WPT): hostile ground units this close to it are struck
        public const float AiLeash = 25000f;
        public const int FlareBursts = 12;

        // Seduction of radar missiles aimed at the launcher: a wider circle and a better chance than the REMORA's 6 km / 30 %.
        public const float SeductionRange = 9000f;
        public const float SeductionChance = 0.5f;

        // Passive sensor: hostile ground units with line of sight inside this are revealed to the launcher's faction.
        public const float SensorRange = 10000f;
        public const float SensorInterval = 2f;
        public const float ScanInterval = 0.5f;

        // Wing slots: stations the MANTA claims. A partner station must be this close to each lead hardpoint and off the centreline.
        public const float CentreX = 0.6f;
        public const float MaxGap = 14f;

        public static int Left(int firedMask) => DroneRounds.Left(firedMask, Rounds);

        /// <summary>The target is always a ground unit. Rounds left strike it; empty, only the pilot's designation is bombed with the airframe.</summary>
        public static RemoraPhase Decide(bool hasTarget, int left, bool designated)
        {
            if (!hasTarget) return RemoraPhase.Escort;
            if (left > 0) return RemoraPhase.Engage;
            return designated ? RemoraPhase.Ram : RemoraPhase.Escort;
        }

        public static bool CanFire(float range, float offNoseDeg, float sinceShot, int left, bool tracked) =>
            tracked && left > 0 && range <= FireRange && offNoseDeg <= FireCone && sinceShot >= FireInterval;

        public static bool HoldsForTrack(float range, float offNoseDeg, int left, bool tracked) =>
            !tracked && left > 0 && range <= FireRange && offNoseDeg <= FireCone;

        public static bool Seduces(string seekerType, float decoyRange, float roll) =>
            SeductionRule.IsRadarSeeker(seekerType) && decoyRange <= SeductionRange && roll < SeductionChance;

        public static string Status(int ready, int capacity, RemoraPhase worst)
        {
            switch (worst)
            {
                case RemoraPhase.Ram: return "MANTA GUIDED BOMB — INBOUND";
                case RemoraPhase.Engage: return "MANTA STRIKING " + ready + "/" + capacity;
                default: return ready > 0 ? "MANTA " + ready + "/" + capacity + " READY" : "MANTA WINCHESTER";
            }
        }

        /// <summary>
        /// The station a MANTA on <paramref name="lead"/> also occupies: the free, off-centreline set whose hardpoints sit closest to
        /// the lead's on the same wing side. <paramref name="sets"/> holds each set's hardpoints in aircraft-local metres.
        /// -1 when there is none (the lead is on the centreline, or no free station is within <see cref="MaxGap"/>).
        /// </summary>
        public static int Partner(int lead, Vector3[][] sets, ICollection<int> taken)
        {
            if (lead < 0 || lead >= sets.Length || sets[lead].Length == 0 || OnCentreline(sets[lead])) return -1;
            int best = -1;
            float bestCost = float.MaxValue;
            for (int i = 0; i < sets.Length; i++)
            {
                if (i == lead || (taken != null && taken.Contains(i)) || sets[i].Length == 0 || OnCentreline(sets[i])) continue;
                float cost = 0f;
                bool reach = true;
                foreach (Vector3 point in sets[lead])
                {
                    float near = Nearest(point, sets[i]);
                    if (near > MaxGap) { reach = false; break; }
                    cost += near;
                }
                if (reach && cost < bestCost) { best = i; bestCost = cost; }
            }
            return best;
        }

        public static bool OnCentreline(Vector3[] points)
        {
            foreach (Vector3 point in points) if (Mathf.Abs(point.x) < CentreX) return true;
            return false;
        }

        private static float Nearest(Vector3 from, Vector3[] others)
        {
            float best = float.MaxValue;
            foreach (Vector3 other in others)
                if (other.x * from.x > 0f) best = Mathf.Min(best, Vector3.Distance(from, other));
            return best;
        }
    }
}
