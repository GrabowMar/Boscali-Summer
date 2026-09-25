using System;
using System.Collections.Generic;
using BepInEx.Logging;
using BoscaliSummer.Features.Squad.Configuration;
using BoscaliSummer.Features.Squad.Domain;
using BoscaliSummer.Features.Squad.Networking;
using BoscaliSummer.Framework.Contracts;
using BoscaliSummer.Framework.Lifecycle;
using BoscaliSummer.Runtime;
using NuclearOption.Networking;
using UnityEngine;

namespace BoscaliSummer.Features.Squad.Runtime
{
    internal sealed class SquadManager : MonoBehaviour, ISceneService, ISquadView
    {
        private sealed class Career
        {
            public readonly AceCareer Rules;
            public Career(int generation = 1, int scoreOrigin = 0) { Rules = new AceCareer(generation, scoreOrigin); }
            public Player Player;
            public FactionHQ Hq, ProvokedHq;
            public Pilot Seat;
            public int SeatInstanceId;
            public PersistentID SeatId;
            public bool SeatDeathObserved;
            public string Name, Callsign, Background;
            public uint Event;
            public string Notice = "Damage hostile forces to attract an enemy ace.", Speaker = "SQUAD", Chatter = "";
            public float NextSpawnAttempt;
        }

        private sealed class Hunt
        {
            public Career Owner;
            public FactionHQ EnemyHq;
            public Aircraft Target;
            public Aircraft[] Aircraft;
            public PersistentID AceId;
            public string Name, Callsign, Symbol, Wing;
            public int Id, Seed, Tier, Returns, Generation, Alive, SpawnedCount;
            public float Began, Ended;
            public float NextChatter;
            public int ChatterCount;
            public bool PlayerHitAce, KillReported, ReturnCandidate, Returned, Released, EjectionAnnounced;
            public bool DebugSpawn;
            public HuntOutcome Outcome;
        }

        private sealed class RivalRecord
        {
            public int Seed;
            public string Name, Callsign, Symbol, Wing;
            public int Tier, Returns;
            public PersistentID AceId;
            public FactionHQ EnemyHq;
        }

        private static readonly List<RivalRecord> survivingRivals = new List<RivalRecord>(16);
        private readonly Dictionary<ulong, Career> careers = new Dictionary<ulong, Career>();
        private readonly List<Hunt> hunts = new List<Hunt>(AceCareer.MaximumHistory);
        private readonly HashSet<Player> connected = new HashSet<Player>();
        private SquadSettings settings;
        private SquadNet network;
        private ManualLogSource logger;
        private object missionIdentity;
        private float nextTick, lastTime;
        private int sequence;
        private uint lastEvent;
        private ulong localIdentity;
        private int localBonus, localOrigin;
        private float pendingChatterRelease;
        private string pendingChatterSpeaker, pendingChatterStatus, pendingChatterMessage;
        private int lastChatterHuntId;
        private EnemyWingView[] enemies = Array.Empty<EnemyWingView>();
        private static readonly string[] Symbols = { "<>", "[+]", "/\\", "[X]", "><", "||" };
        private static readonly string[] Wings = { "LANCE", "CROWN", "TALON", "WRAITH", "VIPER", "REVENANT" };

        public PilotView Pilot { get; private set; }
        public bool HuntActive { get; private set; }
        public string Status { get; private set; } = "Waiting for mission and Wing Command.";
        public string LastChatter { get; private set; } = string.Empty;
        public int EnemyWingCount => enemies.Length;
        public int ActiveEnemyWingIndex { get; private set; } = -1;
        public int ActiveHuntId { get; private set; }
        public EnemyWingView GetEnemyWing(int index) => index >= 0 && index < enemies.Length ? enemies[index] : default;
        public int GetBonusPoints(ulong id) => GameAccess.IsServer()
            ? careers.TryGetValue(id, out Career c) ? c.Rules.BonusPoints : 0 : id == localIdentity ? localBonus : 0;
        public int GetPilotGeneration(ulong id) => GameAccess.IsServer()
            ? careers.TryGetValue(id, out Career c) ? c.Rules.Generation : 1 : id == localIdentity ? Math.Max(1, Pilot.Generation) : 1;
        public int GetScoreOrigin(ulong id) => GameAccess.IsServer()
            ? careers.TryGetValue(id, out Career c) ? c.Rules.ScoreOrigin : 0 : id == localIdentity ? localOrigin : 0;

