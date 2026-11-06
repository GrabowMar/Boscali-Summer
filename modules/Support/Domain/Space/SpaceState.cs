using System;

namespace BoscaliSummer.Modules.Support.Domain.Space
{
    internal readonly struct SpaceTaskReservation
    {
        public readonly BirdTask Task;
        public readonly int Token, Generation;
        internal readonly SpaceState Owner;

        internal SpaceTaskReservation(SpaceState owner, BirdTask task, int token, int generation)
        {
            Owner = owner;
            Task = task;
            Token = token;
            Generation = generation;
        }
    }

    /// <summary>One faction's actual uplinks and three persistent birds; all times are mission time.</summary>
    internal sealed class SpaceState
    {
        private struct BirdSlot
        {
            public int Token;
            public BirdTask Task;
            public float ReservedAt, BusyUntil;
        }

        private readonly float[] health;
        private readonly bool[] down, present;
        private readonly bool[] launched = new bool[SpaceRules.BirdCount];
        private readonly BirdSlot[] birds = new BirdSlot[SpaceRules.BirdCount];
        private readonly float[] cooldownUntil = new float[SpaceRules.TaskCount];
        private readonly bool[] dead = { true, true, true };
        private readonly GeoBird[] geo = { new GeoBird(0.40f, 0.50f, 100f), new GeoBird(0.50f, 0.50f, 100f), new GeoBird(0.60f, 0.50f, 100f) };
        private readonly float[] homeU = { 0.40f, 0.50f, 0.60f }, homeV = { 0.50f, 0.50f, 0.50f };
        private float allDownAt = float.PositiveInfinity;
        private int nextToken;
        private bool retired;

        /// <summary>Every pad slot starts absent and every bird not launched: nothing is on station until built.</summary>
        public SpaceState(int padSlots = 2)
        {
            if (padSlots < 0 || padSlots > 2) throw new ArgumentOutOfRangeException(nameof(padSlots));
            health = new float[padSlots];
            down = new bool[padSlots];
            present = new bool[padSlots];
        }

        public int UplinkCount
        {
            get { int n = 0; for (int i = 0; i < present.Length; i++) if (present[i]) n++; return n; }
        }
        public bool PadPresent(int i) => i >= 0 && i < present.Length && present[i];
        /// <summary>A pad was built in this slot: present, full health, up.</summary>
        public void PlacePad(int i)
        {
            if (i < 0 || i >= present.Length) return;
            present[i] = true; health[i] = 1f; down[i] = false; allDownAt = float.PositiveInfinity;
        }
        /// <summary>The host's last reading of one uplink site: destroyed, absent (or out of range of the list).</summary>
        public bool UplinkDown(int index) => index < 0 || index >= down.Length || !present[index] || down[index];
        public int Generation { get; private set; } = 1;
        public int LiveUplinkCount
        {
            get
            {
                int count = 0;
                for (int i = 0; i < down.Length; i++) if (present[i] && !down[i]) count++;
                return count;
            }
        }
        /// <summary>CYBER BIRD JAM: the host sets the multiplier an enemy intrusion puts on this faction's task cooldowns (1 = none).</summary>
        public float JamFactor { get; set; } = 1f;

        public float CooldownFactor
        {
            get
            {
                float jam = float.IsFinite(JamFactor) && JamFactor >= 1f && JamFactor <= 4f ? JamFactor : 1f;
                for (int i = 0; i < health.Length; i++)
                    if (present[i] && health[i] <= SpaceRules.DamagedHealth) return SpaceRules.DamagedCooldownFactor * jam;
                return jam;
            }
        }

        /// <summary>The bird is up: it was launched and an ASAT strike has not killed it (a dead bird stays dead until a LAUNCH build relaunches it).</summary>
        public bool HasBird(BirdKind bird) => (byte)bird < SpaceRules.BirdCount && !dead[(byte)bird];

        public bool BirdDown(BirdKind bird) => (byte)bird < SpaceRules.BirdCount && dead[(byte)bird];

