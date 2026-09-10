using System;
using System.Collections.Generic;
using UnityEngine;
using BoscaliSummer.Framework.Contracts;
using BoscaliSummer.Framework.Lifecycle;

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

            if (cachedAirbases == null || cachedAirbases.Length == 0)
            {
                if (FactionRegistry.airbaseLookup != null && FactionRegistry.airbaseLookup.Count > 0)
                {
                    var values = FactionRegistry.airbaseLookup.Values;
                    cachedAirbases = new Airbase[values.Count];
                    values.CopyTo(cachedAirbases, 0);
                }
                else
                {
                    cachedAirbases = UnityEngine.Object.FindObjectsOfType<Airbase>();
                }
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
                GlobalPosition baseGlobal = new GlobalPosition(basePos);

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
            }
            else
            {
                ActiveAlertTicker = string.Empty;
                IsBaseUnderAttack = false;
            }
        }
    }
}
