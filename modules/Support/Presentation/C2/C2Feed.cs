using System;
using BoscaliSummer.Modules.Support.Domain.C2;
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
        private const float CreditGapSeconds = 2f, TickSeconds = 0.25f;

        private readonly C2Console console = new C2Console();
        private readonly SpaceNoticeTracker notices = new SpaceNoticeTracker();
        private SupportManager manager;
        private CallsController calls;
        private object faction;
        private bool creditPrimed, spacePrimed;
        private float lastCredit, pendingDelta, nextCredit, nextTick;
        private byte lastLive, lastTotal;
        private SpaceFamilyState lastFamily;
        private string lastThreat = "";

        public C2Console Console => console;

        /// <summary>The current cockpit warning words (RWR SPIKE, MISSILE WARNING ...), empty when none: the header's red slab.</summary>
        public string Alert { get; private set; } = "";

        /// <summary>The last credit change, in whole CR (signed), 0 when none since the last clear.</summary>
        public int LastDelta { get; private set; }

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
            pendingDelta = 0f;
            nextCredit = nextTick = 0f;
            lastThreat = "";
            Alert = "";
            LastDelta = 0;
            faction = null;
        }

        public void Add(string text, C2Tone tone) => console.Add(text, tone, Time.unscaledTime);

        private void OnSpoke(string words) => console.Add(words, C2Cap.LineTone(words), Time.unscaledTime);

        /// <summary>Polls the sources that have no event. Cheap; throttled to four times a second.</summary>
        public void Tick()
        {
            if (manager == null) return;
            float wall = Time.unscaledTime;
            if (wall < nextTick) return;
            nextTick = wall + TickSeconds;

            object hq = Faction();
            if (faction != null && hq != faction) Clear();
            faction = hq;

            Credit(wall);
            Space();
            Threat();
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
            LastDelta = whole;
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

        private void Threat()
        {
            if (!SpaceCockpitThreatProbe.TryRead(out CockpitThreatSnapshot snap)) snap = default;
            string strip = SpaceFeedRules.ThreatStrip(snap);
            if (strip == lastThreat) return;
            Alert = strip;
            console.Add(strip.Length > 0 ? "WARNING · " + strip : "WARNING CLEARED", strip.Length > 0 ? C2Tone.Danger : C2Tone.Info, Time.unscaledTime);
            lastThreat = strip;
        }

        /// <summary>The family as the session line and the console say it.</summary>
        public static string SpaceWord(SpaceFamilyState family) =>
            family == SpaceFamilyState.Normal ? "NORMAL" : family == SpaceFamilyState.Degraded ? "DEGRADED" : "DARK";
    }
}
