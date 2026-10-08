using System;
using System.Collections.Generic;
using BoscaliSummer.Core.Game;
using BoscaliSummer.Core.Lifecycle;
using BoscaliSummer.Modules.Support.Domain.Calls;
using BoscaliSummer.Modules.Support.Domain.Fronts;
using BoscaliSummer.Modules.Support.Domain.Ops;
using BoscaliSummer.Modules.Support.Domain.Sof;
using BoscaliSummer.Modules.Support.Domain.Space;
using BoscaliSummer.Modules.Support.Runtime;
using NOAvionics;
using NuclearOption.Networking;
using UnityEngine;

namespace BoscaliSummer.Modules.Support.Presentation.Fronts
{
    /// <summary>
    /// The one owner of the three OPS front windows. It builds the <see cref="FrontRoomView"/> from the faction mirrors (through the pure
    /// <see cref="FrontViews"/>), paints it into one <see cref="OpsFrontWindow"/> while it is open, and turns every press into a request on the
    /// <see cref="SupportManager"/> client verbs (or the SPACE feed). It decides nothing: the host judges, charges and replies; the reply is worded here
    /// with <see cref="FrontWords"/> (front verbs) or arrives through <see cref="CallsController"/> (CYBER and SOF verbs).
    ///
    /// <para>The OPS, CYBER and SOF faction feeds are pulled only while the window is open (the front feed is always on: perk readiness needs it).</para>
    /// </summary>
    internal sealed class FrontController : MonoBehaviour, ISceneService, IFrontActions
    {
        private const float Refresh = 0.2f, PressGap = 0.35f, WordsHold = 8f;
        private const string Resting = "Keyboard and joystick stay live; the mouse drives this window. It closes with Esc or after a minute without input.";

        private SupportManager manager;
        private CallsController calls;
        private SpaceFeedController feed;

        private OpsFrontWindow window;
        private readonly FrontRoomView view = new FrontRoomView();
        private Front current = Front.Space;
        private bool open, requireOwnship;
        private float nextRefresh, nextPress, wordsAt = -100f;
        private int lastFaction, selectedNode, selectedTeam = -1, selectedTarget;
        private int pendingRequest;
        private Front pendingFront;
        private string words = "";
        private AvState wordsTone = AvState.Inert;
        private readonly bool[] hasBird = new bool[3];

        public bool IsOpen => open && window != null && window.IsOpen;
        public Front Current => current;
        /// <summary>The last front verdict in words (the footer shows it for a few seconds).</summary>
        public string LastWords => words;
        public float LastWordsAt => wordsAt;
        public AvState LastWordsTone => wordsTone;
        internal FrontRoomView View => view;

        public void Configure(SupportManager supportManager, CallsController callsController, SpaceFeedController feedController)
        {
            manager = supportManager;
            calls = callsController;
            feed = feedController;
            if (manager != null) manager.SpaceReplied += OnReply;
        }

        private void OnDestroy()
        {
            if (manager != null) manager.SpaceReplied -= OnReply;
            Shut();
            WantFeeds(false);
        }

        public void ResetForScene()
        {
            Shut();
            selectedNode = 0; selectedTeam = -1; selectedTarget = 0;
            pendingRequest = 0;
            words = "";
            wordsAt = -100f;
            lastFaction = 0;
            WantFeeds(false);
        }

        // ---- Open / close -----------------------------------------------------------------------------------------------

        /// <summary>Opens the window on <paramref name="front"/>, or switches the open window to it. False when no operator exists or the window could not open.</summary>
        public bool Open(Front front)
        {
            if (manager == null) return false;
            if (IsOpen) { SelectFront(front); return true; }
            if (!SpaceCockpitThreatProbe.TryRead(out CockpitThreatSnapshot snap) || !snap.ValidOperator) { Say("NEGATIVE: NO OPERATOR · SPAWN OR TAKE A SEAT", AvState.Danger); return false; }
            requireOwnship = snap.ValidOwnship; // the entry context: a ground operator never needs an aircraft, an airborne one always does
            if (window == null)
            {
                window = OpsFrontWindow.Create(transform, this);
                if (window == null) { Say("NEGATIVE: WINDOW UNAVAILABLE", AvState.Danger); return false; }
                window.Closed += OnClosed;
            }
            current = front;
            selectedNode = 0; selectedTeam = -1; selectedTarget = 0;
            lastFaction = CurrentFaction();
            WantFeeds(true);
            feed?.SetVisible(front == Front.Space);
            Build();
            if (!window.Open(view)) { feed?.SetVisible(false); WantFeeds(false); Say("NEGATIVE: WINDOW COULD NOT OPEN", AvState.Danger); return false; }
            open = true;
            badSince = -1f;
            nextRefresh = Time.unscaledTime + Refresh;
            return true;
        }

