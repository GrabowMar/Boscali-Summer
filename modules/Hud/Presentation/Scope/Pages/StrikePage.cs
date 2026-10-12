using System;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Modules.Hud.Domain;
using NOAvionics;
using static BoscaliSummer.Core.Contracts.ScopeTone;

namespace BoscaliSummer.Modules.Hud.Presentation
{
    /// <summary>
    /// STRIKE / OPS (prototype: <c>pages/strike.js</c>). The pilot's own strikes stacked as tracks (the platform, the payload travelling its curve to the
    /// target and a countdown: KINETIC ROD, PRSM, CRUISE, EMP, DECOY), the SAT window of his own RECON PASS or SAT CAMERA, his own CYBER intrusion
    /// (the node chain with the hop ring and the trace burn) and the OPS perk strip under the body. Fed by <see cref="StrikeScope"/> (Support's
    /// provider); nothing is drawn that the feed does not say. It is live while a strike is in the air or just landed, a window is open, an intrusion
    /// is up or a perk is armed. A friendly kinetic strike about to land near the pilot is announced once on the ticker (DANGER CLOSE).
    /// </summary>
    internal sealed partial class StrikePage : ScopePageBase
    {
        private const int Remembered = 8;

        private struct Memo
        {
            public int Id;
            public bool Close, Flying, Landed;
        }

        private StrikeScope d;
        private bool live, strip, primed, danger;
        private float now;
        private readonly Memo[] memos = new Memo[Remembered];
        private int memoNext;
        private float lastTrace, heldAt = float.NegativeInfinity;
        private int lastHeld;
        private readonly float[] allocTicks = new float[4];
        private string headlineText, rodSub, traceSub;
        private int headlineKey = -1, rodSubCost = -1, traceSubKey = -1;
        private ScopeTone headlineTone;
        private bool headlineFlash;

        public override ScopePage Id => ScopePage.Strike;

        public override string Title => "STRIKE";

        public override char Code => 'K';

        public override bool Fast => live;

        public override bool HasStrip => strip;

        // ------------------------------------------------------------------------------------------------ poll

        public override bool Poll(in ScopeEnv env)
        {
            now = env.Now;
            if (!ScopeFeeds.TryRead(out d)) { Forget(); return false; }

            live = d.SatOpen || d.CyberLive;
            for (int i = 0; i < d.TrackCount; i++) live |= StrikeMath.Live(d.Tracks[i]);
            for (int i = 0; i < d.PerkCount; i++) live |= d.Perks[i].State == PerkPhase.Armed;
            if (!live) { Forget(); return false; }
            strip = d.PerkCount > 0;
            danger = false;
            for (int i = 0; i < d.TrackCount; i++) danger |= StrikeMath.Close(d.Tracks[i]);

            Edges(env);
            ReadHeadline();
            primed = true;
            return true;
        }

        /// <summary>A friendly kinetic strike about to land close: the MSL lamp reads DNGR, amber and flashing (the prototype's strike page).</summary>
        public override bool Lamp(string legend, out LampState state, out bool flash, out string text)
        {
            state = LampState.Caution;
            flash = true;
            text = "DNGR";
            if (live && danger && legend == "MSL") return true;
            state = LampState.Dark;
            flash = false;
            text = null;
            return false;
        }

        private void Forget()
        {
            live = strip = primed = danger = false;
            Array.Clear(memos, 0, memos.Length);
            lastTrace = 0f;
            lastHeld = 0;
        }

        private ref Memo MemoOf(int id)
        {
            for (int i = 0; i < Remembered; i++) if (memos[i].Id == id) return ref memos[i];
            int at = memoNext;
            memoNext = (memoNext + 1) % Remembered;
            memos[at] = new Memo { Id = id };
            return ref memos[at];
        }