        private BoscaliSummer.Framework.Features.ServiceRegistry services;
        internal void Configure(SquadSettings config, SquadNet transport, ManualLogSource log,
            BoscaliSummer.Framework.Features.ServiceRegistry registry)
        {
            services = registry;
            settings = config; network = transport; logger = log; SquadRuntime.Active = this;
            settings.SpawnDebugWing = DebugSpawnWing; settings.ClearDebugWings = DebugClearWings;
        }

        public void ResetForScene()
        {
            for (int i = 0; i < hunts.Count; i++) Release(hunts[i], true);
            hunts.Clear(); careers.Clear(); connected.Clear(); missionIdentity = null;
            nextTick = lastTime = 0; sequence = 0; lastEvent = 0;
            localIdentity = 0; localBonus = localOrigin = 0;
            pendingChatterRelease = 0f;
            pendingChatterSpeaker = pendingChatterStatus = pendingChatterMessage = null;
            lastChatterHuntId = 0;
            ClearLocal("Waiting for a running mission."); network?.ResetScene();
        }

        private void OnDestroy()
        {
            ResetForScene();
            if (settings != null) { settings.SpawnDebugWing = null; settings.ClearDebugWings = null; }
            if (SquadRuntime.Active == this) SquadRuntime.Active = null;
        }

        internal string DebugSpawnWing(int tier)
        {
            if (tier < 1 || tier > 5) return "Select a tier from 1 to 5.";
            if (!GameAccess.IsServer()) return "Host only: remote clients cannot spawn hunting wings.";
            MissionManager mission = NetworkSceneSingleton<MissionManager>.i;
            if (!MissionManager.IsRunning || mission == null || !ReferenceEquals(missionIdentity, MissionManager.CurrentMission))
                return "Wait for a running mission.";
            if (!settings.EnemyAceHunts.Value) return "Enable Squad / EnemyAceHunts first.";
            if (!WingLink.SquadAvailable) return WingLink.SquadUnavailableReason;
            if (!GameManager.GetLocalPlayer<Player>(out Player player) || player == null || !Flyable(player.Aircraft))
                return "Enter an aircraft with a living pilot first.";
            FactionHQ enemy = null;
            int inspected = 0;
            foreach (FactionHQ hq in FactionRegistry.GetAllHQs())
            {
                if (++inspected > 8) break;
                if (Hostile(hq, player.HQ)) { enemy = hq; break; }
            }
            if (enemy == null) return "This mission has no hostile faction.";
            Career career = GetCareer(player);
            if (career == null) return "Squad player capacity reached.";
            int otherOwned = 0;
            for (int i = 0; i < hunts.Count; i++)
            {
                Hunt hunt = hunts[i];
                if (hunt.Owner == career && !hunt.DebugSpawn && hunt.Outcome == HuntOutcome.Hunting)
                    return "Finish the current normal hunt first.";
                if (hunt.Aircraft != null && !(hunt.Owner == career && hunt.DebugSpawn)) otherOwned++;
            }
            if (otherOwned >= AceCareer.MaximumWings) return "Four wings are already deployed. Wait for capacity.";
            ClearDebugWings(career, mission.MissionTime);
            GatherPlayers();
            TickCareer(career, mission.MissionTime, allowSpawn: false); // Synchronize the seat without spawning a natural hunt.
            if (career.Rules.Hunting || career.Rules.ReplacementPending) return "Wait for the current pilot/hunt transition.";
            career.ProvokedHq = enemy;
            bool spawned = Spawn(career, player.Aircraft, Time.unscaledTime, tier);
            if (spawned) Apply(Snapshot(player), PlayerIdentity.Of(player));
            return spawned ? "Spawned tier " + tier + " adversary wing." : "Wing Command rejected adversary spawn.";
        }

        private string DebugClearWings()
        {
            if (!GameAccess.IsServer()) return "Host only.";
            if (!GameManager.GetLocalPlayer<Player>(out Player player) || player == null)
                return "No local player found.";
            if (!careers.TryGetValue(PlayerIdentity.Of(player), out Career career)) return "No local pilot career.";
            int count = ClearDebugWings(career, NetworkSceneSingleton<MissionManager>.i?.MissionTime ?? 0);
            if (count > 0)
            {
                Apply(Snapshot(player), PlayerIdentity.Of(player));
                logger.LogInfo("[Squad] Cleared " + count + " debug adversary wings for local player.");
            }
            return count > 0 ? "Cleared " + count + " debug wings." : "No active debug wings owned by this player.";
        }

        private int ClearDebugWings(Career career, float now)
        {
            int count = 0;
            for (int i = hunts.Count - 1; i >= 0; i--)
            {
                Hunt hunt = hunts[i];
                if (!hunt.DebugSpawn || hunt.Owner != career) continue;
                if (hunt.Outcome == HuntOutcome.Hunting) End(hunt, HuntOutcome.Expired, now, false);
                Release(hunt, true); hunts.RemoveAt(i); count++;
            }
            return count;
        }