        public void Close() => window?.Close();

        private void Shut()
        {
            if (window != null && window.IsOpen) window.Close();
            open = false;
            feed?.SetVisible(false);
        }

        private void OnClosed()
        {
            open = false;
            feed?.SetVisible(false);
        }

        // ---- Frame ------------------------------------------------------------------------------------------------------

        private const float BadGraceSeconds = 4f;
        private float badSince = -1f;

        private void Update()
        {
            if (manager == null) return;
            WantFeeds(open);
            if (!open) return;
            if (window == null || !window.IsOpen) { OnClosed(); return; }
            try
            {
                // A probe or faction read can blink for a frame (respawn handover, ownship swap): only a stretch of bad reads is a real death, despawn or faction change.
                bool bad = !SpaceCockpitThreatProbe.TryRead(out CockpitThreatSnapshot snap) || !snap.ValidOperator || (requireOwnship && !snap.ValidOwnship) || CurrentFaction() != lastFaction;
                if (!bad) badSince = -1f;
                else
                {
                    if (badSince < 0f) badSince = Time.unscaledTime;
                    if (Time.unscaledTime - badSince >= BadGraceSeconds) { badSince = -1f; Close(); return; }
                }
                if (Time.unscaledTime < nextRefresh) return;
                nextRefresh = Time.unscaledTime + Refresh;
                Build();
                window.Paint(view);
                Footer();
            }
            catch (Exception e)
            {
                Plugin.Logger?.LogError("OPS front window refresh failed, closing: " + e);
                Close();
            }
        }

        private void WantFeeds(bool on)
        {
            if (manager == null) return;
            manager.OpsFeed.Want(on);
            manager.CyberFeed.Want(on && (current == Front.Cyber || current == Front.Sof));
            manager.SofFeed.Want(on && (current == Front.Sof || current == Front.Cyber));
        }

        private int CurrentFaction()
        {
            if (GameManager.GetLocalPlayer(out Player player) && player != null && player.HQ != null) return manager.FactionKeyOf(player.HQ);
            return 0;
        }

        // ---- The view ---------------------------------------------------------------------------------------------------

        private void Build()
        {
            float now = SupportManager.MissionNow();
            FrontInputs c = Inputs(now);
            FrontMirror fm = manager.FrontMirror;
            FrontViews.Common(view, current, fm.State, fm.Known, c);
            switch (current)
            {
                case Front.Space:
                    FrontViews.Orbit(view, c);
                    if (feed != null) view.Feed = feed.View;
                    break;
                case Front.Cyber:
                    FrontViews.Network(view.Network, manager.CyberMirror.State, manager.CyberMirror.Known, c.Frame, selectedNode, now);
                    selectedNode = view.Network.SelectedId > 0 ? view.Network.SelectedId : 0;
                    break;
                default:
                    FrontViews.Shadow(view.Shadow, manager.SofMirror.State, manager.SofMirror.Known, manager.CyberMirror.Known ? manager.CyberMirror.State : null, c.Frame, selectedTeam, selectedTarget, now);
                    selectedTeam = view.Shadow.SelectedSlot;
                    selectedTarget = view.Shadow.SelectedTargetId;
                    break;
            }
        }

