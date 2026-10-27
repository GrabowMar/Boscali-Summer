using System.Collections.Generic;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Core.Lifecycle;
using BoscaliSummer.Modules.Support.Configuration;
using BoscaliSummer.Modules.Support.Domain.Calls;
using BoscaliSummer.Modules.Support.Domain.Cyber;
using BoscaliSummer.Modules.Support.Domain.Ops;
using BoscaliSummer.Modules.Support.Domain.Sof;
using BoscaliSummer.Modules.Support.Domain.Space;
using BoscaliSummer.Modules.Support.Runtime;
using BepInEx.Configuration;
using NOAvionics;
using NuclearOption.MissionEditorScripts;
using UnityEngine;

namespace BoscaliSummer.Modules.Support.Presentation
{
    /// <summary>
    /// Client CALL flow (core §5.3): press arms, a second press fires at the own designation (POD) or the last map pick;
    /// right-click on the map while armed also fires. Every outcome ends in words. Never spends: the host does.
    /// </summary>
    internal sealed class CallsController : MonoBehaviour, ISceneService
    {
        private static readonly SupportActionId[] Defaults =
            { SupportActionId.Recon, SupportActionId.Prsm, SupportActionId.JtacMark, SupportActionId.Cruise };

        private readonly ArmState arm = new ArmState();
        private readonly CallRequestTracker request = new CallRequestTracker();
        private readonly TaskedIntent tasked = new TaskedIntent();
        private const float ClaimTimeoutSeconds = 30f;
        private int callGeneration = 1;           // bumps on teardown and disarm: an old TASKED arm can never fire after either
        private int claimRequest, claimPost;      // the one TASKED claim in flight (SPACE request id), 0 when none
        private float claimStartedAt;
        private bool steering;                    // a cruise-leg map pick is armed (the pick itself lives in the manager)
        private bool cruiseClearAwaited;          // the cruise verb in flight was a route clear, not a leg
        private SupportActionId? pendingAction;   // the CALL in flight, for its perk row's PENDING paint
        private SupportManager manager;
        private SupportSettings settings;
        private IObservationSource observations;
        private GlobalPosition? mapAim;       // the pick of the current arm; cleared on every arm
        private bool armHasPick;              // false in the MAP BUSY case (armed without a local map pick)
        public float LastWordsAt { get; private set; }

        public SupportActionId?[] Favourites { get; } = new SupportActionId?[4];
        public SupportActionId? Armed => arm.Armed;
        public AimSource AimNow { get; private set; }
        public string LastWords { get; private set; } = "";
        public bool Pending => request.Pending;
        /// <summary>The CALL in flight (its perk row paints PENDING), null when none is.</summary>
        public SupportActionId? PendingAction => request.Pending ? pendingAction : null;
        /// <summary>The TASKED post armed for a claim, 0 when none (the PERKS tab paints its FIRE state from this).</summary>
        public int TaskedArmedId => tasked.PostId;
        /// <summary>A TASKED claim is waiting for the host's verdict.</summary>
        public bool TaskedClaimPending => claimRequest != 0;
        /// <summary>A cruise-leg map pick is armed.</summary>
        public bool SteeringCruise => steering;

        /// <summary>Raised with every line of words the controller says (arm, refusal, receipt): the C2 console mirrors them.</summary>
        internal event System.Action<string> Spoke;

        /// <summary>The point the armed CALL would fire at now: the own designation first, else the pick of this arm. False when neither exists.</summary>
        public bool TryAimPoint(out GlobalPosition point)
        {
            if (TryPod(out point)) return true;
            if (mapAim.HasValue) { point = mapAim.Value; return true; }
            if (TryTeamAim(out point)) return true;
            point = default;
            return false;
        }

        /// <summary>AIM: TEAM (spec 2.4): the target a SOF team of this faction is lasing right now, from the faction mirror. Last in line after the pod and the map pick.</summary>
        private bool TryTeamAim(out GlobalPosition point)
        {
            point = default;
            if (manager == null || !manager.SofMirror.TryLase(out _, out float x, out float z)) return false;
            point = new GlobalPosition(x, 0f, z);
            return true;
        }

        public void Configure(SupportManager manager, SupportSettings settings, IObservationSource observations)
        {
            this.manager = manager;
            this.settings = settings;
            this.observations = observations;
            for (int i = 0; i < Favourites.Length; i++) Favourites[i] = Defaults[i];
            if (manager != null) { manager.SpaceReplied += OnSpaceReply; manager.CruiseReplied += OnCruiseReply; }
        }