        /// <summary>Bit i set (OPTICAL, RADAR, KINETIC) has been launched at least once (a killed bird stays launched: LOST).</summary>
        public byte LaunchedMask
        {
            get { byte m = 0; for (int i = 0; i < launched.Length; i++) if (launched[i]) m |= (byte)(1 << i); return m; }
        }

        /// <summary>A satellite is put on station at its home point with a full tank. False when that bird is already alive.</summary>
        public bool Launch(BirdKind bird)
        {
            if ((byte)bird >= SpaceRules.BirdCount || !dead[(byte)bird]) return false;
            int i = (byte)bird;
            dead[i] = false; launched[i] = true; birds[i] = default; Reset(i);
            return true;
        }

        /// <summary>The bird's geostationary state (position, burn under way, fuel).</summary>
        public GeoBird Geo(BirdKind bird) => geo[(byte)bird < SpaceRules.BirdCount ? (byte)bird : 0];

        /// <summary>The default parked points (over the faction's side of the front): every bird is placed there with a full tank. A relaunched bird returns to its own point.</summary>
        public void SetHomes(float[] u, float[] v)
        {
            for (int i = 0; i < SpaceRules.BirdCount && i < u.Length && i < v.Length; i++) { homeU[i] = u[i]; homeV[i] = v[i]; geo[i] = new GeoBird(u[i], v[i], 100f); }
        }

        /// <summary>Starts a burn of one bird to map point (u,v). Fuel is spent up front; nothing changes when it is refused.</summary>
        public GeoSpace.Refusal Relocate(BirdKind bird, float now, float u, float v)
        {
            if ((byte)bird >= SpaceRules.BirdCount || !SpaceRules.MissionTime(now) || !float.IsFinite(u) || !float.IsFinite(v)) return GeoSpace.Refusal.Dead;
            int i = (int)bird;
            GeoSpace.Refusal why = GeoSpace.Check(geo[i], !dead[i], now, u, v);
            if (why == GeoSpace.Refusal.None) geo[i] = geo[i].Relocate(now, u, v);
            return why;
        }

        /// <summary>The bird is alive and its footprint covers the map point: the gate for a perk that needs a satellite over its target.</summary>
        public bool Covers(BirdKind bird, float now, float u, float v) => HasBird(bird) && geo[(int)bird].Covers(now, u, v, GeoSpace.Reach[(int)bird]);

        private void Reset(int i) => geo[i] = new GeoBird(homeU[i], homeV[i], 100f);

        /// <summary>Bit i set (OPTICAL, RADAR, KINETIC) is dead.</summary>
        public byte DownMask
        {
            get { byte m = 0; for (int i = 0; i < dead.Length; i++) if (dead[i]) m |= (byte)(1 << i); return m; }
        }

        /// <summary>An ASAT strike hit this bird. Its tasks stop (an in-flight reservation is voided) until a LAUNCH build relaunches it. False when it was already dead.</summary>
        public bool KillBird(BirdKind bird, float now)
        {
            if ((byte)bird >= SpaceRules.BirdCount || dead[(byte)bird] || !SpaceRules.MissionTime(now)) return false;
            int i = (byte)bird;
            dead[i] = true;
            birds[i] = default;
            return true;
        }

        public void SetUplink(int index, float nativeHealthFraction, bool isDown, float now)
        {
            if (index < 0 || index >= health.Length || !present[index] || !SpaceRules.MissionTime(now) ||
                !float.IsFinite(nativeHealthFraction) || nativeHealthFraction < 0f || nativeHealthFraction > 1f) return;
            bool wasAllDown = LiveUplinkCount == 0;
            health[index] = isDown ? 0f : nativeHealthFraction;
            down[index] = isDown;
            bool isAllDown = LiveUplinkCount == 0;
            if (!wasAllDown && isAllDown) allDownAt = now;
            else if (!isAllDown) allDownAt = float.PositiveInfinity;
        }

