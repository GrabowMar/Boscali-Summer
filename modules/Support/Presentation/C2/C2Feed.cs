using System;
using BoscaliSummer.Modules.Support.Domain.C2;
using BoscaliSummer.Modules.Support.Domain.Cyber;
using BoscaliSummer.Modules.Support.Domain.Ops;
using BoscaliSummer.Modules.Support.Domain.Sof;
using BoscaliSummer.Modules.Support.Domain.Space;
using BoscaliSummer.Modules.Support.Runtime;
using NuclearOption.Networking;
using UnityEngine;

namespace BoscaliSummer.Modules.Support.Presentation.C2
{
    /// <summary>
    /// The one host console of an OPS client. It owns a <see cref="C2Console"/> and fills it with real events only: the CALL
    /// controller's words (arm, refusal, receipt), credit changes, SPACE family / uplink changes, TASKED post and intent
    /// notices, and cockpit threat edges. It never invents a line; an empty console reads <c>&gt; link idle</c> in the view.
    /// Cleared on scene reset and when the local faction changes.
    /// </summary>
    internal sealed class C2Feed
    {
        private const float CreditGapSeconds = 2f, TickSeconds = 0.25f, ThreatGapSeconds = 5f, DeltaShownSeconds = 30f;

        private readonly C2Console console = new C2Console();
        private readonly SpaceNoticeTracker notices = new SpaceNoticeTracker();
        private SupportManager manager;
        private CallsController calls;
        private object faction;
        private bool creditPrimed, spacePrimed;
        private int cyberSeq = -1, sofSeq = -1, opsSeq = -1, opsPingSeq = -1, watchSeq = -1;
        private float lastCredit, pendingDelta, nextCredit, nextTick, nextThreatLine, lastDeltaAt;
        private int lastDelta;
        private byte lastLive, lastTotal;
        private SpaceFamilyState lastFamily;
        private string lastThreat = "";

        public C2Console Console => console;

        /// <summary>The current cockpit warning words (RWR SPIKE, MISSILE WARNING ...), empty when none: the header's red slab.</summary>
        public string Alert { get; private set; } = "";

        /// <summary>The last credit change, in whole CR (signed); 0 when none since the last clear or when it is older than 30 s (the page then shows the balance).</summary>
        public int LastDelta => lastDelta != 0 && Time.unscaledTime - lastDeltaAt <= DeltaShownSeconds ? lastDelta : 0;

        /// <summary>Counts local faction changes (A to B, not a scene reset): the panel returns to the CAP tab when it moves.</summary>
        public int FactionEpoch { get; private set; }

        public void Attach(SupportManager supportManager, CallsController callsController)
        {
            Detach();
            manager = supportManager;
            calls = callsController;
            if (calls != null) calls.Spoke += OnSpoke;
        }

        public void Detach()
        {
            if (calls != null) calls.Spoke -= OnSpoke;
            calls = null;
            manager = null;
        }

        /// <summary>Scene reset or faction change: drop every line and re-prime every edge detector.</summary>
        public void Clear()
        {
            console.Clear();
            notices.Reset();
            creditPrimed = spacePrimed = false;
            cyberSeq = -1; sofSeq = -1; opsSeq = opsPingSeq = watchSeq = -1;
            pendingDelta = 0f;
            nextCredit = nextTick = 0f;
            lastThreat = "";
            nextThreatLine = 0f;
            Alert = "";
            lastDelta = 0;
            faction = null;
        }

        public void Add(string text, C2Tone tone) => console.Add(text, tone, Time.unscaledTime);

        private void OnSpoke(string words) => console.Add(words, C2Cap.LineTone(words), Time.unscaledTime);

        /// <summary>
        /// Polls the sources that have no event. Cheap; throttled to four times a second. The cockpit threat probe is read only while
        /// <paramref name="watching"/> (the OPS page or the station is on screen): a pilot who never opens OPS pays nothing.
        /// </summary>
        public void Tick(bool watching)
        {
            if (manager == null) return;
            float wall = Time.unscaledTime;
            if (wall < nextTick) return;
            nextTick = wall + TickSeconds;

            // A null read (no player for a moment) keeps the last faction, so A, null, B is still a change.
            object hq = Faction();
            if (hq != null)
            {
                if (faction != null && hq != faction) { Clear(); FactionEpoch++; }
                faction = hq;
            }

            Credit(wall);
            Space();
            Cyber(wall);
            Sof(wall);
            Ops(wall);
            if (watching) Threat(wall);
        }

