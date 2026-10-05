using System;
using System.Collections.Generic;
using BepInEx.Logging;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Core.Lifecycle;
using BoscaliSummer.Modules.Support.Configuration;
using BoscaliSummer.Modules.Support.Domain.Calls;
using BoscaliSummer.Modules.Support.Domain.Space;
using BoscaliSummer.Modules.Support.Runtime;
using BoscaliSummer.Modules.Support.Visuals;
using NOAvionics;
using NuclearOption.Networking;
using UnityEngine;

namespace BoscaliSummer.Modules.Support.Presentation
{
    /// <summary>
    /// The one owner of the SPACE operator feed. It reads the faction mirror (never a client registry), drives the optical imager
    /// and the SAR collector, builds one <see cref="SpaceFeedView"/> and paints it onto whichever surfaces are showing: the
    /// compact page on the OPS MFD and the full-screen window. Every action is a request to the host through
    /// <see cref="SupportManager"/> and <see cref="CallsController"/>; nothing here spends, grants or decides.
    ///
    /// <para>In-process SPACE replies arrive on the next Update, so a request id is registered here as soon as the request call
    /// returns it. A MARK replay returns the historical verdict and is worded as history.</para>
    /// </summary>
    internal sealed class SpaceFeedController : MonoBehaviour, ISceneService, ISpaceFeedActions
    {
        private const float RefreshCompact = 0.15f, RefreshFull = 0.08f, RequestTimeout = 6f, WordsHold = 10f;
        private const float OpticalWidth = 640f, OpticalHeight = 400f;
        private const float RadarAltitude = 600000f;

        private SupportManager manager;
        private CallsController calls;
        private SupportSettings settings;
        private ManualLogSource logger;

        private readonly FeedDraft draft = new FeedDraft();
        private readonly SpaceFeedView view = new SpaceFeedView();
        private readonly List<FeedEntry> entries = new List<FeedEntry>(SpaceWire.MaxContacts + SpaceWire.MaxMarks);
        private readonly List<int> postOrder = new List<int>(SpaceWire.MaxPosts);
        private readonly int[] sendIds = new int[SpaceCommand.MaxIds];

        private SpaceFeedPanel compact;
        private SpaceFeedWindow window;
        private bool compactVisible, leaseOpen, dirty;
        private float nextRefresh, nextActivity, failedUntil;
        private int lastFaction;

        private SatelliteImager imager;
        private SarCollector sar;
        private int sarSerial, radarBaseline, cameraBaseline;
        private bool baselined;

        private int markRequest, sendRequest;
        private float markAt, sendAt;
        private string feedWords = "";
        private AvState feedTone = AvState.Inert;
        private float feedWordsAt = -100f;

        public bool WindowOpen => window != null && window.IsOpen;
        public FeedDraft Draft => draft;

        public void Configure(SupportManager manager, CallsController calls, SupportSettings settings, ManualLogSource logger)
        {
            this.manager = manager;
            this.calls = calls;
            this.settings = settings;
            this.logger = logger;
            if (manager != null) manager.SpaceReplied += OnReply;
        }

        /// <summary>The compact page of the OPS console; null until it is built.</summary>
        internal void AttachCompact(SpaceFeedPanel panel) { compact = panel; dirty = true; }

        /// <summary>The OPS console says whether the SPACE page is on screen.</summary>
        internal void SetCompactVisible(bool visible)
        {
            if (compactVisible == visible) return;
            compactVisible = visible;
            if (visible) draft.Touch(SupportManager.MissionNow());
            dirty = true;
        }

        public void ResetForScene()
        {
            CloseWindow(FeedCloseReason.UserExit, quiet: true);
            ClearView();
            leaseOpen = false;
            compactVisible = false;
            baselined = false;
            SpaceCockpitThreatProbe.ResetForScene();
            DisposeSensors();
        }

        private void OnDestroy()
        {
            if (manager != null) manager.SpaceReplied -= OnReply;
            CloseWindow(FeedCloseReason.UserExit, quiet: true);
            DisposeSensors();
            SpaceCockpitThreatProbe.ResetForScene();
        }

