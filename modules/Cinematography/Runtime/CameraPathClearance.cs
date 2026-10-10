using UnityEngine;

namespace BoscaliSummer.Modules.Cinematography.Runtime
{
    internal sealed class CameraPathClearance
    {
        internal const float Radius = .25f;
        private readonly Collider[] overlaps = new Collider[16];
        private GlobalPosition previous;
        private bool hasPrevious;
        internal void Reset() { hasPrevious = false; }
        internal bool Clear(Vector3 position)
        {
            if (position.y < Datum.LocalSeaY + Radius) return false;
            int count = Physics.OverlapSphereNonAlloc(position, Radius, overlaps, PhysicsLayers.StaticsMask, QueryTriggerInteraction.Ignore);
            if (count > 0) return false; // overlap, including saturated buffer, is blocked
            if (hasPrevious)
            {
                Vector3 origin = previous.ToLocalPosition();
                Vector3 move = position - origin;
                float distance = move.magnitude;
                if (distance > .0001f && Physics.SphereCast(origin, Radius, move / distance, out _, distance,
                    PhysicsLayers.StaticsMask, QueryTriggerInteraction.Ignore)) return false;
            }
            previous = position.ToGlobalPosition(); hasPrevious = true;
            return true;
        }
    }
}
