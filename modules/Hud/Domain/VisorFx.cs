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
        /// <summary>How hurt the jet is, 0..1 (see <see cref="VisorDamage.Level"/>): under <see cref="VisorFx.DegradeMin"/> nothing glitches, above it the visor glitches in bursts, more often and harder as it climbs to 1.</summary>
        public float Degrade;
        /// <summary>How hard an enemy jammer is working on the pilot's jet, 0..1 (the game's own <c>CombatHUD.jamAccumulation</c>).</summary>
        public float Jam;
        /// <summary>The glitch shader is loaded: the tears, split, dropouts and bars are drawn by it. False: the older per-group shifts and brightness noise stand in.</summary>
        public bool ShaderOn;
        /// <summary>The pilot asked for no flashing: no brightness flicker faster than 3 Hz and no full dropouts (tears and noise bars stay).</summary>
        public bool ReducedFlashes;
    }

    /// <summary>The jet's damage as one number for the visor (pure).</summary>
    internal static class VisorDamage
    {
        /// <summary>Structure left (share of parts not torn off) under which the visor starts to glitch, and under which it glitches hard.</summary>
        public const float StructureLight = 0.60f, StructureHeavy = 0.30f;

        /// <summary>
        /// 0: healthy. 0.4..0.6: significant damage (under 60 % of the structure left, or an engine out). 0.8..1: heavy (under 30 % left, an engine fire or fuel fire, or every engine out).
        /// </summary>
        public static float Level(in DamageState s)
        {
            float left = 1f - Math.Min(1f, Math.Max(0f, s.DetachedRatio));
            float d = left >= StructureLight ? 0f
                : left >= StructureHeavy ? 0.4f + 0.2f * (StructureLight - left) / (StructureLight - StructureHeavy)
                : 0.8f + 0.2f * Math.Min(1f, (StructureHeavy - left) / StructureHeavy);
            if ((s.Flags & DamageFlags.Fire) != 0) d = 1f;
            else if (s.Engines > 0 && s.EnginesOut >= s.Engines) d = Math.Max(d, 0.85f);
            else if (s.EnginesOut > 0) d = Math.Max(d, 0.45f);
            return d;
        }
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
        public bool Reacting => Flash > 0f || Alpha < 0.999f || ShiftX != 0 || ShiftY != 0 || TearBoard != 0 || TearLeft != 0 || Glitching;
        /// <summary>What kind of reaction runs: for the sim probe.</summary>
        public bool GlitchRunning { get; private set; }
        /// <summary>The last frame's inputs said the reactions may run, and a warning was up (the sim probe reads them).</summary>
        public bool Live { get; private set; }
        public bool WarnLevel { get; private set; }
        public bool EmpRunning { get; private set; }
        public float GLoad => gk;

        // ---- BROKEN HELMET (2026-11-16): the glitch shader's terms, set every frame (all zero at rest; see AvionicsUi/Assets/Source/NOA_VisorGlitch.cginc for what each one does) ----

        /// <summary>Damage glitching starts at this <see cref="VisorFxInput.Degrade"/>; 1 is the hardest.</summary>
        public const float DegradeMin = 0.35f;
        /// <summary>Slots per second of the noise that holds still for a step (blocks, tear places, bar jitter), and of the brightness flicker under reduced flashes.</summary>
        public const float NoiseHz = 14f, CalmFlickHz = 3f;

        /// <summary>Three tear bands, four floats each: y (0 top .. 1 bottom), height (0..1; 0 = none), sideways shift in screen px, seam strength.</summary>
        public readonly float[] Tear = new float[12];
        /// <summary>Ghost split in px; fine scanline shimmer 0..1; vertical roll in px.</summary>
        public float Split, Fine, Roll;
        /// <summary>Dropout-block density 0..1, the brightness left inside a dropped block (0 blank), and the whole picture's flicker multiplier (1 steady).</summary>
        public float DropDensity, DropFloor = 1f, Flick = 1f;
        /// <summary>Rolling bars: strength 0..1, position of the first (0..1 down the screen), the gap to the second, sideways jitter in px inside a bar.</summary>
        public float BarStrength, BarPos, BarGap = 0.37f, BarJitter;
        /// <summary>The block and jitter seed (an integer-valued float that changes <see cref="NoiseHz"/> times a second) and the visor's clock.</summary>
        public float Seed, Clock;
        /// <summary>Any shader term is on (the master of the shader: 0 draws the stock look).</summary>
        public bool Glitching;
        /// <summary>A damage burst is running (the owner plays the glitch sound on its edge).</summary>
        public bool BurstRunning { get; private set; }
        /// <summary>Corrupted characters on the non-critical captions: on for the first part of a heavy damage burst; <see cref="ScrambleSeed"/> picks which glyphs.</summary>
        public bool Scramble;
        public int ScrambleSeed;
        /// <summary>The jam level the visor shows (smoothed, 0..1) and the display jitter it asks of the brow's bearing carets, in degrees (display only).</summary>
        public float JamLevel, JamJitterDeg;
        /// <summary>The damage level in use and whether the jam signature is up (the brow shows its word).</summary>
        public float DegradeLevel;
        public bool JamUp => JamLevel > 0.15f;
        /// <summary>The visor is re-syncing after a jam ended.</summary>
        public bool Resyncing { get; private set; }

        // ---- the sim's pins ----

        /// <summary>NaN: the real damage level. Else this level is used.</summary>
        public float DegradeForce = float.NaN;
        /// <summary>NaN: the real jam. Else this level is used.</summary>
        public float JamForce = float.NaN;
        /// <summary>NaN: the burst scheduler runs. Else a damage burst is held at this many seconds in (it needs a level over <see cref="DegradeMin"/>).</summary>
        public float BurstHold = float.NaN;

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
        private bool burstOn, degradeWas, jamWas;
        private float burstStart, burstDur, burstK, nextBurst, jamK, resyncStart = float.NegativeInfinity;
        private int burstCount;

        public void Reset()
        {
            gk = gNeg = 0f;
            glitchStart = flashStart = flashAt = float.NegativeInfinity;
            GlitchRunning = EmpRunning = false;
            burstOn = degradeWas = jamWas = false;
            jamK = 0f;
            resyncStart = float.NegativeInfinity;
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
            ClearGlitch();
            JamLevel = DegradeLevel = 0f;
            BurstRunning = Resyncing = Scramble = false;
        }

        private void ClearGlitch()
        {
            Array.Clear(Tear, 0, Tear.Length);
            Split = Fine = Roll = DropDensity = BarStrength = JamJitterDeg = 0f;
            DropFloor = Flick = 1f;
            Glitching = false;
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
            float tear = 0f, tearLeft = 0f, hitU = 0f;
            bool hitOn = false;
            GlitchRunning = false;
            float u = float.IsNaN(HoldGlitch) ? t - glitchStart : HoldGlitch;
            if (u >= 0f && u < glitchDur)
            {
                GlitchRunning = true;
                Glitch(u, glitchDur, glitchWeight, out float jx, out tear, out tearLeft, out float ga);
                x += jx;
                alpha *= i.ReducedFlashes ? 1f : ga;
                if (i.ShaderOn) { tear = tearLeft = 0f; hitOn = true; hitU = u; }      // the shader tears; the groups stay put
            }

            // ---- EMP ----
            float empSince = 0f;
            EmpRunning = false;
            if (emp)
            {
                EmpRunning = true;
                float since = float.IsNaN(HoldEmp) ? t - empStart : HoldEmp;
                float onset = Clamp01(1f - since / EmpOnsetS);
                int slot = (int)(since / EmpSlotS);
                float r = Hash(slot * 3 + 11);
                empSince = since;
                if (i.ShaderOn) { }      // the shader draws the stutter: tear storm, hard dropouts, dark slots (ComposeGlitch)
                else if (r < 0.1f + 0.6f * onset)
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

            // ---- broken helmet: damage bursts and the jam signature, then every shader term ----
            Clock = t;
            StepDamageAndJam(i, dt);
            if (i.ShaderOn) ComposeGlitch(i, hitOn, hitU, emp, empSince);
            else { ClearGlitch(); Scramble = false; }

            ShiftX = Round(x);
            ShiftY = Round(y);
            TearBoard = Round(tear);
            TearLeft = Round(tearLeft);
            Alpha = alpha < 0.05f ? 0.05f : alpha;
        }

        /// <summary>How long the re-sync flicker after a jam lasts.</summary>
        public const float ResyncS = 0.45f;

        /// <summary>The damage burst scheduler (a burst every 6-10 s once damage is significant, every 1-3 s when heavy) and the jam level's smoothing and re-sync edge.</summary>
        private void StepDamageAndJam(in VisorFxInput i, float dt)
        {
            float deg = float.IsNaN(DegradeForce) ? i.Degrade : DegradeForce;
            DegradeLevel = deg;
            bool on = deg >= DegradeMin;
            if (!on) { burstOn = false; degradeWas = false; }
            else
            {
                float k = Clamp01((deg - DegradeMin) / (1f - DegradeMin));
                if (!degradeWas) { degradeWas = true; nextBurst = t + 1.2f + 1.5f * Hash(burstCount * 7 + 3); }
                if (!burstOn && t >= nextBurst)
                {
                    burstOn = true;
                    burstStart = t;
                    burstK = k;
                    burstDur = (0.3f + 0.8f * k) * (0.8f + 0.4f * Hash(burstCount * 3 + 1));
                    burstCount++;
                }
                if (burstOn && t >= burstStart + burstDur)
                {
                    burstOn = false;
                    nextBurst = t + (8f - 6.5f * k) * (0.75f + 0.5f * Hash(burstCount * 5 + 2));      // 6-10 s at the light end, 1-3 s when heavy
                }
                if (!float.IsNaN(BurstHold)) burstK = k;
            }
            BurstRunning = on && (burstOn || !float.IsNaN(BurstHold));

            float jt = Clamp01(float.IsNaN(JamForce) ? i.Jam : JamForce);
            float tau = jt > jamK ? 0.2f : 0.5f;
            jamK += (jt - jamK) * (dt > 0f ? 1f - (float)Math.Exp(-dt / tau) : 0f);
            if (jt == 0f && jamK < 0.01f) jamK = 0f;
            if (jamK > 0.2f) jamWas = true;
            else if (jamWas && jt < 0.05f && jamK < 0.1f) { jamWas = false; resyncStart = t; }      // the jammer let go: a short re-sync flicker
            JamLevel = jamK;
            Resyncing = t - resyncStart < ResyncS;
        }

        private void AddBand(ref int n, float y, float h, float shift, float seam)
        {
            if (n >= 3) return;
            int o = n * 4;
            Tear[o] = y; Tear[o + 1] = h; Tear[o + 2] = shift; Tear[o + 3] = seam;
            n++;
        }

        private static float Sign(float h) => h < 0.5f ? -1f : 1f;

        /// <summary>
        /// Every shader term of this frame from the real states, strongest source wins per term: the hit's tear + split, a damage burst (light: a tear and a flicker; heavy: up to three bands, split, dropout blocks, corrupted captions),
        /// the EMP's tear storm and hard dropouts, the jam's rolling bars, shimmer and split, and the re-sync flicker when a jam ends. Under reduced flashes the brightness holds for a third of a second and no block goes fully dark.
        /// </summary>
        private void ComposeGlitch(in VisorFxInput i, bool hitOn, float hitU, bool emp, float empSince)
        {
            ClearGlitch();
            Scramble = false;
            int nb = 0, slot = (int)(Clock * NoiseHz), flickSlot = (int)(Clock * CalmFlickHz);
            bool calm = i.ReducedFlashes;
            Seed = (calm ? flickSlot * 5 : slot) % 997;      // blocks hold for a third of a second under reduced flashes
            float split = 0f, drop = 0f, floor = 1f, flick = 1f, fine = 0f, roll = 0f;

            if (hitOn)
            {
                float w = glitchWeight;
                int s = (int)(hitU * 22f);
                AddBand(ref nb, 0.08f + 0.84f * Hash(s * 3 + 1), 0.05f + 0.08f * w, Sign(Hash(s * 3 + 2)) * (10f + 18f * w), 1f);
                if (w > 0.6f) AddBand(ref nb, 0.08f + 0.84f * Hash(s * 5 + 9), 0.04f, Sign(Hash(s * 7 + 4)) * (8f + 10f * w), 1f);
                split = Math.Max(split, 2f + 2f * w);
                drop = Math.Max(drop, 0.08f + 0.12f * w);
                floor = Math.Min(floor, calm ? 0.5f : 0.35f);
            }

            bool pinned = !float.IsNaN(BurstHold) && DegradeLevel >= DegradeMin;
            if (burstOn || pinned)
            {
                float k = burstK, u = pinned ? BurstHold : Clock - burstStart, dur = pinned ? 99f : burstDur;
                float env = pinned ? 1f : 1f - Clamp01((u - dur * 0.7f) / (dur * 0.3f));
                int s = (int)(u * NoiseHz), b = burstCount;
                int bands = 1 + (Hash(s * 13 + b) < 0.35f + 0.5f * k ? 1 : 0) + (k > 0.6f && Hash(s * 17 + b) < 0.7f ? 1 : 0);
                for (int n = 0; n < bands; n++)
                    AddBand(ref nb, 0.06f + 0.88f * Hash(s * 11 + n * 5 + b * 3), 0.03f + 0.07f * k * Hash(s + n + 1 + b), Sign(Hash(s * 19 + n + b)) * (4f + (5f + 22f * k) * Hash(s * 23 + n * 7 + b)) * env, 1f);
                float hv = Clamp01((DegradeLevel - 0.65f) / 0.25f);      // heavy: dropouts, ghosts, corrupted words
                if (hv > 0f)
                {
                    split = Math.Max(split, (1f + 3f * hv) * (Hash(s * 2 + b) < 0.75f ? 1f : 0f) * env);
                    drop = Math.Max(drop, (0.06f + 0.24f * hv * Hash(s * 29 + b)) * env);
                    floor = Math.Min(floor, calm ? 0.5f : 0.1f);
                    Scramble = hv > 0.3f && u < dur * 0.7f;
                    ScrambleSeed = b * 131 + s / 3;
                }
                float fl = calm ? (Hash(flickSlot * 5 + b) < 0.5f ? 0.55f : 1f) : (Hash(s * 31 + b) < 0.45f ? 0.3f + 0.35f * (1f - k) : 1f);
                flick = Math.Min(flick, 1f + (fl - 1f) * env);
            }

            if (emp)
            {
                float onset = Clamp01(1f - empSince / EmpOnsetS);
                int s = (int)(empSince * NoiseHz);
                for (int n = 0; n < 3; n++)
                    AddBand(ref nb, Hash(s * 7 + n * 3 + 1), 0.04f + 0.10f * onset * Hash(s + n * 9 + 2), Sign(Hash(s * 5 + n * 13)) * (8f + (10f + 30f * onset) * Hash(s * 3 + n)), 1f);
                split = Math.Max(split, 2f + 2f * onset);
                drop = Math.Max(drop, 0.25f + 0.5f * onset);
                floor = Math.Min(floor, calm ? 0.5f : 0f);
                fine = Math.Max(fine, 0.5f);
                int es = calm ? (int)(empSince * CalmFlickHz) : (int)(empSince / EmpSlotS);
                flick = Math.Min(flick, Hash(es * 3 + 11) < 0.1f + 0.6f * onset ? (calm ? 0.55f : 0.1f) : 1f);
            }

            if (jamK > 0.03f)
            {
                float j = jamK;
                BarStrength = j;
                BarPos = Clock * 0.17f - (float)Math.Floor(Clock * 0.17f);
                BarJitter = 3f + 9f * j;
                fine = Math.Max(fine, 0.35f + 0.65f * j);
                split = Math.Max(split, (2f + 1.5f * Hash(slot / 2 + 3)) * Clamp01(j * 2f));
                if (Hash(slot * 3 + 1) < 0.35f) roll = Sign(Hash(slot + 9)) * (j > 0.5f ? 2f : 1f);
                float cycle = Clock / 3.1f - (float)Math.Floor(Clock / 3.1f);
                if (cycle < 0.04f) AddBand(ref nb, 0.15f + 0.7f * Hash((int)(Clock / 3.1f) * 7 + 1), 0.05f, Sign(Hash((int)(Clock / 3.1f))) * 7f * j, 1f);
                drop = Math.Max(drop, 0.03f * j);
                floor = Math.Min(floor, 0.5f);
                if (!calm && Hash(slot * 7 + 3) < 0.1f) flick = Math.Min(flick, 0.8f);
                JamJitterDeg = (Hash(slot * 31 + 1) * 2f - 1f) * 1.6f * j;
            }

            if (Resyncing)
            {
                float ru = Clock - resyncStart;
                int rs = (int)(ru * 10f);
                if (ru < 0.15f) AddBand(ref nb, 0.3f + 0.4f * Hash(rs + 5), 0.06f, Sign(Hash(rs + 2)) * 12f, 1f);
                split = Math.Max(split, 2f);
                flick = Math.Min(flick, calm ? (ru < 0.33f ? 0.6f : 1f) : (rs % 2 == 1 ? 0.35f : 1f));
            }

            Split = split;
            DropDensity = drop;
            DropFloor = floor;
            Flick = flick;
            Fine = fine;
            Roll = roll;
            Glitching = nb > 0 || split > 0.4f || drop > 0f || flick < 0.999f || BarStrength > 0.01f || fine > 0.01f || roll != 0f;
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
