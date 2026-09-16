using System;
using System.Collections;
using System.Collections.Generic;
using BepInEx.Configuration;
using BepInEx.Logging;
using BoscaliSummer.Features.Support.Configuration;
using BoscaliSummer.Features.Support.Domain;
using BoscaliSummer.Features.Support.Domain.Orbital;
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
    /// view of its own request. It also owns the host-authoritative orbital stations, cyber and
    /// program systems: launch, jettison, burn, resupply, upgrade and invest commands are
    /// validated and charged here, never in a panel.
    /// </summary>
        internal sealed class SupportManager : MonoBehaviour, ISceneService, ISupportHost, ICameraTargetService,
            IGroundForceReadiness
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
        private OpsCommand? pendingCommandKind;
        private byte pendingCommandArg;
        private float commandTimeout;

        public void PollOps()
        {
            if (Time.unscaledTime < nextOpsQuery) return;
            nextOpsQuery = Time.unscaledTime + 2f;
            network.QueryOps();
        }

        internal void ReceiveOps(OpsStateMessage state)
        {
            if (!OpsStateMessageBuffers.ValidArrays(state)) return;
            if (state.ProgramTiers == null || state.ProgramTiers.Length < OpsProgramLedger.ProgramCount) return;
            if (state.GarrisonLevels == null || state.GarrisonLevels.Length < OpsGarrison.UpgradeCount) return;
            if (!Finite(state.EwX) || !Finite(state.EwZ)) return;

            GameManager.GetLocalPlayer<Player>(out Player player);
            if (player != null && player.HQ != null)
            {
                FactionHQ hq = player.HQ;
                double now = OrbitNow;
                // The host's own station is the authority: rebasing its clocks from a float
                // snapshot would only add rounding. Clients rebuild everything from the snapshot.
                if (!GameAccess.IsServer())
                {
                    OpsStateMessageBuffers.Read(state, mirrorSnapshot);
                    Space.MirrorPlatform(hq, mirrorSnapshot, now);
                }
                Space.MirrorForeign(state.ForeignRegimes, state.ForeignSeeds, state.ForeignClocks, state.ForeignLayouts,
                    state.ForeignCount, now);
                Space.Mirror(hq, state.Sigint, state.Crypto, state.Disrupt, state.Ew);
                // The host ledger is the authority; mirroring its own quantised bytes back
                // would round away accrual progress on every poll.
                if (!GameAccess.IsServer())
                    Space.MirrorPrograms(hq, state.ProgramTiers, state.SpecOpsTokens, state.IntelTokens,
                        state.SpecOpsProgress, state.IntelProgress);
                if (!GameAccess.IsServer())
                    Space.MirrorGarrison(hq, state.GarrisonLevels);
            }

            if (state.RequestId != 0 && state.RequestId == pendingCommand)
            {
                pendingCommand = 0;
                SupportResult result = (SupportResult)state.Result;
                Status = result == SupportResult.Accepted
                    ? AcceptedStatus()
                    : pendingCommandLabel + " denied: " + Explain(result) + ".";
                pendingCommandKind = null;
                pendingCommandArg = 0;
            }

            OpsState = state;
            opsReceived = Time.unscaledTime;
        }

        /// <summary>
        /// A garrison upgrade's reply names the rank and effect that were bought. The client
        /// has already mirrored the snapshot that carried the new rank; the host reads its
        /// own ledger.
        /// </summary>
        private string AcceptedStatus()
        {
            if (pendingCommandKind != OpsCommand.GarrisonUpgrade ||
                pendingCommandArg >= OpsGarrison.UpgradeCount)
                return pendingCommandLabel + " accepted.";

            var upgrade = (GarrisonUpgradeId)pendingCommandArg;
            GameManager.GetLocalPlayer<Player>(out Player player);
            OpsGarrison garrison = player != null ? Space.GarrisonFor(player.HQ) : null;
            int rank = garrison != null ? garrison.Rank(upgrade) : 0;
            return rank > 0
                ? OpsGarrison.Info(upgrade).Name + " RAISED TO " + OpsGarrison.RankLabel(rank) + " · " +
                  OpsGarrison.EffectLabel(upgrade, rank) + "."
                : pendingCommandLabel + " accepted.";
        }

        private readonly PlatformSnapshot mirrorSnapshot = new PlatformSnapshot();
        private readonly PlatformSnapshot exportSnapshot = new PlatformSnapshot();

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
        private Action<GlobalPosition> localPick;

        public IReadOnlyList<ActiveStrikeInfo> ActiveStrikes => activeStrikes;
        public SupportSettings Settings => settings;
        public bool CommandArmed => armedCommand.HasValue;
        public bool CommandPending => pendingCommand != 0;
        public OpsCommand ArmedCommand => armedCommand.GetValueOrDefault();
        public byte ArmedCommandArg => armedArg;
        public byte ArmedCommandArg2 => armedArg2;

        /// <summary>The local faction's station for the console, the uplink, the sky and the map.</summary>
        public OrbitalPlatform LocalPlatform
        {
            get
            {
                GameManager.GetLocalPlayer<Player>(out Player player);
                return player == null ? null : Space.PlatformFor(player.HQ);
            }
        }

        /// <summary>The clock passes are computed against. Scene-local and reset with the stations.</summary>
        public double OrbitNow => Time.timeSinceLevelLoadAsDouble;

        public OrbitClock OrbitClock => settings == null
            ? OrbitClock.Default
            : new OrbitClock(settings.OrbitGapScale.Value);

        double ISupportHost.OrbitNow => OrbitNow;
        OrbitClock ISupportHost.OrbitClock => OrbitClock;

        /// <summary>Client-local uplink aim, remembered between uplink sessions; theatre centre until set.</summary>
        public GlobalPosition UplinkAim { get; private set; }

        public bool UplinkAimSet { get; private set; }

        public void SetUplinkAim(GlobalPosition point)
        {
            UplinkAim = point;
            UplinkAimSet = true;
        }

        /// <summary>Latest accepted radar scan of the local player, for the uplink and PLATFORM products.</summary>
        public int RadarScanSerial { get; private set; }
        public GlobalPosition RadarScanTarget { get; private set; }
        public int RadarScanContacts { get; private set; }

        public InfoNetwork LocalInfo
        {
            get
            {
                GameManager.GetLocalPlayer<Player>(out Player player);
                return player == null ? null : Space.InfoFor(player.HQ);
            }
        }

        /// <summary>The local faction's EW asset state, mirrored from the last snapshot —
        /// same pattern as <see cref="LocalFleet"/>/<see cref="LocalInfo"/>, except an
        /// asset's state is a single byte, so it rides <see cref="OpsState"/> directly rather
        /// than needing its own per-faction model object.</summary>
        public EwAssetState LocalEwAssetState => (EwAssetState)Math.Min(OpsState.EwAssetState, (byte)EwAssetState.Encampment);

        /// <summary>The local station posture as the host last reported it.</summary>
        public EwPosture LocalEwPosture => EwPostures.Clamp(OpsState.EwPosture);

        /// <summary>The local faction SPEC OPS and INTEL programs; mirrored on clients.</summary>
        public OpsProgramLedger LocalPrograms
        {
            get
            {
                GameManager.GetLocalPlayer<Player>(out Player player);
                return player == null ? null : Space.ProgramsFor(player.HQ);
            }
        }

        /// <summary>The local faction base of operations; the host's own ledger or a mirror.</summary>
        public OpsGarrison LocalGarrison
        {
            get
            {
                GameManager.GetLocalPlayer<Player>(out Player player);
                return player == null ? null : Space.GarrisonFor(player.HQ);
            }
        }

        /// <summary>Which station ability, if any, a support action needs.</summary>
        public static PlatformAbility? OrbitalAbility(SupportActionId action)
        {
            switch (action)
            {
                case SupportActionId.Recon: return PlatformAbility.RadarScan;
                case SupportActionId.ElintSweep: return PlatformAbility.Elint;
                case SupportActionId.Artillery: return PlatformAbility.RodStrike;
                case SupportActionId.Emp: return PlatformAbility.EmpBurst;
                default: return null;
            }
        }

        /// <summary>Client prediction of the host's station check for an ability, same model and clock rules.</summary>
        public PlatformDenial PlatformCheck(PlatformAbility ability)
        {
            OrbitalPlatform platform = LocalPlatform;
            return platform == null ? PlatformDenial.NoPlatform : platform.Check(ability, OrbitNow, OrbitClock);
        }

        /// <summary>The station check for a support action; <see cref="PlatformDenial.None"/> when it needs no station.</summary>
        public PlatformDenial PlatformCheck(SupportActionId action)
        {
            PlatformAbility? ability = OrbitalAbility(action);
            return ability.HasValue ? PlatformCheck(ability.Value) : PlatformDenial.None;
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
            OrbitalPlatform platform = Space.PlatformFor(owner);
            bool station = platform != null && platform.Exists;
            switch (action)
            {
                case SupportActionId.Artillery:
                    return SupportEffectPolicy.RodBlastRadius;
                case SupportActionId.Emp:
                    return (settings != null ? settings.EmpRadius.Value : 12000f) * (station ? platform.EmpScale : 1f);
                case SupportActionId.Recon:
                    return (settings != null ? settings.SarSceneRadius.Value : 1000f) *
                           (station ? platform.ScanScale(OrbitNow) : 1f);
                case SupportActionId.ElintSweep:
                    return (settings != null ? settings.ElintRadius.Value : 8000f) *
                           (station ? platform.ElintScale(OrbitNow) : 1f);
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
            catalog = new SupportCatalog(supportSettings, fortifications);
        }

        internal void ConfigureBypass(ConfigEntry<bool> bypass) => bypassRequirements = bypass;
        internal void ConfigureDisableCooldowns(ConfigEntry<bool> disable) => disableCooldowns = disable;

        public SupportActionId? ArmedAction { get; private set; }
        public int ArmedFrame { get; private set; }

        public void ResetForScene()
        {
            Visuals.EmpVisualEffect.Reset();
            Visuals.KineticRodStrikeVisuals.Reset();
            Visuals.FlareMissileBurstVisuals.Reset();
            Visuals.SupportParticles.Reset();
            Space.Clear();
            mirrorSnapshot.Clear();
            UplinkAim = default;
            UplinkAimSet = false;
            Cyber.Clear();
            Ew.Clear();
            OpsState = default;
            opsReceived = -100f;
            nextOpsQuery = 0f;
            pendingCommand = 0;
            pendingCommandLabel = null;
            pendingCommandKind = null;
            pendingCommandArg = 0;
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
                LevelInfo level = NetworkSceneSingleton<LevelInfo>.i;
                Space.TickHost(OrbitNow, Time.deltaTime, level != null && level.isDayLight, OrbitClock,
                    settings != null && settings.PlatformDebrisEvents.Value, LogDebris);
                Ew.Tick(Time.unscaledTime);
            }
            else
            {
                // Keep the mirrored station warm for the sky and the map overlay, not just the panel.
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
            bool anyArmed = ArmedAction.HasValue || armedCommand.HasValue || localPick != null;
            SupportMapMode.GestureArmed = anyArmed && mapGesture.Armed;
            mapGesture.Advance(Time.frameCount);
            if (pending && Time.unscaledTime - pendingSince > ReplyTimeout)
            {
                pending = false;
                Status = "No response from host.";
            }
            if (pendingCommand != 0 && Time.unscaledTime > commandTimeout)
            {
                pendingCommand = 0;
                pendingCommandKind = null;
                pendingCommandArg = 0;
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
                            else if (ArmedAction.HasValue)
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

        /// <summary>
        /// The cooldown this peer would show: the host's configured seconds scaled by the
        /// local player's own re-tasking perk, so the countdown matches what the host applies.
        /// </summary>
        public float LocalCooldownTotal =>
            DisableCooldowns ? 0f : settings != null ? CooldownFor(
                GameManager.GetLocalPlayer<Player>(out Player local) ? local : null) : 0f;

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

        /// <summary>Launch price of a module, the core or cargo for the local player.</summary>
        public float LaunchCost(ModuleKind kind)
        {
            GameManager.GetLocalPlayer<Player>(out Player player);
            return LaunchCost(player, kind);
        }

        /// <summary>The share of what was paid a jettison refunds.</summary>
        public float JettisonRefund => settings != null ? Mathf.Clamp01(settings.PlatformJettisonRefund.Value) : 0f;

        public float FacilityCost(FacilityId facility)
        {
            InfoNetwork info = LocalInfo;
            if (info == null || !info.CanUpgrade(facility)) return 0f;
            GameManager.GetLocalPlayer<Player>(out Player player);
            return player == null ? 0f
                : info.UpgradeCost(facility) * Price(player, settings.CostMultiplier.Value);
        }

        /// <summary>Next-tier price for the local player, with the same multipliers the host charges.</summary>
        public float ProgramCost(OpsProgramId program)
        {
            OpsProgramLedger programs = LocalPrograms;
            if (programs == null || settings == null || !programs.CanInvest(program)) return 0f;
            GameManager.GetLocalPlayer<Player>(out Player player);
            return player == null ? 0f
                : programs.NextCost(program) * Price(player, settings.CostMultiplier.Value);
        }

        public float EwTruckCost()
        {
            GameManager.GetLocalPlayer<Player>(out Player player);
            if (player == null || settings == null) return 0f;
            return settings.EwTruckCost.Value * Price(player, settings.CostMultiplier.Value);
        }

        private float LaunchCost(Player player, ModuleKind kind)
        {
            if (player == null || settings == null) return 0f;
            return PlatformModules.LaunchPrice(kind) * settings.PlatformCostScale.Value *
                   Price(player, settings.CostMultiplier.Value);
        }

        private float Price(Player player, float baseCost)
        {
            if (baseCost <= 0f || player == null) return baseCost;
            return baseCost * EventsCostMultiplier(player) *
                   perks.Multiplier(PlayerIdentity.Of(player), PerkEffect.SupportCost);
        }

        /// <summary>
        /// The requester's own effect scale for the actions that honour it (EMP shock today).
        /// Resolved here so an action reads one number instead of the perk state.
        /// </summary>
        private float EffectScale(Player player) =>
            player == null ? 1f : perks.Multiplier(PlayerIdentity.Of(player), PerkEffect.SupportEffectScale);

        /// <summary>
        /// The cooldown the host will apply to this player's next request. Scaled by the
        /// requester's re-tasking perk, and used for both the check and the reply, so the
        /// countdown a client shows is the one the host enforced.
        /// </summary>
        private float CooldownFor(Player player)
        {
            if (DisableCooldowns || player == null) return 0f;
            return settings.RequestCooldown.Value *
                   perks.Multiplier(PlayerIdentity.Of(player), PerkEffect.SupportCooldown);
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

        /// <summary>Arms the map for a command that needs a point (EW deployment and repositioning).</summary>
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

        /// <summary>Arms a right-click on the map that only reports the point back; nothing is sent.</summary>
        public void ArmLocalPick(string label, Action<GlobalPosition> onPick)
        {
            if (onPick == null || !TryArmMap(label + " · RIGHT-CLICK MAP")) return;
            localPick = onPick;
            armedCommand = null;
            ArmedAction = null;
            ArmedFrame = Time.frameCount;
            Status = "PICK: " + label + " — Right-click on map (ESC to cancel).";
        }

        public bool LocalPickArmed => localPick != null;

        /// <summary>Launch the core onto <paramref name="band"/>.</summary>
        public void RequestCoreLaunch(byte band) => SendCommand(OpsCommand.Launch, (byte)ModuleKind.Core, band, default);

        /// <summary>Launch a module to dock at <paramref name="cell"/>.</summary>
        public void RequestModuleLaunch(ModuleKind kind, int cell) =>
            SendCommand(OpsCommand.Launch, (byte)kind, (byte)cell, default);

        public void RequestJettison(int cell) => SendCommand(OpsCommand.Jettison, (byte)cell, 0, default);

        public void RequestRephase() => SendCommand(OpsCommand.Rephase, 0, 0, default);

        public void RequestOrbitShift(byte band) => SendCommand(OpsCommand.OrbitShift, band, 0, default);

        public void RequestResupply() => SendCommand(OpsCommand.Resupply, 0, 0, default);

        public void RequestUpgrade(FacilityId facility)
        {
            SendCommand(OpsCommand.Upgrade, (byte)facility, 0, default);
        }

        public void RequestInvest(OpsProgramId program)
        {
            SendCommand(OpsCommand.Invest, (byte)program, 0, default);
        }

        public void RequestEwRetune(EwPosture posture)
        {
            SendCommand(OpsCommand.EwRetune, (byte)posture, 0, default);
        }

        public void RequestGarrisonUpgrade(GarrisonUpgradeId upgrade)
        {
            SendCommand(OpsCommand.GarrisonUpgrade, (byte)upgrade, 0, default);
        }

        private void SendCommand(OpsCommand command, byte arg, byte arg2, GlobalPosition target)
        {
            int requestId = ++nextRequestId;
            pendingCommand = requestId;
            pendingCommandLabel = CommandLabel(command, arg, arg2);
            pendingCommandKind = command;
            pendingCommandArg = arg;
            commandTimeout = Time.unscaledTime + ReplyTimeout;
            Status = pendingCommandLabel + " sent to host.";
            network.Command(requestId, command, arg, arg2, target);
        }

        private string CommandLabel(OpsCommand command, byte arg, byte arg2)
        {
            switch (command)
            {
                case OpsCommand.Launch:
                    return arg == (byte)ModuleKind.Core
                        ? "LAUNCH CORE TO " + OrbitRegimes.Get(arg2).Code
                        : "LAUNCH " + PlatformModules.Info((ModuleKind)arg).Code + " TO " + OrbitalPlatform.CellName(arg2);
                case OpsCommand.Jettison:
                {
                    ModuleKind module = LocalPlatform?.Cell(arg) ?? ModuleKind.None;
                    return module == ModuleKind.Core
                        ? "DEORBIT " + OrbitalPlatform.Callsign
                        : "JETTISON " + PlatformModules.Info(module).Code + " " + OrbitalPlatform.CellName(arg);
                }
                case OpsCommand.Rephase:
                    return "REPHASE BURN";
                case OpsCommand.OrbitShift:
                {
                    OrbitalPlatform platform = LocalPlatform;
                    string verb = platform != null && arg < platform.Regime ? "LOWER" : "RAISE";
                    return verb + " TO " + OrbitRegimes.Get(arg).Code;
                }
                case OpsCommand.Resupply:
                    return "CARGO RESUPPLY";
                case OpsCommand.EwDeploy:
                    return "DEPLOY EW TRUCK";
                case OpsCommand.EwReposition:
                    return "EW TRUCK REPOSITION";
                case OpsCommand.Invest:
                    return arg < OpsProgramLedger.ProgramCount
                        ? "FUND " + OpsProgramLedger.Info((OpsProgramId)arg).Name
                        : "PROGRAM FUNDING";
                case OpsCommand.EwRetune:
                    return "EW POSTURE " + EwPostures.Info(EwPostures.Clamp(arg)).Name;
                case OpsCommand.GarrisonUpgrade:
                    return arg < OpsGarrison.UpgradeCount
                        ? "IMPROVE " + OpsGarrison.Info((GarrisonUpgradeId)arg).Name
                        : "BASE OF OPERATIONS";
                default:
                    return "BUILD " + InfoNetwork.Facility((FacilityId)arg).Name;
            }
        }

        public void Disarm()
        {
            if (!ArmedAction.HasValue && !armedCommand.HasValue && localPick == null) return;
            CancelArmed();
            Status = "Support request cancelled.";
        }

        private void CancelArmed()
        {
            ArmedAction = null;
            armedCommand = null;
            localPick = null;
            mapGesture.Complete(Time.frameCount);
        }

        public void Request(SupportActionId action)
        {
            Arm(action);
        }

        /// <summary>Deliver the armed action at a point chosen off the map (the uplink
        /// crosshair). Consumes the arming exactly as a map click would.</summary>
        public bool CallArmedAt(GlobalPosition target)
        {
            if (GameplayUI.GameIsPaused || pending || !ArmedAction.HasValue) return false;
            SupportActionId action = ArmedAction.Value;
            CancelArmed();
            RequestAt(action, target);
            return true;
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
                Status = def.IsHack ? "Infrastructure not built (see INFO)." : "Action not authorised.";
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
            pendingCommandKind = null;
            pendingCommandArg = 0;
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
                    (action.Id == SupportActionId.Recon || action.Id == SupportActionId.ElintSweep ||
                     action.Hack == HackKind.Ping);
                Status = sweep
                    ? name + " complete: " + Mathf.Max(0, message.Contacts) + " contact(s)."
                    : name + " accepted.";
                if (action != null && action.Id == SupportActionId.ElintSweep)
                    Status = name + " complete: " + Mathf.Max(0, message.Contacts) + " emitting radar(s) located.";
                if (action != null && action.Id == SupportActionId.Recon && Finite(message.X) && Finite(message.Z))
                {
                    RadarScanTarget = new GlobalPosition(message.X, message.Y, message.Z);
                    RadarScanContacts = Mathf.Max(0, message.Contacts);
                    RadarScanSerial++;
                    Status = name + " accepted: imaging, " + RadarScanContacts + " stationary contact(s) exploited.";
                }
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
                case SupportResult.OutOfCoverage: return "station not overhead — see the pass clock in SPACE";
                case SupportResult.PlatformExpended: return "rod magazine empty — launch a cargo resupply";
                case SupportResult.PlatformLowPower: return "station energy too low — let it recharge";
                case SupportResult.PlatformRecharging: return "station ability recharging";
                case SupportResult.ModuleNotFitted: return "module not fitted — build it in MISSION PLANNER";
                case SupportResult.ModuleOffline: return "module offline after a debris strike";
                case SupportResult.NoPlatform: return "no station on orbit — launch a core in MISSION PLANNER";
                case SupportResult.PlatformBrownout: return "station browned out — shed load and recharge";
                case SupportResult.NoFuel: return "not enough fuel";
                case SupportResult.LaunchInFlight: return "a launch is already in flight";
                case SupportResult.OverMass: return "station mass limit reached";
                case SupportResult.CellBlocked: return "that cell cannot take the module";
                case SupportResult.CopyLimit: return "copy limit for that module reached";
                case SupportResult.WouldStrand: return "it would strand other modules";
                case SupportResult.PlatformExists: return "the faction already has a station";
                case SupportResult.NeedsPropulsion: return "LOW orbit needs propulsion — launch to MID or HIGH";
                case SupportResult.NotBuilt: return "infrastructure not built in INFO";
                case SupportResult.NoEwAsset: return "needs an EW station near the target";
                case SupportResult.WrongPosture: return "EW station in the wrong posture";
                case SupportResult.Disabled: return "action disabled";
                case SupportResult.NotUnlocked: return "not authorised";
                case SupportResult.InvalidTarget: return "unusable target";
                case SupportResult.OutOfRange: return "target out of range";
                case SupportResult.NotAirborne: return "you must be in an aircraft";
                case SupportResult.InsufficientAllocation: return "not enough allocation";
                case SupportResult.NoStock: return "not enough SOF tokens banked";
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
            if (!DisableCooldowns && ledger.IsCoolingDown(playerId, now, CooldownFor(player)))
                return SupportResult.Cooldown;

            var context = new SupportContext(
                player, new GlobalPosition(request.X, request.Y, request.Z), request.RequestId, this,
                EffectScale(player));
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
                    var kind = (ModuleKind)message.Arg;
                    if (!PlatformModules.Placeable(message.Arg)) return SupportResult.InvalidTarget;
                    OrbitalPlatform platform = Space.PlatformFor(player.HQ);
                    if (platform == null) return SupportResult.CapabilityUnavailable;
                    bool core = kind == ModuleKind.Core;
                    int cell = core ? OrbitalPlatform.CoreCell : message.Arg2;
                    byte band = core ? message.Arg2 : (byte)0;
                    double orbitNow = OrbitNow;
                    SupportResult placement = Placement(platform.CheckPlacement(kind, cell, band, orbitNow));
                    if (placement != SupportResult.Accepted) return placement;
                    float cost = LaunchCost(player, kind);
                    if (!bypass && player.Allocation + 0.001f < cost) return SupportResult.InsufficientAllocation;
                    int seed = UnityEngine.Random.Range(1, int.MaxValue);
                    placement = Placement(platform.TryLaunch(kind, cell, band, seed, orbitNow, bypass ? 0f : cost,
                        settings.PlatformInsertionSeconds.Value, settings.PlatformDockingSeconds.Value));
                    if (placement != SupportResult.Accepted) return placement;
                    if (!bypass) player.SetAllocation(Mathf.Max(0f, player.Allocation - cost));
                    ModuleInfo info = PlatformModules.Info(kind);
                    logger.LogInfo("[Support] Liftoff: " + info.Name + " on a " + PlatformModules.VehicleFor(info.Mass).Code +
                        " vehicle " + (core ? "to " + OrbitRegimes.Get(band).Name : "for cell " + OrbitalPlatform.CellName(cell)) +
                        ", " + Mathf.RoundToInt(cost) + " alloc.");
                    break;
                }
                case OpsCommand.Jettison:
                {
                    OrbitalPlatform platform = Space.PlatformFor(player.HQ);
                    if (platform == null) return SupportResult.CapabilityUnavailable;
                    SupportResult removal = Placement(platform.TryJettison(message.Arg, out ModuleKind removed,
                        out float paid));
                    if (removal != SupportResult.Accepted) return removal;
                    float refund = JettisonRefund * paid;
                    if (refund > 0f && !bypass) player.SetAllocation(player.Allocation + refund);
                    logger.LogInfo("[Support] " + (removed == ModuleKind.Core
                        ? OrbitalPlatform.Callsign + " deorbited"
                        : PlatformModules.Info(removed).Name + " jettisoned from " + OrbitalPlatform.CellName(message.Arg)) +
                        "; refunded " + Mathf.RoundToInt(refund) + " alloc.");
                    break;
                }
                case OpsCommand.Rephase:
                {
                    OrbitalPlatform platform = Space.PlatformFor(player.HQ);
                    if (platform == null) return SupportResult.CapabilityUnavailable;
                    PlatformDenial denial = platform.Check(PlatformAbility.Rephase, OrbitNow, OrbitClock);
                    if (denial != PlatformDenial.None) return SupportContext.Refusal(denial);
                    if (!platform.TryRephase(OrbitNow, OrbitClock, UnityEngine.Random.Range(1, int.MaxValue)))
                        return SupportResult.Busy;
                    logger.LogInfo("[Support] " + OrbitalPlatform.Callsign + " phasing burn; " +
                        Mathf.RoundToInt(platform.Fuel) + " fuel left.");
                    break;
                }
                case OpsCommand.OrbitShift:
                {
                    OrbitalPlatform platform = Space.PlatformFor(player.HQ);
                    if (platform == null) return SupportResult.CapabilityUnavailable;
                    PlatformDenial denial = platform.CheckShift(message.Arg, OrbitNow, OrbitClock);
                    if (denial == PlatformDenial.SameOrbit) return SupportResult.InvalidTarget;
                    if (denial != PlatformDenial.None) return SupportContext.Refusal(denial);
                    if (!platform.TryShift(message.Arg, OrbitNow, OrbitClock, UnityEngine.Random.Range(1, int.MaxValue)))
                        return SupportResult.Busy;
                    logger.LogInfo("[Support] " + OrbitalPlatform.Callsign + " transfer burn to " +
                        platform.Orbit.Name + "; " + Mathf.RoundToInt(platform.Fuel) + " fuel left.");
                    break;
                }
                case OpsCommand.Resupply:
                {
                    OrbitalPlatform platform = Space.PlatformFor(player.HQ);
                    if (platform == null) return SupportResult.CapabilityUnavailable;
                    if (!platform.Exists) return SupportResult.NoPlatform;
                    if (platform.Pending != ModuleKind.None) return SupportResult.LaunchInFlight;
                    float cost = LaunchCost(player, ModuleKind.Cargo);
                    if (!bypass && player.Allocation + 0.001f < cost) return SupportResult.InsufficientAllocation;
                    SupportResult launch = Placement(platform.TryResupply(OrbitNow, settings.PlatformDockingSeconds.Value));
                    if (launch != SupportResult.Accepted) return launch;
                    if (!bypass) player.SetAllocation(Mathf.Max(0f, player.Allocation - cost));
                    logger.LogInfo("[Support] Cargo resupply launched to " + OrbitalPlatform.Callsign + " for " +
                        Mathf.RoundToInt(cost) + " alloc.");
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
                case OpsCommand.Invest:
                {
                    if (message.Arg >= OpsProgramLedger.ProgramCount) return SupportResult.InvalidTarget;
                    OpsProgramLedger programs = Space.ProgramsFor(player.HQ);
                    if (programs == null) return SupportResult.CapabilityUnavailable;
                    OpsProgramId program = (OpsProgramId)message.Arg;
                    if (!programs.CanInvest(program)) return SupportResult.Busy;
                    float cost = programs.NextCost(program) * Price(player, settings.CostMultiplier.Value);
                    if (!bypass && player.Allocation + 0.001f < cost) return SupportResult.InsufficientAllocation;
                    if (!programs.TryInvest(program)) return SupportResult.Busy;
                    if (!bypass) player.SetAllocation(Mathf.Max(0f, player.Allocation - cost));
                    logger.LogInfo("[Support] " + OpsProgramLedger.Info(program).Name + " funded to tier " +
                        programs.Tier(program) + " for " + Mathf.RoundToInt(cost) + " alloc.");
                    break;
                }
                case OpsCommand.EwRetune:
                {
                    if (!settings.EwEnabled.Value) return SupportResult.Disabled;
                    if (message.Arg > (byte)EwPosture.GhostSpoofing) return SupportResult.InvalidTarget;
                    if (!Ew.TryRetune(player.HQ, (EwPosture)message.Arg)) return SupportResult.NoEwAsset;
                    break;
                }
                case OpsCommand.GarrisonUpgrade:
                {
                    if (message.Arg >= OpsGarrison.UpgradeCount) return SupportResult.InvalidTarget;
                    OpsProgramLedger programs = Space.ProgramsFor(player.HQ);
                    OpsGarrison garrison = Space.GarrisonFor(player.HQ);
                    if (programs == null || garrison == null) return SupportResult.CapabilityUnavailable;
                    var upgrade = (GarrisonUpgradeId)message.Arg;
                    if (!garrison.CanUpgrade(upgrade)) return SupportResult.Busy;
                    int tokens = garrison.NextCost(upgrade);
                    if (!bypass && programs.Tokens(OpsReserve.SpecOps) < tokens) return SupportResult.NoStock;
                    if (!Space.UpgradeGarrison(player.HQ, upgrade)) return SupportResult.Busy;
                    if (!bypass) programs.TryConsume(OpsReserve.SpecOps, tokens);
                    logger.LogInfo("[Support] Base of operations: " + OpsGarrison.Info(upgrade).Name +
                        " raised to rank " + garrison.Rank(upgrade) + " for " + tokens + " SOF token(s).");
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

        internal float ServerCooldownFor(Player player) => CooldownFor(player);

        private static SupportResult Placement(PlacementFailure failure)
        {
            switch (failure)
            {
                case PlacementFailure.None: return SupportResult.Accepted;
                case PlacementFailure.NoPlatform: return SupportResult.NoPlatform;
                case PlacementFailure.PlatformExists: return SupportResult.PlatformExists;
                case PlacementFailure.LaunchInFlight: return SupportResult.LaunchInFlight;
                case PlacementFailure.OverMass: return SupportResult.OverMass;
                case PlacementFailure.CopyLimit: return SupportResult.CopyLimit;
                case PlacementFailure.WouldStrand: return SupportResult.WouldStrand;
                case PlacementFailure.NeedsPropulsion: return SupportResult.NeedsPropulsion;
                case PlacementFailure.OutsideGrid:
                case PlacementFailure.CellOccupied:
                case PlacementFailure.NotAttached:
                case PlacementFailure.EmptyCell:
                    return SupportResult.CellBlocked;
                default:
                    return SupportResult.InvalidTarget;
            }
        }

        private void LogDebris(FactionHQ owner, OrbitalPlatform platform)
        {
            string module = PlatformModules.Info(platform.Cell(platform.NoticeCell)).Name;
            logger?.LogInfo("[Support] " + (owner != null && owner.faction != null ? owner.faction.factionName : "?") +
                " station debris strike on " + module + " (" + OrbitalPlatform.CellName(platform.NoticeCell) + "): " +
                (platform.Notice == PlatformNotice.DebrisDeflected ? "deflected." : "offline for 45 s."));
        }

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

        // ---- Base of operations read-out ---------------------------------------------------

        /// <summary>
        /// What Urban Combat may do on the ground for one faction. The host answers from its
        /// own ledger; a client mirror would be its own faction, which is also correct because
        /// encampment placement is host-only.
        /// </summary>
        int IGroundForceReadiness.FortificationShells(FactionHQ owner)
        {
            OpsGarrison garrison = owner != null ? Space.GarrisonFor(owner) : null;
            return garrison != null ? garrison.FortificationShells : 1;
        }

        int IGroundForceReadiness.InsertionCamps(FactionHQ owner)
        {
            OpsGarrison garrison = owner != null ? Space.GarrisonFor(owner) : null;
            return garrison != null ? garrison.InsertionCamps : 1;
        }

        internal OpsStateMessage Snapshot(Player player, int requestId, SupportResult result)
        {
            OrbitalPlatform platform = player != null ? Space.PlatformFor(player.HQ) : null;
            InfoNetwork info = player != null ? Space.InfoFor(player.HQ) : null;
            double now = OrbitNow;
            OpsStateMessage message = OpsStateMessageBuffers.Create();
            message.RequestId = requestId;
            message.Result = (byte)result;
            if (platform != null)
            {
                platform.Export(now, exportSnapshot);
                OpsStateMessageBuffers.Write(exportSnapshot, ref message);
            }
            message.ForeignCount = player != null
                ? (byte)Space.CollectForeign(player.HQ, now, message.ForeignRegimes, message.ForeignSeeds,
                    message.ForeignClocks, message.ForeignLayouts)
                : (byte)0;
            if (info != null)
            {
                message.Sigint = (byte)info.Level(FacilityId.Sigint);
                message.Crypto = (byte)info.Level(FacilityId.Crypto);
                message.Disrupt = (byte)info.Level(FacilityId.Disrupt);
                message.Ew = (byte)info.Level(FacilityId.Ew);
            }
            EwAsset station = player != null ? Ew.ForFaction(player.HQ) : null;
            message.EwAssetState = station != null ? (byte)station.State : (byte)0;
            if (station != null)
            {
                message.EwPosture = (byte)station.Posture;
                if (station.Alive)
                {
                    GlobalPosition position = station.Position.ToGlobalPosition();
                    message.EwX = position.x;
                    message.EwZ = position.z;
                }
            }
            OpsProgramLedger programs = player != null ? Space.ProgramsFor(player.HQ) : null;
            if (programs != null)
            {
                for (int i = 0; i < OpsProgramLedger.ProgramCount; i++)
                    message.ProgramTiers[i] = (byte)programs.Tier((OpsProgramId)i);
                message.SpecOpsTokens = (byte)programs.Tokens(OpsReserve.SpecOps);
                message.IntelTokens = (byte)programs.Tokens(OpsReserve.Intel);
                message.SpecOpsProgress = programs.ProgressByte(OpsReserve.SpecOps);
                message.IntelProgress = programs.ProgressByte(OpsReserve.Intel);
            }
            OpsGarrison garrison = player != null ? Space.GarrisonFor(player.HQ) : null;
            if (garrison != null)
                for (int i = 0; i < OpsGarrison.UpgradeCount; i++)
                    message.GarrisonLevels[i] = (byte)garrison.Rank((GarrisonUpgradeId)i);
            return message;
        }

        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
