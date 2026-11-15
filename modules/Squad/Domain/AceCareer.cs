using System;
using System.Collections.Generic;

namespace BoscaliSummer.Modules.Squad.Domain
{
    internal enum HuntOutcome { Hunting, Defeated, TargetLost, Expired }

    internal sealed class AceCareer
    {
        private readonly HashSet<int> recordedDeaths = new HashSet<int>();
        private readonly Queue<int> deathOrder = new Queue<int>(16);
        public const int MaximumPlayers = 64, MaximumWings = 4, MaximumHistory = 32, MaximumBonus = 20;
        /// <summary>No heat builds in a mission's first <see cref="GraceSeconds"/>; heat cools after <see cref="CoolDelay"/> quiet seconds (a full bar
        /// in <see cref="CoolSeconds"/>); one kill fills at most <see cref="MaxShare"/> of the bar, so a single lucky strike never brings an ace.</summary>
        public const float GraceSeconds = 180f, LifetimeSeconds = 900f, CoolDelay = 60f, CoolSeconds = 480f, MaxShare = 0.4f;
        public int Generation { get; private set; }
        public int Deaths { get; private set; }
        public int Defeats { get; private set; }
        public int BonusPoints { get; private set; }
        public int ScoreOrigin { get; private set; }
        /// <summary>The enemy's animosity toward this pilot; an ace wing is sent when it reaches the threshold.</summary>
        public float Heat { get; private set; }
        private float heatAt = float.NegativeInfinity, cooledAt;
        public float ReadyAt { get; private set; } = GraceSeconds;
        public bool Hunting { get; private set; }
        public bool ReplacementPending { get; private set; }
        public int Tier => Math.Min(5, 1 + Defeats);
        public AceCareer(int generation = 1, int scoreOrigin = 0)
        { Generation = Math.Max(1, generation); ScoreOrigin = Math.Max(0, scoreOrigin); }
        public float Threshold(float initial) => initial * Math.Min(5f, 1f + Defeats * 0.35f);

        public bool AddHeat(float amount, float now, float threshold)
        {
            if (!float.IsFinite(amount) || !float.IsFinite(now) || !float.IsFinite(threshold) || threshold <= 0f || amount <= 0f ||
                Hunting || ReplacementPending || now < ReadyAt) return false;
            Heat = Math.Min(threshold, Heat + Math.Min(amount, threshold * MaxShare));
            heatAt = now;
            return Heat >= threshold;
        }

        /// <summary>Bleeds heat off once the pilot has been quiet for <see cref="CoolDelay"/> seconds; any call cadence works.</summary>
        public void Cool(float now, float threshold)
        {
            if (!float.IsFinite(now)) return;
            float from = Math.Max(cooledAt, heatAt + CoolDelay);
            cooledAt = now;
            if (!float.IsFinite(threshold) || threshold <= 0f || Hunting || now <= from) return;
            Heat = Math.Max(0f, Heat - threshold * (now - from) / CoolSeconds);
        }

        public void Begin() { Hunting = true; Heat = 0f; }
        public void Finish(float now, float cooldown, bool credited)
        {
            if (!Hunting) return;
            Hunting = false; Heat = 0f; ReadyAt = now + cooldown;
            if (credited) CreditVictory();
        }

        public void CreditVictory()
        { Defeats = Math.Min(100, Defeats + 1); BonusPoints = Math.Min(MaximumBonus, BonusPoints + 1); }

        public void PilotDied(bool oneLife)
        { Deaths = Math.Min(10000, Deaths + 1); ReplacementPending |= oneLife; }

        public bool HasObservedDeath(int pilotId) => recordedDeaths.Contains(pilotId);
        public bool RecordDeath(int pilotId, bool oneLife)
        {
            if (!recordedDeaths.Add(pilotId)) return false;
            deathOrder.Enqueue(pilotId);
            if (deathOrder.Count > 16) recordedDeaths.Remove(deathOrder.Dequeue());
            PilotDied(oneLife); return true;
        }

        public bool Replace(int score)
        {
            if (!ReplacementPending) return false;
            Generation++; Defeats = BonusPoints = 0; ScoreOrigin = Math.Max(0, score);
            Heat = 0; ReplacementPending = false;
            return true;
        }

        public static int WingSize(int tier) => tier >= 5 ? 4 : tier >= 3 ? 3 : 2;
        public static int SpawnTier(int careerTier, int returningTier, int debugTier = 0)
        {
            if (debugTier < 0 || debugTier > 5) throw new ArgumentOutOfRangeException(nameof(debugTier));
            return debugTier != 0 ? debugTier : Math.Min(5, Math.Max(careerTier, returningTier + 1));
        }
    }
}