        private void OnDestroy()
        {
            if (manager != null) { manager.SpaceReplied -= OnSpaceReply; manager.CruiseReplied -= OnCruiseReply; }
        }

        public void ResetForScene()
        {
            arm.Clear();
            request.Clear();
            ClearTasked();
            claimRequest = claimPost = 0; // a scene change abandons any claim in flight: its receipt belongs to the old scene
            steering = false;
            mapAim = null;
            armHasPick = false;
            cruiseClearAwaited = false;
            pendingAction = null;
            LastWords = "";
            LastWordsAt = -100f;
        }

        /// <summary>The shared pre-flight: busy, authorised and ready, off cooldown, affordable. Says why and returns false.</summary>
        private bool Check(SupportActionId id)
        {
            bool free = id == SupportActionId.JtacUnlase; // UNLASE is free: no cooldown, no gate
            if (request.Pending || manager.RequestPending) { Say(CallWords.Refusal(CallRefusal.Busy), AvUiCue.Caution); return false; }
            string locked = free ? "" : manager.LockReason(id);
            if (locked.Length > 0) { Say(CallWords.Refusal(CallRefusal.Locked, unlock: locked), AvUiCue.Caution); return false; }
            if (!free && manager.LocalCooldownRemaining(id) > 0.5f)
            {
                Say(CallWords.Refusal(CallRefusal.Cooldown, seconds: Mathf.CeilToInt(manager.LocalCooldownRemaining(id))), AvUiCue.Caution);
                return false;
            }
            int cost = manager.Quote(id).Cost;
            if (!free && !manager.BypassRequirements && manager.LocalAllocation + 0.001f < cost) { Say(CallWords.Refusal(CallRefusal.LowCredit, need: cost), AvUiCue.Caution); return false; }
            return true;
        }

        public void Press(SupportActionId id)
        {
            if (manager == null) return;
            float now = SupportManager.MissionNow();
            if (!Check(id)) return;

            ArmStep step = arm.Press(id, now);
            if (step == ArmStep.Bounce) return; // the arming click bounced (a double-click): never a FIRE
            if (step == ArmStep.Armed)
            {
                if (armHasPick || steering) manager.Disarm(); // our own earlier pick gives way; another owner's pick (a FOCUS pin) refuses the map below
                steering = false;
                tasked.Clear(); // a STANDARD arm gives way to nothing and a TASKED arm gives way to it
                mapAim = null;
                bool mapOk = manager.ArmLocalPick(Label(id), point =>
                {
                    mapAim = point;
                    armHasPick = false; // the manager consumed its pick
                    if (arm.Armed != id) return;
                    if (Check(id)) Fire(id, point, SupportManager.MissionNow()); // right-click while armed fires at once
                    else arm.Clear(); // the pick is spent, so the arm is too; Check already worded why
                });
                armHasPick = mapOk;
                Say("ARMED · " + Label(id) + (mapOk ? " · FIRE, OR RIGHT-CLICK THE MAP" : " · FIRE TO SEND (MAP BUSY)"), AvUiCue.Engage);
                return;
            }
            if (TryPod(out GlobalPosition pod)) { Fire(id, pod, now); return; }
            if (mapAim.HasValue) { Fire(id, mapAim.Value, now); return; }
            if (TryTeamAim(out GlobalPosition team)) { Fire(id, team, now); return; }
            // A perk the host answers without an aim (SIPHON) fires at the pilot's own position when nothing is designated.
            if (!CallSheet.NeedsAim(id) && GameManager.GetLocalAircraft(out Aircraft self) && self != null) { Fire(id, self.GlobalPosition(), now); return; }
            Say(CallWords.Refusal(CallRefusal.NoAim), AvUiCue.Caution);
        }

        /// <summary>An ARM-labelled control: arms this perk and never fires. A no-op when it is already armed, so a stale ARM widget cannot become a FIRE.</summary>
        public void Arm(SupportActionId id)
        {
            if (arm.Armed != id) Press(id);
        }

        /// <summary>A FIRE-labelled control: fires only the perk armed right now. Any other (or no) armed perk is refused and nothing changes.</summary>
        public void FireArmed(SupportActionId id)
        {
            if (arm.Armed != id) { Say("NEGATIVE: " + Label(id) + " NOT ARMED — ARM IT FIRST", AvUiCue.Caution); return; }
            Press(id);
        }