        private void DisposeSensors()
        {
            if (imager != null) Destroy(imager.gameObject);
            imager = null;
            sar?.Dispose();
            sar = null;
            sarSerial = 0;
        }

        /// <summary>Faction change, scene reset or operator loss: nothing of the old view may be painted again.</summary>
        private void ClearView()
        {
            draft.Clear();
            entries.Clear();
            markRequest = sendRequest = 0;
            feedWords = "";
            feedWordsAt = -100f;
            view.Brackets.Clear();
            view.Image = null;
            view.ImageKind = FeedImageKind.None;
            sar?.Dispose();
            sar = null;
            sarSerial = 0;
            imager?.SetApprovedContacts(null);
            dirty = true;
        }

        // ---- Frame -----------------------------------------------------------------------------------------------------

        private void Update()
        {
            if (manager == null) return;
            float now = SupportManager.MissionNow();
            if (!baselined && SpaceRules.MissionTime(now))
            {
                // Scans and looks from before this scene are not this scene's pictures.
                baselined = true;
                radarBaseline = manager.RadarScanSerial;
                cameraBaseline = manager.CameraSerial;
            }
            WatchFaction();
            ExpireRequests(now);

            bool wanted = compactVisible || WindowOpen;
            if (wanted && !leaseOpen) { manager.SpaceOpenFeed(); leaseOpen = true; }
            else if (!wanted && leaseOpen)
            {
                manager.SpaceCloseFeed();
                leaseOpen = false;
                if (imager != null) imager.Visible = false;
                if (sar != null) sar.Visible = false;
            }
            if (!wanted) return;

            float wall = Time.unscaledTime;
            if (wall < failedUntil) return; // a refresh fault holds the feed off for a moment, however often the panel re-shows it
            if (!dirty && wall < nextRefresh) { sar?.Tick(now); return; }
            nextRefresh = wall + (WindowOpen ? RefreshFull : RefreshCompact);
            dirty = false;
            try { Refresh(now); }
            catch (Exception e)
            {
                failedUntil = wall + 2f; // latch: CallsPanel re-shows the compact feed every frame, so a flag alone cannot hold
                logger?.LogError("SPACE feed refresh failed: " + e);
                compactVisible = false;
                CloseWindow(FeedCloseReason.InvalidOperator, quiet: false);
            }
        }

        /// <summary>A faction change clears the old mirror and draft before anything repaints.</summary>
        private void WatchFaction()
        {
            int key = 0;
            if (GameManager.GetLocalPlayer(out Player player) && player != null && player.HQ != null) key = manager.FactionKeyOf(player.HQ);
            if (key == lastFaction) return;
            bool switched = lastFaction != 0;
            lastFaction = key;
            if (!switched) return;
            ClearView();
            if (WindowOpen) CloseWindow(FeedCloseReason.InvalidOperator, quiet: false);
        }

        private void ExpireRequests(float now)
        {
            if (markRequest != 0 && now - markAt > RequestTimeout) { markRequest = 0; Say("NEGATIVE: NO ANSWER — MARK AGAIN", AvState.Danger, AvUiCue.Caution); }
            if (sendRequest != 0 && now - sendAt > RequestTimeout) { sendRequest = 0; Say("NEGATIVE: NO ANSWER — CHECK THE BOARD", AvState.Danger, AvUiCue.Caution); }
        }

        // ---- Refresh ---------------------------------------------------------------------------------------------------