        private void Update()
        {
            if (pendingChatterRelease > 0f && Time.unscaledTime >= pendingChatterRelease)
            {
                pendingChatterRelease = 0f;
                if (!string.IsNullOrEmpty(pendingChatterMessage) && !Application.isBatchMode)
                    WingLink.EnemyChatter(pendingChatterSpeaker, pendingChatterStatus, pendingChatterMessage);
            }
            if (settings == null || Time.unscaledTime < nextTick) return;
            nextTick = Time.unscaledTime + 1f;
            var current = MissionManager.CurrentMission;
            MissionManager mission = NetworkSceneSingleton<MissionManager>.i;
            if (!ReferenceEquals(current, missionIdentity)) { ResetForScene(); missionIdentity = current; }
            if (current == null || mission == null || !MissionManager.IsRunning)
            {
                if (careers.Count > 0 || hunts.Count > 0) ResetForScene();
                ClearLocal("Waiting for a running mission."); return;
            }
            float now = mission.MissionTime;
            if (!AceCareer.Finite(now)) return;
            if (now < lastTime) { ResetForScene(); missionIdentity = current; }
            lastTime = now;
            if (GameAccess.IsServer())
            {
                GatherPlayers();
                for (int i = 0; i < hunts.Count; i++) TickHunt(hunts[i], now);
                foreach (Career career in careers.Values) TickCareer(career, now);
            }
            // Encounter notices/music must arrive even with every MFD closed.
            network.Request();
        }

        private void GatherPlayers()
        {
            connected.Clear(); int factionCount = 0;
            foreach (FactionHQ hq in FactionRegistry.GetAllHQs())
            {
                if (++factionCount > 8) break;
                if (hq == null) continue;
                for (int i = 0; i < Math.Min(64, hq.factionPlayers.Count) && connected.Count < 64; i++)
                {
                    Player player = hq.factionPlayers[i].Player;
                    if (player == null || player.HQ != hq) continue;
                    connected.Add(player); GetCareer(player);
                }
            }
        }

        private Career GetCareer(Player player)
        {
            if (player == null) return null;
            ulong id = PlayerIdentity.Of(player);
            int generation = 1, origin = 0;
            if (careers.TryGetValue(id, out Career career))
            {
                if (player.SteamID == 0 && !ReferenceEquals(career.Player, player))
                {
                    // Non-Steam player indices can be reused by unrelated connections.
                    for (int i = 0; i < hunts.Count; i++)
                        if (hunts[i].Owner == career && hunts[i].Outcome == HuntOutcome.Hunting)
                            End(hunts[i], HuntOutcome.TargetLost, lastTime, false);
                    careers.Remove(id);
                    generation = career.Rules.Generation + 1; origin = Mathf.RoundToInt(player.PlayerScore);
                }
                else { career.Player = player; return career; }
            }
            if (careers.Count >= AceCareer.MaximumPlayers) return null;
            career = new Career(generation, origin) { Player = player, Hq = player.HQ };
            GeneratePilot(career); careers.Add(id, career); return career;
        }

        private void GeneratePilot(Career career)
        {
            ulong id = PlayerIdentity.Of(career.Player);
            int seed = unchecked((int)id ^ (int)(id >> 32) ^ career.Rules.Generation * 7919);
            if (!WingLink.TryCreatePilot(seed, out career.Name, out career.Callsign, out career.Background, out _))
            { career.Name = "Pilot generator unavailable"; career.Callsign = "--"; career.Background = WingLink.SquadUnavailableReason; }
        }

