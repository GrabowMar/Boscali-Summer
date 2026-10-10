using System.Collections.Generic;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Core.Services;
using BoscaliSummer.Modules.Command.Presentation;
using UnityEngine;
using static BoscaliSummer.Core.Diagnostics.AutomationArgs;

namespace BoscaliSummer.Modules.Command.Runtime
{
    /// <summary>Hooks for the unattended in-game STR check (<c>tests/ingame/str-panels.json</c>): open a tab the way the bezel
    /// press does, read back every backend the panel paints from, and send posture / pick / replan through the same
    /// <see cref="ITheaterWarView"/> calls the buttons use. Dev tooling: nothing here runs unless called.</summary>
    public static class StrAutomation
    {
        /// <summary>Maximize the map, select the STR screen and show <c>tab</c> (0 SITUATION, 1 COMMAND, 2 OPERATIONS).</summary>
        public static Dictionary<string, object> Open(Dictionary<string, object> args)
        {
            StrMfdPanel panel = StrMfdPanel.Active;
            if (panel == null) return Fail("Open", "no STR panel in this scene");
            DynamicMap map = SceneSingleton<DynamicMap>.i;
            if (map != null && !DynamicMap.mapMaximized) map.Maximize();
            panel.OpenForAutomation(Mathf.Clamp((int)Number(args, "tab", 0f), 0, 2));
            return Read(null);
        }

        /// <summary>Send one intent: <c>posture</c> (cycle to the next posture), <c>pick</c> (first proposal), <c>replan</c>.</summary>
        public static Dictionary<string, object> Verb(Dictionary<string, object> args)
        {
            if (!ModuleServices.TryGet(out ITheaterWarView war)) return Fail("Verb", "no ITheaterWarView (TheaterOps off)");
            string verb = Text(args, "verb") ?? "";
            bool sent;
            switch (verb)
            {
                case "posture": sent = war.RequestPosture((TheaterWarPosture)(((int)war.Posture + 1) % 3)); break;
                case "pick":
                    // The staff auto-selects O1 after 60 s, so an empty deck with a live operation is a pass, not a failure.
                    if (war.Proposals == null || war.Proposals.Count == 0)
                    {
                        if (war.ActiveOperation == null) return Fail("Verb", "no proposals and no operation");
                        Dictionary<string, object> skipped = Read(null);
                        skipped["sent"] = false; skipped["skipped"] = "staff already selected " + war.ActiveOperation.Label;
                        return skipped;
                    }
                    sent = war.RequestPick(war.Proposals[0].Id, war.Proposals[0].Revision);
                    break;
                case "replan":
                    if (war.ActiveOperation == null) return Fail("Verb", "no active operation to replan");
                    sent = war.RequestCancel(war.ActiveOperation.Id, war.ActiveOperation.Revision);
                    break;
                default: return Fail("Verb", "unknown verb '" + verb + "'");
            }
            Dictionary<string, object> r = Read(null);
            r["sent"] = sent;
            return r;
        }

        public static Dictionary<string, object> Read(Dictionary<string, object> args)
        {
            StrMfdPanel panel = StrMfdPanel.Active;
            var r = new Dictionary<string, object> { { "panel", panel != null } };
            if (panel != null) { r["screenActive"] = panel.ScreenActiveForAutomation; r["tab"] = panel.PageForAutomation; }

            CommandManager command = CommandManager.Active;
            r["command"] = command != null;
            if (command != null)
            {
                var s = command.TheaterState;
                r["sectorsFriendly"] = s.FriendlySectorCount;
                r["sectorsHostile"] = s.HostileSectorCount;
                r["sectorsContested"] = s.ContestedSectorCount;
                r["territory"] = s.TerritoryControlRatio;
                r["frontlineMetres"] = s.FrontlineLengthMetres;
                r["defcon"] = s.DefconLevel;
            }

            r["highCommand"] = ModuleServices.TryGet(out IHighCommandView staff);
            if (staff != null)
            {
                r["staffAvailable"] = staff.Available;
                r["staffPosts"] = staff.Commanders?.Count ?? 0;
                r["staffActive"] = staff.FriendlyActive;
                r["staffCohesion"] = staff.FriendlyCohesion;
                r["staffLog"] = staff.Log?.Count ?? 0;
            }

            r["theaterWar"] = ModuleServices.TryGet(out ITheaterWarView war);
            if (war != null)
            {
                r["warAvailable"] = war.Available;
                r["warSnapshot"] = war.HasSnapshot;
                r["warAge"] = war.SnapshotAgeSeconds;
                r["warCanCommand"] = war.CanCommand;
                r["warPending"] = war.CommandPending;
                r["warStatus"] = war.CommandStatus ?? "";
                r["posture"] = war.Posture.ToString();
                r["proposals"] = war.Proposals?.Count ?? 0;
                r["fronts"] = war.Fronts?.Count ?? 0;
                var fronts = new List<string>();
                if (war.Fronts != null)
                    foreach (TheaterFrontView f in war.Fronts)
                        fronts.Add(f.Label + " | " + f.Status + " | " + f.Pressure.ToString("0.00") + (f.Observed ? "" : " | unobserved") +
                                   " | nearest own ground " + NearestOwnGroundKm(f.X, f.Z).ToString("0.0") + " km");
                r["frontList"] = string.Join(" ;; ", fronts);
                r["activeOp"] = war.ActiveOperation != null ? war.ActiveOperation.Kind + " " + war.ActiveOperation.Label + " / " + war.ActiveOperation.Phase : "";
                r["opsLog"] = war.StaffLog?.Count ?? 0;
            }
            r["ok"] = true;   // a readout; the flags above say what is missing
            return r;
        }

        /// <summary>Distance from a front fix to the closest ground vehicle of the local faction (probe for the 3 km staff radius).</summary>
        private static float NearestOwnGroundKm(float x, float z)
        {
            FactionHQ hq = SceneSingleton<DynamicMap>.i != null ? SceneSingleton<DynamicMap>.i.HQ : null;
            float best = float.MaxValue;
            if (hq == null || UnitRegistry.allUnits == null) return -1f;
            foreach (Unit u in UnitRegistry.allUnits)
            {
                if (!(u is GroundVehicle) || u.disabled || !ReferenceEquals(u.NetworkHQ, hq)) continue;
                GlobalPosition p = u.GlobalPosition();
                best = Mathf.Min(best, Mathf.Sqrt((p.x - x) * (p.x - x) + (p.z - z) * (p.z - z)));
            }
            return best == float.MaxValue ? -1f : best / 1000f;
        }

        private static Dictionary<string, object> Fail(string hook, string error)
        {
            Debug.LogWarning("[StrAutomation] " + hook + ": " + error);
            return new Dictionary<string, object> { { "ok", false }, { "error", error } };
        }
    }
}
