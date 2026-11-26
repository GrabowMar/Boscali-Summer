using UnityEngine;

namespace BoscaliSummer.Modules.Skylark
{
    /// <summary>A door, flap or slide moved about/along an aircraft-space axis, expressed in its parent's space. Optional art: a missing node means no hinge.</summary>
    internal sealed class SkylarkHinge
    {
        private readonly Transform node;
        private readonly Quaternion rest;
        private readonly Vector3 restPosition, axis, slide;
        private readonly float angle;

        /// <summary>0 = authored pose, 1 = swung by the angle / slid by the offset.</summary>
        internal float Amount { get; private set; }

        /// <param name="axis">Hinge axis in aircraft space (Unity: right-handed angles are flipped, so pass the Unity angle).</param>
        /// <param name="slide">Travel in aircraft space at Amount 1.</param>
        internal SkylarkHinge(Transform aircraft, Transform node, Vector3 axis, float angle, Vector3 slide = default)
        {
            this.node = node;
            this.angle = angle;
            rest = node.localRotation;
            restPosition = node.localPosition;
            this.axis = node.parent.InverseTransformDirection(aircraft.TransformDirection(axis));
            this.slide = node.parent.InverseTransformVector(aircraft.TransformVector(slide));
        }

        /// <summary>One step towards <paramref name="target"/> (0 or 1) over <paramref name="seconds"/>; true while it moved.</summary>
        internal bool Step(float target, float seconds, float deltaTime)
        {
            if (Amount == target) return false;
            Set(Mathf.MoveTowards(Amount, target, deltaTime / seconds));
            return true;
        }

        /// <summary>Set pose directly (0 = rest, 1 = full travel).</summary>
        internal void Set(float target)
        {
            Amount = Mathf.Clamp01(target);
            node.localRotation = Quaternion.AngleAxis(angle * Amount, axis) * rest;
            node.localPosition = restPosition + slide * Amount;
        }
    }
}
