using System;
using System.Collections;
using System.Collections.Generic;
using BepInEx.Configuration;
using BepInEx.Logging;
using BoscaliSummer.Features.Support.Configuration;
using BoscaliSummer.Features.Support.Networking;
using BoscaliSummer.Framework.Contracts;
using BoscaliSummer.Framework.Lifecycle;
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
        private const int MaximumSupportVehicles = 24;
        private const int MaximumDropsInFlight = 2;
        private const int MaximumArtilleryJobs = 2;
        private const int RequestsPerSecond = 2;

        /// <summary>How long a client waits for a reply before reporting the host silent.</summary>
        private const float ReplyTimeout = 5f;

        private readonly SupportRequestLedger ledger = new SupportRequestLedger();
        private readonly List<GroundVehicle> supportVehicles = new List<GroundVehicle>(MaximumSupportVehicles);
        private readonly int[] reserved = new int[2];

        private SupportSettings settings;
        private IPlayerPerks perks;
        private SupportNet network;
        private ManualLogSource logger;
        private ConfigEntry<bool> bypassRequirements;
        private SupportCatalog catalog;
        private readonly VanillaSupportCatalog vanilla = new VanillaSupportCatalog();

        private int nextRequestId;
        private float pendingSince;
        private bool pending;
        private float localCooldownUntil;

        public string Status { get; private set; } = "Designate a grid on the maximised map.";

        SupportSettings ISupportHost.Settings => settings;
        ManualLogSource ISupportHost.Logger => logger;
        VanillaSupportCatalog ISupportHost.Vanilla => vanilla;

        public IReadOnlyList<SupportActionDefinition> Actions => catalog.Actions;
        public bool BypassRequirements => bypassRequirements != null && bypassRequirements.Value;

        public void Configure(
            SupportSettings supportSettings, IPlayerPerks playerPerks,
            IZoneFortificationService fortifications, SupportNet net, ManualLogSource log)
        {
            settings = supportSettings;
            perks = playerPerks;
            network = net;
            logger = log;
            catalog = new SupportCatalog(supportSettings, fortifications);
        }

        internal void ConfigureBypass(ConfigEntry<bool> bypass) => bypassRequirements = bypass;

        public SupportActionId? ArmedAction { get; private set; }
        public int ArmedFrame { get; private set; }

        public void ResetForScene()
        {
            ledger.Clear();
            supportVehicles.Clear();
            Array.Clear(reserved, 0, reserved.Length);
            vanilla.Reset();
            StopAllCoroutines();
            pending = false;
            localCooldownUntil = 0f;
            ArmedAction = null;
            ArmedFrame = 0;
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

                if (Time.frameCount > ArmedFrame + 1 && Input.GetMouseButtonDown(1))
                {
                    DynamicMap map = SceneSingleton<DynamicMap>.i;
                    if (map != null && DynamicMap.mapMaximized && map.TryGetCursorCoordinates(out GlobalPosition target))
                    {
                        SupportActionId action = ArmedAction.Value;
                        ArmedAction = null;
                        RequestAt(action, target);
                    }
                }
            }
        }

        // ---- Client view -----------------------------------------------------------------

        public float LocalAllocation =>
            GameManager.GetLocalPlayer<Player>(out Player player) && player != null ? player.Allocation : 0f;

        public float LocalCooldownRemaining =>
            Mathf.Max(0f, localCooldownUntil - Time.unscaledTime);

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

        /// <summary>True when the maximised map currently has a usable cursor coordinate.</summary>
        public bool TryGetDesignatedTarget(out GlobalPosition target)
        {
            target = default;
            DynamicMap map = SceneSingleton<DynamicMap>.i;
            return map != null && DynamicMap.mapMaximized && map.TryGetCursorCoordinates(out target);
        }

        public void Arm(SupportActionId action)
        {
            if (ArmedAction.HasValue && ArmedAction.Value == action)
            {
                Disarm();
                return;
            }

            ArmedAction = action;
            ArmedFrame = Time.frameCount;

            SupportActionDefinition def = catalog != null ? catalog.Find(action) : null;
            string name = def != null ? def.Name : "SUPPORT";
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
                localCooldownUntil = Time.unscaledTime + message.CooldownSeconds;
                Status = name + " accepted.";
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
            if (ledger.IsRateLimited(playerId, now, RequestsPerSecond, 1f)) return SupportResult.RateLimited;
            if (!action.Enabled) return SupportResult.Disabled;
            if (!bypass && !perks.Grants(playerId, action.Capability)) return SupportResult.NotUnlocked;
            if (ledger.IsCoolingDown(playerId, now, settings.RequestCooldown.Value))
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

        internal float ServerCooldown => settings.RequestCooldown.Value;

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
            int limit = pool == SupportPool.Drop ? MaximumDropsInFlight : MaximumArtilleryJobs;
            if (reserved[(int)pool] >= limit) return false;
            reserved[(int)pool]++;
            return true;
        }

        void ISupportHost.Release(SupportPool pool) =>
            reserved[(int)pool] = Math.Max(0, reserved[(int)pool] - 1);

        void ISupportHost.Run(IEnumerator routine) => StartCoroutine(routine);

        bool ISupportHost.HasVehicleCapacity(int count)
        {
            for (int i = supportVehicles.Count - 1; i >= 0; i--)
                if (supportVehicles[i] == null || supportVehicles[i].disabled)
                    supportVehicles.RemoveAt(i);
            return supportVehicles.Count + count <= MaximumSupportVehicles;
        }

        void ISupportHost.TrackVehicle(GroundVehicle vehicle)
        {
            if (vehicle != null) supportVehicles.Add(vehicle);
        }

        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
