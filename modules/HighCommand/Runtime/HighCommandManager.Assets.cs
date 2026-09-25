using System;
using System.Collections.Generic;
using BoscaliSummer.Core;
using BoscaliSummer.Features.HighCommand.Domain;
using BoscaliSummer.Framework.Contracts;
using BoscaliSummer.Runtime;
using NuclearOption.Networking;
using UnityEngine;

namespace BoscaliSummer.Features.HighCommand.Runtime
{
    /// <summary>
    /// The world half of the chain of command: posts, convoys, intel and kill credit.
    /// Everything here is server-only; clients only ever see snapshots.
    /// </summary>
    internal sealed partial class HighCommandManager
    {
        internal static HighCommandManager Active { get; private set; }

        private Encyclopedia catalog;
        private int catalogBuildings = -1, catalogVehicles = -1;
        private BuildingDefinition postDefinition;
        private VehicleDefinition convoyDefinition;
        private readonly Vector3[] intelPositions = new Vector3[MaximumFactions * CommandTier.MaximumSlots];
        private readonly float[] intelRadii = new float[MaximumFactions * CommandTier.MaximumSlots];
        private readonly byte[] intelOwners = new byte[MaximumFactions * CommandTier.MaximumSlots];
        private readonly int[] intelKeys = new int[MaximumFactions * CommandTier.MaximumSlots];

        private static float MissionTime => NetworkSceneSingleton<MissionManager>.i?.MissionTime ?? 0f;

        // ---- Lookup -----------------------------------------------------------------------

        private FactionCommand FindFaction(FactionHQ hq)
        {
            if (hq == null) return null;
            for (int i = 0; i < factions.Count; i++)
                if (factions[i].Hq == hq) return factions[i];
            return null;
        }

        private static int GlobalId(int factionIndex, int slotId) =>
            factionIndex * CommandTier.MaximumSlots + slotId;

        private static int FactionOf(int globalId) => globalId / CommandTier.MaximumSlots;

        private static int LocalId(int globalId) => globalId % CommandTier.MaximumSlots;

        private AssetWatch FindAsset(FactionCommand owner, CommandSlot slot, AssetKind kind)
        {
            for (int i = 0; i < assets.Count; i++)
            {
                AssetWatch watch = assets[i];
                if (watch.Owner == owner && watch.Slot == slot && watch.Kind == kind) return watch;
            }
            return null;
        }

        private AssetWatch FindActiveAsset(FactionCommand owner, CommandSlot slot)
        {
            return slot.Status == CommanderStatus.InTransit
                ? FindAsset(owner, slot, AssetKind.ConvoyLead)
                : FindAsset(owner, slot, AssetKind.Post);
        }

        private Convoy FindConvoy(FactionCommand owner, CommandSlot slot)
        {
            for (int i = 0; i < convoys.Count; i++)
                if (convoys[i].Owner == owner && convoys[i].Slot == slot) return convoys[i];
            return null;
        }

        private static bool HasParticipant(FactionHQ hq)
        {
            List<Player> players = hq?.GetPlayers(false);
            return players != null && players.Count > 0;
        }

        private static bool HasSecondBase(FactionCommand owner, CommandSlot slot)
        {
            if (owner.Sites.Count < 2) return false;
            for (int i = 0; i < owner.Sites.Count; i++)
                if (!string.Equals(owner.Sites[i], slot.SiteName, StringComparison.Ordinal)) return true;
            return false;
        }

        // ---- Sites ------------------------------------------------------------------------

        private void CollectSites(FactionHQ hq, List<string> sites)
        {
            sites.Clear();
            int inspected = 0;
            foreach (Airbase airbase in FactionRegistry.airbaseLookup.Values)
            {
                if (++inspected > 64 || sites.Count >= CommandTree.MaximumSites) break;
                if (airbase == null || airbase.CurrentHQ != hq || airbase.AttachedAirbase ||
                    airbase.UnitDestroyed() || airbase.center == null) continue;
                if (!sites.Contains(airbase.name)) sites.Add(airbase.name);
            }
            sites.Sort(StringComparer.Ordinal);
        }

