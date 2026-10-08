using System;

namespace BoscaliSummer.Modules.Hud.Domain
{
    /// <summary>What the threat row is told each tick. Ages are seconds since the event; +inf = never.</summary>
    internal struct ThreatPicture
    {
        public int Missiles;
        public float SinceLock, SinceSpike, SinceNewMissile;
        /// <summary>Nearest inbound missile (else the last locking emitter): bearing off the nose, degrees, + = right.</summary>
        public bool HasBearing;
        public float BearingDeg, ElevationDeg;
        public bool BearingIsMissile;

        public static ThreatPicture Clear => new ThreatPicture
        {
            SinceLock = float.PositiveInfinity, SinceSpike = float.PositiveInfinity, SinceNewMissile = float.PositiveInfinity,
        };
    }

    /// <summary>
    /// KaceyTronic-RWR style threat lamps (2026-10-07), pure. LOCK flashes for a second after a fresh lock then holds
    /// while locks keep arriving; SPKE (a search radar painting you) holds 1.5 s per ping; MSL flashes while anything is
    /// inbound and shows the count.
    /// </summary>
    internal static class HudThreats
    {
        public const int Count = 3;
        public const float LockHold = 2f, LockFlash = 1f, SpikeHold = 1.5f, Level = 10f;

        public static void Fill(in ThreatPicture t, Lamp[] lamps)
        {
            bool locked = t.SinceLock < LockHold;
            lamps[0] = new Lamp { Legend = "LOCK", Present = true, State = locked ? LampState.Warning : LampState.Dark, Flash = locked && t.SinceLock < LockFlash };
            lamps[1] = new Lamp { Legend = "SPKE", Present = true, State = t.SinceSpike < SpikeHold ? LampState.Caution : LampState.Dark };
            lamps[2] = new Lamp
            {
                Legend = t.Missiles > 1 ? "MSL " + Math.Min(t.Missiles, 9) : "MSL", Present = true,
                State = t.Missiles > 0 ? LampState.Warning : LampState.Dark, Flash = t.Missiles > 0,
            };
        }

        /// <summary>HI / LO / "" for a threat elevation in degrees.</summary>
        public static string HighLow(float elevationDeg) => elevationDeg > Level ? "HI" : elevationDeg < -Level ? "LO" : "";

        /// <summary>Bearing (deg, + right, ±180) and elevation (deg) of a point given in the aircraft's own frame
        /// (x right, y up, z forward).</summary>
        public static void Clock(float x, float y, float z, out float bearingDeg, out float elevationDeg)
        {
            bearingDeg = (float)(Math.Atan2(x, z) * 180.0 / Math.PI);
            elevationDeg = (float)(Math.Atan2(y, Math.Sqrt(x * x + z * z)) * 180.0 / Math.PI);
        }
    }

    internal enum ShotState { Empty, InFlight, Hit, Miss }

    /// <summary>
    /// The shot tracker (after NO_Tactitools' delivery checker), pure: one pip per own weapon released, in flight until it
    /// detonates (hit = struck armour) and kept 2.5 s after the outcome; a weapon still flying after two minutes counts as a
    /// miss. Fixed ring of <see cref="Capacity"/>; the oldest resolved pip makes room first.
    /// </summary>
    internal sealed class ShotLedger
    {
        public const int Capacity = 8;
        public const float ShowAfter = 2.5f, Timeout = 120f;

        private readonly int[] ids = new int[Capacity];
        private readonly ShotState[] states = new ShotState[Capacity];
        private readonly bool[] bombs = new bool[Capacity];
        private readonly float[] times = new float[Capacity];
        public int Version { get; private set; }

        public ShotState State(int i) => states[i];
        public bool IsBomb(int i) => bombs[i];

        public void Launch(int id, bool bomb, float now)
        {
            int slot = Array.IndexOf(states, ShotState.Empty);
            if (slot < 0) slot = OldestResolved();
            if (slot < 0) return;
            ids[slot] = id; bombs[slot] = bomb; states[slot] = ShotState.InFlight; times[slot] = now;
            Version++;
        }

        public void Detonate(int id, bool hit, float now)
        {
            for (int i = 0; i < Capacity; i++)
                if (states[i] == ShotState.InFlight && ids[i] == id)
                {
                    states[i] = hit ? ShotState.Hit : ShotState.Miss; times[i] = now; Version++;
                    return;
                }
        }

        public void Expire(float now)
        {
            for (int i = 0; i < Capacity; i++)
            {
                if (states[i] == ShotState.InFlight && now - times[i] > Timeout) { states[i] = ShotState.Miss; times[i] = now; Version++; }
                else if ((states[i] == ShotState.Hit || states[i] == ShotState.Miss) && now - times[i] > ShowAfter) { states[i] = ShotState.Empty; Version++; }
            }
        }

        public void Clear()
        {
            Array.Clear(states, 0, Capacity);
            Version++;
        }

        private int OldestResolved()
        {
            int best = -1;
            for (int i = 0; i < Capacity; i++)
                if ((states[i] == ShotState.Hit || states[i] == ShotState.Miss) && (best < 0 || times[i] < times[best])) best = i;
            return best;
        }
    }

    /// <summary>
    /// The spawn lamp test (the RWR's boot splash, as a real annunciator test), pure: lamps light one by one over
    /// <see cref="Sweep"/>, all flash together until <see cref="Hold"/>, then the board goes live.
    /// </summary>
    internal static class LampTest
    {
        public const float Sweep = 0.9f, Hold = 1.5f, Online = 2.6f;

        /// <summary>True while the test owns the lamps.</summary>
        public static bool Running(float t) => t >= 0f && t < Hold;

        /// <summary>Whether lamp <paramref name="index"/> of <paramref name="count"/> is lit <paramref name="t"/> s into the test.</summary>
        public static bool Lit(int index, int count, float t)
        {
            if (!Running(t)) return false;
            if (t < Sweep) return t >= Sweep * index / Math.Max(1, count);
            return (int)((t - Sweep) * 6f) % 2 == 0;
        }

        public static string Caption(float t) => t < 0f || t >= Online ? null : Running(t) ? "LAMP TEST" : "SYSTEMS ONLINE";
    }

    /// <summary>Bank and sideslip for the third-person bank &amp; slip indicator (after NO_Tactitools), pure.</summary>
    internal static class Attitude
    {
        /// <summary>Bank in degrees, + = right wing down, from the world-up (y) components of the aircraft's right and up axes.</summary>
        public static float Bank(float rightY, float upY) => (float)(Math.Atan2(-rightY, upY) * 180.0 / Math.PI);

        /// <summary>Sideslip in degrees, + = air from the right (nose left of the flight path), from velocity in the
        /// aircraft frame. Zero below 20 m/s, where it is noise.</summary>
        public static float Slip(float vx, float vz) =>
            vx * vx + vz * vz < 400f ? 0f : (float)(Math.Atan2(vx, Math.Abs(vz)) * 180.0 / Math.PI);
    }
}