        public void Disarm()
        {
            arm.Clear();
            ClearTasked();
            steering = false;
            armHasPick = false;
            mapAim = null; // no aim marker outlives its arm
            manager?.Disarm();
            Say("DISARMED", AvUiCue.Release);
        }

        private void ClearTasked()
        {
            tasked.Clear();
            callGeneration++;
        }

        private bool TryFindPost(int postId, out FeedPost post)
        {
            var posts = manager.SpaceMirror.State.Posts;
            for (int i = 0; i < posts.Count; i++)
                if (posts[i].CallId == postId) { post = posts[i]; return true; }
            post = default;
            return false;
        }

        /// <summary>
        /// The TASKED preflight on the mirrored post: busy, link, post status, the host's claim gate for this viewer and the host
        /// quote on the post (never the STANDARD price). Says why and returns false.
        /// </summary>
        private bool CheckTasked(in FeedPost post, float now)
        {
            if (request.Pending || manager.RequestPending || claimRequest != 0) { Say(CallWords.Refusal(CallRefusal.Busy), AvUiCue.Caution); return false; }
            SpaceFeedState state = manager.SpaceMirror.State;
            if (!manager.SpaceMirror.Known || !state.Active) { Say(CallWords.Refusal(CallRefusal.Offline), AvUiCue.Caution); return false; }
            switch (SpaceFeedRules.PostStatusOf(post, now))
            {
                case PostStatus.Stale: Say(TaskedWords.Of(TaskedOutcome.NoCall), AvUiCue.Caution); return false;
                case PostStatus.Launching: Say(TaskedWords.Of(TaskedOutcome.ClaimedByOther), AvUiCue.Caution); return false;
            }
            if (state.Gate != TaskedOutcome.None && post.Domain == TaskedDomain.Space) // the headline gate is the rod's: a CYBER package or a SOF post has no bird
            {
                int detail = SpaceMirror.GateIsDeadline(state.Gate) ? Mathf.Max(1, state.GateDetail - Mathf.FloorToInt(now)) : state.GateDetail;
                Say(TaskedWords.Of(state.Gate, detail), AvUiCue.Caution);
                return false;
            }
            if (post.Price > 0 && manager.LocalAllocation + 0.001f < post.Price)
            {
                Say(CallWords.Refusal(CallRefusal.LowCredit, need: post.Price), AvUiCue.Caution);
                return false;
            }
            return true;
        }

        /// <summary>First press arms exactly this post locally (nothing is claimed yet); the next press on it (same mirror and call generation) sends the host's claim-and-launch.</summary>
        public void PressTasked(int postId)
        {
            if (manager == null) return;
            float now = SupportManager.MissionNow();
            if (!TryFindPost(postId, out FeedPost post))
            {
                tasked.Clear();
                Say(TaskedWords.Of(TaskedOutcome.NoCall), AvUiCue.Caution);
                return;
            }
            if (!CheckTasked(post, now)) { if (!StillOpen(postId, now)) tasked.Clear(); return; } // a busy line or a short purse keeps the arm; a gone post drops it
            if (tasked.Press(postId, manager.SpaceMirror.Generation, callGeneration, now) == ArmStep.Armed)
            {
                arm.Clear();
                steering = false;
                manager.Disarm(); // one armed intent at a time: a STANDARD arm gives way
                Say("ARMED · TASKED " + (post.Points != null ? post.Points.Length : 0) + " TARGET" +
                    (post.Points != null && post.Points.Length == 1 ? "" : "S") + " · " + (post.Price > 0 ? post.Price + " ALLOC" : "FREE") + " · FIRE TO LAUNCH", AvUiCue.Engage);
                return;
            }
            // No aim is sent: the host claims the post's own stored ground point and prices it itself.
            int id = manager.SpaceClaim(postId);
            if (id <= 0) { Say(CallWords.Refusal(CallRefusal.Unavailable), AvUiCue.Caution); return; }
            claimRequest = id; // registered before any verdict: in-process replies arrive on the next Update
            claimPost = postId;
            claimStartedAt = now;
            AnswerTasked(id, TaskedReceiptState.Pending, TaskedReceipts.Words(TaskedReceiptState.Pending, TaskedOutcome.Queued, 0));
        }

