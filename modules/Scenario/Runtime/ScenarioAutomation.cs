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
        /// The scripted world as the game sees it: each named unit (args <c>units</c>, comma-separated) as "x z" or "down" or "absent"; the local
        /// pilot's aircraft; and for every side, how many units of each other side its HQ is tracking (peace means zero).
        /// </summary>
        public static Dictionary<string, object> World(Dictionary<string, object> args)
        {
            var report = new Dictionary<string, object> { ["ok"] = true, ["peace"] = Core.Scenario.FactionPeace.Any ? 1 : 0 };
            string names = args != null && args.TryGetValue("units", out object list) ? list as string : null;
            foreach (string name in (names ?? "").Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string key = name.Trim();
                report["unit_" + key] = !UnitRegistry.customIDLookup.TryGetValue(key, out Unit unit) ? "absent"
                    : unit == null || unit.disabled ? "down" : unit.GlobalPosition().x.ToString("0") + " " + unit.GlobalPosition().z.ToString("0");
            }
            bool flying = GameManager.GetLocalAircraft(out Aircraft own) && own != null;
            report["pilot"] = !flying ? "none" : own.disabled ? "down" : own.GlobalPosition().x.ToString("0") + " " + own.GlobalPosition().y.ToString("0") + " " +
                own.GlobalPosition().z.ToString("0") + " " + (own.NetworkHQ?.faction != null ? own.NetworkHQ.faction.factionName : "?");
            var hqs = new List<FactionHQ>(FactionRegistry.GetAllHQs());
            foreach (string extra in new[] { Core.Scenario.SideNames.Pmc, Core.Scenario.SideNames.Pirates })
            {
                FactionHQ hq = FactionRegistry.HqFromName(extra);
                if (hq != null && !hqs.Contains(hq)) hqs.Add(hq);
            }
            hqs.RemoveAll(hq => hq == null || hq.faction == null);
            foreach (FactionHQ hq in hqs)
            {
                // Every pair is written, zeros included, so a test can assert that a side at peace tracks nobody of the other.
                var counts = new Dictionary<string, int>(StringComparer.Ordinal);
                foreach (FactionHQ other in hqs) counts[other.faction.factionName] = 0;
                foreach (KeyValuePair<PersistentID, TrackingInfo> tracked in hq.trackingDatabase)
                {
                    if (!UnitRegistry.TryGetUnit(tracked.Key, out Unit unit) || unit.NetworkHQ == null || unit.NetworkHQ.faction == null) continue;
                    string side = unit.NetworkHQ.faction.factionName;
                    counts[side] = counts.TryGetValue(side, out int n) ? n + 1 : 1;
                }
                foreach (KeyValuePair<string, int> count in counts) report["tracks_" + hq.faction.factionName + "_" + count.Key] = count.Value;
                // A track between two sides at peace is a leak: name it, with its type, so the next run shows where it came from.
                var leaks = new List<string>();
                foreach (KeyValuePair<PersistentID, TrackingInfo> tracked in hq.trackingDatabase)
                    if (UnitRegistry.TryGetUnit(tracked.Key, out Unit unit) && unit.NetworkHQ != null && unit.NetworkHQ.faction != null &&
                        Core.Scenario.FactionPeace.Between(hq.faction.factionName, unit.NetworkHQ.faction.factionName))
                        leaks.Add((unit.unitName ?? unit.name) + (unit.disabled ? " (down)" : ""));
                if (leaks.Count > 0) report["leaks_" + hq.faction.factionName] = string.Join(", ", leaks);
            }
            return report;
        }

        /// <summary>Knocks out the named units (args <c>units</c>, comma-separated), as if shot down or sunk: lets a test walk a script's story.</summary>
        public static Dictionary<string, object> Disable(Dictionary<string, object> args)
        {
            int done = 0;
            var failed = new List<string>();
            string names = args != null && args.TryGetValue("units", out object list) ? list as string : null;
            foreach (string name in (names ?? "").Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
                if (UnitRegistry.customIDLookup.TryGetValue(name.Trim(), out Unit unit) && unit != null && !unit.disabled)
                {
                    // A handler of the game's that throws is a finding, not the end of the test: the unit is down either way.
                    try { unit.DisableUnit(); }
                    catch (Exception e)
                    {
                        failed.Add(name.Trim());
                        Plugin.Logger.LogError("[Scenario] disabling '" + name.Trim() + "' threw: " + e);
                    }
                    done++;
                }
            return new Dictionary<string, object> { ["ok"] = true, ["disabled"] = done, ["threw"] = string.Join(",", failed) };
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
