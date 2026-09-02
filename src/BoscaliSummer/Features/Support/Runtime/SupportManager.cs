using System;
using System.Collections;
using System.Collections.Generic;
using BepInEx.Logging;
using BoscaliSummer.Features.Support.Configuration;
using BoscaliSummer.Features.Support.Networking;
using BoscaliSummer.Features.Support.Runtime.Actions;
using BoscaliSummer.Framework.Contracts;
using BoscaliSummer.Framework.Lifecycle;
using Mirage;
using NuclearOption.Networking;
using UnityEngine;

namespace BoscaliSummer.Features.Support.Runtime
{
    internal sealed class SupportManager : MonoBehaviour, ISceneService
    {
        private const int MaximumSupportVehicles = 24;
        private const int MaximumDropsInFlight = 2;
        private const int MaximumArtilleryJobs = 2;
        private const int ArtilleryRounds = 4;

        private readonly SupportRequestLedger ledger = new SupportRequestLedger();
        private readonly List<GroundVehicle> supportVehicles = new List<GroundVehicle>();
        private SupportSettings settings;
        private IPlayerEntitlements entitlements;
        private IZoneFortificationService fortifications;
        private SupportNet network;
        private ManualLogSource logger;
        private int dropsInFlight;
        private int artilleryJobs;
        private int nextRequestId;

        public event Action StateChanged;
        public string Status { get; private set; } = "Select support and designate a target on the map.";

        public void Configure(
            SupportSettings supportSettings, IPlayerEntitlements playerEntitlements,
            IZoneFortificationService zoneFortifications, SupportNet net, ManualLogSource log)
        {
            settings = supportSettings;
            entitlements = playerEntitlements;
            fortifications = zoneFortifications;
            network = net;
            logger = log;
        }

        public void ResetForScene()
        {
            ledger.Clear();
            supportVehicles.Clear();
            dropsInFlight = 0;
            artilleryJobs = 0;
            StopAllCoroutines();
            Status = "Select support and designate a target on the map.";
            StateChanged?.Invoke();
        }

        public bool IsLocallyUnlocked(SupportActionId action)
        {
            if (!GameManager.GetLocalPlayer<Player>(out Player player) || player == null) return false;
            return entitlements.HasEntitlement(Identity(player), Entitlement(action));
        }

        public bool CanRequestLocally(SupportActionId action) =>
            settings != null && settings.Enabled.Value && ActionEnabled(action) && IsLocallyUnlocked(action);

        public float Cost(SupportActionId action)
        {
            switch (action)
            {
                case SupportActionId.VehicleAirdrop: return settings.VehicleAirdropCost.Value;
                case SupportActionId.Artillery: return settings.ArtilleryCost.Value;
                case SupportActionId.FortifyZone: return settings.FortificationCost.Value;
                default: return 0f;
            }
        }

        public void RequestAtMapCursor(SupportActionId action)
        {
            DynamicMap map = SceneSingleton<DynamicMap>.i;
            if (map == null || !DynamicMap.mapMaximized || !map.TryGetCursorCoordinates(out GlobalPosition target))
            {
                Status = "Open the maximized map and place the cursor over a valid target.";
                StateChanged?.Invoke();
                return;
            }
            network.Request(++nextRequestId, action, target);
            Status = "Request sent to host.";
            StateChanged?.Invoke();
        }

        internal void ReceiveRequest(INetworkPlayer sender, SupportRequestMessage request)
        {
            SupportResult result = ValidateAndExecute(sender, request);
            network.Reply(sender, request, result,
                result == SupportResult.Accepted ? settings.RequestCooldown.Value : 0f);
        }

        internal void ReceiveResult(SupportResultMessage message)
        {
            SupportResult result = (SupportResult)message.Result;
            Status = result == SupportResult.Accepted
                ? $"{(SupportActionId)message.Action} accepted. Cooldown {message.CooldownSeconds:0}s."
                : $"{(SupportActionId)message.Action} denied: {result}.";
            StateChanged?.Invoke();
        }