        /// <summary>An ARM-labelled TASKED control: arms exactly this post and never sends the claim. A no-op when it is already armed.</summary>
        public void ArmTasked(int postId)
        {
            if (tasked.PostId != postId) PressTasked(postId);
        }

        /// <summary>A FIRE-labelled TASKED control: sends the claim only for the exact post armed right now; anything else is refused and nothing changes.</summary>
        public void FireTasked(int postId)
        {
            if (postId == 0 || tasked.PostId != postId) { Say("NEGATIVE: THAT POST IS NOT ARMED — ARM IT FIRST", AvUiCue.Caution); return; }
            PressTasked(postId);
        }

        /// <summary>The receipt of the claim: PENDING and LAUNCHING are not SHOT; only the physical launch is.</summary>
        internal void AnswerTasked(int requestId, TaskedReceiptState state, string words)
        {
            if (requestId != claimRequest) return; // a late answer to a claim already finished or cleared
            if (state == TaskedReceiptState.Launched || state == TaskedReceiptState.Refused) { claimRequest = 0; claimPost = 0; }
            Say(words, state == TaskedReceiptState.Launched ? AvUiCue.Confirm
                : state == TaskedReceiptState.Refused ? AvUiCue.Caution
                : state == TaskedReceiptState.Launching ? AvUiCue.Engage : AvUiCue.Press);
        }

        /// <summary>The host's cruise verdict: only now is a leg (or a clear) confirmed; a refusal is worded, never silent.</summary>
        private void OnCruiseReply(SupportResult result, int legs)
        {
            bool cleared = cruiseClearAwaited;
            cruiseClearAwaited = false;
            if (result == SupportResult.Accepted)
                Say(cleared ? "CRUISE ROUTE CLEARED \u00B7 FLYING DIRECT" : "LEG CONFIRMED \u00B7 " + legs + " SET \u00B7 ADD LEG FOR ANOTHER", AvUiCue.Confirm);
            else
                Say("NEGATIVE: CRUISE " + (cleared ? "CLEAR" : "LEG") + " REFUSED \u2014 CHECK THE ROUTE AND TRY AGAIN", AvUiCue.Caution);
        }

        private void OnSpaceReply(SpaceReply reply)
        {
            if ((reply.Kind >= SpaceCommandKind.CyberHop && reply.Kind <= SpaceCommandKind.CyberDrop) || reply.Kind == SpaceCommandKind.CyberPurge)
            {
                // A CYBER verdict (HOP / BURN / DROP): its words reach the console and the NET footer; a refusal also cautions.
                CyberResult verdict = new CyberResult(reply.CyberVerdict, reply.CallId, reply.Charged, reply.Detail);
                Say((reply.Replayed ? "EARLIER · " : "") + verdict.Words, verdict.Ok ? AvUiCue.Confirm : AvUiCue.Caution);
                return;
            }
            if ((reply.Kind >= SpaceCommandKind.SofRaise && reply.Kind <= SpaceCommandKind.SofDivert) || reply.Kind == SpaceCommandKind.SofSweep)
            {
                // A SOF verdict (RAISE / ORDER / MISSION / DIVERT): its words reach the console and the SOF footer; a refusal also cautions.
                var verdict = new SofResult(reply.SofVerdict, reply.CallId, reply.Charged, reply.Detail);
                Say((reply.Replayed ? "EARLIER · " : "") + verdict.Words, verdict.Ok ? AvUiCue.Confirm : AvUiCue.Caution);
                return;
            }
            if (reply.Kind >= SpaceCommandKind.OpFund && reply.Kind <= SpaceCommandKind.OpCancel)
            {
                // An OPERATIONS verdict (FUND / PLAN / CANCEL): its words reach the console and the NET and SOF footers; a refusal also cautions.
                var verdict = new OpResult((OpOutcome)reply.Outcome, (OpKind)Mathf.Clamp(reply.CallId, 0, (int)OpKind.Fob), reply.Charged, reply.Detail);
                Say((reply.Replayed ? "EARLIER · " : "") + verdict.Words, verdict.Ok ? AvUiCue.Confirm : AvUiCue.Caution);
                return;
            }
            if (reply.Kind != SpaceCommandKind.ClaimTasked || reply.RequestId != claimRequest || claimRequest == 0) return;
            bool launching = TryFindPost(claimPost, out FeedPost post) && post.Launching;
            TaskedReceiptState state = TaskedReceipts.State(reply.Tasked, launching);
            string words = TaskedReceipts.Words(state, reply.Tasked, reply.Detail);
            if (reply.Tasked == TaskedOutcome.ClaimedByOther && !string.IsNullOrEmpty(reply.Claimant))
                words = "NEGATIVE: CLAIMED BY " + reply.Claimant + " — IT REOPENS IF THEIR LAUNCH FAILS";
            if (reply.Replayed) words = "EARLIER · " + words; // a retry replays the original receipt: history, not a new event
            AnswerTasked(reply.RequestId, state, words);
        }

