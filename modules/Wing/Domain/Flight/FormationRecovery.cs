using System;

using BoscaliSummer.Modules.Wing.Domain;
using BoscaliSummer.Modules.Wing.Runtime;
using BoscaliSummer.Modules.Wing.Presentation;
using BoscaliSummer.Modules.Wing.Patches;
using BoscaliSummer.Modules.Wing.Networking;
using BoscaliSummer.Modules.Wing.Configuration;
using BoscaliSummer.Core.Math;
using BoscaliSummer.Core.Util;
using BoscaliSummer.Core.Storage;
namespace BoscaliSummer.Modules.Wing.Domain
{
    internal enum FormationRecoveryMode { Station, SlowLeader, Overshoot }

    /// <summary>Per-member recovery timers and airframe estimates.</summary>
    internal sealed class FormationRecovery
    {
        public FormationRecoveryMode Mode { get; private set; }
        public float Blend { get; private set; }
        public float Braking { get; private set; } = WingTuning.FormationInitialBraking;
        public float ResponseSeconds { get; private set; } = WingTuning.SpeedLeadSeconds;
        private float slowTime, readyTime, lastSpeed, lastThrottle, stableThrottle, responseTime;
        private bool sampled, awaitingResponse;
        private float burstRemaining, burstCooldown, reportElapsed;

        // Learn drag only from stable level flight at settled throttle; reset across manoeuvres and
        // reject collision-like discontinuities.
        public void Observe(float speed, float throttle, bool stableFlight, float dt)
        {
            if (!sampled || dt <= 0f || dt > 0.5f || !stableFlight)
            {
                sampled = true; lastSpeed = speed; lastThrottle = throttle;
                stableThrottle = 0f; awaitingResponse = false; return;
            }
            float accel = (speed - lastSpeed) / dt;
            float leverChange = throttle - lastThrottle;
            stableThrottle = Math.Abs(leverChange) < 0.02f ? stableThrottle + dt : 0f;
            if (leverChange > 0.25f) { awaitingResponse = true; responseTime = 0f; }
            if (leverChange < -0.1f) awaitingResponse = false;
            if (awaitingResponse)
            {
                responseTime += dt;
                if (accel > 0.5f && accel < WingTuning.MaxCredibleAccel)
                {
                    ResponseSeconds += (Clamp(responseTime, 0.25f, 2f) - ResponseSeconds) * 0.15f;
                    awaitingResponse = false;
                }
                if (responseTime > 2f) awaitingResponse = false;
            }
            if (throttle < 0.1f && stableThrottle > 2f && accel < -0.2f && accel > -8f)
            {
                float observed = Clamp(-accel * 0.8f, 0.5f, 6f);
                // Learn weaker braking faster because underestimating stopping distance is the
                // dangerous error.
                float tau = observed < Braking ? 1f : 8f;
                Braking += (observed - Braking) * (1f - (float)Math.Exp(-dt / tau));
            }
            lastSpeed = speed; lastThrottle = throttle;
        }

        public static float Move(float current, float target, float maximumChange) =>
            current + Clamp(target - current, -Math.Max(0f, maximumChange), Math.Max(0f, maximumChange));
        private static float Clamp(float value, float low, float high) => Math.Max(low, Math.Min(high, value));
    }
}
