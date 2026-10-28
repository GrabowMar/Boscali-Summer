using System;
using BoscaliSummer.Core.Ui;
using NOAvionics;

namespace BoscaliSummer.Modules.Hud.Domain
{
    /// <summary>
    /// The centre WARNING banner's alerts (2026-10-28), most urgent first: the enum order is the priority. The first four are warnings, the last two cautions. PULL UP leads
    /// (the ground kills faster than a missile, GPWS's own order); a second warning up at the same time is named on the detail line ("+FIRE"), never dropped.
    /// </summary>
    internal enum AlertKind : byte { PullUp, Missile, Fire, EngineOut, Locked, Bingo }

    /// <summary>This tick's alert conditions and the numbers their detail lines quote (the board's 10 Hz read).</summary>
    internal struct AlertInputs
    {
        /// <summary>Live missiles inbound, and the nearest one's seeker, range and seconds to impact (NaN: not known).</summary>
        public int Missiles;
        public string Seeker;
        public float MissileRangeM, MissileTtiS;
        /// <summary>The ground is closer than <see cref="HudCues.PullUpSeconds"/> at the sink rate; <see cref="GroundS"/> is how many seconds.</summary>
        public bool PullUp;
        public float GroundS;
        public bool Fire;
        public int Engines, EnginesOut;
        /// <summary>A radar has us locked: its name and range.</summary>
        public bool Locked;
        public string LockName;
        public float LockRangeM;
        /// <summary>Fuel at BINGO, and the field to go home to (null: none known) with its range.</summary>
        public bool Bingo;
        public string Field;
        public float FieldM;

        /// <summary>The raised alerts as a bit per <see cref="AlertKind"/>.</summary>
        public int Raised =>
            (Missiles > 0 ? 1 << (int)AlertKind.Missile : 0) | (PullUp ? 1 << (int)AlertKind.PullUp : 0) | (Fire ? 1 << (int)AlertKind.Fire : 0) |
            (EnginesOut > 0 ? 1 << (int)AlertKind.EngineOut : 0) | (Locked ? 1 << (int)AlertKind.Locked : 0) | (Bingo ? 1 << (int)AlertKind.Bingo : 0);
    }

    /// <summary>
    /// Which alerts are up, since when, and which one holds the banner, pure (the caller's clock). The attention rule: a warning holds the banner for as long as it is up;
    /// a caution interrupts once (it holds the banner <see cref="SpotlightS"/> after it comes up) and then leaves it to the TAC BOARD's lamps, which carry it on. An alert outlives its
    /// condition by <see cref="HoldS"/> (no flicker), and only one gone longer than <see cref="RearmS"/> comes back as new (onset blink, spotlight again).
    /// </summary>
    internal sealed class AlertBoard
    {
        public const int Count = 6;
        public const float HoldS = 0.8f, SpotlightS = 4f, RearmS = 5f;
        /// <summary>With the gear down only a sink this hard is a PULL UP, so a landing is quiet (the same rule as the voice warning, Immersion's <c>WarningMath</c>).</summary>
        public const float GearDownSinkMps = 10f;

        private readonly float[] since = new float[Count], seen = new float[Count];
        private int active;

        public AlertBoard() => Reset();

        public void Reset()
        {
            for (int k = 0; k < Count; k++) since[k] = seen[k] = float.NegativeInfinity;
            active = 0;
        }

        public bool IsUp(AlertKind kind) => (active >> (int)kind & 1) != 0;

        /// <summary>When the alert last came up as new.</summary>
        public float Since(AlertKind kind) => since[(int)kind];

        public static bool IsWarning(AlertKind kind) => kind <= AlertKind.EngineOut;

        /// <summary>PULL UP: the terrain cue says so, and with the gear down the sink is hard.</summary>
        public static bool PullUp(TerrainCue cue, float climbMps, bool gearDown) => cue == TerrainCue.PullUp && (!gearDown || -climbMps >= GearDownSinkMps);

        /// <summary>Take this tick's raised conditions (<see cref="AlertInputs.Raised"/>).</summary>
        public void Update(int raised, float now)
        {
            for (int k = 0; k < Count; k++)
            {
                int bit = 1 << k;
                if ((raised & bit) != 0)
                {
                    if ((active & bit) == 0 && now - seen[k] > RearmS) since[k] = now;
                    seen[k] = now;
                    active |= bit;
                }
                else if ((active & bit) != 0 && now - seen[k] > HoldS) active &= ~bit;
            }
        }

        /// <summary>The alert on the banner: the most urgent warning up, else the most urgent caution still in its spotlight. False: the banner is empty.</summary>
        public bool Banner(float now, out AlertKind kind)
        {
            for (int k = 0; k < Count; k++)
            {
                kind = (AlertKind)k;
                if ((active >> k & 1) != 0 && (IsWarning(kind) || now - since[k] < SpotlightS)) return true;
            }
            kind = default;
            return false;
        }
    }

