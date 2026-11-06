using System;
using System.Collections.Generic;
using System.Globalization;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Core.Lifecycle;
using BoscaliSummer.Modules.Support.Configuration;
using BoscaliSummer.Modules.Support.Domain.C2;
using BoscaliSummer.Modules.Support.Domain.Space;
using BoscaliSummer.Modules.Support.Presentation.C2;
using BoscaliSummer.Modules.Support.Runtime;
using BoscaliSummer.Modules.Support.Visuals;
using NOAvionics;
using NuclearOption.Networking;
using UnityEngine;

namespace BoscaliSummer.Modules.Support.Presentation
{
    /// <summary>
    /// The one owner of the SPACE operator feed. It reads the faction mirror (never a client registry), drives the optical imager
    /// and the SAR collector and builds one <see cref="SpaceFeedView"/> for the ORBIT room of the SPACE front window (the
    /// <see cref="Fronts.FrontController"/> reads it). Every action is a request to the host through
    /// <see cref="SupportManager"/> and <see cref="CallsController"/>; nothing here spends, grants or decides.
    ///
    /// <para>In-process SPACE replies arrive on the next Update, so a request id is registered here as soon as the request call
    /// returns it. A MARK replay returns the historical verdict and is worded as history.</para>
    /// </summary>
    internal sealed class SpaceFeedController : MonoBehaviour, ISceneService, ISpaceFeedActions
    {
        private const float RefreshFull = 0.08f, RequestTimeout = 6f, WordsHold = 10f;
        private const float OpticalWidth = 640f, OpticalHeight = 400f;
        private const float RadarAltitude = 600000f, GeoAltitude = 35786000f;
        /// <summary>After an own rod lands the STRIKE view holds on the impact this long (mission seconds).</summary>
        private const float StrikeHold = 8f;

        private SupportManager manager;
        private CallsController calls;
        private SupportSettings settings;

        private readonly FeedDraft draft = new FeedDraft();
        private readonly SpaceFeedView view = new SpaceFeedView();
        private readonly List<FeedEntry> entries = new List<FeedEntry>(SpaceWire.MaxContacts + SpaceWire.MaxMarks);
        private readonly int[] sendIds = new int[SpaceCommand.MaxIds];

        private int pageRows = SpaceFeedLayout.Tiles6;
        private bool visible, leaseOpen, dirty;

        /// <summary>The OPS page's PERKS tab is on screen: hold the feed lease for its TASKED rows (posts arrive with the rows) without rebuilding the ORBIT view.</summary>
        internal bool PanelWatch { get; set; }
        private float nextRefresh, nextActivity, failedUntil;
        private int lastFaction;

        private SatelliteImager imager;
        private SarCollector sar;
        private DownlinkPost post;
        private readonly Vector2[] glowUv = new Vector2[DownlinkPost.MaxGlows];
        private int sarSerial, radarBaseline, cameraBaseline;
        private GlobalPosition sarTarget;
        private bool baselined;

        // A fresh look (another bird or mode, a new pick) restarts the resolve counter and centres the pan.
        private int acquireKey;
        private float acquireAt;

        private int markRequest, sendRequest, markTrack;
        private float markAt, sendAt;
        // SEND TO TASKED: the track whose MARK the press sent; the TRANSMIT follows once that mark is on the board.
        private int chainTrack;
        private float chainAt;

        // Telemetry: what changed since the last look at the mirrors. Baselined on the first sample, so nothing that happened before the room existed is reported as news.
        private const float StaleLookSeconds = 60f, PostedHold = 20f;
        private bool watching;
        private int cameraSeen, radarSeen;
        private float cameraAt = -1f, radarAt = -1f, postedAt = -100f;
        private readonly float[] departSeen = new float[3];
        private readonly int[] siteSeen = { -1, -1 };
        private int briefId;
        private float briefX, briefZ;
        private string briefGrid = "";
        private string feedWords = "";
        private AvState feedTone = AvState.Inert;
        private float feedWordsAt = -100f;
        /// <summary>The view the ORBIT room paints; rebuilt while <see cref="SetVisible"/> says the room is on screen.</summary>
        internal SpaceFeedView View => view;

        public void Configure(SupportManager manager, CallsController calls, SupportSettings settings)
        {
            this.manager = manager;
            this.calls = calls;
            this.settings = settings;
            if (manager != null) manager.SpaceReplied += OnReply;
        }

        /// <summary>The DOWNLINK is on screen: its view is current (a hidden feed's words are stale).</summary>
        internal bool Shown => visible;

        /// <summary>The SPACE room shows DOWNLINK (column tab or enlarged; FrontController follows the window): the feed lease is held, the imager renders and the view is rebuilt.</summary>
        internal void SetVisible(bool on)
        {
            if (visible == on) return;
            visible = on;
            float now = SupportManager.MissionNow();
            if (on) { if (!draft.CanRestore(now)) draft.Clear(); draft.Touch(now); }
            else draft.Close(now);
            dirty = true;
        }