        private Airbase FindBase(FactionCommand owner, string siteName)
        {
            Airbase fallback = null;
            int inspected = 0;
            foreach (Airbase airbase in FactionRegistry.airbaseLookup.Values)
            {
                if (++inspected > 64) break;
                if (airbase == null || airbase.CurrentHQ != owner.Hq || airbase.AttachedAirbase ||
                    airbase.UnitDestroyed() || airbase.center == null) continue;
                if (!string.Equals(airbase.name, siteName, StringComparison.Ordinal))
                {
                    if (fallback == null) fallback = airbase;
                    continue;
                }
                return airbase;
            }
            return fallback;
        }

        // ---- Posts ------------------------------------------------------------------------

        private void SpawnMissingAssets()
        {
            for (int f = 0; f < factions.Count; f++)
            {
                FactionCommand owner = factions[f];
                for (int i = 0; i < owner.Tree.Slots.Count; i++)
                {
                    CommandSlot slot = owner.Tree.Slots[i];
                    if (!slot.Alive) continue;
                    if (FindAsset(owner, slot, AssetKind.Post) != null) continue;
                    if (owner.RespawnScheduled[slot.Id])
                    {
                        if (MissionTime < owner.RespawnAt[slot.Id]) continue;
                        owner.RespawnScheduled[slot.Id] = false;
                    }
                    if (assets.Count >= MaximumAssets || !SpawnPost(owner, slot)) continue;
                }
            }
        }

        private bool SpawnPost(FactionCommand owner, CommandSlot slot)
        {
            Spawner spawner = NetworkSceneSingleton<Spawner>.i;
            if (spawner == null || !spawner.IsServer) return false;
            BuildingDefinition definition = PickPostDefinition();
            if (definition == null) return false;

            Airbase baseTarget = FindBase(owner, slot.SiteName);
            if (baseTarget == null || baseTarget.center == null) return false;
            // Record the anchor before placement, so a post that is between spawns still has
            // a place on the map instead of reporting the origin.
            if (!owner.HasAnchor[slot.Id])
            {
                owner.Anchors[slot.Id] = baseTarget.center.position;
                owner.HasAnchor[slot.Id] = true;
            }
            Vector3 anchor = owner.Anchors[slot.Id];

            int angleSeed = unchecked((int)(uint)(owner.SpawnSerial * 31 + slot.Id * 7));
            if (!TryPlaceAround(definition, anchor, angleSeed, out Vector3 position, out Quaternion rotation))
            {
                anchor = baseTarget.center.position;
                if (!TryPlaceAround(definition, anchor, angleSeed + 5, out position, out rotation)) return false;
            }

            string name = "BoscaliSummer:HighCommand:" + owner.FactionToken + ":" + slot.Id + ":" + (++owner.SpawnSerial);
            Unit unit = spawner.SpawnBuilding(definition.unitPrefab, position.ToGlobalPosition(), rotation,
                owner.Hq, baseTarget, name, false, null);
            if (unit == null) return false;
            Watch(owner, slot, unit, AssetKind.Post);
            logger?.LogInfo("[HighCommand] Post established: " + slot.Role + " at " + slot.SiteName + " (" + baseTarget.name + ").");
            return true;
        }

        private void Watch(FactionCommand owner, CommandSlot slot, Unit unit, AssetKind kind)
        {
            var watch = new AssetWatch { Owner = owner, Slot = slot, Unit = unit, Kind = kind };
            watch.InstanceId = unit.GetInstanceID();
            assets.Add(watch);
            assetLookup[watch.InstanceId] = watch;
            watch.Watch();
        }

        private static void Unwatch(AssetWatch watch)
        {
            if (watch == null) return;
            watch.Unwatch();
            watch.Unit = null;
        }

        // ---- Damage and death -------------------------------------------------------------