        /// <summary>Events once: a friendly strike about to land close to the pilot, an impact, the trace crossing its marks, a node arriving.</summary>
        private void Edges(in ScopeEnv env)
        {
            for (int i = 0; i < d.TrackCount; i++)
            {
                StrikeTrack t = d.Tracks[i];
                ref Memo m = ref MemoOf(t.Id);
                if (StrikeMath.Close(t) && t.Eta <= 15f && !m.Close)
                {
                    m.Close = true;
                    env.Notice?.Invoke("ops", HudTone.Warning, "DANGER CLOSE - " + OpsScopeWords.Short(t.Kind) + " IMPACT T-" + (int)Math.Ceiling(t.Eta) + " S",
                        ScopeStr.Distance("", t.RangeM, env.Units, 12).ToString());
                }
                if (StrikeMath.InFlight(t)) m.Flying = true;
                else if (m.Flying && !m.Landed)
                {
                    m.Landed = true;
                    env.Notice?.Invoke("ops", HudTone.Info, OpsScopeWords.Short(t.Kind) + " IMPACT", ScopeStr.Distance("", t.RangeM, env.Units, 12).ToString());
                }
            }

            if (d.CyberLive)
            {
                if (primed && lastTrace < StrikeMath.TraceCaution && d.Trace >= StrikeMath.TraceCaution && d.Trace < StrikeMath.TraceWarning)
                    env.Notice?.Invoke("cyber", HudTone.Caution, "TRACE " + (int)d.Trace + "% - HOLD OR BURN AT 100", null);
                if (primed && lastTrace < StrikeMath.TraceWarning && d.Trace >= StrikeMath.TraceWarning)
                    env.Notice?.Invoke("cyber", HudTone.Warning, "TRACE " + (int)d.Trace + "% - DROP OR BE TRACED", null);
                if (primed && d.ChainHeld > lastHeld) heldAt = now;
                lastTrace = d.Trace;
                lastHeld = d.ChainHeld;
            }
            else { lastTrace = 0f; lastHeld = 0; }
        }

        // ------------------------------------------------------------------------------------------------ headline

        /// <summary>The one figure at the tab, by what matters most now: the soonest strike, an impact, an effect, the SAT window, the trace, an armed perk.</summary>
        private void ReadHeadline()
        {
            int soon = StrikeMath.Soonest(d.Tracks, d.TrackCount), key;
            ScopeTone tone = Hi;
            bool flash = false;
            string text = null;
            int impact = -1, effect = -1;
            for (int i = 0; i < d.TrackCount; i++)
            {
                StrikePhase ph = StrikeMath.Phase(d.Tracks[i]);
                if (ph == StrikePhase.Impact && impact < 0) impact = i;
                if (ph == StrikePhase.Effect && effect < 0) effect = i;
            }

            if (soon >= 0)
            {
                StrikeTrack t = d.Tracks[soon];
                int n = (int)Math.Ceiling(t.Eta);
                key = 2000000 + (int)t.Kind * 1000 + n;
                if (key != headlineKey) text = ScopeStr.Cat(OpsScopeWords.Short(t.Kind) + " ", n, " S").ToString();
                bool close = StrikeMath.Close(t);
                tone = close ? Amber : Hi;
                flash = close && n <= 4;
            }
            else if (impact >= 0) { key = 3000000; text = "IMPACT"; }
            else if (effect >= 0)
            {
                StrikeTrack t = d.Tracks[effect];
                int n = (int)Math.Ceiling(Math.Max(0f, t.Linger + t.Eta));
                key = 4000000 + (int)t.Kind * 1000 + n;
                if (key != headlineKey) text = ScopeStr.Cat(OpsScopeWords.Short(t.Kind) + " ", n, " S").ToString();
            }
            else if (d.SatOpen)
            {
                int n = (int)Math.Ceiling(d.SatLeft);
                key = 5000000 + n;
                if (key != headlineKey) text = ScopeStr.Cat("SAT ", n, " S").ToString();
                tone = StrikeMath.SatLate(d.SatLeft) ? Amber : Hi;
            }
            else if (d.CyberLive)
            {
                int trace = (int)d.Trace;
                tone = StrikeMath.TraceTone(d.Trace);
                if (tone == Ink) tone = Hi;
                if (tone != Hi) { key = 6000000 + trace; if (key != headlineKey) text = ScopeStr.Cat("TRACE ", trace, "%").ToString(); }
                else if (d.CyberHopping)
                {
                    int n = (int)Math.Ceiling(d.HopLeft);
                    key = 7000000 + n;
                    if (key != headlineKey) text = ScopeStr.Cat("HOP ", n, " S").ToString();
                }
                else { key = 8000000; text = "HOLDING"; }
            }
            else
            {
                key = 9000000;
                text = "ARMED";
                for (int i = 0; i < d.PerkCount; i++) if (d.Perks[i].State == PerkPhase.Armed) { key += i + 1; text = key == headlineKey ? null : d.Perks[i].Name + " ARMED"; break; }
            }

            headlineText = text ?? headlineText;
            headlineKey = key;
            headlineTone = tone;
            headlineFlash = flash;
        }