        private FrontInputs Inputs(float now)
        {
            Vector2 span = TheaterFrame.Resolve();
            SpaceFeedMirror space = manager.SpaceMirror;
            OpsStateData ops = manager.OpsMirror.State;
            var c = new FrontInputs
            {
                Allocation = (int)manager.LocalAllocation, Now = now, CostScale = manager.Settings != null ? manager.Settings.ProgrammeCostScale.Value : 1f,
                Utc = DateTime.UtcNow, Frame = new MapFrame(span.x, span.y), HasBird = hasBird,
                BirdsDown = manager.OpsMirror.Known ? ops.BirdsDown : (byte)0, BirdPercent = ops.BirdPercent, EnemyBirdsDown = manager.OpsMirror.Known ? ops.EnemyBirdsDown : (byte)0, EnemyKnown = manager.OpsMirror.Known && ops.Active,
                SpaceLinked = space.Known && space.State.Active, Family = space.State.Family,
                RadarSeconds = SpaceFeedRules.RadarReadySeconds(space.State.RadarReadyAt, now), RadarUnavailable = space.State.RadarReadyAt == SpaceWire.RadarUnavailable,
                Teams = manager.SofMirror.Known ? manager.SofMirror.State.Teams.Count : 0, HeldBuilding = manager.SofMirror.Known && manager.SofMirror.State.Held.Count > 0,
                Price = id => manager.Quote(id).Cost, Faction = "", Callsign = ""
            };
            if (c.EnemyKnown)
            {
                for (int k = 0; k < 3; k++) { geoOwn[k] = ops.Geo[k]; geoFoe[k] = ops.Geo[3 + k]; }
                c.Geo = geoOwn; c.EnemyGeo = geoFoe;
            }
            if (calls != null && calls.TryAimPoint(out GlobalPosition aim)) { c.AimKnown = true; c.AimU = c.Frame.U(aim.x); c.AimV = c.Frame.V(aim.z); }
            c.Airbases = ReadAirbases(GameManager.GetLocalPlayer(out Player me) && me != null ? me.HQ : null);
            hasBird[0] = hasBird[1] = hasBird[2] = false;
            if (GameManager.GetLocalPlayer(out Player player) && player != null)
            {
                c.Faction = player.HQ != null && player.HQ.faction != null ? player.HQ.faction.factionName : "";
                c.Callsign = Callsign(player);
                if (player.HQ != null) for (int k = 0; k < 3; k++) hasBird[k] = manager.HasSpaceBird(player.HQ, (BirdKind)k);
            }
            return c;
        }

        private readonly GeoBird[] geoOwn = new GeoBird[3], geoFoe = new GeoBird[3];
        private readonly List<AirbaseFix> airbaseFixes = new List<AirbaseFix>(32);
        private readonly Dictionary<int, string> airbaseNames = new Dictionary<int, string>(32);

        /// <summary>The theatre's fixed airbases (carriers move and hold no ground) with who holds each, from the vanilla registry.</summary>
        private List<AirbaseFix> ReadAirbases(FactionHQ mine)
        {
            airbaseFixes.Clear();
            if (FactionRegistry.airbaseLookup == null) return airbaseFixes;
            foreach (Airbase a in FactionRegistry.airbaseLookup.Values)
            {
                try
                {
                    if (a == null || a.AttachedAirbase || a.UnitDestroyed()) continue;
                    int id = a.GetInstanceID();
                    if (!airbaseNames.TryGetValue(id, out string name)) airbaseNames[id] = name = BoscaliSummer.Modules.Command.Presentation.MapUi.MfdAirbaseFormatter.Format(a);
                    GlobalPosition p = (a.center != null ? a.center : a.transform).GlobalPosition();
                    FactionHQ hq = a.CurrentHQ;
                    airbaseFixes.Add(new AirbaseFix { X = p.x, Z = p.z, Name = name, Side = hq == null ? AirbaseSide.Neutral : hq == mine ? AirbaseSide.Own : AirbaseSide.Enemy });
                }
                catch (Exception) { /* a base torn down mid-frame: skip it this refresh */ }
            }
            return airbaseFixes;
        }

        private static string Callsign(Player player)
        {
            try { return (player.GetDisplayName(PlayerNameContext.ChatOrLeaderboard) ?? "").ToUpperInvariant(); }
            catch (Exception) { return ""; }
        }

