using System;
using System.Collections;
using System.Collections.Generic;
using BepInEx.Configuration;
using BepInEx.Logging;
using BoscaliSummer.Features.Support.Configuration;
using BoscaliSummer.Features.Support.Networking;
using BoscaliSummer.Framework.Contracts;
using BoscaliSummer.Framework.Features;
using BoscaliSummer.Framework.Lifecycle;
using BoscaliSummer.Interop;
using BoscaliSummer.Runtime;
using NuclearOption.Networking;
using UnityEngine;

namespace BoscaliSummer.Features.Support.Runtime
{
    /// <summary>
    /// Validates and dispatches support requests. Everything an action does lives in the
    /// action; this class owns only authority, economy, bounded concurrency and the client's
    /// view of its own request. It also owns the host-authoritative fleet and cyber systems:
    /// launch, move, recall and upgrade commands are validated and charged here, never in a
    /// panel.
    /// </summary>
        internal sealed class SupportManager : MonoBehaviour, ISceneService, ISupportHost, ICameraTargetService
        {
        private const int MaximumStrikeJobs = 2;
        private const int RequestsPerSecond = 2;
        private const int MaximumContactReplies = 16;

        /// <summary>Release travel, in pixels, still read as a click rather than a map drag.</summary>
        private const float ClickSlopPixels = 8f;

        /// <summary>How long a client waits for a reply before reporting the host silent.</summary>
        private const float ReplyTimeout = 5f;

        internal readonly SpaceOperations Space = new SpaceOperations();
        internal readonly CyberEffects Cyber = new CyberEffects();
        internal readonly EwAssets Ew = new EwAssets();
        SpaceOperations ISupportHost.Space => Space;

        public OpsStateMessage OpsState { get; private set; }
        private float opsReceived = -100f;
        public bool OpsStateFresh => Time.unscaledTime - opsReceived < 3f;
        private float nextOpsQuery;
        private int pendingCommand;
        private string pendingCommandLabel;
        private float commandTimeout;

        public void PollOps()
        {
            if (Time.unscaledTime < nextOpsQuery) return;
            nextOpsQuery = Time.unscaledTime + 2f;
            network.QueryOps();
        }

        internal void ReceiveOps(OpsStateMessage state)
        {
            if (state.SatelliteCount > SpaceOperations.MaximumSatellites) return;
            if (!ValidSatelliteArrays(state)) return;

            GameManager.GetLocalPlayer<Player>(out Player player);
            if (player != null && player.HQ != null)
            {
                FactionHQ hq = player.HQ;
                for (int i = 0; i < state.SatelliteCount; i++)
                {
                    if (!Finite(state.StationXs[i]) || !Finite(state.StationZs[i]) ||
                        !Finite(state.OriginXs[i]) || !Finite(state.OriginZs[i]) ||
                        !Finite(state.TransitLeft[i]) || !Finite(state.TransitTotal[i])) continue;
                    if (state.SatelliteRoles[i] > (byte)SatelliteRole.Ew) continue;
                    if (state.SatelliteAltitudes[i] >= Constellation.AltitudeCount) continue;
                    Space.Mirror(hq, state.SatelliteIds[i], (SatelliteRole)state.SatelliteRoles[i],
                        state.SatelliteAltitudes[i], state.StationXs[i], state.StationZs[i],
                        state.OriginXs[i], state.OriginZs[i], state.TransitLeft[i], state.TransitTotal[i],
                        Mathf.Clamp(state.SatelliteFuel[i], 0f, Constellation.MaximumFuel),
                        (SatelliteState)Math.Min(state.SatelliteStates[i], (byte)SatelliteState.Transit));
                }
                Space.RemoveUnlisted(hq, state.SatelliteIds, state.SatelliteCount);
                Space.Mirror(hq, state.Sigint, state.Crypto, state.Disrupt, state.Ew);
            }

            if (state.RequestId != 0 && state.RequestId == pendingCommand)
            {
                pendingCommand = 0;
                SupportResult result = (SupportResult)state.Result;
                Status = result == SupportResult.Accepted
                    ? pendingCommandLabel + " accepted."
                    : pendingCommandLabel + " denied: " + Explain(result) + ".";
            }

            OpsState = state;
            opsReceived = Time.unscaledTime;
        }

        private static bool ValidSatelliteArrays(in OpsStateMessage state)
        {
            int count = state.SatelliteCount;
            return state.SatelliteIds != null && state.SatelliteRoles != null &&
                   state.SatelliteAltitudes != null && state.SatelliteStates != null &&
                   state.SatelliteFuel != null && state.StationXs != null && state.StationZs != null &&
                   state.OriginXs != null && state.OriginZs != null &&
                   state.TransitLeft != null && state.TransitTotal != null &&
                   state.SatelliteIds.Length >= count && state.SatelliteRoles.Length >= count &&
                   state.SatelliteAltitudes.Length >= count && state.SatelliteStates.Length >= count &&
                   state.SatelliteFuel.Length >= count && state.StationXs.Length >= count &&
                   state.StationZs.Length >= count && state.OriginXs.Length >= count &&
                   state.OriginZs.Length >= count && state.TransitLeft.Length >= count &&
                   state.TransitTotal.Length >= count;
        }