        private void TickCareer(Career career, float now, bool allowSpawn = true)
        {
            Player player = career.Player;
            if (player == null || !connected.Contains(player)) return;
            Aircraft aircraft = player.Aircraft;
            Pilot seat = Primary(aircraft);
            if (career.Seat != null && career.Seat.dead) RecordPilotDeath(player, career.Seat);
            if (career.SeatId.IsValid && !career.SeatDeathObserved && WingLink.SurvivorStatus(career.SeatId) == 3)
            {
                career.SeatDeathObserved = true;
                career.Rules.RecordDeath(career.SeatInstanceId, settings.PilotLives.Value == PilotLifeMode.OneLife);
            }
            if (seat != null && seat != career.Seat)
            {
                if (!seat.dead && career.Rules.Replace(Mathf.RoundToInt(player.PlayerScore)))
                { GeneratePilot(career); Notice(career, "New pilot reporting for duty. Previous pilot's career retired.", "SQUAD", ""); }
                career.Seat = seat;
                career.SeatId = aircraft.persistentID; career.SeatInstanceId = seat.GetInstanceID();
                career.SeatDeathObserved = career.Rules.HasObservedDeath(career.SeatInstanceId);
                WingLink.SurvivorStatus(career.SeatId); // Enrol before a native ejection event.
            }
            if (seat != null && seat.dead) RecordPilotDeath(player, seat);
            if (career.Hq != player.HQ) { career.Hq = player.HQ; career.ProvokedHq = null; }
            if (!allowSpawn || !settings.EnemyAceHunts.Value || !WingLink.SquadAvailable || career.Rules.Hunting ||
                !Flyable(aircraft) || !Hostile(career.ProvokedHq, player.HQ) || now < career.NextSpawnAttempt ||
                career.Rules.Threat < career.Rules.Threshold(settings.DamageThreshold.Value)) return;
            int owned = 0;
            for (int i = 0; i < hunts.Count; i++) if (hunts[i].Aircraft != null) owned++;
            if (owned >= AceCareer.MaximumWings) return;
            career.NextSpawnAttempt = now + 15f;
            Spawn(career, aircraft, now);
        }

        internal void RecordDamage(Unit victim, PersistentID dealer, float amount)
        {
            if (!GameAccess.IsServer() || !MissionManager.IsRunning || victim == null || victim.disabled ||
                victim is Missile || victim is Scenery || !AceCareer.Finite(amount) || amount <= 0f) return;

            Hunt aceHunt = null;
            for (int i = 0; i < hunts.Count; i++)
            {
                Hunt h = hunts[i];
                if (h.Aircraft != null && h.Aircraft.Length > 0 && h.Aircraft[0] == victim && h.Outcome == HuntOutcome.Hunting)
                {
                    aceHunt = h;
                    break;
                }
            }

            if (!UnitRegistry.TryGetPersistentUnit(dealer, out PersistentUnit source)) return;
            Player player = source.player;

            if (aceHunt != null)
            {
                if (player != null && aceHunt.Owner != null && player == aceHunt.Owner.Player)
                {
                    aceHunt.PlayerHitAce = true;
                }
                else if (aceHunt.Owner?.Player != null && source.GetHQ() == aceHunt.Owner.Player.HQ)
                {
                    // Mitigate friendly AI damage on the ace by 75% to preserve the duel for the player
                    MitigateFriendlyDamage(victim, amount * 0.75f);
                }
            }

            if (player == null || !connected.Contains(player) || !Hostile(victim.NetworkHQ, player.HQ) || source.GetHQ() != player.HQ) return;
            Career career = GetCareer(player);
            if (career == null) return;
            for (int i = 0; i < hunts.Count; i++)
            {
                Hunt hunt = hunts[i];
                if (hunt.Owner == career && hunt.Generation == career.Rules.Generation && hunt.Aircraft != null &&
                    hunt.Aircraft.Length > 0 && hunt.Aircraft[0] == victim) hunt.PlayerHitAce = true;
            }
            if (!settings.EnemyAceHunts.Value || !Flyable(player.Aircraft)) return;
            float now = NetworkSceneSingleton<MissionManager>.i?.MissionTime ?? 0;
            if (career.Rules.Damage(amount, now, career.Rules.Threshold(settings.DamageThreshold.Value)))
                career.ProvokedHq = victim.NetworkHQ;
        }

        private static void MitigateFriendlyDamage(Unit victim, float amountToRestore)
        {
            if (victim == null || !(victim is Aircraft aircraft) || aircraft.partLookup == null) return;
            for (int i = 0; i < aircraft.partLookup.Count; i++)
            {
                UnitPart part = aircraft.partLookup[i];
                if (part != null && part.hitPoints > 0f)
                {
                    part.hitPoints += amountToRestore;
                    break;
                }
            }
        }

        internal void RecordKill(Unit victim)
        {
            if (victim == null || !victim.disabled) return;
            for (int i = 0; i < hunts.Count; i++)
            {
                Hunt hunt = hunts[i];
                if (hunt.Aircraft != null && hunt.Aircraft.Length > 0 && hunt.Aircraft[0] == victim)
                    hunt.KillReported = true;
            }
        }

        internal void RecordPilotDeath(Player player, Pilot seat)
        {
            if (!GameAccess.IsServer() || seat == null || !seat.dead) return;
            Career career = GetCareer(player);
            if (career == null || ReferenceEquals(career.Seat, seat) && career.SeatDeathObserved ||
                !career.Rules.RecordDeath(seat.GetInstanceID(), settings.PilotLives.Value == PilotLifeMode.OneLife)) return;
            if (ReferenceEquals(career.Seat, seat)) career.SeatDeathObserved = true;
        }

