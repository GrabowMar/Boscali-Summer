using System;
using System.Collections.Generic;
using BoscaliSummer.Features.AirSurvival.Domain;
using BoscaliSummer.Framework.Contracts;
using BoscaliSummer.Framework.Features;
using BoscaliSummer.Runtime;
using HarmonyLib;
using UnityEngine;

namespace BoscaliSummer.Features.AirSurvival.Patches
{
    [HarmonyPatch(typeof(AIPilotCombatModes), nameof(AIPilotCombatModes.FixedUpdateState))]
    internal static class AirMissileFlarePatch
    {
        private sealed class FlareState
        {
            internal float NextCheck;
            internal float LastSeen;
            internal float LastFlare = -100f;
            internal readonly int[] RecentMissiles = new int[AirFlarePolicy.MaximumMissilesPerCheck];
            internal int RecentCount;
            internal int RecentCursor;
        }

        private static readonly Dictionary<int, FlareState> aircraftStates =
            new Dictionary<int, FlareState>(AirFlarePolicy.MaximumTrackedAircraft);
        private static float lastNow;

        [HarmonyPostfix]
        private static void Postfix(Pilot pilot)
        {
            Aircraft aircraft = pilot?.aircraft;
            if (aircraft == null || !GameAccess.IsServer() || !aircraft.IsServer ||
                !aircraft.LocalSim || aircraft.disabled || aircraft.Player != null ||
                pilot.dead || pilot.playerControlled || pilot.currentState is not AIPilotCombatModes)
                return;

            float now = Time.timeSinceLevelLoad;
            if (now < lastNow) aircraftStates.Clear(); // scene clock restarted
            lastNow = now;
            int id = aircraft.persistentID.GetHashCode();
            if (!aircraftStates.TryGetValue(id, out FlareState state))
            {
                if (aircraftStates.Count >= AirFlarePolicy.MaximumTrackedAircraft)
                {
                    int? stale = null;
                    foreach (KeyValuePair<int, FlareState> entry in aircraftStates)
                        if (now - entry.Value.LastSeen > 10f) { stale = entry.Key; break; }
                    if (stale.HasValue) aircraftStates.Remove(stale.Value);
                    if (aircraftStates.Count >= AirFlarePolicy.MaximumTrackedAircraft) return;
                }
                state = new FlareState();
                aircraftStates.Add(id, state);
            }
            state.LastSeen = now;
            if (now < state.NextCheck) return;
            state.NextCheck = now + AirFlarePolicy.CheckSeconds;

            if (!WingLink.TryIsWingMember(id, out bool isWingMember) || isWingMember ||
                (ModServices.TryGet(out IAircraftTaskExclusion exclusion) && exclusion.IsExcluded(id)) ||
                aircraft.countermeasureTrigger || aircraft.countermeasureManager == null)
                return;
            MissileWarning warning = aircraft.GetMissileWarningSystem();
            List<Missile> missiles = warning?.knownMissiles;
            if (missiles == null) return;
            if (missiles.Count == 0) { state.RecentCount = 0; return; }

            int count = Math.Min(missiles.Count, AirFlarePolicy.MaximumMissilesPerCheck);
            for (int i = 0; i < count; i++)
            {
                Missile missile = missiles[i];
                if (missile == null || missile.disabled || missile.targetID != aircraft.persistentID ||
                    missile.GetSeekerType() != "IR" || missile.rb == null || aircraft.rb == null)
                    continue;
                int missileId = missile.persistentID.GetHashCode();
                bool alreadyFlared = false;
                for (int j = 0; j < state.RecentCount; j++)
                    if (state.RecentMissiles[j] == missileId) { alreadyFlared = true; break; }
                if (alreadyFlared) continue;
                Vector3 relative = missile.transform.position - aircraft.transform.position;
                float distance = relative.magnitude;
                if (distance <= 0f) continue;
                float closing = -Vector3.Dot(relative / distance,
                    missile.rb.velocity - aircraft.rb.velocity);
                if (!AirFlarePolicy.ShouldPop(distance, closing,
                    aircraft.countermeasureManager.GetFlareAmmoProportion(),
                    aircraft.countermeasureTrigger, now - state.LastFlare)) continue;

                state.RecentMissiles[state.RecentCursor] = missileId;
                if (state.RecentCount < state.RecentMissiles.Length) state.RecentCount++;
                state.RecentCursor = (state.RecentCursor + 1) % state.RecentMissiles.Length;
                state.LastFlare = now;
                aircraft.countermeasureManager.PopFlares();
                return;
            }
        }
    }
}
