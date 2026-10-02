using System;
using System.Collections;
using System.Collections.Generic;
using BepInEx.Configuration;
using BepInEx.Logging;
using BoscaliSummer.Modules.Support.Configuration;
using BoscaliSummer.Modules.Support.Domain;
using BoscaliSummer.Modules.Support.Domain.Calls;
using BoscaliSummer.Modules.Support.Networking;
using BoscaliSummer.Modules.Support.Runtime.Actions;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Core.Modules;
using BoscaliSummer.Core.Services;
using BoscaliSummer.Core.Ui;
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

        private SupportSettings settings;
        private IPlayerPerks perks;
        private SupportNet network;
        private CreditService credits;
        private float nextCreditTick;
        private float lastCreditTick;
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

        /// <summary>Latest accepted radar scan of the local player, for the imager.</summary>
        public int RadarScanSerial { get; private set; }
        public GlobalPosition RadarScanTarget { get; private set; }
        public int RadarScanContacts { get; private set; }

        /// <summary>Display the same owned-zone footprint that FORTIFY executes.</summary>
        public void ResolveMapArea(SupportActionId action, ref GlobalPosition target, ref float radius)
        {
            EffectReceipt(action, LocalHQ(), target.x, target.z, out radius, out _);
            if (action != SupportActionId.Fortify) return;
            if (!GameManager.GetLocalPlayer<Player>(out Player player)) return;
            Airbase zone = SupportTargeting.NearestOwnedAirbase(player, target.ToLocalPosition(), out float distance);
            if (zone == null || distance > Mathf.Max(650f, zone.GetRadius() * 1.5f))
            {
                radius = 0f;
                return;
            }
            target = (zone.center != null ? zone.center.position : zone.transform.position).ToGlobalPosition();
            radius = zone.GetRadius();
        }

        public float GetEffectRadius(SupportActionId action) =>
            GetEffectRadius(action, LocalHQ());

        public float GetEffectRadius(SupportActionId action, FactionHQ owner)
        {
            switch (action)
            {
                case SupportActionId.Artillery:
                    return SupportEffectPolicy.RodBlastRadius;
                case SupportActionId.Emp:
                    return Mathf.Min(SupportEffectPolicy.MaxEmpRadius,
                        settings != null ? settings.EmpRadius.Value : 12000f);
                case SupportActionId.MtiSweep:
                case SupportActionId.Recon:
                    return settings != null ? settings.SarSceneRadius.Value : 1000f;
                case SupportActionId.ElintSweep:
                    return settings != null ? settings.ElintRadius.Value : 8000f;
                case SupportActionId.FlareMissile:
                    return settings != null ? settings.FlareBarrageRadius.Value : 4000f;
                case SupportActionId.Fortify:
                    return 650f; // Selection radius; the overlay resolves the actual owned zone.
                default:
                    return 1000f;
            }
        }

        // Capture before execution so a receipt describes the effect the host actually applied.
        internal void EffectReceipt(SupportActionId action, FactionHQ owner, float x, float z,
            out float radius, out float duration)
        {
            radius = GetEffectRadius(action, owner);
            duration = settings != null ? settings.JtacMarkDuration.Value : 10f;
            if (action == SupportActionId.Emp)
                duration = SupportEffectPolicy.EmpDuration;
            else if (action == SupportActionId.FlareMissile)
                duration = settings.FlareBarrageDuration.Value;
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

        public IReadOnlyList<SupportActionDefinition> Actions => catalog.Actions;
        public bool BypassRequirements => bypassRequirements != null && bypassRequirements.Value;
        public bool DisableCooldowns => disableCooldowns != null && disableCooldowns.Value;
        public bool RequestPending => pending;

        public void Configure(
            SupportSettings supportSettings, IPlayerPerks playerPerks,
            IZoneFortificationService fortifications, SupportNet net, ManualLogSource log)
        {
            settings = supportSettings;
            perks = playerPerks;
            network = net;
            credits = new CreditService(net);
            credits.PriceFactors = HostPriceFactors;
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
            inboundStrikeName = null;
            inboundStrikeImpactTime = 0f;
            inboundStrikeConfirmedUntil = 0f;
            ledger.Clear();
            credits?.Clear();
            LocalCredit = 0f;
            LocalFrozenSeconds = 0;
            LocalEventFactor = 1f;
            LocalSilentFactor = 1f;
            nextCreditTick = 0f;
            lastCreditTick = 0f;
            contactReplies.Clear();
            ttiReplies.Clear();
            if (cruiseTasking != null) cruiseTasking.Clear();
            cruiseLegs.Clear();
            pendingWaypoint = -1;
            strikeJobs.Clear();
            Array.Clear(fallbackJobs, 0, fallbackJobs.Length);
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

        private static float MissionNow() =>
            NetworkSceneSingleton<MissionManager>.i != null
                ? NetworkSceneSingleton<MissionManager>.i.MissionTime
                : Time.timeSinceLevelLoad;

        private void Update()
        {
            if (credits != null && GameAccess.IsServer() && Time.unscaledTime >= nextCreditTick)
            {
                float missionNow = MissionNow();
                credits.Tick(missionNow, Mathf.Max(0f, missionNow - lastCreditTick));
                lastCreditTick = missionNow;
                nextCreditTick = Time.unscaledTime + 1f;
            }

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
        public float LocalCredit { get; private set; }
        public int LocalFrozenSeconds { get; private set; }
        public float LocalEventFactor { get; private set; } = 1f;
        public float LocalSilentFactor { get; private set; } = 1f;

        internal void ReceiveCredit(CreditStateMessage message)
        {
            LocalCredit = message.Balance;
            LocalFrozenSeconds = message.FrozenSeconds;
            LocalEventFactor = SaneFactor(message.EventFactor);
            LocalSilentFactor = SaneFactor(message.SilentFactor);
        }

        /// <summary>The host's combined knob x events x perk factor for one player.</summary>
        private (float eventFactor, float silentFactor) HostPriceFactors(Player player) =>
            player == null ? (1f, 1f)
                : (EventsCostMultiplier(player),
                   settings.PriceKnob.Value * perks.Multiplier(PlayerIdentity.Of(player), PerkEffect.SupportCost));

        private static float SaneFactor(float f) => float.IsNaN(f) || float.IsInfinity(f) || f <= 0f ? 1f : f;

        /// <summary>CR price of one CALL for the local player (host runs the same QuoteFor).</summary>
        internal CallQuote Quote(SupportActionId id)
        {
            SupportActionDefinition def = catalog != null ? catalog.Find(id) : null;
            if (def == null || !GameManager.GetLocalPlayer<Player>(out Player player) || player == null)
                return new CallQuote(0, "");
            return QuoteFor(def, player);
        }

        /// <summary>Whether the local player's faction has earned this tier; unlockText names the next goal.</summary>
        internal bool Unlocked(SupportActionId id, out string unlockText)
        {
            unlockText = "";
            if (!CallSheet.TryGet(id, out CallRow row) ||
                !GameManager.GetLocalPlayer<Player>(out Player player) || player == null || credits == null)
                return false;
            ObjectiveCount census = credits.Census(player.HQ);
            float minutes = MissionNow() / 60f;
            bool open = CallFloors.Unlocked(row.Tier, census.held, census.n, minutes, 1f);
            if (!open) unlockText = CallFloors.NextUnlock(census.held, census.n, minutes, 1f);
            return open;
        }

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
            return perks.Grants(PlayerIdentity.Of(player), action.Capability);
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
        public void ArmLocalPick(string label, Action<GlobalPosition> onPick)
        {
            if (onPick == null || !TryArmMap(label + " · RIGHT-CLICK MAP")) return;
            localPick = onPick;
            ArmedAction = null;
            ArmedFrame = Time.frameCount;
            Status = "PICK: " + label + " — Right-click on map (ESC to cancel).";
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
            var target = new GlobalPosition(point.X, point.Y, point.Z);
            CancelArmed();
            RequestAt(action, target);
        }

        public void RequestAt(SupportActionId action, GlobalPosition target)
        {
            if (pending)
            {
                Status = "REQUEST PENDING - wait for host acknowledgement.";
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
                Status = "Action not authorised.";
                return;
            }

            if (LocalCooldownRemaining > 0.5f)
            {
                Status = "Support network cooling down.";
                return;
            }

            if (!BypassRequirements && LocalFrozenSeconds > 0)
            {
                Status = "Wallet frozen after a faction switch (" + LocalFrozenSeconds + " s).";
                return;
            }

            if (!BypassRequirements && LocalCredit + 0.001f < cost)
            {
                Status = "Low credit (" + cost.ToString("0") + " CR required).";
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
                     action.Id == SupportActionId.MtiSweep);
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
                case SupportResult.OutOfCoverage: return "outside coverage";
                case SupportResult.Disabled: return "action disabled";
                case SupportResult.NotUnlocked: return "not authorised";
                case SupportResult.InvalidTarget: return "unusable target";
                case SupportResult.NoMarkTarget: return "no unit at the mark - re-mark on a contact";
                case SupportResult.StaleIntel: return "stale intel at the grid - task RADAR SCAN first";
                case SupportResult.OutOfRange: return "target out of range";
                case SupportResult.NotAirborne: return "you must be in an aircraft";
                case SupportResult.InsufficientAllocation: return "not enough allocation";
                case SupportResult.NoStock: return "none left";
                case SupportResult.Cooldown: return "cooling down";
                case SupportResult.Busy: return "too many jobs in flight";
                case SupportResult.Duplicate: return "already handled";
                case SupportResult.CapabilityUnavailable: return "unavailable on this map";
                case SupportResult.SpawnFailed: return "could not be delivered";
                case SupportResult.RateLimited: return "too many requests";
                default: return "unavailable";
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
            if (!bypass && !HostAuthorised(player, action)) return SupportResult.NotUnlocked;
            if (!DisableCooldowns && ledger.IsCoolingDown(playerId, now, CooldownFor(player)))
                return SupportResult.Cooldown;

            var context = new SupportContext(
                player, new GlobalPosition(request.X, request.Y, request.Z), request.RequestId, this);
            CallQuote quote = QuoteFor(action, player);
            float cost = quote.Cost;
            if (cost <= 0f || !CallSheet.TryGet(action.Id, out CallRow row)) return SupportResult.CapabilityUnavailable;
            float missionNow = MissionNow();
            ObjectiveCount census = credits.Census(player.HQ);
            if (!bypass && !CallFloors.Unlocked(row.Tier, census.held, census.n, missionNow / 60f, 1f))
                return SupportResult.NotUnlocked;
            if (!bypass && !credits.TrySpend(player, cost, missionNow)) return SupportResult.InsufficientAllocation;

            SupportResult result;
            try { result = action.Action.Execute(context); }
            catch (Exception e)
            {
                logger.LogError(e);
                result = SupportResult.SpawnFailed;
            }
            if (result != SupportResult.Accepted)
            {
                if (!bypass) credits.Refund(player, cost, missionNow);
                logger.LogWarning("[Support] " + action.Name + " request " + request.RequestId +
                    " rejected: " + result + ".");
                return result;
            }

            ledger.Accept(playerId, request.RequestId, now);
            try
            {
                credits.Assists.Record(credits.FactionKey(player.HQ), context.Target.x, context.Target.z,
                    GetEffectRadius(action.Id, player.HQ), missionNow);
            }
            catch (Exception e) { logger.LogError(e); }
            logger.LogInfo("[Support] Accepted " + action.Name + " request " + request.RequestId +
                " from " + player + " at " + context.Target +
                (cost > 0f ? " for " + Mathf.RoundToInt(cost) + " CR." : "."));
            return SupportResult.Accepted;
        }

        private bool HostAuthorised(Player player, SupportActionDefinition action) =>
            perks.Grants(PlayerIdentity.Of(player), action.Capability);

        internal float ServerCooldownFor(Player player) => CooldownFor(player);

        /// <summary>
        /// Price for one player. No action prices itself from the target, so costing uses a
        /// bare context and both the panel and the host reach the same number.
        /// </summary>
        internal CallQuote QuoteFor(SupportActionDefinition action, Player player)
        {
            // BaseCost <= 0 still means "this action is not available on this map".
            if (player == null || credits == null || !CallSheet.TryGet(action.Id, out CallRow row) ||
                action.Action.BaseCost(new SupportContext(player, default, 0, this)) <= 0f)
                return new CallQuote(0, "");
            ObjectiveCount census = credits.Census(player.HQ);
            var inputs = new PriceInputs(CallFloors.Share(census.held, census.contested, census.n), census.n,
                false, null, false,
                // Clients quote with the factor the host sent, so the panel matches what the host charges.
                GameAccess.IsServer() ? EventsCostMultiplier(player) : LocalEventFactor,
                GameAccess.IsServer() ? perks.Multiplier(PlayerIdentity.Of(player), PerkEffect.SupportCost) : LocalSilentFactor,
                GameAccess.IsServer() ? settings.PriceKnob.Value : 1f);
            return CallPricing.Quote(row.Tier, inputs);
        }

        // ---- Host services ---------------------------------------------------------------

        private int[] JobsFor(FactionHQ owner)
        {
            if (owner == null) return fallbackJobs;
            if (strikeJobs.TryGetValue(owner, out int[] jobs)) return jobs;
            if (strikeJobs.Count >= MaximumFactionPools) strikeJobs.Clear();
            jobs = new int[2];
            strikeJobs.Add(owner, jobs);
            return jobs;
        }

        bool ISupportHost.TryReserve(FactionHQ owner, SupportPool pool)
        {
            int[] jobs = JobsFor(owner);
            int index = (int)pool;
            if (index < 0 || index >= jobs.Length || jobs[index] >= MaximumStrikeJobs) return false;
            jobs[index]++;
            return true;
        }

        void ISupportHost.Release(FactionHQ owner, SupportPool pool)
        {
            int[] jobs = JobsFor(owner);
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

        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

        private static string FactionKey(FactionHQ hq)
        {
            string name = hq != null && hq.faction != null && !string.IsNullOrEmpty(hq.faction.factionName)
                ? hq.faction.factionName.ToUpperInvariant()
                : "UNKNOWN ACTOR";
            return name.Length <= 24 ? name : name.Substring(0, 24);
        }
    }
}