        private void Refresh(float now)
        {
            SpaceFeedMirror mirror = manager.SpaceMirror;
            SpaceFeedState state = mirror.State;
            bool known = mirror.Known;

            SpaceFeedEntries.Build(state.Contacts, state.Marks, now, entries);
            view.Pages = SpaceFeedRules.PageCount(entries.Count);
            draft.Page = SpaceFeedRules.ClampPage(draft.Page, entries.Count);
            if (draft.Selected != 0 && IndexOf(draft.Selected) < 0) draft.Selected = 0;
            view.Page = draft.Page;
            view.Source = draft.Source;
            view.Zoom = draft.Zoom;
            view.ZoomEnabled = draft.Source == BirdKind.Optical;

            FillThreat();
            FillImage(state, known, now);
            view.Status = StatusFor(state, known);
            view.StatusTone = !known || !state.Active || state.Family == SpaceFamilyState.Dark ? AvState.Danger
                : state.Family == SpaceFamilyState.Degraded ? AvState.Caution : AvState.Info;
            FillTiles();
            FillNoContacts(state, known, now);
            FillActions(state, known);
            FillPosts(state, now);
            FillWords(now);

            if (compactVisible && compact != null)
            {
                view.TaskedCaption = SpaceFeedRules.CaptionOf(postOrder.Count, compact.Layout.CardCount);
                compact.Paint(view);
            }
            if (WindowOpen)
            {
                view.TaskedCaption = SpaceFeedRules.CaptionOf(postOrder.Count, window.Panel.Layout.CardCount);
                window.Panel.Paint(view);
            }
        }

        private int IndexOf(int id)
        {
            for (int i = 0; i < entries.Count; i++) if (entries[i].Id == id) return i;
            return -1;
        }

        private void FillThreat()
        {
            if (!SpaceCockpitThreatProbe.TryRead(out CockpitThreatSnapshot snap)) snap = default;
            view.Ground = !snap.ValidOwnship;
            string strip = SpaceFeedRules.ThreatStrip(snap);
            view.ThreatActive = strip.Length > 0;
            view.Threat = view.ThreatActive ? "WARNING · " + strip
                : snap.ValidOwnship ? "NO CRITICAL WARNING" : "GROUND OPERATOR · NO COCKPIT WARNINGS";
        }

        private string StatusFor(SpaceFeedState state, bool known)
        {
            string line = SpaceFeedRules.StatusLine(state, known);
            if (draft.Source == BirdKind.Radar && sar != null && sar.Phase != SarPhase.Idle && sar.Phase != SarPhase.Complete)
                line += sar.Phase == SarPhase.Collecting ? " · SAR COLLECTING " + Mathf.RoundToInt(sar.Progress * 100f) + "%" : " · SAR PROCESSING";
            return line;
        }

        // ---- Picture ---------------------------------------------------------------------------------------------------

        private void FillImage(SpaceFeedState state, bool known, float now)
        {
            view.ImageKind = FeedImageKind.None;
            view.Image = null;
            view.Refusal = "";
            view.Brackets.Clear();
            if (imager != null) imager.Visible = false;
            if (sar != null) sar.Visible = false;

            if (!known || !state.Active) { view.Refusal = "NEGATIVE: NO SPACE LINK — HOLD AN UPLINK SITE"; return; }
            if (state.Family == SpaceFamilyState.Dark) { view.Refusal = "NEGATIVE: SPACE OFFLINE — RESTORE AN UPLINK SITE"; return; }
            if (!GameManager.GetLocalPlayer(out Player player) || player == null || !manager.HasSpaceBird(player.HQ, draft.Source))
            {
                view.Refusal = draft.Source == BirdKind.Optical
                    ? "NEGATIVE: NO OPTICAL SATELLITE — SWITCH TO THE RADAR BIRD"
                    : "NEGATIVE: NO RADAR SATELLITE — SWITCH TO THE OPTICAL BIRD";
                return;
            }
            if (draft.Source == BirdKind.Optical) FillOptical(state, now);
            else FillRadar(state, now);
        }

        private bool TryAim(BirdKind bird, SpaceFeedState state, out GlobalPosition aim)
        {
            int index = IndexOf(draft.Selected);
            if (draft.Zoom > 0 && bird == BirdKind.Optical && index >= 0) { aim = new GlobalPosition(entries[index].X, 0f, entries[index].Z); return true; }
            if (bird == BirdKind.Optical && manager.CameraSerial > cameraBaseline) { aim = manager.CameraTarget; return true; }
            if (bird == BirdKind.Radar && manager.RadarScanSerial > radarBaseline) { aim = manager.RadarScanTarget; return true; }
            // Another pilot's look: centre on what the faction's reveals say.
            float x = 0f, z = 0f;
            int n = 0;
            for (int i = 0; i < state.Contacts.Count; i++)
            {
                if (state.Contacts[i].Source != bird) continue;
                x += state.Contacts[i].X; z += state.Contacts[i].Z; n++;
            }
            if (n > 0) { aim = new GlobalPosition(x / n, 0f, z / n); return true; }
            aim = default;
            return false;
        }

