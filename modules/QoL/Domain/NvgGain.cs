using System;

namespace BoscaliSummer.Modules.QoL.Domain
{
    internal enum NvgLook { Vanilla, WhitePhosphor, Fused }

    /// <summary>
    /// The image tube's gain, in exposure stops, pure. AUTO meters the picture and aims its average at a fixed
    /// brightness; a bright source cuts the gain at once (gating) and it creeps back afterwards, as a real tube does.
    /// </summary>
    internal struct NvgGain
    {
        /// <summary>Stops per second: down for a bright source, back up afterwards, and how fast "normal" follows.</summary>
        public const float Attack = 14f, Release = 1.6f, BaselineRate = 0.4f;
        /// <summary>How far under its recent normal the gain must sit before the GATE lamp lights.</summary>
        public const float GateMargin = 0.75f;
        /// <summary>The tube starts this many stops under its floor and warms up from there.</summary>
        public const float ColdStart = 2f;

        public float Ev, Baseline;
        public bool Gating => Baseline - Ev > GateMargin;

        /// <summary>What the meter reports: the average, pulled up by the brightest patch so one flare still counts.</summary>
        public static float Metered(float mean, float peak) => mean + 0.15f * Math.Max(0f, peak - mean);

        /// <summary>The gain the tube wants: <paramref name="step"/> 0 meters towards <paramref name="target"/>, 1-3 are fixed LOW / MED / HIGH.</summary>
        public static float Wanted(int step, float metered, float target, float min, float max)
        {
            switch (step)
            {
                case 1: return min + (max - min) * 0.4f;
                case 2: return min + (max - min) * 0.7f;
                case 3: return max;
                default:
                    float ev = (float)Math.Log(target / Math.Max(metered, 1e-6f), 2.0);
                    return Math.Max(min, Math.Min(max, ev));
            }
        }

        public void Start(float min) { Ev = min - ColdStart; Baseline = Ev; }

        public void Step(float wanted, float dt)
        {
            Ev = wanted < Ev ? Math.Max(wanted, Ev - Attack * dt) : Math.Min(wanted, Ev + Release * dt);
            // "Normal" drops slowly and rises with the tube, so a lasting bright scene stops counting as gating.
            Baseline = Ev > Baseline ? Ev : Math.Max(Ev, Baseline - BaselineRate * dt);
        }

        /// <summary>
        /// 0-1 heat of a unit from the game's own infrared sources (a propeller engine is about 2.5, an afterburner
        /// well above). Running surface vehicles and ships carry no infrared source in the game, so they get a
        /// flat warm value; anything else without a source is cold.
        /// </summary>
        public static float Heat01(float infrared, bool runningSurfaceUnit)
        {
            if (infrared <= 0.05f) return runningSurfaceUnit ? 0.45f : 0f;
            return Math.Min(1f, 0.35f + infrared / 6f);
        }
    }
}
