using System;
using System.Collections;
using System.Collections.Generic;
using BepInEx.Configuration;
using BepInEx.Logging;
using BoscaliSummer.Features.Support.Configuration;
using BoscaliSummer.Features.Support.Domain;
using BoscaliSummer.Features.Support.Domain.Cyber;
using BoscaliSummer.Features.Support.Domain.Orbital;
using BoscaliSummer.Features.Support.Domain.SpecOps;
using BoscaliSummer.Features.Support.Networking;
using BoscaliSummer.Features.Support.Runtime.Actions;
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
    /// view of its own request. It also owns the host-authoritative orbital stations, CYBER
    /// networks and program systems: launch, jettison, burn, resupply, site, console verb,
    /// upgrade and invest commands are validated and charged here, never in a panel.
    /// </summary>
        internal sealed class SupportManager : MonoBehaviour, ISceneService, ISupportHost, ICameraTargetService,
            IGroundForceReadiness, ITheaterStrikePicture
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
        internal readonly CyberDefense Spectrum = new CyberDefense();
        internal readonly SpecOpsTheater Field = new SpecOpsTheater();
        SpaceOperations ISupportHost.Space => Space;
        SpecOpsTheater ISupportHost.SpecOps => Field;

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
            if (!OpsStateMessageBuffers.ValidCyber(state)) return;

            GameManager.GetLocalPlayer<Player>(out Player player);
            if (player == null || player.HQ == null || state.FactionName != FactionKey(player.HQ)) return;
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
                    Space.MirrorCyber(hq, state.Cyber, now);
                }
                for (int i = 0; i < cyberOrigins.Length; i++)
                    cyberOrigins[i] = i < state.CyberOriginCount ? state.CyberOrigins[i] : null;
                Space.MirrorForeign(state.ForeignRegimes, state.ForeignSeeds, state.ForeignClocks, state.ForeignLayouts,
                    state.ForeignCount, now);
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

            if (state.TeamCooldown != null)
                for (int i = 0; i < TeamGates.Count && i < state.TeamCooldown.Length; i++)
                    teamMirror[i] = Mathf.Max(0f, state.TeamCooldown[i]);
            if (Finite(state.OpsReserve))
                reserveMirror = Mathf.Clamp(state.OpsReserve, 0f, FactionOpsReserve.Maximum);
            threatMirror = state.CyberThreatSlot < CyberNetwork.SlotCount ? state.CyberThreatSlot : byte.MaxValue;
            intelStaleMirror = state.IntelStale;

            opsReceived = Time.unscaledTime;
        }

        /// <summary>
        /// A launch reply names the odds the host fixed; the client has already mirrored the
        /// snapshot that carried them.
        /// </summary>
        private string AcceptedStatus()
        {
            if (pendingCommandKind != OpsCommand.SpecOpsLaunch || pendingCommandArg >= SpecOpsDetachment.TeamCount)
                return pendingCommandLabel + " accepted.";
            FieldTeam team = LocalDetachment?.Team(pendingCommandArg) ?? default;
            return team.State == TeamState.EnRoute
                ? FieldWords.Callsign(pendingCommandArg) + " MOVING OUT · " + FieldWords.Mission(team.Mission) + " · " +
                  team.Target + " · " + team.Chance + "% SUCCESS."
                : pendingCommandLabel + " accepted.";
        }

        /// <summary>
        /// The SPEC OPS half of a poll, arriving just before the ops snapshot. The host's own
        /// detachment is the authority; mirroring its own snapshot back would only round its clocks.
        /// </summary>
        internal void ReceiveSpecOps(SpecOpsStateMessage state)
        {
            if (state.State == null || GameAccess.IsServer()) return;
            GameManager.GetLocalPlayer<Player>(out Player player);
            if (player != null && player.HQ != null && state.FactionName == FactionKey(player.HQ))
                Space.MirrorDetachment(player.HQ, state.State, OrbitNow);
        }

        private readonly PlatformSnapshot mirrorSnapshot = new PlatformSnapshot();
        private readonly PlatformSnapshot exportSnapshot = new PlatformSnapshot();
        private readonly string[] cyberOrigins = new string[OpsStateMessageBuffers.MaximumOriginNames];
        private readonly float[] teamMirror = new float[TeamGates.Count];
        private float reserveMirror = FactionOpsReserve.Starting;
        private byte threatMirror = byte.MaxValue;
        private bool intelStaleMirror;
        private float nextReserveTick;

        private readonly SupportRequestLedger ledger = new SupportRequestLedger();
        private readonly SupportRequestLedger commandLedger = new SupportRequestLedger();
        private readonly SupportMapGesture mapGesture = new SupportMapGesture();
        private const int MaximumFactionLedgers = 8;
        private readonly Dictionary<FactionHQ, TeamLedger> teamLedgers = new Dictionary<FactionHQ, TeamLedger>();
        private readonly TeamLedger fallbackLedger = new TeamLedger();
        private readonly Dictionary<int, int> contactReplies = new Dictionary<int, int>();
        private readonly Dictionary<int, float> ttiReplies = new Dictionary<int, float>();
        private CruiseTasking cruiseTasking;
        private readonly Dictionary<int, CruiseLegMirror> cruiseLegs = new Dictionary<int, CruiseLegMirror>();
        private int pendingWaypoint = -1;
        private float pendingWaypointUntil;

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

        private Action<GlobalPosition> localPick;

        public IReadOnlyList<ActiveStrikeInfo> ActiveStrikes => activeStrikes;

        public bool TryGetNear(float x, float z, float vicinity,
            out string label, out float secondsToImpact)
        {
            label = null;
            secondsToImpact = 0f;
            if (float.IsNaN(x) || float.IsNaN(z) || float.IsNaN(vicinity) ||
                float.IsInfinity(x) || float.IsInfinity(z) || float.IsInfinity(vicinity) ||
                vicinity < 0f)
                return false;
            float now = Time.timeSinceLevelLoad;
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
                label = strike.ActionId == SupportActionId.FlareMissile ? "FLARE BARRAGE"
                    : strike.ActionId == SupportActionId.Artillery ? "KINETIC ROD"
                    : strike.ActionId == SupportActionId.Prsm ? "PRSM"
                    : strike.ActionId == SupportActionId.Cruise ? "CRUISE" : "EMP STRIKE";
            }
            return label != null;
        }
        public SupportSettings Settings => settings;

        bool ISupportHost.WindowOpen(FactionHQ owner) => WindowOpenFor(owner);

        /// <summary>Tasking window for a faction, projected locally from the host's durations.</summary>
        public bool WindowOpenFor(FactionHQ owner)
        {
            if (settings == null) return true;
            double anchor = WindowMath.Stagger(FactionKey(owner),
                settings.WindowOpenSeconds.Value + settings.WindowClosedSeconds.Value);
            return WindowMath.Open(anchor, OrbitNow,
                settings.WindowOpenSeconds.Value, settings.WindowClosedSeconds.Value, out _);
        }

        public bool LocalWindowOpen => WindowOpenFor(LocalHQ());

        /// <summary>Seconds until the local window opens or closes, for the MFD clocks.</summary>
        public float LocalWindowChangeIn
        {
            get
            {
                if (settings == null) return 0f;
                double anchor = WindowMath.Stagger(FactionKey(LocalHQ()),
                    settings.WindowOpenSeconds.Value + settings.WindowClosedSeconds.Value);
                WindowMath.Open(anchor, OrbitNow,
                    settings.WindowOpenSeconds.Value, settings.WindowClosedSeconds.Value, out float changeIn);
                return changeIn;
            }
        }

        /// <summary>Faction intel picture for the FIRES pre-check: live on the server, the host mirror on clients.</summary>
        public bool LocalIntelStale
        {
            get
            {
                if (GameAccess.IsServer())
                {
                    FactionHQ hq = LocalHQ();
                    return hq == null || SupportTargeting.FactionIntelCold(hq, settings.IntelFreshSeconds.Value);
                }
                return intelStaleMirror;
            }
        }
        public bool CommandPending => pendingCommand != 0;

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

        double ISupportHost.OrbitNow => OrbitNow;

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

        /// <summary>The local faction's CYBER network: the host's own model or a client mirror.</summary>
        public CyberNetwork LocalCyber
        {
            get
            {
                GameManager.GetLocalPlayer<Player>(out Player player);
                return player == null ? null : Space.CyberFor(player.HQ);
            }
        }

        /// <summary>The enemy faction an incident origin byte names, as the host last reported it.</summary>
        public string CyberOriginName(int origin)
        {
            if (GameAccess.IsServer())
            {
                GameManager.GetLocalPlayer<Player>(out Player player);
                FactionHQ hq = player != null ? Spectrum.Origin(player.HQ, origin) : null;
                if (hq != null) return CyberDefense.Name(hq);
            }
            string name = origin >= 0 && origin < cyberOrigins.Length ? cyberOrigins[origin] : null;
            return string.IsNullOrEmpty(name) ? "HOSTILE ACTOR " + (origin + 1) : name;
        }

        /// <summary>The local faction's SPEC OPS detachment: the host's own model or a client mirror.</summary>
        public SpecOpsDetachment LocalDetachment
        {
            get
            {
                GameManager.GetLocalPlayer<Player>(out Player player);
                return player == null ? null : Space.DetachmentFor(player.HQ);
            }
        }

        /// <summary>Which station ability, if any, a support action needs.</summary>
        public static PlatformAbility? OrbitalAbility(SupportActionId action)
        {
            switch (action)
            {
                case SupportActionId.MtiSweep: return PlatformAbility.RadarScan;
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
            return platform == null ? PlatformDenial.NoPlatform : platform.Check(ability, OrbitNow);
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
            OrbitalPlatform platform = Space.PlatformFor(owner);
            bool station = platform != null && platform.Exists;
            switch (action)
            {
                case SupportActionId.Artillery:
                    return SupportEffectPolicy.RodBlastRadius;
                case SupportActionId.Emp:
                    return (settings != null ? settings.EmpRadius.Value : 12000f) * (station ? platform.EmpScaleAt(OrbitNow) : 1f);
                case SupportActionId.MtiSweep:
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
                case SupportActionId.HackTrack:
                case SupportActionId.HackBlackout:
                    return CyberCatalog.Radius((HackKind)((byte)action - (byte)SupportActionId.HackPing));
                case SupportActionId.HackScan:
                    return CyberCatalog.Radius(HackKind.Scan);
                case SupportActionId.HackHijack:
                    return CyberCatalog.Radius(HackKind.Hijack);
                case SupportActionId.HackOverload:
                    return CyberCatalog.Radius(HackKind.Overload);
                case SupportActionId.HackGhost:
                case SupportActionId.HackSpoof:
                    return 0f; // Fleet-wide track deception; no area ring to draw.
                case SupportActionId.SpecSpot:
                    return FieldCatalog.SpotRadius(BestPostRank(owner, FieldMission.Recon));
                case SupportActionId.SpecSuppress:
                    return FieldCatalog.SuppressRadius(BestPostRank(owner, FieldMission.Sabotage));
                case SupportActionId.SpecSkywatch:
                    return FieldCatalog.SkywatchRadius(BestPostRank(owner, FieldMission.Recon));
                case SupportActionId.SpecEavesdrop:
                    return FieldCatalog.EavesdropRadius(BestPostRank(owner, FieldMission.Steal));
                case SupportActionId.SpecHunt:
                    return FieldCatalog.HuntRadius(BestPostRank(owner, FieldMission.Sabotage));
                default:
                    return 1000f;
            }
        }

        /// <summary>The rank of the best team holding a post of this kind; 0 when none is held.</summary>
        private int BestPostRank(FactionHQ owner, FieldMission post)
        {
            SpecOpsDetachment detachment = Space.DetachmentFor(owner);
            if (detachment == null) return 0;
            int best = 0;
            for (int i = 0; i < SpecOpsDetachment.TeamCount; i++)
            {
                FieldTeam team = detachment.Team(i);
                if (team.State == TeamState.Holding && team.Mission == post && team.Rank > best) best = team.Rank;
            }
            return best;
        }

        /// <summary>How long an accepted SUPPRESS lasts, for the map overlay's countdown.</summary>
        public float FieldEffectDuration(SupportActionId action, FactionHQ owner) =>
            action == SupportActionId.SpecSuppress
                ? FieldCatalog.SuppressSeconds(BestPostRank(owner, FieldMission.Sabotage))
                : action == SupportActionId.SpecHunt
                    ? FieldCatalog.HuntSeconds(BestPostRank(owner, FieldMission.Sabotage)) : 10f;

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

            activeStrikes.Add(new ActiveStrikeInfo(requestId, action, target, radius, impact, expiry));
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
        void ISupportHost.ReportOperation(Player caster, GlobalPosition target)
        {
            if (caster != null) Spectrum.ReportOperation(Space, caster.HQ, target, OrbitNow);
        }

        public IReadOnlyList<SupportActionDefinition> Actions => catalog.Actions;
        public bool BypassRequirements => bypassRequirements != null && bypassRequirements.Value;
        public bool DisableCooldowns => disableCooldowns != null && disableCooldowns.Value;
        public bool RequestPending => pending;

        public void Configure(
            SupportSettings supportSettings, IPlayerPerks playerPerks,
            IZoneFortificationService fortifications, SupportNet net, ManualLogSource log)
        {
            Field.Fortifications = fortifications;
            settings = supportSettings;
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
            Visuals.EmpVisualEffect.Reset();
            Visuals.KineticRodStrikeVisuals.Reset();
            Visuals.FlareMissileBurstVisuals.Reset();
            Visuals.SupportParticles.Reset();
            Space.Clear();
            mirrorSnapshot.Clear();
            UplinkAim = default;
            UplinkAimSet = false;
            Cyber.Clear();
            Spectrum.Clear();
            Field.Clear();
            Array.Clear(cyberOrigins, 0, cyberOrigins.Length);
            Array.Clear(teamMirror, 0, teamMirror.Length);
            reserveMirror = FactionOpsReserve.Starting;
            threatMirror = byte.MaxValue;
            nextReserveTick = 0f;
            opsReceived = -100f;
            nextOpsQuery = 0f;
            pendingCommand = 0;
            pendingCommandLabel = null;
            pendingCommandKind = null;
            pendingCommandArg = 0;
            inboundStrikeName = null;
            inboundStrikeImpactTime = 0f;
            inboundStrikeConfirmedUntil = 0f;
            ledger.Clear();
            commandLedger.Clear();
            contactReplies.Clear();
            if (cruiseTasking != null) cruiseTasking.Clear();
            cruiseLegs.Clear();
            pendingWaypoint = -1;
            intelStaleMirror = false;
            teamLedgers.Clear();
            fallbackLedger.Clear();
            StopAllCoroutines();
            pending = false;
            localCooldownUntil = 0f;
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
            ResetForScene();
            SupportMapMode.GestureArmed = false;
        }

        private void Update()
        {
            bool host = GameAccess.IsServer();
            // Read every frame so a change from the in-game settings page applies at once.
            if (settings != null) Space.CyberReach = settings.CyberReach.Value;
            if (host)
            {
                LevelInfo level = NetworkSceneSingleton<LevelInfo>.i;
                double orbitNow = OrbitNow;
                bool spectrum = settings != null && settings.EwEnabled.Value;
                Spectrum.Sync(Space, orbitNow, Time.unscaledTime, spectrum);
                Space.TickHost(orbitNow, Time.deltaTime, level != null && level.isDayLight,
                    settings != null && settings.PlatformDebrisEvents.Value,
                    spectrum ? settings.CyberCampaignIntensity.Value : 0f, LogDebris);
                if (spectrum) Spectrum.Apply(Space, orbitNow, Time.unscaledTime, logger);
                Field.Tick(Space, orbitNow, Time.unscaledTime, settings == null || settings.SpecOpsEnabled.Value,
                    Field.Fortifications != null && Field.Fortifications.Available, logger);
                if (Time.unscaledTime >= nextReserveTick)
                {
                    nextReserveTick = Time.unscaledTime + 2f;
                    for (int i = 0; i < Space.FactionCount; i++)
                    {
                        FactionHQ faction = Space.FactionAt(i);
                        if (faction == null) continue;
                        int sites = 0;
                        foreach (Airbase airbase in faction.GetAirbases())
                            if (airbase != null && airbase.CurrentHQ == faction && !airbase.AttachedAirbase) sites++;
                        TeamLedgerFor(faction).Reserve.Tick(Time.unscaledTime, sites);
                    }
                }
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
            if (pendingWaypoint != -1 && Time.unscaledTime > pendingWaypointUntil)
            {
                pendingWaypoint = -1;
                Status = "No response from host.";
            }
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
                            else
                            {
                                SupportActionId action = ArmedAction.Value;
                                if (pending || pendingCommand != 0) return;
                                if (!CheckLocalOrbitalTarget(action, target)) return;
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

        /// <summary>
        /// Seconds until the local faction's team gate reopens. The host answers from its own
        /// ledger; a client counts down from the last ops snapshot.
        /// </summary>
        public float TeamCooldownRemaining(TeamGate gate)
        {
            int index = (int)gate;
            if (index < 0 || index >= TeamGates.Count) return 0f;
            if (GameAccess.IsServer())
            {
                GameManager.GetLocalPlayer<Player>(out Player host);
                return TeamLedgerFor(host != null ? host.HQ : null).Remaining(gate, Time.unscaledTime, TeamCooldownFor(gate));
            }
            return Mathf.Max(0f, teamMirror[index] - (Time.unscaledTime - opsReceived));
        }

        public bool IsAuthorised(SupportActionDefinition action)
        {
            if (BypassRequirements) return true;
            if (!GameManager.GetLocalPlayer<Player>(out Player player) || player == null) return false;
            if (action.IsCyber)
            {
                CyberNetwork cyber = Space.CyberFor(player.HQ);
                if (cyber == null) return false;
                if (action.Hack.HasValue)
                    return cyber.AnyTier(CyberCatalog.RequiredStage(action.Hack.Value) - 1, OrbitNow);
                return cyber.AnyCapstone(action.Cap.Value, OrbitNow);
            }
            if (action.IsField) return HoldsPost(player, action.Field.Value);
            return perks.Grants(PlayerIdentity.Of(player), action.Capability);
        }

        /// <summary>A SPEC OPS ability is authorised while a team holds its kind of post.</summary>
        private bool HoldsPost(Player player, FieldAbility ability)
        {
            SpecOpsDetachment detachment = player != null ? Space.DetachmentFor(player.HQ) : null;
            return detachment != null && detachment.Enabled && detachment.Posts(FieldCatalog.PostFor(ability)) > 0;
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

        /// <summary>Price of the next network-wide CYBER upgrade for the local player.</summary>
        public float CyberUpgradeCost(CyberUpgrade upgrade)
        {
            CyberNetwork cyber = LocalCyber;
            if (cyber == null || settings == null || !cyber.CanUpgrade(upgrade)) return 0f;
            return cyber.UpgradeCost(upgrade) * settings.CyberUpgradeCostScale.Value * settings.CostMultiplier.Value;
        }

        /// <summary>Faction infrastructure balance; a client reads only the host's snapshot.</summary>
        public float LocalOpsReserve
        {
            get
            {
                if (!GameAccess.IsServer()) return reserveMirror;
                GameManager.GetLocalPlayer<Player>(out Player player);
                return player != null && player.HQ != null ? TeamLedgerFor(player.HQ).Reserve.Balance : 0f;
            }
        }

        public int LocalCyberThreatSlot
        {
            get
            {
                if (!GameAccess.IsServer()) return threatMirror == byte.MaxValue ? -1 : threatMirror;
                GameManager.GetLocalPlayer<Player>(out Player player);
                return player != null ? ThreatenedHome(player.HQ) : -1;
            }
        }

        private int ThreatenedHome(FactionHQ defender)
        {
            CyberNetwork home = defender != null ? Space.CyberFor(defender) : null;
            if (home == null) return -1;
            for (int i = 0; i < Space.FactionCount; i++)
            {
                FactionHQ other = Space.FactionAt(i);
                if (other == null || other == defender) continue;
                CyberNetwork attacker = Space.CyberFor(other);
                if (attacker == null || !attacker.BreachActive) continue;
                int anchor = attacker.Node(attacker.BreachTarget).Anchor;
                for (int slot = 0; slot < CyberNetwork.TargetBase; slot++)
                    if (home.Exists(slot) && home.Node(slot).Static && home.Node(slot).Anchor == anchor)
                        return slot;
            }
            return -1;
        }

        /// <summary>SPEC OPS prices for the local player, with the same multipliers the host charges.</summary>
        public float SpecOpsRaiseCost()
        {
            GameManager.GetLocalPlayer<Player>(out Player player);
            return SpecOpsPrice(player, FieldCatalog.RaiseCost);
        }

        public float SpecOpsMissionCost(FieldMission mission)
        {
            GameManager.GetLocalPlayer<Player>(out Player player);
            return SpecOpsPrice(player, FieldCatalog.MissionCost(mission));
        }

        public bool SpecOpsEnabled => settings == null || settings.SpecOpsEnabled.Value;

        private float SpecOpsPrice(Player player, float baseCost) =>
            settings == null ? 0f : baseCost * settings.SpecOpsCostScale.Value * settings.CostMultiplier.Value;

        public bool CyberEnabled => settings == null || settings.EwEnabled.Value;

        private float LaunchCost(Player player, ModuleKind kind)
        {
            if (settings == null) return 0f;
            return PlatformModules.LaunchPrice(kind) * settings.PlatformCostScale.Value *
                   settings.CostMultiplier.Value;
        }

        private float Price(Player player, float baseCost)
        {
            if (baseCost <= 0f || player == null) return baseCost;
            return baseCost * EventsCostMultiplier(player) *
                   perks.Multiplier(PlayerIdentity.Of(player), PerkEffect.SupportCost);
        }

        /// <summary>
        /// The cooldown the host will apply to this player's next request. Scaled by the
        /// requester's re-tasking perk, and used for both the check and the reply, so the
        /// countdown a client shows is the one the host enforced.
        /// </summary>
        private float CooldownFor(Player player)
        {
            if (DisableCooldowns || player == null) return 0f;
            return settings.RequestCooldown.Value *
                   perks.Multiplier(PlayerIdentity.Of(player), PerkEffect.SupportCooldown) *
                   EventsCooldownMultiplier(player);
        }

        /// <summary>
        /// The Events module's live world modifier for one player, resolved late so neither
        /// module has to install first. Exactly 1 when Events is absent or the theater is calm.
        /// </summary>
        internal static float EventsCostMultiplier(Player player) =>
            player != null && ModServices.TryGet<IActiveEventsView>(out IActiveEventsView events)
                ? events.SupportCostMultiplierFor(PlayerIdentity.Of(player))
                : 1f;

        internal static float EventsCooldownMultiplier(Player player) =>
            player != null && ModServices.TryGet<IActiveEventsView>(out IActiveEventsView events)
                ? events.SupportCooldownMultiplierFor(PlayerIdentity.Of(player))
                : 1f;

        /// <summary>The shared team gate a support action waits on, if any.</summary>
        private static TeamGate? TeamGateFor(SupportActionId action)
        {
            switch (action)
            {
                case SupportActionId.FlareMissile: return TeamGate.FlareBarrage;
                case SupportActionId.Fortify: return TeamGate.Fortify;
                default: return null;
            }
        }

        /// <summary>The host's current cooldown for one team gate; 0 turns the gate off.</summary>
        private float TeamCooldownFor(TeamGate gate)
        {
            if (DisableCooldowns || settings == null) return 0f;
            switch (gate)
            {
                case TeamGate.FlareBarrage: return settings.TeamFlareCooldown.Value;
                case TeamGate.Fortify: return settings.TeamFortifyCooldown.Value;
                case TeamGate.Relocate: return settings.TeamRelocateCooldown.Value;
                case TeamGate.Isolate: return settings.TeamIsolateCooldown.Value;
                default: return 0f;
            }
        }

        /// <summary>Faction pilots on station, bounded like every other faction scan.</summary>
        private static int FactionPilots(FactionHQ hq)
        {
            if (hq == null || hq.factionPlayers == null) return 0;
            int pilots = 0;
            for (int i = 0; i < hq.factionPlayers.Count && i < 64; i++)
            {
                Player player = hq.factionPlayers[i].Player;
                if (player != null && player.HQ == hq) pilots++;
            }
            return pilots;
        }

        private static bool PilotPresent(FactionHQ hq, ulong identity) => PilotByIdentity(hq, identity) != null;

        private static Player PilotByIdentity(FactionHQ hq, ulong identity)
        {
            if (hq == null || hq.factionPlayers == null || identity == 0) return null;
            for (int i = 0; i < hq.factionPlayers.Count && i < 64; i++)
            {
                Player player = hq.factionPlayers[i].Player;
                if (player != null && player.HQ == hq && PlayerIdentity.Of(player) == identity) return player;
            }
            return null;
        }

        /// <summary>
        /// Removing faction infrastructure answers to its founder while that pilot is present.
        /// A lone pilot or a faction whose founder has left can still recover the station.
        /// </summary>
        private bool TeamGuardJettison(Player player)
        {
            ulong owner = TeamLedgerFor(player.HQ).StationOwner;
            return TeamRules.MayTouch(PlayerIdentity.Of(player), owner, PilotPresent(player.HQ, owner),
                FactionPilots(player.HQ), settings != null && settings.TeamOwnershipGuards.Value);
        }

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
            ArmedAction = null;
            ArmedFrame = Time.frameCount;
            Status = "PICK: " + label + " — Right-click on map (ESC to cancel).";
        }

        public bool LocalPickArmed => localPick != null;

        /// <summary>Launch the core onto <paramref name="band"/>.</summary>
        public void RequestCoreLaunch(byte band = OrbitRegimes.Standard) => SendCommand(OpsCommand.Launch, (byte)ModuleKind.Core, band, default);

        /// <summary>Launch a module to dock at <paramref name="cell"/>.</summary>
        public void RequestModuleLaunch(ModuleKind kind, int cell) =>
            SendCommand(OpsCommand.Launch, (byte)kind, (byte)cell, default);

        public void RequestJettison(int cell) => SendCommand(OpsCommand.Jettison, (byte)cell, 0, default);

        public void RequestRelocate(int sector)
        {
            if (StationKeeping.Valid(sector)) SendCommand(OpsCommand.Rephase, (byte)sector, 0, default);
        }

        public void RequestResupply() => SendCommand(OpsCommand.Resupply, 0, 0, default);

        public void RequestPlatformFocus(PlatformFocus focus) =>
            SendCommand(OpsCommand.PlatformFocus, (byte)focus, 0, default);

        public void RequestCyberUpgrade(CyberUpgrade upgrade) =>
            SendCommand(OpsCommand.CyberUpgrade, (byte)upgrade, 0, default);

        /// <summary>Raise an empty SPEC OPS team slot.</summary>
        public void RequestSpecOpsRaise(int team) =>
            SendCommand(OpsCommand.SpecOpsRaise, (byte)Mathf.Clamp(team, 0, 255), 0, default);

        /// <summary>Send a team on a mission to the objective carrying <paramref name="anchor"/>.</summary>
        public void RequestSpecOpsLaunch(int team, FieldMission mission, int anchor) =>
            SendCommand(OpsCommand.SpecOpsLaunch, (byte)Mathf.Clamp(team, 0, 255), (byte)mission, default,
                unchecked((uint)anchor));

        public void RequestSpecOpsRecall(int team) =>
            SendCommand(OpsCommand.SpecOpsRecall, (byte)Mathf.Clamp(team, 0, 255), 0, default);

        public void RequestSpecOpsDirective(int team, SpecOpsDirective directive) =>
            SendCommand(OpsCommand.SpecOpsDirective, (byte)Mathf.Clamp(team, 0, 255), (byte)directive, default);

        /// <summary>Pick the capstone a mastered location takes.</summary>
        public void RequestCyberChoice(Capstone capstone) =>
            SendCommand(OpsCommand.CyberChoice, (byte)capstone, 0, default);

        /// <summary>A breach order: start quiet or loud, retune, spoof or disconnect.</summary>
        public void RequestCyberBreach(int target, BreachTool tool) =>
            SendCommand(OpsCommand.CyberBreach, (byte)Mathf.Clamp(target, 0, 255), (byte)tool, default);

        /// <summary>A console verb on a node slot or, for TRACE and BURN THROUGH, a stable incident id.</summary>
        public void RequestCyberVerb(CyberVerb verb, int target) =>
            SendCommand(OpsCommand.CyberVerb, (byte)Mathf.Clamp(target, 0, 255), (byte)verb, default);

        private void SendCommand(OpsCommand command, byte arg, byte arg2, GlobalPosition target, uint revision = 0)
        {
            if (pending || pendingCommand != 0)
            {
                Status = "REQUEST PENDING — wait for host acknowledgement.";
                return;
            }
            TeamGate? gate = command == OpsCommand.Rephase ? TeamGate.Relocate :
                command == OpsCommand.CyberVerb && arg2 == (byte)CyberVerb.Isolate ? TeamGate.Isolate : (TeamGate?)null;
            if (gate == TeamGate.Isolate && LocalCyber != null &&
                LocalCyber.Exists(arg) && LocalCyber.Node(arg).Isolated)
                gate = null;
            if (gate.HasValue)
            {
                float teamLeft = TeamCooldownRemaining(gate.Value);
                if (teamLeft > 0.5f)
                {
                    Status = "SUPPORT TEAM RE-TASKING: T-" + Mathf.CeilToInt(teamLeft) + "s.";
                    return;
                }
            }
            int requestId = ++nextRequestId;
            pendingCommand = requestId;
            pendingCommandLabel = CommandLabel(command, arg, arg2);
            pendingCommandKind = command;
            pendingCommandArg = arg;
            commandTimeout = Time.unscaledTime + ReplyTimeout;
            Status = pendingCommandLabel + " sent to host.";
            network.Command(requestId, command, arg, arg2, target, revision);
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
                    return "RELOCATE TO " + StationKeeping.Name(arg);
                case OpsCommand.OrbitShift:
                {
                    OrbitalPlatform platform = LocalPlatform;
                    string verb = platform != null && arg < platform.Regime ? "LOWER" : "RAISE";
                    return verb + " TO " + OrbitRegimes.Get(arg).Code;
                }
                case OpsCommand.Resupply:
                    return "CARGO RESUPPLY";
                case OpsCommand.PlatformFocus:
                    return "ROUTE POWER TO " + PlatformWords.Focus((PlatformFocus)arg);
                case OpsCommand.CyberUpgrade:
                    return "BUY " + CyberLocations.UpgradeName((CyberUpgrade)arg);
                case OpsCommand.CyberBreach:
                    return ((BreachTool)arg2 == BreachTool.Spoof ? "SPOOF · " :
                            (BreachTool)arg2 == BreachTool.Disconnect ? "DISCONNECT · " :
                            ((BreachTool)arg2 == BreachTool.Force ? "FORCE BREACH · " : "BREACH · ")) +
                           CyberWords.Callsign(LocalCyber, arg);
                case OpsCommand.CyberChoice:
                    return "CAPSTONE " + Capstones.Name((Capstone)arg);
                case OpsCommand.CyberVerb:
                {
                    var verb = (CyberVerb)Math.Min(arg2, (byte)(CyberNetwork.VerbCount - 1));
                    return CyberWords.Verb(verb) + (CyberNetwork.TargetsIncident(verb)
                        ? " " + CyberWords.Incident(LocalCyber?.Incident(LocalCyber.IncidentSlot(arg)).Kind ?? IncidentKind.None)
                        : " " + CyberWords.Callsign(LocalCyber, arg));
                }
                case OpsCommand.SpecOpsRaise: return "RAISE " + FieldWords.Callsign(arg);
                case OpsCommand.SpecOpsLaunch:
                    return FieldWords.Callsign(arg) + " · " +
                           (FieldCatalog.KnownMission(arg2) ? FieldWords.Mission((FieldMission)arg2) : "MISSION");
                case OpsCommand.SpecOpsRecall: return "RECALL " + FieldWords.Callsign(arg);
                case OpsCommand.SpecOpsDirective:
                    return (arg2 == (byte)SpecOpsDirective.Execute ? "EXECUTE " : "EXTRACT ") + FieldWords.Callsign(arg);
                default:
                    return "CYBER ORDER";
            }
        }

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

        /// <summary>Deliver the armed action at a point chosen off the map (the uplink
        /// crosshair). Consumes the arming exactly as a map click would.</summary>
        public bool CallArmedAt(GlobalPosition target)
        {
            if (GameplayUI.GameIsPaused || pending || pendingCommand != 0 || !ArmedAction.HasValue) return false;
            SupportActionId action = ArmedAction.Value;
            if (!CheckLocalOrbitalTarget(action, target)) return false;
            CancelArmed();
            RequestAt(action, target);
            return true;
        }

        public void RequestAtMark(IObservationSource observations)
        {
            if (GameplayUI.GameIsPaused || pending || pendingCommand != 0 || !ArmedAction.HasValue) return;
            if (observations == null || !observations.TryGet(out ObservationPoint point))
            {
                Status = "Camera mark unavailable. Mark again or right-click the map.";
                return;
            }
            SupportActionId action = ArmedAction.Value;
            var target = new GlobalPosition(point.X, point.Y, point.Z);
            if (!CheckLocalOrbitalTarget(action, target)) return;
            CancelArmed();
            RequestAt(action, target);
        }

        // Keep the map gesture armed after an invalid point so the pilot can pick another
        // point immediately. This is only prediction; execution repeats the check on the host.
        private bool CheckLocalOrbitalTarget(SupportActionId action, GlobalPosition target)
        {
            PlatformAbility? ability = OrbitalAbility(action);
            if (!ability.HasValue) return true;
            OrbitalPlatform platform = LocalPlatform;
            PlatformDenial denial = platform == null ? PlatformDenial.NoPlatform :
                platform.CheckTarget(ability.Value, target.x, target.z, OrbitNow);
            if (denial == PlatformDenial.None) return true;
            Status = PlatformWords.Denial(denial, platform, ability.Value, OrbitNow) + ". Pick another point or cancel.";
            return false;
        }

        public void RequestAt(SupportActionId action, GlobalPosition target)
        {
            if (pending || pendingCommand != 0)
            {
                Status = "REQUEST PENDING - wait for host acknowledgement.";
                return;
            }

            if (!CheckLocalOrbitalTarget(action, target)) return;

            SupportActionDefinition def = catalog != null ? catalog.Find(action) : null;
            if (def == null || !def.Enabled)
            {
                Status = "Action unavailable.";
                return;
            }

            bool cyber = def.IsCyber;
            float cost = cyber ? 0f : Cost(def);
            if (!cyber && cost <= 0f)
            {
                Status = "Action unavailable on this map.";
                return;
            }

            if (!IsAuthorised(def))
            {
                Status = cyber ? "No fresh access window. Prepare a target in CYBER."
                    : def.IsField ? FieldWords.AbilityLocked(def.Field.Value) + "."
                    : "Action not authorised.";
                return;
            }

            if (cyber)
            {
                CyberNetwork model = LocalCyber;
                float intel = def.Hack.HasValue ? CyberCatalog.Intel(def.Hack.Value) : Capstones.Intel;
                if (model != null && model.Intel + 0.001f < intel)
                {
                    Status = "Insufficient intel (" + intel.ToString("0") + " required).";
                    return;
                }
                if (def.Cap.HasValue && model != null && model.CapstoneRechargeRemaining(def.Cap.Value, OrbitNow) > 0f)
                {
                    Status = def.Name + " recharging.";
                    return;
                }
            }

            if (LocalCooldownRemaining > 0.5f)
            {
                Status = "Support network cooling down.";
                return;
            }

            TeamGate? gate = TeamGateFor(action);
            if (gate.HasValue)
            {
                float teamLeft = TeamCooldownRemaining(gate.Value);
                if (teamLeft > 0.5f)
                {
                    Status = "Support team re-tasking: T-" + Mathf.CeilToInt(teamLeft) + "s.";
                    return;
                }
            }

            if (!cyber && !BypassRequirements && LocalAllocation + 0.001f < cost)
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
                     action.Id == SupportActionId.MtiSweep ||
                     action.Hack == HackKind.Ping || action.Id == SupportActionId.SpecSpot ||
                     action.Id == SupportActionId.SpecSkywatch || action.Id == SupportActionId.SpecEavesdrop ||
                     action.Id == SupportActionId.SpecHunt);
                Status = sweep
                    ? name + " complete: " + Mathf.Max(0, message.Contacts) + " contact(s)."
                    : name + " accepted.";
                if (action != null && action.Id == SupportActionId.ElintSweep)
                    Status = name + " complete: " + Mathf.Max(0, message.Contacts) + " emitting radar(s) located.";
                if (action != null && (action.Id == SupportActionId.Recon || action.Id == SupportActionId.MtiSweep) &&
                    Finite(message.X) && Finite(message.Z))
                {
                    RadarScanTarget = new GlobalPosition(message.X, message.Y, message.Z);
                    RadarScanContacts = Mathf.Max(0, message.Contacts);
                    RadarScanSerial++;
                    Status = name + " accepted: imaging, " + RadarScanContacts +
                        (action.Id == SupportActionId.MtiSweep ? " moving contact(s) tracked." : " stationary contact(s) exploited.");
                }
                float eta = action != null && (action.Id == SupportActionId.Prsm || action.Id == SupportActionId.Cruise) ? message.Duration :
                            action != null && action.Id == SupportActionId.Artillery ? 8f :
                            action != null && action.Id == SupportActionId.Emp ? SupportEffectPolicy.EmpDelay :
                            action != null && action.Id == SupportActionId.FlareMissile ? 5.5f : 0f;
                if (!Finite(message.Radius) || message.Radius < 0f || message.Radius > 200000f ||
                    !Finite(message.Duration) || message.Duration < 0f || message.Duration > 60f ||
                    !Finite(message.X) || !Finite(message.Y) || !Finite(message.Z)) return;
                if (eta <= 0f && action != null && (action.Id == SupportActionId.Prsm || action.Id == SupportActionId.Cruise)) return;
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
                case SupportResult.ModuleNotFitted: return "module not fitted — build it in the station console";
                case SupportResult.ModuleOffline: return "module offline after a debris strike";
                case SupportResult.NoPlatform: return "no station on orbit — launch a core in the station console";
                case SupportResult.PlatformBrownout: return "station browned out — shed load and recharge";
                case SupportResult.NoFuel: return "not enough fuel";
                case SupportResult.LaunchInFlight: return "a launch is already in flight";
                case SupportResult.OverMass: return "station mass limit reached";
                case SupportResult.CellBlocked: return "that cell cannot take the module";
                case SupportResult.CopyLimit: return "copy limit for that module reached";
                case SupportResult.WouldStrand: return "it would strand other modules";
                case SupportResult.PlatformExists: return "the faction already has a station";
                case SupportResult.NotBuilt: return "no fresh access window (prepare a target in CYBER)";
                case SupportResult.NoEwAsset: return "no hacked location covers the target";
                case SupportResult.WrongPosture: return "the network is not in the right state";
                case SupportResult.LowIntel: return "not enough intel";
                case SupportResult.NoFieldPost: return "no SPEC OPS post of that kind covers the target";
                case SupportResult.NeedsCyberCommand: return "build Cyber Command first";
                case SupportResult.NetworkFull: return "the network is full";
                case SupportResult.CommandCompromised: return "Cyber Command is compromised - patch it";
                case SupportResult.Disabled: return "action disabled";
                case SupportResult.NotUnlocked: return "not authorised";
                case SupportResult.InvalidTarget: return "unusable target";
                case SupportResult.NoMarkTarget: return "no unit at the mark - re-mark on a contact";
                case SupportResult.StaleIntel: return "stale intel at the grid - task RADAR SCAN first";
                case SupportResult.WindowClosed: return "outside the tasking window - see the window clock in SPACE";
                case SupportResult.OutOfRange: return "target out of range";
                case SupportResult.NotAirborne: return "you must be in an aircraft";
                case SupportResult.InsufficientAllocation: return "not enough allocation";
                case SupportResult.InsufficientOpsReserve: return "faction OPS reserve too low";
                case SupportResult.PlatformWrongFocus: return "select STRIKE or SCREEN power focus in SPACE";
                case SupportResult.PlatformRetasking: return "station is retasking; wait for the shared timer";
                case SupportResult.NeedsTargetSolution: return "target needs fresh SAR, ELINT or field recon";
                case SupportResult.PlatformOutOfReach: return "outside station reach; relocate or fit a relay in SPACE";
                case SupportResult.NoStock: return "none left";
                case SupportResult.Cooldown: return "cooling down";
                case SupportResult.TeamCoolingDown: return "faction team re-tasking";
                case SupportResult.TeamDenied: return "owned by another pilot who is still here";
                case SupportResult.Busy: return "too many jobs in flight";
                case SupportResult.Duplicate: return "already handled";
                case SupportResult.CapabilityUnavailable: return "unavailable on this map";
                case SupportResult.SpawnFailed: return "could not be delivered";
                case SupportResult.RateLimited: return "too many requests";
                default:
                    if ((byte)result > (byte)SupportResult.CyberRefused &&
                        (byte)result <= (byte)SupportResult.CyberRefused + (byte)CyberDenial.Recharging)
                        return CyberWords.Denial((CyberDenial)((byte)result - (byte)SupportResult.CyberRefused)).ToLowerInvariant();
                    if ((byte)result > (byte)SupportResult.SpecOpsRefused)
                        return FieldWords.Denial((SpecOpsDenial)((byte)result - (byte)SupportResult.SpecOpsRefused)).ToLowerInvariant();
                    return result.ToString();
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
            if (!bypass && !HostAuthorised(player, action))
                return action.IsCyber ? SupportResult.NotBuilt : action.IsField ? SupportResult.NoFieldPost : SupportResult.NotUnlocked;
            if (!DisableCooldowns && ledger.IsCoolingDown(playerId, now, CooldownFor(player)))
                return SupportResult.Cooldown;
            TeamGate? gate = TeamGateFor(action.Id);
            if (gate.HasValue && !DisableCooldowns &&
                TeamLedgerFor(player.HQ).Remaining(gate.Value, now, TeamCooldownFor(gate.Value)) > 0f)
                return SupportResult.TeamCoolingDown;

            // Cyber abilities spend shared intel and one fresh, local access window.
            CyberNetwork cyber = action.IsCyber ? Space.CyberFor(player.HQ) : null;
            if (action.IsCyber && cyber == null) return SupportResult.CapabilityUnavailable;
            float intelCost = action.Hack.HasValue ? CyberCatalog.Intel(action.Hack.Value) :
                action.Cap.HasValue ? Capstones.Intel : 0f;
            if (intelCost > 0f && !bypass && cyber.Intel + 0.001f < intelCost) return SupportResult.LowIntel;
            if (action.Cap.HasValue && cyber.CapstoneRechargeRemaining(action.Cap.Value, OrbitNow) > 0f)
                return SupportResult.Cooldown;
            SpecOpsDetachment detachment = action.IsField ? Space.DetachmentFor(player.HQ) : null;
            if (action.IsField && (detachment == null || !detachment.Enabled)) return SupportResult.Disabled;
            if (action.IsField && detachment.AbilityRechargeRemaining(action.Field.Value, OrbitNow) > 0f)
                return SupportResult.Cooldown;

            var context = new SupportContext(
                player, new GlobalPosition(request.X, request.Y, request.Z), request.RequestId, this);
            float cost = action.IsCyber ? 0f : Cost(action, player);
            if (!action.IsCyber && cost <= 0f) return SupportResult.CapabilityUnavailable;
            if (!action.IsCyber && !bypass && player.Allocation + 0.001f < cost) return SupportResult.InsufficientAllocation;

            SupportResult result = action.Action.Execute(context);
            if (result != SupportResult.Accepted)
            {
                logger.LogWarning("[Support] " + action.Name + " request " + request.RequestId +
                    " rejected: " + result + ".");
                return result;
            }

            if (!bypass)
            {
                if (cost > 0f) player.SetAllocation(Mathf.Max(0f, player.Allocation - cost));
                if (intelCost > 0f) cyber.SpendIntel(intelCost);
                if (action.Cap.HasValue) cyber.TryUseCapstone(action.Cap.Value, OrbitNow);
            }
            // The recharge runs even under the debug bypass: it is the ability's own rhythm.
            if (action.IsCyber) cyber.ConsumeAccess(context.Target.x, context.Target.z, OrbitNow);
            if (action.IsField) detachment.TryUseAbility(action.Field.Value, OrbitNow);
            if (gate.HasValue) TeamLedgerFor(player.HQ).Accept(gate.Value, now);
            ledger.Accept(playerId, request.RequestId, now);
            logger.LogInfo("[Support] Accepted " + action.Name + " request " + request.RequestId +
                " from " + player + " at " + context.Target +
                (cost > 0f ? " for " + Mathf.RoundToInt(cost) + " alloc." : intelCost > 0f
                    ? " for " + Mathf.RoundToInt(intelCost) + " intel." : "."));
            return SupportResult.Accepted;
        }

        private bool HostAuthorised(Player player, SupportActionDefinition action)
        {
            if (action.IsField) return HoldsPost(player, action.Field.Value);
            if (!action.IsCyber)
                return perks.Grants(PlayerIdentity.Of(player), action.Capability);
            CyberNetwork cyber = Space.CyberFor(player.HQ);
            if (cyber == null) return false;
            if (action.Hack.HasValue)
                return cyber.AnyTier(CyberCatalog.RequiredStage(action.Hack.Value) - 1, OrbitNow);
            return cyber.AnyCapstone(action.Cap.Value, OrbitNow);
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
                case OpsCommand.PlatformFocus:
                {
                    OrbitalPlatform platform = Space.PlatformFor(player.HQ);
                    if (platform == null || !platform.Exists) return SupportResult.NoPlatform;
                    if (message.Arg > (byte)PlatformFocus.Screen) return SupportResult.InvalidTarget;
                    if (OrbitNow < platform.RetaskUntil) return SupportResult.PlatformRetasking;
                    if (!platform.TryFocus((PlatformFocus)message.Arg, OrbitNow)) return SupportResult.InvalidTarget;
                    break;
                }
                case OpsCommand.SpecOpsRaise:
                {
                    SpecOpsDetachment detachment = Space.DetachmentFor(player.HQ);
                    if (detachment == null) return SupportResult.CapabilityUnavailable;
                    SpecOpsDenial denial = detachment.CheckRaise(message.Arg);
                    if (denial != SpecOpsDenial.None) return SpecOpsRefusal(denial);
                    float cost = SpecOpsPrice(player, FieldCatalog.RaiseCost);
                    FactionOpsReserve reserve = TeamLedgerFor(player.HQ).Reserve;
                    if (!bypass && !reserve.CanSpend(cost)) return SupportResult.InsufficientOpsReserve;
                    if (!detachment.TryRaise(message.Arg)) return SupportResult.Busy;
                    if (!bypass) reserve.Spend(cost);
                    logger.LogInfo("[Support] SPEC OPS " + FieldWords.Callsign(message.Arg) + " raised for " +
                        Mathf.RoundToInt(cost) + " ops reserve.");
                    break;
                }
                case OpsCommand.SpecOpsLaunch:
                {
                    SpecOpsDetachment detachment = Space.DetachmentFor(player.HQ);
                    if (detachment == null) return SupportResult.CapabilityUnavailable;
                    // The objective is named by its anchor, never by a slot a relisting could reshuffle.
                    int anchor = unchecked((int)message.Revision);
                    var mission = (FieldMission)message.Arg2;
                    SpecOpsDenial denial = detachment.CheckLaunch(message.Arg, mission, anchor);
                    if (denial != SpecOpsDenial.None) return SpecOpsRefusal(denial);
                    float cost = SpecOpsPrice(player, FieldCatalog.MissionCost(mission));
                    FactionOpsReserve reserve = TeamLedgerFor(player.HQ).Reserve;
                    if (!bypass && !reserve.CanSpend(cost)) return SupportResult.InsufficientOpsReserve;
                    FieldObjective objective = detachment.Objective(detachment.SlotOf(anchor));
                    float travel = SpecOpsTheater.TravelOrigin(player.HQ, objective.X, objective.Z,
                        out float originX, out float originZ, out string originName);
                    if (travel < 0f) return SupportResult.InvalidTarget;
                    denial = detachment.TryLaunch(message.Arg, mission, anchor, travel, OrbitNow,
                        originX, originZ, originName);
                    if (denial != SpecOpsDenial.None) return SpecOpsRefusal(denial);
                    if (message.Arg < SpecOpsDetachment.TeamCount)
                        TeamLedgerFor(player.HQ).Launcher[message.Arg] = PlayerIdentity.Of(player);
                    if (!bypass) reserve.Spend(cost);
                    FieldTeam team = detachment.Team(message.Arg);
                    logger.LogInfo("[Support] SPEC OPS " + FieldWords.Callsign(message.Arg) + " " +
                        FieldWords.Mission(mission) + " on " + team.Target + ": " + team.Chance + "% success, " +
                        team.Loss + "% loss, " + Mathf.RoundToInt(cost) + " ops reserve.");
                    break;
                }
                case OpsCommand.SpecOpsRecall:
                {
                    SpecOpsDetachment detachment = Space.DetachmentFor(player.HQ);
                    if (detachment == null) return SupportResult.CapabilityUnavailable;
                    SpecOpsDenial denial = detachment.CheckRecall(message.Arg);
                    if (denial != SpecOpsDenial.None) return SpecOpsRefusal(denial);
                    ulong launcher = TeamLedgerFor(player.HQ).Launcher[message.Arg];
                    if (!TeamRules.MayTouch(PlayerIdentity.Of(player), launcher,
                        PilotPresent(player.HQ, launcher), FactionPilots(player.HQ),
                        settings.TeamOwnershipGuards.Value)) return SupportResult.TeamDenied;
                    if (!detachment.TryRecall(message.Arg, OrbitNow)) return SupportResult.Busy;
                    if (message.Arg < SpecOpsDetachment.TeamCount)
                        TeamLedgerFor(player.HQ).Launcher[message.Arg] = 0;
                    break;
                }
                case OpsCommand.SpecOpsDirective:
                {
                    SpecOpsDetachment detachment = Space.DetachmentFor(player.HQ);
                    if (detachment == null) return SupportResult.CapabilityUnavailable;
                    var directive = (SpecOpsDirective)message.Arg2;
                    SpecOpsDenial denial = detachment.CheckDirective(message.Arg, directive);
                    if (denial != SpecOpsDenial.None) return SpecOpsRefusal(denial);
                    ulong launcher = TeamLedgerFor(player.HQ).Launcher[message.Arg];
                    if (!TeamRules.MayTouch(playerId, launcher, PilotPresent(player.HQ, launcher),
                        FactionPilots(player.HQ), settings.TeamOwnershipGuards.Value)) return SupportResult.TeamDenied;
                    if (!detachment.TryDirective(message.Arg, directive, OrbitNow)) return SupportResult.Busy;
                    break;
                }
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
                    FactionOpsReserve reserve = TeamLedgerFor(player.HQ).Reserve;
                    if (!bypass && !reserve.CanSpend(cost)) return SupportResult.InsufficientOpsReserve;
                    int seed = UnityEngine.Random.Range(1, int.MaxValue);
                    placement = Placement(platform.TryLaunch(kind, cell, band, seed, orbitNow, bypass ? 0f : cost,
                        settings.PlatformInsertionSeconds.Value, settings.PlatformDockingSeconds.Value,
                        0UL));
                    if (placement != SupportResult.Accepted) return placement;
                    if (!bypass) reserve.Spend(cost);
                    if (core) TeamLedgerFor(player.HQ).StationOwner = PlayerIdentity.Of(player);
                    ModuleInfo info = PlatformModules.Info(kind);
                    logger.LogInfo("[Support] Liftoff: " + info.Name + " on a " + PlatformModules.VehicleFor(info.Mass).Code +
                        " vehicle " + (core ? "to " + OrbitRegimes.Get(band).Name : "for cell " + OrbitalPlatform.CellName(cell)) +
                        ", " + Mathf.RoundToInt(cost) + " ops reserve.");
                    break;
                }
                case OpsCommand.Jettison:
                {
                    OrbitalPlatform platform = Space.PlatformFor(player.HQ);
                    if (platform == null) return SupportResult.CapabilityUnavailable;
                    if (!TeamGuardJettison(player)) return SupportResult.TeamDenied;
                    float pendingValue = message.Arg == OrbitalPlatform.CoreCell ? platform.PendingPaid : 0f;
                    SupportResult removal = Placement(platform.TryJettison(message.Arg, out ModuleKind removed,
                        out float paid));
                    if (removal != SupportResult.Accepted) return removal;
                    float refund = JettisonRefund * (paid + pendingValue);
                    if (refund > 0f && !bypass)
                        TeamLedgerFor(player.HQ).Reserve.Refund(refund);
                    if (removed == ModuleKind.Core) TeamLedgerFor(player.HQ).StationOwner = 0;
                    logger.LogInfo("[Support] " + (removed == ModuleKind.Core
                        ? OrbitalPlatform.Callsign + " deorbited"
                        : PlatformModules.Info(removed).Name + " jettisoned from " + OrbitalPlatform.CellName(message.Arg)) +
                        "; refunded " + Mathf.RoundToInt(refund) + " ops reserve.");
                    break;
                }
                case OpsCommand.Rephase:
                {
                    OrbitalPlatform platform = Space.PlatformFor(player.HQ);
                    if (platform == null) return SupportResult.CapabilityUnavailable;
                    if (!StationKeeping.Valid(message.Arg)) return SupportResult.InvalidTarget;
                    PlatformDenial denial = platform.CheckRelocate(message.Arg, OrbitNow);
                    if (denial == PlatformDenial.SameOrbit) return SupportResult.InvalidTarget;
                    if (denial != PlatformDenial.None) return SupportContext.Refusal(denial);
                    if (!DisableCooldowns &&
                        TeamLedgerFor(player.HQ).Remaining(TeamGate.Relocate, now, TeamCooldownFor(TeamGate.Relocate)) > 0f)
                        return SupportResult.TeamCoolingDown;
                    if (!platform.TryRelocate(message.Arg, OrbitNow))
                        return SupportResult.Busy;
                    TeamLedgerFor(player.HQ).Accept(TeamGate.Relocate, now);
                    logger.LogInfo("[Support] " + OrbitalPlatform.Callsign + " relocating to " + StationKeeping.Name(message.Arg) + "; " +
                        Mathf.RoundToInt(platform.Fuel) + " fuel left.");
                    break;
                }
                case OpsCommand.OrbitShift:
                {
                    OrbitalPlatform platform = Space.PlatformFor(player.HQ);
                    if (platform == null) return SupportResult.CapabilityUnavailable;
                    PlatformDenial denial = platform.CheckShift(message.Arg, OrbitNow);
                    if (denial == PlatformDenial.SameOrbit) return SupportResult.InvalidTarget;
                    if (denial != PlatformDenial.None) return SupportContext.Refusal(denial);
                    if (!platform.TryShift(message.Arg, OrbitNow, UnityEngine.Random.Range(1, int.MaxValue)))
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
                    FactionOpsReserve reserve = TeamLedgerFor(player.HQ).Reserve;
                    if (!bypass && !reserve.CanSpend(cost)) return SupportResult.InsufficientOpsReserve;
                    ulong buyer = 0UL;
                    SupportResult launch = Placement(platform.TryResupply(OrbitNow, settings.PlatformDockingSeconds.Value,
                        bypass ? 0f : cost, buyer));
                    if (launch != SupportResult.Accepted) return launch;
                    if (!bypass) reserve.Spend(cost);
                    logger.LogInfo("[Support] Cargo resupply launched to " + OrbitalPlatform.Callsign + " for " +
                        Mathf.RoundToInt(cost) + " ops reserve.");
                    break;
                }
                case OpsCommand.CyberUpgrade:
                {
                    if (!settings.EwEnabled.Value) return SupportResult.Disabled;
                    if (message.Arg > (byte)CyberUpgrade.Trace) return SupportResult.InvalidTarget;
                    CyberNetwork cyber = Space.CyberFor(player.HQ);
                    if (cyber == null) return SupportResult.CapabilityUnavailable;
                    var upgrade = (CyberUpgrade)message.Arg;
                    if (!cyber.CanUpgrade(upgrade)) return SupportResult.NotBuilt;
                    float cost = cyber.UpgradeCost(upgrade) * settings.CyberUpgradeCostScale.Value *
                                 settings.CostMultiplier.Value;
                    FactionOpsReserve reserve = TeamLedgerFor(player.HQ).Reserve;
                    if (!bypass && !reserve.CanSpend(cost)) return SupportResult.InsufficientOpsReserve;
                    if (!cyber.TryUpgrade(upgrade)) return SupportResult.NotBuilt;
                    if (!bypass) reserve.Spend(cost);
                    logger.LogInfo("[Support] CYBER " + CyberLocations.UpgradeName(upgrade) + " to level " +
                        cyber.UpgradeLevel(upgrade) + " for " + Mathf.RoundToInt(cost) + " ops reserve.");
                    break;
                }
                case OpsCommand.CyberBreach:
                {
                    if (!settings.EwEnabled.Value) return SupportResult.Disabled;
                    CyberNetwork cyber = Space.CyberFor(player.HQ);
                    if (cyber == null) return SupportResult.CapabilityUnavailable;
                    var tool = (BreachTool)message.Arg2;
                    BreachDenial denial;
                    TeamLedger teams = TeamLedgerFor(player.HQ);
                    switch (tool)
                    {
                        case BreachTool.Quiet:
                            denial = cyber.TryStartBreach(message.Arg, true, OrbitNow);
                            if (denial == BreachDenial.None) teams.BreachOwner = PlayerIdentity.Of(player);
                            break;
                        case BreachTool.Force:
                            denial = cyber.TryStartBreach(message.Arg, false, OrbitNow);
                            if (denial == BreachDenial.None) teams.BreachOwner = PlayerIdentity.Of(player);
                            break;
                        case BreachTool.RetuneQuiet:
                        case BreachTool.RetuneForce:
                            if (!TeamRules.MayTouch(PlayerIdentity.Of(player), teams.BreachOwner,
                                PilotPresent(player.HQ, teams.BreachOwner), FactionPilots(player.HQ),
                                settings.TeamOwnershipGuards.Value)) return SupportResult.TeamDenied;
                            if (!cyber.TryBreachMode(tool == BreachTool.RetuneQuiet)) return SupportResult.InvalidTarget;
                            denial = BreachDenial.None;
                            break;
                        case BreachTool.Spoof:
                            if (!TeamRules.MayTouch(PlayerIdentity.Of(player), teams.BreachOwner,
                                PilotPresent(player.HQ, teams.BreachOwner), FactionPilots(player.HQ),
                                settings.TeamOwnershipGuards.Value)) return SupportResult.TeamDenied;
                            denial = cyber.TrySpoof(OrbitNow);
                            break;
                        case BreachTool.Disconnect:
                            if (!TeamRules.MayTouch(PlayerIdentity.Of(player), teams.BreachOwner,
                                PilotPresent(player.HQ, teams.BreachOwner), FactionPilots(player.HQ),
                                settings.TeamOwnershipGuards.Value)) return SupportResult.TeamDenied;
                            if (!cyber.TryDisconnect(OrbitNow)) return SupportResult.InvalidTarget;
                            teams.BreachOwner = 0;
                            denial = BreachDenial.None;
                            break;
                        default:
                            return SupportResult.InvalidTarget;
                    }
                    if (denial != BreachDenial.None)
                        return (SupportResult)((byte)SupportResult.CyberRefused + (byte)denial);
                    break;
                }
                case OpsCommand.CyberChoice:
                {
                    if (!settings.EwEnabled.Value) return SupportResult.Disabled;
                    if (!Capstones.Known(message.Arg) || message.Arg == (byte)Capstone.None) return SupportResult.InvalidTarget;
                    CyberNetwork cyber = Space.CyberFor(player.HQ);
                    if (cyber == null || !cyber.TryChooseCapstone((Capstone)message.Arg, OrbitNow))
                        return SupportResult.InvalidTarget;
                    logger.LogInfo("[Support] CYBER capstone " + Capstones.Name((Capstone)message.Arg) + " fielded.");
                    break;
                }
                case OpsCommand.CyberVerb:
                {
                    if (!settings.EwEnabled.Value) return SupportResult.Disabled;
                    if (message.Arg2 >= CyberNetwork.VerbCount) return SupportResult.InvalidTarget;
                    CyberNetwork cyber = Space.CyberFor(player.HQ);
                    if (cyber == null) return SupportResult.CapabilityUnavailable;
                    var verb = (CyberVerb)message.Arg2;
                    bool isolating = verb == CyberVerb.Isolate &&
                        !(cyber.Exists(message.Arg) && cyber.Node(message.Arg).Isolated);
                    if (isolating && !DisableCooldowns &&
                        TeamLedgerFor(player.HQ).Remaining(TeamGate.Isolate, now, TeamCooldownFor(TeamGate.Isolate)) > 0f)
                        return SupportResult.TeamCoolingDown;
                    CyberDenial denial = cyber.TryVerb(verb, message.Arg, OrbitNow);
                    if (denial != CyberDenial.None)
                        return (SupportResult)((byte)SupportResult.CyberRefused + (byte)denial);
                    if (isolating)
                    {
                        TeamLedgerFor(player.HQ).Accept(TeamGate.Isolate, now);
                        CyberNode defended = cyber.Node(message.Arg);
                        if (defended.Static)
                            for (int i = 0; i < Space.FactionCount; i++)
                            {
                                FactionHQ other = Space.FactionAt(i);
                                if (other == null || other == player.HQ) continue;
                                CyberNetwork attacker = Space.CyberFor(other);
                                if (attacker == null || !attacker.BreachActive) continue;
                                CyberNode target = attacker.Node(attacker.BreachTarget);
                                if (target.Anchor == defended.Anchor)
                                    attacker.ResistBreach(OrbitNow);
                            }
                    }
                    break;
                }
                default:
                    return SupportResult.InvalidTarget;
            }

            commandLedger.Accept(playerId, message.RequestId, now);
            return SupportResult.Accepted;
        }

        internal float ServerCooldownFor(Player player) => CooldownFor(player);

        private static SupportResult SpecOpsRefusal(SpecOpsDenial denial) =>
            (SupportResult)((byte)SupportResult.SpecOpsRefused + (byte)denial);

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
            float price = baseCost * EventsCostMultiplier(player) *
                   perks.Multiplier(PlayerIdentity.Of(player), PerkEffect.SupportCost);
            if ((action.Id == SupportActionId.FlareMissile || action.Id == SupportActionId.Fortify) && settings != null)
                price *= TeamRules.SizeScale(FactionPilots(player.HQ), settings.TeamCostPerExtra.Value, settings.TeamCostCap.Value);
            return price;
        }

        // ---- Host services ---------------------------------------------------------------

        /// <summary>The faction's team ledger, created on demand; ownerless work shares one fallback.</summary>
        private TeamLedger TeamLedgerFor(FactionHQ owner)
        {
            if (owner == null) return fallbackLedger;
            if (teamLedgers.TryGetValue(owner, out TeamLedger ledger)) return ledger;
            if (teamLedgers.Count >= MaximumFactionLedgers) teamLedgers.Clear();
            ledger = new TeamLedger();
            teamLedgers.Add(owner, ledger);
            return ledger;
        }

        bool ISupportHost.TryReserve(FactionHQ owner, SupportPool pool) =>
            TeamLedgerFor(owner).TryReserve((int)pool, MaximumStrikeJobs);

        void ISupportHost.Release(FactionHQ owner, SupportPool pool) =>
            TeamLedgerFor(owner).Release((int)pool);

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

        bool ISupportHost.BeginDeception(Player caster, HackKind kind, GlobalPosition target, float duration)
        {
            if (caster == null || caster.HQ == null) return false;
            if (!Cyber.Begin(kind, caster.HQ, target.x, target.z, duration, Time.timeSinceLevelLoad))
                return false;
            network.BroadcastCyberEffect(kind, caster.HQ.faction != null ? caster.HQ.faction.factionName : string.Empty,
                target.x, target.z, duration);
            return true;
        }

        // ---- Cruise tasking ----------------------------------------------------------------

        internal CruiseTasking Tasking => cruiseTasking ?? (cruiseTasking =
            new CruiseTasking(routine => ((ISupportHost)this).Run(routine), BroadcastLegs));

        internal CruiseLegMirror LegsFor(int requestId)
        {
            cruiseLegs.TryGetValue(requestId, out CruiseLegMirror mirror);
            return mirror;
        }

        /// <summary>Client: chains a waypoint onto a live cruise strike, or clears its legs.</summary>
        public void SendWaypoint(int requestId, GlobalPosition point, bool clear)
        {
            if (network == null)
            {
                ReportOffline();
                return;
            }
            pendingWaypoint = requestId;
            pendingWaypointUntil = Time.unscaledTime + ReplyTimeout;
            network.SendWaypoint(requestId, point, clear);
        }

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
            else Tasking.QueueLeg(strike, leg, Time.timeSinceLevelLoad, settings.MaximumRange.Value);
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
                    "Waypoint denied: " + Explain(result) + ".";
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
                    if (!Finite(message.X[i]) || !Finite(message.Z[i])) continue;
                    mirror.Legs.Add(new GlobalPosition(message.X[i], 0f, message.Z[i]));
                }
            cruiseLegs[message.RequestId] = mirror;
            if (Finite(message.Tti) && message.Tti > 0f)
            {
                SupportActionDefinition def = catalog != null ? catalog.Find(SupportActionId.Cruise) : null;
                float linger = Math.Max(0f, known.Value.ExpiryTime - known.Value.ImpactTime);
                RegisterActiveStrike(known.Value.RequestId, known.Value.ActionId, known.Value.Target,
                    known.Value.Radius, message.Tti, def != null ? def.Name : "CRUISE", linger);
            }
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

        // ---- Ground readiness read-out -----------------------------------------------------

        /// <summary>
        /// What Urban Combat may do on the ground for one faction: one position or camp plus the
        /// best SPEC OPS team's rank. The host answers from its own detachment; a client mirror
        /// would be its own faction, which is also correct because placement is host-only.
        /// </summary>
        int IGroundForceReadiness.InsertionCamps(FactionHQ owner) =>
            owner != null ? Space.DetachmentFor(owner)?.GroundReadiness ?? 1 : 1;

        internal OpsStateMessage Snapshot(Player player, int requestId, SupportResult result)
        {
            OrbitalPlatform platform = player != null ? Space.PlatformFor(player.HQ) : null;
            double now = OrbitNow;
            OpsStateMessage message = OpsStateMessageBuffers.Create();
            // Without the header a client's reader drops the whole snapshot as an old protocol.
            message.Protocol = SupportNet.ProtocolVersion;
            message.RequestId = requestId;
            message.Result = (byte)result;
            message.FactionName = FactionKey(player != null ? player.HQ : null);
            message.OpsReserve = player != null && player.HQ != null
                ? TeamLedgerFor(player.HQ).Reserve.Balance : 0f;
            int threatened = player != null ? ThreatenedHome(player.HQ) : -1;
            message.CyberThreatSlot = threatened >= 0 ? (byte)threatened : byte.MaxValue;
            message.IntelStale = player == null || player.HQ == null ||
                SupportTargeting.FactionIntelCold(player.HQ, settings.IntelFreshSeconds.Value);
            if (platform != null)
            {
                platform.Export(now, exportSnapshot);
                OpsStateMessageBuffers.Write(exportSnapshot, ref message);
            }
            message.ForeignCount = player != null
                ? (byte)Space.CollectForeign(player.HQ, now, message.ForeignRegimes, message.ForeignSeeds,
                    message.ForeignClocks, message.ForeignLayouts)
                : (byte)0;
            CyberNetwork cyber = player != null ? Space.CyberFor(player.HQ) : null;
            if (cyber != null)
            {
                cyber.Export(now, message.Cyber);
                message.CyberOriginCount = (byte)Spectrum.OriginNames(player.HQ, message.CyberOrigins);
            }
            TeamLedger teams = TeamLedgerFor(player != null ? player.HQ : null);
            float stampNow = Time.unscaledTime;
            for (int i = 0; i < TeamGates.Count; i++)
            {
                var teamGate = (TeamGate)i;
                message.TeamCooldown[i] = teams.Remaining(teamGate, stampNow, TeamCooldownFor(teamGate));
            }
            return message;
        }

        internal SpecOpsStateMessage SpecOpsSnapshot(Player player)
        {
            var message = new SpecOpsStateMessage { Protocol = SupportNet.ProtocolVersion,
                FactionName = FactionKey(player != null ? player.HQ : null), State = new SpecOpsSnapshot() };
            SpecOpsDetachment detachment = player != null ? Space.DetachmentFor(player.HQ) : null;
            if (detachment != null) detachment.Export(OrbitNow, message.State);
            return message;
        }

        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

        private static string FactionKey(FactionHQ hq)
        {
            string name = CyberDefense.Name(hq);
            return name.Length <= 24 ? name : name.Substring(0, 24);
        }
    }
}
