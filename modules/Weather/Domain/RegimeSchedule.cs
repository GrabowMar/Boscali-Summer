using System;

namespace BoscaliSummer.Features.Weather.Domain
{
    /// <summary>
    /// A segment of the chain that brings a front with it. The front's geometry depends only on
    /// the segment (its id, seed and midpoint), never on the moment it is sampled, so it moves
    /// smoothly through blends; only <see cref="Strength"/> eases in and out.
    /// </summary>
    internal struct FrontSource
    {
        public uint Seed;
        public int Id;
        public WeatherRegime Regime;

        /// <summary>Mission time the front crosses the map centre.</summary>
        public float Mid;

        /// <summary>0..1: the segment's blend weight times the regime's frontal weight.</summary>
        public float Strength;
    }

    /// <summary>Where the regime chain is at one instant.</summary>
    internal struct RegimeState
    {
        public const int MaxFronts = 3;

        /// <summary>The regime being left (or held).</summary>
        public WeatherRegime From;

        /// <summary>The regime being entered; equals <see cref="From"/> while holding.</summary>
        public WeatherRegime To;

        /// <summary>Eased blend weight toward <see cref="To"/>, 0..1.</summary>
        public float Blend;

        /// <summary>The blended numbers everything downstream reads.</summary>
        public RegimeParams Params;

        /// <summary>Mission time the next change of regime starts, or +∞ when none will.</summary>
        public float NextChangeAt;

        /// <summary>The regime that change leads to (valid when <see cref="NextChangeAt"/> is finite).</summary>
        public WeatherRegime NextRegime;

        public FrontSource Front0;
        public FrontSource Front1;
        public FrontSource Front2;
        public int FrontCount;

        public WeatherRegime Dominant => Blend < 0.5f ? From : To;

        public FrontSource GetFront(int i) => i == 0 ? Front0 : i == 1 ? Front1 : Front2;

        public void AddFront(FrontSource source)
        {
            if (source.Strength <= 0.001f || FrontCount >= MaxFronts) return;
            if (FrontCount == 0) Front0 = source;
            else if (FrontCount == 1) Front1 = source;
            else Front2 = source;
            FrontCount++;
        }

        public void ScaleFronts(float factor)
        {
            Front0.Strength *= factor;
            Front1.Strength *= factor;
            Front2.Strength *= factor;
        }
    }

    /// <summary>
    /// The seeded regime chain. A segment holds one regime for 14–28 min, then blends into the
    /// next over 6–10 min with a smoothstep, so every parameter is continuous in time.
    ///
    /// <para>An override keyframe re-roots the chain: from its time the sky blends out of
    /// whatever it was into the forced regime over <see cref="OverrideBlendSeconds"/>, and a fresh
    /// chain seeded from the keyframe continues from there. Evaluation is a pure function of
    /// (key, time): segments are walked from the chain's root, bounded by
    /// <see cref="MaxSegments"/>.</para>
    ///
    /// <para>A held (non-dynamic) sky never changes regime; if that regime brings a front, a
    /// new one crosses every <see cref="HeldFrontPeriod"/> so a static FRONTAL sky still has
    /// weather moving through it.</para>
    /// </summary>
    internal static class RegimeSchedule
    {
        public const float MinHoldSeconds = 14f * 60f;
        public const float MaxHoldSeconds = 28f * 60f;
        public const float MinBlendSeconds = 6f * 60f;
        public const float MaxBlendSeconds = 10f * 60f;
        public const float OverrideBlendSeconds = 120f;
        public const float HeldFrontPeriod = 40f * 60f;

        /// <summary>Walk limit; 400 segments is well over a hundred hours of sky.</summary>
        public const int MaxSegments = 400;

        public static RegimeState Evaluate(WeatherKey key, float time)
        {
            int last = -1;
            for (int i = 0; i < key.OverrideCount; i++)
            {
                if (key.Override(i).Time <= time) last = i;
            }
            return EvaluateFrom(key, time, last);
        }