        /// <summary>
        /// Bounded 1 Hz backstop for the disable event: a tracked asset that is gone or
        /// disabled without a callback is resolved through the same path.
        /// </summary>
        private void SweepAssets()
        {
            for (int i = assets.Count - 1; i >= 0; i--)
            {
                AssetWatch watch = assets[i];
                if (watch.Unit == null)
                {
                    assetLookup.Remove(watch.InstanceId);
                    assets.RemoveAt(i);
                    continue;
                }
                if (!watch.Unit.disabled) continue;
                OnAssetDisabled(watch, watch.Unit);
            }
        }

        internal void RecordDamage(Unit victim, PersistentID dealer)
        {
            if (victim == null || !assetLookup.TryGetValue(victim.GetInstanceID(), out AssetWatch watch)) return;
            if (lastDamage.Count >= MaximumAssets && !lastDamage.ContainsKey(victim.GetInstanceID())) return;
            lastDamage[victim.GetInstanceID()] = dealer;

            // The first hit of a window is the beat the page shows; further hits only keep
            // the window open, so a strafing run is one alert, not one per bullet.
            float now = MissionTime;
            bool raised = now >= watch.Slot.AlertUntil;
            watch.Slot.AlertUntil = now + AlertSeconds;
            if (raised)
            {
                Broadcast(watch.Owner, GlobalId(FactionIndex(watch.Owner.Hq), watch.Slot.Id),
                    CommanderLogTone.Alert,
                    (watch.Kind == AssetKind.ConvoyLead ? "VIP CONVOY UNDER FIRE · " : "POST UNDER FIRE · ") +
                    watch.Slot.SiteName, now);
            }
        }

        private void OnAssetDisabled(AssetWatch watch, Unit unit)
        {
            if (!GameAccess.IsServer() || watch == null || unit == null || !unit.disabled) return;
            int instanceId = watch.InstanceId;
            if (!assetLookup.Remove(instanceId)) return;
            assets.Remove(watch);
            lastDamage.Remove(instanceId);
            watch.Unwatch();

            FactionCommand owner = watch.Owner;
            CommandSlot slot = watch.Slot;
            float now = MissionTime;
            CreditKill(owner, slot, instanceId);

            if (watch.Kind == AssetKind.ConvoyLead)
            {
                AbortTransfer(FindConvoy(owner, slot));
                KillCommander(owner, slot, now, "KILLED IN TRANSIT", postDestroyed: false);
                return;
            }

            if (slot.Status == CommanderStatus.InTransit)
            {
                Broadcast(owner, GlobalId(FactionIndex(owner.Hq), slot.Id), CommanderLogTone.Alert,
                    slot.Person.Name + " WAS AFIELD — SURVIVED THE STRIKE ON THEIR POST", now);
                owner.RespawnScheduled[slot.Id] = true;
                owner.RespawnAt[slot.Id] = now + settings.PostRespawnSeconds.Value;
            }
            else
            {
                KillCommander(owner, slot, now, "KILLED AT " + slot.SiteName, postDestroyed: true);
            }
        }

        private void KillCommander(FactionCommand owner, CommandSlot slot, float now, string cause, bool postDestroyed)
        {
            CommandPerson dead = slot.Person;
            float disruption = settings.DisruptionSeconds.Value + CommandTraits.DisruptionBonus(dead?.Traits ?? CommandTrait.None);
            int nextSeed = unchecked(owner.SeedSerial + 31 * (++owner.RespawnSerial));
            int displaced = owner.Tree.Promote(slot.Id, nextSeed);
            slot.Status = CommanderStatus.Disrupted;
            slot.StatusUntil = now + disruption;
            slot.AlertUntil = 0f;
            if (displaced >= 0)
            {
                CommandSlot successor = owner.Tree.Find(displaced);
                if (successor != null)
                {
                    successor.Status = CommanderStatus.Disrupted;
                    successor.StatusUntil = now + disruption * 0.75f;
                }
            }
            if (postDestroyed)
            {
                owner.RespawnScheduled[slot.Id] = true;
                owner.RespawnAt[slot.Id] = now + settings.PostRespawnSeconds.Value;
            }
            Broadcast(owner, GlobalId(FactionIndex(owner.Hq), slot.Id), CommanderLogTone.Loss,
                dead.Rank + " " + dead.Name + " " + cause + " · " +
                slot.Person.Rank + " " + slot.Person.Name + " ASSUMES POST", now);
        }