        private static bool IsTooCloseToFriendlies(FactionHQ playerHq, float x, float z, float minDistance)
        {
            if (playerHq == null || FactionRegistry.airbaseLookup == null) return false;
            float minSqr = minDistance * minDistance;
            foreach (Airbase ab in FactionRegistry.airbaseLookup.Values)
            {
                if (ab == null || ab.CurrentHQ != playerHq || ab.UnitDestroyed()) continue;
                GlobalPosition pos = ab.transform.position.ToGlobalPosition();
                float dx = x - pos.x, dz = z - pos.z;
                if (dx * dx + dz * dz < minSqr) return true;
            }
            return false;
        }

        private static bool TryCalculateEnemyIngress(Aircraft target, FactionHQ enemyHq, out float ingressX, out float ingressZ)
        {
            ingressX = ingressZ = 0f;
            var map = NetworkSceneSingleton<LevelInfo>.i?.LoadedMapSettings;
            if (map == null || target == null || enemyHq == null) return false;
            GlobalPosition player = target.GlobalPosition();
            Vector2 enemyCenter = Vector2.zero;
            int count = 0;
            if (FactionRegistry.airbaseLookup != null)
            {
                foreach (Airbase ab in FactionRegistry.airbaseLookup.Values)
                {
                    if (ab == null || ab.CurrentHQ != enemyHq || ab.UnitDestroyed()) continue;
                    GlobalPosition pos = ab.transform.position.ToGlobalPosition();
                    enemyCenter += new Vector2(pos.x, pos.z);
                    count++;
                }
            }
            if (count > 0) enemyCenter /= count;
            else
            {
                GlobalPosition hqPos = enemyHq.transform.position.ToGlobalPosition();
                enemyCenter = new Vector2(hqPos.x, hqPos.z);
            }
            Vector2 dir = enemyCenter - new Vector2(player.x, player.z);
            if (dir.sqrMagnitude < 100f) dir = Vector2.up;
            else dir.Normalize();

            float halfX = map.MapSize.x * 0.5f - 1200f;
            float halfZ = map.MapSize.y * 0.5f - 1200f;
            ingressX = Mathf.Clamp(player.x + dir.x * 22000f, -halfX, halfX);
            ingressZ = Mathf.Clamp(player.z + dir.y * 22000f, -halfZ, halfZ);
            return true;
        }

        private bool Spawn(Career career, Aircraft target, float now, int debugTier = 0)
        {
            RivalRecord chosenRival = null;
            List<RivalRecord> candidates = new List<RivalRecord>();
            for (int i = 0; debugTier == 0 && i < survivingRivals.Count; i++)
            {
                RivalRecord r = survivingRivals[i];
                if (r.EnemyHq == career.ProvokedHq && Survived(r.AceId))
                    candidates.Add(r);
            }
            // 30% chance to reuse a previously met surviving ace, or randomly generate
            if (candidates.Count > 0 && UnityEngine.Random.value < 0.30f)
            {
                chosenRival = candidates[UnityEngine.Random.Range(0, candidates.Count)];
            }

            int id = ++sequence;
            int seed = chosenRival != null ? chosenRival.Seed : UnityEngine.Random.Range(1, int.MaxValue);
            int returns = chosenRival != null ? chosenRival.Returns + 1 : 0;
            int tier = AceCareer.SpawnTier(career.Rules.Tier, chosenRival?.Tier ?? 0, debugTier);
            if (!WingLink.TryCreatePilot(seed, out string name, out string callsign, out _, out _)) return false;
            if (chosenRival != null)
            {
                name = chosenRival.Name;
                callsign = chosenRival.Callsign;
            }

            GlobalPosition playerPosition = target.GlobalPosition();
            float ingressX = 0f, ingressZ = 0f;
            bool validIngress = false;
            if (services.TryGet<ITerritoryIngress>(out var territory) &&
                territory.TryNearestEdge(career.ProvokedHq.GetInstanceID(), playerPosition.x, playerPosition.z,
                    out ingressX, out ingressZ))
            {
                validIngress = !IsTooCloseToFriendlies(career.Player?.HQ, ingressX, ingressZ, 15000f);
            }
            if (!validIngress && !TryCalculateEnemyIngress(target, career.ProvokedHq, out ingressX, out ingressZ))
                return false;

            Aircraft[] aircraft = WingLink.SpawnAceWing(target, career.ProvokedHq, seed, tier, AceCareer.WingSize(tier), callsign, ingressX, ingressZ);
            if (aircraft == null || aircraft.Length != AceCareer.WingSize(tier))
            { if (aircraft != null) WingLink.ReleaseAceWing(aircraft, true); return false; }

            if (chosenRival != null)
            {
                if (WingLink.SurvivorStatus(chosenRival.AceId) == 1)
                    WingLink.RecoverSurvivor(chosenRival.AceId);
                chosenRival.Returns = returns;
                chosenRival.Tier = tier;
                chosenRival.AceId = aircraft[0].persistentID;
            }

            while (hunts.Count >= AceCareer.MaximumHistory)
            {
                int oldest = hunts.FindIndex(h => h.Aircraft == null);
                if (oldest < 0) { WingLink.ReleaseAceWing(aircraft, true); return false; }
                hunts.RemoveAt(oldest);
            }
            int emblem = (seed & int.MaxValue) % Wings.Length;
            var hunt = new Hunt { Id = id, Owner = career, EnemyHq = career.ProvokedHq, Target = target,
                Aircraft = aircraft, AceId = aircraft[0].persistentID, Name = name, Callsign = callsign,
                Symbol = chosenRival?.Symbol ?? Symbols[emblem], Wing = chosenRival?.Wing ?? (Wings[emblem] + " " + (sequence % 100).ToString("00")),
                Seed = seed, Tier = tier, Returns = returns, Returned = chosenRival != null, Generation = career.Rules.Generation,
                Began = now, NextChatter = now + 35f, Alive = aircraft.Length, SpawnedCount = aircraft.Length, Outcome = HuntOutcome.Hunting,
                DebugSpawn = debugTier != 0 };
            hunts.Add(hunt); career.Rules.Begin();
            Notice(career, hunt.Symbol + " " + hunt.Wing + " / " + callsign + " — HUNT ACTIVE. Tier " + tier + ", " + aircraft.Length + " aircraft.",
                callsign, returns > 0 ? "Remember me? This time you are not getting away." : "We have your signature. Wing, concentrate on the marked aircraft.");
            logger.LogInfo("[Squad] Ace hunt spawned: " + hunt.Wing + ", tier " + tier + ", members " + aircraft.Length + ".");
            return true;
        }

