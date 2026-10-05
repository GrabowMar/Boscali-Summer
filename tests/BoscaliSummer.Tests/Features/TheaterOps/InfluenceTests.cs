using BoscaliSummer.Modules.TheaterOps.Domain;

namespace BoscaliSummer.Tests.Features.TheaterOps
{
    internal static class InfluenceTests
    {
        public static void Run()
        {
            DefaultsLeanAttackWithAnOpenChest();
            EverySetterIsBoundedAndReported();
            AxesLeanFavorAndAvoidWithinCeiling();
            DegenerateInputCannotCorrupt();
            TheChestIsPricedFromTheOffensiveCosts();
            AVotedChestIsReclampedNotReplaced();
            TheReserveIsBoundedByWaves();
        }

        private static void DefaultsLeanAttackWithAnOpenChest()
        {
            var state = new InfluenceState();
            TestAssert.That(state.Stance == InfluenceState.DefaultStance, "a new staff leans attack");
            TestAssert.That(!state.HoldOffense, "a new staff holds nothing");
            TestAssert.That(state.MaxEscrowPerPlan == state.DefaultMaxEscrow, "a new chest is open");
            TestAssert.That(state.DefaultMaxEscrow == 25f + 2f * 45f,
                "the default chest opens a plan with two waves at the settings' default costs");
            TestAssert.That(state.ReserveFloor == 0f, "a new staff keeps no reserve");
            TestAssert.That(state.Setter == "" && state.Axes.Count == 0, "a new staff names no setter and no axes");
        }

        private static void EverySetterIsBoundedAndReported()
        {
            var state = new InfluenceState();
            TestAssert.That(state.SetStance(0.2f, "VIPER-1"), "a stance change lands");
            TestAssert.That(state.Setter == "VIPER-1", "the setter is whoever set it");
            TestAssert.That(!state.SetStance(0.2f, "GHOST"), "an unchanged stance reports no change");
            TestAssert.That(state.Setter == "VIPER-1", "a no-op never steals the attribution");
            TestAssert.That(state.SetStance(0.9f, "A callsign far longer than twenty-four characters"), "a long setter lands");
            TestAssert.That(state.Setter.Length == InfluenceState.MaximumSetterLength, "the setter is cut to the ceiling");
        }

        private static void AxesLeanFavorAndAvoidWithinCeiling()
        {
            var state = new InfluenceState();
            TestAssert.That(state.SetAxis("alpha", 1f, "VIPER-1"), "a favored axis lands");
            TestAssert.That(state.SetAxis("bravo", -0.5f, "VIPER-1"), "an avoided axis lands");
            TestAssert.That(state.WeightOf("alpha") == 1f && state.WeightOf("bravo") == -0.5f, "leans read back");
            TestAssert.That(state.WeightOf("charlie") == 0f, "an unweighted axis reads neutral");
            for (int i = 0; i < InfluenceState.MaximumAxes; i++) state.SetAxis("extra" + i, 1f, "VIPER-1");
            TestAssert.That(state.Axes.Count == InfluenceState.MaximumAxes, "axes stop at the ceiling");
            TestAssert.That(!state.SetAxis("overflow", 1f, "VIPER-1"), "a fifth axis is refused");
            TestAssert.That(state.SetAxis("alpha", 0f, "VIPER-1"), "zeroing an axis clears it");
            TestAssert.That(state.WeightOf("alpha") == 0f, "a cleared axis reads neutral");
        }

        private static void DegenerateInputCannotCorrupt()
        {
            var state = new InfluenceState();
            state.SetStance(float.NaN, "VIPER-1");
            TestAssert.That(state.Stance == InfluenceState.DefaultStance, "a NaN stance falls back");
            state.SetStance(9f, "VIPER-1");
            TestAssert.That(state.Stance == 1f, "a wild stance clamps");
            state.SetChest(float.PositiveInfinity, -5f, "VIPER-1");
            TestAssert.That(state.MaxEscrowPerPlan == state.DefaultMaxEscrow, "a wild escrow falls back");
            TestAssert.That(state.ReserveFloor == 0f, "a negative reserve clamps");
            TestAssert.That(!state.SetAxis("", 1f, "VIPER-1"), "an empty key is refused");
            TestAssert.That(!state.SetAxis(null, 1f, "VIPER-1"), "a null key is refused");
        }

        private static void TheChestIsPricedFromTheOffensiveCosts()
        {
            var state = new InfluenceState();
            TestAssert.That(state.EscrowCap == 25f + 6f * 45f, "the default cap funds all six waves");
            TestAssert.That(state.Price(10f, 20f), "new costs reprice an unset chest");
            TestAssert.That(state.DefaultMaxEscrow == 50f && state.MaxEscrowPerPlan == 50f,
                "an unset chest follows the overhead plus two waves");
            TestAssert.That(state.EscrowCap == 130f && state.ReserveCap == 200f,
                "the caps follow six waves of escrow and ten waves of reserve");
            TestAssert.That(!state.Price(10f, 20f), "the same costs change nothing");
            TestAssert.That(state.Price(float.NaN, -5f), "degenerate costs fall back without throwing");
            TestAssert.That(state.EscrowCap >= 0f && state.ReserveCap == 0f, "a negative wave prices nothing");
        }

        private static void AVotedChestIsReclampedNotReplaced()
        {
            var state = new InfluenceState();
            TestAssert.That(state.SetChest(200f, 0f, "VOTE"), "a voted chest lands");
            state.Price(25f, 45f);
            TestAssert.That(state.MaxEscrowPerPlan == 200f, "repricing keeps a voted chest");
            state.Price(10f, 5f);
            TestAssert.That(state.MaxEscrowPerPlan == 40f, "a cheaper war clamps a voted chest to its cap");
            TestAssert.That(!state.SetChest(9999f, 0f, "VOTE"), "a chest already at its cap reports no change");
        }

        private static void TheReserveIsBoundedByWaves()
        {
            var state = new InfluenceState();
            state.SetChest(state.MaxEscrowPerPlan, 100000f, "VIPER-1");
            TestAssert.That(state.ReserveFloor == state.ReserveCap && state.ReserveCap == 450f,
                "one player cannot freeze the pool: the reserve stops at ten waves");
        }
    }
}
