using System;

namespace BoscaliSummer.Modules.Hud.Domain
{
    /// <summary>What a one-beat flash of the visor's rim and brow is toned to: the warning red (a top warning came up) or the ink lifted to full bright (the power-on finished).</summary>
    internal enum FlashKind : byte { None, Warning, Bright }

    /// <summary>One frame's real inputs of the reactive visor (2026-11-16, "HELMET ON" round 2). The caller reads them off the game; nothing here is invented.</summary>
    internal struct VisorFxInput
    {
        /// <summary>Unscaled seconds since the last frame.</summary>
        public float Dt;
        /// <summary>The reactions may run: the option is on, no reduced motion, an overlay may show (no pause, map, cinematic), the cockpit view, a live aircraft. False: everything rests at once.</summary>
        public bool Allowed;
        /// <summary>The seat's load factor (signed: 1 at rest, negative when pushed) and the airframe's limit.</summary>
        public float G, GLimit;
        /// <summary>The aircraft's hit count (it only grows) and how heavy the last hit was (0..1: a round 0.5, a warhead's blast 1).</summary>
        public int HitSerial;
        public float HitWeight;
        /// <summary>The EMP disruption the mod models is running.</summary>
        public bool Emp;
        /// <summary>A top warning (missile, pull up, fire, engine out, lock) is up: the beat fires on the rising edge.</summary>
        public bool WarnUp;
        /// <summary>The power-on reached its closing line this frame: the end beat.</summary>
        public bool OnlineBeat;
    }

    /// <summary>
    /// The reactive visor (2026-11-16, "HELMET ON" round 2), pure: one small state machine the HUD roots read. It turns what really happens to the pilot and the jet into a per-frame look of the mod's own symbology:
    /// <list type="bullet">
    /// <item><b>G load</b>: above <see cref="GStart"/> g a vertical jitter and sag (1 to 4 px) and a dim to 70 % at the airframe's limit, eased in at 0.1 s and out over about 0.5 s; pushed (negative g) the symbology floats up a little.</item>
    /// <item><b>HIT</b>: 0.25-0.4 s of glitch (heavier is longer): sideways jumps, the board torn aside for three frames, a brightness flicker, then steady.</item>
    /// <item><b>EMP</b>: the symbology stutters out (alpha noise and sideways jitter, strongest at the onset, softening over a few seconds); when it ends <see cref="ResyncDue"/> asks for the visor's re-sync boot.</item>
    /// <item><b>Warning onset / power-on end</b>: one flash beat of 0.3 s (<see cref="Flash"/> of <see cref="FlashKind"/>), never more than about two a second.</item>
    /// <item><b>Idle life</b>: the boresight mark and the rim breathe (alpha 0.85-1 over 4 s) and a faint tick travels along the brow rail every 6 s.</item>
    /// </list>
    /// Offsets are reference pixels of a 1080-high screen, whole numbers (a line moved by a fraction would shimmer). Nothing allocates. While not <see cref="VisorFxInput.Allowed"/> every output is at rest.
    /// The sim can pin a reaction at a chosen time (<see cref="HoldGlitch"/>, <see cref="HoldFlash"/>, <see cref="GForce"/>, <see cref="EmpForce"/>) so a still can be taken of a moment that lasts a third of a second.
    /// </summary>
    internal sealed class VisorFx
    {
        public const float GStart = 5f, DimAtLimit = 0.7f, GAttackS = 0.12f, GReleaseS = 0.17f;
        public const float GlitchMinS = 0.25f, GlitchMaxS = 0.4f, FlashS = 0.3f, FlashGapS = 0.45f;
        public const float EmpOnsetS = 2.5f, EmpSlotS = 1f / 14f;
        public const float BreathPeriodS = 4f, BreathLow = 0.85f, ScanPeriodS = 6f, ScanS = 1.2f;

        // ---- outputs ----

        /// <summary>The symbology's shift (reference px, right and up) and the board's extra tear shift and the left column's (a glitch's torn band).</summary>
        public int ShiftX, ShiftY, TearBoard, TearLeft;
        /// <summary>The symbology's brightness, 0.05..1 (G dim, glitch flicker and EMP stutter multiplied).</summary>
        public float Alpha = 1f;
        /// <summary>The beat that tones the rim and brow: 0..1 and what to.</summary>
        public float Flash;
        public FlashKind FlashTone;
        /// <summary>Idle life: the boresight mark's and the rim's breathing alpha (0.85..1) and the scan tick's place along the rail (0..1; negative: not showing) with its alpha.</summary>
        public float BreathMark = 1f, BreathRim = 1f, ScanAt = -1f, ScanAlpha;
        /// <summary>An EMP just ended: the owner starts the re-sync boot (read once; reading clears it).</summary>
        public bool ResyncDue { get { bool d = resync; resync = false; return d; } }
        /// <summary>Any reaction is moving something now (the sim probe and the owner read it).</summary>
        public bool Reacting => Flash > 0f || Alpha < 0.999f || ShiftX != 0 || ShiftY != 0 || TearBoard != 0 || TearLeft != 0;
        /// <summary>What kind of reaction runs: for the sim probe.</summary>
        public bool GlitchRunning { get; private set; }
        /// <summary>The last frame's inputs said the reactions may run, and a warning was up (the sim probe reads them).</summary>
        public bool Live { get; private set; }
        public bool WarnLevel { get; private set; }
        public bool EmpRunning { get; private set; }
        public float GLoad => gk;

