using System;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Modules.Hud.Domain;
using NOAvionics;
using static BoscaliSummer.Core.Contracts.ScopeTone;

namespace BoscaliSummer.Modules.Hud.Presentation
{
    /// <summary>
    /// OBJECTIVE (prototype: <c>pages/objective.js</c>): the pilot's selected contract as a plot: the zone ring (the hold arc runs around it), the known
    /// air-defence rings with the altitude-limited reach that shrinks as he descends, himself with his trail and a line to the zone; beside it the
    /// deadline, the zone's range and bearing, the hold gates (RNG ALT LOS TRK), the payout and the cone of the nearest radar SAM seen from the side
    /// (or, with none known, the host's next instruction); under it the objective pips. Fed by <see cref="ObjectiveScope"/> (DynamicOperations'
    /// provider over <see cref="ISecondaryObjectivesView"/> and <see cref="IThreatPicture"/>). Live while the selected contract is accepted, marked
    /// and in its vicinity. Entering an air-defence envelope is announced once on the ticker.
    /// </summary>
    internal sealed partial class ObjectivePage : ScopePageBase
    {
        private const int TrailPoints = 13;
        private const float TrailEvery = 0.35f, EnvelopeFlash = 3f, DoneSeconds = 1.5f;

        private ObjectiveScope d;
        private bool live, strip, primed, inSam, inGun, wasInEnvelope;
        private float now, trailAt, rangeKm;
        private readonly float[] trailX = new float[TrailPoints], trailZ = new float[TrailPoints];
        private int trailN, trailId, lastCurrent;
        private float doneAt = float.NegativeInfinity, strikeDoneAt = float.NegativeInfinity, samAt = float.NegativeInfinity;
        private string doneWord;

        // cached words (rebuilt when what they say changes, never per frame)
        private string subText, lineText, moneyText, xpText, multText, totalText, zoneText, headlineText;
        private readonly string[] nextLines = new string[3];
        private int subKey = -1, lineKey = -1, moneyKey = -1, xpKey = -1, totalKey = -1, zoneKey = -1, headlineKey = -1, nextCount;
        private string nextSource;
        private float multKey = -1f;
        private ScopeTone headlineTone;
        private bool headlineFlash;
        private string aglText, aglSub, samSub, phaseSub;
        private int aglKey = int.MinValue, aglSubKey = int.MinValue, samSubKey = int.MinValue, phaseSubKey = int.MinValue;
        private readonly char[] scratch = new char[24];
        private readonly float[] aglTicks = new float[1];

        public override ScopePage Id => ScopePage.Objective;

        public override string Title => "OBJECTIVE";

        public override char Code => 'O';

        public override bool Fast => live;

        public override bool HasStrip => strip;

        // ------------------------------------------------------------------------------------------------ poll

        public override bool Poll(in ScopeEnv env)
        {
            now = env.Now;
            units = env.Units;
            if (!ScopeFeeds.TryRead(out d) || d.Id == 0) { Forget(); return false; }
            live = true;
            strip = d.Phases != null && d.PhaseCount > 0;

            if (d.Id != trailId) NewContract();
            if (trailN == 0 || now - trailAt >= TrailEvery) Sample();
            ReadAir(env);
            Phases();
            ReadHeadline(env.Units);
            primed = true;
            return true;
        }

        private void Forget()
        {
            live = strip = primed = inSam = inGun = wasInEnvelope = false;
            trailId = 0;
            trailN = 0;
        }

        private void NewContract()
        {
            trailId = d.Id;
            trailN = 0;
            rangeKm = 0f;
            primed = false;
            lastCurrent = d.Current;
            doneAt = strikeDoneAt = samAt = float.NegativeInfinity;
            subKey = lineKey = totalKey = zoneKey = headlineKey = -1;
            nextSource = null;
            wasInEnvelope = false;
        }

        /// <summary>One more point of the pilot's trail (global coordinates: a floating-origin shift cannot bend it).</summary>
        private void Sample()
        {
            if (trailN == TrailPoints)
            {
                Array.Copy(trailX, 1, trailX, 0, TrailPoints - 1);
                Array.Copy(trailZ, 1, trailZ, 0, TrailPoints - 1);
                trailN--;
            }
            trailX[trailN] = d.OwnX;
            trailZ[trailN] = d.OwnZ;
            trailN++;
            trailAt = now;
        }