        private static object Faction() =>
            GameManager.GetLocalPlayer<Player>(out Player player) && player != null ? (object)player.HQ : null;

        private void Credit(float wall)
        {
            float credit = manager.LocalCredit;
            if (!creditPrimed) { creditPrimed = true; lastCredit = credit; return; }
            pendingDelta += credit - lastCredit;
            lastCredit = credit;
            int whole = Mathf.RoundToInt(pendingDelta);
            if (whole == 0 || wall < nextCredit) return;
            nextCredit = wall + CreditGapSeconds;
            pendingDelta = 0f;
            lastDelta = whole;
            lastDeltaAt = wall;
            console.Add("LEDGER " + C2Cap.Delta(whole) + " · " + Mathf.RoundToInt(credit) + " CR", C2Tone.Info, wall);
        }

        private void Space()
        {
            SpaceFeedMirror mirror = manager.SpaceMirror;
            SpaceFeedState state = mirror.State;
            if (!mirror.Known || !state.Active) { spacePrimed = false; return; }
            if (!spacePrimed)
            {
                spacePrimed = true;
                lastLive = state.UplinksLive; lastTotal = state.UplinksTotal; lastFamily = state.Family;
            }
            else if (lastLive != state.UplinksLive || lastTotal != state.UplinksTotal || lastFamily != state.Family)
            {
                lastLive = state.UplinksLive; lastTotal = state.UplinksTotal; lastFamily = state.Family;
                console.Add("UPLINKS " + state.UplinksLive + "/" + state.UplinksTotal + " · SPACE " + SpaceWord(state.Family),
                    state.Family == SpaceFamilyState.Normal ? C2Tone.Info : C2Tone.Warn, Time.unscaledTime);
            }
            SpaceNotice notice = notices.Observe(true, state, SupportManager.MissionNow(), false);
            if (notice.Kind != SpaceNoticeKind.None) console.Add(notice.Text, C2Tone.Info, Time.unscaledTime);
        }

        /// <summary>One console line per new CYBER event of the faction (real intrusion events only); the first sight of a mirror is silent.</summary>
        private void Cyber(float wall)
        {
            CyberMirror mirror = manager.CyberMirror;
            if (!mirror.Known) { cyberSeq = -1; return; }
            int newest = 0;
            foreach (CyberEventRow e in mirror.State.Events) newest = Mathf.Max(newest, e.Seq);
            if (cyberSeq < 0) { cyberSeq = newest; return; }
            foreach (CyberEventRow e in mirror.State.Events)
            {
                if (e.Seq <= cyberSeq) continue;
                bool bad = e.Kind == CyberEventKind.Traced || (e.Kind == CyberEventKind.Released && e.Reason != IntrusionEnd.Burned && e.Reason != IntrusionEnd.Dropped);
                console.Add("CYBER · " + CyberNetWords.EventLine(e, SupportManager.MissionNow()) + (e.Own ? "" : " · TEAM"), bad ? C2Tone.Warn : C2Tone.Info, wall);
            }
            cyberSeq = Mathf.Max(cyberSeq, newest);
        }

        /// <summary>One console line per new SOF event of the faction (real team events only); the first sight of a mirror is silent.</summary>
        private void Sof(float wall)
        {
            SofMirror mirror = manager.SofMirror;
            if (!mirror.Known) { sofSeq = -1; return; }
            int newest = 0;
            foreach (SofEventRow e in mirror.State.Events) newest = Mathf.Max(newest, e.Seq);
            if (sofSeq < 0) { sofSeq = newest; return; }
            foreach (SofEventRow e in mirror.State.Events)
            {
                if (e.Seq <= sofSeq) continue;
                bool bad = e.Kind == SofEventKind.Lost || e.Kind == SofEventKind.Pinned || e.Kind == SofEventKind.Failed || e.Kind == SofEventKind.Retaken;
                console.Add("SOF · " + SofPageWords.EventLine(e), bad ? C2Tone.Warn : C2Tone.Info, wall);
            }
            sofSeq = Mathf.Max(sofSeq, newest);
        }

