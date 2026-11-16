using UnityEngine;

namespace BoscaliSummer.Modules.Immersion.Runtime
{
    // One owned pivot write. Native/foreign replacements are authoritative on removal.
    internal sealed class CockpitCameraOffset
    {
        private Transform pivot;
        private Quaternion baseRotation;
        private Quaternion writtenRotation;
        private Vector3 basePosition, writtenPosition;
        private bool positionApplied;
        internal const float PlayerBodyEyeForwardMetres = .03f;
        public bool IsApplied { get; private set; }

        internal void Apply(Transform target, Quaternion offset, Vector3 worldPositionOffset = default)
        {
            Remove();
            if (target == null) return;
            pivot = target;
            baseRotation = pivot.localRotation;
            writtenRotation = baseRotation * offset;
            pivot.localRotation = writtenRotation;
            positionApplied = worldPositionOffset.sqrMagnitude > 0f;
            if (positionApplied)
            {
                basePosition = pivot.localPosition;
                Vector3 localOffset = pivot.parent != null ? pivot.parent.InverseTransformVector(worldPositionOffset) : worldPositionOffset;
                writtenPosition = basePosition + localOffset;
                pivot.localPosition = writtenPosition;
            }
            IsApplied = true;
        }

        internal void Remove()
        {
            if (IsApplied && pivot != null && SameWrite(pivot.localRotation, writtenRotation))
                pivot.localRotation = baseRotation;
            if (IsApplied && positionApplied && pivot != null && (pivot.localPosition - writtenPosition).sqrMagnitude < 1e-12f)
                pivot.localPosition = basePosition;
            IsApplied = false;
            positionApplied = false;
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