        /// <summary>Host answer: called by SupportManager.ReceiveResult with the request id and the result words.</summary>
        internal void Answer(int requestId, bool accepted, string words)
        {
            if (!request.Resolve(requestId)) return; // late answer after the local timeout: the host reply is the truth
            pendingAction = null;
            Say(accepted ? "SHOT · " + words : words, accepted ? AvUiCue.Confirm : AvUiCue.Caution);
        }

        /// <summary>
        /// The old single STEER control, kept for automation and hotkeys: <see cref="AddLeg"/> when no pick is armed, <see cref="ClearRoute"/> while one is.
        /// The OPS page uses the three distinct intents (ADD LEG, CANCEL PICK, CLEAR ROUTE). One leg per press, so the map gesture is never re-armed under the pilot.
        /// </summary>
        public void SteerCruise()
        {
            if (steering) ClearRoute();
            else AddLeg();
        }

        /// <summary>CLEAR ROUTE: asks the host to drop every confirmed leg of the flying cruise (a pick in progress is cancelled first). The verdict arrives in <see cref="OnCruiseReply"/>.</summary>
        public void ClearRoute()
        {
            if (manager == null) return;
            if (steering) { steering = false; manager.Disarm(); }
            cruiseClearAwaited = manager.ClearCruiseLegs(); // only a clear that was really sent can be answered as one
            Say(cruiseClearAwaited ? "CLEAR SENT · STAND BY FOR THE HOST" : "NEGATIVE: NO CRUISE ROUTE TO CLEAR", cruiseClearAwaited ? AvUiCue.Release : AvUiCue.Caution);
        }

        /// <summary>CANCEL PICK: drops the armed leg pick and keeps the confirmed route. A no-op when no pick is armed.</summary>
        public void CancelPick()
        {
            if (!steering) return;
            steering = false;
            manager?.Disarm();
            Say("PICK CANCELLED · ROUTE KEPT", AvUiCue.Release);
        }

        /// <summary>ADD LEG: arms one map pick for the next leg of the flying cruise; the right-click sends it. A no-op while a pick is already armed.</summary>
        public void AddLeg()
        {
            if (manager == null || steering) return;
            if (!manager.TryFlyingCruise(out int id, out int legs)) { Say("NEGATIVE: NO CRUISE IN FLIGHT \u2014 FIRE CRUISE FIRST", AvUiCue.Caution); return; }
            if (legs >= StrikeBallistics.MaxLegs) { Say("NEGATIVE: ROUTE FULL \u2014 " + StrikeBallistics.MaxLegs + " LEGS ALREADY SET", AvUiCue.Caution); return; }
            arm.Clear(); tasked.Clear();
            if (armHasPick || steering) { manager.Disarm(); armHasPick = false; } // our own earlier pick (the armed perk, a leg) gives way to this leg
            steering = true;
            bool ok = manager.ArmLocalPick("CRUISE LEG " + (legs + 1) + " OF " + StrikeBallistics.MaxLegs, point =>
            {
                steering = false;
                if (!manager.RequestWaypoint(point)) { Say(CallWords.Refusal(CallRefusal.Unavailable), AvUiCue.Caution); return; }
                cruiseClearAwaited = false;
                Say("LEG SENT \u00B7 STAND BY FOR THE HOST", AvUiCue.Engage);
            });
            if (!ok) { steering = false; Say(CallWords.Refusal(CallRefusal.Unavailable), AvUiCue.Caution); return; }
            Say("ADD LEG \u00B7 RIGHT-CLICK THE MAP FOR THE LEG \u00B7 CANCEL PICK KEEPS THE ROUTE", AvUiCue.Engage);
        }

