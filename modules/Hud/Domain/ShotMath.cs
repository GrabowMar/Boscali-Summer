namespace BoscaliSummer.Features.Hud.Domain
{
    /// <summary>
    /// One live shot as the corner board tracks it, already resolved to plain numbers. The bar's
    /// full scale is the range at first sight — the board samples within a quarter second of the
    /// shot appearing, so that reads as the launch range without the game ever stating one.
    /// </summary>
    internal struct ShotTrack
    {
        public float FirstRange;
        public float PrevRange;
        public float PrevTime;
        public float Range;

        /// <summary>Closing speed in m/s, positive while the range shrinks.</summary>
        public float Closure;

        /// <summary>Seconds to intercept at current closure; NaN while not closing.</summary>
        public float Eta;

        /// <summary>Remaining share of the first-seen range, 0 to 1.</summary>
        public float Fraction;
    }

    /// <summary>
    /// Shot countdown math, pure so it is tested on its own: closure from successive range
    /// samples, ETA from closure, and the bar fraction from the first-seen range. A shot that
    /// opens, hovers, or reports garbage reads as not closing rather than as a bogus countdown.
    /// </summary>
    internal static class ShotMath
    {
        /// <summary>Below this the range is noise, not an intercept.</summary>
        public const float MinClosure = 1f;

        public const float MaxEta = 999f;

        /// <summary>Past this the last sample is history, not a track.</summary>
        public const float StaleAfter = 5f;

        public static ShotTrack Start(float range, float time)
        {
            float sane = Finite(range) && range >= 0f ? range : 0f;
            return new ShotTrack
            {
                FirstRange = sane,
                PrevRange = sane,
                PrevTime = time,
                Range = sane,
                Closure = 0f,
                Eta = float.NaN,
                Fraction = FractionOf(sane, sane)
            };
        }

        public static ShotTrack Update(ShotTrack track, float range, float time)
        {
            float sane = Finite(range) && range >= 0f ? range : track.Range;
            float dt = Finite(time) && Finite(track.PrevTime) ? time - track.PrevTime : 0f;
            if (dt <= 0f || dt > StaleAfter)
            {
                track.PrevRange = sane;
                track.PrevTime = Finite(time) ? time : track.PrevTime;
                track.Range = sane;
                track.Closure = 0f;
                track.Eta = float.NaN;
                track.Fraction = FractionOf(sane, track.FirstRange);
                return track;
            }

            float closure = (track.PrevRange - sane) / dt;
            track.Closure = Finite(closure) ? closure : 0f;
            track.Eta = track.Closure >= MinClosure && sane > 0f
                ? System.Math.Min(sane / track.Closure, MaxEta)
                : float.NaN;
            track.PrevRange = sane;
            track.PrevTime = time;
            track.Range = sane;
            track.Fraction = FractionOf(sane, track.FirstRange);
            return track;
        }

        private static float FractionOf(float range, float first)
        {
            if (!Finite(range) || !Finite(first) || first <= 0f) return 0f;
            float share = range / first;
            return share < 0f ? 0f : share > 1f ? 1f : share;
        }

        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