        private void FillOptical(SpaceFeedState state, float now)
        {
            if (!TryAim(BirdKind.Optical, state, out GlobalPosition aim))
            {
                view.Refusal = "NO CAMERA LOOK YET — FIRE SAT CAMERA AT THE MAP, OR WAIT FOR A REVEAL";
                return;
            }
            EnsureImager();
            if (imager == null) { view.Refusal = "NEGATIVE: CAMERA UNAVAILABLE — REOPEN THE FEED"; return; }
            bool haveSky = SpaceSky.TrySample(aim, out WeatherViewSample sky);
            imager.SetSky(haveSky, sky);
            if (imager.Verdict != OpticalVerdict.Ok)
            {
                // A refused camera shows words, never the last frame (the imager has blanked its target).
                view.Refusal = imager.Words;
                return;
            }
            float radius = SpaceFeedRules.OpticalRadius(settings.OpticalSceneRadius.Value, sky);
            float footprint = SpaceFeedRules.Footprint(draft.Zoom, radius);
            Vector3 aimLocal = aim.ToLocalPosition();
            if (SupportTargeting.TryMapPoint(aim, out Vector3 ground)) aimLocal = ground;
            imager.Visible = true;
            imager.SetApprovedContacts(state.Contacts);
            imager.Aim(aimLocal, SatelliteSky.LineOfSight(BirdKind.Optical), Vector3.forward, footprint, WindowOpen ? 8f : 4f);
            view.ImageKind = FeedImageKind.Optical;
            view.Image = imager.Output;
            view.ImageAspect = OpticalWidth / OpticalHeight;
            IReadOnlyList<OpticalBracket> found = imager.Brackets;
            for (int i = 0; i < found.Count && view.Brackets.Count < SpaceFeedView.MaxBrackets; i++)
                view.Brackets.Add(new FeedBracketView
                {
                    Id = found[i].Id, U = found[i].Uv.x, V = found[i].Uv.y, Class = found[i].Class, Label = found[i].Label,
                    Marked = MarkedId(found[i].Id), Selected = found[i].Id == draft.Selected
                });
        }

        private void FillRadar(SpaceFeedState state, float now)
        {
            if (manager.RadarScanSerial > radarBaseline && manager.RadarScanSerial != sarSerial) BeginSar(manager.RadarScanTarget, manager.RadarScanSerial, now);
            if (sar == null || sar.Phase == SarPhase.Idle)
            {
                view.Refusal = "NO RADAR PRODUCT YET — FIRE RADAR SCAN OR MTI SWEEP AT THE MAP";
                return;
            }
            sar.Visible = true;
            for (int i = 0; i < state.Contacts.Count; i++)
                if (state.Contacts[i].Source == BirdKind.Radar) sar.AddApprovedContact(state.Contacts[i]);
            sar.Tick(now);
            if (sar.Image == null) { view.Refusal = "NEGATIVE: RADAR PRODUCT UNAVAILABLE — FIRE RADAR SCAN AGAIN"; return; }
            view.ImageKind = FeedImageKind.Sar;
            view.Image = sar.Image;
            view.ImageAspect = SarCollector.ImageWidth / (float)SarCollector.ImageHeight;
            if (sar.Phase == SarPhase.Complete)
                for (int i = 0; i < state.Contacts.Count && view.Brackets.Count < SpaceFeedView.MaxBrackets; i++)
                {
                    FeedContact c = state.Contacts[i];
                    if (c.Source != BirdKind.Radar || c.Expires <= now || !sar.TryProject(c, out Vector2 uv)) continue;
                    view.Brackets.Add(new FeedBracketView
                    {
                        Id = c.Id, U = uv.x, V = uv.y, Class = c.Class, Label = SpaceFeedRules.ContactLabel(c.Class, c.Percent),
                        Marked = MarkedId(c.Id), Selected = c.Id == draft.Selected
                    });
                }
        }

