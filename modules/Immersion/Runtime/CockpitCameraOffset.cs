using UnityEngine;

namespace BoscaliSummer.Modules.Immersion.Runtime
{
    // One owned pivot write. Native/foreign replacements are authoritative on removal.
    internal sealed class CockpitCameraOffset
    {
        private Transform pivot;
        private Quaternion baseRotation;
        private Quaternion writtenRotation;
        public bool IsApplied { get; private set; }

        internal void Apply(Transform target, Quaternion offset)
        {
            Remove();
            if (target == null) return;
            pivot = target;
            baseRotation = pivot.localRotation;
            writtenRotation = baseRotation * offset;
            pivot.localRotation = writtenRotation;
            IsApplied = true;
        }

        internal void Remove()
        {
            if (IsApplied && pivot != null && SameWrite(pivot.localRotation, writtenRotation))
                pivot.localRotation = baseRotation;
            IsApplied = false;
            pivot = null;
        }

        private static bool SameWrite(Quaternion current, Quaternion written)
        {
            // Transform normalizes its quaternion. Components avoid Angle's poor precision
            // near zero and still detect a subsequent native/foreign absolute rotation.
            float sign = Quaternion.Dot(current, written) < 0f ? -1f : 1f;
            return Mathf.Abs(current.x - written.x * sign) < 0.00001f &&
                Mathf.Abs(current.y - written.y * sign) < 0.00001f &&
                Mathf.Abs(current.z - written.z * sign) < 0.00001f &&
                Mathf.Abs(current.w - written.w * sign) < 0.00001f;
        }
    }
}