        private void CreditKill(FactionCommand owner, CommandSlot slot, int instanceId)
        {
            if (!settings.EconomyEnabled.Value || slot.Person == null) return;
            if (!lastDamage.TryGetValue(instanceId, out PersistentID dealer)) return;
            if (!UnitRegistry.TryGetPersistentUnit(dealer, out PersistentUnit source)) return;
            FactionHQ killer = source.GetHQ();
            if (killer == null || killer == owner.Hq) return;

            FactionCommand killerCommand = FindFaction(killer);
            float trait = CommandTraits.BountyMultiplier(slot.Person.Traits);
            int funds = CommandEconomy.BountyFunds(slot.Tier,
                settings.BountyBaseFunds.Value, settings.BountyComponentFunds.Value,
                settings.BountyTheaterFunds.Value, trait);
            if (funds <= 0) return;

            killer.AddFunds(funds);
            killer.AddScore(CommandEconomy.BountyScore(slot.Tier));
            if (killerCommand != null)
            {
                Broadcast(killerCommand, GlobalId(FactionIndex(owner.Hq), slot.Id), CommanderLogTone.Loss,
                    "KILL PAID +" + funds + " · " + slot.Person.Rank + " " +
                    slot.Person.Name + " ELIMINATED", MissionTime);
            }
        }

        // ---- Transfers and convoys --------------------------------------------------------

        private void TickTransfers(float now)
        {
            if (!settings.TransfersEnabled.Value) return;
            for (int i = 0; i < factions.Count; i++)
            {
                FactionCommand command = factions[i];
                if (now < command.NextTransfer) continue;
                command.NextTransfer = now + NextTransferDelay();
                if (HasActiveTransfer(command) || !HasParticipant(command.Hq)) continue;

                CommandSlot pick = PickTransferSlot(command);
                if (pick != null && TryStartTransfer(command, pick))
                    Broadcast(command, GlobalId(FactionIndex(command.Hq), pick.Id), CommanderLogTone.Contact,
                        pick.Person.Name + " IS TRAVELLING TO " +
                        (FindConvoy(command, pick)?.DestinationName ?? "A FORWARD BASE"), now);
            }
        }

        private CommandSlot PickTransferSlot(FactionCommand command)
        {
            CommandSlot best = null;
            int seen = 0;
            for (int i = 0; i < command.Tree.Slots.Count; i++)
            {
                CommandSlot slot = command.Tree.Slots[i];
                if (!slot.Alive || slot.Status != CommanderStatus.Active) continue;
                if (FindAsset(command, slot, AssetKind.Post) == null || !HasSecondBase(command, slot)) continue;
                seen++;
                int roll = (int)(Deterministic.UnitFloat(Deterministic.Hash(
                    ++command.SeedSerial, slot.Id, missionGeneration)) * seen);
                if (roll == 0) best = slot;
            }
            return best;
        }

        private bool HasActiveTransfer(FactionCommand command)
        {
            for (int i = 0; i < convoys.Count; i++)
                if (convoys[i].Owner == command) return true;
            return false;
        }