        private void Footer()
        {
            float now = SupportManager.MissionNow();
            string text = Resting; AvState tone = AvState.Inert;
            bool mine = words.Length > 0 && now - wordsAt < WordsHold;
            bool theirs = calls != null && calls.LastWords.Length > 0 && now - calls.LastWordsAt < WordsHold;
            if (theirs && (!mine || calls.LastWordsAt >= wordsAt)) { text = calls.LastWords; tone = text.StartsWith("NEGATIVE", StringComparison.Ordinal) ? AvState.Danger : AvState.Info; }
            else if (mine) { text = words; tone = wordsTone; }
            else if (current == Front.Space && feed != null && feed.View.Words.Length > 0) { text = feed.View.Words; tone = feed.View.WordsTone; }
            window.Window.Footer.Set(text, tone);
        }

        // ---- Replies ----------------------------------------------------------------------------------------------------

        private void OnReply(SpaceReply reply)
        {
            if ((reply.Kind < SpaceCommandKind.FrontDirective || reply.Kind > SpaceCommandKind.FrontDonate) && reply.Kind != SpaceCommandKind.RelocateBird) return;
            if (pendingRequest == 0 || reply.RequestId != pendingRequest) return;
            pendingRequest = 0;
            var code = (FrontOutcome)reply.Outcome;
            words = (reply.Replayed ? "EARLIER · " : "") + FrontWords.Outcome(code, pendingFront, reply.Detail, reply.Claimant, reply.Charged);
            wordsTone = FrontWords.Good(code) ? AvState.Ready : AvState.Danger;
            wordsAt = SupportManager.MissionNow();
            AvUiSound.Play(FrontWords.Good(code) ? AvUiCue.Confirm : AvUiCue.Caution);
            nextRefresh = 0f; // the mirror usually follows in the same frame: repaint at once
        }

        private bool Sent(Front front, int request, string refusal)
        {
            if (request > 0) { pendingRequest = request; pendingFront = front; return true; }
            Say("NEGATIVE: " + refusal, AvState.Danger);
            return false;
        }

        private void Say(string text, AvState tone)
        {
            words = text; wordsTone = tone; wordsAt = SupportManager.MissionNow();
            AvUiSound.Play(tone == AvState.Danger ? AvUiCue.Caution : AvUiCue.Press);
            nextRefresh = 0f;
        }

        private bool Gate()
        {
            if (Time.unscaledTime < nextPress) return false;
            nextPress = Time.unscaledTime + PressGap;
            return manager != null;
        }

        // ---- IFrontActions: front verbs ---------------------------------------------------------------------------------

        public void SetDirective(Front front, FrontDirective directive)
        {
            if (!Gate()) return;
            Sent(front, manager.FrontSetDirective(front, directive), "NO HOST LINK");
        }

        public void SetPriority(Front front, int deltaPct)
        {
            if (!Gate()) return;
            FrontStateData d = manager.FrontMirror.State;
            if (!manager.FrontMirror.Known) { Say("NEGATIVE: WAITING FOR THE HOST", AvState.Danger); return; }
            int[] w = new int[FrontRules.FrontCount];
            for (int i = 0; i < w.Length; i++) w[i] = d.Fronts[i].PriorityPct;
            int f = (int)front, target = Mathf.Clamp(w[f] + deltaPct, 0, 100), others = 0;
            for (int i = 0; i < w.Length; i++) if (i != f) others += w[i];
            int rest = 100 - target;
            for (int i = 0; i < w.Length; i++)
                if (i != f) w[i] = others > 0 ? Mathf.RoundToInt(w[i] / (float)others * rest) : rest / (w.Length - 1);
            w[f] = target;
            Sent(front, manager.FrontSetPriority(w[0], w[1], w[2]), "NO HOST LINK");
        }

        public void SetFocus(Front front)
        {
            if (!Gate()) return;
            bool armed = manager.ArmLocalPick("FOCUS " + FrontRules.Name(front), point =>
            {
                if (!manager.FrontMirror.Known) return;
                Sent(front, manager.FrontSetFocus(front, (float)point.x, (float)point.z), "NO HOST LINK");
            });
            if (armed) Say("RIGHT-CLICK THE MAP TO PIN THE " + FrontRules.Name(front) + " FRONT'S FOCUS", AvState.Caution);
            else Say("NEGATIVE: MAP INPUT BUSY, DISARM THE ARMED CALL FIRST", AvState.Danger);
        }