        // ---- the sim's pins ----

        /// <summary>NaN: the real load. Else this load factor is used.</summary>
        public float GForce = float.NaN;
        /// <summary>NaN: none. Else the running glitch's clock is held at this many seconds (start one with <see cref="StartGlitch"/>).</summary>
        public float HoldGlitch = float.NaN;
        /// <summary>NaN: none. Else the running flash's clock is held at this many seconds (start one with <see cref="StartFlash"/>).</summary>
        public float HoldFlash = float.NaN;
        /// <summary>-1 the real EMP flag, 0 forced off, 1 forced on.</summary>
        public int EmpForce = -1;
        /// <summary>NaN: none. Else the EMP's clock is held at this many seconds since onset.</summary>
        public float HoldEmp = float.NaN;

        private float t, gk, gNeg, glitchStart = float.NegativeInfinity, glitchDur, glitchWeight, flashStart = float.NegativeInfinity, flashAt = float.NegativeInfinity, empStart;
        private int hitSeen = int.MinValue;
        private bool warnWas, empWas, resync, seeded;

        public void Reset()
        {
            gk = gNeg = 0f;
            glitchStart = flashStart = flashAt = float.NegativeInfinity;
            GlitchRunning = EmpRunning = false;
            Rest();
        }

        private void Rest()
        {
            ShiftX = ShiftY = TearBoard = TearLeft = 0;
            Alpha = 1f;
            Flash = 0f;
            FlashTone = FlashKind.None;
            BreathMark = BreathRim = 1f;
            ScanAt = -1f;
            ScanAlpha = 0f;
        }

        /// <summary>The sim's pin: set the idle clock (the breathing and the scan tick follow it).</summary>
        public void Seek(float seconds) => t = seconds;

        /// <summary>Begin a glitch now (a hit of <paramref name="weight"/> 0..1).</summary>
        public void StartGlitch(float weight)
        {
            glitchStart = t;
            glitchWeight = Clamp01(weight);
            glitchDur = GlitchMinS + (GlitchMaxS - GlitchMinS) * glitchWeight;
        }

        /// <summary>Begin a flash beat now.</summary>
        public void StartFlash(FlashKind kind)
        {
            if (t - flashAt < FlashGapS) return;      // under three beats a second, whatever asks
            flashAt = t;
            flashStart = t;
            FlashTone = kind;
        }

