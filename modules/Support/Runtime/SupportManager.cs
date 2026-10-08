using System;
using System.Collections;
using System.Collections.Generic;
using BepInEx.Configuration;
using BepInEx.Logging;
using BoscaliSummer.Modules.Support.Configuration;
using BoscaliSummer.Modules.Support.Domain.Calls;
using BoscaliSummer.Modules.Support.Domain.Fronts;
using BoscaliSummer.Modules.Support.Domain.Space;
using BoscaliSummer.Modules.Support.Networking;
using BoscaliSummer.Modules.Support.Presentation;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Core.Services;
using BoscaliSummer.Core.Lifecycle;
using BoscaliSummer.Interop;
using BoscaliSummer.Core.Game;
using NuclearOption.Networking;
using UnityEngine;

namespace BoscaliSummer.Modules.Support.Runtime
{
    /// <summary>
    /// Validates and dispatches support requests. Everything an action does lives in the
    /// action; this class owns only authority, economy, bounded concurrency and the client's
    /// view of its own request.
    /// </summary>
        internal sealed partial class SupportManager : MonoBehaviour, ISceneService, ISupportHost, ICameraTargetService,
            IGroundForceReadiness, ITheaterStrikePicture
        {
        private const int MaximumStrikeJobs = 2;
        private const int RequestsPerSecond = 2;
        private const int MaximumContactReplies = 16;

        /// <summary>Release travel, in pixels, still read as a click rather than a map drag.</summary>
        private const float ClickSlopPixels = 8f;

        /// <summary>How long a client waits for a reply before reporting the host silent.</summary>
        private const float ReplyTimeout = CallRequestTracker.TimeoutSeconds;

        /// <summary>Concurrent strike jobs per faction, per <see cref="SupportPool"/>.</summary>
        private readonly Dictionary<FactionHQ, int[]> strikeJobs = new Dictionary<FactionHQ, int[]>();
        private readonly int[] fallbackJobs = new int[2];
        private const int MaximumFactionPools = 8;

        private readonly SupportRequestLedger ledger = new SupportRequestLedger();
        private readonly SupportMapGesture mapGesture = new SupportMapGesture();
        private readonly Dictionary<int, int> contactReplies = new Dictionary<int, int>();
        private readonly Dictionary<int, float> ttiReplies = new Dictionary<int, float>();
        private CruiseTasking cruiseTasking;
        private readonly Dictionary<int, CruiseLegMirror> cruiseLegs = new Dictionary<int, CruiseLegMirror>();
        private int pendingWaypoint = -1;
        private float pendingWaypointUntil;

        /// <summary>The live manager, or null; set in Configure, cleared only on destroy (survives scene resets).</summary>
        internal static SupportManager Active { get; private set; }

        private SupportSettings settings;
        private IPlayerPerks perks;
        private SupportNet network;
        private IFrontReadiness readiness = new DefaultFrontReadiness();
        private SpaceService space;
        private int sceneGeneration = 1;
        private ManualLogSource logger;
        private ConfigEntry<bool> bypassRequirements;
        private ConfigEntry<bool> disableCooldowns;
        private SupportCatalog catalog;
        private readonly VanillaSupportCatalog vanilla = new VanillaSupportCatalog();

        private int nextRequestId;
        private float pendingSince;
        private bool pending;
        /// <summary>Per perk: the mission time the local pilot may use it again (the host sent the length with the accepted reply).</summary>
        private readonly Dictionary<SupportActionId, float> localCooldownUntil = new Dictionary<SupportActionId, float>();

        private string inboundStrikeName;
        private float inboundStrikeImpactTime;
        private float inboundStrikeConfirmedUntil;
        private string statusText = "Designate a grid on the maximised map.";

        private SupportActionId pendingAction;
        private readonly List<ActiveStrikeInfo> activeStrikes = new List<ActiveStrikeInfo>(8);

        private Action<GlobalPosition> localPick;

        public bool TryGetNear(float x, float z, float vicinity,
            out string label, out float secondsToImpact)
        {
            label = null;
            secondsToImpact = 0f;
            if (float.IsNaN(x) || float.IsNaN(z) || float.IsNaN(vicinity) ||
                float.IsInfinity(x) || float.IsInfinity(z) || float.IsInfinity(vicinity) ||
                vicinity < 0f)
                return false;
            float now = MissionNow();
            float soonest = float.MaxValue;
            for (int i = 0; i < activeStrikes.Count; i++)
            {
                ActiveStrikeInfo strike = activeStrikes[i];
                if (!strike.IsActive(now) ||
                    (strike.ActionId != SupportActionId.Artillery &&
                     strike.ActionId != SupportActionId.FlareMissile &&
                     strike.ActionId != SupportActionId.Emp &&
                     strike.ActionId != SupportActionId.Prsm &&
                     strike.ActionId != SupportActionId.Cruise)) continue;
                GlobalPosition point = strike.Target;
                if (float.IsNaN(point.x) || float.IsNaN(point.z) ||
                    float.IsInfinity(point.x) || float.IsInfinity(point.z) ||
                    float.IsNaN(strike.Radius) || float.IsInfinity(strike.Radius)) continue;
                float dx = point.x - x, dz = point.z - z;
                float reach = Mathf.Max(vicinity, strike.Radius);
                if (dx * dx + dz * dz > reach * reach) continue;
                float eta = strike.SecondsRemaining(now);
                if (eta >= soonest) continue;
                soonest = eta;
                secondsToImpact = eta;
                label = strike.ActionId == SupportActionId.FlareMissile ? "DECOY BARRAGE"
                    : strike.ActionId == SupportActionId.Artillery ? "KINETIC ROD"
                    : strike.ActionId == SupportActionId.Prsm ? "PRSM"
                    : strike.ActionId == SupportActionId.Cruise ? "CRUISE" : "EMP STRIKE";
            }
            return label != null;
        }
        public SupportSettings Settings => settings;

        /// <summary>Latest accepted radar scan of the local player, for the imager.</summary>
        public int RadarScanSerial { get; private set; }
        public GlobalPosition RadarScanTarget { get; private set; }
        public int RadarScanContacts { get; private set; }

        /// <summary>Latest accepted SAT CAMERA of the local player: where the optical bird last looked.</summary>
        public int CameraSerial { get; private set; }
        public GlobalPosition CameraTarget { get; private set; }

        public float GetEffectRadius(SupportActionId action, FactionHQ owner)
        {
            switch (action)
            {
                case SupportActionId.Artillery:
                    return SupportEffectPolicy.RodBlastRadius;
                case SupportActionId.Emp:
                    return Mathf.Min(SupportEffectPolicy.MaxEmpRadius,
                        settings != null ? settings.EmpRadius.Value : 12000f);
                case SupportActionId.Recon:
                    return settings != null ? settings.SarSceneRadius.Value : 1000f;
                case SupportActionId.SatCamera:
                    return settings != null ? settings.OpticalSceneRadius.Value : 1000f;
                case SupportActionId.ElintSweep:
                    return settings != null ? settings.ElintRadius.Value : 8000f;
                case SupportActionId.FlareMissile:
                    return settings != null ? settings.FlareBarrageRadius.Value : 4000f;
                case SupportActionId.RadarBlind:
                    return 1500f;
                case SupportActionId.SamNetDown:
                    return 3000f;
                case SupportActionId.ReconTeam:
                    return 2000f;
                case SupportActionId.SabotageStrike:
                    return 1000f;
                case SupportActionId.Fortify:
                    return 650f; // Selection radius; the overlay resolves the actual owned zone.
                default:
                    return 1000f;
            }
        }

        public void RegisterActiveStrike(
            int requestId, SupportActionId action, GlobalPosition target, float radius, float etaSeconds, string name, float duration)
        {
            float now = MissionNow();
            float impact = now + etaSeconds;
            float linger = duration;
            float expiry = impact + linger;

            for (int i = activeStrikes.Count - 1; i >= 0; i--)
            {
                if (activeStrikes[i].RequestId == requestId)
                {
                    activeStrikes.RemoveAt(i);
                }
            }

            if (activeStrikes.Count >= 16)
            {
                activeStrikes.RemoveAt(0);
            }

            activeStrikes.Add(new ActiveStrikeInfo(requestId, action, target, radius, impact, expiry));
            RegisterInboundStrike(name, etaSeconds);
        }

        public void RegisterInboundStrike(string strikeName, float etaSeconds)
        {
            inboundStrikeName = strikeName;
            inboundStrikeImpactTime = MissionNow() + etaSeconds;
            inboundStrikeConfirmedUntil = 0f;
        }

        public string Status
        {
            get
            {
                float now = MissionNow();
                if (inboundStrikeImpactTime > 0f)
                {
                    if (now < inboundStrikeImpactTime)
                    {
                        int remaining = Mathf.Max(0, Mathf.CeilToInt(inboundStrikeImpactTime - now));
                        return $"[TRAJECTORY ACQUIRED // {inboundStrikeName}: T-{remaining:D2}s]";
                    }
                    else if (inboundStrikeConfirmedUntil == 0f)
                    {
                        inboundStrikeConfirmedUntil = now + 4f;
                    }

                    if (now < inboundStrikeConfirmedUntil)
                    {
                        return $"[ESTIMATED ARRIVAL // {inboundStrikeName}]";
                    }
                    else
                    {
                        inboundStrikeImpactTime = 0f;
                    }
                }
                return statusText;
            }
            private set => statusText = value;
        }

        SupportSettings ISupportHost.Settings => settings;
        ManualLogSource ISupportHost.Logger => logger;
        VanillaSupportCatalog ISupportHost.Vanilla => vanilla;
        int ISupportHost.SceneGeneration => sceneGeneration;
        bool ISupportHost.TryGetSpaceState(FactionHQ owner, out SpaceState state) => TryGetSpaceState(owner, out state);

        internal void AttachSpace(SpaceService service)
        {
            space = service;
            spaceNet = new SpaceNetHost(this, service, network);
        }
        int ISupportHost.OpenSpaceWindow(FactionHQ owner, GlobalPosition point, float radius, BirdKind source,
            float minimumSpeed, float maximumSpeed) => space?.OpenWindow(owner, point, radius, source, minimumSpeed, maximumSpeed) ?? -1;
        int ISupportHost.OpenOpticalWindow(FactionHQ owner, GlobalPosition point, float baseRadius, out SupportResult refusal)
        {
            refusal = SupportResult.SpawnFailed;
            return space != null ? space.OpenOptical(owner, point, baseRadius, out refusal) : -1;
        }
        internal MarkVerdict ConfirmSpaceMark(Player player, int id)
        {
            if (!GameAccess.IsServer() || player == null || player.HQ == null) return MarkVerdict.NoContact;
            return space?.ObservationsFor(player.HQ)?.Mark(PlayerIdentity.Of(player), id, true) ?? MarkVerdict.NoContact;
        }
        internal bool TryGetSpaceState(FactionHQ owner, out SpaceState state)
        {
            state = null;
            return space != null && space.TryGetState(owner, out state);
        }
        /// <summary>The viewer faction's bird in its geostationary slot: the host's state on the host, the OPS mirror's on a client. False until it is known.</summary>
        internal bool TryOwnGeo(FactionHQ owner, int bird, out GeoBird geo)
        {
            geo = default;
            if (bird < 0 || bird >= SpaceRules.BirdCount) return false;
            if (GameAccess.IsServer()) { if (!TryGetSpaceStateCoarse(owner, out SpaceState state)) return false; geo = state.Geo((BirdKind)bird); return true; }
            if (!opsMirror.Known || !opsMirror.State.Active) return false;
            geo = opsMirror.State.Geo[bird];
            return true;
        }

        /// <summary>Display read of the host's SPACE state (quotes, panels, sky): the 1 Hz world state, no native re-sample.</summary>
        internal bool TryGetSpaceStateCoarse(FactionHQ owner, out SpaceState state)
        {
            state = null;
            return space != null && space.TryGetStateCoarse(owner, out state);
        }

        public IReadOnlyList<SupportActionDefinition> Actions => catalog.Actions;
        public bool BypassRequirements => !OpsAutomation.Strict && bypassRequirements != null && bypassRequirements.Value;
        public bool DisableCooldowns => disableCooldowns != null && disableCooldowns.Value;
        public bool RequestPending => pending;
        internal void AbandonPending() => pending = false;

        public void Configure(
            SupportSettings supportSettings, IPlayerPerks playerPerks,
            IZoneFortificationService fortifications, SupportNet net, ManualLogSource log)
        {
            settings = supportSettings;
            Active = this;
            perks = playerPerks;
            network = net;
            logger = log;
            catalog = new SupportCatalog(supportSettings, fortifications);
        }

        internal void ConfigureBypass(ConfigEntry<bool> bypass) => bypassRequirements = bypass;
        internal void ConfigureDisableCooldowns(ConfigEntry<bool> disable) => disableCooldowns = disable;

        public SupportActionId? ArmedAction { get; private set; }
        public int ArmedFrame { get; private set; }

        public void ResetForScene()
        {
            sceneGeneration = sceneGeneration == int.MaxValue ? 1 : sceneGeneration + 1;
            Clock.Reset();
            space?.ResetForScene();
            cyber?.ResetForScene();
            sof?.ResetForScene();
            ops?.ResetForScene();
            Visuals.OpsFlightVisuals.Reset();
            Visuals.FrontEffectVisuals.Reset();
            Visuals.AreaFx.Reset();
            Visuals.StagedBlastFx.Reset();
            Visuals.LaseFx.Reset();
            ResetSpaceMirror();
            Visuals.EmpVisualEffect.Reset();
            Visuals.KineticRodStrikeVisuals.Reset();
            RodGuard.Reset();
            Visuals.FlareMissileBurstVisuals.Reset();
            Visuals.FlareFx.Reset();
            Visuals.CineFx.Reset();
            Visuals.SupportParticles.Reset();
            inboundStrikeName = null;
            inboundStrikeImpactTime = 0f;
            inboundStrikeConfirmedUntil = 0f;
            ledger.Clear();
            census.Clear();
            contactReplies.Clear();
            ttiReplies.Clear();
            if (cruiseTasking != null) cruiseTasking.Clear();
            cruiseLegs.Clear();
            pendingWaypoint = -1;
            strikeJobs.Clear();
            Array.Clear(fallbackJobs, 0, fallbackJobs.Length);
            StopAllCoroutines();
            pending = false;
            localCooldownUntil.Clear();
            ArmedAction = null;
            localPick = null;
            ArmedFrame = 0;
            activeStrikes.Clear();
            mapGesture.Reset();
            SupportMapMode.GestureArmed = false;
            Status = "Select support option, then right-click on map.";
        }

        private void OnDestroy()
        {
            if (Active == this) Active = null;
            ResetForScene();
            SupportMapMode.GestureArmed = false;
        }

        private static readonly MissionClock Clock = new MissionClock();
        private static float nextClockWarning;

        /// <summary>
        /// Gameplay clock: pauses and acceleration follow the mission; MP uses its shared start. Guarded: a multiplayer
        /// peer (host included) whose MissionManager is missing or has not started (multiplayerStartTime unset, so the raw
        /// value is lobby time) keeps the last good value; the result is finite, never decreases within a scene, and
        /// re-baselines if a transient ever latched high.
        /// </summary>
        internal static float MissionNow()
        {
            float raw = 0f;
            bool valid = false;
            try
            {
                MissionManager mission = NetworkSceneSingleton<MissionManager>.i;
                if (GameManager.gameState != GameState.Multiplayer)
                { raw = mission != null ? mission.MissionTime : Time.timeSinceLevelLoad; valid = true; }
                else if (mission != null && mission.multiplayerStartTime > 0d)
                { raw = mission.MissionTime; valid = true; }
            }
            catch (Exception e)
            {
                valid = false;
                if (Time.unscaledTime >= nextClockWarning)
                {
                    nextClockWarning = Time.unscaledTime + 30f;
                    Plugin.Logger?.LogWarning("[Support] Mission clock read failed; holding the last good time: " + e.Message);
                }
            }
            return Clock.Read(raw, valid, Time.frameCount);
        }

        private void Update()
        {
            UpdateSpaceMirror();

            // Prune expired active strikes
            float now = MissionNow();
            for (int i = activeStrikes.Count - 1; i >= 0; i--)
            {
                if (!activeStrikes[i].IsActive(now))
                    activeStrikes.RemoveAt(i);
            }

            // Publish the armed state for Wing Command to read (BoscaliLink), so a wing
            // point-order and a support call-in never both fire on one right-click.
            // Drop leg mirrors whose strike left the board, one per tick, allocation-free.
            if (cruiseLegs.Count > 0)
            {
                int stale = 0;
                bool found = false;
                foreach (int key in cruiseLegs.Keys)
                {
                    bool alive = false;
                    for (int i = 0; i < activeStrikes.Count; i++)
                        if (activeStrikes[i].RequestId == key) { alive = true; break; }
                    if (!alive) { stale = key; found = true; break; }
                }
                if (found) cruiseLegs.Remove(stale);
            }
            bool anyArmed = ArmedAction.HasValue || localPick != null;
            SupportMapMode.GestureArmed = anyArmed && mapGesture.Armed;
            mapGesture.Advance(Time.frameCount);
            // Transport timeouts run on wall time: a frozen or paused MP mission clock must not hold a request pending forever.
            float wall = Time.unscaledTime;
            if (pendingWaypoint != -1 && wall > pendingWaypointUntil)
            {
                pendingWaypoint = -1;
                Status = "No response from host.";
            }
            if (pending && wall - pendingSince > ReplyTimeout)
            {
                pending = false;
                Status = "No response from host.";
            }

            if (anyArmed)
            {
                if (Input.GetKeyDown(KeyCode.Escape))
                {
                    CancelArmed();
                    return;
                }

                if (Time.frameCount > ArmedFrame + 1 && mapGesture.Armed)
                {
                    // Commit on release, not press, and only when the pointer barely moved.
                    // A right-drag pans the maximised map; it must not also drop a strike at
                    // the point where the drag began.
                    if (Input.GetMouseButtonDown(1))
                    {
                        Vector3 down = Input.mousePosition;
                        mapGesture.NotePointerDown(down.x, down.y);
                    }
                    else if (Input.GetMouseButtonUp(1))
                    {
                        Vector3 up = Input.mousePosition;
                        DynamicMap map = SceneSingleton<DynamicMap>.i;
                        if (mapGesture.ReleasedAsClick(up.x, up.y, ClickSlopPixels) &&
                            map != null && DynamicMap.mapMaximized &&
                            map.TryGetCursorCoordinates(out GlobalPosition target))
                        {
                            if (localPick != null)
                            {
                                Action<GlobalPosition> pick = localPick;
                                CancelArmed();
                                pick(target);
                            }
                            else
                            {
                                SupportActionId action = ArmedAction.Value;
                                if (pending) return;
                                ArmedAction = null;
                                mapGesture.Complete(Time.frameCount);
                                RequestAt(action, target);
                            }
                        }
                    }
                }
            }
        }

        // ---- Client view -----------------------------------------------------------------

        /// <summary>The local pilot's vanilla allocation: what a perk costs is paid from it (the host re-checks on every request).</summary>
        public float LocalAllocation =>
            GameManager.GetLocalPlayer<Player>(out Player player) && player != null && float.IsFinite(player.Allocation) ? player.Allocation : 0f;

        /// <summary>The allocation price of one perk for the local player (the host runs the same QuoteFor).</summary>
        internal CallQuote Quote(SupportActionId id)
        {
            SupportActionDefinition def = catalog != null ? catalog.Find(id) : null;
            if (def == null || !GameManager.GetLocalPlayer<Player>(out Player player) || player == null)
                return new CallQuote(0, "");
            return QuoteFor(def, player);
        }

        /// <summary>
        /// Why the local pilot cannot use this perk right now, or "" when nothing blocks it: the progression qualification
        /// (NEEDS STRIKE QUALIFICATION) first, then the front readiness (NEEDS READINESS 3).
        /// </summary>
        internal string LockReason(SupportActionId id)
        {
            if (!CallSheet.TryGet(id, out CallRow row) || catalog == null) return "";
            SupportActionDefinition def = catalog.Find(id);
            if (def == null || !GameManager.GetLocalPlayer<Player>(out Player player) || player == null) return "";
            if (!IsAuthorised(def)) return "NEEDS " + QualificationName(def.Capability);
            if (!BypassRequirements && readiness.Readiness(player.HQ, row.Front) < row.Rung && row.Rung > OpsAutomation.ReadinessFloor) return "NEEDS READINESS " + row.Rung;
            if (def.RequiredBird != SpaceBirdRequirement.None && !BirdsUpFor(def.RequiredBird)) return "NO SATELLITE · REBUILD IT";
            return "";
        }

        /// <summary>Client view: the OPS mirror says the satellite the perk needs is alive (true while the mirror is unknown).</summary>
        private bool BirdsUpFor(SpaceBirdRequirement r) => r switch
        {
            SpaceBirdRequirement.Optical => opsMirror.BirdUp(BirdKind.Optical),
            SpaceBirdRequirement.Radar => opsMirror.BirdUp(BirdKind.Radar),
            SpaceBirdRequirement.Kinetic => opsMirror.BirdUp(BirdKind.Kinetic),
            SpaceBirdRequirement.OpticalOrRadar => opsMirror.BirdUp(BirdKind.Optical) || opsMirror.BirdUp(BirdKind.Radar),
            _ => true
        };

        /// <summary>The qualification that authorises a capability, in words (read through <see cref="IProgressionView"/>; a fixed name when it is not installed).</summary>
        private static string QualificationName(string capability)
        {
            string lane = capability == SupportCapabilities.Artillery ? "STRIKE"
                : capability == SupportCapabilities.Emp ? "SIGNALS"
                : capability == SupportCapabilities.Fortify ? "ENGINEER" : "RECON";
            if (ModuleServices.TryGet<IProgressionView>(out IProgressionView view) && view != null)
            {
                try
                {
                    PerkView[] list = view.GetPerks();
                    for (int i = 0; list != null && i < list.Length; i++)
                    {
                        string name = list[i].Name;
                        if (!string.IsNullOrEmpty(name) && name.EndsWith("Qualification", StringComparison.OrdinalIgnoreCase) &&
                            name.StartsWith(lane, StringComparison.OrdinalIgnoreCase)) return name.ToUpperInvariant();
                    }
                }
                catch (Exception) { /* a view that is not ready yet falls back to the fixed name */ }
            }
            return lane + " QUALIFICATION";
        }

        /// <summary>Host: the front readiness gate for one perk; the dev bypass opens everything.</summary>
        private bool ReadinessOpen(Player player, in CallRow row) =>
            BypassRequirements || readiness.Readiness(player.HQ, row.Front) >= row.Rung || row.Rung <= OpsAutomation.ReadinessFloor;

        /// <summary>S1 replaces the default (every front fully built, neutral quality) with the live front state.</summary>
        internal void ConfigureReadiness(IFrontReadiness source) => readiness = source ?? new DefaultFrontReadiness();

        /// <summary>True when this peer is the host or has a live client link to one.</summary>
        public bool Online => GameAccess.IsServer() || GameAccess.NetworkManagerOrNull?.Client?.Active == true;

        /// <summary>Seconds until the local pilot may use this perk again (the host enforces its own clock).</summary>
        public float LocalCooldownRemaining(SupportActionId id) =>
            DisableCooldowns || !localCooldownUntil.TryGetValue(id, out float until) ? 0f : Mathf.Max(0f, until - MissionNow());

        public bool IsAuthorised(SupportActionDefinition action)
        {
            if (BypassRequirements || OpsAutomation.ReadinessFloor > 0) return true; // the floor is the sim's test-only gate opener
            if (!GameManager.GetLocalPlayer<Player>(out Player player) || player == null) return false;
            return QualificationOpen(player, action);
        }

        /// <summary>
        /// Price for the local player, including the support-cost perk. The server runs the
        /// same method, so the panel never advertises a number the host will not honour.
        /// </summary>
        public float Cost(SupportActionDefinition action)
        {
            GameManager.GetLocalPlayer<Player>(out Player player);
            return QuoteFor(action, player).Cost;
        }

        /// <summary>
        /// The cooldown the host will apply to this player's next use of one perk: the rung's table value (R1 30 s .. R5 300 s),
        /// per pilot and per perk. Scaled by the requester's re-tasking perk, and used for both the check and the reply, so the
        /// countdown a client shows is the one the host enforced.
        /// </summary>
        private float CooldownFor(Player player, SupportActionId action)
        {
            if (DisableCooldowns || player == null || !CallSheet.TryGet(action, out CallRow row)) return 0f;
            return CallSheet.Cooldown(row.Rung) *
                   perks.Multiplier(PlayerIdentity.Of(player), PerkEffect.SupportCooldown) *
                   EventsCooldownMultiplier(player);
        }

        /// <summary>
        /// The Events module's live world modifier for one player, resolved late so neither
        /// module has to install first. Exactly 1 when Events is absent or the theater is calm.
        /// </summary>
        internal static float EventsCostMultiplier(Player player) =>
            player != null && ModuleServices.TryGet<IActiveEventsView>(out IActiveEventsView events)
                ? events.SupportCostMultiplierFor(PlayerIdentity.Of(player))
                : 1f;

        internal static float EventsCooldownMultiplier(Player player) =>
            player != null && ModuleServices.TryGet<IActiveEventsView>(out IActiveEventsView events)
                ? events.SupportCooldownMultiplierFor(PlayerIdentity.Of(player))
                : 1f;

        public void Arm(SupportActionId action)
        {
            if (pending)
            {
                Status = "REQUEST PENDING — wait for host acknowledgement.";
                return;
            }

            if (ArmedAction.HasValue && ArmedAction.Value == action)
            {
                CancelArmed();
                return;
            }

            SupportActionDefinition def = catalog != null ? catalog.Find(action) : null;
            string name = def != null ? def.Name : "SUPPORT";
            if (!TryArmMap(name + " ARMED · RIGHT-CLICK MAP")) return;

            ArmedAction = action;
            ArmedFrame = Time.frameCount;
            Status = "ARMED: " + name + " — Right-click on map to execute (ESC to cancel).";
        }

        private bool TryArmMap(string prompt)
        {
            if (WingLink.WingMapGestureArmed)
            {
                Status = "WING ORDER ARMED — cancel it in WMC first.";
                return false;
            }
            if (!mapGesture.TryArm(prompt))
            {
                Status = "MAP INPUT BUSY — cancel the armed order first.";
                return false;
            }

            DynamicMap map = SceneSingleton<DynamicMap>.i;
            if (map != null && !DynamicMap.mapMaximized) map.Maximize();
            return true;
        }

        /// <summary>Arms a right-click on the map that only reports the point back; nothing is sent.</summary>
        public bool ArmLocalPick(string label, Action<GlobalPosition> onPick)
        {
            if (onPick == null || !TryArmMap(label + " · RIGHT-CLICK MAP")) return false;
            localPick = onPick;
            ArmedAction = null;
            ArmedFrame = Time.frameCount;
            Status = "PICK: " + label + " — Right-click on map (ESC to cancel).";
            return true;
        }

        public bool LocalPickArmed => localPick != null;

        public void Disarm()
        {
            if (!ArmedAction.HasValue && localPick == null) return;
            CancelArmed();
            Status = "Support request cancelled.";
        }

        private void CancelArmed()
        {
            ArmedAction = null;
            localPick = null;
            mapGesture.Complete(Time.frameCount);
        }

        public void RequestAtMark(IObservationSource observations)
        {
            if (GameplayUI.GameIsPaused || pending || !ArmedAction.HasValue) return;
            if (observations == null || !observations.TryGet(out ObservationPoint point))
            {
                Status = "Camera mark unavailable. Mark again or right-click the map.";
                return;
            }
            SupportActionId action = ArmedAction.Value;
            var target = new GlobalPosition(point.X, point.Y, point.Z);
            CancelArmed();
            RequestAt(action, target);
        }

        /// <summary>True when the last RequestAt could not leave this machine (no host link).</summary>
        public bool LastRequestOffline { get; private set; }

        /// <summary>
        /// Submits one request. <paramref name="begun"/> receives the request id BEFORE the network send, because a
        /// server peer evaluates and answers synchronously inside this call.
        /// </summary>
        public int RequestAt(SupportActionId action, GlobalPosition target, Action<int> begun = null)
        {
            LastRequestOffline = false;
            if (pending)
            {
                Status = CallWords.Refusal(CallRefusal.Busy);
                return 0;
            }

            SupportActionDefinition def = catalog != null ? catalog.Find(action) : null;
            if (def == null || !def.Enabled)
            {
                Status = CallWords.Refusal(CallRefusal.Unavailable);
                return 0;
            }

            float cost = Cost(def);
            if (cost <= 0f && action != SupportActionId.JtacUnlase) // UNLASE is free by design
            {
                Status = SupportWords.Refusal(SupportResult.CapabilityUnavailable);
                return 0;
            }

            bool free = action == SupportActionId.JtacUnlase; // no cooldown, no charge
            if (free && !IsAuthorised(def))
            {
                Status = CallWords.Refusal(CallRefusal.Unavailable);
                return 0;
            }

            if (!free && LocalCooldownRemaining(action) > 0.5f)
            {
                Status = CallWords.Refusal(CallRefusal.Cooldown, seconds: Mathf.CeilToInt(LocalCooldownRemaining(action)));
                return 0;
            }

            string locked = free ? "" : LockReason(action);
            if (locked.Length > 0)
            {
                Status = CallWords.Refusal(CallRefusal.Locked, unlock: locked);
                return 0;
            }

            if (!free && !BypassRequirements && LocalAllocation + 0.001f < cost)
            {
                Status = CallWords.Refusal(CallRefusal.LowCredit, need: Mathf.CeilToInt(cost));
                return 0;
            }

            pending = true;
            pendingSince = Time.unscaledTime;
            pendingAction = action;
            Status = "Request sent to grid " + Mathf.RoundToInt(target.x) + " / " + Mathf.RoundToInt(target.z) + ".";
            int id = ++nextRequestId;
            begun?.Invoke(id);
            network.Request(id, action, target);
            return id;
        }

        /// <summary>Called when the request could not leave this machine at all.</summary>
        internal void ReportOffline()
        {
            pending = false;
            LastRequestOffline = true;
            Status = "No host connection.";
        }

        private CallsController calls;
        internal void AttachCalls(CallsController controller) => calls = controller;

        private string CallWordsFor(in SupportResultMessage r)
        {
            switch ((SupportResult)r.Result)
            {
                case SupportResult.InsufficientAllocation: return CallWords.Refusal(CallRefusal.LowCredit, need: Quote((SupportActionId)r.Action).Cost);
                case SupportResult.Cooldown:
                    return CallWords.Refusal(CallRefusal.Cooldown,
                        seconds: Mathf.CeilToInt(r.CooldownSeconds > 0.01f ? r.CooldownSeconds : LocalCooldownRemaining((SupportActionId)r.Action)));
                case SupportResult.NotUnlocked:
                    return CallWords.Refusal(CallRefusal.Locked, unlock: LockReason((SupportActionId)r.Action));
                case SupportResult.OutOfRange: return CallWords.Refusal(CallRefusal.OutOfRange);
                case SupportResult.OutOfCoverage:
                    int bird = Presentation.Fronts.FrontViews.PerkBird((SupportActionId)r.Action);
                    return "NEGATIVE: " + SupportWords.OutsideFootprint(bird >= 0 ? GeoSpace.Names[bird] : "SATELLITE");
                case SupportResult.InvalidTarget: return SupportWords.Refusal(SupportResult.InvalidTarget);
                case SupportResult.RateLimited:
                case SupportResult.Busy: return CallWords.Refusal(CallRefusal.Busy);
                default: return SupportWords.Refusal((SupportResult)r.Result);
            }
        }

        internal void ReceiveResult(SupportResultMessage message)
        {
            if (!pending || message.RequestId != nextRequestId || message.Action != (byte)pendingAction) return;
            pending = false;
            SupportResult result = (SupportResult)message.Result;
            SupportActionDefinition action = catalog.Find((SupportActionId)message.Action);
            string name = action != null ? action.Name : "Support";
            calls?.Answer(message.RequestId, result == SupportResult.Accepted, result == SupportResult.Accepted ? name : CallWordsFor(message));
            if (ModuleServices.TryGet<IChatterChannel>(out var chatter))
                chatter.Report("SUPPORT CONTROL", "SUPPORT:" + message.RequestId,
                    result == SupportResult.Accepted ? name + " authorised. Request accepted." :
                    name + ": " + CallWordsFor(message), ChatterUrgency.Status);
            if (result == SupportResult.Accepted)
            {
                if ((SupportActionId)message.Action != SupportActionId.JtacUnlase)
                    localCooldownUntil[(SupportActionId)message.Action] = DisableCooldowns ? 0f : MissionNow() + message.CooldownSeconds;
                bool sweep = action != null &&
                    (action.Id == SupportActionId.Recon || action.Id == SupportActionId.ElintSweep ||
                     action.Id == SupportActionId.SatCamera);
                Status = sweep
                    ? name + " complete: " + Mathf.Max(0, message.Contacts) + " contact(s)."
                    : name + " accepted.";
                if (action != null && action.Id == SupportActionId.ElintSweep)
                    Status = name + " complete: " + Mathf.Max(0, message.Contacts) + " emitting radar(s) located.";
                if (action != null && action.Id == SupportActionId.Recon &&
                    float.IsFinite(message.X) && float.IsFinite(message.Z))
                {
                    RadarScanTarget = new GlobalPosition(message.X, message.Y, message.Z);
                    RadarScanContacts = Mathf.Max(0, message.Contacts);
                    RadarScanSerial++;
                    Status = name + " accepted: imaging, " + RadarScanContacts + " contact(s) revealed.";
                }
                if (action != null && action.Id == SupportActionId.SatCamera && float.IsFinite(message.X) && float.IsFinite(message.Z))
                {
                    CameraTarget = new GlobalPosition(message.X, message.Y, message.Z);
                    CameraSerial++;
                    Status = name + " accepted: imaging, " + Mathf.Max(0, message.Contacts) + " contact(s) revealed.";
                }
                float eta = action != null && (action.Id == SupportActionId.Prsm || action.Id == SupportActionId.Cruise) ? message.Duration :
                            action != null && action.Id == SupportActionId.Artillery ? 8f :
                            action != null && action.Id == SupportActionId.Emp ? SupportEffectPolicy.EmpDelay :
                            action != null && action.Id == SupportActionId.FlareMissile ? 5.5f : 0f;
                if (!float.IsFinite(message.Radius) || message.Radius < 0f || message.Radius > 200000f ||
                    !float.IsFinite(message.Duration) || message.Duration < 0f || message.Duration > 60f ||
                    !float.IsFinite(message.X) || !float.IsFinite(message.Y) || !float.IsFinite(message.Z)) return;
                if (eta <= 0f && action != null && (action.Id == SupportActionId.Prsm || action.Id == SupportActionId.Cruise)) return;
                RegisterActiveStrike(message.RequestId, (SupportActionId)message.Action,
                    new GlobalPosition(message.X, message.Y, message.Z), message.Radius, eta, name, message.Duration);
            }
            else
            {
                Status = Explain(result);
            }
        }

        internal static string Explain(SupportResult result) => SupportWords.Refusal(result);

        // ---- Server ----------------------------------------------------------------------

        internal SupportResult Evaluate(Player player, SupportRequestMessage request)
        {
            if (player == null || player.HQ == null) return SupportResult.InvalidTarget;
            if (!float.IsFinite(request.X) || !float.IsFinite(request.Y) || !float.IsFinite(request.Z) ||
                Math.Abs(request.X) > 10000000f || Math.Abs(request.Y) > 10000000f || Math.Abs(request.Z) > 10000000f)
                return SupportResult.InvalidTarget;

            SupportActionDefinition action = catalog.Find((SupportActionId)request.Action);
            if (action == null) return SupportResult.CapabilityUnavailable;

            ulong playerId = PlayerIdentity.Of(player);
            float now = MissionNow();
            bool bypass = BypassRequirements;
            int scene = sceneGeneration;

            if (ledger.WasAccepted(playerId, request.RequestId)) return SupportResult.Duplicate;
            // Transport abuse limits keep wall time; gameplay cooldowns use mission time.
            if (!DisableCooldowns && ledger.IsRateLimited(playerId, Time.unscaledTime, RequestsPerSecond, 1f)) return SupportResult.RateLimited;
            if (!action.Enabled) return SupportResult.Disabled;
            if (!bypass && !HostAuthorised(player, action)) return SupportResult.NotUnlocked;
            SpaceState spaceState = null;
            if (action.RequiredBird != SpaceBirdRequirement.None)
            {
                if (!TryGetSpaceState(player.HQ, out spaceState)) return SupportResult.CapabilityUnavailable;
                if (spaceState.LiveUplinkCount == 0) return SupportResult.UplinkDown;
                if (!HasRequiredBird(spaceState, action.RequiredBird)) return SupportResult.CapabilityUnavailable;
                if (action.SpaceTask.HasValue && !spaceState.CanStart(action.SpaceTask.Value, now)) return SupportResult.BirdNotReady;
            }
            // A perk that needs a satellite over its target: the bird's footprint must hold the aim (a relocation moves it).
            if (!bypass && TryGeoBird(action.RequiredBird, out BirdKind geoBird))
            {
                Vector2 span = TheaterFrame.Resolve();
                if (!spaceState.Covers(geoBird, now, GeoSpace.U(request.X, span.x), GeoSpace.V(request.Z, span.y))) return SupportResult.OutOfCoverage;
            }
            // JTAC UNLASE is free and gate-less: it is the recovery half of a paid mark, not a perk of its own.
            bool free = action.Id == SupportActionId.JtacUnlase;
            if (!free && !DisableCooldowns && ledger.IsCoolingDown(playerId, (byte)action.Id, now, CooldownFor(player, action.Id)))
                return SupportResult.Cooldown;

            float cost = free ? 0f : QuoteFor(action, player).Cost;
            CallRow row = default;
            if (!free && (cost <= 0f || !CallSheet.TryGet(action.Id, out row))) return SupportResult.CapabilityUnavailable;
            if (!free && !ReadinessOpen(player, row)) return SupportResult.NeedsReadiness;
            float quality = free ? 1f : readiness.Quality(player.HQ, row.Front);

            SpaceActionTransaction transaction = null;
            if (action.SpaceTask.HasValue)
            {
                if (spaceState == null || !spaceState.TryReserve(action.SpaceTask.Value, now, out SpaceTaskReservation receipt))
                    return SupportResult.BirdNotReady;
                transaction = new SpaceActionTransaction(space, player.HQ, spaceState, receipt, action.TaskSeconds);
            }
            if (!bypass && !free && !TrySpendAllocation(player, cost))
            {
                transaction?.Cancel();
                return SupportResult.InsufficientAllocation;
            }
            GlobalPosition aim = new GlobalPosition(request.X, request.Y, request.Z);
            bool snapped = false, sar = false;
            if (action.Id == SupportActionId.Artillery || action.Id == SupportActionId.Prsm || action.Id == SupportActionId.Cruise)
            {
                // Spec 3.7: operator MARKs are map marks; a strike aimed within 300 m of a fresh own-faction mark lands on it, tight.
                SpaceMark mark = default;
                if (space != null && space.TryNearestMark(player.HQ, request.X, request.Z, now, out mark))
                {
                    aim = new GlobalPosition(mark.X, request.Y, mark.Z);
                    snapped = true; sar = mark.Source == BirdKind.Radar;
                    logger.LogInfo("[Support] " + action.Name + " snapped to MARK " + mark.Id + ".");
                }
            }
            var context = new SupportContext(player, aim, request.RequestId, this, transaction, quality: quality, markSnapped: snapped, markSar: sar);
            if (scene != sceneGeneration) { transaction?.Cancel(); return SupportResult.SpawnFailed; }

            SupportResult result;
            try { result = action.Action.Execute(context); }
            catch (Exception e)
            {
                logger.LogError(e);
                result = SupportResult.SpawnFailed;
            }
            if (scene != sceneGeneration) { transaction?.Cancel(); return SupportResult.SpawnFailed; }
            if (transaction?.PhysicalLaunch == true) result = SupportResult.Accepted;
            if (result == SupportResult.Accepted && transaction != null &&
                (action.RequiresPhysicalLaunch ? !transaction.PhysicalLaunch : !transaction.Commit()))
                result = SupportResult.SpawnFailed;
            if (result != SupportResult.Accepted)
            {
                transaction?.Cancel();
                if (!bypass && !free) RefundAllocation(player, cost);
                logger.LogWarning("[Support] " + action.Name + " request " + request.RequestId +
                    " rejected: " + result + ".");
                return result;
            }

            ledger.Accept(playerId, request.RequestId, (byte)action.Id, now, startCooldown: !free);
            // An accepted scan or camera tasking is a human working SPACE: OVERLORD steps back. Replays, refusals,
            // rods and every other CALL are not.
            if (WatchOfficerPolicy.CountsAsHumanWork(action.Id))
            {
                try { space?.NoteHumanSpaceVerb(player); }
                catch (Exception e) { logger.LogError(e); }
            }
            logger.LogInfo("[Support] Accepted " + action.Name + " request " + request.RequestId +
                " from " + player + " at " + context.Target +
                (cost > 0f ? " for " + Mathf.RoundToInt(cost) + " allocation." : "."));
            return SupportResult.Accepted;
        }

        /// <summary>RECON PASS = RADAR, SAT CAMERA = OPTICAL, ORBITAL ROD = KINETIC need their bird over the aim; PRSM and CRUISE (either bird) do not.</summary>
        internal static bool TryGeoBird(SpaceBirdRequirement requirement, out BirdKind bird)
        {
            bird = requirement == SpaceBirdRequirement.Radar ? BirdKind.Radar : requirement == SpaceBirdRequirement.Kinetic ? BirdKind.Kinetic : BirdKind.Optical;
            return requirement == SpaceBirdRequirement.Radar || requirement == SpaceBirdRequirement.Kinetic || requirement == SpaceBirdRequirement.Optical;
        }

        private static bool HasRequiredBird(SpaceState state, SpaceBirdRequirement requirement) => requirement switch
        {
            SpaceBirdRequirement.Optical => state.HasBird(BirdKind.Optical),
            SpaceBirdRequirement.Radar => state.HasBird(BirdKind.Radar),
            SpaceBirdRequirement.Kinetic => state.HasBird(BirdKind.Kinetic),
            SpaceBirdRequirement.OpticalOrRadar => state.HasBird(BirdKind.Optical) || state.HasBird(BirdKind.Radar),
            _ => requirement == SpaceBirdRequirement.None
        };

        private bool HostAuthorised(Player player, SupportActionDefinition action) =>
            OpsAutomation.ReadinessFloor > 0 || QualificationOpen(player, action);

        /// <summary>Single-player ready: a rung-1 perk needs no progression qualification (a fresh pilot has none), so every front opens with one usable perk; R2 and up still need it.</summary>
        private bool QualificationOpen(Player player, SupportActionDefinition action) =>
            (CallSheet.TryGet(action.Id, out CallRow row) && row.Rung <= 1) || perks.Grants(PlayerIdentity.Of(player), action.Capability);

        internal float ServerCooldownFor(Player player, SupportActionId action) => CooldownFor(player, action);

        /// <summary>Seconds left on this player's host cooldown of one perk; what a Cooldown refusal tells the client.</summary>
        internal float ServerCooldownRemaining(Player player, SupportActionId action) =>
            player == null || DisableCooldowns ? 0f
                : ledger.CooldownRemaining(PlayerIdentity.Of(player), (byte)action, MissionNow(), CooldownFor(player, action));

        // ---- Allocation (the perk currency) -------------------------------------------------

        /// <summary>Host: takes the price from the pilot's vanilla allocation. False (nothing taken) when they cannot afford it.</summary>
        internal bool TrySpendAllocation(Player player, float cost)
        {
            if (player == null) return false;
            if (!AllocationRules.CanAfford(player.Allocation, cost)) return false;
            player.SetAllocation(AllocationRules.AfterSpend(player.Allocation, cost));
            return true;
        }

        /// <summary>Host: gives a failed perk's price back (also the pay for operator work).</summary>
        internal void RefundAllocation(Player player, float amount)
        {
            if (player != null && amount > 0f) player.SetAllocation(AllocationRules.AfterRefund(player.Allocation, amount));
        }

        /// <summary>
        /// Price for one player. No action prices itself from the target, so costing uses a
        /// bare context and both the panel and the host reach the same number.
        /// </summary>
        internal CallQuote QuoteFor(SupportActionDefinition action, Player player)
        {
            // BaseCost <= 0 still means "this action is not available on this map".
            if (player == null || !CallSheet.TryGet(action.Id, out CallRow row) ||
                action.Action.BaseCost(new SupportContext(player, default, 0, this)) <= 0f)
                return new CallQuote(0, "");
            // The host reads its own SPACE state; a client reads the faction mirror, so both quote the same +40 % when degraded.
            bool degraded = action.RequiredBird != SpaceBirdRequirement.None &&
                TryGetSpaceFamily(player.HQ, out SpaceFamilyState family) && family == SpaceFamilyState.Degraded;
            // Every factor is readable on a client too (the Events and Progression mirrors, and the host's replicated PerkPriceScale), so the panel quotes what the host charges.
            var inputs = new PriceInputs(degraded, "UPLINK DOWN", row.Front == Front.Cyber && CyberExploit(player.HQ),
                EventsCostMultiplier(player), perks.Multiplier(PlayerIdentity.Of(player), PerkEffect.SupportCost),
                settings.PerkPriceScale.Value);
            return CallPricing.Quote(row.Rung, inputs);
        }

        // ---- Host services ---------------------------------------------------------------

        private int[] JobsFor(FactionHQ owner)
        {
            if (owner == null) return fallbackJobs;
            if (strikeJobs.TryGetValue(owner, out int[] jobs)) return jobs;
            if (strikeJobs.Count >= MaximumFactionPools) return null;
            jobs = new int[2];
            strikeJobs.Add(owner, jobs);
            return jobs;
        }

        bool ISupportHost.TryReserve(FactionHQ owner, SupportPool pool)
        {
            int[] jobs = JobsFor(owner);
            int index = (int)pool;
            if (jobs == null || index < 0 || index >= jobs.Length || jobs[index] >= MaximumStrikeJobs) return false;
            jobs[index]++;
            return true;
        }

        void ISupportHost.Release(FactionHQ owner, SupportPool pool)
        {
            // Cleanup must never recreate a pool after ResetForScene.
            if (owner == null || !strikeJobs.TryGetValue(owner, out int[] jobs)) return;
            int index = (int)pool;
            if (index >= 0 && index < jobs.Length && jobs[index] > 0) jobs[index]--;
        }

        void ISupportHost.Run(IEnumerator routine) => StartCoroutine(routine);

        void ISupportHost.ReportTti(int requestId, float seconds)
        {
            if (ttiReplies.Count >= MaximumContactReplies) ttiReplies.Clear();
            ttiReplies[requestId] = seconds;
        }

        void ISupportHost.ReportContacts(int requestId, int contacts)
        {
            if (contactReplies.Count >= MaximumContactReplies) contactReplies.Clear();
            contactReplies[requestId] = contacts;
        }

        internal float TakeTti(int requestId)
        {
            if (!ttiReplies.TryGetValue(requestId, out float seconds)) return -1f;
            ttiReplies.Remove(requestId);
            return seconds;
        }

        void ISupportHost.TrackCruiseStrike(int requestId, ulong requesterId, FactionHQ owner, GlobalPosition target, Missile first) =>
            Tasking.Track(requestId, requesterId, owner, target, first);

        void ISupportHost.AddCruiseMissile(int requestId, Missile missile) =>
            Tasking.AddMissile(requestId, missile);

        internal int TakeContacts(int requestId)
        {
            if (!contactReplies.TryGetValue(requestId, out int contacts)) return -1;
            contactReplies.Remove(requestId);
            return contacts;
        }

        // ---- Cruise tasking ----------------------------------------------------------------

        internal CruiseTasking Tasking => cruiseTasking ?? (cruiseTasking =
            new CruiseTasking(routine => ((ISupportHost)this).Run(routine), BroadcastLegs));

        /// <summary>Server: validates a leg intent; every event broadcasts the strike's legs.</summary>
        internal void ApplyWaypoint(Player player, int requestId, GlobalPosition leg, bool clear)
        {
            CruiseTasking.Strike strike = Tasking.Find(requestId);
            if (strike == null || player == null || player.HQ == null || player.HQ != strike.Owner)
            {
                BroadcastShell(player, requestId,
                    strike == null ? SupportResult.SpawnFailed : SupportResult.NotUnlocked);
                return;
            }
            if (clear) Tasking.ClearLegs(strike);
            else Tasking.QueueLeg(strike, leg, MissionNow(), settings.MaximumRange.Value);
        }

        private void BroadcastLegs(CruiseTasking.Strike strike, SupportResult result)
        {
            if (strike == null || network == null) return;
            var message = new CruiseLegsMessage
            {
                Protocol = SupportNet.ProtocolVersion,
                RequestId = strike.RequestId,
                OwnerId = strike.RequesterId,
                Result = (byte)result,
                FactionName = FactionKey(strike.Owner),
                Dive = strike.Dive,
                Tti = Tasking.RouteTti(strike)
            };
            int legs = Math.Min(strike.Legs.Count, StrikeBallistics.MaxLegs);
            message.LegCount = (byte)legs;
            message.X = new float[StrikeBallistics.MaxLegs];
            message.Z = new float[StrikeBallistics.MaxLegs];
            for (int i = 0; i < legs; i++)
            {
                message.X[i] = strike.Legs[i].x;
                message.Z[i] = strike.Legs[i].z;
            }
            network.BroadcastCruiseLegs(message);
            if (GameAccess.IsServer()) ReceiveCruiseLegs(message);
        }

        private void BroadcastShell(Player player, int requestId, SupportResult result)
        {
            if (network == null) return;
            var message = new CruiseLegsMessage
            {
                Protocol = SupportNet.ProtocolVersion,
                RequestId = requestId,
                OwnerId = player != null ? PlayerIdentity.Of(player) : 0UL,
                Result = (byte)result,
                FactionName = player != null ? FactionKey(player.HQ) : string.Empty,
                Tti = -1f,
                X = new float[StrikeBallistics.MaxLegs],
                Z = new float[StrikeBallistics.MaxLegs]
            };
            network.BroadcastCruiseLegs(message);
            if (GameAccess.IsServer()) ReceiveCruiseLegs(message);
        }

        internal void ReceiveCruiseLegs(CruiseLegsMessage message)
        {
            GameManager.GetLocalPlayer<Player>(out Player player);
            if (player == null || player.HQ == null || message.FactionName != FactionKey(player.HQ)) return;
            if (message.OwnerId != PlayerIdentity.Of(player)) return;
            if (message.RequestId == pendingWaypoint)
            {
                pendingWaypoint = -1;
                SupportResult result = (SupportResult)message.Result;
                Status = result == SupportResult.Accepted ? "Waypoint accepted." :
                    Explain(result);
            }
            ActiveStrikeInfo? known = null;
            for (int i = 0; i < activeStrikes.Count; i++)
                if (activeStrikes[i].RequestId == message.RequestId && activeStrikes[i].ActionId == SupportActionId.Cruise)
                    known = activeStrikes[i];
            if (!known.HasValue) return;
            if (cruiseLegs.Count >= CruiseTasking.MaxStrikes && !cruiseLegs.ContainsKey(message.RequestId))
                cruiseLegs.Clear();
            var mirror = new CruiseLegMirror { Dive = message.Dive, Tti = message.Tti };
            int legs = Math.Min((int)message.LegCount, StrikeBallistics.MaxLegs);
            if (message.X != null && message.Z != null)
                for (int i = 0; i < legs && i < message.X.Length && i < message.Z.Length; i++)
                {
                    if (!float.IsFinite(message.X[i]) || !float.IsFinite(message.Z[i])) continue;
                    mirror.Legs.Add(new GlobalPosition(message.X[i], 0f, message.Z[i]));
                }
            cruiseLegs[message.RequestId] = mirror;
            if (float.IsFinite(message.Tti) && message.Tti > 0f)
            {
                SupportActionDefinition def = catalog != null ? catalog.Find(SupportActionId.Cruise) : null;
                float linger = Math.Max(0f, known.Value.ExpiryTime - known.Value.ImpactTime);
                RegisterActiveStrike(known.Value.RequestId, known.Value.ActionId, known.Value.Target,
                    known.Value.Radius, message.Tti, def != null ? def.Name : "CRUISE", linger);
            }
        }

        // ---- Camera target service ---------------------------------------------------------
        //
        // QoL owns the observation mark and support owns delivery. The target panel consumes
        // this seam instead of reaching into either module.

        private IObservationSource observations;

        private IObservationSource Observations
        {
            get
            {
                if (observations == null) ModuleServices.TryGet(out observations);
                return observations;
            }
        }

        bool ICameraTargetService.Available => Observations != null;
        bool ICameraTargetService.HasMark =>
            Observations != null && Observations.TryGet(out _);
        string ICameraTargetService.Status =>
            Observations != null ? Observations.Status : "NO SENSOR ATTACHED";
        bool ICameraTargetService.CanCapture => Observations != null && Observations.CanCapture;
        bool ICameraTargetService.CanCallAtMark =>
            ArmedAction.HasValue && !pending && Observations != null && Observations.TryGet(out _);
        string ICameraTargetService.ArmedActionName
        {
            get
            {
                if (!ArmedAction.HasValue) return null;
                SupportActionDefinition definition = catalog != null ? catalog.Find(ArmedAction.Value) : null;
                return definition != null ? definition.Name : ArmedAction.Value.ToString();
            }
        }
        ObservationPoint ICameraTargetService.Mark
        {
            get
            {
                if (Observations != null && Observations.TryGet(out ObservationPoint point)) return point;
                return default;
            }
        }
        float ICameraTargetService.AgeSeconds
        {
            get
            {
                if (Observations != null && Observations.TryGet(out ObservationPoint point))
                    return Mathf.Max(0f, Time.unscaledTime - point.RecordedAt);
                return 0f;
            }
        }
        bool ICameraTargetService.Capture() => Observations != null && Observations.Capture();
        void ICameraTargetService.Clear() => Observations?.Clear();
        bool ICameraTargetService.CallAtMark()
        {
            if (!((ICameraTargetService)this).CanCallAtMark) return false;
            RequestAtMark(Observations);
            return true;
        }

        // ---- Ground readiness read-out -----------------------------------------------------

        /// <summary>What Urban Combat may do on the ground for one faction: one position or camp.</summary>
        int IGroundForceReadiness.InsertionCamps(FactionHQ owner) => 1;


        private static string FactionKey(FactionHQ hq)
        {
            string name = hq != null && hq.faction != null && !string.IsNullOrEmpty(hq.faction.factionName)
                ? hq.faction.factionName.ToUpperInvariant()
                : "UNKNOWN ACTOR";
            return name.Length <= 24 ? name : name.Substring(0, 24);
        }
    }
}
