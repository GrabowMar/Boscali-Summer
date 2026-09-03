using System;
using System.Collections;
using System.Collections.Generic;
using BepInEx.Configuration;
using BepInEx.Logging;
using BoscaliSummer.Features.Support.Configuration;
using BoscaliSummer.Features.Support.Networking;
using BoscaliSummer.Framework.Contracts;
using BoscaliSummer.Framework.Lifecycle;
using NOAvionics;
using NuclearOption.Networking;
using UnityEngine;

namespace BoscaliSummer.Features.Support.Runtime
{
    /// <summary>
    /// Validates and dispatches support requests. Everything an action does lives in the
    /// action; this class owns only authority, economy, bounded concurrency and the client's
    /// view of its own request.
    /// </summary>
    internal sealed class SupportManager : MonoBehaviour, ISceneService, ISupportHost
    {
        private const int MaximumStrikeJobs = 2;
        private const int RequestsPerSecond = 2;

        /// <summary>How long a client waits for a reply before reporting the host silent.</summary>
        private const float ReplyTimeout = 5f;

        private readonly SupportRequestLedger ledger = new SupportRequestLedger();
        private readonly int[] reserved = new int[1];

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
                        return $"[IMPACT CONFIRMED // {inboundStrikeName} SPLASH]";
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
            ledger.Clear();
            Array.Clear(reserved, 0, reserved.Length);
            StopAllCoroutines();
            pending = false;
            localCooldownUntil = 0f;
            ArmedAction = null;
            ArmedFrame = 0;
            MapPicker.Disarm(MapPicker.Support);
            Status = "Select support option, then right-click on map.";
        }

        private void Update()
        {
            if (pending && Time.unscaledTime - pendingSince > ReplyTimeout)
            {
                pending = false;
                Status = "No response from host.";
            }

            if (ArmedAction.HasValue)
            {
                if (Input.GetKeyDown(KeyCode.Escape))
                {
                    Disarm();
                    return;
                }

                if (Time.frameCount > ArmedFrame + 1 && Input.GetMouseButtonDown(1) &&
                    MapPicker.IsOwner(MapPicker.Support))
                {
                    DynamicMap map = SceneSingleton<DynamicMap>.i;
                    if (map != null && DynamicMap.mapMaximized && map.TryGetCursorCoordinates(out GlobalPosition target))
                    {
                        SupportActionId action = ArmedAction.Value;
                        ArmedAction = null;
                        MapPicker.Disarm(MapPicker.Support);
                        RequestAt(action, target);
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

        public void Arm(SupportActionId action)
        {
            if (ArmedAction.HasValue && ArmedAction.Value == action)
            {
                Disarm();
                return;
            }

            SupportActionDefinition def = catalog != null ? catalog.Find(action) : null;
            string name = def != null ? def.Name : "SUPPORT";
            string prompt = name + " ARMED · RIGHT-CLICK MAP";
            if (!MapPicker.TryArm(MapPicker.Support, MapPicker.GestureRight, prompt))
            {
                Status = MapPicker.Prompt ?? "MAP BUSY";
                return;
            }

            ArmedAction = action;
            ArmedFrame = Time.frameCount;
            Status = "ARMED: " + name + " — Right-click on map to call in (ESC to cancel).";

            DynamicMap map = SceneSingleton<DynamicMap>.i;
            if (map != null && !DynamicMap.mapMaximized)
            {
                map.Maximize();
            }
        }

        public void Disarm()
        {
            if (!ArmedAction.HasValue) return;
            ArmedAction = null;
            MapPicker.Disarm(MapPicker.Support);
            Status = "Support request cancelled.";
        }

        public void Request(SupportActionId action)
        {
            Arm(action);
        }

        public void RequestAt(SupportActionId action, GlobalPosition target)
        {
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

            if (!BypassRequirements && LocalAllocation + 0.001f < cost)
            {
                Status = "Insufficient allocation (" + cost.ToString("0") + " required).";
                return;
            }

            pending = true;
            pendingSince = Time.unscaledTime;
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
            pending = false;
            SupportResult result = (SupportResult)message.Result;
            SupportActionDefinition action = catalog.Find((SupportActionId)message.Action);
            string name = action != null ? action.Name : "Support";
            if (result == SupportResult.Accepted)
            {
                localCooldownUntil = DisableCooldowns ? 0f : Time.unscaledTime + message.CooldownSeconds;
                Status = name + " accepted.";
                if (action != null && (action.Id == SupportActionId.Artillery || action.Id == SupportActionId.Emp))
                {
                    float eta = action.Id == SupportActionId.Artillery ? 8f : 12f;
                    RegisterInboundStrike(name, eta);
                }
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
            if (!Finite(request.X) || !Finite(request.Y) || !Finite(request.Z))
                return SupportResult.InvalidTarget;

            SupportActionDefinition action = catalog.Find((SupportActionId)request.Action);
            if (action == null) return SupportResult.CapabilityUnavailable;

            ulong playerId = PlayerIdentity.Of(player);
            float now = Time.unscaledTime;
            bool bypass = BypassRequirements;

            if (ledger.WasAccepted(playerId, request.RequestId)) return SupportResult.Duplicate;
            if (!DisableCooldowns && ledger.IsRateLimited(playerId, now, RequestsPerSecond, 1f)) return SupportResult.RateLimited;
            if (!action.Enabled) return SupportResult.Disabled;
            if (!bypass && !perks.Grants(playerId, action.Capability)) return SupportResult.NotUnlocked;
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
            return baseCost * perks.Multiplier(PlayerIdentity.Of(player), PerkEffect.SupportCost);
        }

        // ---- Host services ---------------------------------------------------------------

        bool ISupportHost.TryReserve(SupportPool pool)
        {
            if (DisableCooldowns) return true;
            if (reserved[(int)pool] >= MaximumStrikeJobs) return false;
            reserved[(int)pool]++;
            return true;
        }

        void ISupportHost.Release(SupportPool pool) =>
            reserved[(int)pool] = Math.Max(0, reserved[(int)pool] - 1);

        void ISupportHost.Run(IEnumerator routine) => StartCoroutine(routine);

        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
