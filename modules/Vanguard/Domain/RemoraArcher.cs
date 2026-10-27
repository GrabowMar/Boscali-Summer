using UnityEngine;

namespace BoscaliSummer.Modules.Vanguard.Domain
{
    internal enum RemoraPhase : byte { Escort, Engage, Ram }

    /// <summary>What a REMORA is shooting at, best first: missiles homing on the launcher, aircraft locking it, the pilot's designation, the nearest hostile aircraft.</summary>
    internal enum AirCue : byte { Missile, Locker, Designated, Nearest }

    /// <summary>
    /// REMORA "ARCHER" (2026-10-27: the air fighter of the pair; the MANTA does the ground). Two mini-AAMs, one per rail
    /// (bit per round, L then R), shoot-look-shoot, spread over targets. It hunts on its own: missiles homing on the launcher,
    /// aircraft locking it, the pilot's air designation, any hostile aircraft near the launcher, or near the point it was sent to
    /// (<see cref="AreaRange"/>). Ground designations are not its job. Empty, a fresh air designation still rams the airframe into
    /// the target; the cue its volley was fired on only sends it back to escort. Flares for IR seekers on drone or launcher.
    /// </summary>
    internal static class RemoraArcher
    {
        public const int Rounds = 2;
        public const float FireRange = 12000f;
        public const float FireCone = 60f;      // deg off the drone's nose
        public const float DefenceCone = 120f;  // defence shots at inbound missiles: the round turns hard after launch
        public const float FireInterval = 1.5f;
        public const float LookSeconds = 6f;    // shoot-look-shoot: a second round at the same target waits this long for the first
        public const float DefendRange = 9000f; // m from the drone: inbound missiles inside this are auto-engaged
        public const float GuardRange = 12000f; // an undesignated hostile aircraft this close to the launcher is hunted
        public const float AreaRange = 8000f;   // sent to a point (WPT): hostile aircraft this close to it are hunted
        public const float LockerRange = 25000f;
        public const float AiLeash = 20000f;    // AI launchers: engage only targets this close to the launcher
        public const float SensorRange = 15000f; // hostile aircraft in line of sight inside this are revealed to the faction
        public const float SensorInterval = 2f;
        public const float ScanInterval = 0.5f;
        public const float RamFuseMin = 6f;     // m to the bombed target centre (blast frags fall off with the cube of range)
        public const float RamFuseMax = 12f;
        public const float RamHoming = 2500f;   // m: inside this the bomb steers on the target itself every physics tick
        public const float HitRadius = 40f;     // an AAM that dies this close to its target scored
        public const float ElevonLimit = 35f;   // deg (25 before the 2026-10-27 agility pass)
        public const float LaunchTurn = 30f;    // deg a fired round is turned from the drone's nose toward its target
        public const int FlareBursts = 6;
        public const float FlareRange = 3000f;  // m: IR missiles this close to drone or launcher draw a burst
        public const float FlareClosing = 80f;  // m/s: slower is geometry, not a shot
        public const float FlareChance = 0.65f; // per IR missile per burst: it drops the lock and flies on unguided
        public const float FlareInterval = 1f;  // s between bursts
        // ECM burst (2026-10-27, the REMORA's secondary kit): the radar twin of the flares. A radar missile closing on the drone
        // or its launcher inside JamRange spends a burst; each radar missile inside JamRange of the drone then rolls JamChance
        // to lose its lock and fly on unguided.
        public const int JamBursts = 3;
        public const float JamRange = 5000f;
        public const float JamChance = 0.5f;
        public const float JamInterval = 2.5f;
        // Top throttle (2026-10-20, user: "make remoras faster"). The game's missile throttle is a plain thrust multiplier
        // (Missile.Motor.Thrust), so 2 = twice the bundle's 4200 N, ~1.4x the top speed, with no bundle rebuild and none of the
        // radar visibility a bigger rated motor would add (visibility follows the rated thrust, not the throttle).
        public const float Boost = 2f;

        // Energy (2026-10-27): a 30 g turn dumps 400 m/s in 3 s and can leave the airframe mushing nose-high at 40 m/s, where drag
        // beats even the boost. Under RecoverSpeed the drone unloads and dives at the boost until RecoveredSpeed, then fights again.
        public const float RecoverSpeed = 110f, RecoveredSpeed = 200f;

        public static int Left(int firedMask) => DroneRounds.Left(firedMask, Rounds);