        private void Fire(SupportActionId id, GlobalPosition target, float now)
        {
            // Begin tracking BEFORE the send: on a server peer the host answers synchronously inside RequestAt.
            int requestId = manager.RequestAt(id, target, rid => request.Begin(rid, manager.Quote(id).Cost, now));
            if (requestId <= 0) { Say(CallWords.Refusal(CallRefusal.Unavailable), AvUiCue.Caution); return; } // arm stays
            if (id != SupportActionId.JtacUnlase) { arm.Clear(); manager.Disarm(); armHasPick = false; mapAim = null; } // UNLASE never disturbs an armed CALL
            if (manager.LastRequestOffline) { request.Clear(); Say(CallWords.Refusal(CallRefusal.Offline), AvUiCue.Caution); return; }
            pendingAction = id;
            if (!request.Pending) { pendingAction = null; return; } // answered in-process: Answer already said the real words
            AimNow = AimSource.None;
            Say("PENDING · " + Label(id) + (id == SupportActionId.Cruise ? " · STEER IT ON TASKED" : ""), AvUiCue.Press);
        }

        private void Update()
        {
            if (manager == null) return;
            float now = SupportManager.MissionNow();
            if (arm.Tick(now)) { manager.Disarm(); Say("DISARMED", AvUiCue.Release); }
            else if (arm.Armed != null && armHasPick && !manager.LocalPickArmed)
            {
                arm.Clear(); // ESC (or a scene reset) cancelled the manager's pick: do not stay half armed
                Say("DISARMED", AvUiCue.Release);
            }
            if (request.Tick(now, out _)) { pendingAction = null; manager.AbandonPending(); Say(CallWords.Refusal(CallRefusal.Timeout), AvUiCue.Caution); }
            if (tasked.Tick(now)) Say("DISARMED", AvUiCue.Release);
            else if (tasked.PostId != 0 && (tasked.Revalidate(manager.SpaceMirror.Generation, callGeneration) || !StillOpen(tasked.PostId, now)))
            {
                tasked.Clear(); // the board moved or the post went stale under the arm: it can never fire
                Say(TaskedWords.Of(TaskedOutcome.NoCall), AvUiCue.Caution);
            }
            if (claimRequest != 0 && now - claimStartedAt > ClaimTimeoutSeconds)
            {
                claimRequest = claimPost = 0;
                Say("NEGATIVE: NO ANSWER — CHECK THE BOARD BEFORE PRESSING AGAIN", AvUiCue.Caution);
            }
            if (steering && !manager.LocalPickArmed) steering = false; // ESC (or a scene reset) dropped the pick under the steer
            AimNow = arm.Armed == null ? AimSource.None : Aim.Pick(TryPod(out _), mapAim.HasValue, TryTeamAim(out _));

            if (GameplayUI.GameIsPaused || InputFieldChecker.InsideInputField || !Application.isFocused) return;
            Poll(settings.CallKey1, 0);
            Poll(settings.CallKey2, 1);
            Poll(settings.CallKey3, 2);
            Poll(settings.CallKey4, 3);
        }

        private bool StillOpen(int postId, float now) =>
            TryFindPost(postId, out FeedPost post) && SpaceFeedRules.PostStatusOf(post, now) == PostStatus.Open;

        private void Poll(ConfigEntry<KeyboardShortcut> key, int slot)
        {
            if (key == null || key.Value.MainKey == KeyCode.None || !key.Value.IsDown()) return;
            if (Favourites[slot].HasValue) Press(Favourites[slot].Value);
        }

        private bool TryPod(out GlobalPosition point)
        {
            point = default;
            if (GameManager.GetLocalAircraft(out Aircraft aircraft) && aircraft != null && !aircraft.disabled
                && aircraft.weaponManager != null)
            {
                List<Unit> targets = aircraft.weaponManager.GetTargetList();
                if (targets != null && targets.Count > 0 && targets[0] != null && !targets[0].disabled)
                {
                    point = targets[0].GlobalPosition();
                    return true;
                }
            }
            if (observations != null && observations.TryGet(out ObservationPoint mark))
            {
                point = new GlobalPosition(mark.X, mark.Y, mark.Z);
                return true;
            }
            return false;
        }

        private void Say(string words, AvUiCue cue)
        {
            LastWords = words ?? "";
            LastWordsAt = SupportManager.MissionNow();
            AvUiSound.Play(cue);
            Spoke?.Invoke(LastWords);
        }

        private static string Label(SupportActionId id) => CallSheet.TryGet(id, out CallRow row) ? row.Label : id.ToString();
    }
}
