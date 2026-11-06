using System;
using System.Collections.Generic;
using BoscaliSummer.Core.Game;
using BoscaliSummer.Core.Lifecycle;
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
        // The footer's resting line: how to use the room on screen, in the order the clicks happen (the latest answer replaces it for a few seconds).
        private static readonly string[] Resting =
        {
            "BUILD PAD > click a held airbase. LAUNCH a satellite on its card. FEED › or a click on a bird opens its DOWNLINK: click a box, then SEND TO TASKED. Esc closes.",
            "BUILD DATA CENTER > click a held airbase. DEPLOY TRUCK > click near it. On NODES: a node in reach > HOP, then hold it or BURN it. Esc closes.",
            "BUILD CAMP > click ground you hold. TRAIN a team. Pick a target on TARGETS (or click open ground for RECON): the nearest ready team is picked, SEND sends it. SWEEP hunts intruders. Esc closes.",
        };

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
        private float pendingAtWall, failedUntil;
        private bool refreshFaulted;
        private Front pendingFront;
        private string pendingWhat = "";
        private SpaceCommandKind pendingKind;
        private string words = "";
        private AvState wordsTone = AvState.Inert;
        private readonly bool[] hasBird = new bool[3];

        public bool IsOpen => open && window != null && window.IsOpen;
        public Front Current => current;
        /// <summary>The last front verdict in words (the footer shows it for a few seconds).</summary>
        public string LastWords => words;
        public float LastWordsAt => wordsAt;
        public AvState LastWordsTone => wordsTone;

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
            ClearSelections();
            pendingRequest = 0;
            failedUntil = 0f; refreshFaulted = false;
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
            int faction = CurrentFaction();
            if (faction != lastFaction) ClearSelections(); // another faction's ids mean something else; the same faction keeps its picks across opens and room switches
            lastFaction = faction;
            // The room covers the map but the manager still reads right-clicks there: an armed perk or post must not fire from inside the room.
            if (calls != null && (calls.Armed != null || calls.TaskedArmedId != 0 || calls.SteeringCruise)) calls.Disarm();
            WantFeeds(true);
            bool shown;
            try { Build(); shown = window.Open(view); }
            catch (System.Exception e) { Plugin.Logger?.LogError("OPS front window build failed: " + e); shown = false; }
            if (!shown) { feed?.SetVisible(false); WantFeeds(false); Say("NEGATIVE: WINDOW COULD NOT OPEN", AvState.Danger); return false; }
            open = true;
            ShowFeed();
            badSince = -1f;
            nextRefresh = Time.unscaledTime + Refresh;
            return true;
        }

        public void Close()
        {
            window?.Close();
        }

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

        /// <summary>The SPACE feed holds its lease and renders only while the room shows DOWNLINK (enlarged counts); SATELLITES, FRONT and other rooms let it go.</summary>
        private void ShowFeed() => feed?.SetVisible(open && window != null && window.DownlinkShown);

        /// <summary>Picks belong to one faction and one scene: either changing drops them all (a vanished id is cleared by the views, nothing else).</summary>
        private void ClearSelections()
        {
            selectedNode = 0; selectedTeam = -1; selectedTarget = 0;
            ShadowRoomView sof = view.Shadow;
            sof.HasPoint = false; sof.Reason = ""; sof.AimSeen = 0; sof.PickedId = 0;
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
            ShowFeed(); // a tab switch or ENLARGE lands here the frame it happens
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
                if (pendingRequest != 0 && Time.unscaledTime - pendingAtWall > 6f) { pendingRequest = 0; Say("NEGATIVE: NO ANSWER \u2014 PRESS AGAIN", AvState.Danger); }
                if (Time.unscaledTime < failedUntil || Time.unscaledTime < nextRefresh) return;
                nextRefresh = Time.unscaledTime + Refresh;
                Build();
                window.Paint(view);
                refreshFaulted = false;
                Footer();
            }
            catch (Exception e)
            {
                failedUntil = Time.unscaledTime + 2f; // a transient mirror glitch backs off and retries; it must not close the window
                if (!refreshFaulted) Plugin.Logger?.LogError("OPS front window refresh failed: " + e);
                refreshFaulted = true;
                nextRefresh = failedUntil;
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
                    FrontViews.Network(view, c, manager.CyberMirror.State, manager.CyberMirror.Known, selectedNode);
                    if (view.Network.Active) selectedNode = view.Network.SelectedId; // a blink with no snapshot keeps the pick
                    if (pendingRequest != 0 && ((pendingKind >= SpaceCommandKind.CyberHop && pendingKind <= SpaceCommandKind.CyberDrop) || pendingKind == SpaceCommandKind.CyberPurge))
                    {
                        // A sent verb waits for its receipt: say so on the session line (the receipt words land in the footer).
                        view.Network.Next = (pendingKind == SpaceCommandKind.CyberHop ? "HOP" : pendingKind == SpaceCommandKind.CyberBurn ? "BURN" : pendingKind == SpaceCommandKind.CyberPurge ? "PURGE" : "DROP") + " PENDING · " + Mathf.Max(0, (int)(Time.unscaledTime - pendingAtWall)) + " S · WAITING FOR THE HOST";
                        view.Network.NextTone = AvState.Info;
                    }
                    break;
                default:
                    FrontViews.Shadow(view, c, manager.SofMirror.State, manager.SofMirror.Known, manager.CyberMirror.Known ? manager.CyberMirror.State : null, selectedTeam, selectedTarget);
                    // Bug 9: a mirror that stopped running drops the picks too; only a blink before the host speaks keeps them.
                    if (manager.SofMirror.Known) { selectedTeam = view.Shadow.SelectedSlot; selectedTarget = view.Shadow.SelectedTargetId; }
                    break;
            }
            FrontViews.Terms(view);
        }

        /// <summary>The theatre frame the view projects with (the ORBIT room measures its pad pick in it).</summary>
        internal static MapFrame Frame()
        {
            Vector2 span = TheaterFrame.Resolve();
            return new MapFrame(span.x, span.y);
        }

        private FrontInputs Inputs(float now)
        {
            SpaceFeedMirror space = manager.SpaceMirror;
            OpsStateData ops = manager.OpsMirror.State;
            var c = new FrontInputs
            {
                Allocation = (int)manager.LocalAllocation, Grades = manager.LocalGrades(), Now = now, CostScale = manager.Settings != null ? manager.Settings.ProgrammeCostScale.Value : 1f,
                Utc = DateTime.UtcNow, Frame = Frame(), HasBird = hasBird,
                BirdsDown = manager.OpsMirror.Known ? ops.BirdsDown : (byte)0, BirdsLaunched = manager.OpsMirror.Known ? ops.BirdsLaunched : (byte)0,
                EnemyBirdsDown = manager.OpsMirror.Known ? ops.EnemyBirdsDown : (byte)0, EnemyKnown = manager.OpsMirror.Known && ops.Active,
                SpaceLinked = space.Known && space.State.Active, Family = space.State.Family,
                RadarSeconds = SpaceFeedRules.RadarReadySeconds(space.State.RadarReadyAt, now), RadarUnavailable = space.State.RadarReadyAt == SpaceWire.RadarUnavailable,
                Price = id => manager.Quote(id).Cost, Faction = "", Callsign = ""
            };
            c.WorldKnown = FrontViews.World(current, manager.OpsMirror.Known && ops.Active ? ops : null, manager.CyberMirror.Known ? manager.CyberMirror.State : null,
                manager.SofMirror.Known ? manager.SofMirror.State : null, now, out c.World);
            if (c.World.FobUp) foreach (OpsRow r in ops.Rows) if (r.Domain == OpDomain.Sof && r.Kind == OpKind.Fob && r.State == OpState.Done && r.EndsAt > c.FobUntil) c.FobUntil = r.EndsAt;
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
                // The faction's minute share as FrontBook.ShareTick takes it, read with this peer's share settings (the host's are not on the wire).
                float funds = player.HQ != null ? player.HQ.factionFunds : 0f;
                if (manager.Settings != null && float.IsFinite(funds) && funds > 0f)
                    c.Trickle = Math.Min(funds * FrontRules.ClampShare(manager.Settings.FrontShare.Value), Math.Max(0f, manager.Settings.FrontShareCap.Value));
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
                    if (!airbaseNames.TryGetValue(id, out string name)) airbaseNames[id] = name = MfdAirbaseFormatter.Format(a);
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
            string text = Resting[Mathf.Clamp((int)current, 0, Resting.Length - 1)]; AvState tone = AvState.Inert;
            if (pendingRequest != 0)
            {
                // Sent and not answered yet: say so at once, so a press is never met with silence.
                window.Window.Footer.Set("SENDING" + (pendingWhat.Length > 0 ? " " + pendingWhat : "") + " · WAITING FOR THE HOST · " + Mathf.Max(0, (int)(Time.unscaledTime - pendingAtWall)) + " S", AvState.Info);
                return;
            }
            bool mine = words.Length > 0 && now - wordsAt < WordsHold;
            bool theirs = calls != null && calls.LastWords.Length > 0 && now - calls.LastWordsAt < WordsHold;
            if (theirs && (!mine || calls.LastWordsAt >= wordsAt)) { text = calls.LastWords; tone = text.StartsWith("NEGATIVE", StringComparison.Ordinal) ? AvState.Danger : AvState.Info; }
            else if (mine) { text = words; tone = wordsTone; }
            else if (current == Front.Space && feed != null && feed.Shown && feed.View.Words.Length > 0) { text = feed.View.Words; tone = feed.View.WordsTone; }
            window.Window.Footer.Set(text, tone);
        }

        // ---- Replies ----------------------------------------------------------------------------------------------------

        private void OnReply(SpaceReply reply)
        {
            if (pendingRequest == 0 || reply.RequestId != pendingRequest) return;
            if (pendingKind != SpaceCommandKind.None)
            {
                // A CYBER or SOF verb: CallsController words the verdict (it is never a FrontOutcome); here the receipt only releases the gate, for the matching kind and id.
                if (reply.Kind != pendingKind) return;
                pendingRequest = 0;
                nextRefresh = 0f;
                return;
            }
            bool truck = reply.Kind == SpaceCommandKind.TruckMove;
            if ((reply.Kind < SpaceCommandKind.FrontDirective || reply.Kind > SpaceCommandKind.FrontDonate) && reply.Kind != SpaceCommandKind.RelocateBird && !truck) return;
            pendingRequest = 0;
            var code = (FrontOutcome)reply.Outcome;
            words = (reply.Replayed ? "EARLIER · " : "") + (truck ? FrontWords.TruckMove(code) : FrontWords.Outcome(code, pendingFront, reply.Detail, reply.Claimant, reply.Charged));
            wordsTone = FrontWords.Good(code) ? AvState.Ready : AvState.Danger;
            wordsAt = SupportManager.MissionNow();
            AvUiSound.Play(FrontWords.Good(code) ? AvUiCue.Confirm : AvUiCue.Caution);
            nextRefresh = 0f; // the mirror usually follows in the same frame: repaint at once
        }

        /// <param name="kind">The CYBER or SOF command whose receipt releases the gate; None for a front verb (worded here).</param>
        private bool Sent(Front front, int request, string refusal, SpaceCommandKind kind = SpaceCommandKind.None, string what = "")
        {
            if (pendingRequest != 0) { Say("NEGATIVE: WAITING FOR THE HOST", AvState.Danger); return false; }
            if (request > 0) { pendingRequest = request; pendingFront = front; pendingKind = kind; pendingAtWall = Time.unscaledTime; pendingWhat = what; AvUiSound.Play(AvUiCue.Press); return true; }
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
            if (pendingRequest != 0) { Say("NEGATIVE: WAITING FOR THE HOST", AvState.Danger); return false; }
            if (Time.unscaledTime < nextPress) { Say("STAND BY · ONE PRESS AT A TIME", AvState.Info); return false; } // never a silent drop
            nextPress = Time.unscaledTime + PressGap;
            return manager != null;
        }

        // ---- IFrontActions: front verbs ---------------------------------------------------------------------------------

        public void SetDirective(Front front, FrontDirective directive)
        {
            if (!Gate()) return;
            Sent(front, manager.FrontSetDirective(front, directive), "NO HOST LINK", SpaceCommandKind.None, FrontRules.Name(directive) + " POSTURE");
        }

        public void Queue(Front front, ProgrammeId id, byte kind, bool hasPoint = false, float u = 0f, float v = 0f)
        {
            if (!Gate()) return;
            MapFrame frame = Frame(); // the map uv the room picked, in world metres
            Sent(front, manager.FrontQueue(front, id, kind, hasPoint, frame.X(u), frame.Z(v)), "NOT A " + FrontRules.Name(front) + " PROGRAMME", SpaceCommandKind.None, "QUEUE " + FrontWords.Programme(id, kind));
        }

        public void Donate(Front front, ushort row, int amount)
        {
            if (!Gate()) return;
            Sent(front, manager.FrontDonate(front, row, amount), "NOTHING TO FUND", SpaceCommandKind.None, "CHIP IN " + amount + " AP");
        }

        public void MoveTruck(int truck, float x, float z)
        {
            if (Gate()) Sent(Front.Cyber, manager.MoveTruck(truck, x, z), "NO HOST LINK", SpaceCommandKind.None, "TRUCK MOVE");
        }

        public void SelectFront(Front front)
        {
            if (front == current) return;
            current = front;
            WantFeeds(true);
            nextRefresh = 0f;
            if (open && window != null) { Build(); window.Paint(view); }
            ShowFeed();
        }

        // ---- IFrontActions: NETWORK -------------------------------------------------------------------------------------

        public void SelectNode(int nodeId) { selectedNode = selectedNode == nodeId ? FrontViews.Gone : nodeId; nextRefresh = 0f; }

        public void Hop(int nodeId) { if (nodeId > 0 && Gate()) Sent(Front.Cyber, manager.CyberHop(nodeId), "NO HOST LINK", SpaceCommandKind.CyberHop, "HOP"); }
        public void Burn(int nodeId) { if (nodeId > 0 && Gate()) Sent(Front.Cyber, manager.CyberBurn(nodeId), "NO HOST LINK", SpaceCommandKind.CyberBurn, "BURN"); }
        public void Drop(int nodeId) { if (nodeId > 0 && Gate()) Sent(Front.Cyber, manager.CyberDrop(nodeId), "NO HOST LINK", SpaceCommandKind.CyberDrop, "DROP"); }
        /// <summary>The host reads a DROP of node 0 as the end of the whole session (every held node released, no package).</summary>
        public void DropAll() { if (Gate()) Sent(Front.Cyber, manager.CyberDrop(0), "NO HOST LINK", SpaceCommandKind.CyberDrop, "DROP ALL"); }
        public void Purge() { if (Gate()) Sent(Front.Cyber, manager.CyberPurge(), "NO HOST LINK", SpaceCommandKind.CyberPurge, "PURGE"); }
        public void Sweep() { if (Gate()) Sent(Front.Sof, manager.SofSweep(), "NO HOST LINK", SpaceCommandKind.SofSweep, "SWEEP"); }

        // ---- IFrontActions: SHADOW --------------------------------------------------------------------------------------

        public void SelectTeam(int slot) { selectedTeam = slot; nextRefresh = 0f; }

        /// <summary>
        /// Ruling D-R5: a pick never toggles (the same id keeps it; 0 is the explicit CLEAR) and a different target within <see cref="PickGap"/> of the last change is
        /// ignored, so a double event (a row and the marker under the same press) never flips the pick away.
        /// </summary>
        public void SelectTarget(int targetId)
        {
            nextRefresh = 0f;
            if (targetId == selectedTarget) return;
            if (targetId != 0 && Time.unscaledTime < nextPick) return;
            nextPick = Time.unscaledTime + PickGap;
            selectedTarget = targetId;
        }

        private const float PickGap = 0.25f;
        private float nextPick;

        public void TeamOrder(int slot, int verb)
        {
            if (slot < 0 || verb < 0 || verb > 3 || !Gate() || !TeamOk(slot)) return;
            TeamVerb v = verb == 0 ? TeamVerb.Push : verb == 1 ? TeamVerb.Hold : verb == 2 ? TeamVerb.Exfil : TeamVerb.Lift;
            Sent(Front.Sof, manager.SofOrder(slot, v), "NO HOST LINK", SpaceCommandKind.SofOrder, v.ToString().ToUpperInvariant());
        }

        public void TeamMission(int slot, int missionKind, int targetId)
        {
            if (slot < 0 || !Gate() || !TeamOk(slot)) return;
            var kind = (MissionKind)missionKind;
            SofStateData s = manager.SofMirror.State;
            SofTargetRow target = default;
            bool have = targetId > 0 && s.Targets.Exists(t => t.Id == targetId) && TryTarget(s, targetId, out target);
            bool sent;
            if (kind == MissionKind.Recon)
            {
                // RECON also takes a ground point (the draft click on the SHADOW map), carried in the target slot as FrontViews.PointId.
                if (FrontViews.TryPoint(targetId, out float px, out float pz)) sent = Sent(Front.Sof, manager.SofMission(slot, kind, 0, px, pz), "NO HOST LINK", SpaceCommandKind.SofMission, SofWords.Kind(kind));
                else if (!have) { Say("NEGATIVE: PICK A TARGET OR CLICK A MAP POINT FIRST", AvState.Danger); return; }
                else sent = Sent(Front.Sof, manager.SofMission(slot, kind, 0, target.X, target.Z), "NO HOST LINK", SpaceCommandKind.SofMission, SofWords.Kind(kind));
            }
            else if (!have) { Say("NEGATIVE: PICK A TARGET FIRST", AvState.Danger); return; }
            else sent = Sent(Front.Sof, manager.SofMission(slot, kind, targetId, 0f, 0f), "NO HOST LINK", SpaceCommandKind.SofMission, SofWords.Kind(kind));
            if (sent && kind == MissionKind.Recon && targetId < 0) view.Shadow.HasPoint = false; // the RECON point is spent with its order
        }

        /// <summary>An order names a team that must still be on the roster and not be commanded by another operator; a press never reaches a different team, and a vanished one drops the draft.</summary>
        private bool TeamOk(int slot)
        {
            foreach (SofTeamRow t in manager.SofMirror.State.Teams)
            {
                if (t.Slot != slot) continue;
                if (t.Mine || string.IsNullOrEmpty(t.Controller)) return true;
                Say("NEGATIVE: " + SofRules.Callsign(slot) + " IS COMMANDED BY " + t.Controller, AvState.Danger);
                return false;
            }
            Say("NEGATIVE: " + SofRules.Callsign(slot) + " IS NOT ON THE ROSTER", AvState.Danger);
            return false;
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
            if (Gate()) Sent(Front.Space, manager.RelocateBird(birdKind, u, v), "NO HOST LINK", SpaceCommandKind.None, "RELOCATION BURN");
        }

        public ISpaceFeedActions Orbit => feed;
    }
}
