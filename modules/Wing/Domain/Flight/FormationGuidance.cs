using System;
using System.Numerics;

namespace BoscaliSummer.Modules.Wing.Domain
{
    /// <summary>Horizontal formation command before terrain and collision safety overrides.</summary>
    internal static class FormationGuidance
    {

        internal readonly struct HorizontalCommand
        {
            public readonly Vector2 Aim, Correction;
            public readonly float MaxCorrection;
            public HorizontalCommand(Vector2 aim, Vector2 correction, float maxCorrection)
            { Aim = aim; Correction = correction; MaxCorrection = maxCorrection; }
        }

        public static HorizontalCommand Horizontal(Vector2 toSlot, Vector2 ownVelocity,
            Vector2 slotVelocity, Vector2 forward, Vector2 rendezvous, Vector2 arrivalVelocity,
            float distance, float lookAhead, float speed,
            float acquisition, float aggression, float damping)
        {
            float limit = lookAhead * (float)Math.Tan(WingTuning.CommandAngle * Math.PI / 180d);
            Vector2 cross = toSlot - forward * Vector2.Dot(toSlot, forward);
            Vector2 drift = ownVelocity - slotVelocity;
            drift -= forward * Vector2.Dot(drift, forward);
            // Filtered slot motion handles noise; proportional correction responds to small moves
            // without a dead zone.
            Vector2 correction = cross * (1.35f * aggression) - drift * (5f * damping);
            if (correction.LengthSquared() > limit * limit)
                correction = Vector2.Normalize(correction) * limit;
            float travelTime = Math.Max(0.1f, distance / Math.Max(speed, 50f));
            var capture = FormationTracking.Capture(rendezvous.X, rendezvous.Y,
                ownVelocity.X, ownVelocity.Y, arrivalVelocity.X, arrivalVelocity.Y,
                travelTime, lookAhead / Math.Max(ownVelocity.Length(), 50f));
            Vector2 pursuit = new Vector2(capture.x, capture.z);
            // Far from formation, fly directly toward the future meeting point. A Hermite
            // departure tangent favours our existing heading and delays crossing intercepts.
            // Fade back to the curved, velocity-matched arrival before entering formation.
            if (rendezvous.LengthSquared() > 1f)
                pursuit = Vector2.Lerp(pursuit, Vector2.Normalize(rendezvous) * lookAhead,
                    FormationIntercept.LongRangeBlend(distance));
            return new HorizontalCommand(Vector2.Lerp(forward * lookAhead + correction,
                pursuit, acquisition), correction, limit);
        }
        private static float Clamp(float value, float low, float high) => Math.Max(low, Math.Min(high, value));
    }
}
