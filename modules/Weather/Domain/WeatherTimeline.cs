using System;

namespace BoscaliSummer.Modules.Weather.Domain
{
    /// <summary>Where the weather timeline is at one instant.</summary>
    internal struct TimelineState
    {
        /// <summary>The state being left (equals <see cref="To"/> once the fade is over).</summary>
        public WeatherRegimeType From;

        /// <summary>The state of the current step.</summary>
        public WeatherRegimeType To;

        /// <summary>Eased fade weight toward <see cref="To"/>, 0..1.</summary>
        public float Blend;

        /// <summary>Fractional ladder position: Clear = 0 … Storm = 6.</summary>
        public float Level;

        /// <summary>The ladder position convection has reached. Cells grow slower than the
        /// sheet fades: towers first, then rain, then lightning, over <see cref="WeatherTimeline.SettleSeconds"/>.</summary>
        public float GrowthLevel;

        public int Step;
        public float StepStart;

        /// <summary>Mission time the next step begins, or +∞ for a held sky.</summary>
        public float NextChangeAt;

        /// <summary>The state the next step moves to (may equal <see cref="To"/>: a hold).</summary>
        public WeatherRegimeType Next;

        /// <summary>Seed of the static cloud layout in use.</summary>
        public uint Layout;

        public WeatherRegimeType Dominant => Blend < 0.5f ? From : To;
    }

    /// <summary>
    /// The weather timeline: one static state per interval (5 min by default), each step a hold
    /// or a move to a neighbouring state, faded over <see cref="WeatherKey.FadeSeconds"/>.
    ///
    /// <para>The cloud layout (front bands, cumulus groups, storm-cell sites) is fixed while the
    /// weather builds and decays through it, so states grow and shrink clouds in place. A new
    /// layout is rolled only once a step into CLEAR has fully settled, when the sky is empty and the
    /// swap cannot be seen.</para>
    ///
    /// <para>A pure function of (key, time): every peer computes the same sky.</para>
    /// </summary>
    internal static class WeatherTimeline
    {
        /// <summary>Walk limit: 4000 steps is over 300 hours at five minutes.</summary>
        public const int MaxSteps = 4000;

        public static TimelineState Evaluate(WeatherKey key, float time)
        {
            WeatherRegimeType start = key.OpeningState();
            if (!key.Dynamic)
            {
                return new TimelineState
                {
                    From = start, To = start, Blend = 1f, Level = (int)start, GrowthLevel = (int)start,
                    StepStart = key.Epoch, NextChangeAt = float.PositiveInfinity, Next = start,
                    Layout = LayoutSeed(key, 0),
                };
            }

            float interval = key.IntervalMinutes * 60f;
            float fade = Math.Min(key.FadeSeconds, interval * 0.5f);
            int n = time <= key.Epoch ? 0 : (int)Math.Min(MaxSteps, Math.Floor((time - key.Epoch) / interval));

            WeatherRegimeType previous = start, current = start;
            int clears = 0;
            for (int k = 1; k <= n; k++)
            {
                // A CLEAR step that settled before this step began swapped the layout.
                if (k > 1 && current == WeatherRegimeType.Clear) clears++;
                previous = current;
                current = StateTable.Next(current, WeatherMath.Hash01(key.Seed, k, 13));
            }

            float stepStart = key.Epoch + n * interval;
            float blend = n == 0 ? 1f : WeatherMath.Smoothstep(stepStart, stepStart + fade, time);
            float growth = n == 0 ? 1f : WeatherMath.Smoothstep(stepStart, stepStart + SettleSeconds(key), time);
            if (n == 0) previous = current;
            if (n > 0 && current == WeatherRegimeType.Clear && growth >= 1f) clears++;

            return new TimelineState
            {
                From = previous,
                To = current,
                Blend = blend,
                Level = WeatherMath.Lerp((int)previous, (int)current, blend),
                GrowthLevel = WeatherMath.Lerp((int)previous, (int)current, growth),
                Step = n,
                StepStart = stepStart,
                NextChangeAt = stepStart + interval,
                Next = StateTable.Next(current, WeatherMath.Hash01(key.Seed, n + 1, 13)),
                Layout = LayoutSeed(key, clears),
            };
        }

        /// <summary>
        /// How long a step takes to settle completely: the fade, or the slower convective growth
        /// (up to five minutes, never more than 90 % of the interval).
        /// </summary>
        public static float SettleSeconds(WeatherKey key)
        {
            float interval = key.IntervalMinutes * 60f;
            float fade = Math.Min(key.FadeSeconds, interval * 0.5f);
            return Math.Max(fade, Math.Min(300f, interval * 0.9f));
        }

        /// <summary>Mission time the step containing this state has fully settled.</summary>
        public static float SettledAt(WeatherKey key, TimelineState state)
            => key.Dynamic ? state.StepStart + SettleSeconds(key) : key.Epoch;

        private static uint LayoutSeed(WeatherKey key, int generation)
            => unchecked(key.Seed * 2654435761u ^ (uint)(generation * 40503 + 17) ^ (uint)(key.LayoutSalt * 0x9E3779B1u));
    }
}