        /// <summary>One console line per new OPERATIONS event of the faction and per new enemy ping (real events only); the first sight of a mirror is silent.</summary>
        private void Ops(float wall)
        {
            OpsMirror mirror = manager.OpsMirror;
            if (!mirror.Known) { opsSeq = opsPingSeq = watchSeq = -1; return; }
            int newest = 0, newestPing = 0, newestWatch = 0;
            foreach (OpsEventRow e in mirror.State.Events) newest = Mathf.Max(newest, e.Seq);
            foreach (OpsPingRow p in mirror.State.Pings) newestPing = Mathf.Max(newestPing, p.Seq);
            foreach (WatchLogRow l in mirror.State.Log) newestWatch = Mathf.Max(newestWatch, l.Seq);
            // WATCH OFFICER OVERLORD's own actions, each with the reason it gave (the faction's own console only: the enemy learns of them through traces, pings and real sensing).
            // They ride the state even when OPERATIONS is off. The first sight is silent; a log that restarted (its newest row is older than what was printed: a scene reset on the host)
            // catches up by printing what is there.
            if (watchSeq < 0) watchSeq = newestWatch;
            else
            {
                if (newestWatch < watchSeq) watchSeq = 0;
                foreach (WatchLogRow l in mirror.State.Log)
                    if (l.Seq > watchSeq) console.Add(WatchWords.Domain(l.Domain) + " · " + WatchWords.Line(l), C2Tone.Info, wall);
                watchSeq = Mathf.Max(watchSeq, newestWatch);
            }
            if (!mirror.State.Active) { opsSeq = opsPingSeq = -1; return; }
            if (opsSeq < 0) { opsSeq = newest; opsPingSeq = newestPing; return; }
            foreach (OpsEventRow e in mirror.State.Events)
            {
                if (e.Seq <= opsSeq) continue;
                bool bad = e.Kind == OpEventKind.Broken || e.Kind == OpEventKind.CounterTrace || e.Kind == OpEventKind.Stalled;
                console.Add("OPERATION · " + OpsWords.Event(e.Kind, e.Op), bad ? C2Tone.Warn : C2Tone.Info, wall);
            }
            foreach (OpsPingRow p in mirror.State.Pings)
            {
                if (p.Seq <= opsPingSeq) continue;
                console.Add("OPERATION · " + OpsWords.Ping(p.Kind, p.Phase, p.Name, p.Detail), p.Phase == OpPingPhase.Half ? C2Tone.Warn : C2Tone.Danger, wall);
            }
            opsSeq = Mathf.Max(opsSeq, newest);
            opsPingSeq = Mathf.Max(opsPingSeq, newestPing);
        }

        private void Threat(float wall)
        {
            if (!SpaceCockpitThreatProbe.TryRead(out CockpitThreatSnapshot snap)) snap = default;
            string strip = SpaceFeedRules.ThreatStrip(snap);
            Alert = strip; // the header slab keeps the full strip, distances included
            string classes = C2Words.ThreatClasses(strip);
            if (classes == lastThreat || wall < nextThreatLine) return; // edge only: a line when the set of classes changes, at most one per 5 s
            lastThreat = classes;
            nextThreatLine = wall + ThreatGapSeconds;
            console.Add(classes.Length > 0 ? "WARNING · " + classes : "WARNING CLEARED", classes.Length > 0 ? C2Tone.Danger : C2Tone.Info, wall);
        }

        /// <summary>The family as the session line and the console say it.</summary>
        public static string SpaceWord(SpaceFamilyState family) =>
            family == SpaceFamilyState.Normal ? "NORMAL" : family == SpaceFamilyState.Degraded ? "DEGRADED" : "OFFLINE";
    }
}
