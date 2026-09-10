using BoscaliSummer.Features.QoL.Runtime;
using BoscaliSummer.Framework.Contracts;

namespace BoscaliSummer.Tests.Features.QoL
{
    internal static class ObservationTests
    {
        public static void Run()
        {
            var store = new ObservationStore();
            TestAssert.That(!store.TryGet(0f, out _), "empty mark must not target world zero");
            var mark = new ObservationPoint(-12345f, 250f, 67890f, 10f, 500f, "FORWARD");
            TestAssert.That(store.Set(mark) && store.TryGet(129f, out var point) &&
                point.X == mark.X && point.Z == mark.Z && point.RecordedAt == 10f,
                "global point and original observation time must remain unchanged");
            TestAssert.That(!store.TryGet(130f, out _) && !store.TryGet(11f, out _),
                "expired mark must not return after clock rollback");
            store.Set(mark);
            TestAssert.That(!store.TryGet(9f, out _), "scene clock rollback must clear a mark");
            store.Set(mark);
            TestAssert.That(!store.Set(new ObservationPoint(float.NaN, 0f, 0f, 10f, 1f, "FORWARD")) &&
                !store.TryGet(11f, out _), "failed capture must not leave an old strike point usable");
            TestAssert.That(!store.Set(new ObservationPoint(0f, 0f, 0f, 10f, float.PositiveInfinity, "FORWARD")),
                "invalid range must be rejected");
            store.Set(mark);
            store.Clear();
            TestAssert.That(!store.TryGet(11f, out _), "reset must remove observation state");
        }
    }
}
