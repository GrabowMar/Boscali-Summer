using System;
using BoscaliSummer.Features.Squad.Domain;

namespace BoscaliSummer.Tests.Features.Squad
{
    internal static class AceCareerTests
    {
        public static void Run()
        {
            InvalidDamageCannotCreateThreat();
            GraceHuntAndCooldownDoNotBankDamage();
            DefeatPaysExactlyOnce();
            DifficultyAndBonusesStayBounded();
            PilotReplacementRequiresOneLifeDeath();
            RapidDeathsAndDelayedObservationsAreIdempotent();
            DebugTierOverridesCareerDifficulty();
        }

        private static void DebugTierOverridesCareerDifficulty()
        {
            for (int tier = 1; tier <= 5; tier++)
            {
                int selected = AceCareer.SpawnTier(5, 5, tier);
                TestAssert.That(selected == tier && AceCareer.WingSize(selected) == (tier < 3 ? 2 : tier < 5 ? 3 : 4),
                    "debug tier must override veteran/returning-ace difficulty and select its normal wing size");
            }
            TestAssert.That(AceCareer.SpawnTier(1, 0) == 1 && AceCareer.SpawnTier(2, 3) == 4 &&
                AceCareer.SpawnTier(5, 5) == 5, "normal encounter escalation must retain its tier ceiling");
            foreach (int invalid in new[] { -1, 6 })
            {
                bool rejected = false;
                try { AceCareer.SpawnTier(1, 0, invalid); }
                catch (ArgumentOutOfRangeException) { rejected = true; }
                TestAssert.That(rejected, "invalid debug tiers must not reach the spawn API");
            }
        }

        private static void InvalidDamageCannotCreateThreat()
        {
            var career = new AceCareer();
            float[] invalid = { 0f, -1f, float.NaN, float.PositiveInfinity, float.NegativeInfinity };
            foreach (float amount in invalid)
                TestAssert.That(!career.Damage(amount, 60f, 25f) && career.Threat == 0f,
                    "invalid damage must not create or corrupt threat");
            foreach (float time in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity })
                TestAssert.That(!career.Damage(25f, time, 25f) && career.Threat == 0f,
                    "invalid mission time must not bypass the grace period");
            foreach (float threshold in invalid)
                TestAssert.That(!career.Damage(25f, 60f, threshold) && career.Threat == 0f,
                    "invalid configured damage threshold must not corrupt career state");
            TestAssert.That(!career.Damage(10f, 60f, 25f) && career.Threat == 10f &&
                career.Damage(15f, 60f, 25f) && career.Threat == 25f,
                "separate credited damage events must accumulate to the trigger");
            TestAssert.That(career.Damage(float.MaxValue, 60f, 25f) && career.Threat == 25f,
                "an oversized hit must stay bounded at the current trigger");
        }

        private static void GraceHuntAndCooldownDoNotBankDamage()
        {
            var career = new AceCareer();
            TestAssert.That(!career.Damage(1000f, 59.9f, 25f) && career.Threat == 0f,
                "damage during mission grace must not queue an immediate ace");
            TestAssert.That(career.Damage(25f, 60f, 25f), "grace should end at exactly 60 seconds");
            career.Begin();
            TestAssert.That(career.Hunting && career.Threat == 0f &&
                !career.Damage(1000f, 80f, 25f) && career.Threat == 0f,
                "active hunts must consume existing threat and ignore new damage");
            career.Finish(100f, 180f, false);
            TestAssert.That(!career.Hunting && career.ReadyAt == 280f &&
                !career.Damage(1000f, 279.9f, 25f) && career.Threat == 0f,
                "cooldown damage must not bank a replacement hunt");
            TestAssert.That(!career.Damage(1f, 280f, 25f) && career.Threat == 1f &&
                career.Damage(24f, 281f, 25f),
                "the next hunt must require fresh threshold damage after cooldown");
        }