        public SpaceFamilyState Family(float now)
        {
            if (!SpaceRules.MissionTime(now)) return SpaceFamilyState.Dark;
            int live = LiveUplinkCount;
            if (live == UplinkCount) return SpaceFamilyState.Normal; // also true with no pad at all: no surcharge before the first build
            if (live > 0 || now - allDownAt < SpaceRules.DarkGraceSeconds) return SpaceFamilyState.Degraded;
            return SpaceFamilyState.Dark;
        }

        public bool CanStart(BirdTask task, float now)
        {
            if (retired || !SpaceRules.MissionTime(now) || LiveUplinkCount == 0 ||
                !SpaceRules.TryBird(task, out BirdKind bird) || dead[(int)bird]) return false;
            BirdSlot slot = birds[(int)bird];
            return slot.Token == 0 && now >= slot.BusyUntil && now >= cooldownUntil[(int)task];
        }

        /// <summary>
        /// Mission seconds until this task could start: its cooldown and the bird's own busy time, zero when it is ready now,
        /// positive infinity without a live uplink. The feed and OVERLORD read it; the host still decides through <see cref="CanStart"/>.
        /// </summary>
        public float ReadyIn(BirdTask task, float now)
        {
            if (retired || !SpaceRules.MissionTime(now) || LiveUplinkCount == 0 || !SpaceRules.TryBird(task, out BirdKind bird) || dead[(int)bird])
                return float.PositiveInfinity;
            BirdSlot slot = birds[(int)bird];
            float wait = Math.Max(cooldownUntil[(int)task] - now, slot.BusyUntil - now);
            if (slot.Token != 0) wait = Math.Max(wait, 1f); // reserved by a task that is mid-launch
            return Math.Max(0f, wait);
        }

        public bool TryReserve(BirdTask task, float now, out SpaceTaskReservation reservation)
        {
            reservation = default;
            if (!CanStart(task, now) || nextToken == int.MaxValue) return false;
            SpaceRules.TryBird(task, out BirdKind bird);
            int token = ++nextToken;
            birds[(int)bird] = new BirdSlot { Token = token, Task = task, ReservedAt = now };
            reservation = new SpaceTaskReservation(this, task, token, Generation);
            return true;
        }

        public bool CanCommit(in SpaceTaskReservation reservation, float now, float taskSeconds)
        {
            if (!Owns(reservation, out int bird) || !SpaceRules.MissionTime(now) || Family(now) == SpaceFamilyState.Dark ||
                now < birds[bird].ReservedAt || !SpaceRules.MissionTime(taskSeconds)) return false;
            float busyUntil = now + taskSeconds;
            float coolingUntil = now + SpaceRules.Cooldown(reservation.Task) * CooldownFactor;
            return float.IsFinite(busyUntil) && float.IsFinite(coolingUntil);
        }

        public bool Commit(in SpaceTaskReservation reservation, float now, float taskSeconds)
        {
            if (!CanCommit(reservation, now, taskSeconds)) return false;
            SpaceRules.TryBird(reservation.Task, out BirdKind bird);
            birds[(int)bird] = new BirdSlot { BusyUntil = now + taskSeconds };
            cooldownUntil[(int)reservation.Task] = now + SpaceRules.Cooldown(reservation.Task) * CooldownFactor;
            return true;
        }

        public bool Cancel(in SpaceTaskReservation reservation)
        {
            if (!Owns(reservation, out int bird)) return false;
            birds[bird] = default;
            return true;
        }

        /// <summary>Invalidates delayed receipts without repairing anchors or restarting all-down grace.</summary>
        public void Clear()
        {
            Array.Clear(birds, 0, birds.Length);
            Array.Clear(cooldownUntil, 0, cooldownUntil.Length);
            if (Generation == int.MaxValue) retired = true;
            else Generation++;
        }

        private bool Owns(in SpaceTaskReservation reservation, out int bird)
        {
            bird = -1;
            if (retired || !ReferenceEquals(reservation.Owner, this) || reservation.Generation != Generation ||
                reservation.Token <= 0 || !SpaceRules.TryBird(reservation.Task, out BirdKind kind)) return false;
            bird = (int)kind;
            return birds[bird].Token == reservation.Token && birds[bird].Task == reservation.Task;
        }
    }
}