        public void ResetForScene()
        {
            ClearView();
            leaseOpen = false;
            visible = false;
            PanelWatch = false;
            baselined = false;
            SpaceCockpitThreatProbe.ResetForScene();
            DisposeSensors();
        }

        private void OnDestroy()
        {
            if (manager != null) manager.SpaceReplied -= OnReply;
            DisposeSensors();
            SpaceCockpitThreatProbe.ResetForScene();
        }

        private void DisposeSensors()
        {
            if (imager != null) Destroy(imager.gameObject);
            imager = null;
            post?.Dispose();
            post = null;
            sar?.Dispose();
            sar = null;
            sarSerial = 0;
        }

        /// <summary>Faction change, scene reset or operator loss: nothing of the old view may be painted again.</summary>
        private void ClearView()
        {
            draft.Clear();
            entries.Clear();
            markRequest = sendRequest = chainTrack = 0;
            feedWords = "";
            feedWordsAt = -100f;
            watching = false;
            cameraAt = radarAt = -1f;
            postedAt = -100f;
            view.Log.Clear();
            view.Brief = default;
            view.Brackets.Clear();
            view.Image = null;
            view.PostTexture = null;
            view.HasLook = false;
            view.Strike = default;
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
                radarBaseline = radarSeen = manager.RadarScanSerial;
                cameraBaseline = cameraSeen = manager.CameraSerial;
            }
            WatchFaction();
            ExpireRequests(now);
            AdvanceChain(now);
            WatchEvents(now);

            bool wanted = visible || PanelWatch;
            leaseOpen = manager.SpaceFeedWanted; // a link or faction reset clears the manager's lease; follow it so the feed re-opens
            if (wanted && !leaseOpen) { manager.SpaceOpenFeed(); leaseOpen = true; }
            else if (!wanted && leaseOpen)
            {
                manager.SpaceCloseFeed();
                leaseOpen = false;
                if (imager != null) imager.Visible = false;
                if (sar != null) sar.Visible = false;
            }
            if (!visible) return; // a PERKS-tab lease only wants the posts, not the ORBIT view

            float wall = Time.unscaledTime;
            if (wall < failedUntil) return; // a refresh fault holds the feed off for a moment, however often the panel re-shows it
            if (!dirty && wall < nextRefresh)
            {
                try { sar?.Tick(now); }
                catch (Exception e) { FailFeed(e, wall); }
                return;
            }
            nextRefresh = wall + RefreshFull;
            dirty = false;
            try { Refresh(now); }
            catch (Exception e) { FailFeed(e, wall); }
        }

        private void FailFeed(Exception e, float wall)
        {
            failedUntil = wall + 2f; // back off for a moment, then the dirty retry below runs
            Plugin.Logger?.LogError("SPACE feed refresh failed: " + e);
            dirty = true; // stay visible: the failedUntil backoff above retries instead of killing the feed until reopen
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
        }

        private void ExpireRequests(float now)
        {
            if (markRequest != 0 && now - markAt > RequestTimeout) { markRequest = chainTrack = 0; Say("NEGATIVE: NO ANSWER — MARK AGAIN", AvState.Danger, AvUiCue.Caution); }
            if (sendRequest != 0 && now - sendAt > RequestTimeout) { sendRequest = 0; Say("NEGATIVE: NO ANSWER — CHECK THE BOARD", AvState.Danger, AvUiCue.Caution); }
        }

        // ---- Telemetry ---------------------------------------------------------------------------------------------------

        /// <summary>One real event onto the station log (newest first, bounded).</summary>
        private void Note(float at, string text, AvState tone)
        {
            view.Log.Insert(0, new FeedLogLine { Stamp = C2Words.Clock(at), Text = text, Tone = tone });
            if (view.Log.Count > SpaceFeedView.MaxLog) view.Log.RemoveAt(view.Log.Count - 1);
            dirty = true;
        }