        /// <summary>Is the pilot inside the reach of a known radar SAM, of a gun or an IR SAM; the first time, say so.</summary>
        private void ReadAir(in ScopeEnv env)
        {
            bool sam = false, gun = false;
            int nearest = -1;
            float best = float.MaxValue;
            for (int i = 0; i < d.RingCount; i++)
            {
                SamExposure e = ObjectiveMath.Expose(d.Rings[i], d.OwnX, d.OwnZ, d.OwnAgl);
                if (!e.Inside) continue;
                if (ObjectiveMath.Radar(d.Rings[i])) sam = true; else gun = true;
                if (e.DistanceM < best) { best = e.DistanceM; nearest = i; }
            }
            if ((sam || gun) && !wasInEnvelope && primed)
            {
                SamExposure e = ObjectiveMath.Expose(d.Rings[nearest], d.OwnX, d.OwnZ, d.OwnAgl);
                bool radar = ObjectiveMath.Radar(d.Rings[nearest]);
                env.Notice?.Invoke(radar ? "sam" : "gun", HudTone.Caution, "IN " + ObjectiveMath.Kind(d.Rings[nearest].Kind) + " ENVELOPE - " + ScopeStr.Distance("", e.DistanceM, env.Units, 12).ToString(),
                    radar && e.MaskM > 0f ? "BELOW " + (int)Math.Ceiling(AvUnitTable.Altitude(e.MaskM, env.Units)) + (env.Units == AvUnits.Imperial ? " FT" : " M") + " IS MASKED" : null);
            }
            if (sam && !inSam) samAt = now;
            wasInEnvelope = sam || gun;
            inSam = sam;
            inGun = gun;
        }

        /// <summary>A phase finished: say so on the tab for a moment, and remember when the STRIKE of an assessment fell (the target's cross).</summary>
        private void Phases()
        {
            if (d.Current != lastCurrent)
            {
                if (primed && d.Current > lastCurrent && lastCurrent >= 0 && d.Phases != null && lastCurrent < d.Phases.Length)
                {
                    doneAt = now;
                    doneWord = d.Phases[lastCurrent].Title + " DONE";
                    if (d.Phases[lastCurrent].Title == "STRIKE") strikeDoneAt = now;
                }
                lastCurrent = d.Current;
            }
        }

        private bool StrikeDone => d.Phases != null && d.PhaseCount > 1 && d.Phases[0].Status == ObjectivePhaseStatus.Done && d.Phases[0].Title == "STRIKE";

        // ------------------------------------------------------------------------------------------------ headline

        /// <summary>The tab's figure: an air-defence envelope, what keeps the hold from counting, a finished phase, the hold's progress, else the way to the zone.</summary>
        private void ReadHeadline(AvUnits units)
        {
            ObjectiveLine line = ObjectiveMath.Line(d);
            float edge = Math.Max(0f, ObjectiveMath.Distance(d.OwnX, d.OwnZ, d.X, d.Z) - d.Radius);
            bool inZone = edge <= 0f;
            string text;
            int key;
            ScopeTone tone = Hi;
            bool flash = false;
            if (inSam) { key = 1; text = "SAM ENV"; tone = Amber; flash = now < samAt + EnvelopeFlash; }
            else if (line == ObjectiveLine.NoSightline) { key = 2; text = "NO LOS"; tone = Amber; flash = true; }
            else if (line == ObjectiveLine.TooLow) { key = 3; text = "TOO LOW"; tone = Amber; flash = true; }
            else if (line == ObjectiveLine.NoTrack) { key = 4; text = "NO TRACK"; tone = Amber; flash = true; }
            else if (line == ObjectiveLine.ContactLost) { key = 5; text = "NO CONTACT"; tone = Amber; flash = true; }
            else if (inGun && !inZone) { key = 6; text = "GUN ENV"; tone = Amber; flash = true; }
            else if (now < doneAt + DoneSeconds) { key = 7; text = doneWord; }
            else if (line == ObjectiveLine.Progress)
            {
                int pct = (int)Math.Round(ObjectiveMath.Progress(d) * 100f);
                key = 100000 + pct * 10 + d.Current;
                text = key == headlineKey ? headlineText : ScopeStr.Cat(d.Phases[d.Current].Title + " ", pct, "%").ToString();
            }
            else
            {
                int shown = (int)(edge / 100f);
                key = 200000 + shown * 2 + (units == AvUnits.Imperial ? 1 : 0);
                text = key == headlineKey ? headlineText : ScopeStr.Distance("", edge, units, 10).ToString();
            }
            headlineText = text;
            headlineKey = key;
            headlineTone = tone;
            headlineFlash = flash;
        }

