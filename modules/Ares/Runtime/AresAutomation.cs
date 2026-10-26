#if !PUBLIC_RELEASE
using System;
using System.Collections.Generic;
using BoscaliSummer.Modules.Ares.Domain;
using UnityEngine;
using static BoscaliSummer.Core.Diagnostics.AutomationArgs;

namespace BoscaliSummer.Modules.Ares.Runtime
{
    /// <summary>
    /// Hook for the unattended ARES check. <c>Step {action}</c> drives the same <see cref="AresHost"/> methods the MISC buttons call;
    /// map clicks are given as <c>ahead</c>/<c>right</c> metres from the local aircraft. Refuses outside a nomodkit scenario except
    /// <c>status</c>. Results are flat numbers and strings.
    /// </summary>
    public static class AresAutomation
    {
        public static Dictionary<string, object> Step(Dictionary<string, object> args)
        {
            AresHost host = AresHost.Active;
            if (host == null) return Failure("AresAutomation", "Step", "no ARES host in this scene");
            string action = Text(args, "action") ?? "status";
            if (action != "status" && string.IsNullOrEmpty(Environment.GetEnvironmentVariable("NOMODKIT_SIM_SCENARIO")))
                return Failure("AresAutomation", "Step", "requires a nomodkit scenario");
            string said = null;
            switch (action)
            {
                case "status": break;
                case "spawn": said = Spawn(host, args); break;
                case "select": said = Select(host); break;
                case "verb": said = Verb(host, Text(args, "verb")); break;
                case "point": said = AtPoint(host, args); break;
                case "weather": said = host.Weather(Text(args, "name") ?? "live"); break;
                case "hour": said = host.SetHour(Number(args, "h", 12f)); break;
                case "summon":
                    string boss = Text(args, "boss");
                    said = boss == "ace" ? host.SummonAce() : boss == "dismiss" ? host.DismissBosses() : host.SummonKaiju();
                    break;
                case "cheat": said = Cheat(host, Text(args, "name"), Bool(args, "on")); break;
                case "undo": said = host.Undo(); break;
                case "clear": said = host.ClearAll(); break;
                default: return Failure("AresAutomation", "Step", "unknown action " + action);
            }
            Dictionary<string, object> state = Status(host);
            state["said"] = said ?? host.Result;
            return state;
        }

        private static Dictionary<string, object> Status(AresHost host)
        {
            List<Unit> batch = host.LastBatch();
            int alive = 0, down = 0;
            string sides = "", firstAt = "";
            if (batch != null)
                foreach (Unit unit in batch)
                {
                    if (unit == null) continue;
                    if (unit.disabled) down++; else alive++;
                    if (sides.Length == 0 && unit.NetworkHQ != null && unit.NetworkHQ.faction != null) sides = unit.NetworkHQ.faction.factionName;
                    if (firstAt.Length == 0)
                    {
                        GlobalPosition p = unit.GlobalPosition();
                        firstAt = Mathf.RoundToInt(p.x) + "," + Mathf.RoundToInt(p.z);
                    }
                }
            return new Dictionary<string, object>
            {
                ["ok"] = true,
                ["spawned"] = host.Spawned,
                ["batchAlive"] = alive,
                ["batchDown"] = down,
                ["batchSide"] = sides,
                ["batchAt"] = firstAt,
                ["selected"] = host.Selection().Count,
                ["frozen"] = host.FrozenCount,
                ["protected"] = host.ProtectedCount,
                ["followers"] = host.FollowerCount,
                ["armed"] = host.Armed ? host.PickLabel : "",
                ["air"] = host.Catalog(AresKind.Air).Count,
                ["ground"] = host.Catalog(AresKind.Ground).Count,
                ["naval"] = host.Catalog(AresKind.Naval).Count,
                ["structure"] = host.Catalog(AresKind.Structure).Count,
                ["munitions"] = host.Munitions().Count,
                ["groundKeys"] = Keys(host.Catalog(AresKind.Ground)),
                ["structureKeys"] = Keys(host.Catalog(AresKind.Structure)),
                ["god"] = AresCheats.God,
                ["missionRunning"] = MissionManager.IsRunning,
                ["myType"] = AresHost.LocalAircraft(out Aircraft me) && me.definition != null ? me.definition.unitName : "",
                ["myRpm"] = me != null ? Rpm(me) : 0f,
                ["mySpeed"] = me != null && me.CockpitRB() != null ? me.CockpitRB().velocity.magnitude : 0f,
                ["gravity"] = Physics.gravity.y,
                ["result"] = host.Result
            };
        }

        /// <summary>Mean engine RPM ratio (a turboprop reports no thrust of its own: its propeller makes it).</summary>
        private static float Rpm(Aircraft aircraft)
        {
            float total = 0f;
            int n = 0;
            foreach (IEngine engine in aircraft.engineStates)
                if (engine != null) { total += engine.GetRPMRatio(); n++; }
            return n == 0 ? 0f : total / n;
        }

