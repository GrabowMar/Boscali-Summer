using BoscaliSummer.Core.Contracts;

namespace BoscaliSummer.Modules.QoL.Runtime
{
    internal sealed class ObservationStore
    {
        public const float LifetimeSeconds = 120f;
        private ObservationPoint? current;

        public bool Set(ObservationPoint point)
        {
            Clear();
            if (!float.IsFinite(point.X) || !float.IsFinite(point.Y) || !float.IsFinite(point.Z) ||
                !float.IsFinite(point.RecordedAt) || point.RecordedAt < 0f ||
                !float.IsFinite(point.Range) || point.Range <= 0f || point.Range > 60000f ||
                string.IsNullOrEmpty(point.Source)) return false;
            current = point;
            return true;
        }

        public bool TryGet(float now, out ObservationPoint point)
        {
            point = default;
            if (!current.HasValue) return false;
            float age = now - current.Value.RecordedAt;
            if (!float.IsFinite(age) || age < 0f || age >= LifetimeSeconds)
            {
                Clear();
                return false;
            }
            point = current.Value;
            return true;
        }

        public void Clear() => current = null;
    }
}