        private void BeginSar(GlobalPosition target, int serial, float now)
        {
            if (sar == null) sar = new SarCollector();
            Vector3 los = SatelliteSky.LineOfSight(BirdKind.Radar);
            double horizontal = Math.Sqrt((double)los.x * los.x + (double)los.z * los.z);
            double elevation = Math.Atan2(los.y, horizontal);
            double azX = horizontal > 1e-6 ? los.x / horizontal : 0.0, azZ = horizontal > 1e-6 ? los.z / horizontal : 1.0;
            double slant = RadarAltitude / Math.Max(0.2, Math.Sin(elevation));
            var look = new LookAngles(true, elevation, 0.0, slant, azX, azZ);
            var orbit = new OrbitState(OrbitPhase.InPass, new PassPlan(0.0), RadarAltitude, 0.0, 0.0, 0.0, 0.0);
            sar.Begin(target, look, orbit, settings.SarSceneRadius.Value, serial, now);
            sarSerial = serial;
        }

        private void EnsureImager()
        {
            if (imager != null || Application.isBatchMode) return;
            imager = SatelliteImager.Create((int)OpticalWidth, (int)OpticalHeight);
        }

        private bool MarkedId(int id)
        {
            var marks = manager.SpaceMirror.State.Marks;
            for (int i = 0; i < marks.Count; i++) if (marks[i].Id == id) return true;
            return false;
        }

        // ---- Tiles, actions, cards, words -----------------------------------------------------------------------------

        private void FillTiles()
        {
            int first = draft.Page * SpaceFeedRules.ContactsPerPage;
            for (int i = 0; i < view.Tiles.Length; i++)
            {
                int at = first + i;
                if (at >= entries.Count) { view.Tiles[i] = default; continue; }
                FeedEntry e = entries[at];
                view.Tiles[i] = new FeedTileView
                {
                    Present = true, Id = e.Id, Class = e.Class, Marked = e.Marked, Selected = e.Id == draft.Selected,
                    Title = SpaceFeedEntries.Title(e), Sub = SpaceFeedEntries.Sub(e)
                };
            }
        }

        /// <summary>
        /// With nothing revealed the tile row says how to get a contact: a RADAR SCAN, ready or when, or that SPACE is offline. It never
        /// names a target. The RADAR readiness is the host's deadline from the faction mirror, counted down on this clock.
        /// </summary>
        private void FillNoContacts(SpaceFeedState state, bool known, float now)
        {
            view.NoContacts = "";
            view.NoContactsTone = AvState.Inert;
            if (!known || !state.Active || !state.Feed || entries.Count > 0) return;
            int seconds = SpaceFeedRules.RadarReadySeconds(state.RadarReadyAt, now);
            bool unavailable = state.RadarReadyAt == SpaceWire.RadarUnavailable;
            view.NoContacts = SpaceFeedRules.NoContactsLine(state.Family, state.UplinksLive, seconds, unavailable);
            view.NoContactsTone = state.Family == SpaceFamilyState.Dark || state.UplinksLive <= 0 ? AvState.Danger
                : unavailable ? AvState.Caution : seconds <= 0 ? AvState.Ready : AvState.Caution;
        }

        private void FillActions(SpaceFeedState state, bool known)
        {
            bool linked = known && state.Active && state.Family != SpaceFamilyState.Dark;
            int index = IndexOf(draft.Selected);
            bool confirmable = linked && index >= 0 && SpaceFeedEntries.CanConfirm(entries[index]) && markRequest == 0;
            view.ConfirmFull = confirmable && state.LiveMarks >= SpaceWire.MaxMarks;
            view.CanConfirm = confirmable && !view.ConfirmFull;
            view.ConfirmHelp = view.ConfirmFull ? "12 of 12 MARKs held: SEND them or wait for one to expire."
                : index < 0 ? "Select a contact bracket or target first."
                : !SpaceFeedEntries.CanConfirm(entries[index]) ? "This target is already MARKed, or is a fixed MARK point."
                : "MARK the selected target. The host answers CONFIRMED, NEUTRAL, DECOY or FRIENDLY; only CONFIRMED can be posted.";
            view.SendCount = linked ? Math.Min(SpaceCommand.MaxIds, state.Marks.Count) : 0;
            view.CanSend = linked && state.Marks.Count > 0 && sendRequest == 0;
            view.SendHelp = state.Marks.Count == 0 ? "No live MARKs: CONFIRM a target first."
                : "Post your live MARKs as one TASKED call. The first pilot to claim it fires.";
        }