        private readonly SupportRequestLedger ledger = new SupportRequestLedger();
        private readonly SupportRequestLedger commandLedger = new SupportRequestLedger();
        private readonly SupportMapGesture mapGesture = new SupportMapGesture();
        private readonly int[] reserved = new int[2];
        private readonly Dictionary<int, int> contactReplies = new Dictionary<int, int>();

        private SupportSettings settings;
        private IPlayerPerks perks;
        private SupportNet network;
        private ManualLogSource logger;
        private ConfigEntry<bool> bypassRequirements;
        private ConfigEntry<bool> disableCooldowns;
        private SupportCatalog catalog;
        private readonly VanillaSupportCatalog vanilla = new VanillaSupportCatalog();

        private int nextRequestId;
        private float pendingSince;
        private bool pending;
        private float localCooldownUntil;

        private string inboundStrikeName;
        private float inboundStrikeImpactTime;
        private float inboundStrikeConfirmedUntil;
        private string statusText = "Designate a grid on the maximised map.";

        private SupportActionId pendingAction;
        private readonly List<ActiveStrikeInfo> activeStrikes = new List<ActiveStrikeInfo>(8);

        private OpsCommand? armedCommand;
        private byte armedArg, armedArg2;

        public IReadOnlyList<ActiveStrikeInfo> ActiveStrikes => activeStrikes;
        public SupportSettings Settings => settings;
        public bool CommandArmed => armedCommand.HasValue;
        public bool CommandPending => pendingCommand != 0;
        public OpsCommand ArmedCommand => armedCommand.GetValueOrDefault();
        public byte ArmedCommandArg => armedArg;
        public byte ArmedCommandArg2 => armedArg2;

        /// <summary>Local faction fleet for the panel and the tactical overlay.</summary>
        public Constellation LocalConstellation
        {
            get
            {
                GameManager.GetLocalPlayer<Player>(out Player player);
                return player == null ? null : Space.ConstellationFor(player.HQ);
            }
        }

        /// <summary>Client-local map focus; the overlay draws this satellite's swath.</summary>
        public int SelectedSatelliteId { get; set; }

        public InfoNetwork LocalInfo
        {
            get
            {
                GameManager.GetLocalPlayer<Player>(out Player player);
                return player == null ? null : Space.InfoFor(player.HQ);
            }
        }

        /// <summary>The local faction's EW asset state, mirrored from the last snapshot —
        /// same pattern as <see cref="LocalConstellation"/>/<see cref="LocalInfo"/>, except an
        /// asset's state is a single byte, so it rides <see cref="OpsState"/> directly rather
        /// than needing its own per-faction model object.</summary>
        public EwAssetState LocalEwAssetState => (EwAssetState)Math.Min(OpsState.EwAssetState, (byte)EwAssetState.Encampment);

        /// <summary>Which satellite role, if any, an ability needs overhead.</summary>
        public static SatelliteRole? CoverageRole(SupportActionId action)
        {
            switch (action)
            {
                case SupportActionId.Recon: return SatelliteRole.Recon;
                case SupportActionId.Artillery: return SatelliteRole.Strike;
                case SupportActionId.Emp: return SatelliteRole.Ew;
                default: return null;
            }
        }

        public bool CoverageNow(SupportActionId action, float x, float z)
        {
            SatelliteRole? role = CoverageRole(action);
            if (!role.HasValue) return true;
            Constellation constellation = LocalConstellation;
            return constellation != null && constellation.Covers(role.Value, x, z);
        }

        /// <summary>Display snapping for zone-targeted actions (Fortify resolves an owned base).</summary>
        public void ResolveMapArea(SupportActionId action, ref GlobalPosition target, ref float radius)
        {
            if (action != SupportActionId.Fortify) return;
            if (!GameManager.GetLocalPlayer<Player>(out Player player)) return;
            Airbase zone = SupportTargeting.NearestOwnedAirbase(player, target.ToLocalPosition(), out float distance);
            if (zone == null || distance > Mathf.Max(650f, zone.GetRadius() * 1.5f)) { radius = 0f; return; }
            target = (zone.center != null ? zone.center.position : zone.transform.position).ToGlobalPosition();
            radius = zone.GetRadius();
        }

        public float GetEffectRadius(SupportActionId action) =>
            GetEffectRadius(action, LocalHQ());

        public float GetEffectRadius(SupportActionId action, FactionHQ owner)
        {
            InfoNetwork info = Space.InfoFor(owner);
            switch (action)
            {
                case SupportActionId.Artillery:
                    return SupportEffectPolicy.RodBlastRadius;
                case SupportActionId.Emp:
                    return settings != null ? settings.EmpRadius.Value : 12000f;
                case SupportActionId.Recon:
                    return settings != null ? settings.ReconRadius.Value : 6000f;
                case SupportActionId.FlareMissile:
                    return settings != null ? settings.FlareBarrageRadius.Value : 4000f;
                case SupportActionId.Fortify:
                    return 650f; // Selection radius; the overlay resolves the actual owned zone.
                case SupportActionId.HackPing:
                    return info != null ? info.Powers.RevealRadius : 3500f;
                case SupportActionId.HackTrack:
                    return info != null ? info.Powers.TrackRadius : 8000f;
                case SupportActionId.HackBlackout:
                    return info != null ? info.Powers.JamRadius : 5000f;
                case SupportActionId.HackGhost:
                case SupportActionId.HackSpoof:
                    return 0f; // Fleet-wide track deception; no area ring to draw.
                default:
                    return 1000f;
            }
        }