    /// <summary>The banner's words and blink, pure.</summary>
    internal static class AlertText
    {
        private static readonly string[] Words = { "PULL UP", "MISSILE", "FIRE", "ENGINE OUT", "LOCKED", "BINGO FUEL" };
        private static readonly AvIcon[] Icons = { AvIcon.ArrowUp, AvIcon.Rocket, AvIcon.Flame, AvIcon.Gauge, AvIcon.Lock, AvIcon.Runway };

        public static string Word(AlertKind kind) => Words[(int)kind];

        /// <summary>The word's picture, from the kit's icon font (the C-menu's family): an alert reads by its word, its colour AND its icon, never by colour alone.</summary>
        public static AvIcon Icon(AlertKind kind) => Icons[(int)kind];

        /// <summary>A timed alert's countdown runs over this many seconds: the fuse under the banner is full at this time to impact (or to the ground) and gone at none.</summary>
        public const float CountdownS = 10f;

        /// <summary>The share of the fuse left, 0..1; NaN for an alert with no clock (FIRE, ENGINE OUT, LOCKED, BINGO FUEL) or a clock not known.</summary>
        public static float Countdown(AlertKind kind, in AlertInputs a)
        {
            float s = kind == AlertKind.Missile ? a.MissileTtiS : kind == AlertKind.PullUp ? a.GroundS : float.NaN;
            return float.IsNaN(s) || float.IsInfinity(s) ? float.NaN : Math.Max(0f, Math.Min(1f, s / CountdownS));
        }

        /// <summary>Whether the banner's word is lit <paramref name="age"/> s after it came up: the onset blink, then a missile keeps blinking (2 Hz, 3 Hz under 3 s to impact) and the rest hold.</summary>
        public static bool Lit(AlertKind kind, float age, float ttiS)
        {
            if (age < TopCentre.OnsetS) return TopCentre.Lit(age, TopCentre.UrgentHz);
            if (kind != AlertKind.Missile) return true;
            return TopCentre.Lit(age, ttiS < 3f ? TopCentre.UrgentHz : TopCentre.BlinkHz);
        }

        /// <summary>
        /// The detail line under the word into <paramref name="b"/>; returns its length. "2 INBOUND · ARH · 4.2KM · 6S · BEAM + CHAFF" (the THREAT page's advice for the seeker),
        /// "TERRAIN · 3S", "HLT RADAR · 35KM", "RTB · K92 HIGHWAY STRIP · 22KM".
        /// </summary>
        public static int Detail(char[] b, AlertKind kind, in AlertInputs a, AvUnits units)
        {
            int n = 0;
            switch (kind)
            {
                case AlertKind.Missile:
                    if (a.Missiles > 1) { n = AvNumFormat.Write(b, n, a.Missiles, 0); n = AvNumFormat.Append(b, n, " INBOUND"); }
                    if (!string.IsNullOrEmpty(a.Seeker)) n = Up(b, Sep(b, n), a.Seeker);
                    if (Finite(a.MissileRangeM)) n = Up(b, Sep(b, n), a.MissileRangeM, units);
                    if (Finite(a.MissileTtiS)) { n = AvNumFormat.Write(b, Sep(b, n), Math.Max(0f, a.MissileTtiS), 0); n = AvNumFormat.Append(b, n, "S"); }
                    ThreatSeeker seeker = ThreatMath.ClassOf(a.Seeker);
                    if (seeker != ThreatSeeker.Other) n = AvNumFormat.Append(b, Sep(b, n), seeker == ThreatSeeker.Ir ? "FLARES" : "BEAM + CHAFF");
                    break;
                case AlertKind.PullUp:
                    n = AvNumFormat.Append(b, n, "TERRAIN");
                    if (Finite(a.GroundS)) { n = AvNumFormat.Write(b, Sep(b, n), Math.Max(0f, a.GroundS), 0); n = AvNumFormat.Append(b, n, "S"); }
                    break;
                case AlertKind.Fire:
                    n = AvNumFormat.Append(b, n, "AIRFRAME ON FIRE");
                    break;
                case AlertKind.EngineOut:
                    if (a.Engines > 1 && a.EnginesOut < a.Engines)
                    {
                        n = AvNumFormat.Write(b, n, a.EnginesOut, 0);
                        n = AvNumFormat.Append(b, n, " OF ");
                        n = AvNumFormat.Write(b, n, a.Engines, 0);
                        n = AvNumFormat.Append(b, n, " ENGINES");
                    }
                    else n = AvNumFormat.Append(b, n, "NO THRUST");
                    break;
                case AlertKind.Locked:
                    if (!string.IsNullOrEmpty(a.LockName)) n = Up(b, n, a.LockName);
                    if (Finite(a.LockRangeM)) n = Up(b, Sep(b, n), a.LockRangeM, units);
                    break;
                case AlertKind.Bingo:
                    n = AvNumFormat.Append(b, n, "RTB");
                    if (!string.IsNullOrEmpty(a.Field)) n = Up(b, Sep(b, n), a.Field);
                    if (!string.IsNullOrEmpty(a.Field) && Finite(a.FieldM)) n = Up(b, Sep(b, n), a.FieldM, units);
                    break;
            }
            return n;
        }