        private void FillPosts(SpaceFeedState state, float now)
        {
            List<FeedPost> posts = state.Posts;
            postOrder.Clear();
            for (int i = 0; i < posts.Count; i++)
            {
                int at = postOrder.Count;
                while (at > 0 && Before(posts[i], posts[postOrder[at - 1]], now)) at--;
                postOrder.Insert(at, i);
            }
            view.CardCount = Math.Min(view.Cards.Length, postOrder.Count);
            for (int i = 0; i < view.CardCount; i++) view.Cards[i] = Card(posts[postOrder[i]], state, now);
        }

        /// <summary>Open cards first, then launching, then stale; yours before others; soonest to expire first.</summary>
        private static bool Before(in FeedPost a, in FeedPost b, float now)
        {
            int ra = (int)SpaceFeedRules.PostStatusOf(a, now), rb = (int)SpaceFeedRules.PostStatusOf(b, now);
            if (ra != rb) return ra < rb;
            if (a.Own != b.Own) return a.Own;
            return a.Expires < b.Expires;
        }

        private FeedCardView Card(in FeedPost post, SpaceFeedState state, float now)
        {
            PostStatus status = SpaceFeedRules.PostStatusOf(post, now);
            int points = post.Points != null ? post.Points.Length : 0;
            string label = CallSheet.TryGet(post.Action, out CallRow row) ? row.Label : "TASKED";
            string title = label + " · " + points + " TARGET" + (points == 1 ? "" : "S") + (post.Payoff > 0 ? " · PAYOFF " + post.Payoff + " CR" : "");
            BirdKind source = points > 0 ? post.Points[0].Source : BirdKind.Radar;
            int left = Mathf.Max(0, Mathf.FloorToInt(post.Expires - now));
            string sub = (source == BirdKind.Optical ? "OPTICAL" : "RADAR") + " · " +
                (post.WatchOfficer ? "OVERLORD" : post.Own ? "YOUR CALL" : string.IsNullOrEmpty(post.Maker) ? "OPERATOR" : "OPERATOR " + post.Maker) + " · " +
                (left / 60) + ":" + (left % 60).ToString("00");
            var card = new FeedCardView
            {
                Present = true, PostId = post.CallId, Title = title, Sub = sub,
                Price = post.Price > 0 ? post.Price + " CR" : "FREE", Enabled = true
            };
            if (status == PostStatus.Stale) { card.State = "STALE"; card.Tone = AvState.Inert; card.Enabled = false; card.Price = ""; }
            else if (calls != null && calls.TaskedPending && calls.ClaimingPost == post.CallId)
            { card.State = post.Launching ? "LAUNCHING" : "PENDING"; card.Tone = AvState.Info; card.Enabled = false; }
            else if (status == PostStatus.Launching)
            { card.State = string.IsNullOrEmpty(post.Claimant) ? "LAUNCHING" : "LAUNCHING · " + post.Claimant; card.Tone = AvState.Info; card.Enabled = false; }
            else if (calls != null && calls.ArmedTasked == post.CallId)
            { card.State = "ARMED — PRESS AGAIN"; card.Tone = AvState.Caution; card.Armed = true; }
            else if (state.Gate != TaskedOutcome.None) { card.State = GateWord(state); card.Tone = AvState.Caution; }
            else if (post.Price > 0 && manager.LocalCredit + 0.001f < post.Price) { card.State = "NEED " + post.Price + " CR"; card.Tone = AvState.Danger; }
            else { card.State = SpaceFeedRules.PostWord(status); card.Tone = AvState.Ready; }
            return card;
        }