        private bool TryStartTransfer(FactionCommand command, CommandSlot slot)
        {
            if (convoys.Count >= MaximumConvoys) return false;
            Spawner spawner = NetworkSceneSingleton<Spawner>.i;
            if (spawner == null || !spawner.IsServer) return false;
            VehicleDefinition definition = PickConvoyDefinition();
            AssetWatch post = FindAsset(command, slot, AssetKind.Post);
            if (definition == null || post?.Unit == null) return false;

            Vector3 home = post.Unit.transform.position;
            Airbase destination = PickDestination(command, slot.SiteName);
            if (destination == null || destination.center == null) return false;
            Vector3 target = destination.center.position;

            var convoy = new Convoy
            {
                Owner = command, Slot = slot, Home = home, Destination = target,
                DestinationName = destination.name, Phase = 0, PhaseUntil = MissionTime + TransferTimeoutSeconds,
                Deadline = MissionTime + TransferTimeoutSeconds,
            };

            int angleSeed = unchecked((int)(uint)(command.SpawnSerial * 17 + slot.Id * 3));
            for (int i = 0; i < 3; i++)
            {
                if (!TryPlaceAround(definition, home, angleSeed + i * 3, i * 24f, out Vector3 position, out Quaternion rotation))
                {
                    DestroyConvoyVehicles(convoy);
                    return false;
                }
                string name = "BoscaliSummer:HighCommand:convoy:" + command.FactionToken + ":" +
                              slot.Id + ":" + (++command.SpawnSerial) + ":" + i;
                Unit unit = spawner.SpawnVehicle(definition.unitPrefab, position.ToGlobalPosition(), rotation,
                    Vector3.zero, command.Hq, name, 1f, true, null);
                if (unit == null)
                {
                    DestroyConvoyVehicles(convoy);
                    return false;
                }
                convoy.Vehicles.Add(unit);
            }

            convoy.Lead = convoy.Vehicles[0];
            if (convoy.Lead is GroundVehicle lead && lead.UnitCommand != null)
                lead.UnitCommand.SetDestination(target.ToGlobalPosition(), false);
            else
            {
                DestroyConvoyVehicles(convoy);
                return false;
            }

            Watch(command, slot, convoy.Lead, AssetKind.ConvoyLead);
            convoys.Add(convoy);
            slot.Status = CommanderStatus.InTransit;
            slot.StatusUntil = 0f;
            return true;
        }

        private Airbase PickDestination(FactionCommand owner, string currentName)
        {
            Airbase fallback = null;
            int inspected = 0;
            int choice = unchecked((int)(uint)(owner.SeedSerial * 2654435761) % Math.Max(1, owner.Sites.Count));
            int seen = 0;
            foreach (Airbase airbase in FactionRegistry.airbaseLookup.Values)
            {
                if (++inspected > 64) break;
                if (airbase == null || airbase.CurrentHQ != owner.Hq || airbase.AttachedAirbase ||
                    airbase.UnitDestroyed() || airbase.center == null ||
                    string.Equals(airbase.name, currentName, StringComparison.Ordinal)) continue;
                if (fallback == null) fallback = airbase;
                if (seen++ == choice) return airbase;
            }
            return fallback;
        }

        private void TickConvoys(float now)
        {
            for (int i = convoys.Count - 1; i >= 0; i--)
            {
                Convoy convoy = convoys[i];
                if (convoy.Lead == null || convoy.Lead.disabled)
                {
                    AbortTransfer(convoy);
                    continue;
                }

                float distance = (convoy.Lead.transform.position - convoy.Destination).sqrMagnitude;
                if (convoy.Phase == 0 && distance < 120f * 120f)
                {
                    convoy.Phase = 1;
                    convoy.PhaseUntil = now + TransferDwellSeconds;
                }
                else if (convoy.Phase == 1 && now >= convoy.PhaseUntil)
                {
                    convoy.Phase = 2;
                    convoy.Destination = convoy.Home;
                    convoy.DestinationName = convoy.Slot.SiteName;
                    if (convoy.Lead is GroundVehicle vehicle && vehicle.UnitCommand != null)
                        vehicle.UnitCommand.SetDestination(convoy.Home.ToGlobalPosition(), false);
                }
                else if (convoy.Phase == 2 && distance < 120f * 120f)
                {
                    FinishTransfer(convoy, arrived: true);
                    continue;
                }

                if (now >= convoy.Deadline)
                {
                    FinishTransfer(convoy, arrived: false);
                }
            }
        }

        private void AbortTransfer(Convoy convoy)
        {
            if (convoy == null) return;
            AssetWatch watch = FindAsset(convoy.Owner, convoy.Slot, AssetKind.ConvoyLead);
            if (watch != null && watch.Kind == AssetKind.ConvoyLead)
            {
                watch.Unwatch();
                assets.Remove(watch);
                assetLookup.Remove(watch.InstanceId);
            }
            DestroyConvoyVehicles(convoy);
            convoys.Remove(convoy);
            if (convoy.Slot.Status == CommanderStatus.InTransit) convoy.Slot.Status = CommanderStatus.Active;
        }

