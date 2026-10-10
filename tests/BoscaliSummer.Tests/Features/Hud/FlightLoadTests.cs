using BoscaliSummer.Core.Math;

namespace BoscaliSummer.Tests.Features.Hud
{
    /// <summary>The native G-meter load factor and the per-airframe over-G caution point.</summary>
    internal static class FlightLoadTests
    {
        public static void Run()
        {
            TestAssert.Near(FlightLoad.Factor(0f, 0f, 0f, 0f, 1f, 0f), 1f, "Level unaccelerated flight reads 1 g");
            TestAssert.Near(FlightLoad.Factor(0f, 3f, 0f, 0f, 1f, 0f), 4f, "A 4 g pull-up: 3 g of seat acceleration plus gravity");
            TestAssert.Near(FlightLoad.Factor(0f, 0f, 0f, 0f, -1f, 0f), -1f, "Inverted and unaccelerated reads -1 g");
            TestAssert.Near(FlightLoad.Factor(1.7320508f, 0f, 0f, 0.8660254f, 0.5f, 0f), 2f, "A coordinated 60 degree level turn reads 2 g");
            TestAssert.Near(FlightLoad.Factor(0f, -1f, 0f, 0f, 1f, 0f), 0f, "Free fall reads 0 g");

            TestAssert.Near(FlightLoad.CautionAt(9f), 7.2f, "Over-G cautions at 80 % of a 9 g limit");
            TestAssert.Near(FlightLoad.CautionAt(5f), 4f, "A 5 g airframe cautions at 4 g");
            TestAssert.Near(FlightLoad.CautionAt(0f), 7.2f, "An unknown limit falls back to the 9 g default");
        }
    }
}