        /// <summary>A burn begun (by anyone in the faction), a look heard, an uplink site standing or falling: each becomes a log line, from the mirrors the feed already reads.</summary>
        private void WatchEvents(float now)
        {
            if (!baselined) return;
            if (manager.CameraSerial != cameraSeen)
            {
                cameraSeen = manager.CameraSerial; cameraAt = now;
                Note(now, "OPTICAL LOOK · GRID " + Ops.OpsKit.Grid(manager.CameraTarget.x, manager.CameraTarget.z) + " · " + manager.CameraContacts + " CONTACTS", AvState.Info);
            }
            if (manager.RadarScanSerial != radarSeen)
            {
                radarSeen = manager.RadarScanSerial; radarAt = now;
                Note(now, "RADAR SCAN · GRID " + Ops.OpsKit.Grid(manager.RadarScanTarget.x, manager.RadarScanTarget.z) + " · " + manager.RadarScanContacts + " CONTACTS", AvState.Info);
            }
            var ops = manager.OpsMirror;
            if (!ops.Known || !ops.State.Active) return;
            bool first = !watching;
            watching = true;
            for (int k = 0; k < departSeen.Length; k++)
            {
                GeoBird g = ops.State.Geo[k];
                // The wire carries the burn as whole seconds since it began, so the client's DepartAt wobbles by about a second with every snapshot of the same burn (and a parked bird keeps its
                // last leg, Length > 0, for the rest of the mission): only a departure that moved by more than that wobble, or the first one seen, is a new burn.
                bool burnt = g.Length > 0f;
                if (!first && burnt && (float.IsNaN(departSeen[k]) || Math.Abs(g.DepartAt - departSeen[k]) > 3f))
                    Note(g.DepartAt, Fronts.FrontViews.BirdCallsign(k) + " BURN · -" + Mathf.RoundToInt(g.BurnCost) + " % FUEL · ETA " + C2Words.Clock(g.Duration), AvState.Caution);
                departSeen[k] = burnt ? g.DepartAt : float.NaN;
            }
            var sites = ops.State.Sites;
            for (int i = 0; i < siteSeen.Length; i++)
            {
                int state = -1;
                for (int j = 0; j < sites.Count; j++) if (sites[j].Slot == i) state = (int)sites[j].State;
                if (!first && state >= 0 && siteSeen[i] >= 0 && state != siteSeen[i])
                    Note(now, Fronts.FrontViews.SiteName(i) + (state == 0 ? " BACK UP" : state == 1 ? " DESTROYED" : " REBUILDING"), state == 0 ? AvState.Ready : state == 1 ? AvState.Danger : AvState.Caution);
                siteSeen[i] = state;
            }
        }

        // ---- Refresh ---------------------------------------------------------------------------------------------------