        private static FactionHQ LocalHQ()
        {
            GameManager.GetLocalPlayer<Player>(out Player player);
            return player == null ? null : player.HQ;
        }

        public void RegisterActiveStrike(
            int requestId, SupportActionId action, GlobalPosition target, float radius, float etaSeconds, string name, float duration)
        {
            float now = Time.timeSinceLevelLoad;
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

            activeStrikes.Add(new ActiveStrikeInfo(requestId, action, target, radius, now, impact, expiry, name));
            RegisterInboundStrike(name, etaSeconds);
        }

        public void RegisterInboundStrike(string strikeName, float etaSeconds)
        {
            inboundStrikeName = strikeName;
            inboundStrikeImpactTime = Time.timeSinceLevelLoad + etaSeconds;
            inboundStrikeConfirmedUntil = 0f;
        }

        public string Status
        {
            get
            {
                float now = Time.timeSinceLevelLoad;
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
        EwAsset ISupportHost.EwAssetFor(FactionHQ owner) => Ew.ForFaction(owner);

        public IReadOnlyList<SupportActionDefinition> Actions => catalog.Actions;
        public bool BypassRequirements => bypassRequirements != null && bypassRequirements.Value;
        public bool DisableCooldowns => disableCooldowns != null && disableCooldowns.Value;
        public bool RequestPending => pending;

        private IFireSuppressionService fireSuppressionService;

        public string FireTelemetry
        {
            get
            {
                if (fireSuppressionService == null) return string.Empty;
                int active = fireSuppressionService.ActiveFireCount;
                if (active <= 0) return string.Empty;
                string hazard = active >= 6 ? "CRITICAL" : active >= 3 ? "HIGH" : "MODERATE";
                return $"[WILDFIRE CONDITIONS // {active} ACTIVE FRONTS · HAZARD {hazard}]";
            }
        }

        public void Configure(
            SupportSettings supportSettings, IPlayerPerks playerPerks,
            IZoneFortificationService fortifications, SupportNet net, ManualLogSource log,
            IFireSuppressionService fireSuppression = null)
        {
            settings = supportSettings;
            perks = playerPerks;
            network = net;
            logger = log;
            fireSuppressionService = fireSuppression;
            catalog = new SupportCatalog(supportSettings, fortifications, fireSuppression);
        }

        internal void ConfigureBypass(ConfigEntry<bool> bypass) => bypassRequirements = bypass;
        internal void ConfigureDisableCooldowns(ConfigEntry<bool> disable) => disableCooldowns = disable;

        public SupportActionId? ArmedAction { get; private set; }
        public int ArmedFrame { get; private set; }

        public void ResetForScene()
        {
            Visuals.EmpVisualEffect.Reset();
            Visuals.KineticRodStrikeVisuals.Reset();
            Space.Clear();
            Cyber.Clear();
            Ew.Clear();
            OpsState = default;
            opsReceived = -100f;
            nextOpsQuery = 0f;
            pendingCommand = 0;
            pendingCommandLabel = null;
            ledger.Clear();
            commandLedger.Clear();
            contactReplies.Clear();
            Array.Clear(reserved, 0, reserved.Length);
            StopAllCoroutines();
            pending = false;
            localCooldownUntil = 0f;
            ArmedAction = null;
            ArmedFrame = 0;
            armedCommand = null;
            activeStrikes.Clear();
            mapGesture.Reset();
            SupportMapMode.GestureArmed = false;
            Status = "Select support option, then right-click on map.";
        }

        private void OnDestroy()
        {
            ResetForScene();
            SupportMapMode.GestureArmed = false;
        }

        private void Update()
        {
            bool host = GameAccess.IsServer();
            if (host)
            {
                Space.Tick(Time.deltaTime, true);
                Ew.Tick(Time.unscaledTime);
            }
            else
            {
                Space.Tick(Time.unscaledDeltaTime, false);
                // Keep the mirrored constellation warm for the map overlay, not just the panel.
                PollOps();
            }
            Cyber.Tick(Time.timeSinceLevelLoad);

            // Prune expired active strikes
            float now = Time.timeSinceLevelLoad;
            for (int i = activeStrikes.Count - 1; i >= 0; i--)
            {
                if (!activeStrikes[i].IsActive(now))
                    activeStrikes.RemoveAt(i);
            }

            // Publish the armed state for Wing Command to read (BoscaliLink), so a wing
            // point-order and a support call-in never both fire on one right-click.
            SupportMapMode.GestureArmed = (ArmedAction.HasValue || armedCommand.HasValue) && mapGesture.Armed;
            mapGesture.Advance(Time.frameCount);
            if (pending && Time.unscaledTime - pendingSince > ReplyTimeout)
            {
                pending = false;
                Status = "No response from host.";
            }
            if (pendingCommand != 0 && Time.unscaledTime > commandTimeout)
            {
                pendingCommand = 0;
                Status = "No response from host.";
            }

            if (ArmedAction.HasValue || armedCommand.HasValue)
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
                            if (ArmedAction.HasValue)
                            {
                                SupportActionId action = ArmedAction.Value;
                                ArmedAction = null;
                                mapGesture.Complete(Time.frameCount);
                                RequestAt(action, target);
                            }
                            else
                            {
                                OpsCommand command = armedCommand.Value;
                                byte arg = armedArg;
                                byte arg2 = armedArg2;
                                CancelArmed();
                                SendCommand(command, arg, arg2, target);
                            }
                        }
                    }
                }
            }
        }

        // ---- Client view -----------------------------------------------------------------

        public float LocalAllocation =>
            GameManager.GetLocalPlayer<Player>(out Player player) && player != null ? player.Allocation : 0f;

        public float LocalCooldownRemaining =>
            DisableCooldowns ? 0f : Mathf.Max(0f, localCooldownUntil - Time.unscaledTime);

        public float LocalCooldownTotal =>
            DisableCooldowns ? 0f : settings != null ? settings.RequestCooldown.Value : 0f;

        public bool IsAuthorised(SupportActionDefinition action)
        {
            if (BypassRequirements) return true;
            if (!GameManager.GetLocalPlayer<Player>(out Player player) || player == null) return false;
            if (action.IsHack)
            {
                InfoNetwork info = Space.InfoFor(player.HQ);
                HackKind kind = action.Hack.Value;
                return info != null && info.Level(CyberCatalog.Facility(kind)) >= CyberCatalog.RequiredLevel(kind);
            }
            return perks.Grants(PlayerIdentity.Of(player), action.Capability);
        }

        /// <summary>
        /// Price for the local player, including the support-cost perk. The server runs the
        /// same method, so the panel never advertises a number the host will not honour.
        /// </summary>
        public float Cost(SupportActionDefinition action)
        {
            GameManager.GetLocalPlayer<Player>(out Player player);
            return Cost(action, player);
        }

        public float SatelliteCost(SatelliteRole role)
        {
            GameManager.GetLocalPlayer<Player>(out Player player);
            return SatelliteCost(player, role);
        }

        public float FacilityCost(FacilityId facility)
        {
            InfoNetwork info = LocalInfo;
            if (info == null || !info.CanUpgrade(facility)) return 0f;
            GameManager.GetLocalPlayer<Player>(out Player player);
            return player == null ? 0f
                : info.UpgradeCost(facility) * Price(player, settings.CostMultiplier.Value);
        }

        public float EwTruckCost()
        {
            GameManager.GetLocalPlayer<Player>(out Player player);
            if (player == null || settings == null) return 0f;
            return settings.EwTruckCost.Value * Price(player, settings.CostMultiplier.Value);
        }

        private float SatelliteCost(Player player, SatelliteRole role)
        {
            if (player == null || settings == null) return 0f;
            float baseCost = role == SatelliteRole.Recon ? settings.SatelliteReconCost.Value
                : role == SatelliteRole.Strike ? settings.SatelliteStrikeCost.Value
                : settings.SatelliteEwCost.Value;
            return baseCost * Price(player, settings.CostMultiplier.Value);
        }

        private float Price(Player player, float baseCost)
        {
            if (baseCost <= 0f || player == null) return baseCost;
            return baseCost * EventsCostMultiplier(player) *
                   perks.Multiplier(PlayerIdentity.Of(player), PerkEffect.SupportCost);
        }

        /// <summary>
        /// The Events module's live world modifier for one player, resolved late so neither
        /// module has to install first. Exactly 1 when Events is absent or the theater is calm.
        /// </summary>
        internal static float EventsCostMultiplier(Player player) =>
            player != null && ModServices.TryGet<IActiveEventsView>(out IActiveEventsView events)
                ? events.SupportCostMultiplierFor(PlayerIdentity.Of(player))
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
            armedCommand = null;
            ArmedFrame = Time.frameCount;
            Status = "ARMED: " + name + " — Right-click on map to execute (ESC to cancel).";
        }

        /// <summary>Arms the map for a fleet command: launch at a point, or move a satellite.</summary>
        public void ArmCommand(OpsCommand command, byte arg, byte arg2, string label)
        {
            if (!TryArmMap(label + " · RIGHT-CLICK MAP")) return;
            armedCommand = command;
            armedArg = arg;
            armedArg2 = arg2;
            ArmedAction = null;
            ArmedFrame = Time.frameCount;
            Status = "ARMED: " + label + " — Right-click on map to confirm (ESC to cancel).";
        }

        private bool TryArmMap(string prompt)
        {
            if (pendingCommand != 0)
            {
                Status = "COMMAND PENDING — wait for host acknowledgement.";
                return false;
            }
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

        public void RequestRecall(byte satelliteId)
        {
            SendCommand(OpsCommand.Recall, satelliteId, 0, default);
        }

        public void RequestUpgrade(FacilityId facility)
        {
            SendCommand(OpsCommand.Upgrade, (byte)facility, 0, default);
        }

        private void SendCommand(OpsCommand command, byte arg, byte arg2, GlobalPosition target)
        {
            int requestId = ++nextRequestId;
            pendingCommand = requestId;
            pendingCommandLabel = CommandLabel(command, arg, arg2);
            commandTimeout = Time.unscaledTime + ReplyTimeout;
            Status = pendingCommandLabel + " sent to host.";
            network.Command(requestId, command, arg, arg2, target);
        }

        private string CommandLabel(OpsCommand command, byte arg, byte arg2)
        {
            switch (command)
            {
                case OpsCommand.Launch:
                    return "LAUNCH " + RoleName((SatelliteRole)arg2) + " SATELLITE";
                case OpsCommand.Move:
                    return "ORBIT BURN";
                case OpsCommand.Recall:
                    return "SATELLITE RECALL";
                case OpsCommand.EwDeploy:
                    return "DEPLOY EW TRUCK";
                case OpsCommand.EwReposition:
                    return "EW TRUCK REPOSITION";
                default:
                    return "BUILD " + InfoNetwork.Facility((FacilityId)arg).Name;
            }
        }

        private static string RoleName(SatelliteRole role)
        {
            switch (role)
            {
                case SatelliteRole.Recon: return "RECON";
                case SatelliteRole.Strike: return "STRIKE";
                default: return "EW";
            }
        }

        public void Disarm()
        {
            if (!ArmedAction.HasValue && !armedCommand.HasValue) return;
            CancelArmed();
            Status = "Support request cancelled.";
        }

        private void CancelArmed()
        {
            ArmedAction = null;
            armedCommand = null;
            mapGesture.Complete(Time.frameCount);
        }

        public void Request(SupportActionId action)
        {
            Arm(action);
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
            CancelArmed();
            RequestAt(action, new GlobalPosition(point.X, point.Y, point.Z));
        }

        public void RequestAt(SupportActionId action, GlobalPosition target)
        {
            if (pending)
            {
                Status = "REQUEST PENDING — wait for host acknowledgement.";
                return;
            }

            SupportActionDefinition def = catalog != null ? catalog.Find(action) : null;
            if (def == null || !def.Enabled)
            {
                Status = "Action unavailable.";
                return;
            }

            float cost = Cost(def);
            if (cost <= 0f)
            {
                Status = "Action unavailable on this map.";
                return;
            }

            if (!IsAuthorised(def))
            {
                Status = def.IsHack ? "Infrastructure not built (see CYBER)." : "Action not authorised.";
                return;
            }

            if (LocalCooldownRemaining > 0.5f)
            {
                Status = "Support network cooling down.";
                return;
            }

            if (!BypassRequirements && LocalAllocation + 0.001f < cost)
            {
                Status = "Insufficient allocation (" + cost.ToString("0") + " required).";
                return;
            }

            pending = true;
            pendingSince = Time.unscaledTime;
            pendingAction = action;
            Status = "Request sent to grid " + Mathf.RoundToInt(target.x) + " / " + Mathf.RoundToInt(target.z) + ".";
            network.Request(++nextRequestId, action, target);
        }

        /// <summary>Called when the request could not leave this machine at all.</summary>
        internal void ReportOffline()
        {
            pending = false;
            pendingCommand = 0;
            Status = "No host connection.";
        }

        internal void ReceiveResult(SupportResultMessage message)
        {
            if (!pending || message.RequestId != nextRequestId || message.Action != (byte)pendingAction) return;
            pending = false;
            SupportResult result = (SupportResult)message.Result;
            SupportActionDefinition action = catalog.Find((SupportActionId)message.Action);
            string name = action != null ? action.Name : "Support";
            if (result == SupportResult.Accepted)
            {
                localCooldownUntil = DisableCooldowns ? 0f : Time.unscaledTime + message.CooldownSeconds;
                bool sweep = action != null &&
                    (action.Id == SupportActionId.Recon || action.Hack == HackKind.Ping);
                Status = sweep
                    ? name + " complete: " + Mathf.Max(0, message.Contacts) + " contact(s)."
                    : name + " accepted.";
                float eta = action != null && action.Id == SupportActionId.Artillery ? 8f :
                            action != null && action.Id == SupportActionId.Emp ? SupportEffectPolicy.EmpDelay :
                            action != null && action.Id == SupportActionId.FlareMissile ? 5.5f : 0f;
                if (!Finite(message.Radius) || message.Radius < 0f || message.Radius > 200000f ||
                    !Finite(message.Duration) || message.Duration < 0f || message.Duration > 60f ||
                    !Finite(message.X) || !Finite(message.Y) || !Finite(message.Z)) return;
                RegisterActiveStrike(message.RequestId, (SupportActionId)message.Action,
                    new GlobalPosition(message.X, message.Y, message.Z), message.Radius, eta, name, message.Duration);
            }
            else
            {
                Status = name + " denied: " + Explain(result) + ".";
            }
        }

        internal static string Explain(SupportResult result)
        {
            switch (result)
            {
                case SupportResult.OutOfCoverage: return "no satellite coverage — task one in SPACE";
                case SupportResult.NotBuilt: return "infrastructure not built in CYBER";
                case SupportResult.NoEwAsset: return "needs an EW asset near the target";
                case SupportResult.Disabled: return "action disabled";
                case SupportResult.NotUnlocked: return "not authorised";
                case SupportResult.InvalidTarget: return "unusable target";
                case SupportResult.OutOfRange: return "target out of range";
                case SupportResult.NotAirborne: return "you must be in an aircraft";
                case SupportResult.InsufficientAllocation: return "not enough allocation";
                case SupportResult.NoStock: return "no stock at HQ";
                case SupportResult.Cooldown: return "cooling down";
                case SupportResult.Busy: return "too many jobs in flight";
                case SupportResult.Duplicate: return "already handled";
                case SupportResult.CapabilityUnavailable: return "unavailable on this map";
                case SupportResult.SpawnFailed: return "could not be delivered";
                case SupportResult.RateLimited: return "too many requests";
                default: return result.ToString();
            }
        }

        // ---- Server ----------------------------------------------------------------------

        internal SupportResult Evaluate(Player player, SupportRequestMessage request)
        {
            if (player == null || player.HQ == null) return SupportResult.InvalidTarget;
            if (!Finite(request.X) || !Finite(request.Y) || !Finite(request.Z) ||
                Math.Abs(request.X) > 10000000f || Math.Abs(request.Y) > 10000000f || Math.Abs(request.Z) > 10000000f)
                return SupportResult.InvalidTarget;

            SupportActionDefinition action = catalog.Find((SupportActionId)request.Action);
            if (action == null) return SupportResult.CapabilityUnavailable;

            ulong playerId = PlayerIdentity.Of(player);
            float now = Time.unscaledTime;
            bool bypass = BypassRequirements;

            if (ledger.WasAccepted(playerId, request.RequestId)) return SupportResult.Duplicate;
            if (!DisableCooldowns && ledger.IsRateLimited(playerId, now, RequestsPerSecond, 1f)) return SupportResult.RateLimited;
            if (!action.Enabled) return SupportResult.Disabled;
            if (!bypass && !HostAuthorised(player, action)) return action.IsHack ? SupportResult.NotBuilt : SupportResult.NotUnlocked;
            if (!DisableCooldowns && ledger.IsCoolingDown(playerId, now, settings.RequestCooldown.Value))
                return SupportResult.Cooldown;

            var context = new SupportContext(
                player, new GlobalPosition(request.X, request.Y, request.Z), request.RequestId, this);
            float cost = Cost(action, player);
            if (cost <= 0f) return SupportResult.CapabilityUnavailable;
            if (!bypass && player.Allocation + 0.001f < cost) return SupportResult.InsufficientAllocation;

            SupportResult result = action.Action.Execute(context);
            if (result != SupportResult.Accepted)
            {
                logger.LogWarning("[Support] " + action.Name + " request " + request.RequestId +
                    " rejected: " + result + ".");
                return result;
            }

            if (!bypass) player.SetAllocation(Mathf.Max(0f, player.Allocation - cost));
            ledger.Accept(playerId, request.RequestId, now);
            logger.LogInfo("[Support] Accepted " + action.Name + " request " + request.RequestId +
                " from " + player + " at " + context.Target + " for " + Mathf.RoundToInt(cost) + " alloc.");
            return SupportResult.Accepted;
        }

        private bool HostAuthorised(Player player, SupportActionDefinition action)
        {
            if (!action.IsHack)
                return perks.Grants(PlayerIdentity.Of(player), action.Capability);
            InfoNetwork info = Space.InfoFor(player.HQ);
            HackKind kind = action.Hack.Value;
            return info != null && info.Level(CyberCatalog.Facility(kind)) >= CyberCatalog.RequiredLevel(kind);
        }

        /// <summary>Host validation for one fleet or infrastructure command.</summary>
        internal SupportResult EvaluateCommand(Player player, OpsCommandMessage message)
        {
            if (player == null || player.HQ == null) return SupportResult.InvalidTarget;
            if (!Finite(message.X) || !Finite(message.Z) ||
                Math.Abs(message.X) > 10000000f || Math.Abs(message.Z) > 10000000f)
                return SupportResult.InvalidTarget;

            ulong playerId = PlayerIdentity.Of(player);
            float now = Time.unscaledTime;
            bool bypass = BypassRequirements;

            if (commandLedger.WasAccepted(playerId, message.RequestId)) return SupportResult.Duplicate;
            if (!DisableCooldowns && commandLedger.IsRateLimited(playerId, now, RequestsPerSecond, 1f))
                return SupportResult.RateLimited;

            switch ((OpsCommand)message.Command)
            {
                case OpsCommand.Launch:
                {
                    SatelliteRole role = (SatelliteRole)message.Arg2;
                    if (role > SatelliteRole.Ew) return SupportResult.InvalidTarget;
                    if (message.Arg >= Constellation.AltitudeCount) return SupportResult.InvalidTarget;
                    float cost = SatelliteCost(player, role);
                    if (cost <= 0f) return SupportResult.CapabilityUnavailable;
                    if (!bypass && player.Allocation + 0.001f < cost) return SupportResult.InsufficientAllocation;
                    OrbitalFailure failure = Space.Deploy(player.HQ, role, message.Arg, message.X, message.Z,
                        settings.MaximumSatellites.Value, settings.SatelliteLaunchTransitSeconds.Value,
                        out Satellite satellite);
                    if (failure != OrbitalFailure.None)
                        return failure == OrbitalFailure.AtCapacity ? SupportResult.Busy : SupportResult.InvalidTarget;
                    satellite.Paid = bypass ? 0f : cost;
                    if (!bypass) player.SetAllocation(Mathf.Max(0f, player.Allocation - cost));
                    break;
                }
                case OpsCommand.Move:
                {
                    OrbitalFailure failure = Space.Retask(player.HQ, message.Arg, message.X, message.Z, out float fuel);
                    if (failure != OrbitalFailure.None)
                        return failure == OrbitalFailure.InsufficientFuel ? SupportResult.Busy : SupportResult.InvalidTarget;
                    logger.LogInfo("[Support] Station transfer on satellite " + message.Arg + " costing " +
                        Mathf.RoundToInt(fuel) + "% fuel.");
                    break;
                }
                case OpsCommand.Recall:
                {
                    if (!Space.Recall(player.HQ, message.Arg, out Satellite satellite))
                        return SupportResult.InvalidTarget;
                    float refund = Mathf.Clamp01(settings.SatelliteRecallRefund.Value) * satellite.Paid;
                    if (refund > 0f && !bypass) player.SetAllocation(player.Allocation + refund);
                    logger.LogInfo("[Support] Satellite " + message.Arg + " recalled; refunded " +
                        Mathf.RoundToInt(refund) + " alloc.");
                    break;
                }
                case OpsCommand.Upgrade:
                {
                    if (message.Arg >= InfoNetwork.Facilities.Length) return SupportResult.InvalidTarget;
                    InfoNetwork info = Space.InfoFor(player.HQ);
                    if (info == null) return SupportResult.CapabilityUnavailable;
                    FacilityId facility = (FacilityId)message.Arg;
                    if (!info.CanUpgrade(facility)) return SupportResult.NotBuilt;
                    float cost = info.UpgradeCost(facility) * Price(player, settings.CostMultiplier.Value);
                    if (!bypass && player.Allocation + 0.001f < cost) return SupportResult.InsufficientAllocation;
                    if (!info.TryUpgrade(facility)) return SupportResult.NotBuilt;
                    if (!bypass) player.SetAllocation(Mathf.Max(0f, player.Allocation - cost));
                    logger.LogInfo("[Support] " + InfoNetwork.Facility(facility).Name + " upgraded to LV" +
                        info.Level(facility) + " for " + Mathf.RoundToInt(cost) + " alloc.");
                    break;
                }
                case OpsCommand.EwDeploy:
                {
                    if (!settings.EwEnabled.Value) return SupportResult.Disabled;
                    if (Ew.ForFaction(player.HQ) != null) return SupportResult.Busy;
                    if (!SupportTargeting.TryGround(new GlobalPosition(message.X, 0f, message.Z), out Vector3 ground))
                        return SupportResult.InvalidTarget;
                    VehicleDefinition definition = vanilla.EwTruck();
                    if (definition == null || definition.unitPrefab == null || NetworkSceneSingleton<Spawner>.i == null)
                        return SupportResult.CapabilityUnavailable;
                    Airbase depot = SupportTargeting.NearestOwnedAirbase(player, ground, out _);
                    if (depot == null) return SupportResult.InvalidTarget;
                    Vector3 spawnPoint = depot.center != null ? depot.center.position : depot.transform.position;
                    float cost = settings.EwTruckCost.Value * Price(player, settings.CostMultiplier.Value);
                    if (!bypass && player.Allocation + 0.001f < cost) return SupportResult.InsufficientAllocation;
                    Vector3 facing = ground - spawnPoint;
                    facing.y = 0f;
                    Quaternion rotation = facing.sqrMagnitude > 1f
                        ? Quaternion.LookRotation(facing.normalized) : Quaternion.identity;
                    GroundVehicle truck = NetworkSceneSingleton<Spawner>.i.SpawnVehicle(
                        definition.unitPrefab, spawnPoint.ToGlobalPosition(), rotation, Vector3.zero,
                        player.HQ, "BoscaliSummer:Support:EwTruck:" + PlayerIdentity.Of(player) + ":" + message.RequestId,
                        1f, true, player);
                    if (truck == null || truck.UnitCommand == null) return SupportResult.SpawnFailed;
                    if (!Ew.TryDeployTruck(player.HQ, truck, bypass ? 0f : cost)) return SupportResult.Busy;
                    truck.UnitCommand.SetDestination(ground.ToGlobalPosition(), false);
                    if (!bypass) player.SetAllocation(Mathf.Max(0f, player.Allocation - cost));
                    logger.LogInfo("[Support] EW truck deployed from depot, en route, for " +
                        Mathf.RoundToInt(cost) + " alloc.");
                    break;
                }
                case OpsCommand.EwReposition:
                {
                    if (!settings.EwEnabled.Value) return SupportResult.Disabled;
                    EwAsset asset = Ew.ForFaction(player.HQ);
                    if (asset == null || asset.State != EwAssetState.Truck || asset.Truck == null ||
                        asset.Truck.UnitCommand == null)
                        return SupportResult.InvalidTarget;
                    if (!SupportTargeting.TryGround(new GlobalPosition(message.X, 0f, message.Z), out Vector3 ground))
                        return SupportResult.InvalidTarget;
                    asset.Truck.UnitCommand.SetDestination(ground.ToGlobalPosition(), false);
                    break;
                }
                default:
                    return SupportResult.InvalidTarget;
            }

            commandLedger.Accept(playerId, message.RequestId, now);
            return SupportResult.Accepted;
        }

        internal float ServerCooldown => DisableCooldowns ? 0f : settings.RequestCooldown.Value;

        /// <summary>
        /// Price for one player. No action prices itself from the target, so costing uses a
        /// bare context and both the panel and the host reach the same number.
        /// </summary>
        private float Cost(SupportActionDefinition action, Player player)
        {
            if (player == null) return 0f;
            float baseCost = action.Action.BaseCost(new SupportContext(player, default, 0, this));
            if (baseCost <= 0f) return 0f;
            return baseCost * EventsCostMultiplier(player) *
                   perks.Multiplier(PlayerIdentity.Of(player), PerkEffect.SupportCost);
        }

        // ---- Host services ---------------------------------------------------------------

        bool ISupportHost.TryReserve(SupportPool pool)
        {
            if (reserved[(int)pool] >= MaximumStrikeJobs) return false;
            reserved[(int)pool]++;
            return true;
        }

        void ISupportHost.Release(SupportPool pool) =>
            reserved[(int)pool] = Math.Max(0, reserved[(int)pool] - 1);

        void ISupportHost.Run(IEnumerator routine) => StartCoroutine(routine);

        void ISupportHost.ReportContacts(int requestId, int contacts)
        {
            if (contactReplies.Count >= MaximumContactReplies) contactReplies.Clear();
            contactReplies[requestId] = contacts;
        }

        internal int TakeContacts(int requestId)
        {
            if (!contactReplies.TryGetValue(requestId, out int contacts)) return -1;
            contactReplies.Remove(requestId);
            return contacts;
        }

        bool ISupportHost.BeginDeception(Player caster, HackKind kind, GlobalPosition target, float duration)
        {
            if (caster == null || caster.HQ == null) return false;
            if (!Cyber.Begin(kind, caster.HQ, target.x, target.z, duration, Time.timeSinceLevelLoad))
                return false;
            network.BroadcastCyberEffect(kind, caster.HQ.faction != null ? caster.HQ.faction.factionName : string.Empty,
                target.x, target.z, duration);
            return true;
        }

        internal void ReceiveCyberEffect(CyberEffectMessage message)
        {
            float duration = Mathf.Clamp(message.Duration, 1f, 60f);
            if (!Finite(message.X) || !Finite(message.Z) || Math.Abs(message.X) > 10000000f ||
                Math.Abs(message.Z) > 10000000f) return;
            FactionHQ attacker;
            try { attacker = FactionRegistry.HqFromName(message.FactionName); }
            catch (Exception) { attacker = null; }
            if (attacker == null) return;
            if (message.Kind > (byte)HackKind.Spoof) return;
            Cyber.Begin((HackKind)message.Kind, attacker, message.X, message.Z, duration, Time.timeSinceLevelLoad);
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
                if (observations == null) ModServices.TryGet(out observations);
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

        internal OpsStateMessage Snapshot(Player player, int requestId, SupportResult result)
        {
            Constellation constellation = player != null ? Space.ConstellationFor(player.HQ) : null;
            InfoNetwork info = player != null ? Space.InfoFor(player.HQ) : null;
            int count = constellation != null ? constellation.Satellites.Count : 0;
            var message = new OpsStateMessage
            {
                SatelliteCount = (byte)count,
                RequestId = requestId,
                Result = (byte)result,
                SatelliteIds = new byte[SpaceOperations.MaximumSatellites],
                SatelliteRoles = new byte[SpaceOperations.MaximumSatellites],
                SatelliteAltitudes = new byte[SpaceOperations.MaximumSatellites],
                SatelliteStates = new byte[SpaceOperations.MaximumSatellites],
                SatelliteFuel = new byte[SpaceOperations.MaximumSatellites],
                StationXs = new float[SpaceOperations.MaximumSatellites],
                StationZs = new float[SpaceOperations.MaximumSatellites],
                OriginXs = new float[SpaceOperations.MaximumSatellites],
                OriginZs = new float[SpaceOperations.MaximumSatellites],
                TransitLeft = new float[SpaceOperations.MaximumSatellites],
                TransitTotal = new float[SpaceOperations.MaximumSatellites]
            };
            for (int i = 0; i < count; i++)
            {
                Satellite satellite = constellation.Satellites[i];
                message.SatelliteIds[i] = satellite.Id;
                message.SatelliteRoles[i] = (byte)satellite.Role;
                message.SatelliteAltitudes[i] = satellite.Altitude;
                message.SatelliteStates[i] = (byte)satellite.State;
                message.SatelliteFuel[i] = (byte)Mathf.Clamp(Mathf.RoundToInt(satellite.Fuel), 0, 100);
                message.StationXs[i] = satellite.StationX;
                message.StationZs[i] = satellite.StationZ;
                message.OriginXs[i] = satellite.OriginX;
                message.OriginZs[i] = satellite.OriginZ;
                message.TransitLeft[i] = satellite.TransitLeft;
                message.TransitTotal[i] = satellite.TransitTotal;
            }
            if (info != null)
            {
                message.Sigint = (byte)info.Level(FacilityId.Sigint);
                message.Crypto = (byte)info.Level(FacilityId.Crypto);
                message.Disrupt = (byte)info.Level(FacilityId.Disrupt);
                message.Ew = (byte)info.Level(FacilityId.Ew);
            }
            message.EwAssetState = player != null ? Ew.StateByteFor(player.HQ) : (byte)0;
            return message;
        }

        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