        private void TickHunt(Hunt hunt, float now)
        {
            if (hunt.Aircraft == null) return;
            hunt.Alive = 0;
            for (int i = 0; i < hunt.Aircraft.Length; i++) if (Flyable(hunt.Aircraft[i])) hunt.Alive++;
            Aircraft leader = hunt.Aircraft[0];
            // A wing released to ordinary combat is still an ace encounter. Its later confirmed
            // defeat may pay, without ending a different hunt this pilot has since attracted.
            if (hunt.KillReported && hunt.Outcome != HuntOutcome.Defeated)
            {
                bool credit = hunt.PlayerHitAce && hunt.Generation == hunt.Owner.Rules.Generation;
                int previousBonus = hunt.Owner.Rules.BonusPoints;
                if (hunt.Outcome == HuntOutcome.Hunting) End(hunt, HuntOutcome.Defeated, now, credit);
                else
                {
                    hunt.Outcome = HuntOutcome.Defeated; hunt.Ended = now;
                    if (credit) hunt.Owner.Rules.CreditVictory();
                }
                hunt.ReturnCandidate = !hunt.DebugSpawn && hunt.Returns < 3;
                Notice(hunt.Owner, hunt.Symbol + " " + hunt.Wing + " ace defeated." +
                    (hunt.Owner.Rules.BonusPoints > previousBonus ? " +1 bonus perk point." : credit ? " Bonus budget complete." : " No player damage credit."),
                    hunt.Callsign, "Wing, break off. You have command.");
            }
            if (hunt.Outcome == HuntOutcome.Hunting)
            {
                if (!settings.EnemyAceHunts.Value || !Flyable(hunt.Target) || hunt.Owner.Player == null ||
                    !connected.Contains(hunt.Owner.Player) || hunt.Owner.Player.Aircraft != hunt.Target ||
                    !Hostile(hunt.EnemyHq, hunt.Owner.Player.HQ))
                {
                    End(hunt, HuntOutcome.TargetLost, now, false);
                    Notice(hunt.Owner, hunt.Wing + " hunt ended. Survivors resume normal operations.", hunt.Callsign,
                        "Marked aircraft is gone. Resume the mission.");
                }
                else if (!Flyable(leader) || now - hunt.Began >= AceCareer.LifetimeSeconds)
                {
                    End(hunt, HuntOutcome.Expired, now, false);
                    Notice(hunt.Owner, hunt.Wing + " disengaged. No ace kill confirmed.", hunt.Callsign, "Disengage. Return to normal tasking.");
                }
                else
                {
                    WingLink.SetAceWingTarget(hunt.Aircraft, hunt.Target);
                    if (hunt.ChatterCount < 2 && now >= hunt.NextChatter)
                    {
                        hunt.NextChatter = now + 60f; hunt.ChatterCount++;
                        Notice(hunt.Owner, hunt.Symbol + " " + hunt.Wing + " — HUNT ACTIVE. " + hunt.Alive + " aircraft remaining.",
                            hunt.Callsign, hunt.ChatterCount == 1 ? "Keep pressure on the marked aircraft. Make them turn." :
                            hunt.Alive < hunt.SpawnedCount ? "We lost a wingman. Stay focused on the target." : "You cannot run forever. Wing, close the distance.");
                    }
                }
            }
            if (hunt.ReturnCandidate && Survived(hunt.AceId))
            {
                if (!hunt.EjectionAnnounced)
                {
                    hunt.EjectionAnnounced = true;
                    Notice(hunt.Owner, hunt.Symbol + " " + hunt.Wing + " ace ejected. MIA — may return stronger.",
                        hunt.Callsign, "Punching out. We are not finished.");
                }
                int rIdx = survivingRivals.FindIndex(r => r.Seed == hunt.Seed);
                if (rIdx >= 0)
                {
                    survivingRivals[rIdx].Returns = hunt.Returns;
                    survivingRivals[rIdx].Tier = hunt.Tier;
                    survivingRivals[rIdx].AceId = hunt.AceId;
                }
                else if (survivingRivals.Count < 16)
                {
                    survivingRivals.Add(new RivalRecord
                    {
                        Seed = hunt.Seed, Name = hunt.Name, Callsign = hunt.Callsign,
                        Symbol = hunt.Symbol, Wing = hunt.Wing, Tier = hunt.Tier,
                        Returns = hunt.Returns, AceId = hunt.AceId, EnemyHq = hunt.EnemyHq
                    });
                }
            }
            else if (hunt.KillReported && !Survived(hunt.AceId))
            {
                survivingRivals.RemoveAll(r => r.Seed == hunt.Seed || r.AceId == hunt.AceId);
            }
            // Native ejection is asynchronous; leave a downed airframe long enough to spawn its survivor.
            if ((hunt.Alive == 0 && hunt.Outcome != HuntOutcome.Hunting && now - hunt.Ended >= 30f) ||
                now - hunt.Began >= AceCareer.LifetimeSeconds)
            {
                Release(hunt, true); hunt.Aircraft = null;
            }
        }