        private void Refresh(float now)
        {
            SpaceFeedMirror mirror = manager.SpaceMirror;
            SpaceFeedState state = mirror.State;
            bool known = mirror.Known;

            SpaceFeedEntries.Build(state.Contacts, state.Marks, now, entries);
            view.Pages = SpaceFeedRules.PageCount(entries.Count, pageRows);
            draft.Page = SpaceFeedRules.ClampPage(draft.Page, entries.Count, pageRows);
            // The rows only flow while DOWNLINK shows: a lock survives the trip to SATELLITES and back until the rows say it is gone.
            if (draft.Selected != 0 && known && state.Feed && IndexOf(draft.Selected) < 0) draft.Selected = 0;
            view.Page = draft.Page;
            view.SelectedId = draft.Selected;
            view.Source = draft.Source;
            view.Mode = draft.Mode;
            view.Zoom = draft.Zoom;
            view.ZoomEnabled = draft.Source != BirdKind.Radar;
            view.Link = SpaceFeedRules.LinkQuality(state.UplinksLive, state.UplinksTotal, state.Family == SpaceFamilyState.Degraded); // no jam flag reaches the client: degraded stands in
            Acquire(Time.unscaledTime);

            FillThreat();
            FillBirds(state, known, now);
            FillImage(state, known, now);
            view.ImageAge = -1f;
            view.ImageStale = false;
            float lookAt = draft.Source == BirdKind.Radar ? radarAt : draft.Source == BirdKind.Optical ? cameraAt : -1f;
            if (view.ImageKind != FeedImageKind.None && lookAt >= 0f)
            {
                view.ImageAge = Math.Max(0f, now - lookAt);
                view.ImageStale = view.ImageAge > StaleLookSeconds;
            }
            view.Status = StatusFor(state, known);
            if (view.Strike.Held) view.Status += " · HOLD · IMPACT";
            view.StatusTone = !known || !state.Active || state.Family == SpaceFamilyState.Dark ? AvState.Danger
                : state.Family == SpaceFamilyState.Degraded ? AvState.Caution : AvState.Info;
            FillTiles();
            FillBrief(now);
            FillNoContacts(state, known, now);
            FillActions(state, known);
            FillWords(now);
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

        /// <summary>
        /// The three constellation cells: each bird's real state from the faction mirror (NO LINK / NO BIRD / OFFLINE / the RADAR scan
        /// countdown / READY). The orbit art and the altitude figures beside them are cosmetic.
        /// </summary>
        private void FillBirds(SpaceFeedState state, bool known, float now)
        {
            bool linked = known && state.Active;
            bool haveLocal = GameManager.GetLocalPlayer(out Player player) && player != null;
            int radarSeconds = SpaceFeedRules.RadarReadySeconds(state.RadarReadyAt, now);
            bool radarUnavailable = state.RadarReadyAt == SpaceWire.RadarUnavailable;
            for (int i = 0; i < view.Birds.Length; i++)
            {
                var bird = (BirdKind)i;
                bool has = linked && haveLocal && manager.HasSpaceBird(player.HQ, bird);
                string word = C2Orbit.BirdState(bird, linked, has, state.Family, radarSeconds, radarUnavailable, out C2Tone tone);
                view.Birds[i] = new FeedBirdView { State = word, Tone = C2Kit.StateOf(tone) };
            }
            view.ConstellationMeta = C2Orbit.ConstellationMeta(known, state.Active, state.UplinksLive, state.Family);
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
            view.PostTexture = null;
            view.Refusal = "";
            view.HasLook = false;
            view.Strike = default;
            view.Brackets.Clear();
            for (int i = 0; i < view.Telemetry.Length; i++) view.Telemetry[i] = "";
            if (imager != null) imager.Visible = false;
            if (sar != null) sar.Visible = false;

            if (!known || !state.Active) { view.Refusal = "NEGATIVE: NO SPACE LINK — BUILD A LAUNCH PAD"; return; }
            if (state.Family == SpaceFamilyState.Dark) { view.Refusal = "NEGATIVE: SPACE OFFLINE — BUILD A LAUNCH PAD"; return; }
            if (!GameManager.GetLocalPlayer(out Player player) || player == null || !manager.HasSpaceBird(player.HQ, draft.Source))
            {
                view.Refusal = "NEGATIVE: NO " + C2Orbit.BirdName(draft.Source) + " SATELLITE — LAUNCH ONE ON SATELLITES";
                return;
            }
            if (draft.Source == BirdKind.Radar) FillRadar(state, now);
            else FillOptical(state, now, player.HQ);
        }

        private bool TryAim(BirdKind bird, SpaceFeedState state, FactionHQ hq, float now, out GlobalPosition aim)
        {
            int index = IndexOf(draft.Selected);
            // STARE: zoomed in with a track selected, the look follows it.
            if (draft.Zoom > 1.01f && bird == BirdKind.Optical && index >= 0) { aim = new GlobalPosition(entries[index].X, 0f, entries[index].Z); return true; }
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
            return TryNadir(bird, hq, now, out aim); // nothing picked yet: the bird looks straight down
        }

        /// <summary>The ground point under one of our birds (its footprint centre), from the same geostationary slot the map draws.</summary>
        private bool TryNadir(BirdKind bird, FactionHQ hq, float now, out GlobalPosition aim)
        {
            aim = default;
            if (hq == null || !manager.TryOwnGeo(hq, (int)bird, out GeoBird geo)) return false;
            Fronts.MapFrame frame = Fronts.FrontController.Frame();
            aim = new GlobalPosition(frame.X(geo.U(now)), 0f, frame.Z(geo.V(now)));
            return true;
        }

        /// <summary>The newest own rod (ORBITAL ROD or STARFALL) still in the air or inside its hold after impact.</summary>
        private bool TryStrike(float now, out ActiveStrikeInfo strike)
        {
            strike = default;
            bool found = false;
            IReadOnlyList<ActiveStrikeInfo> list = manager.ActiveStrikes;
            for (int i = 0; i < list.Count; i++)
            {
                ActiveStrikeInfo s = list[i];
                if ((s.ActionId != SupportActionId.Artillery && s.ActionId != SupportActionId.Starfall) || now >= s.ImpactTime + StrikeHold) continue;
                if (!found || s.StartTime >= strike.StartTime) { strike = s; found = true; }
            }
            return found;
        }

        /// <summary>
        /// The OPTICAL bird (EO, IR, LOW LIGHT) and the KINETIC bird's STRIKE view: one imager re-aimed. The look is the aim plus the pan, clamped inside
        /// the imaged window; IR and LOW LIGHT (and STRIKE at night, as IR) go through <see cref="DownlinkPost"/> and may image at night, EO refuses.
        /// </summary>
        private void FillOptical(SpaceFeedState state, float now, FactionHQ hq)
        {
            bool strikeView = draft.Mode == DownlinkMode.Strike;
            bool haveStrike = TryStrike(now, out ActiveStrikeInfo strike);
            GlobalPosition aim;
            bool aimed;
            if (!strikeView) aimed = TryAim(BirdKind.Optical, state, hq, now, out aim);
            else if (haveStrike) { aim = strike.Target; aimed = true; } // the newest own rod, held 8 s past its impact
            else aimed = TryNadir(BirdKind.Kinetic, hq, now, out aim);
            if (!aimed)
            {
                view.Refusal = "NO CAMERA LOOK YET — FIRE SAT CAMERA AT THE MAP, OR WAIT FOR A REVEAL";
                return;
            }
            EnsureImager();
            if (imager == null || !imager.Ready) { view.Refusal = "NEGATIVE: CAMERA UNAVAILABLE — VIDEO MEMORY BUDGET FULL"; return; }
            bool haveSky = SpaceSky.TrySample(aim, out WeatherViewSample sky);
            DownlinkMode look = draft.Mode == DownlinkMode.Ir || draft.Mode == DownlinkMode.LowLight ? draft.Mode
                : strikeView && haveSky && sky.Night ? DownlinkMode.Ir : DownlinkMode.Eo;
            imager.NightCapable = look != DownlinkMode.Eo;
            imager.SetSky(haveSky, sky);
            if (imager.Output == null)
            {
                // A refused camera shows words, never the last frame (the imager has blanked its target).
                view.Refusal = imager.Verdict == OpticalVerdict.NightUnavailable ? "NEGATIVE: EO HAS NO PICTURE AT NIGHT — SWITCH TO IR OR LOW LIGHT" : imager.Words;
                return;
            }
            float radius = SpaceFeedRules.OpticalRadius(settings.OpticalSceneRadius.Value, sky);
            float footprint = SpaceFeedRules.Footprint(draft.Zoom, radius);
            bool stare = !strikeView && draft.Zoom > 1.01f && IndexOf(draft.Selected) >= 0; // a locked track stays centred
            (draft.PanX, draft.PanZ) = SpaceFeedRules.ClampPan(draft.PanX, draft.PanZ, stare ? 0f : SpaceFeedRules.PanReach(draft.Zoom, radius));
            var lookAt = new GlobalPosition(aim.x + draft.PanX, 0f, aim.z + draft.PanZ);
            Vector3 aimLocal = lookAt.ToLocalPosition();
            if (SupportTargeting.TryMapPoint(lookAt, out Vector3 ground)) aimLocal = ground;
            BirdKind bird = strikeView ? BirdKind.Kinetic : BirdKind.Optical;
            imager.Visible = true;
            imager.SetApprovedContacts(state.Contacts);
            imager.Aim(aimLocal, SatelliteSky.LineOfSight(bird), Vector3.forward, footprint, 8f);
            Look(lookAt, footprint);
            FillTelemetry(bird, footprint, haveSky, sky);
            view.ImageKind = FeedImageKind.Optical;
            view.ImageAspect = OpticalWidth / OpticalHeight;
            IReadOnlyList<OpticalBracket> found = imager.Brackets;
            int glows = 0;
            for (int i = 0; i < found.Count && view.Brackets.Count < SpaceFeedView.MaxBrackets; i++)
            {
                view.Brackets.Add(new FeedBracketView
                {
                    Id = found[i].Id, U = found[i].Uv.x, V = found[i].Uv.y, Class = found[i].Class, Label = Tagged(found[i].Id, found[i].Label),
                    Marked = MarkedId(found[i].Id), Selected = found[i].Id == draft.Selected
                });
                glowUv[glows++] = found[i].Uv;
            }
            if (haveStrike)
            {
                Vector3 impact = strike.Target.ToLocalPosition();
                if (SupportTargeting.TryMapPoint(strike.Target, out Vector3 impactGround)) impact = impactGround;
                bool onPicture = imager.TryProject(impact, out Vector2 uv);
                bool held = now >= strike.ImpactTime;
                if (strikeView) view.Strike = new FeedStrikeView { Has = true, Held = held, OnPicture = onPicture, Eta = Math.Max(0f, strike.ImpactTime - now), Radius = strike.Radius, U = uv.x, V = uv.y };
                if (held && onPicture && glows < glowUv.Length) glowUv[glows++] = uv; // the impact is hot in IR
            }
            if (look == DownlinkMode.Eo) { view.Image = imager.Output; return; }
            if (post == null) post = new DownlinkPost();
            Texture picture = post.Process(imager.Output, look, SpaceFeedRules.ResolveBands(view.Link), view.Packets / (float)SpaceFeedRules.ResolvePackets, glowUv, glows);
            if (picture == null)
            {
                view.Refusal = post.Words.Length > 0 ? post.Words : SpaceFeedRules.ModeWord(look) + " PROCESSING — STAND BY";
                view.Brackets.Clear();
                return;
            }
            view.Image = view.PostTexture = picture;
        }

        /// <summary>The look box on the theatre map: its centre and width.</summary>
        private void Look(GlobalPosition at, float widthMetres)
        {
            Fronts.MapFrame frame = Fronts.FrontController.Frame();
            view.HasLook = true;
            view.LookU = frame.U(at.x);
            view.LookV = frame.V(at.z);
            view.LookKm = widthMetres / 1000f;
        }

        private static readonly string[] Points = { "N", "NE", "E", "SE", "S", "SW", "W", "NW" };

        /// <summary>
        /// The burn-in numbers, all derived from what the client already has: ground sample distance off the framed width, slant range and elevation off
        /// the bird's line of sight, the sun's height and the way shadows fall off the scene's sun, and the cloud word off the sky sample.
        /// </summary>
        private void FillTelemetry(BirdKind bird, float footprint, bool haveSky, in WeatherViewSample sky)
        {
            bool sar = bird == BirdKind.Radar;
            Vector3 los = SatelliteSky.LineOfSight(bird);
            float elevation = Mathf.Asin(Mathf.Clamp(los.y, -1f, 1f));
            float slantKm = (sar ? RadarAltitude : GeoAltitude) / 1000f / Mathf.Max(0.2f, Mathf.Sin(elevation));
            string[] t = view.Telemetry;
            t[0] = sar ? "RES 1.0 M" : "GSD " + (footprint / OpticalWidth).ToString("0.00", CultureInfo.InvariantCulture) + " M";
            t[1] = "SLANT " + Mathf.RoundToInt(slantKm).ToString(CultureInfo.InvariantCulture) + " KM";
            t[2] = "ELEV " + Mathf.RoundToInt(elevation * Mathf.Rad2Deg) + "°";
            LevelInfo level = NetworkSceneSingleton<LevelInfo>.i;
            Light sun = level != null && level.sun != null ? level.sun : RenderSettings.sun;
            if (sun != null)
            {
                Vector3 f = sun.transform.forward; // the light's travel: shadows fall this way
                float height = Mathf.Asin(Mathf.Clamp(-f.y, -1f, 1f)) * Mathf.Rad2Deg;
                t[3] = height <= 0f ? "SUN DOWN" : "SUN " + Mathf.RoundToInt(height) + "° · SHADOW " + Points[Mathf.RoundToInt(((Mathf.Atan2(f.x, f.z) * Mathf.Rad2Deg + 360f) % 360f) / 45f) % 8];
            }
            if (sar) { t[4] = "ALL WEATHER"; return; }
            if (!haveSky) { t[4] = "SKY UNKNOWN"; return; }
            float cover = SpaceFeedRules.Softness(sky);
            t[4] = (cover < 0.15f ? "CLEAR" : cover < 0.45f ? "SCATTERED" : cover < 0.8f ? "BROKEN" : "OVERCAST") + (sky.Rain01 > 0.2f ? " · RAIN" : "") + (sky.Night ? " · NIGHT" : "");
        }

        private void FillRadar(SpaceFeedState state, float now)
        {
            if (manager.RadarScanSerial > radarBaseline && manager.RadarScanSerial != sarSerial) BeginSar(manager.RadarScanTarget, manager.RadarScanSerial, now);
            if (sar == null || sar.Phase == SarPhase.Idle)
            {
                view.Refusal = "NO RADAR PRODUCT YET — FIRE RECON PASS AT THE MAP";
                return;
            }
            sar.Visible = true;
            for (int i = 0; i < state.Contacts.Count; i++)
                if (state.Contacts[i].Source == BirdKind.Radar) sar.AddApprovedContact(state.Contacts[i]);
            sar.Tick(now);
            if (sar.Image == null) { view.Refusal = "NEGATIVE: RADAR PRODUCT UNAVAILABLE — FIRE RECON PASS AGAIN"; return; }
            view.ImageKind = FeedImageKind.Sar;
            view.Image = sar.Image;
            view.ImageAspect = SarCollector.ImageWidth / (float)SarCollector.ImageHeight;
            Look(sarTarget, 2f * settings.SarSceneRadius.Value);
            FillTelemetry(BirdKind.Radar, 0f, false, default);
            if (sar.Phase == SarPhase.Complete)
                for (int i = 0; i < state.Contacts.Count && view.Brackets.Count < SpaceFeedView.MaxBrackets; i++)
                {
                    FeedContact c = state.Contacts[i];
                    if (c.Source != BirdKind.Radar || c.Expires <= now || !sar.TryProject(c, out Vector2 uv)) continue;
                    view.Brackets.Add(new FeedBracketView
                    {
                        Id = c.Id, U = uv.x, V = uv.y, Class = c.Class, Label = Tagged(c.Id, SpaceFeedRules.ContactLabel(c.Class, c.Percent)),
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
            sar.Begin(target, Math.PI * 0.5 - elevation, azX, azZ, slant, RadarAltitude,
                settings.SarSceneRadius.Value, serial, now);
            sarSerial = serial;
            sarTarget = target;
        }

        /// <summary>
        /// A fresh look (another mode or bird, a new pick or lock, a new own rod) restarts the resolve and centres the pan; the packet counter then runs
        /// to 256 over the link's resolve time.
        /// </summary>
        private void Acquire(float wall)
        {
            float now = SupportManager.MissionNow();
            int pick = draft.Source == BirdKind.Radar ? manager.RadarScanSerial : draft.Source == BirdKind.Optical ? manager.CameraSerial
                : TryStrike(now, out ActiveStrikeInfo s) ? s.RequestId : 0;
            int key = unchecked(((int)draft.Mode * 397 + draft.Selected) * 397 + pick);
            if (key != acquireKey) { acquireKey = key; acquireAt = wall; draft.PanX = draft.PanZ = 0f; }
            float t = Mathf.Clamp01((wall - acquireAt) / SpaceFeedRules.ResolveSeconds(view.Link));
            view.Packets = Mathf.RoundToInt(t * SpaceFeedRules.ResolvePackets);
        }

        /// <summary>SEND TO TASKED's second half: once the press's MARK shows on the board, TRANSMIT; a mark that never shows ends the chain with words.</summary>
        private void AdvanceChain(float now)
        {
            if (chainTrack == 0 || markRequest != 0) return; // still waiting for the verdict
            if (MarkedId(chainTrack)) { chainTrack = 0; Send(); return; }
            if (now - chainAt > RequestTimeout * 2f) { chainTrack = 0; Say("NEGATIVE: MARK NOT ON THE BOARD — PRESS SEND AGAIN", AvState.Danger, AvUiCue.Caution); }
        }

        private void EnsureImager()
        {
            if (imager != null || Application.isBatchMode) return;
            imager = SatelliteImager.Create((int)OpticalWidth, (int)OpticalHeight);
        }

        /// <summary>The selected bracket wears the same id as its row and the brief (T12 HOSTILE 94%).</summary>
        private string Tagged(int id, string label) => id == draft.Selected ? C2Orbit.TrackId(id) + " " + label : label;

        private bool MarkedId(int id)
        {
            var marks = manager.SpaceMirror.State.Marks;
            for (int i = 0; i < marks.Count; i++) if (marks[i].Id == id) return true;
            return false;
        }

        // ---- Tiles, actions, cards, words -----------------------------------------------------------------------------

        private void FillTiles()
        {
            int first = draft.Page * pageRows;
            for (int i = 0; i < view.Tiles.Length; i++)
            {
                int at = first + i;
                if (i >= pageRows || at >= entries.Count) { view.Tiles[i] = default; continue; }
                FeedEntry e = entries[at];
                view.Tiles[i] = new FeedTileView
                {
                    Present = true, Id = e.Id, Class = e.Class, Marked = e.Marked, Selected = e.Id == draft.Selected,
                    Percent = e.Percent, Moving = e.Moving, FixedPoint = e.Kind == FeedEntryKind.Mark,
                    Title = SpaceFeedEntries.Title(e), Sub = SpaceFeedEntries.Sub(e)
                };
            }
        }

        /// <summary>The selected track read once, from the entry its row and bracket also come from (the grid name is rebuilt only when the point moves).</summary>
        private void FillBrief(float now)
        {
            int index = IndexOf(draft.Selected);
            if (index < 0) { view.Brief = default; return; }
            FeedEntry e = entries[index];
            if (briefId != e.Id || briefX != e.X || briefZ != e.Z) { briefId = e.Id; briefX = e.X; briefZ = e.Z; briefGrid = Ops.OpsKit.Grid(e.X, e.Z); }
            view.Brief = new FeedBriefView
            {
                Has = true, Id = e.Id, Class = e.Class, Percent = e.Percent, Moving = e.Moving, Marked = e.Marked, FixedPoint = e.Kind == FeedEntryKind.Mark,
                Title = SpaceFeedEntries.Title(e), Grid = briefGrid, Left = e.Expires - now
            };
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
            view.ConfirmHelp = view.ConfirmFull ? "12 of 12 MARKs held: TRANSMIT them or wait for one to expire."
                : index < 0 ? "Select a contact bracket or target first."
                : !SpaceFeedEntries.CanConfirm(entries[index]) ? "This target is already MARKed, or is a fixed MARK point."
                : "MARK the selected target. The host answers CONFIRMED, NEUTRAL, DECOY or FRIENDLY; only CONFIRMED can be posted.";
            view.SendCount = linked ? Math.Min(SpaceCommand.MaxIds, state.Marks.Count) : 0;
            view.CanSend = linked && state.Marks.Count > 0 && sendRequest == 0;
            view.SendHelp = state.Marks.Count == 0 ? "No live MARKs: CONFIRM a target first."
                : "Post your live MARKs as one TASKED call to the board. The first pilot to claim it fires.";
        }

        private void FillWords(float now)
        {
            string words = ""; AvState tone = AvState.Inert;
            view.Posted = now - postedAt < PostedHold;
            float callAt = calls != null ? calls.LastWordsAt : -100f;
            bool callRecent = calls != null && calls.LastWords.Length > 0 && now - callAt < WordsHold;
            bool feedRecent = feedWords.Length > 0 && now - feedWordsAt < WordsHold;
            if (callRecent && (!feedRecent || callAt >= feedWordsAt)) { words = calls.LastWords; tone = ToneOf(words); }
            else if (feedRecent) { words = feedWords; tone = feedTone; }
            else if (calls != null && calls.Armed != null) { words = "ARMED CALL · PRESS ITS CALL AGAIN, OR RIGHT-CLICK THE MAP"; tone = AvState.Caution; }
            else if (IntentWords.IsKnown(manager.SpaceMirror.State.Intent)) { words = manager.SpaceMirror.State.Intent; tone = AvState.Info; } // the footer
            else { words = view.Ground ? "READY · CLICK A TARGET, CONFIRM, THEN TRANSMIT" : "READY · FLY ON: KEYBOARD AND JOYSTICK STAY LIVE"; tone = AvState.Ready; }
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
        public void NextPage() { draft.Page = SpaceFeedRules.ClampPage(draft.Page + 1, entries.Count, pageRows); dirty = true; }

        /// <summary>A bird's feed: OPTICAL keeps its EO / IR / LOW LIGHT choice, RADAR is the SAR strip, KINETIC the STRIKE view.</summary>
        public void SetSource(BirdKind source)
        {
            if (source == draft.Source) { Changed(); return; }
            SetMode(source == BirdKind.Radar ? DownlinkMode.Sar : source == BirdKind.Kinetic ? DownlinkMode.Strike : DownlinkMode.Eo);
        }

        public void SetMode(DownlinkMode mode)
        {
            if (mode > DownlinkMode.Strike) return;
            draft.Mode = mode;
            Changed();
        }

        /// <summary>
        /// A choice made while the feed is hidden (FEED › on SATELLITES picks a bird before the column shows DOWNLINK) counts as a recent close, so
        /// the coming SetVisible keeps it instead of starting fresh.
        /// </summary>
        private void Changed()
        {
            if (!visible) draft.Close(SupportManager.MissionNow());
            dirty = true;
        }

        public void CycleZoom() => SetZoom(SpaceFeedRules.NextZoomStep(draft.Zoom));

        public void SetZoom(float zoom) { draft.Zoom = SpaceFeedRules.ClampZoom(zoom); dirty = true; }

        /// <summary>The look moves by metres east / north; the next refresh clamps it inside the imaged window (and a STARE lock keeps it centred).</summary>
        public void Pan(float dx, float dz)
        {
            if (!float.IsFinite(dx) || !float.IsFinite(dz)) return;
            draft.PanX += dx; draft.PanZ += dz;
            dirty = true;
        }

        public void ResetLook() { draft.PanX = draft.PanZ = 0f; dirty = true; }

        /// <summary>
        /// One press, one MARK and one TRANSMIT: the selected target is MARKed first when it is not yet, and AdvanceChain sends once the mark shows
        /// on the board; an already marked selection (or none, with marks held) transmits at once. A press while either is in flight does nothing.
        /// </summary>
        public void SendToTasked()
        {
            if (manager == null || chainTrack != 0 || markRequest != 0 || sendRequest != 0) return;
            int index = IndexOf(draft.Selected);
            if (index >= 0 && SpaceFeedEntries.CanConfirm(entries[index]))
            {
                Confirm();
                if (markRequest != 0) { chainTrack = markTrack; chainAt = SupportManager.MissionNow(); }
                return;
            }
            if (manager.SpaceMirror.State.Marks.Count == 0) { Say("NEGATIVE: SELECT A TARGET FIRST", AvState.Danger, AvUiCue.Caution); return; }
            Send();
        }

        public void Confirm()
        {
            int index = IndexOf(draft.Selected);
            if (manager == null || index < 0 || !SpaceFeedEntries.CanConfirm(entries[index]) || markRequest != 0) return;
            if (manager.SpaceMirror.State.LiveMarks >= SpaceWire.MaxMarks) { Say("NEGATIVE: 12 OF 12 MARKS HELD — TRANSMIT OR WAIT", AvState.Danger, AvUiCue.Caution); return; }
            int id = manager.SpaceMark(entries[index].Id);
            if (id <= 0) { Say("NEGATIVE: NO HOST LINK — WAIT FOR THE LINK, THEN PRESS AGAIN", AvState.Danger, AvUiCue.Caution); return; }
            markRequest = id; // registered before the verdict: in-process replies land on the next Update
            markTrack = entries[index].Id;
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
            Say("TRANSMIT SENT — STAND BY FOR THE HOST", AvState.Info, AvUiCue.Press);
        }


        private void OnReply(SpaceReply reply)
        {
            if (reply.Kind == SpaceCommandKind.Mark && reply.RequestId == markRequest && markRequest != 0)
            {
                markRequest = 0;
                if (reply.Verdict != MarkVerdict.Confirmed) chainTrack = 0; // only a confirmed mark goes on to TRANSMIT
                string words = SpaceFeedRules.MarkWords(reply.Verdict, reply.Replayed);
                bool good = reply.Verdict == MarkVerdict.Confirmed && !reply.Replayed;
                AvState markTone = good ? AvState.Ready : reply.Verdict == MarkVerdict.Neutral || reply.Verdict == MarkVerdict.Decoy || reply.Replayed ? AvState.Caution : AvState.Danger;
                Say(words, markTone, good ? AvUiCue.Confirm : AvUiCue.Caution);
                if (!reply.Replayed)
                    Note(SupportManager.MissionNow(), "MARK " + C2Orbit.TrackId(markTrack) + " · " + (reply.Verdict == MarkVerdict.Confirmed ? "CONFIRMED" : reply.Verdict == MarkVerdict.Neutral ? "NEUTRAL" :
                        reply.Verdict == MarkVerdict.Decoy ? "DECOY" : reply.Verdict == MarkVerdict.Friendly ? "FRIENDLY" : "REFUSED"), markTone);
            }
            else if (reply.Kind == SpaceCommandKind.SendTasked && reply.RequestId == sendRequest && sendRequest != 0)
            {
                sendRequest = 0;
                bool posted = reply.Tasked == TaskedOutcome.Posted;
                if (!reply.Replayed)
                {
                    if (posted) postedAt = SupportManager.MissionNow();
                    Note(SupportManager.MissionNow(), posted ? "TRANSMIT · PACKAGE POSTED TO TASKED" : "TRANSMIT REFUSED · " + C2Words.Fit(TaskedWords.Of(reply.Tasked, reply.Detail), 36), posted ? AvState.Ready : AvState.Danger);
                }
                Say(SpaceFeedRules.SendWords(reply.Tasked, reply.Detail, reply.Replayed), posted && !reply.Replayed ? AvState.Ready : AvState.Danger,
                    posted && !reply.Replayed ? AvUiCue.Confirm : AvUiCue.Caution);
            }
        }

    }
}