        private void FinishTransfer(Convoy convoy, bool arrived)
        {
            if (convoy == null) return;
            AssetWatch watch = FindAsset(convoy.Owner, convoy.Slot, AssetKind.ConvoyLead);
            if (watch != null && watch.Kind == AssetKind.ConvoyLead)
            {
                watch.Unwatch();
                assets.Remove(watch);
                assetLookup.Remove(watch.InstanceId);
            }
            DestroyConvoyVehicles(convoy);
            convoys.Remove(convoy);
            if (arrived)
            {
                convoy.Slot.Status = CommanderStatus.Active;
                Broadcast(convoy.Owner, GlobalId(FactionIndex(convoy.Owner.Hq), convoy.Slot.Id),
                    CommanderLogTone.Staff,
                    convoy.Slot.Person.Name + " RETURNED TO " + convoy.Slot.SiteName, MissionTime);
            }
            else
            {
                convoy.Slot.Status = CommanderStatus.Active;
            }
        }

        private void DestroyConvoy(Convoy convoy)
        {
            if (convoy == null) return;
            DestroyConvoyVehicles(convoy);
        }

        private void DestroyConvoyVehicles(Convoy convoy)
        {
            for (int i = 0; i < convoy.Vehicles.Count; i++) TryRemove(convoy.Vehicles[i]);
            convoy.Vehicles.Clear();
            convoy.Lead = null;
        }

        private static bool TryRemove(Unit unit)
        {
            try
            {
                Spawner spawner = NetworkSceneSingleton<Spawner>.i;
                if (spawner != null && spawner.IsServer) spawner.ServerObjectManager.Destroy(unit.gameObject);
                else if (unit != null && unit.IsServer) UnityEngine.Object.Destroy(unit.gameObject);
                else return false;
                return true;
            }
            catch { return false; }
        }

        // ---- Intel ------------------------------------------------------------------------

        private void TickIntel(float now)
        {
            int count = 0;
            for (int o = 0; o < factions.Count; o++)
            {
                for (int s = 0; s < factions[o].Tree.Slots.Count; s++)
                {
                    if (count >= intelPositions.Length) break;
                    CommandSlot slot = factions[o].Tree.Slots[s];
                    AssetWatch asset = FindActiveAsset(factions[o], slot);
                    if (asset?.Unit == null) continue;
                    intelOwners[count] = (byte)o;
                    intelKeys[count] = o * CommandTier.MaximumSlots + slot.Id;
                    intelPositions[count] = asset.Unit.transform.position;
                    intelRadii[count] = (slot.Status == CommanderStatus.InTransit ? IntelConvoyRadius : IntelPostRadius) *
                        CommandTraits.IntelRadiusMultiplier(slot.Person?.Traits ?? CommandTrait.None);
                    count++;
                }
            }

            List<Unit> units = UnitRegistry.allUnits;
            if (units == null) return;
            int inspected = 0;
            for (int i = 0; i < units.Count; i++)
            {
                if (++inspected > 4096) break;
                Unit unit = units[i];
                if (unit == null || unit.disabled || unit.NetworkHQ == null || unit.NetworkHQ.faction == null) continue;
                int observer = FactionIndex(unit.NetworkHQ);
                if (observer < 0) continue;
                Vector3 position = unit.transform.position;
                for (int c = 0; c < count; c++)
                {
                    if (intelOwners[c] == observer) continue;
                    float radius = intelRadii[c];
                    if ((position - intelPositions[c]).sqrMagnitude > radius * radius) continue;
                    int key = intelKeys[c];
                    bool first = sight[observer, key] <= 0f;
                    sight[observer, key] = now <= 0f ? 0.01f : now;
                    if (first) ConfirmContact(observer, intelOwners[c], key, now);
                }
            }
        }