        public void Queue(Front front, ProgrammeId id)
        {
            if (!Gate()) return;
            Sent(front, manager.FrontQueue(front, id), "NOT A " + FrontRules.Name(front) + " PROGRAMME");
        }

        public void Donate(Front front, ProgrammeId id, int amount)
        {
            if (!Gate()) return;
            Sent(front, manager.FrontDonate(front, id, amount), "NOTHING TO FUND");
        }

        public void SelectFront(Front front)
        {
            if (front == current) return;
            current = front;
            selectedNode = 0; selectedTeam = -1; selectedTarget = 0;
            feed?.SetVisible(front == Front.Space);
            WantFeeds(true);
            nextRefresh = 0f;
            if (open && window != null) { Build(); window.Paint(view); }
        }

        // ---- IFrontActions: NETWORK -------------------------------------------------------------------------------------

        public void SelectNode(int nodeId) { selectedNode = selectedNode == nodeId ? 0 : nodeId; nextRefresh = 0f; }

        public void Hop(int nodeId) { if (nodeId > 0 && Gate()) Sent(Front.Cyber, manager.CyberHop(nodeId), "NO HOST LINK"); }
        public void Burn(int nodeId) { if (nodeId > 0 && Gate()) Sent(Front.Cyber, manager.CyberBurn(nodeId), "NO HOST LINK"); }
        public void Drop(int nodeId) { if (nodeId > 0 && Gate()) Sent(Front.Cyber, manager.CyberDrop(nodeId), "NO HOST LINK"); }

        // ---- IFrontActions: SHADOW --------------------------------------------------------------------------------------

        public void SelectTeam(int slot) { selectedTeam = slot; nextRefresh = 0f; }

        public void SelectTarget(int targetId) { selectedTarget = selectedTarget == targetId ? 0 : targetId; nextRefresh = 0f; }

        public void TeamOrder(int slot, int verb)
        {
            if (slot < 0 || verb < 0 || verb > 3 || !Gate()) return;
            TeamVerb v = verb == 0 ? TeamVerb.Push : verb == 1 ? TeamVerb.Hold : verb == 2 ? TeamVerb.Exfil : TeamVerb.Lift;
            Sent(Front.Sof, manager.SofOrder(slot, v), "NO HOST LINK");
        }

        public void TeamMission(int slot, int missionKind, int targetId)
        {
            if (slot < 0 || !Gate()) return;
            var kind = (MissionKind)missionKind;
            SofStateData s = manager.SofMirror.State;
            SofTargetRow target = default;
            bool have = targetId > 0 && s.Targets.Exists(t => t.Id == targetId) && TryTarget(s, targetId, out target);
            if (kind == MissionKind.Recon)
            {
                if (!have) { Say("NEGATIVE: PICK A TARGET FIRST", AvState.Danger); return; }
                Sent(Front.Sof, manager.SofMission(slot, kind, 0, target.X, target.Z), "NO HOST LINK");
                return;
            }
            if (!have) { Say("NEGATIVE: PICK A TARGET FIRST", AvState.Danger); return; }
            Sent(Front.Sof, manager.SofMission(slot, kind, targetId, 0f, 0f), "NO HOST LINK");
        }

        private static bool TryTarget(SofStateData s, int id, out SofTargetRow row)
        {
            foreach (SofTargetRow t in s.Targets) if (t.Id == id) { row = t; return true; }
            row = default;
            return false;
        }

        // ---- IFrontActions: shared --------------------------------------------------------------------------------------

        public void Touch() { if (current == Front.Space) feed?.Touch(); }

        /// <summary>Sends the relocation order; the host (SpaceState owns a GeoBird per bird) judges it and the verdict words come back through OnReply.</summary>
        public void RelocateBird(int birdKind, float u, float v)
        {
            Touch();
            if (Gate()) Sent(Front.Space, manager.RelocateBird(birdKind, u, v), "NO HOST LINK");
        }

        public ISpaceFeedActions Orbit => feed;
    }
}
