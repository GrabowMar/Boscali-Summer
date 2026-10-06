using System;
using System.Numerics;

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
    /// <summary>Braking-distance limits, pursuit throttle, and speed-brake hysteresis.</summary>
    internal static class FormationClosure
    {
        internal readonly struct Controls
        {
            public readonly float Throttle;
            public readonly bool Airbrake;
            public Controls(float throttle, bool airbrake) { Throttle = throttle; Airbrake = airbrake; }
        }

        public static float SafeClosure(float distance, float braking, float responseSeconds)
        {
            double decel = Math.Max(0.1f, braking);
            double lag = decel * Math.Max(0f, responseSeconds);
            return (float)(Math.Sqrt(lag * lag + 2d * decel * Math.Max(0f, distance)) - lag);
        }

        public static float LoadedMinimum(float minimumAirspeed, float bankDegrees) =>
            minimumAirspeed / (float)Math.Sqrt(Math.Max(0.25d, Math.Cos(bankDegrees * Math.PI / 180d)));

        public static Controls Resolve(float throttle, float speedError, float airspeed, float minimumAirspeed,
            float distance, float closing, float spacing, float braking, float responseSeconds,
            float alignment, float bankDegrees, float radarAltitude, float verticalSpeed,
            bool terrainWarning, bool allowPursuit, bool allowBraking, bool wasBraking)
        {
            float reserve = Math.Max(30f, spacing * 0.5f);
            float safeClosure = SafeClosure(distance - reserve, braking, responseSeconds);
            bool overspeed = closing > safeClosure + (wasBraking ? -3f : 3f);
            // Release brakes before closure reaches zero to allow spool-up; separate thresholds prevent
            // chatter.
            // Terminal energy-shedding and airbraking applies only within the arrival envelope.
            // Long-range intercepts (distance >= 1500m) maintain pursuit cruise power.
            float loadedMinimum = LoadedMinimum(minimumAirspeed, bankDegrees);
            float maxArrivalBrakeDistance = Math.Max(1500f, spacing * 6f);
            bool canShedEnergy = allowBraking && distance < maxArrivalBrakeDistance && !terrainWarning && radarAltitude > 250f &&
                Math.Abs(bankDegrees) < 50f && verticalSpeed < 5f &&
                airspeed > loadedMinimum + (wasBraking ? 8f : 15f);
            bool brake = canShedEnergy &&
                closing > (wasBraking ? 6f : 10f) && speedError < (wasBraking ? -3f : -8f) && overspeed;

            // Prioritise capture until the braking envelope, allowing for slight overshoot from engine
            // lag.
            if (allowPursuit && distance > WingTuning.CaptureDistance && alignment > 0.65f &&
                closing < safeClosure && !terrainWarning)
                throttle = 1f;
            if (canShedEnergy && overspeed && closing > 6f && speedError < -3f)
                throttle = Math.Min(throttle, 0.1f);
            if (brake) throttle = 0f;
            // Minimum-energy protection overrides arrival and braking demands.
            if (airspeed < loadedMinimum) return new Controls(1f, false);
            // Exact zero throttle deploys native airbrakes; positive idle retracts them.
            // ControlInputs.brake operates wheel brakes.
            return new Controls(brake ? 0f : Math.Max(0.01f, Math.Min(1f, throttle)), brake);
        }
    }
}
