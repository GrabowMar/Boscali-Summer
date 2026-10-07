using System.Collections.Generic;
using BoscaliSummer.Core.Lifecycle;
using UnityEngine;

namespace BoscaliSummer.Modules.Vanguard.Runtime
{
    /// <summary>GLAIVE UGVs run on batteries: each is scuttled BatterySeconds after deployment (server, 1 Hz).</summary>
    internal sealed class PayloadLifetime : MonoBehaviour, ISceneService
    {
        internal static float BatterySeconds = 240f; // the nomodkit sim shortens it
        private static PayloadLifetime instance;
        private readonly List<Unit> units = new List<Unit>();
        private readonly List<float> expiry = new List<float>();
        private float nextCheck;

        public static int Alive => instance != null ? instance.units.Count : 0;

        private void Awake() => instance = this;

        public void ResetForScene()
        {
            units.Clear();
            expiry.Clear();
        }

        public static void Track(Unit unit)
        {
            if (instance == null || unit == null) return;
            instance.units.Add(unit);
            instance.expiry.Add(Time.timeSinceLevelLoad + BatterySeconds);
        }

        private void Update()
        {
            if (units.Count == 0 || Time.timeSinceLevelLoad < nextCheck) return;
            nextCheck = Time.timeSinceLevelLoad + 1f;
            for (int i = units.Count - 1; i >= 0; i--)
            {
                Unit unit = units[i];
                if (unit != null && !unit.disabled && Time.timeSinceLevelLoad < expiry[i]) continue;
                if (unit != null && !unit.disabled) Scuttle(unit);
                units.RemoveAt(i);
                expiry.RemoveAt(i);
            }
        }

        private static void Scuttle(Unit unit)
        {
            Spawner spawner = NetworkSceneSingleton<Spawner>.i;
            unit.Networkdisabled = true;
            unit.gameObject.SetActive(false);
            if (spawner != null && spawner.IsServer && spawner.ServerObjectManager != null)
                spawner.ServerObjectManager.Destroy(unit.gameObject);
        }
    }
}
