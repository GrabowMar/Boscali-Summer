using UnityEngine;

namespace BoscaliSummer.Modules.Vanguard.Domain
{
    internal enum RemoraPhase : byte { Escort, Engage, Ram }

    /// <summary>
    /// REMORA "ARCHER" fire control: two mini-AAMs (MissileL = bit 0, MissileR = bit 1) fired on the launcher's
    /// designation, one per <see cref="FireInterval"/>, inside <see cref="FireRange"/> and the nose cone. Empty, a
    /// fresh designation turns the drone into a ram; the cue its volley was fired on only sends it back to escort.
    /// </summary>
    internal static class RemoraArcher
    {
        public const int Rounds = 2;
        public const float FireRange = 12000f;
        public const float FireCone = 60f;      // deg off the drone's nose
        public const float FireInterval = 1.5f;
        public const float AiLeash = 20000f;    // AI launchers: engage only targets this close to the launcher
        public const float RamFuseMin = 6f;     // m to the rammed target centre (blast frags fall off with the cube of range)
        public const float RamFuseMax = 12f;
        public const float RamHoming = 2500f;   // m: inside this the ram steers on the target itself every physics tick
        public const float HitRadius = 40f;     // an AAM that dies this close to its target scored
        public const float ElevonLimit = 25f;   // deg
        public const float LaunchTurn = 30f;    // deg a fired round is turned from the drone's nose toward its target

        public static int Left(int firedMask) => Rounds - ((firedMask & 1) + ((firedMask >> 1) & 1));

        /// <summary>Pylon for the next round: 0 = MissileL, 1 = MissileR, -1 = empty.</summary>
        public static int NextPylon(int firedMask) => (firedMask & 1) == 0 ? 0 : (firedMask & 2) == 0 ? 1 : -1;

        public static RemoraPhase Decide(bool hasTarget, bool air, int left, bool cueSpent)
        {
            if (!hasTarget) return RemoraPhase.Escort;
            if (air && left > 0) return RemoraPhase.Engage;
            return cueSpent ? RemoraPhase.Escort : RemoraPhase.Ram;
        }

        /// <summary>
        /// Ram fuse radius: tight so the blast lands on the hull, wide enough that a fast pass always has a physics tick
        /// inside it (the 2 x radius window is 1.2 steps of closing speed).
        /// </summary>
        public static float FuseRadius(float closingSpeed, float step) =>
            Mathf.Clamp(Mathf.Max(closingSpeed, 0f) * step * 0.6f, RamFuseMin, RamFuseMax);

        /// <summary>In range and cone with rounds left but no faction track: the volley is waiting on a sensor.</summary>
        public static bool HoldsForTrack(float range, float offNoseDeg, int left, bool tracked) =>
            !tracked && left > 0 && range <= FireRange && offNoseDeg <= FireCone;

        public static bool CanFire(float range, float offNoseDeg, float sinceShot, int left, bool tracked) =>
            tracked && left > 0 && range <= FireRange && offNoseDeg <= FireCone && sinceShot >= FireInterval;

        /// <summary>The launcher's held HUD line over all its drones (worst phase wins).</summary>
        public static string Status(int ready, int capacity, RemoraPhase worst)
        {
            switch (worst)
            {
                case RemoraPhase.Ram: return "REMORA WINCHESTER — RAMMING";
                case RemoraPhase.Engage: return "ARCHER ENGAGING " + ready + "/" + capacity;
                default: return ready > 0 ? "ARCHER " + ready + "/" + capacity + " READY" : "REMORA WINCHESTER";
            }
        }

        /// <summary>Elevon deflection, + = trailing edge up: both lift in a banked turn, split with roll rate.</summary>
        public static void Elevons(float bankDeg, float rollRateDeg, out float left, out float right)
        {
            float pull = Mathf.Min(Mathf.Abs(bankDeg) * 0.15f, 10f);
            float roll = Mathf.Clamp(rollRateDeg * 0.25f, -15f, 15f);
            left = Mathf.Clamp(pull - roll, -ElevonLimit, ElevonLimit);
            right = Mathf.Clamp(pull + roll, -ElevonLimit, ElevonLimit);
        }
    }
}