        private static string GateWord(SpaceFeedState state)
        {
            switch (state.Gate)
            {
                case TaskedOutcome.Cooldown: return "COOLDOWN";
                case TaskedOutcome.Frozen: return "CREDIT FROZEN";
                case TaskedOutcome.Locked: return "LOCKED";
                case TaskedOutcome.BirdBusy: return "BIRD BUSY";
                case TaskedOutcome.UplinkDown: return "UPLINK DOWN";
                case TaskedOutcome.LowCredit: return "LOW CREDIT";
                default: return "UNAVAILABLE";
            }
        }

        private void FillWords(float now)
        {
            string words = ""; AvState tone = AvState.Inert;
            float callAt = calls != null ? calls.LastWordsAt : -100f;
            bool callRecent = calls != null && calls.LastWords.Length > 0 && now - callAt < WordsHold;
            bool feedRecent = feedWords.Length > 0 && now - feedWordsAt < WordsHold;
            if (callRecent && (!feedRecent || callAt >= feedWordsAt)) { words = calls.LastWords; tone = ToneOf(words); }
            else if (feedRecent) { words = feedWords; tone = feedTone; }
            else if (calls != null && calls.Armed != null) { words = "ARMED CALL · PRESS ITS CALL AGAIN, OR RIGHT-CLICK THE MAP"; tone = AvState.Caution; }
            else if (IntentWords.IsKnown(manager.SpaceMirror.State.Intent)) { words = manager.SpaceMirror.State.Intent; tone = AvState.Info; } // the footer
            else { words = view.Ground ? "READY · CLICK A TARGET, CONFIRM, THEN SEND" : "READY · FLY ON: KEYBOARD AND JOYSTICK STAY LIVE"; tone = AvState.Ready; }
            view.Words = words;
            view.WordsTone = tone;
        }

        private static AvState ToneOf(string words) =>
            words.StartsWith("NEGATIVE", StringComparison.Ordinal) ? AvState.Danger
            : words.StartsWith("SHOT", StringComparison.Ordinal) || words.StartsWith("MARK CONFIRMED", StringComparison.Ordinal) ? AvState.Ready
            : words.StartsWith("ARMED", StringComparison.Ordinal) ? AvState.Caution : AvState.Info;

        private void Say(string words, AvState tone, AvUiCue cue)
        {
            feedWords = words ?? "";
            feedTone = tone;
            feedWordsAt = SupportManager.MissionNow();
            AvUiSound.Play(cue);
            dirty = true;
        }

        // ---- ISpaceFeedActions -----------------------------------------------------------------------------------------

        public void Touch()
        {
            draft.Touch(SupportManager.MissionNow());
            // Real input keeps the host's feed lease; the manager also keeps it alive, so one nudge every two seconds is plenty.
            if (manager != null && Time.unscaledTime >= nextActivity) { nextActivity = Time.unscaledTime + 2f; manager.SpaceFeedActivity(); }
        }

        public void SelectEntry(int id)
        {
            draft.Selected = IndexOf(id) >= 0 ? id : 0;
            dirty = true;
        }

        public void PrevPage() { draft.Page = Math.Max(0, draft.Page - 1); dirty = true; }
        public void NextPage() { draft.Page = SpaceFeedRules.ClampPage(draft.Page + 1, entries.Count); dirty = true; }

        public void SetSource(BirdKind source)
        {
            if (source != BirdKind.Optical && source != BirdKind.Radar) return;
            draft.Source = source;
            dirty = true;
        }

        public void CycleZoom() { draft.Zoom = (draft.Zoom + 1) % SpaceFeedRules.ZoomSteps; dirty = true; }

        public void Confirm()
        {
            int index = IndexOf(draft.Selected);
            if (manager == null || index < 0 || !SpaceFeedEntries.CanConfirm(entries[index]) || markRequest != 0) return;
            if (manager.SpaceMirror.State.LiveMarks >= SpaceWire.MaxMarks) { Say("NEGATIVE: 12 OF 12 MARKS HELD — SEND OR WAIT", AvState.Danger, AvUiCue.Caution); return; }
            int id = manager.SpaceMark(entries[index].Id);
            if (id <= 0) { Say("NEGATIVE: NO HOST LINK — WAIT FOR THE LINK, THEN PRESS AGAIN", AvState.Danger, AvUiCue.Caution); return; }
            markRequest = id; // registered before the verdict: in-process replies land on the next Update
            markAt = SupportManager.MissionNow();
            Say("MARK SENT — STAND BY FOR THE HOST", AvState.Info, AvUiCue.Press);
        }