        private void End(Hunt hunt, HuntOutcome outcome, float now, bool credit)
        {
            hunt.Outcome = outcome; hunt.Ended = now;
            hunt.Owner.Rules.Finish(now, settings.HuntCooldown.Value, credit);
            Release(hunt, false);
        }

        private static void Release(Hunt hunt, bool destroy)
        {
            if (hunt.Aircraft == null || (!destroy && hunt.Released)) return;
            WingLink.ReleaseAceWing(hunt.Aircraft, destroy); hunt.Released = true;
        }

        private void Notice(Career career, string text, string speaker, string chatter)
        {
            career.Event++; career.Notice = text; career.Speaker = speaker; career.Chatter = chatter;
            if (!string.IsNullOrEmpty(chatter))
                LastChatter = string.IsNullOrEmpty(speaker) ? chatter : speaker + ": " + chatter;
        }

        internal SquadSnapshot Snapshot(Player player)
        {
            var result = new SquadSnapshot { Protocol = SquadNet.ProtocolVersion, ActiveIndex = -1,
                Wings = Array.Empty<EnemyWingView>(), Status = Status };
            Career career = GetCareer(player);
            if (career == null) return result;
            Pilot seat = Primary(player.Aircraft);
            result.Pilot = new PilotView(career.Name, career.Callsign,
                career.Rules.ReplacementPending ? "KIA — successor on next sortie" : seat == null ? "Awaiting aircraft" :
                seat.dead ? "KIA — respawn available" : seat.ejected ? "EJECTED" : "ACTIVE",
                settings.PilotLives.Value == PilotLifeMode.Respawning, career.Rules.Deaths, career.Rules.Generation, career.Background);
            result.Hunt = career.Rules.Hunting;
            result.Bonus = career.Rules.BonusPoints; result.Origin = career.Rules.ScoreOrigin;
            result.Status = !WingLink.SquadAvailable ? WingLink.SquadUnavailableReason : !settings.EnemyAceHunts.Value
                ? "Enemy ace hunts disabled by host." : career.Notice;
            result.Event = career.Event; result.Speaker = career.Speaker; result.Chatter = career.Chatter;
            var rows = new List<EnemyWingView>(8);
            // Active local hunt is always first, even when other players fill recent history.
            for (int pass = 0; pass < 2; pass++)
            for (int i = hunts.Count - 1; i >= 0 && rows.Count < 8; i--)
            {
                Hunt h = hunts[i];
                if (!Hostile(h.EnemyHq, player.HQ)) continue;
                bool localActive = h.Owner == career && h.Outcome == HuntOutcome.Hunting;
                if ((pass == 0) != localActive) continue;
                string status = h.Outcome == HuntOutcome.Hunting ? "HUNTING" : h.Outcome == HuntOutcome.Defeated
                    ? h.Returned ? "RETURNED" : h.ReturnCandidate && Survived(h.AceId) ? "ACE MIA — MAY RETURN" : "ACE DEFEATED"
                    : h.Outcome == HuntOutcome.TargetLost ? "NORMAL OPERATIONS" : "DISENGAGED";
                if (h.Owner == career && h.Outcome == HuntOutcome.Hunting) { result.ActiveIndex = rows.Count; result.HuntId = h.Id; }
                rows.Add(new EnemyWingView(h.Symbol, h.Wing, h.Name + " / " + h.Callsign, h.Tier,
                    h.Tier == 1 ? "Veteran" : h.Tier < 4 ? "Elite" : "Ace", status, h.Alive,
                    h.SpawnedCount, h.Outcome == HuntOutcome.Hunting ? h.Owner == career ? "YOU" : h.Owner.Callsign : "", h.Returns,
                    h.Aircraft != null && h.Aircraft.Length > 0 ? WingLink.AceAbilityMask(h.Aircraft[0]) : 0));
            }
            result.Wings = rows.ToArray(); return result;
        }

