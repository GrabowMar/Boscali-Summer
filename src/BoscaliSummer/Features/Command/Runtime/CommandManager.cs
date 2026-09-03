using System;
using System.Collections.Generic;
using BepInEx.Logging;
using BoscaliSummer.Features.Command.Configuration;
using BoscaliSummer.Features.Command.Domain;
using BoscaliSummer.Framework.Contracts;
using BoscaliSummer.Framework.Lifecycle;
using BoscaliSummer.Runtime;
using NOAvionics;
using NuclearOption.Networking;
using UnityEngine;

namespace BoscaliSummer.Features.Command.Runtime
{
    internal sealed class CommandManager : MonoBehaviour, ISceneService
    {
        public static CommandManager Active { get; internal set; }

        private CommandSettings settings;
        private IProgressionView progression;
        private ManualLogSource logger;

        public CommandDoctrine ActiveDoctrine { get; private set; } = CommandDoctrine.Balanced;
        public readonly List<PersistentID> PriorityTargets = new List<PersistentID>(4);
        public Airbase SectorStrikeTarget { get; private set; }
        public readonly TacticalTheaterState TheaterState = new TacticalTheaterState();

        public int PlayerRank => progression != null ? progression.Rank : 0;

        public void Configure(CommandSettings config, IProgressionView progressionView, ManualLogSource log)
        {
            settings = config;
            progression = progressionView;
            logger = log;
            Active = this;
        }

        public void ResetForScene()
        {
            ActiveDoctrine = CommandDoctrine.Balanced;
            PriorityTargets.Clear();
            SectorStrikeTarget = null;
            TheaterState.Reset();
            PublishInterop();
        }

        private void OnDestroy()
        {
            if (Active == this) Active = null;
        }

        public bool TrySetDoctrine(CommandDoctrine doctrine)
        {
            ActiveDoctrine = doctrine;
            PublishInterop();
            logger?.LogInfo("[COM] Friendly mission-AI doctrine: " + CommandDoctrineHelper.GetName(doctrine));
            return true;
        }

        public bool TryDesignatePriorityTarget(Unit target)
        {
            if (target == null) return false;
            int max = CommandDoctrineHelper.MaxPriorityTargets(PlayerRank);
            if (max <= 0)
            {
                logger?.LogInfo("[COM] Priority target designation denied: requires Rank 1 (Sergeant).");
                return false;
            }

            if (PriorityTargets.Contains(target.persistentID))
            {
                PriorityTargets.Remove(target.persistentID);
                PublishInterop();
                return true;
            }

            if (PriorityTargets.Count >= max)
            {
                PriorityTargets.RemoveAt(0);
            }

            PriorityTargets.Add(target.persistentID);
            PublishInterop();
            logger?.LogInfo("[COM] Designated priority target: " + target.unitName);
            return true;
        }

        public bool TryOrderSectorStrike(Airbase airbase)
        {
            if (airbase == null) return false;
            if (!CommandDoctrineHelper.CanOrderSectorStrike(PlayerRank))
            {
                logger?.LogInfo("[COM] Sector strike wave denied: requires Rank 4 (Major).");
                return false;
            }

            SectorStrikeTarget = airbase;
            logger?.LogInfo("[COM] Sector strike is not implemented; recorded " + airbase.name + " as a mark only.");
            return true;
        }

        public float GetTargetScoreMultiplier(Unit searcher, Unit target)
        {
            if (searcher == null || target == null) return 1f;

            bool analyzerIsFriendly = false;
            if (GameManager.GetLocalPlayer<Player>(out Player player) && player != null && player.HQ != null)
                analyzerIsFriendly = searcher.NetworkHQ == player.HQ;

            bool targetIsWingman = PresenceBoard.Contains(
                PresenceBoard.GetInts(PresenceBoard.WingMemberIds),
                target.persistentID.GetHashCode());

            bool targetIsAntiAir = target.definition != null && target.definition.roleIdentity.antiAir > 0.1f;
            return TheaterScoring.Bias(
                analyzerIsFriendly,
                targetIsWingman,
                (int)ActiveDoctrine,
                PriorityTargets.Contains(target.persistentID),
                target is Aircraft,
                target is Building,
                targetIsAntiAir);
        }

        public void PublishInterop()
        {
            TheaterInteropPush.PublishGuid();
            var hashes = new int[PriorityTargets.Count];
            for (int i = 0; i < PriorityTargets.Count; i++)
                hashes[i] = PriorityTargets[i].GetHashCode();
            TheaterInteropPush.PublishDoctrine((int)ActiveDoctrine, hashes);
        }

        public void UpdateTelemetry(FactionHQ localHq)
        {
            if (localHq == null) return;

            TheaterState.FriendlyAircraftCount = 0;
            TheaterState.HostileAircraftCount = 0;
            TheaterState.FriendlyAirbaseCount = 0;
            TheaterState.HostileAirbaseCount = 0;
            TheaterState.ContestedAirbaseCount = 0;
            TheaterState.FriendlySamCount = 0;
            TheaterState.HostileSamCount = 0;

            IReadOnlyList<Aircraft> allAircraft = UnitRegistry.allAircraft;
            if (allAircraft != null)
            {
                for (int i = 0; i < allAircraft.Count; i++)
                {
                    Aircraft ac = allAircraft[i];
                    if (ac == null || ac.disabled) continue;
                    if (ac.NetworkHQ == localHq) TheaterState.FriendlyAircraftCount++;
                    else if (localHq.IsTargetBeingTracked(ac)) TheaterState.HostileAircraftCount++;
                }
            }

            int totalAir = TheaterState.FriendlyAircraftCount + TheaterState.HostileAircraftCount;
            TheaterState.AirSuperiorityRatio = totalAir > 0
                ? (float)TheaterState.FriendlyAircraftCount / totalAir
                : 0.5f;

            // Airbases
            IEnumerable<Airbase> airbases = localHq.GetAirbases();
            if (airbases != null)
            {
                foreach (Airbase ab in airbases)
                {
                    if (ab == null || ab.UnitDestroyed()) continue;
                    if (ab.CurrentHQ == localHq) TheaterState.FriendlyAirbaseCount++;
                    else TheaterState.HostileAirbaseCount++;
                }
            }

            // Sensors
            if (GameAccess.HqSensorsAvailable)
            {
                List<Radar> radars = GameAccess.GetHqRadars(localHq);
                if (radars != null) TheaterState.FriendlySamCount = radars.Count;
            }

            // Defcon status
            if (TheaterState.AirSuperiorityRatio > 0.7f && TheaterState.FriendlyAirbaseCount > TheaterState.HostileAirbaseCount)
            {
                TheaterState.DefconLevel = 4;
                TheaterState.PrimaryThreatDescription = "AIR DOMINANCE";
            }
            else if (TheaterState.AirSuperiorityRatio < 0.35f || TheaterState.HostileAircraftCount > TheaterState.FriendlyAircraftCount * 2)
            {
                TheaterState.DefconLevel = 1;
                TheaterState.PrimaryThreatDescription = "AIR DEFENSE ALERT";
            }
            else
            {
                TheaterState.DefconLevel = 3;
                TheaterState.PrimaryThreatDescription = "CONTESTED THEATER";
            }
        }
    }
}
