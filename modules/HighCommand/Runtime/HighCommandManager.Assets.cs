using System;
using System.Collections.Generic;
using BoscaliSummer.Core.Math;
using BoscaliSummer.Modules.HighCommand.Domain;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Core.Game;
using NuclearOption.Networking;
using UnityEngine;

namespace BoscaliSummer.Modules.HighCommand.Runtime
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
        private readonly Vector3[] intelGlobal = new Vector3[MaximumFactions * CommandTier.MaximumSlots];
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

        /// <summary>The player-facing name of a base; the scene object name when no display name was recorded.</summary>
        private static string Label(FactionCommand owner, string key) =>
            owner != null && key != null && owner.Labels.TryGetValue(key, out string text) ? text : key;

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

        private void CollectSites(FactionHQ hq, List<string> sites, Dictionary<string, string> labels)
        {
            sites.Clear();
            // A base with an enemy base next door is no place for a post: it dies to the neighbour's guns every respawn,
            // and the ammo-dump posts chain-collapse. Contested bases are used only when the faction has nothing else.
            for (int pass = 0; pass < 2 && sites.Count == 0; pass++)
            {
                int inspected = 0;
                foreach (Airbase airbase in FactionRegistry.airbaseLookup.Values)
                {
                    if (++inspected > 64 || sites.Count >= CommandTree.MaximumSites) break;
                    if (airbase == null || airbase.CurrentHQ != hq || airbase.AttachedAirbase ||
                        airbase.UnitDestroyed() || airbase.center == null) continue;
                    if (sites.Contains(airbase.name) || (pass == 0 && Contested(airbase))) continue;
                    sites.Add(airbase.name);
                    labels[airbase.name] = MfdAirbaseFormatter.Format(airbase);
                }
            }
            sites.Sort(StringComparer.Ordinal);
        }

        private const float ContestedRadius = 4000f;

        /// <summary>A hostile-held airbase stands within <see cref="ContestedRadius"/> of this one.</summary>
        private static bool Contested(Airbase airbase)
        {
            Vector3 at = airbase.center.position;
            int inspected = 0;
            foreach (Airbase other in FactionRegistry.airbaseLookup.Values)
            {
                if (++inspected > 64) break;
                if (other == null || other == airbase || other.center == null || other.UnitDestroyed() ||
                    other.CurrentHQ == null || other.CurrentHQ == airbase.CurrentHQ) continue;
                if ((other.center.position - at).sqrMagnitude < ContestedRadius * ContestedRadius) return true;
            }
            return false;
        }

        private Airbase FindBase(FactionCommand owner, string siteName)
        {
            Airbase fallback = null, contested = null;
            int inspected = 0;
            foreach (Airbase airbase in FactionRegistry.airbaseLookup.Values)
            {
                if (++inspected > 64) break;
                if (airbase == null || airbase.CurrentHQ != owner.Hq || airbase.AttachedAirbase ||
                    airbase.UnitDestroyed() || airbase.center == null) continue;
                if (!string.Equals(airbase.name, siteName, StringComparison.Ordinal))
                {
                    if (fallback == null)
                    {
                        if (!Contested(airbase)) fallback = airbase;
                        else if (contested == null) contested = airbase;
                    }
                    continue;
                }
                return airbase;
            }
            return fallback ?? contested;
        }

        // ---- Posts ------------------------------------------------------------------------

        // The pass over the posts a tick starts and the frames after it finish: (faction, slot) to look at next, -1 when none is in flight.
        private int spawnFaction = -1, spawnSlot;

        /// <summary>
        /// The missing posts, at most one spawn per call (a placement search and a vanilla spawn): at mission start every post
        /// is missing, and all of them in one tick was a multi-millisecond frame. Each slot is still tried once per pass.
        /// </summary>
        private void StepSpawnPass()
        {
            for (; spawnFaction < factions.Count; spawnFaction++, spawnSlot = 0)
            {
                FactionCommand owner = factions[spawnFaction];
                for (; spawnSlot < owner.Tree.Slots.Count; spawnSlot++)
                {
                    CommandSlot slot = owner.Tree.Slots[spawnSlot];
                    if (FindAsset(owner, slot, AssetKind.Post) != null) continue;
                    if (owner.RespawnScheduled[slot.Id])
                    {
                        if (MissionTime < owner.RespawnAt[slot.Id]) continue;
                        owner.RespawnScheduled[slot.Id] = false;
                    }
                    if (assets.Count >= MaximumAssets) continue;
                    spawnSlot++;   // the next call resumes after this slot
                    SpawnPost(owner, slot);
                    return;
                }
            }
            spawnFaction = -1;
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
                owner.AnchorsGlobal[slot.Id] = baseTarget.center.position.ToGlobalPosition().AsVector3();
                owner.HasAnchor[slot.Id] = true;
            }
            Vector3 stored = owner.AnchorsGlobal[slot.Id];
            Vector3 anchor = new GlobalPosition(stored.x, stored.y, stored.z).ToLocalPosition();

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
            Plugin.Logger?.LogInfo("[HighCommand] Post established: " + slot.Role + " at " + slot.SiteName + " (" + baseTarget.name + ").");
            return true;
        }

        private void Watch(FactionCommand owner, CommandSlot slot, Unit unit, AssetKind kind)
        {
            var watch = new AssetWatch { Owner = owner, Slot = slot, Unit = unit, Kind = kind };
            watch.InstanceId = unit.GetInstanceID();
            assets.Add(watch);
            assetLookup[watch.InstanceId] = watch;
            watch.Watch();
            // A rebuilt post stands somewhere nobody has seen: what was confirmed about the old one is void.
            if (kind == AssetKind.Post) intel.Forget(GlobalId(FactionIndex(owner.Hq), slot.Id));
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
                    Label(watch.Owner, watch.Slot.SiteName), now);
            }
        }

        private void OnAssetDisabled(AssetWatch watch, Unit unit)
        {
            if (!GameAccess.IsServer() || watch == null || unit == null || !unit.disabled) return;
            int instanceId = watch.InstanceId;
            if (!assetLookup.Remove(instanceId)) return;
            assets.Remove(watch);
            // The dealer is read before its record is dropped: the damage is recorded before the disable on the vanilla path.
            FactionHQ attacker = AttackerOf(instanceId);
            lastDamage.Remove(instanceId);
            watch.Unwatch();

            FactionCommand owner = watch.Owner;
            CommandSlot slot = watch.Slot;
            float now = MissionTime;
            int key = GlobalId(FactionIndex(owner.Hq), slot.Id);

            if (watch.Kind == AssetKind.ConvoyLead)
            {
                // The commander is in that vehicle: a real death.
                CreditKill(owner, slot, key, attacker, unit, "KILLED IN TRANSIT");
                AbortTransfer(FindConvoy(owner, slot));
                KillCommander(owner, slot, now, "KILLED IN TRANSIT", postDestroyed: false);
                return;
            }

            if (slot.Status == CommanderStatus.InTransit)
            {
                // The post fell while its commander was away: no death, no bounty.
                if (attacker != null && attacker != owner.Hq && HasParticipant(attacker))
                    Announce(attacker, StaffWords.AfieldLine(slot.Person.Rank, slot.Person.Name));
                Broadcast(owner, key, CommanderLogTone.Alert,
                    slot.Person.Name + " WAS AFIELD — SURVIVED THE STRIKE ON THEIR POST", now);
                owner.RespawnScheduled[slot.Id] = true;
                owner.RespawnAt[slot.Id] = now + settings.PostRespawnSeconds.Value;
            }
            else
            {
                string cause = "KILLED AT " + Label(owner, slot.SiteName);
                CreditKill(owner, slot, key, attacker, unit, cause);      // before KillCommander replaces slot.Person
                KillCommander(owner, slot, now, cause, postDestroyed: true);
            }
            intel.Forget(key);                // the post fell: nobody knows where the next one stands
        }

        private FactionHQ AttackerOf(int instanceId) =>
            lastDamage.TryGetValue(instanceId, out PersistentID dealer) && UnitRegistry.TryGetPersistentUnit(dealer, out PersistentUnit source)
                ? source.GetHQ() : null;

        private void KillCommander(FactionCommand owner, CommandSlot slot, float now, string cause, bool postDestroyed)
        {
            CommandPerson dead = slot.Person;
            float disruption = settings.DisruptionSeconds.Value + CommandTraits.DisruptionBonus(dead?.Traits ?? CommandTrait.None);
            int nextSeed = unchecked(owner.SeedSerial + 31 * (++owner.RespawnSerial));
            string deadRank = dead.Rank;
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
                deadRank + " " + dead.Name + " " + cause + " · " +
                slot.Person.Rank + " " + slot.Person.Name + " ASSUMES POST", now);
        }

        /// <summary>
        /// Pays the bounty for a real death: to a human faction that has not yet taken this office, once per office per killer
        /// faction per mission. An AI killer, the post's own side or a flipped post pays nothing and spends no ledger entry.
        /// </summary>
        private void CreditKill(FactionCommand owner, CommandSlot slot, int key, FactionHQ attacker, Unit unit, string cause)
        {
            if (!settings.EconomyEnabled.Value || slot.Person == null || attacker == null || attacker == owner.Hq) return;
            if (!HasParticipant(attacker)) return;                         // an AI faction is paid nothing and consumes no ledger entry
            if (unit != null && unit.NetworkHQ != owner.Hq) return;        // a flipped (captured-base) post pays nobody
            if (!bounties.TryClaim(attacker.GetInstanceID(), key))
            { Announce(attacker, StaffWords.ClaimedLine(slot.Person.Rank, slot.Person.Name)); return; }
            int funds = BountyFor(slot);
            if (funds <= 0) return;
            attacker.AddFunds(funds);                                      // no AddScore: score moves escalation
            BountyPaidTotal += funds;
            FactionCommand killer = FindFaction(attacker);
            if (killer != null)
                Broadcast(killer, key, CommanderLogTone.Loss,
                    "KILL PAID +" + funds + " · " + slot.Person.Rank + " " + slot.Person.Name + " ELIMINATED", MissionTime);
            Announce(attacker, StaffWords.KillLine(funds, slot.Person.Rank, slot.Person.Name, cause));
        }

        /// <summary>One native HQ radio line to a faction that has a human pilot (the pattern of the orbital-strike warning).</summary>
        private void Announce(FactionHQ hq, string text)
        {
            if (!settings.EconomyEnabled.Value || hq == null || !HasParticipant(hq)) return;
            MessageManager messages = NetworkSceneSingleton<MessageManager>.i;
            if (messages == null) return;
            try { messages.RpcHQMessage(hq, BoscaliSummer.Core.Net.NetText.Clip(text, 96)); }
            catch (Exception e) { Plugin.Logger?.LogWarning("[HighCommand] HQ message not delivered: " + e.Message); }
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
                        (Label(command, FindConvoy(command, pick)?.DestinationName) ?? "A FORWARD BASE"), now);
            }
        }

        private CommandSlot PickTransferSlot(FactionCommand command)
        {
            CommandSlot best = null;
            int seen = 0;
            for (int i = 0; i < command.Tree.Slots.Count; i++)
            {
                CommandSlot slot = command.Tree.Slots[i];
                if (slot.Status != CommanderStatus.Active) continue;
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
                    convoy.Slot.Person.Name + " RETURNED TO " + Label(convoy.Owner, convoy.Slot.SiteName), MissionTime);
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

        // The sensing sweep a tick starts: the posts' positions (captured at the start), the tick's time, and how far through the unit list it is (-1: none in flight).
        private int intelCount, intelCursor = -1;
        private float intelNow;
        private const int IntelUnitsPerFrame = 512;

        private void BeginIntel(float now)
        {
            int count = 0;
            for (int o = 0; o < factions.Count; o++)
            {
                for (int s = 0; s < factions[o].Tree.Slots.Count; s++)
                {
                    if (count >= intelPositions.Length) break;
                    CommandSlot slot = factions[o].Tree.Slots[s];
                    // Only the post building is sensed: a convoy is never recorded, so a confirmed position can never point at a road.
                    AssetWatch asset = FindAsset(factions[o], slot, AssetKind.Post);
                    if (asset?.Unit == null) continue;
                    intelOwners[count] = (byte)o;
                    intelKeys[count] = o * CommandTier.MaximumSlots + slot.Id;
                    intelPositions[count] = asset.Unit.transform.position;
                    intelGlobal[count] = asset.Unit.transform.position.ToGlobalPosition().AsVector3();
                    intelRadii[count] = IntelPostRadius * CommandTraits.IntelRadiusMultiplier(slot.Person?.Traits ?? CommandTrait.None);
                    count++;
                }
            }

            intelCount = count;
            intelNow = now;
            intelCursor = count > 0 ? 0 : -1;
        }

        /// <summary>The next slice of the sweep: every unit (the first 4096) against every post's sensing radius, a slice a frame so the whole sweep lands inside its second.</summary>
        private void StepIntel()
        {
            List<Unit> units = UnitRegistry.allUnits;
            int total = units == null ? 0 : Math.Min(units.Count, 4096);
            int end = Math.Min(total, intelCursor + IntelUnitsPerFrame);
            int count = intelCount;
            float now = intelNow;
            for (int i = intelCursor; i < end; i++)
            {
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
                    Vector3 where = intelGlobal[c];
                    if (intel.Confirm(observer, key, now, where.x, where.y, where.z)) ConfirmContact(observer, intelOwners[c], key, now);
                }
            }
            intelCursor = end >= total ? -1 : end;
        }

        /// <summary>One log beat per observer and post, the first time a patrol reports it.</summary>
        private void ConfirmContact(int observer, int ownerIndex, int globalId, float now)
        {
            if (observer < 0 || observer >= factions.Count ||
                ownerIndex < 0 || ownerIndex >= factions.Count) return;
            CommandSlot slot = factions[ownerIndex].Tree.Find(LocalId(globalId));
            if (slot?.Person == null) return;
            Broadcast(factions[observer], globalId, CommanderLogTone.Contact,
                "CONTACT · " + slot.Person.Rank + " " + slot.Person.Name + " AT " + Label(factions[ownerIndex], slot.SiteName), now);
        }

        private int FactionIndex(FactionHQ hq)
        {
            for (int i = 0; i < factions.Count; i++)
                if (factions[i].Hq == hq) return i;
            return -1;
        }

        // ---- Signals ----------------------------------------------------------------------

        /// <summary>The one place a staff event is stated: remembered in the faction's log with its subject and its tone.</summary>
        private void Broadcast(FactionCommand command, int targetId, CommanderLogTone tone, string text, float now) =>
            command.Log.Append(targetId, tone, text, now);

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
                    if (!GroundPlacement.Usable(candidate) || candidate.unitPrefab.GetComponent<Building>() == null) continue;
                    if (postDefinition == null || candidate.value < postDefinition.value) postDefinition = candidate;
                }
            }
            Plugin.Logger?.LogInfo("[HighCommand] Command post structure: " +
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
                if (candidate == null || !GroundPlacement.Usable(candidate) ||
                    candidate.unitPrefab.GetComponent<GroundVehicle>()?.UnitCommand == null) continue;
                if (convoyDefinition == null || candidate.value < convoyDefinition.value) convoyDefinition = candidate;
            }
            return convoyDefinition;
        }

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
            if (!GroundPlacement.Usable(definition)) return false;
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
                    if (GroundPlacement.TryPlace(definition, anchor + direction * radius, rotation, out position)) return true;
                }
            }
            return false;
        }
    }
}
