#if !PUBLIC_RELEASE
using System;
using System.Collections.Generic;

namespace BoscaliSummer.Modules.Scenario.Runtime
{
    /// <summary>
    /// A hook for unattended in-game tests (nomodkit's <c>call</c> op, tests/ingame/scenario-boscali-summer.json): reads back what the script runner is doing.
    /// Nothing here runs unless a nomodkit scenario calls it.
    /// </summary>
    public static class ScenarioAutomation
    {
        public static Dictionary<string, object> State(Dictionary<string, object> args)
        {
            ScenarioHost host = ScenarioHost.Instance;
            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("NOMODKIT_SIM_SCENARIO")) || host == null)
                return new Dictionary<string, object> { ["ok"] = false, ["error"] = "requires a nomodkit scenario with the Scenario module installed" };
            return host.Report();
        }

        /// <summary>
        /// For writing a script: where the map's airbases are, and the ground under a grid of points (args x, z, step, count; or airbase = a name to centre on).
        /// Each grid cell reads as the height in metres, with a trailing b where something built stands on it.
        /// </summary>
        public static Dictionary<string, object> Survey(Dictionary<string, object> args)
        {
            var report = new Dictionary<string, object> { ["ok"] = true };
            var bases = new List<string>();
            UnityEngine.Vector3 centre = default;
            string wanted = args != null && args.TryGetValue("airbase", out object name) ? name as string : null;
            foreach (KeyValuePair<string, Airbase> pair in FactionRegistry.airbaseLookup)
            {
                if (pair.Value == null || pair.Value.center == null) continue;
                GlobalPosition at = pair.Value.center.position.ToGlobalPosition();
                bases.Add(pair.Key + " " + at.x.ToString("0") + " " + at.y.ToString("0") + " " + at.z.ToString("0"));
                if (pair.Key == wanted) centre = new UnityEngine.Vector3(at.x, 0f, at.z);
            }
            report["airbases"] = string.Join(" | ", bases);
            if (args == null) return report;
            float Number(string key, float fallback) => args.TryGetValue(key, out object value) && value != null ? Convert.ToSingle(value) : fallback;
            float x = Number("x", centre.x), z = Number("z", centre.z), step = Number("step", 150f);
            int count = (int)Number("count", 15f);
            var rows = new List<string>();
            for (int row = 0; row < count; row++)
            {
                var cells = new List<string>();
                for (int column = 0; column < count; column++)
                {
                    float px = x + (column - count / 2) * step, pz = z + (count / 2 - row) * step;
                    UnityEngine.Vector3 from = new GlobalPosition(px, 5000f, pz).ToLocalPosition();
                    if (!UnityEngine.Physics.Raycast(from, UnityEngine.Vector3.down, out UnityEngine.RaycastHit hit, 6000f, (int)PhysicsLayers.StaticsMask,
                            UnityEngine.QueryTriggerInteraction.Ignore)) { cells.Add("-"); continue; }
                    bool ground = GameAssets.i != null && hit.collider.sharedMaterial == GameAssets.i.terrainMaterial;
                    cells.Add(hit.point.ToGlobalPosition().y.ToString("0") + (ground ? "" : "b"));
                }
                rows.Add(string.Join(" ", cells));
            }
            report["x"] = x; report["z"] = z; report["step"] = step;
            report["rows"] = string.Join(" / ", rows);
            return report;
        }
    }
}
#endif