        public override ScopeHeadline Headline() => live ? new ScopeHeadline { Text = headlineText, Tone = headlineTone, Flash = headlineFlash } : default;

        // ------------------------------------------------------------------------------------------------ gauges

        /// <summary>The phase, the nearest radar SAM as a needle and the height above ground against the height that masks it; the AUTOPILOT gauge is the registered one.</summary>
        public override int Gauges(GaugeSpec[] into)
        {
            if (!live) return 0;
            int n = 0;
            ObjectivePhase current = d.Phases != null && d.Current >= 0 && d.Current < d.PhaseCount && d.Current < d.Phases.Length ? d.Phases[d.Current] : null;
            if (current != null)
            {
                float prog = ObjectiveMath.Progress(d);
                int pct = (int)Math.Round(prog * 100f), subKeyNow = prog > 0f ? pct : -1 - d.Current * 100 - d.PhaseCount;
                if (subKeyNow != phaseSubKey)
                {
                    phaseSubKey = subKeyNow;
                    phaseSub = prog > 0f ? ScopeStr.Pct(pct) : ScopeStr.Cat("PH ", d.Current + 1, "/", d.PhaseCount, null).ToString();
                }
                into[n++] = new GaugeSpec { Id = "phase", Label = "PHASE", Kind = GaugeKind.Ring, Value = prog, Inner = ScopeStr.Int(d.Current + 1), Text = current.Title, Sub = phaseSub, Tone = Ink };
            }

            int radar = ObjectiveMath.NearestRadar(d.Rings, d.RingCount, d.OwnX, d.OwnZ);
            SamExposure e = radar >= 0 ? ObjectiveMath.Expose(d.Rings[radar], d.OwnX, d.OwnZ, d.OwnAgl) : default;
            if (radar >= 0)
            {
                int key = (int)(e.DistanceM / 100f);
                if (key != samSubKey) { samSubKey = key; samSub = ScopeStr.Distance("", e.DistanceM, units, 7).ToString(); }
                into[n++] = new GaugeSpec { Id = "sam", Label = "SAM", Kind = GaugeKind.Needle, Angle = ObjectiveMath.Relative(e.BearingDeg, d.OwnHeading), Text = ObjectiveMath.Kind(d.Rings[radar].Kind), Sub = samSub, Tone = e.Inside ? Amber : Ink };
            }

            bool exposed = radar >= 0 && e.Inside && d.OwnAgl > e.MaskM;
            int altKey = (int)Math.Round(d.OwnAgl / 10f) * 10 + (units == AvUnits.Imperial ? 1 : 0);
            if (altKey != aglKey) { aglKey = altKey; aglText = Reading(d.OwnAgl, units); }
            string sub = null;
            int tickCount = 0;
            if (radar >= 0 && e.MaskM > 0f)
            {
                int maskKey = (int)Math.Round(e.MaskM / 10f) * 10 + (units == AvUnits.Imperial ? 1 : 0);
                if (maskKey != aglSubKey) { aglSubKey = maskKey; aglSub = "<" + Reading(e.MaskM, units); }
                sub = aglSub;
                aglTicks[0] = Math.Min(0.99f, e.MaskM / AglFull);
                tickCount = 1;
            }
            into[n++] = new GaugeSpec { Id = "agl", Label = "AGL", Kind = GaugeKind.Bar, Value = d.OwnAgl / AglFull, Text = aglText, Sub = sub, Tone = exposed ? Amber : Ink, Ticks = aglTicks, TickCount = tickCount, TickTone = exposed ? Amber : Dim };
            return n;
        }

        /// <summary>The AGL bar's whole scale in metres.</summary>
        private const float AglFull = 2000f;

        private string Reading(float metres, AvUnits units)
        {
            int len = AvUnitTable.AltitudeReading(scratch, 0, metres, units);
            return new string(scratch, 0, len).ToUpperInvariant();
        }
    }
}