        private static void DefeatPaysExactlyOnce()
        {
            var career = new AceCareer();
            career.Finish(100f, 180f, true);
            TestAssert.That(career.BonusPoints == 0 && career.Defeats == 0 && career.ReadyAt == 60f,
                "a reported defeat without an active hunt cannot pay");
            career.Begin();
            career.Finish(100f, 180f, true);
            career.Finish(200f, 180f, true);
            career.Finish(300f, 180f, false);
            TestAssert.That(career.BonusPoints == 1 && career.Defeats == 1 && career.ReadyAt == 280f,
                "duplicate defeat/expiry callbacks must not pay twice or restart cooldown");
            career.Begin();
            career.Finish(300f, 180f, false);
            career.Finish(301f, 180f, true);
            TestAssert.That(career.BonusPoints == 1 && career.Defeats == 1,
                "an uncredited resolution cannot later be upgraded into a rewarded defeat");
        }

        private static void DifficultyAndBonusesStayBounded()
        {
            var career = new AceCareer();
            float previousThreshold = 0f;
            for (int defeats = 0; defeats <= 150; defeats++)
            {
                TestAssert.That(career.Tier == Math.Min(5, 1 + defeats),
                    "ace tier must advance after victories and stop at five");
                TestAssert.That(career.BonusPoints == Math.Min(AceCareer.MaximumBonus, defeats),
                    "ace bonus ledger must remain bounded during a long mission");
                float threshold = career.Threshold(25f);
                TestAssert.That(AceCareer.Finite(threshold) && threshold >= previousThreshold && threshold <= 125f,
                    "required threat must increase monotonically and stop at five times the initial value");
                TestAssert.That(AceCareer.WingSize(career.Tier) == (defeats >= 4 ? 4 : defeats >= 2 ? 3 : 2),
                    "escorts must increase at tiers three/five and never exceed four aircraft");
                previousThreshold = threshold;
                career.Begin();
                career.Finish(60f + defeats * 200f, 180f, true);
            }
            TestAssert.That(career.Defeats == 100, "career history counter must have a hard ceiling");
            TestAssert.That(AceCareer.WingSize(int.MaxValue) == 4, "extreme tier cannot exceed the wing ceiling");
        }

        private static void PilotReplacementRequiresOneLifeDeath()
        {
            var career = new AceCareer();
            career.Begin(); career.Finish(100f, 180f, true);
            TestAssert.That(!career.Replace(500) && career.Generation == 1 && career.BonusPoints == 1,
                "aircraft changes or ejection without a death record must preserve the pilot");
            career.PilotDied(false);
            TestAssert.That(career.Deaths == 1 && !career.ReplacementPending && !career.Replace(500) &&
                career.Generation == 1 && career.BonusPoints == 1,
                "default respawn mode must preserve identity and earned ace points after pilot death");
            career.PilotDied(true);
            TestAssert.That(career.ReplacementPending && !career.Damage(1000f, 500f, 25f),
                "a retired one-life pilot cannot accumulate a new hunt before the successor arrives");
            TestAssert.That(career.Replace(750) && career.Generation == 2 && career.ScoreOrigin == 750 &&
                career.Defeats == 0 && career.BonusPoints == 0 && career.Threat == 0f && !career.ReplacementPending,
                "a one-life successor must receive a fresh career with the current vanilla score as origin");
            TestAssert.That(!career.Replace(900) && career.Generation == 2 && career.ScoreOrigin == 750,
                "repeated spawn observations must not create multiple successors");
            career.PilotDied(true); career.Replace(-100);
            TestAssert.That(career.ScoreOrigin == 0, "a negative vanilla score cannot grant successor progress");
        }

        private static void RapidDeathsAndDelayedObservationsAreIdempotent()
        {
            var career = new AceCareer();
            TestAssert.That(career.RecordDeath(101, true) && career.RecordDeath(102, true),
                "two native pilot deaths before the slow tick must both be counted");
            TestAssert.That(!career.RecordDeath(101, true) && !career.RecordDeath(102, true) && career.Deaths == 2,
                "delayed seat observations must not recount either rapid death");
            TestAssert.That(career.Replace(70000) && !career.Replace(70000) && career.Generation == 2,
                "multiple deaths before the next living seat must create only one successor");
            career.Begin();
            career.CreditVictory();
            TestAssert.That(career.Hunting && career.BonusPoints == 1,
                "defeating an older released ace must not end a newer active hunt");
        }
    }
}