        private static string Keys(List<UnitDefinition> list)
        {
            var keys = new List<string>();
            foreach (UnitDefinition definition in list) keys.Add(definition.jsonKey);
            string joined = string.Join(" ", keys);
            return joined.Length > 900 ? joined.Substring(0, 900) : joined;
        }

        private static GlobalPosition Point(Dictionary<string, object> args)
        {
            Transform from = AresHost.LocalAircraft(out Aircraft me) ? me.transform : Camera.main != null ? Camera.main.transform : null;
            if (from == null) return default;
            Vector3 forward = from.forward;
            forward.y = 0f;
            forward = forward.sqrMagnitude < 0.01f ? Vector3.forward : forward.normalized;
            Vector3 right = Vector3.Cross(Vector3.up, forward);
            return (from.position + forward * Number(args, "ahead", 2000f) + right * Number(args, "right", 0f)).ToGlobalPosition();
        }

        private static string Spawn(AresHost host, Dictionary<string, object> args)
        {
            switch (Text(args, "kind") ?? "ground")
            {
                case "air": host.Kind = AresKind.Air; break;
                case "naval": host.Kind = AresKind.Naval; break;
                case "structure": host.Kind = AresKind.Structure; break;
                case "kit": host.Kind = AresKind.Kit; break;
                default: host.Kind = AresKind.Ground; break;
            }
            host.Side = (int)Number(args, "side", 1f);
            host.Count = (int)Number(args, "count", 1f);
            host.Altitude = Number(args, "alt", 1500f);
            host.Item = (int)Number(args, "item", 0f);
            string key = Text(args, "key");
            if (!string.IsNullOrEmpty(key))
                for (int i = 0; i < host.ItemCount; i++)
                    if (host.ItemDetail(i).IndexOf(key, StringComparison.OrdinalIgnoreCase) >= 0 ||
                        host.ItemName(i).IndexOf(key, StringComparison.OrdinalIgnoreCase) >= 0) { host.Item = i; break; }
            return host.PlaceAt(Point(args), Number(args, "heading", 0f));
        }

        private static string Select(AresHost host)
        {
            var map = SceneSingleton<DynamicMap>.i;
            List<Unit> batch = host.LastBatch();
            if (map == null || batch == null) return "nothing to select";
            map.DeselectAllIcons();
            foreach (Unit unit in batch)
                if (unit != null) map.SelectIcon(unit);
            return "selected " + host.Selection().Count;
        }

        private static string Verb(AresHost host, string verb)
        {
            switch (verb)
            {
                case "repair": return host.RepairSelection();
                case "rearm": return host.RearmSelection();
                case "kill": return host.KillSelection();
                case "delete": return host.DeleteSelection();
                case "side": return host.CycleSideSelection();
                case "invuln": return host.ToggleProtectSelection();
                case "freeze": return host.ToggleFreezeSelection();
                case "follow": return host.ToggleFollowSelection();
                case "takecontrol": return host.TakeControl();
                case "fixme": return host.FixMe();
                case "credit": return host.AddCredit();
                default: return "unknown verb " + verb;
            }
        }

        private static string AtPoint(AresHost host, Dictionary<string, object> args)
        {
            GlobalPosition at = Point(args);
            switch (Text(args, "verb"))
            {
                case "move": return host.MoveSelectionTo(at);
                case "teleport": return host.TeleportSelectionTo(at);
                case "teleportme": return host.TeleportMe(at);
                case "strike":
                    host.Pattern = (int)Number(args, "pattern", 0f);
                    string key = Text(args, "munition");
                    if (!string.IsNullOrEmpty(key))
                        for (int i = 0; i < host.Munitions().Count; i++)
                            if ((host.Munitions()[i].jsonKey ?? "").IndexOf(key, StringComparison.OrdinalIgnoreCase) >= 0 ||
                                (host.Munitions()[i].unitName ?? "").IndexOf(key, StringComparison.OrdinalIgnoreCase) >= 0) { host.Munition = i; break; }
                    return host.StrikeAt(at);
                case "cast":
                    host.OpsAction = (int)Number(args, "ops", 1f);
                    return host.CastAt(at);
                case "fire": return host.FireAt(at);
                case "level": return host.LevelAt(at);
                default: return "unknown point verb";
            }
        }

        private static string Cheat(AresHost host, string name, bool on)
        {
            switch (name)
            {
                case "god": AresCheats.God = on; break;
                case "fuel": AresCheats.Fuel = on; break;
                case "ammo": AresCheats.Ammo = on; break;
                case "flares": AresCheats.Flares = on; break;
                case "blackout": AresCheats.NoBlackout = on; break;
                case "onehit": AresCheats.OneHit = on; break;
                case "boost": AresCheats.Boost = on; break;
                case "reveal": AresCheats.Reveal = on; break;
                case "gravity": return host.SetLowGravity(on);
                case "timestop": return host.SetTimeStop(on);
                case "bullet": return host.SetBulletTime(on);
                default: return "unknown cheat " + name;
            }
            return name.ToUpperInvariant() + (on ? " ON" : " OFF");
        }
    }
}
#endif