        public void Send()
        {
            if (manager == null || sendRequest != 0) return;
            var marks = manager.SpaceMirror.State.Marks;
            int n = SpaceFeedEntries.SendIds(marks, draft.Selected, sendIds);
            if (n == 0) return;
            var ids = new int[n];
            Array.Copy(sendIds, ids, n);
            int id = manager.SpaceSend(ids);
            if (id <= 0) { Say("NEGATIVE: NO HOST LINK — WAIT FOR THE LINK, THEN PRESS AGAIN", AvState.Danger, AvUiCue.Caution); return; }
            sendRequest = id;
            sendAt = SupportManager.MissionNow();
            Say("SEND SENT — STAND BY FOR THE HOST", AvState.Info, AvUiCue.Press);
        }

        public void PressCard(int postId) => calls?.PressTasked(postId);

        public void ToggleFull()
        {
            if (WindowOpen) CloseWindow(FeedCloseReason.UserExit, quiet: false);
            else OpenWindow();
        }

        private void OnReply(SpaceReply reply)
        {
            if (reply.Kind == SpaceCommandKind.Mark && reply.RequestId == markRequest && markRequest != 0)
            {
                markRequest = 0;
                string words = SpaceFeedRules.MarkWords(reply.Verdict, reply.Replayed);
                bool good = reply.Verdict == MarkVerdict.Confirmed && !reply.Replayed;
                Say(words, good ? AvState.Ready : reply.Verdict == MarkVerdict.Neutral || reply.Verdict == MarkVerdict.Decoy || reply.Replayed ? AvState.Caution : AvState.Danger,
                    good ? AvUiCue.Confirm : AvUiCue.Caution);
            }
            else if (reply.Kind == SpaceCommandKind.SendTasked && reply.RequestId == sendRequest && sendRequest != 0)
            {
                sendRequest = 0;
                bool posted = reply.Tasked == TaskedOutcome.Posted;
                Say(SpaceFeedRules.SendWords(reply.Tasked, reply.Detail, reply.Replayed), posted && !reply.Replayed ? AvState.Ready : AvState.Danger,
                    posted && !reply.Replayed ? AvUiCue.Confirm : AvUiCue.Caution);
            }
        }

        // ---- Window ----------------------------------------------------------------------------------------------------

        private void OpenWindow()
        {
            if (manager == null) return;
            if (window == null) window = SpaceFeedWindow.Create(transform, this);
            if (window == null) return;
            float now = SupportManager.MissionNow();
            if (!draft.CanRestore(now) && !compactVisible) draft.Clear();
            draft.Touch(now);
            dirty = true;
            if (!window.Open(view)) Say("NEGATIVE: FEED UNAVAILABLE — SPAWN OR TAKE A SEAT", AvState.Danger, AvUiCue.Caution);
        }

        internal void CloseWindow(FeedCloseReason reason, bool quiet)
        {
            if (window == null || !window.IsOpen) return;
            window.Close(reason);
            if (!quiet) Say("FEED CLOSED · " + CloseWords(reason), AvState.Info, AvUiCue.Release);
        }

        /// <summary>The window has closed (by any path): the draft's two-minute restore clock starts.</summary>
        internal void OnWindowClosed(FeedCloseReason reason)
        {
            draft.Close(SupportManager.MissionNow());
            if (reason == FeedCloseReason.InvalidOperator) ClearView();
            dirty = true;
        }

        private static string CloseWords(FeedCloseReason reason)
        {
            switch (reason)
            {
                case FeedCloseReason.Idle: return "NO INPUT FOR 8 S";
                case FeedCloseReason.InvalidOwnship: return "AIRCRAFT LOST";
                case FeedCloseReason.FocusLost: return "WINDOW LOST FOCUS";
                case FeedCloseReason.InvalidOperator: return "NO OPERATOR OR FACTION CHANGED";
                default: return "CLOSED BY YOU";
            }
        }
    }
}
