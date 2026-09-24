using System;
using System.Collections.Generic;
using UnityEngine;
using BoscaliSummer.Framework.Contracts;
using BoscaliSummer.Framework.Lifecycle;
using BoscaliSummer.Garrisons;

namespace BoscaliSummer.Features.UrbanCombat.Runtime
{
    /// <summary>
    /// Monitors friendly airbases and fortified garrisons for incoming hostile strike
    /// packages, driving base defense alerts and cockpit OPS emergency tickers.
    /// </summary>
    internal sealed class BaseDefenseAlarmService : MonoBehaviour, IBaseDefenseAlarmService, ISceneService
    {
        private const float PollInterval = 2.0f;
        private const float AlertRadiusSq = 7500f * 7500f;
        private const float GroundAlertRadiusSq = 2500f * 2500f;
        private const float StrongpointRadiusSq = 2000f * 2000f;

        private float nextPoll;
        private Airbase[] cachedAirbases;

        private static readonly List<Unit> scratchUnits = new List<Unit>(64);

        public string ActiveAlertTicker { get; private set; } = string.Empty;
        public bool IsBaseUnderAttack { get; private set; }

        public void ResetForScene()
        {
            nextPoll = 0f;
            cachedAirbases = null;
            scratchUnits.Clear();
            ActiveAlertTicker = string.Empty;
            IsBaseUnderAttack = false;
        }

        private void Update()
        {
            float now = Time.timeSinceLevelLoad;
            if (now < nextPoll) return;
            nextPoll = now + PollInterval;

            EvaluateThreats();
        }

        private void EvaluateThreats()
        {
            if (!GameManager.GetLocalAircraft(out Aircraft local) || local == null || local.NetworkHQ == null)
            {
                ActiveAlertTicker = string.Empty;
                IsBaseUnderAttack = false;
                return;
            }

            int known = FactionRegistry.airbaseLookup != null ? FactionRegistry.airbaseLookup.Count : 0;
            if (known > 0 && (cachedAirbases == null || cachedAirbases.Length != known))
            {
                cachedAirbases = new Airbase[known];
                FactionRegistry.airbaseLookup.Values.CopyTo(cachedAirbases, 0);
            }
            else if (known == 0 && (cachedAirbases == null || cachedAirbases.Length == 0))
            {
                cachedAirbases = UnityEngine.Object.FindObjectsOfType<Airbase>();
            }

            if (cachedAirbases == null || cachedAirbases.Length == 0)
            {
                ActiveAlertTicker = string.Empty;
                IsBaseUnderAttack = false;
                return;
            }

            FactionHQ friendlyHq = local.NetworkHQ;
            Airbase threatenedBase = null;

            for (int a = 0; a < cachedAirbases.Length; a++)
            {
                Airbase airbase = cachedAirbases[a];
                if (airbase == null || airbase.CurrentHQ != friendlyHq) continue;

                Vector3 basePos = airbase.center != null ? airbase.center.position : airbase.transform.position;
                GlobalPosition baseGlobal = basePos.ToGlobalPosition();

                scratchUnits.Clear();
                BattlefieldGrid.GetUnitsInRangeNonAlloc(baseGlobal, 7500f, scratchUnits);

                for (int u = 0; u < scratchUnits.Count; u++)
                {
                    Unit craft = scratchUnits[u];
                    if (craft == null || craft.disabled || craft.NetworkHQ == null || craft.NetworkHQ == friendlyHq)
                        continue;

                    if (craft is Aircraft || craft is Missile)
                    {
                        float distSq = (craft.transform.position - basePos).sqrMagnitude;
                        if (distSq <= AlertRadiusSq)
                        {
                            threatenedBase = airbase;
                            break;
                        }
                    }
                    else if (craft is GroundVehicle)
                    {
                        float distSq = (craft.transform.position - basePos).sqrMagnitude;
                        if (distSq <= GroundAlertRadiusSq)
                        {
                            threatenedBase = airbase;
                            break;
                        }
                    }
                }

                if (threatenedBase != null) break;
            }

            if (threatenedBase != null)
            {
                string baseName = !string.IsNullOrEmpty(threatenedBase.name)
                    ? threatenedBase.name.Replace("(Clone)", "").Trim().ToUpperInvariant()
                    : "AIRFIELD";
                ActiveAlertTicker = $"[BASE ALERT // {baseName} UNDER INGRESS THREAT · SCRAMBLE SQUADRON]";
                IsBaseUnderAttack = true;
                return;
            }

            Airbase strongpointBase = null;
            Vector3 localPos = local.transform.position;
            for (int a = 0; a < cachedAirbases.Length; a++)
            {
                Airbase airbase = cachedAirbases[a];
                if (airbase == null || airbase.CurrentHQ == null || airbase.CurrentHQ == friendlyHq) continue;

                Vector3 basePos = airbase.center != null ? airbase.center.position : airbase.transform.position;
                if ((localPos - basePos).sqrMagnitude > StrongpointRadiusSq) continue;
                if (ZoneGarrisonManager.TierFor(airbase) < 1) continue;
                if (ZoneGarrisonManager.IntactNestsFor(airbase) < 1) continue;

                strongpointBase = airbase;
                break;
            }

            if (strongpointBase != null)
            {
                string baseName = !string.IsNullOrEmpty(strongpointBase.name)
                    ? strongpointBase.name.Replace("(Clone)", "").Trim().ToUpperInvariant()
                    : "AIRFIELD";
                ActiveAlertTicker = $"[STRONGPOINTS // {baseName} GARRISONED - REDUCE NESTS BEFORE ASSAULT]";
                IsBaseUnderAttack = false;
            }
            else
            {
                ActiveAlertTicker = string.Empty;
                IsBaseUnderAttack = false;
            }
        }
    }
}