        internal void Apply(SquadSnapshot snapshot, ulong id)
        {
            Pilot = snapshot.Pilot; HuntActive = snapshot.Hunt; Status = snapshot.Status;
            localIdentity = id; localBonus = snapshot.Bonus; localOrigin = snapshot.Origin;
            enemies = snapshot.Wings ?? Array.Empty<EnemyWingView>();
            ActiveEnemyWingIndex = snapshot.ActiveIndex;
            ActiveHuntId = snapshot.HuntId;
            if (snapshot.Event != lastEvent)
            {
                lastEvent = snapshot.Event;
                if (snapshot.Event != 0 && !string.IsNullOrEmpty(snapshot.Chatter))
                {
                    LastChatter = string.IsNullOrEmpty(snapshot.Speaker)
                        ? snapshot.Chatter
                        : snapshot.Speaker + ": " + snapshot.Chatter;
                }
                if (snapshot.Event != 0 && !Application.isBatchMode && !string.IsNullOrEmpty(snapshot.Chatter))
                {
                    if (snapshot.ActiveIndex >= 0 && ActiveHuntId != lastChatterHuntId)
                    {
                        lastChatterHuntId = ActiveHuntId;
                        pendingChatterSpeaker = snapshot.Speaker;
                        pendingChatterStatus = snapshot.Status;
                        pendingChatterMessage = snapshot.Chatter;
                        pendingChatterRelease = Time.unscaledTime + 10.4f;
                    }
                    else
                    {
                        WingLink.EnemyChatter(snapshot.Speaker, snapshot.Status, snapshot.Chatter);
                    }
                }
            }
        }

        internal void ClearLocal(string reason)
        { Pilot = default; HuntActive = false; ActiveEnemyWingIndex = -1; ActiveHuntId = 0; enemies = Array.Empty<EnemyWingView>(); Status = reason; LastChatter = string.Empty; }

        private static Pilot Primary(Aircraft aircraft) => aircraft != null && aircraft.pilots != null && aircraft.pilots.Length > 0 ? aircraft.pilots[0] : null;
        private static bool Survived(PersistentID id)
        { int status = WingLink.SurvivorStatus(id); return status == 1 || status == 2; }
        private static bool Flyable(Aircraft aircraft)
        { Pilot seat = Primary(aircraft); return aircraft != null && !aircraft.disabled && seat != null && !seat.dead && !seat.ejected; }
        private static bool Hostile(FactionHQ a, FactionHQ b) => a != null && b != null && a != b &&
            a.faction != null && b.faction != null &&
            !FactionHelper.EmptyOrNoFactionOrNeutral(a.faction.factionName) &&
            !FactionHelper.EmptyOrNoFactionOrNeutral(b.faction.factionName);
    }

    internal static class SquadRuntime { internal static SquadManager Active; }
}
