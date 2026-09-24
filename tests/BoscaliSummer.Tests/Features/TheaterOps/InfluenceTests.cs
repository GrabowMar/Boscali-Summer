using BoscaliSummer.Features.TheaterOps.Domain;

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
        }

        private static void DefaultsLeanAttackWithAnOpenChest()
        {
            var state = new InfluenceState();
            TestAssert.That(state.Stance == InfluenceState.DefaultStance, "a new staff leans attack");
            TestAssert.That(!state.HoldOffense, "a new staff holds nothing");
            TestAssert.That(state.MaxEscrowPerPlan == InfluenceState.DefaultMaxEscrow, "a new chest is open");
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
            TestAssert.That(state.MaxEscrowPerPlan == InfluenceState.DefaultMaxEscrow, "a wild escrow falls back");
            TestAssert.That(state.ReserveFloor == 0f, "a negative reserve clamps");
            TestAssert.That(!state.SetAxis("", 1f, "VIPER-1"), "an empty key is refused");
            TestAssert.That(!state.SetAxis(null, 1f, "VIPER-1"), "a null key is refused");
        }
    }
}