        /// <summary>One frame.</summary>
        public void Step(in VisorFxInput i)
        {
            if (!seeded) { hitSeen = i.HitSerial; seeded = true; }
            Live = i.Allowed;
            WarnLevel = i.WarnUp;
            if (!i.Allowed)
            {
                hitSeen = i.HitSerial;      // a hit while the visor is not drawing is not replayed when it is
                warnWas = i.WarnUp;
                empWas = EmpForceOn(i.Emp);      // an EMP that ends while the visor rests asks for no re-sync: nobody saw it go
                Reset();
                return;
            }
            float dt = i.Dt > 0f && i.Dt < 0.25f ? i.Dt : 0f;
            t += dt;

            // ---- events ----
            if (i.HitSerial != hitSeen)
            {
                hitSeen = i.HitSerial;
                StartGlitch(i.HitWeight);
            }
            bool warn = i.WarnUp;
            if (warn && !warnWas) StartFlash(FlashKind.Warning);
            warnWas = warn;
            if (i.OnlineBeat) StartFlash(FlashKind.Bright);
            bool emp = EmpForceOn(i.Emp);
            if (emp && !empWas) empStart = t;
            if (!emp && empWas) resync = true;
            empWas = emp;

            // ---- G ----
            float g = float.IsNaN(GForce) ? i.G : GForce;
            float high = g > GStart ? Clamp01((g - GStart) / Math.Max(1f, i.GLimit - GStart)) : 0f;
            float tau = high > gk ? GAttackS : GReleaseS;
            gk += (high - gk) * (dt > 0f ? 1f - (float)Math.Exp(-dt / tau) : 0f);
            if (gk < 0.001f && high == 0f) gk = 0f;
            float low = g < 0f ? Clamp01(-g / 3f) : 0f;
            gNeg += (low - gNeg) * (dt > 0f ? 1f - (float)Math.Exp(-dt / GReleaseS) : 0f);
            if (gNeg < 0.001f && low == 0f) gNeg = 0f;
            float alpha = 1f - (1f - DimAtLimit) * gk;
            float x = 0f, y = 0f;
            if (gk > 0.02f)
            {
                float amp = 1f + 3f * gk;
                int slot = (int)(t * 24f);
                y += amp * (Hash(slot) * 2f - 1f) - 2f * gk;      // the symbols shake and sag a little behind the head
                x += amp * 0.5f * (Hash(slot + 977) * 2f - 1f);
            }
            y += 3f * gNeg;      // pushed: they float up

            // ---- hit glitch ----
            float tear = 0f, tearLeft = 0f;
            GlitchRunning = false;
            float u = float.IsNaN(HoldGlitch) ? t - glitchStart : HoldGlitch;
            if (u >= 0f && u < glitchDur)
            {
                GlitchRunning = true;
                Glitch(u, glitchDur, glitchWeight, out float jx, out tear, out tearLeft, out float ga);
                x += jx;
                alpha *= ga;
            }

            // ---- EMP ----
            EmpRunning = false;
            if (emp)
            {
                EmpRunning = true;
                float since = float.IsNaN(HoldEmp) ? t - empStart : HoldEmp;
                float onset = Clamp01(1f - since / EmpOnsetS);
                int slot = (int)(since / EmpSlotS);
                float r = Hash(slot * 3 + 11);
                if (r < 0.1f + 0.6f * onset)
                {
                    alpha *= 0.05f + 0.1f * Hash(slot + 5);
                    x += (Hash(slot + 31) < 0.5f ? -1f : 1f) * (2f + 5f * onset);
                }
                else alpha *= 0.55f + 0.45f * (1f - onset);
            }

            // ---- flash ----
            Flash = 0f;
            float fu = float.IsNaN(HoldFlash) ? t - flashStart : HoldFlash;
            if (fu >= 0f && fu < FlashS && FlashTone != FlashKind.None) Flash = FlashProfile(fu);
            else if (!(fu >= 0f && fu < FlashS)) FlashTone = FlashKind.None;

            // ---- idle life ----
            BreathMark = Breath(t, 0f);
            BreathRim = Breath(t, 0.37f);
            float phase = t + ScanPeriodS * 0.5f;      // the first tick comes 3 s in, not the moment the visor wakes
            float cycle = phase - (float)Math.Floor(phase / ScanPeriodS) * ScanPeriodS;
            if (cycle < ScanS) { ScanAt = cycle / ScanS; ScanAlpha = 0.55f * (float)Math.Sin(Math.PI * ScanAt); }
            else { ScanAt = -1f; ScanAlpha = 0f; }

            ShiftX = Round(x);
            ShiftY = Round(y);
            TearBoard = Round(tear);
            TearLeft = Round(tearLeft);
            Alpha = alpha < 0.05f ? 0.05f : alpha;
        }

        private bool EmpForceOn(bool real) => EmpForce < 0 ? real : EmpForce > 0;

        /// <summary>The glitch at <paramref name="u"/> seconds in: the symbology's sideways jump, the torn band's offsets (board and left column) and the brightness. Pure of its inputs; steady at its end.</summary>
        public static void Glitch(float u, float dur, float weight, out float jump, out float tearBoard, out float tearLeft, out float alpha)
        {
            float w = 0.7f + 0.3f * weight;
            jump = u < 0.05f ? 9f * w : u < 0.10f ? -7f * w : weight > 0.7f && u >= 0.18f && u < 0.22f ? 6f : 0f;
            tearBoard = u >= 0.10f && u < 0.145f ? 16f * w : 0f;
            tearLeft = u >= 0.12f && u < 0.16f ? -11f * w : 0f;
            float flick = (((int)(u * 25f)) & 1) == 0 ? 1f : 0.45f;
            float calm = Clamp01((u - dur * 0.75f) / (dur * 0.25f));      // the last quarter settles to steady
            alpha = flick + (1f - flick) * calm;
        }

        /// <summary>One beat: up in 50 ms, held to 120 ms, down by 300 ms.</summary>
        public static float FlashProfile(float u) => u < 0.05f ? u / 0.05f : u < 0.12f ? 1f : Clamp01(1f - (u - 0.12f) / (FlashS - 0.12f));

        /// <summary>The idle breathing: 0.85..1 over <see cref="BreathPeriodS"/>.</summary>
        public static float Breath(float time, float phase) =>
            BreathLow + (1f - BreathLow) * (0.5f + 0.5f * (float)Math.Sin((time / BreathPeriodS + phase) * 2.0 * Math.PI));

        private static int Round(float v) => (int)Math.Round(v, MidpointRounding.AwayFromZero);
        private static float Clamp01(float v) => v < 0f ? 0f : v > 1f ? 1f : v;

        /// <summary>A repeatable 0..1 noise of an integer (no state, no allocation).</summary>
        public static float Hash(int n)
        {
            unchecked
            {
                uint h = (uint)n * 2654435761u;
                h ^= h >> 15; h *= 2246822519u; h ^= h >> 13;
                return (h & 0xFFFFu) / 65535f;
            }
        }
    }
}
