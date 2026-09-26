using BoscaliSummer.Runtime;
using UnityEngine;

namespace BoscaliSummer.Features.Support.Runtime
{
    /// <summary>
    /// Telemetry snapshot for an in-flight or actively detonating tactical support ability.
    /// Consumed by the tactical map overlay for drawing on-map icons, range rings, and countdowns.
    /// </summary>
    internal readonly struct ActiveStrikeInfo
    {
        public readonly int RequestId;
        public readonly SupportActionId ActionId;
        public readonly GlobalPosition Target;
        public readonly float Radius;
        public readonly float ImpactTime;
        public readonly float ExpiryTime;

        public ActiveStrikeInfo(
            int requestId, SupportActionId actionId, GlobalPosition target,
            float radius, float impactTime, float expiryTime)
        {
            RequestId = requestId;
            ActionId = actionId;
            Target = target;
            Radius = radius;
            ImpactTime = impactTime;
            ExpiryTime = expiryTime;
        }

        public bool IsActive(float now) => now < ExpiryTime;
        public float SecondsRemaining(float now) => Mathf.Max(0f, ImpactTime - now);
    }
}
