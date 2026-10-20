using UnityEngine;

namespace BoscaliSummer.Modules.Vanguard.Domain
{
    internal enum RemoraPhase : byte { Escort, Engage, Ram }

    /// <summary>
    /// REMORA "ARCHER" fire control: two mini-AAMs (MissileL = bit 0, MissileR = bit 1) fired on the launcher's
    /// designation, one per <see cref="FireInterval"/>, inside <see cref="FireRange"/> and the nose cone. The
    /// designation picks the role: an aircraft or missile is hunted, a ground unit turns the drone itself into a
    /// guided bomb on its 60 kg warhead, and with no designation the drone guards the launcher — it shoots down
    /// inbound missiles with the same rounds and pops its <see cref="FlareBursts"/> flare bursts at IR seekers.
    /// Empty, a fresh air designation still rams; the cue its volley was fired on only sends it back to escort.
    /// </summary>
    internal static class RemoraArcher
    {
        public const int Rounds = 2;
        public const float FireRange = 12000f;
        public const float FireCone = 60f;      // deg off the drone's nose
        public const float DefenceCone = 120f;  // defence shots at inbound missiles: the round turns hard after launch
        public const float FireInterval = 1.5f;
        public const float DefendRange = 9000f; // m from the drone: inbound missiles inside this are auto-engaged
        public const float AiLeash = 20000f;    // AI launchers: engage only targets this close to the launcher
        public const float RamFuseMin = 6f;     // m to the bombed target centre (blast frags fall off with the cube of range)
        public const float RamFuseMax = 12f;
        public const float RamHoming = 2500f;   // m: inside this the bomb steers on the target itself every physics tick
        public const float HitRadius = 40f;     // an AAM that dies this close to its target scored
        public const float ElevonLimit = 25f;   // deg
        public const float LaunchTurn = 30f;    // deg a fired round is turned from the drone's nose toward its target
        public const int FlareBursts = 6;
        public const float FlareRange = 3000f;  // m: IR missiles this close to drone or launcher draw a burst
        public const float FlareClosing = 80f;  // m/s: slower is geometry, not a shot
        public const float FlareChance = 0.65f; // per IR missile per burst: it drops the lock and flies on unguided
        public const float FlareInterval = 1f;  // s between bursts
        // Top throttle (2026-10-20, user: "make remoras faster"). The game's missile throttle is a plain thrust multiplier
        // (Missile.Motor.Thrust), so 2 = twice the bundle's 4200 N, ~1.4x the top speed, with no bundle rebuild and none of the
        // radar visibility a bigger rated motor would add (visibility follows the rated thrust, not the throttle).
        public const float Boost = 2f;

        public static int Left(int firedMask) => Rounds - ((firedMask & 1) + ((firedMask >> 1) & 1));

        /// <summary>Pylon for the next round: 0 = MissileL, 1 = MissileR, -1 = empty.</summary>
        public static int NextPylon(int firedMask) => (firedMask & 1) == 0 ? 0 : (firedMask & 2) == 0 ? 1 : -1;

        /// <summary>hunt: the cue is an aircraft or a missile. A ground cue is bombed with the airframe itself.</summary>
        public static RemoraPhase Decide(bool hasTarget, bool hunt, int left, bool cueSpent)
        {
            if (!hasTarget) return RemoraPhase.Escort;
            if (hunt && left > 0) return RemoraPhase.Engage;
            return cueSpent ? RemoraPhase.Escort : RemoraPhase.Ram;
        }

        /// <summary>
        /// Bomb fuse radius: tight so the blast lands on the hull, wide enough that a fast pass always has a physics tick
        /// inside it (the 2 x radius window is 1.2 steps of closing speed).
        /// </summary>
        public static float FuseRadius(float closingSpeed, float step) =>
            Mathf.Clamp(Mathf.Max(closingSpeed, 0f) * step * 0.6f, RamFuseMin, RamFuseMax);

        /// <summary>In range and cone with rounds left but no faction track: the volley is waiting on a sensor.</summary>
        public static bool HoldsForTrack(float range, float offNoseDeg, int left, bool tracked) =>
            !tracked && left > 0 && range <= FireRange && offNoseDeg <= FireCone;

        public static bool CanFire(float range, float offNoseDeg, float sinceShot, int left, bool tracked) =>
            CanFire(range, offNoseDeg, sinceShot, left, tracked, FireCone);

        public static bool CanFire(float range, float offNoseDeg, float sinceShot, int left, bool tracked, float coneDeg) =>
            tracked && left > 0 && range <= FireRange && offNoseDeg <= coneDeg && sinceShot >= FireInterval;

        /// <summary>An inbound missile this close to the drone, with rounds left, is a defence shot.</summary>
        public static bool CanDefend(float range, int left) => left > 0 && range <= DefendRange;

        /// <summary>An IR missile closing this fast, this close, with bursts left and the last burst spent.</summary>
        public static bool ShouldFlare(float range, float closing, int burstsLeft, float sincePop) =>
            burstsLeft > 0 && range <= FlareRange && closing >= FlareClosing && sincePop >= FlareInterval;

        /// <summary>The launcher's held HUD line over all its drones (worst phase wins).</summary>
        public static string Status(int ready, int capacity, RemoraPhase worst)
        {
            switch (worst)
            {
                case RemoraPhase.Ram: return "REMORA GUIDED BOMB — INBOUND";
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
