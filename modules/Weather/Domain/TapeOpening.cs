using System;
using BoscaliSummer.Core.Math;

namespace BoscaliSummer.Modules.Weather.Domain
{
    /// <summary>Where <see cref="TapeOpening.Solve"/> put the tape: the offset D, and for a front opening the minutes at which the first change (T1) and
    /// the main weather (Tm) reach the theater centre.</summary>
    internal readonly struct OpeningPlan
    {
        public OpeningPlan(float offset, float t1Minutes, float tmMinutes, bool front)
        { Offset = offset; T1Minutes = t1Minutes; TmMinutes = tmMinutes; Front = front; }
        public readonly float Offset, T1Minutes, TmMinutes;
        public readonly bool Front;
    }

    /// <summary>
    /// The mission-led opening (spec 5.5.3). Cases in order: a front opening (<c>placeFront</c> and a tape that opens with a front) puts the first change at the
    /// theater centre T1 minutes after the epoch, exactly; a class-only opening keeps the centre and everything downwind of it inside the opening run; a pinned story
    /// takes [0, 100] km; a sky pin takes [0, 250] km. Slice 4a passes <c>placeFront = false</c> from the live capture (D is held); slice 5 passes the key's Dynamic flag.
    /// </summary>
    internal static class TapeOpening
    {

        /// <summary>T1_hi = max(15, min(35, 75 - Dpre/v)) in minutes, Dpre in metres and v in m/s (spec 5.5.3).</summary>
        public static float T1High(float dpreMetres, float speed) => Math.Max(15f, Math.Min(35f, 75f - dpreMetres / Math.Max(1f, speed) / 60f));

        public static OpeningPlan Solve(WeatherTape tape, WeatherKey key, bool placeFront)
        {
            TapeInputs inputs = tape.Inputs;
            uint salt = unchecked((uint)Deterministic.Hash((int)inputs.Seed, inputs.LayoutSalt, inputs.MissionClass, 0x0FE1));
            float u1 = WeatherMath.Hash01(salt, 1), u2 = WeatherMath.Hash01(salt, 2);
            if (inputs.SkyPin < SkyLibrary.Count) return new OpeningPlan(u1 * 250000f, 0f, 0f, false);
            if (inputs.Story < WeatherStories.Count && !(placeFront && tape.OpensWithFront && tape.HasFront)) return new OpeningPlan(u1 * 100000f, 0f, 0f, false);
            if (placeFront && tape.OpensWithFront && tape.HasFront)
            {
                // The theater centre is the world origin: dot(p_c, u) = 0 and across = 0, so its tape position at D = 0 is M(0) and the offset
                // that puts the first change there T1 minutes after the epoch is s_ev - M(0) - v*60*T1, with no meander slack.
                float v = Math.Max(1f, key.SystemSpeed);
                TapeFront f = tape.Fronts[0];
                bool cold = f.SMain == f.SLead;
                float t1 = cold ? Scalar.Lerp(30f, 60f, u1) : Scalar.Lerp(15f, T1High(f.Dpre, v), u1);
                float tm = cold ? t1 : t1 + f.Dpre / v / 60f;
                float centre = tape.Frame(key.HeadingDeg, 0f).Meander(0f);
                return new OpeningPlan(f.SLead - centre - v * 60f * t1, t1, tm, true);
            }
            // Class-only: the centre and every point downwind of it lie in the opening run (downwind of 0 the first keyframe holds).
            float endRamp = tape.Keyframes.Length > 1 ? tape.Keyframes[1].Width : 0f;
            float room = Math.Max(0f, WeatherTape.OpeningRunMinMetres - tape.MeanderAmplitude - 0.5f * endRamp);
            return new OpeningPlan(u2 * room, 0f, 0f, false);
        }
    }
}
