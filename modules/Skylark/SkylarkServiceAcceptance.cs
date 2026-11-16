#if !PUBLIC_RELEASE
using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Mirage;
using NuclearOption.Networking;
using NuclearOption.SavedMission;
using UnityEngine;

namespace BoscaliSummer.Modules.Skylark
{
    // Sandbox fixtures and observations only. Purchase, spawn vetting, service and sensors remain native.
    public static class SkylarkServiceAcceptance
    {
        private static Hangar purchasedHangar;
        private static GameObject previousSpawn;
        private static Airbase selectedBase;
        private static Unit watchedTarget;
        private static Aircraft watchedAircraft;
        private static float watchStarted, firstDetection = -1f;
        private static int detectionEvents;
        private static Aircraft landingAircraft;
        private static float landedSince = -1f;
        private static readonly List<TargetDetector> watchedDetectors = new List<TargetDetector>();

        private static void RequireSandbox()
        {
            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("NOMODKIT_SIM_SCENARIO")))
                throw new InvalidOperationException("Skylark service exercise requires a nomodkit scenario.");
        }

        public static object Purchase(Dictionary<string, object> args)
        {
            RequireSandbox();
            if (!GameManager.GetLocalPlayer(out Player player) || player.HQ == null) throw new InvalidOperationException("No seated local faction player.");
            AircraftDefinition definition = SkylarkTemplate.Definition;
            var candidates = UnityEngine.Object.FindObjectsOfType<Airbase>()
                .Where(a => a != null && !a.disabled && !a.AttachedAirbase && a.CanSpawnAircraft(definition))
                .OrderBy(a => Vector3.Distance(a.transform.position, player.Aircraft.transform.position)).ToArray();
            selectedBase = candidates.FirstOrDefault(a => a.CurrentHQ == player.HQ);
            bool assignedNeutralBase = false;
            if (selectedBase == null && args.TryGetValue("claimNeutral", out object claim) && Convert.ToBoolean(claim))
            {
                selectedBase = candidates.FirstOrDefault(a => a.CurrentHQ == null);
                if (selectedBase != null)
                {
                    AccessTools.Method(typeof(Airbase), "CaptureFaction").Invoke(selectedBase, new object[] { player.HQ });
                    assignedNeutralBase = true;
                }
            }
            if (selectedBase == null) throw new InvalidOperationException("No compatible friendly land base; candidates=" + string.Join(",", candidates.Select(a => a.name + ":" + (a.CurrentHQ == null ? "neutral" : a.CurrentHQ.faction.factionName))));
            float before = player.Allocation;
            int ownedBefore = player.OwnedAirframeTypeCount(definition, true);
            float grant = Mathf.Max(0f, 100f - before);
            if (grant > 0f) player.AddAllocation(grant); // Explicit fixture funding, reported separately from purchase debit.
            float funded = player.Allocation;
            player.CmdPurchaseAirframe(definition);
            float afterPurchase = player.Allocation;
            int ownedAfter = player.OwnedAirframeTypeCount(definition, true);
            var loadout = new Loadout { weapons = definition.aircraftParameters.loadouts[0].weapons.ToList() };
            bool loadoutAllowed = loadout.AllowedByHQ(definition.unitPrefab.GetComponent<Aircraft>().weaponManager, player.HQ);
            bool gunAllowed = WeaponChecker.MountAllowedHQ(loadout.weapons[0], player.HQ);
            var request = AccessTools.Method(typeof(Spawner), "TrySpawnAircraft", new[] { typeof(Airbase), typeof(AircraftDefinition), typeof(LiveryKey), typeof(Loadout), typeof(float), typeof(INetworkPlayer) });
            var old = selectedBase.hangars.ToDictionary(h => h, h => SkylarkTemplate.Get<GameObject>(h, "spawnedObject"));
            var result = (Airbase.TrySpawnResult)request.Invoke(NetworkSceneSingleton<Spawner>.i,
                new object[] { selectedBase, definition, new LiveryKey(0), loadout, 1f, player.Owner });
            purchasedHangar = result.Hangar;
            previousSpawn = purchasedHangar != null && old.TryGetValue(purchasedHangar, out GameObject previous) ? previous : null;
            return new
            {
                allowed = result.Allowed, delayed = result.DelayedSpawn, airbase = selectedBase.name,
                hangar = purchasedHangar != null ? purchasedHangar.name : null, faction = player.HQ.faction.factionName,
                assignedNeutralBase, baseSetupCall = assignedNeutralBase ? "Airbase.CaptureFaction" : "none",
                allocationBefore = before, fixtureFunds = grant, fundsCall = grant > 0 ? "Player.AddAllocation" : "none", rankChanged = false,
                fundedAllocation = funded, afterPurchase, purchaseDebit = funded - afterPurchase, expectedPrice = definition.value,
                ownedBefore, ownedAfter, loadoutAllowed, gunAllowed,
                acceptedMounts = loadout.weapons.Select(w => w != null ? w.jsonKey : null).ToArray(),
                route = "Player.CmdPurchaseAirframe -> native Spawner.TrySpawnAircraft server handler -> WeaponChecker.VetLoadout -> Airbase.TrySpawnAircraft -> Hangar.TrySpawnAircraft"
            };
        }

        public static bool PurchasedReady(Dictionary<string, object> args)
        {
            if (purchasedHangar == null) return false;
            GameObject current = SkylarkTemplate.Get<GameObject>(purchasedHangar, "spawnedObject");
            return current != null && current != previousSpawn && GameManager.GetLocalPlayer(out Player player)
                && player.Aircraft == current.GetComponent<Aircraft>();
        }

        public static Dictionary<string, object> RegisterPurchased(Dictionary<string, object> args)
        {
            RequireSandbox();
            if (!PurchasedReady(args)) throw new InvalidOperationException("Native hangar spawn/player attachment has not finished.");
            Aircraft aircraft = SkylarkTemplate.Get<GameObject>(purchasedHangar, "spawnedObject").GetComponent<Aircraft>();
            return new Dictionary<string, object> { ["ok"] = true, ["ids"] = new Dictionary<string, object> { ["purchased"] = aircraft } };
        }

        public static object RequestService(Dictionary<string, object> args)
        {
            RequireSandbox();
            Aircraft aircraft = (Aircraft)args["idUnit"];
            aircraft.RequestRearm();
            return Inspect(args);
        }

        public static bool Landed(Dictionary<string, object> args)
        {
            Aircraft aircraft = (Aircraft)args["idUnit"];
            if (landingAircraft != aircraft) { landingAircraft = aircraft; landedSince = -1f; }
            bool settled = !aircraft.disabled && aircraft.radarAlt < .25f && aircraft.rb.velocity.sqrMagnitude < .25f
                && aircraft.partLookup.All(p => !p.IsDetached() && p.hitPoints > 0f);
            if (!settled) { landedSince = -1f; return false; }
            if (landedSince < 0f) landedSince = Time.time;
            return Time.time - landedSince >= 2f;
        }

        public static object Inspect(Dictionary<string, object> args)
        {
            Aircraft aircraft = (Aircraft)args["idUnit"];
            Player player = aircraft.GetPlayer();
            bool rearmerFound = aircraft.NetworkHQ.RearmMissionController.TryGetRearmer(aircraft, out Rearmer rearmer);
            return new
            {
                nativeHangar = aircraft.NetworkspawningHangar != null ? aircraft.NetworkspawningHangar.name : null,
                playerAttached = player != null && player.Aircraft == aircraft,
                allocation = player != null ? player.Allocation : -1f,
                ammo = aircraft.weaponStations.Select(s => new { s.Number, name = s.WeaponInfo.weaponName, ammo = s.GetAmmoTotal(), s.FullAmmo }).ToArray(),
                gunAmmo = aircraft.weaponStations.Where(s => s.WeaponInfo.gun).Sum(s => s.GetAmmoTotal()),
                missileAmmo = aircraft.weaponStations.Where(s => !s.WeaponInfo.gun).Sum(s => s.GetAmmoTotal()),
                allFull = aircraft.weaponStations.All(s => s.GetAmmoTotal() == s.FullAmmo),
                aircraft.disabled, detached = aircraft.partLookup.Count(p => p.IsDetached()),
                minimumPartHP = aircraft.partLookup.Min(p => p.hitPoints),
                requested = aircraft.HasRequestedRearm, aircraft.radarAlt, aircraft.speed, rearmerFound,
                rearmer = rearmer != null ? rearmer.Unit.name : null, capacity = rearmer != null ? rearmer.Capacity : -1f,
                liveryOptions = ((AircraftDefinition)aircraft.definition).aircraftParameters.liveries.Select(l => l.name).ToArray(),
                liveryCount = ((AircraftDefinition)aircraft.definition).aircraftParameters.liveries.Count,
                livery = aircraft.NetworkLiveryKey.ToString()
            };
        }

        public static object WatchTarget(Dictionary<string, object> args)
        {
            RequireSandbox();
            foreach (TargetDetector old in watchedDetectors) if (old != null) old.onDetectTarget -= Detected;
            watchedDetectors.Clear();
            watchedAircraft = (Aircraft)args["idUnit"]; watchedTarget = (Unit)args["targetUnit"];
            // Native complex physics reparents damage islands outside the aircraft root.
            watchedDetectors.AddRange(watchedAircraft.partLookup
                .SelectMany(p => p.GetComponentsInChildren<TargetDetector>(true))
                .Concat(watchedAircraft.GetComponentsInChildren<TargetDetector>(true)).Distinct()
                .Where(d => d.GetAttachedUnit() == watchedAircraft));
            watchStarted = Time.time; firstDetection = -1f; detectionEvents = 0;
            foreach (TargetDetector detector in watchedDetectors) detector.onDetectTarget += Detected;
            return Sensors(args);
        }

        private static void Detected(Unit target)
        {
            if (target != watchedTarget) return;
            detectionEvents++;
            if (firstDetection < 0f) firstDetection = Time.time - watchStarted;
        }

        public static bool Acquired(Dictionary<string, object> args) => Time.time - watchStarted >= 8f
            && detectionEvents > 0 && watchedDetectors.Any(d => d != null && d.detectedTargets.Contains(watchedTarget));

        public static object Sensors(Dictionary<string, object> args)
        {
            return new
            {
                elapsed = Time.time - watchStarted, detectionEvents, firstDetection,
                distance = Vector3.Distance(watchedAircraft.transform.position, watchedTarget.transform.position),
                forwardDot = Vector3.Dot(watchedAircraft.transform.forward, (watchedTarget.transform.position - watchedAircraft.transform.position).normalized),
                target = watchedTarget.name, opposingFaction = watchedTarget.NetworkHQ != watchedAircraft.NetworkHQ,
                hqTracked = watchedAircraft.NetworkHQ.IsTargetBeingTracked(watchedTarget),
                ownDetected = watchedDetectors.Any(d => d != null && d.detectedTargets.Contains(watchedTarget)),
                weaponTargetCount = watchedAircraft.weaponManager.GetTargetList().Count,
                detectors = watchedDetectors.Where(d => d != null).Select(d => new { type = d.GetType().Name, d.activated,
                    operational = d.IsOperational(), visualRange = d.GetVisualRange(), radarRange = d.GetRadarRange(),
                    detected = d.detectedTargets.Contains(watchedTarget), detectedCount = d.detectedTargets.Count }).ToArray()
            };
        }
    }
}
#endif