        /// <summary>Evaluates the chain rooted at override <paramref name="index"/> (-1 = the base chain).</summary>
        private static RegimeState EvaluateFrom(WeatherKey key, float time, int index)
        {
            if (index < 0)
            {
                return Chain(key.Seed, key.Epoch, key.OpeningRegime(), key.Dynamic, time, 0);
            }

            RegimeOverride keyframe = key.Override(index);
            uint seed = unchecked(key.Seed * 2654435761u ^ (uint)BitConverter.SingleToInt32Bits(keyframe.Time));
            RegimeState chain = Chain(seed, keyframe.Time, keyframe.Regime, key.Dynamic, time, 100000 * (index + 1));

            float since = time - keyframe.Time;
            if (since >= OverrideBlendSeconds) return chain;

            // Ease out of whatever the sky was doing when the keyframe landed; its fronts fade
            // with it rather than vanishing.
            RegimeState before = EvaluateFrom(key, time, index - 1);
            float w = WeatherMath.Smoothstep(0f, OverrideBlendSeconds, since);
            var blended = chain;
            blended.Params = RegimeParams.Blend(before.Params, chain.Params, w);
            if (w < 0.5f)
            {
                blended.From = before.Dominant;
                blended.To = keyframe.Regime;
                blended.Blend = w;
            }

            blended.FrontCount = 0;
            chain.ScaleFronts(w);
            before.ScaleFronts(1f - w);
            for (int i = 0; i < chain.FrontCount; i++) blended.AddFront(chain.GetFront(i));
            for (int i = 0; i < before.FrontCount; i++) blended.AddFront(before.GetFront(i));
            return blended;
        }

        private static RegimeState Chain(uint seed, float root, WeatherRegime first, bool dynamic, float time, int idBase)
        {
            if (!dynamic) return Held(seed, root, first, time, idBase);

            WeatherRegime current = first;
            float start = root;

            for (int k = 0; k < MaxSegments; k++)
            {
                float hold = WeatherMath.HashRange(seed, k, 11, 0, MinHoldSeconds, MaxHoldSeconds);
                float blend = WeatherMath.HashRange(seed, k, 12, 0, MinBlendSeconds, MaxBlendSeconds);
                WeatherRegime next = RegimeTable.Next(current, WeatherMath.Hash01(seed, k, 13));
                float blendStart = start + hold;
                float end = blendStart + blend;

                if (time < end || k == MaxSegments - 1)
                {
                    bool blending = time >= blendStart;
                    float w = blending ? WeatherMath.Smoothstep(blendStart, end, time) : 0f;

                    var state = new RegimeState
                    {
                        From = current,
                        To = blending ? next : current,
                        Blend = w,
                        Params = blending
                            ? RegimeParams.Blend(RegimeTable.Get(current), RegimeTable.Get(next), w)
                            : RegimeTable.Get(current),
                        NextChangeAt = blending ? float.PositiveInfinity : blendStart,
                        NextRegime = next,
                    };
                    if (blending)
                    {
                        // The next change after this blend is the next segment's own blend.
                        float nextHold = WeatherMath.HashRange(seed, k + 1, 11, 0, MinHoldSeconds, MaxHoldSeconds);
                        state.NextChangeAt = end + nextHold;
                        state.NextRegime = RegimeTable.Next(next, WeatherMath.Hash01(seed, k + 1, 13));
                    }

                    // The outgoing segment's front, and the incoming one's once blending starts.
                    state.AddFront(Source(seed, idBase + k, current, start + hold * 0.5f, 1f - w));
                    if (blending)
                    {
                        float nextHold = WeatherMath.HashRange(seed, k + 1, 11, 0, MinHoldSeconds, MaxHoldSeconds);
                        state.AddFront(Source(seed, idBase + k + 1, next, end + nextHold * 0.5f, w));
                    }
                    return state;
                }

                current = next;
                start = end;
            }

            return default;
        }

        /// <summary>
        /// A segment's front, weighted by the segment's share of the sky: 1 while it holds, the
        /// blend weight while it is being entered or left.
        /// </summary>
        private static FrontSource Source(uint seed, int id, WeatherRegime regime, float mid, float weight)
        {
            float strength = weight * RegimeTable.Get(regime).Frontal;
            return new FrontSource { Seed = seed, Id = id, Regime = regime, Mid = mid, Strength = strength };
        }

        private static RegimeState Held(uint seed, float root, WeatherRegime regime, float time, int idBase)
        {
            var state = new RegimeState
            {
                From = regime,
                To = regime,
                Blend = 0f,
                Params = RegimeTable.Get(regime),
                NextChangeAt = float.PositiveInfinity,
                NextRegime = regime,
            };
            if (RegimeTable.Get(regime).Frontal > 0f)
            {
                // One front per period, crossing the centre mid-period, fading in and out at the
                // period's ends so consecutive fronts never overlap abruptly.
                int n = (int)Math.Floor((time - root) / HeldFrontPeriod);
                float periodStart = root + n * HeldFrontPeriod;
                float phase = (time - periodStart) / HeldFrontPeriod;
                float envelope = WeatherMath.Envelope(phase, 0f, 0.2f, 0.8f, 1f);
                state.AddFront(new FrontSource
                {
                    Seed = seed,
                    Id = idBase + n,
                    Regime = regime,
                    Mid = periodStart + HeldFrontPeriod * 0.5f,
                    Strength = envelope * RegimeTable.Get(regime).Frontal,
                });
            }
            return state;
        }
    }
}
