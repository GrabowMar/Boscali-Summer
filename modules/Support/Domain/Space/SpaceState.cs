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
        private readonly bool[] down;
        private readonly BirdSlot[] birds = new BirdSlot[SpaceRules.BirdCount];
        private readonly float[] cooldownUntil = new float[SpaceRules.TaskCount];
        private float allDownAt = float.PositiveInfinity;
        private int nextToken;
        private bool retired;

        public SpaceState(int uplinkCount)
        {
            if (uplinkCount < 1 || uplinkCount > 2) throw new ArgumentOutOfRangeException(nameof(uplinkCount));
            health = new float[uplinkCount];
            down = new bool[uplinkCount];
            for (int i = 0; i < health.Length; i++) health[i] = 1f;
        }

        public int UplinkCount => health.Length;
        public int BirdCount => SpaceRules.BirdCount;
        public int Generation { get; private set; } = 1;
        public int LiveUplinkCount
        {
            get
            {
                int count = 0;
                for (int i = 0; i < down.Length; i++) if (!down[i]) count++;
                return count;
            }
        }
        public int ReservationCount
        {
            get
            {
                int count = 0;
                for (int i = 0; i < birds.Length; i++) if (birds[i].Token != 0) count++;
                return count;
            }
        }
        /// <summary>CYBER BIRD JAM: the host sets the multiplier an enemy intrusion puts on this faction's task cooldowns (1 = none).</summary>
        public float JamFactor { get; set; } = 1f;

        public float CooldownFactor
        {
            get
            {
                float jam = SpaceRules.Finite(JamFactor) && JamFactor >= 1f && JamFactor <= 4f ? JamFactor : 1f;
                for (int i = 0; i < health.Length; i++)
                    if (health[i] <= SpaceRules.DamagedHealth) return SpaceRules.DamagedCooldownFactor * jam;
                return jam;
            }
        }

        public bool HasBird(BirdKind bird) => (byte)bird < SpaceRules.BirdCount;
        public bool UplinkDown(int index) => index < 0 || index >= down.Length || down[index];
        public float UplinkHealthFraction(int index) => index < 0 || index >= health.Length ? 0f : health[index];

        public void SetUplink(int index, float nativeHealthFraction, bool isDown, float now)
        {
            if (index < 0 || index >= health.Length || !SpaceRules.MissionTime(now) ||
                !SpaceRules.Finite(nativeHealthFraction) || nativeHealthFraction < 0f || nativeHealthFraction > 1f) return;
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
            if (live == down.Length) return SpaceFamilyState.Normal;
            if (live > 0 || now - allDownAt < SpaceRules.DarkGraceSeconds) return SpaceFamilyState.Degraded;
            return SpaceFamilyState.Dark;
        }

        public bool CanStart(BirdTask task, float now)
        {
            if (retired || !SpaceRules.MissionTime(now) || LiveUplinkCount == 0 ||
                !SpaceRules.TryBird(task, out BirdKind bird)) return false;
            BirdSlot slot = birds[(int)bird];
            return slot.Token == 0 && now >= slot.BusyUntil && now >= cooldownUntil[(int)task];
        }

        /// <summary>
        /// Mission seconds until this task could start: its cooldown and the bird's own busy time, zero when it is ready now,
        /// positive infinity without a live uplink. The feed and OVERLORD read it; the host still decides through <see cref="CanStart"/>.
        /// </summary>
        public float ReadyIn(BirdTask task, float now)
        {
            if (retired || !SpaceRules.MissionTime(now) || LiveUplinkCount == 0 || !SpaceRules.TryBird(task, out BirdKind bird))
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
            return SpaceRules.Finite(busyUntil) && SpaceRules.Finite(coolingUntil);
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

        public bool TryStart(BirdTask task, float now, float taskSeconds)
        {
            if (!SpaceRules.MissionTime(taskSeconds) || !TryReserve(task, now, out var reservation)) return false;
            if (Commit(reservation, now, taskSeconds)) return true;
            Cancel(reservation);
            return false;
        }

        public float CooldownRemaining(BirdTask task, float now) =>
            !SpaceRules.MissionTime(now) || !SpaceRules.TryBird(task, out _)
                ? 0f : Math.Max(0f, cooldownUntil[(int)task] - now);

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