        private SupportResult ValidateAndExecute(INetworkPlayer sender, SupportRequestMessage request)
        {
            if (settings == null || !settings.Enabled.Value) return SupportResult.Disabled;
            if (sender == null || !sender.IsAuthenticated ||
                !sender.TryGetPlayer<Player>(out Player player) || player == null || player.HQ == null)
                return SupportResult.InvalidTarget;
            if (!Enum.IsDefined(typeof(SupportActionId), request.Action) ||
                !Finite(request.X) || !Finite(request.Y) || !Finite(request.Z))
                return SupportResult.InvalidTarget;

            SupportActionId action = (SupportActionId)request.Action;
            ulong playerId = Identity(player);
            if (ledger.IsDuplicate(playerId, request.RequestId)) return SupportResult.Duplicate;
            if (ledger.IsRateLimited(playerId, Time.unscaledTime, 2, 1f)) return SupportResult.RateLimited;
            if (!ActionEnabled(action)) return SupportResult.Disabled;
            if (!entitlements.HasEntitlement(playerId, Entitlement(action))) return SupportResult.NotUnlocked;
            if (ledger.IsCoolingDown(playerId, Time.unscaledTime, settings.RequestCooldown.Value))
                return SupportResult.Cooldown;
            float cost = Cost(action);
            if (player.Allocation + 0.001f < cost) return SupportResult.InsufficientAllocation;

            GlobalPosition target = new GlobalPosition(request.X, request.Y, request.Z);
            SupportResult result;
            switch (action)
            {
                case SupportActionId.VehicleAirdrop: result = TryAirdrop(player, target, request.RequestId); break;
                case SupportActionId.Artillery: result = TryArtillery(player, target, request.RequestId); break;
                case SupportActionId.FortifyZone: result = TryFortify(player, target); break;
                default: result = SupportResult.InvalidTarget; break;
            }
            if (result != SupportResult.Accepted) return result;
            player.SetAllocation(Mathf.Max(0f, player.Allocation - cost));
            ledger.Accept(playerId, Time.unscaledTime);
            logger.LogInfo($"Accepted {action} request {request.RequestId} from {player} for {target}.");
            return SupportResult.Accepted;
        }

        private SupportResult TryAirdrop(Player player, GlobalPosition target, int requestId)
        {
            RemoveInactiveVehicles();
            if (supportVehicles.Count >= MaximumSupportVehicles || dropsInFlight >= MaximumDropsInFlight)
                return SupportResult.Busy;
            if (!TryGround(target, out Vector3 ground)) return SupportResult.InvalidTarget;
            if (player.Aircraft == null ||
                Vector3.Distance(player.Aircraft.transform.position, ground) > 25000f)
                return SupportResult.InvalidTarget;
            VehicleDefinition definition = VanillaSupportCatalog.ResolveAirdropVehicle(settings);
            if (definition == null) return SupportResult.CapabilityUnavailable;
            if (player.HQ.GetUnitSupply(definition) <= 0) return SupportResult.NoStock;
            Spawner spawner = NetworkSceneSingleton<Spawner>.i;
            if (spawner == null) return SupportResult.CapabilityUnavailable;

            player.HQ.ModifyUnitSupply(definition, -1);
            try
            {
                Vector3 spawn = ground + Vector3.up * 550f;
                string unique = $"BoscaliSummer:Support:Drop:{Identity(player)}:{requestId}";
                GroundVehicle vehicle = spawner.SpawnVehicle(
                    definition.unitPrefab, spawn.ToGlobalPosition(), Quaternion.identity,
                    Vector3.down * 12f, player.HQ, unique, 1f, false, null);
                if (vehicle == null) throw new InvalidOperationException("Spawner returned no vehicle.");
                supportVehicles.Add(vehicle);
                dropsInFlight++;
                StartCoroutine(ReleaseDropSlot(vehicle));
                return SupportResult.Accepted;
            }
            catch (Exception e)
            {
                player.HQ.ModifyUnitSupply(definition, 1);
                logger.LogWarning("Vehicle airdrop failed and stock was refunded: " + e.Message);
                return SupportResult.SpawnFailed;
            }
        }

        private IEnumerator ReleaseDropSlot(GroundVehicle vehicle)
        {
            float until = Time.unscaledTime + 90f;
            while (vehicle != null && !vehicle.disabled && Time.unscaledTime < until &&
                vehicle.transform.position.y > Datum.LocalSeaY + 8f)
                yield return new WaitForSecondsRealtime(1f);
            dropsInFlight = Math.Max(0, dropsInFlight - 1);
        }

        private SupportResult TryFortify(Player player, GlobalPosition target)
        {
            if (fortifications == null) return SupportResult.CapabilityUnavailable;
            Vector3 localTarget = target.ToLocalPosition();
            Airbase closest = null;
            float closestDistance = float.MaxValue;
            foreach (Airbase airbase in player.HQ.GetAirbases())
            {
                if (airbase == null || airbase.AttachedAirbase || airbase.CurrentHQ != player.HQ) continue;
                Vector3 center = airbase.center != null ? airbase.center.position : airbase.transform.position;
                float distance = Vector3.Distance(center, localTarget);
                if (distance < closestDistance) { closest = airbase; closestDistance = distance; }
            }
            if (closest == null || closestDistance > Mathf.Max(closest.GetRadius() * 1.5f, 650f))
                return SupportResult.InvalidTarget;
            return fortifications.TryFortify(closest, player.HQ, player)
                ? SupportResult.Accepted
                : SupportResult.SpawnFailed;
        }