        /// <summary>One log beat per observer and post, the first time a patrol reports it.</summary>
        private void ConfirmContact(int observer, int ownerIndex, int globalId, float now)
        {
            if (observer < 0 || observer >= factions.Count ||
                ownerIndex < 0 || ownerIndex >= factions.Count) return;
            CommandSlot slot = factions[ownerIndex].Tree.Find(LocalId(globalId));
            if (slot?.Person == null) return;
            Broadcast(factions[observer], globalId, CommanderLogTone.Contact,
                "CONTACT · " + slot.Person.Rank + " " + slot.Person.Name + " AT " + slot.SiteName, now);
        }

        private int FactionIndex(FactionHQ hq)
        {
            for (int i = 0; i < factions.Count; i++)
                if (factions[i].Hq == hq) return i;
            return -1;
        }

        private bool IsKnown(FactionCommand observer, FactionCommand owner, CommandSlot slot, float now)
        {
            if (observer == null || owner == null || slot == null) return false;
            int observerIndex = factions.IndexOf(observer);
            int ownerIndex = factions.IndexOf(owner);
            if (observerIndex < 0 || ownerIndex < 0) return false;
            float last = sight[observerIndex, ownerIndex * CommandTier.MaximumSlots + slot.Id];
            return last > 0f && now - last <= IntelMemorySeconds;
        }

        private float LastSight(int observerIndex, FactionCommand owner, CommandSlot slot)
        {
            int ownerIndex = factions.IndexOf(owner);
            if (observerIndex < 0 || ownerIndex < 0) return 0f;
            return sight[observerIndex, ownerIndex * CommandTier.MaximumSlots + slot.Id];
        }

        // ---- Signals ----------------------------------------------------------------------

        /// <summary>
        /// The one place a staff event is stated: the status-strip signal and the page's log
        /// are the same sentence, remembered with its subject and its tone.
        /// </summary>
        private void Broadcast(FactionCommand command, int targetId, CommanderLogTone tone, string text, float now)
        {
            command.Signal = text.Length > 96 ? text.Substring(0, 96) : text;
            command.SignalUntil = now + SignalSeconds;
            command.Log.Append(targetId, tone, text, now);
        }

        // ---- Placement and catalogues -----------------------------------------------------

        private BuildingDefinition PickPostDefinition()
        {
            Encyclopedia encyclopedia = Encyclopedia.i;
            if (encyclopedia == null || encyclopedia.buildings == null) return null;
            int count = Math.Min(256, encyclopedia.buildings.Count);
            if (encyclopedia == catalog && count == catalogBuildings && postDefinition != null) return postDefinition;
            catalog = encyclopedia; catalogBuildings = count;
            postDefinition = null;
            BuildingType[] preference =
            {
                BuildingType.FAC, BuildingType.DEP, BuildingType.AMMO, BuildingType.HGR,
                BuildingType.CIV, BuildingType.DEF, BuildingType.RDR,
            };
            for (int p = 0; p < preference.Length && postDefinition == null; p++)
            {
                for (int i = 0; i < count; i++)
                {
                    BuildingDefinition candidate = encyclopedia.buildings[i];
                    if (candidate == null || candidate.buildingType != preference[p]) continue;
                    if (!Usable(candidate) || candidate.unitPrefab.GetComponent<Building>() == null) continue;
                    if (postDefinition == null || candidate.value < postDefinition.value) postDefinition = candidate;
                }
            }
            logger?.LogInfo("[HighCommand] Command post structure: " +
                            (postDefinition != null ? postDefinition.name : "none available"));
            return postDefinition;
        }

        private VehicleDefinition PickConvoyDefinition()
        {
            Encyclopedia encyclopedia = Encyclopedia.i;
            if (encyclopedia == null || encyclopedia.vehicles == null) return null;
            int count = Math.Min(256, encyclopedia.vehicles.Count);
            if (encyclopedia == catalog && count == catalogVehicles && convoyDefinition != null) return convoyDefinition;
            catalog = encyclopedia; catalogVehicles = count;
            convoyDefinition = null;
            for (int i = 0; i < count; i++)
            {
                VehicleDefinition candidate = encyclopedia.vehicles[i];
                if (candidate == null || !Usable(candidate) ||
                    candidate.unitPrefab.GetComponent<GroundVehicle>()?.UnitCommand == null) continue;
                if (convoyDefinition == null || candidate.value < convoyDefinition.value) convoyDefinition = candidate;
            }
            return convoyDefinition;
        }