        public override ScopeHeadline Headline() => live ? new ScopeHeadline { Text = headlineText, Tone = headlineTone, Flash = headlineFlash } : default;

        // ------------------------------------------------------------------------------------------------ gauges

        /// <summary>ALLOC with the strikes' prices as ticks, the kinetic bird's fuel, the trace burn and the own SOF team's mission clock.</summary>
        public override int Gauges(GaugeSpec[] into)
        {
            if (!live) return 0;
            int n = 0;
            if (d.HasAlloc)
            {
                if (d.RodCost != rodSubCost) { rodSubCost = d.RodCost; rodSub = ScopeStr.Cat("ROD ", d.RodCost).ToString(); }
                int ticks = Math.Min(d.AllocCostCount, allocTicks.Length);
                for (int i = 0; i < ticks; i++) allocTicks[i] = d.AllocCosts[i] / StrikeMath.AllocFull;
                into[n++] = new GaugeSpec
                {
                    Id = "alloc", Label = "ALLOC", Kind = GaugeKind.Bar, Value = d.Alloc / StrikeMath.AllocFull, Text = ScopeStr.Int((int)d.Alloc), Sub = d.RodCost > 0 ? rodSub : null,
                    Tone = d.RodCost <= 0 || d.Alloc >= d.RodCost ? Ink : Dim, Ticks = allocTicks, TickCount = ticks, TickTone = Dim,
                };
            }
            if (d.HasBird)
                into[n++] = new GaugeSpec { Id = "bird", Label = "BIRD", Kind = GaugeKind.Arc, Value = d.BirdFuel, Text = ScopeStr.Pct((int)Math.Round(d.BirdFuel * 100f)), Sub = "KINETIC", Tone = d.BirdFuel <= 0.25f ? Amber : Ink };
            if (d.CyberLive)
            {
                int slot = d.ChainHeld + (d.CyberHopping ? 1 : 0);
                if (slot != traceSubKey) { traceSubKey = slot; traceSub = ScopeStr.Cat("HOP ", slot, "/", 5, null).ToString(); }
                into[n++] = new GaugeSpec { Id = "trace", Label = "TRACE", Kind = GaugeKind.Arc, Value = d.Trace / 100f, Text = ScopeStr.Pct((int)d.Trace), Sub = traceSub, Tone = StrikeMath.TraceTone(d.Trace) };
            }
            if (d.HasSof)
                into[n++] = new GaugeSpec
                {
                    Id = "sof", Label = "SOF", Kind = GaugeKind.Ring, Value = d.SofTotal > 0f ? 1f - d.SofLeft / d.SofTotal : 0f, Inner = ScopeStr.Int((int)Math.Ceiling(d.SofLeft)), Text = d.SofName, Sub = d.SofMission, Tone = Ink,
                };
            return n;
        }
    }
}
