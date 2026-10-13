using UnityEngine;

namespace BoscaliSummer.Modules.Support.Runtime
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
        public readonly float ImpactTime; // mission seconds
        public readonly float ExpiryTime; // mission seconds
        /// <summary>Mission second the strike was first registered (kept when a cruise strike's route time is updated): the whole flight runs from here to <see cref="ImpactTime"/>.</summary>
        public readonly float StartTime;

        public ActiveStrikeInfo(
            int requestId, SupportActionId actionId, GlobalPosition target,
            float radius, float impactTime, float expiryTime, float startTime)
        {
            RequestId = requestId;
            ActionId = actionId;
            Target = target;
            Radius = radius;
            ImpactTime = impactTime;
            ExpiryTime = expiryTime;
            StartTime = startTime;
        }

        public bool IsActive(float now) => now < ExpiryTime;
    }
}
