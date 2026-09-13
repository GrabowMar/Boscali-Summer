using BoscaliSummer.Features.Command.Domain;
using BoscaliSummer.Features.Command.Presentation.MapUi;

namespace BoscaliSummer.Tests.Features.Command
{
    internal static class FactionResourceTests
    {
        internal static void Run()
        {
            var morale = new FactionMoraleState();
            TestAssert.That(morale.TryGet(1, out float value) && value == 100f, "New faction morale starts at 100");
            TestAssert.That(morale.TrySet(1, 42.5f) && morale.TryGet(1, out value) && value == 42.5f,
                "Morale survives reads without rounding");
            foreach (float invalid in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity, -1f, 101f })
                TestAssert.That(!morale.TrySet(1, invalid) && morale.TryGet(1, out value) && value == 42.5f,
                    "Invalid writes preserve stored morale");
            for (int i = 2; i <= FactionMoraleState.MaximumFactions; i++)
                TestAssert.That(morale.TryGet(i, out value) && value == 100f, "Faction state is independent");
            TestAssert.That(!morale.TrySet(9, 50f), "Faction storage is bounded");
            TestAssert.That(morale.TrySet(1, 0f) && morale.TrySet(1, 100f), "Both morale endpoints are valid");
            morale.Reset();
            morale.Reset();
            TestAssert.That(morale.TryGet(9, out value) && value == 100f, "Scene reset releases capacity and restores defaults");

            var history = new MfdResourceHistory();
            TestAssert.That(history.Sample(0f, -10f, 0f, float.NaN, 100f), "First observation recorded");
            TestAssert.That(!history.Sample(1f, 999f, 0f, 0f, 0f) && history.Value(0, 0) == -10f,
                "Sampling is throttled without overwriting history");
            TestAssert.That(history.Due(5f) && !history.Due(4.9f),
                "Only a full interval makes the next observation due");
            TestAssert.That(!history.Due(float.NaN), "Invalid timestamps are never due");
            history.Sample(5f, 20f, 2f, float.NaN, 100f);
            history.Range(0, out float min, out float max);
            TestAssert.That(min == -10f && max == 20f, "Signed funds graph covers debt and credit");
            history.Range(3, out min, out max);
            TestAssert.That(min == 0f && max == 100f, "Morale graph uses its meaningful fixed scale");
            history.Range(2, out min, out max);
            TestAssert.That(MfdResourceHistory.Finite(min) && max > min, "Unavailable data never poisons chart geometry");
            for (int i = 2; i <= 65; i++) history.Sample(i * 5f, i, i, i, 100f);
            TestAssert.That(history.Count == 60 && history.Time(0) == 30f && history.Value(0, 0) == 6f &&
                history.Value(0, 59) == 65f, "History retains the newest sixty samples in order");
            history.Sample(400f, 1f, 1f, 1f, 1f);
            TestAssert.That(history.Count == 1 && history.Time(0) == 400f, "Observation gaps restart history without fictitious lines");
            history.Sample(0f, 2f, 2f, 2f, 2f);
            TestAssert.That(history.Count == 1 && history.Value(0, 0) == 2f, "Clock reset cannot join different missions");
            TestAssert.That(!history.Sample(float.NaN, 1f, 1f, 1f, 1f), "Invalid timestamps rejected");
            history.Clear();
            TestAssert.That(history.Count == 0, "Faction changes clear observation history");
        }
    }
}