        private static bool Finite(float v) => !float.IsNaN(v) && !float.IsInfinity(v);

        private static int Sep(char[] b, int n) => n == 0 ? 0 : AvNumFormat.Append(b, n, " · ");

        /// <summary>Text in capitals, cut at the buffer's end.</summary>
        private static int Up(char[] b, int n, string s)
        {
            for (int i = 0; i < s.Length && n < b.Length; i++) b[n++] = char.ToUpperInvariant(s[i]);
            return n;
        }

        private static int Up(char[] b, int n, float metres, AvUnits units)
        {
            int start = n;
            n = AvUnitTable.DistanceReading(b, n, metres, units);
            for (int i = start; i < n; i++) b[i] = char.ToUpperInvariant(b[i]);
            return n;
        }
    }

    /// <summary>
    /// The THREAT RING round the screen centre (2026-10-28), pure: a mark on the ring for every live inbound missile, pointing the way it is, in the camera's frame (so it is
    /// right in the cockpit, chase and orbit alike). Units are the layer's, centre origin, y up.
    /// </summary>
    internal static class ThreatRing
    {
        /// <summary>
        /// The ring is an ellipse this share of the screen's width and height out from the centre: wide enough to clear the cockpit HUD's speed and altitude columns and the
        /// third-person SPD / ALT boxes at three and nine o'clock (in game a 0.26 H circle sat on the altitude readout), and under the WARNING banner at twelve.
        /// </summary>
        public const float WidthShare = 0.27f, HeightShare = 0.30f;

        /// <summary>
        /// Where on the ring a threat's mark goes, in degrees (0 = right, counter-clockwise). <paramref name="x"/>/<paramref name="y"/>/<paramref name="z"/>: the threat in the camera's
        /// frame (x right, y up, z ahead); <paramref name="sx"/>/<paramref name="sy"/>: its offset from the screen centre when it is ahead. False: it is ahead, on the screen and inside
        /// the ring (semi-axes <paramref name="rx"/>, <paramref name="ry"/>), where the game's own flashing marker already shows it. Behind, the mark points the way to turn; dead astern
        /// reads six o'clock.
        /// </summary>
        public static bool Place(float x, float y, float z, float sx, float sy, float rx, float ry, float halfW, float halfH, out float angleDeg)
        {
            angleDeg = -90f;
            if (z > 0f)
            {
                if (Math.Abs(sx) <= halfW && Math.Abs(sy) <= halfH && (sx / rx) * (sx / rx) + (sy / ry) * (sy / ry) <= 1f) return false;
                angleDeg = (float)(Math.Atan2(sy, sx) * 180.0 / Math.PI);
                return true;
            }
            if (x * x + y * y > 1e-6f) angleDeg = (float)(Math.Atan2(y, x) * 180.0 / Math.PI);
            return true;
        }

        /// <summary>How far out from the centre the ellipse is along <paramref name="angleDeg"/>.</summary>
        public static float Reach(float angleDeg, float rx, float ry)
        {
            double a = angleDeg * Math.PI / 180.0, c = Math.Cos(a) / rx, s = Math.Sin(a) / ry;
            return (float)(1.0 / Math.Sqrt(c * c + s * s));
        }

        /// <summary>The chevron's size: 14 far out, growing to 24 as impact nears (2 s); NaN reads far.</summary>
        public static float Size(float ttiS) => float.IsNaN(ttiS) ? 14f : 14f + 10f * (1f - Math.Max(0f, Math.Min(1f, (ttiS - 2f) / 10f)));

        /// <summary>The screen-edge glow toward a missile: none until impact is under <see cref="EdgeS"/>, then rising to <see cref="EdgeMax"/> as it closes (steady, never a flash).</summary>
        public const float EdgeS = 6f, EdgeMax = 0.45f;

        public static float EdgeAlpha(float ttiS) => !(ttiS < EdgeS) ? 0f : EdgeMax * (0.25f + 0.75f * (1f - Math.Max(0f, ttiS) / EdgeS));

        /// <summary>Where the ray from the screen centre at <paramref name="angleDeg"/> leaves a screen of half extents <paramref name="hw"/> x <paramref name="hh"/>; true when it leaves through a side.</summary>
        public static bool ExitsSide(float angleDeg, float hw, float hh, out float ex, out float ey)
        {
            double a = angleDeg * Math.PI / 180.0, c = Math.Cos(a), s = Math.Sin(a);
            double tx = Math.Abs(c) > 1e-6 ? hw / Math.Abs(c) : double.MaxValue, ty = Math.Abs(s) > 1e-6 ? hh / Math.Abs(s) : double.MaxValue, t = Math.Min(tx, ty);
            ex = (float)(c * t);
            ey = (float)(s * t);
            return tx <= ty;
        }

        /// <summary>Steady until impact is under 3 s, then the urgent blink.</summary>
        public static bool Lit(float ttiS, float now) => !(ttiS < 3f) || TopCentre.Lit(now, TopCentre.UrgentHz);
    }
}
