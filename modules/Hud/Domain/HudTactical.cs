using System;

namespace BoscaliSummer.Modules.Hud.Domain
{
    /// <summary>What the threat row is told each tick. Ages are seconds since the event; +inf = never.</summary>
    internal struct ThreatPicture
    {
        public int Missiles;
        public float SinceLock, SinceSpike;
        /// <summary>Nearest inbound missile (else the last locking emitter): bearing off the nose, degrees, + = right.</summary>
        public bool HasBearing;
        public float BearingDeg, ElevationDeg;
        public bool BearingIsMissile;

        public static ThreatPicture Clear => new ThreatPicture
        {
            SinceLock = float.PositiveInfinity, SinceSpike = float.PositiveInfinity,
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

        /// <summary>
        /// The radar-warning page keeps a contact until <paramref name="hold"/> seconds after its newest warning (a lock or a search ping), so a radar that pings every few seconds stays
        /// between pings. The lamps keep their own shorter holds (<see cref="LockHold"/>, <see cref="SpikeHold"/>); the option's range (BoardOptions.HoldMin) never goes under them.
        /// </summary>
        public static bool Retained(float now, float lockAt, float spikeAt, float hold) => now - Math.Max(lockAt, spikeAt) < hold;

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

    /// <summary>
    /// How a delivery ended. Defeated is a miss with a reason: the seeker had lost the target while that target was still alive (a flare, chaff, a notch or the target
    /// breaking the lock) and the weapon never reached it.
    /// </summary>
    internal enum ShotState { Empty, InFlight, Hit, Miss, Defeated }

    /// <summary>What counts as a delivery and what counts as a hit, pure.</summary>
    internal static class ShotRules
    {
        /// <summary>
        /// <c>WeaponInfo.blastDamage</c> below this is a decoy or a utility munition, not a delivery: MALD-X/J (1), ALE-X (2),
        /// AEGIS darts (0.2) and GLAIVE gun pods (0) never earn a pip.
        /// ponytail: a damage threshold stands in for a flag the game does not have; upgrade to an explicit per-weapon flag.
        /// </summary>
        public const float MinDeliveryBlast = 3f;

        public static bool IsDelivery(float blastDamage, bool jammer) => !jammer && blastDamage >= MinDeliveryBlast;

        /// <summary>
        /// The detonation RPC's flags: it struck armour (a vehicle, building or ship), or it went off within 200 m of its
        /// target (<c>useUnit</c>). A terrain-only impact has neither and is a miss; a ground target's own impact also flags
        /// the terrain, so <c>hitTerrain</c> must not veto a hit (the spec's rule: <c>hitArmor || useUnit</c>).
        /// </summary>
        public static bool Hit(bool hitArmor, bool useUnit) => hitArmor || useUnit;
    }

    /// <summary>
    /// The shot tracker (after NO_Tactitools' delivery checker), pure: one entry per own delivery released, in flight until it detonates (<see cref="ShotRules.Hit"/>)
    /// and kept <see cref="KeepSeconds"/> after the outcome for the SHOT page's timeline. A weapon the game no longer has in flight for <see cref="LostGrace"/> without a
    /// detonation counts as a miss; a cruise missile in the air for twenty minutes stays in flight. Fixed ring of <see cref="Capacity"/>; the oldest resolved entry makes room first.
    /// An entry keeps the weapon's short name, when it left and, once resolved, where it ended (global metres).
    /// </summary>
    internal sealed class ShotLedger
    {
        public const int Capacity = 8;
        public const float KeepSeconds = 60f, LostGrace = 3f;

        private readonly int[] ids = new int[Capacity];
        private readonly ShotState[] states = new ShotState[Capacity];
        private readonly bool[] bombs = new bool[Capacity], placed = new bool[Capacity], defeatedFlag = new bool[Capacity];
        private readonly string[] names = new string[Capacity];
        private readonly long[] order = new long[Capacity];
        private readonly float[] times = new float[Capacity], seen = new float[Capacity], x = new float[Capacity], y = new float[Capacity], z = new float[Capacity];
        private long next;
        public int Version { get; private set; }

        public ShotState State(int i) => states[i];
        public bool IsBomb(int i) => bombs[i];
        public int Id(int i) => ids[i];
        public string Name(int i) => names[i];

        /// <summary>Seconds-clock of the resolution (hit, miss, defeat); the launch time while still in flight.</summary>
        public float Time(int i) => times[i];

        /// <summary>The slot of the entry with this id, -1 when none.</summary>
        public int IndexOf(int id)
        {
            for (int i = 0; i < Capacity; i++) if (states[i] != ShotState.Empty && ids[i] == id) return i;
            return -1;
        }

        /// <summary>The entries in the order they were launched, oldest first: slot indices into <paramref name="into"/>; returns how many.</summary>
        public int InOrder(int[] into)
        {
            int n = 0;
            for (int i = 0; i < Capacity && n < into.Length; i++) if (states[i] != ShotState.Empty) into[n++] = i;
            for (int a = 1; a < n; a++)
                for (int b = a; b > 0 && order[into[b]] < order[into[b - 1]]; b--) { int t = into[b]; into[b] = into[b - 1]; into[b - 1] = t; }
            return n;
        }

        /// <summary>Where the entry ended (false while in flight or when the game gave no place).</summary>
        public bool TryPlace(int i, out float px, out float py, out float pz)
        {
            px = x[i]; py = y[i]; pz = z[i];
            return states[i] != ShotState.Empty && states[i] != ShotState.InFlight && placed[i];
        }

        public void Launch(int id, bool bomb, string name, float now)
        {
            int slot = Array.IndexOf(states, ShotState.Empty);
            if (slot < 0) slot = OldestResolved();
            if (slot < 0) return;
            ids[slot] = id; bombs[slot] = bomb; names[slot] = name; states[slot] = ShotState.InFlight; times[slot] = now; seen[slot] = now;
            order[slot] = ++next; placed[slot] = false; defeatedFlag[slot] = false;
            Version++;
        }

        /// <summary>The weapon with this id has lost its target while the target is alive (or, <paramref name="lost"/> false, has it back): a miss while it is lost is a defeat.</summary>
        public void MarkLost(int id, bool lost = true)
        {
            int i = IndexOf(id);
            if (i >= 0 && states[i] == ShotState.InFlight) defeatedFlag[i] = lost;
        }

        /// <summary>The weapon detonated (or was seen to end) at the global place (<paramref name="px"/>, <paramref name="py"/>, <paramref name="pz"/>), NaN when unknown.</summary>
        public void Detonate(int id, bool hit, float now, float px = float.NaN, float py = float.NaN, float pz = float.NaN)
        {
            int i = IndexOf(id);
            if (i < 0 || states[i] != ShotState.InFlight) return;
            Resolve(i, hit ? ShotState.Hit : defeatedFlag[i] ? ShotState.Defeated : ShotState.Miss, now, px, py, pz);
        }

        /// <summary><paramref name="flying"/> says whether the game still has the weapon with that id in the air.</summary>
        public void Expire(float now, Func<int, bool> flying)
        {
            for (int i = 0; i < Capacity; i++)
            {
                if (states[i] == ShotState.InFlight)
                {
                    if (flying != null && flying(ids[i])) seen[i] = now;
                    else if (now - seen[i] > LostGrace) Resolve(i, defeatedFlag[i] ? ShotState.Defeated : ShotState.Miss, now, float.NaN, float.NaN, float.NaN);
                }
                else if (states[i] != ShotState.Empty && now - times[i] > KeepSeconds) { states[i] = ShotState.Empty; Version++; }
            }
        }

        public void Clear()
        {
            Array.Clear(states, 0, Capacity);
            Version++;
        }

        private void Resolve(int i, ShotState state, float now, float px, float py, float pz)
        {
            states[i] = state;
            times[i] = now;
            placed[i] = !float.IsNaN(px);
            x[i] = px; y[i] = py; z[i] = pz;
            Version++;
        }

        private int OldestResolved()
        {
            int best = -1;
            for (int i = 0; i < Capacity; i++)
                if (states[i] != ShotState.Empty && states[i] != ShotState.InFlight && (best < 0 || times[i] < times[best])) best = i;
            return best;
        }
    }

    /// <summary>
    /// The spawn lamp test (the RWR's boot splash, as a real annunciator test), pure (LAYOUT.md 3.4): for the first <see cref="Hold"/> seconds lamp <c>i</c> is lit once <c>t</c> passes
    /// <see cref="Step"/> x <c>i</c>, and stays lit; then the board goes live.
    /// </summary>
    internal static class LampTest
    {
        public const float Step = 0.1f, Hold = 1.2f, Online = 2.6f;

        /// <summary>True while the test owns the lamps.</summary>
        public static bool Running(float t) => t >= 0f && t < Hold;

        /// <summary>Whether lamp <paramref name="index"/> is lit <paramref name="t"/> s into the test.</summary>
        public static bool Lit(int index, float t) => Running(t) && t > Step * index;

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