        private static bool Usable(UnitDefinition definition) => definition != null && definition.unitPrefab != null &&
            definition.IsAllowed(MissionManager.AllowEventContent) &&
            Finite(definition.spawnOffset) && definition.spawnOffset.sqrMagnitude <= 400f &&
            Finite(definition.value) && definition.value > 0f &&
            Finite(definition.width) && definition.width > 0f && definition.width <= 20f &&
            Finite(definition.length) && definition.length > 0f && definition.length <= 25f &&
            Finite(definition.height) && definition.height > 0f && definition.height <= 20f;

        private bool TryPlaceAround(UnitDefinition definition, Vector3 anchor, int seed, out Vector3 position, out Quaternion rotation)
            => TryPlaceAround(definition, anchor, seed, 0f, 3, out position, out rotation);

        private bool TryPlaceAround(UnitDefinition definition, Vector3 anchor, int seed, float radiusOffset,
            out Vector3 position, out Quaternion rotation)
            => TryPlaceAround(definition, anchor, seed, radiusOffset, 1, out position, out rotation);

        private bool TryPlaceAround(UnitDefinition definition, Vector3 anchor, int seed, float radiusOffset,
            int rings, out Vector3 position, out Quaternion rotation)
        {
            position = default;
            rotation = Quaternion.identity;
            if (!Usable(definition)) return false;
            // A post is more valuable than a tight footprint: a base apron or a slope can leave
            // the near ring unusable, so widen outwards before declaring the site unspawnable.
            for (int ring = 0; ring < rings; ring++)
            {
                float radius = (90f + radiusOffset) * (1f + ring * 0.9f);
                for (int attempt = 0; attempt < 12; attempt++)
                {
                    float angle = (seed * 0.618f + attempt + ring * 5) * Mathf.PI / 6f;
                    Vector3 direction = new Vector3(Mathf.Sin(angle), 0f, Mathf.Cos(angle));
                    rotation = Quaternion.LookRotation(direction);
                    if (TryPlace(definition, anchor + direction * radius, rotation, out position)) return true;
                }
            }
            return false;
        }

        private static bool TryPlace(UnitDefinition definition, Vector3 desired, Quaternion rotation, out Vector3 position)
        {
            position = default;
            if (!Usable(definition)) return false;
            Vector3 offset = rotation * definition.spawnOffset;
            desired += new Vector3(offset.x, 0f, offset.z);
            if (!DryGround(desired, out Vector3 center)) return false;
            Vector3 half = new Vector3(definition.width * 0.5f + 1f, definition.height * 0.5f, definition.length * 0.5f + 1f);
            for (int i = 0; i < 4; i++)
            {
                Vector3 corner = center + rotation * new Vector3((i & 1) == 0 ? -half.x : half.x, 0f, (i & 2) == 0 ? -half.z : half.z);
                if (!DryGround(corner, out Vector3 hit) || Mathf.Abs(hit.y - center.y) > 1f) return false;
            }
            Vector3 volume = center + Vector3.up * (half.y + 0.15f);
            if (Physics.CheckBox(volume, half, rotation, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)) return false;
            position = center + Vector3.up * (offset.y + 0.2f);
            return Finite(position);
        }

        private static bool DryGround(Vector3 desired, out Vector3 point)
        {
            point = default;
            if (!Finite(desired) || GameAssets.i?.terrainMaterial == null ||
                !Physics.Raycast(desired + Vector3.up * 500f, Vector3.down, out RaycastHit hit, 2000f,
                    Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore) ||
                hit.collider == null || hit.collider.sharedMaterial != GameAssets.i.terrainMaterial ||
                hit.normal.y < 0.96f || hit.point.y <= Datum.LocalSeaY + 1f) return false;
            point = hit.point;
            return Finite(point);
        }

        private static bool Finite(Vector3 value) => Finite(value.x) && Finite(value.y) && Finite(value.z);
    }
}
