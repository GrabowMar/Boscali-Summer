using BoscaliSummer.Framework.Contracts;

namespace BoscaliSummer.Features.QoL.Runtime
{
    internal sealed class ObservationStore
    {
        public const float LifetimeSeconds = 120f;
        private ObservationPoint? current;

        public bool Set(ObservationPoint point)
        {
            Clear();
            if (!Finite(point.X) || !Finite(point.Y) || !Finite(point.Z) ||
                !Finite(point.RecordedAt) || point.RecordedAt < 0f ||
                !Finite(point.Range) || point.Range <= 0f || point.Range > 60000f ||
                string.IsNullOrEmpty(point.Source)) return false;
            current = point;
            return true;
        }

        public bool TryGet(float now, out ObservationPoint point)
        {
            point = default;
            if (!current.HasValue) return false;
            float age = now - current.Value.RecordedAt;
            if (!Finite(age) || age < 0f || age >= LifetimeSeconds)
            {
                Clear();
                return false;
            }
            point = current.Value;
            return true;
        }

        public void Clear() => current = null;
        internal static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