        /// <summary>
        /// The target is always an aircraft or a missile (the REMORA never takes a ground one). designated: it is the pilot's
        /// designation. cueSpent: this designation already had the volley.
        /// </summary>
        public static RemoraPhase Decide(bool hasTarget, int left, bool designated, bool cueSpent, bool ramMode = false, bool gunReady = false)
        {
            if (!hasTarget) return RemoraPhase.Escort;
            if (ramMode) return RemoraPhase.Ram;   // RAM MODE (C menu): the airframe is the weapon, the AAMs stay on the rails
            if (left > 0 || gunReady) return RemoraPhase.Engage;   // the gun keeps an empty REMORA in the fight against aircraft
            return designated && !cueSpent ? RemoraPhase.Ram : RemoraPhase.Escort;
        }

        /// <summary>a beats b: the better cue wins, the nearer one breaks a tie.</summary>
        public static bool Outranks(AirCue a, float rangeA, AirCue b, float rangeB) => a != b ? a < b : rangeA < rangeB;

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

        // Nose gun (2026-10-27): a vanilla 20 mm on the Muzzle joint, fixed forward. Bursts at aircraft only, inside GunRange and
        // GunCone of the lead point; the drone points its nose at the lead point once inside GunRange.
        public const int GunRounds = 150;
        public const float GunRange = 1500f;
        public const float GunCone = 4f;         // deg between the nose and the lead point
        public const float GunInterval = 0.05f;  // s between rounds (20 per second)
        public const int GunBurst = 12;          // rounds per burst
        public const float GunPause = 0.7f;      // s between bursts
        public const float GunSpread = 0.35f;    // deg

        /// <summary>The gun may fire this round: an aircraft in range and cone, rounds left, the interval (or the burst pause) spent.</summary>
        public static bool CanGun(bool aircraft, float range, float offDeg, int ammo, float sinceShot, int burstShots) =>
            aircraft && ammo > 0 && range <= GunRange && offDeg <= GunCone &&
            sinceShot >= (burstShots > 0 && burstShots % GunBurst == 0 ? GunPause : GunInterval);

        /// <summary>A radar missile closing this fast, this close, with bursts left and the last one spent.</summary>
        public static bool ShouldJam(bool radarSeeker, float range, float closing, int burstsLeft, float sinceJam) =>
            radarSeeker && burstsLeft > 0 && range <= JamRange && closing >= FlareClosing && sinceJam >= JamInterval;

        /// <summary>The launcher's held HUD line over all its drones (worst phase wins).</summary>
        public static string Status(int ready, int capacity, RemoraPhase worst)
        {
            switch (worst)
            {
                case RemoraPhase.Ram: return "REMORA RAMMING — INBOUND";
                case RemoraPhase.Engage: return "ARCHER ENGAGING " + ready + "/" + capacity;
                default: return ready > 0 ? "ARCHER " + ready + "/" + capacity + " READY" : "REMORA WINCHESTER";
            }
        }

        /// <summary>Elevon deflection, + = trailing edge up: both lift in a banked turn, split with roll rate.</summary>
        public static void Elevons(float bankDeg, float rollRateDeg, out float left, out float right)
        {
            float pull = Mathf.Min(Mathf.Abs(bankDeg) * 0.22f, 14f);
            float roll = Mathf.Clamp(rollRateDeg * 0.35f, -21f, 21f);
            left = Mathf.Clamp(pull - roll, -ElevonLimit, ElevonLimit);
            right = Mathf.Clamp(pull + roll, -ElevonLimit, ElevonLimit);
        }
    }

    /// <summary>Round bookkeeping both drones share: a bit per round, rounds leave alternately from the two rails (0 = MissileL, 1 = MissileR, 2 = L, 3 = R).</summary>
    internal static class DroneRounds
    {
        public static int Left(int firedMask, int rounds) => rounds - BitCount(firedMask & ((1 << rounds) - 1));

        /// <summary>Index of the next round to leave, -1 when empty.</summary>
        public static int Next(int firedMask, int rounds)
        {
            for (int i = 0; i < rounds; i++) if ((firedMask & (1 << i)) == 0) return i;
            return -1;
        }

        public static int PylonOf(int round) => round & 1;

        /// <summary>A rail's model goes when every one of its rounds has left (one on a two-round REMORA, two on a MANTA).</summary>
        public static bool RailSpent(int firedMask, int pylon, int rounds)
        {
            for (int round = pylon; round < rounds; round += 2) if ((firedMask & (1 << round)) == 0) return false;
            return true;
        }

        /// <summary>Shoot-look-shoot: the previous round is still flying at this very target and has had less than lookSeconds.</summary>
        public static bool HoldForResult(bool roundInFlight, float sinceRound, float lookSeconds) => roundInFlight && sinceRound < lookSeconds;

        private static int BitCount(int value)
        {
            int n = 0;
            for (; value != 0; value &= value - 1) n++;
            return n;
        }
    }
}
