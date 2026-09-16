using System;

namespace BoscaliSummer.Features.Weather.Domain
{
    /// <summary>
    /// How one driven value gets from the live reading to the model's target. Two jobs, both
    /// pure: ramp instead of snap, and stand down when somebody else — the authored mission's
    /// <c>ModifyEnvironment</c> beats, a debug control, another mod — has written the same value.
    /// A foreign write is adopted as the new baseline for a bounded hold, so a scripted beat
    /// always wins and the model resumes around it.
    /// </summary>
    internal sealed class WeatherDrive
    {
        /// <summary>An observed move this far past our own last write is treated as foreign.</summary>
        public const float AdoptEpsilon = 0.02f;

        /// <summary>Seconds we stay off a value after adopting somebody else's write.</summary>
        public const float HoldSeconds = 40f;

        private float lastWritten = float.NaN;
        private float holdRemaining;

        public bool Holding => holdRemaining > 0f;

        public float HoldRemaining => holdRemaining;

        public float LastWritten => lastWritten;

        public bool ShouldWrite => holdRemaining <= 0f;

        /// <summary>Run down the hold. Call once per tick, before <see cref="ShouldWrite"/>.</summary>
        public void Advance(float elapsed)
        {
            if (elapsed > 0f && holdRemaining > 0f)
                holdRemaining = Math.Max(0f, holdRemaining - elapsed);
        }

        /// <summary>
        /// Watch the live value. Returns true when it moved past our own last write, which is
        /// somebody else's edit: adopt it and hold off for <see cref="HoldSeconds"/>.
        /// </summary>
        public bool Observe(float live)
        {
            if (float.IsNaN(live)) return false;
            if (float.IsNaN(lastWritten))
            {
                lastWritten = live;
                return false;
            }
            if (Math.Abs(live - lastWritten) <= AdoptEpsilon) return false;
            lastWritten = live;
            holdRemaining = HoldSeconds;
            return true;
        }

        /// <summary>Record a value this feature just pushed into the world.</summary>
        public void Acknowledge(float value)
        {
            if (!float.IsNaN(value)) lastWritten = value;
        }

        /// <summary>Bounded ramp toward a target; <paramref name="rate"/> is units per second.</summary>
        public float Step(float live, float target, float elapsed, float rate)
        {
            float value = float.IsNaN(live) ? target : live;
            if (float.IsNaN(target) || rate <= 0f || elapsed <= 0f) return value;
            float delta = target - value;
            float limit = rate * elapsed;
            if (delta > limit) return value + limit;
            if (delta < -limit) return value - limit;
            return target;
        }

        /// <summary>
        /// The same ramp for a heading: it takes the short arc, so 350 to 10 crosses north
        /// instead of sweeping the long way round and dragging the wind zone with it.
        /// </summary>
        public float StepAngle(float live, float target, float elapsed, float rate)
        {
            float value = WeatherState.WrapHeading(float.IsNaN(live) ? target : live);
            if (float.IsNaN(target) || rate <= 0f || elapsed <= 0f) return value;
            float delta = WeatherState.WrapHeading(WeatherState.WrapHeading(target) - value + 180f) - 180f;
            float limit = rate * elapsed;
            if (delta > limit) delta = limit;
            else if (delta < -limit) delta = -limit;
            return WeatherState.WrapHeading(value + delta);
        }

        /// <summary>Hand the value back to the model now, e.g. when a debug override ends.</summary>
        public void Release() => holdRemaining = 0f;

        public void Reset()
        {
            holdRemaining = 0f;
            lastWritten = float.NaN;
        }
    }
}