        private SupportResult TryArtillery(Player player, GlobalPosition target, int requestId)
        {
            if (artilleryJobs >= MaximumArtilleryJobs) return SupportResult.Busy;
            if (!TryGround(target, out Vector3 ground)) return SupportResult.InvalidTarget;
            if (NetworkSceneSingleton<Spawner>.i == null || player.Aircraft == null)
                return SupportResult.CapabilityUnavailable;
            if (Vector3.Distance(player.Aircraft.transform.position, ground) > 30000f)
                return SupportResult.InvalidTarget;
            MissileDefinition definition = VanillaSupportCatalog.ResolveArtilleryOrdnance(settings);
            if (definition == null) return SupportResult.CapabilityUnavailable;
            artilleryJobs++;
            StartCoroutine(ArtillerySalvo(player, definition, ground, requestId));
            return SupportResult.Accepted;
        }

        private IEnumerator ArtillerySalvo(Player player, MissileDefinition definition, Vector3 target, int requestId)
        {
            try
            {
                for (int round = 0; round < ArtilleryRounds; round++)
                {
                    if (NetworkSceneSingleton<Spawner>.i == null || player == null || player.HQ == null) yield break;
                    Vector2 scatter = UnityEngine.Random.insideUnitCircle * 45f;
                    Vector3 impact = target + new Vector3(scatter.x, 0f, scatter.y);
                    Vector3 spawn = impact + Vector3.up * 1000f;
                    string guide = player.Aircraft != null ? player.Aircraft.UniqueName : string.Empty;
                    Missile missile = NetworkSceneSingleton<Spawner>.i.SpawnSavedMissile(
                        definition.unitPrefab, spawn.ToGlobalPosition(), Quaternion.LookRotation(Vector3.down),
                        player.HQ, string.Empty, guide, Vector3.down * 220f,
                        $"BoscaliSummer:Support:Artillery:{Identity(player)}:{requestId}:{round}");
                    if (missile != null)
                    {
                        missile.SetAimpoint(impact.ToGlobalPosition(), Vector3.zero);
                        missile.Arm();
                    }
                    yield return new WaitForSecondsRealtime(1.25f);
                }
            }
            finally
            {
                artilleryJobs = Math.Max(0, artilleryJobs - 1);
            }
        }

        private static bool TryGround(GlobalPosition target, out Vector3 ground)
        {
            Vector3 local = target.ToLocalPosition();
            Vector3 origin = new Vector3(local.x, Mathf.Max(local.y, Datum.LocalSeaY) + 3000f, local.z);
            if (Physics.Raycast(origin, Vector3.down, out RaycastHit hit, 6000f,
                (int)PhysicsLayers.StaticsMask | (int)PhysicsLayers.ShipsMask))
            {
                ground = hit.point;
                if (ground.y <= Datum.LocalSeaY + 2f) return false;
                if (Vector3.Angle(hit.normal, Vector3.up) > 25f) return false;
                int blockers = (int)PhysicsLayers.DefaultMask | (int)PhysicsLayers.StaticsMask |
                    (int)PhysicsLayers.ShipsMask | (int)PhysicsLayers.ExclusionZonesMask;
                if (Physics.CheckSphere(ground + Vector3.up * 10f, 8f, blockers)) return false;
                return true;
            }
            ground = default;
            return false;
        }

        private void RemoveInactiveVehicles()
        {
            for (int i = supportVehicles.Count - 1; i >= 0; i--)
                if (supportVehicles[i] == null || supportVehicles[i].disabled) supportVehicles.RemoveAt(i);
        }

        private bool ActionEnabled(SupportActionId action) =>
            action == SupportActionId.VehicleAirdrop ? settings.VehicleAirdropsEnabled.Value :
            action == SupportActionId.Artillery ? settings.ArtilleryEnabled.Value :
            action == SupportActionId.FortifyZone && settings.FortificationEnabled.Value;

        private static string Entitlement(SupportActionId action) =>
            action == SupportActionId.VehicleAirdrop ? SupportEntitlements.VehicleAirdrop :
            action == SupportActionId.Artillery ? SupportEntitlements.Artillery :
            SupportEntitlements.Fortification;

        private static ulong Identity(Player player) => player.SteamID != 0UL
            ? player.SteamID
            : 0x8000000000000000UL | (uint)Math.Max(0, player.PlayerIndex);

        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
