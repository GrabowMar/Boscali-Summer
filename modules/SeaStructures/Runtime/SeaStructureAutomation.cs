#if !PUBLIC_RELEASE
using System;
using System.Collections.Generic;
using BoscaliSummer.Core.Game;
using BoscaliSummer.Modules.SeaStructures.Domain;
using BoscaliSummer.Modules.SeaStructures.Runtime;
using UnityEngine;
using static BoscaliSummer.Core.Diagnostics.AutomationArgs;

namespace BoscaliSummer.SeaStructures
{
    /// <summary>nomodkit single-player test hook (tests/ingame/sea-structures.json); inert outside a sim run.</summary>
    public static class SeaStructureAutomation
    {
        private const string Source = "SeaStructureAutomation";

        public static Dictionary<string, object> Step(Dictionary<string, object> args)
        {
            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("NOMODKIT_SIM_SCENARIO")) ||
                GameManager.gameState != GameState.SinglePlayer || !GameAccess.IsServer())
                return Failure(Source, "Step", "requires a hosted single-player nomodkit scenario");
            SeaStructureService service = SeaStructureService.Active;
            if (service == null) return Failure(Source, "Step", "Sea structures module not installed");
            string action = Text(args, "action") ?? "status";
            int index = (int)Number(args, "index", -1f); // default: the newest structure
            if (index < 0) index = SeaStructure.All.Count - 1;
            SeaStructure target = index >= 0 && index < SeaStructure.All.Count ? SeaStructure.All[index] : null;
            switch (action)
            {
                case "spawn":
                {
                    // "kind": rig | fort, in open water near map point "x","z", owned by "faction".
                    SeaKind kind = (Text(args, "kind") ?? "fort") == "rig" ? SeaKind.OilRig : SeaKind.SeaFort;
                    FactionHQ hq = FactionRegistry.HqFromName(Text(args, "faction") ?? "Primeva");
                    Vector3 near = new GlobalPosition(Number(args, "x", 30000f), 0f, Number(args, "z", -30000f)).ToLocalPosition();
                    var taken = new List<Vector3>();
                    foreach (SeaStructure s in SeaStructure.All) taken.Add(s.transform.position);
                    if (!SeaStructureService.FindSite(near, 0f, 12000f, taken, out Vector3 site)) return Failure(Source, action, "no open water near the point");
                    Ship ship = service.Spawn(kind, site.ToGlobalPosition(), Number(args, "heading", 0f), hq);
                    if (ship == null) return Failure(Source, action, "the structure was not spawned (no template or spawner)");
                    target = ship.GetComponent<SeaStructure>();
                    break;
                }
                case "hit":
                {
                    // The game's own TakeDamage on one section ("section": name) or on all of them, "impact" hit points each.
                    if (target == null) return Failure(Source, action, "no structure at that index");
                    string name = Text(args, "section");
                    float impact = Number(args, "impact", 30f);
                    foreach (KeyValuePair<string, UnitPart> part in target.Parts())
                        if (name == null || string.Equals(part.Key, name, StringComparison.OrdinalIgnoreCase))
                            part.Value.TakeDamage(0f, 0f, 1f, 0f, impact, default);
                    break;
                }
                case "look":
                {
                    // After a scenario "camera" op: sit "distance" m out on "bearing", "height" m up, looking at the structure "lift" m above the sea.
                    if (target == null || SceneSingleton<CameraStateManager>.i == null) return Failure(Source, action, "no structure or camera");
                    Vector3 aim = target.transform.position + Vector3.up * Number(args, "lift", 25f);
                    Vector3 eye = target.transform.position + Quaternion.Euler(0f, Number(args, "bearing", 30f), 0f) * Vector3.forward *
                                  Number(args, "distance", 220f) + Vector3.up * Number(args, "height", 60f);
                    SceneSingleton<CameraStateManager>.i.SetCameraPosition(eye.ToGlobalPosition(), Quaternion.LookRotation(aim - eye));
                    SceneSingleton<CameraStateManager>.i.cameraVelocity = Vector3.zero;
                    break;
                }
            }
            return Status(action, target);
        }

        private static Dictionary<string, object> Status(string action, SeaStructure target)
        {
            var result = new Dictionary<string, object>
            {
                ["ok"] = true, ["step"] = action, ["count"] = SeaStructure.All.Count,
                ["rigTemplate"] = SeaTemplates.Of(SeaKind.OilRig) != null, ["fortTemplate"] = SeaTemplates.Of(SeaKind.SeaFort) != null,
                ["rigDonor"] = SeaTemplates.Donor(SeaKind.OilRig), ["fortDonor"] = SeaTemplates.Donor(SeaKind.SeaFort)
            };
            if (target == null || target.Ship == null) return result;
            int parts = 0, detached = 0, turrets = 0, stations = target.Ship.weaponStations.Count;
            float worst = 100f;
            foreach (KeyValuePair<string, UnitPart> part in target.Parts())
            {
                parts++;
                if (part.Value.IsDetached()) detached++;
                worst = Mathf.Min(worst, part.Value.hitPoints);
            }
            foreach (Turret _ in target.GetComponentsInChildren<Turret>()) turrets++;
            target.Wear(out int worn, out int pieces, out int gone);
            // What is drawn that is not ours: donor renderers left visible, with where they sit.
            var donor = new List<string>();
            foreach (Renderer r in target.GetComponentsInChildren<Renderer>())
            {
                if (!(r is MeshRenderer || r is SkinnedMeshRenderer) || r.forceRenderingOff || !r.enabled || r.name.StartsWith("BS")) continue;
                string path = r.name;
                for (Transform t = r.transform.parent; t != null && t != target.transform && path.Length < 110; t = t.parent) path = t.name + "/" + path;
                Vector3 at = target.transform.InverseTransformPoint(r.bounds.center);
                donor.Add(path + " @" + at.x.ToString("0") + "," + at.y.ToString("0") + "," + at.z.ToString("0") + " size " + r.bounds.size.magnitude.ToString("0"));
            }
            result["donorRenderers"] = string.Join(" | ", donor);
            var ships = new List<string>();
            foreach (ShipDefinition def in Encyclopedia.i.ships)
                if (def != null && def.unitPrefab != null)
                    ships.Add(def.jsonKey + ":" + def.unitPrefab.GetComponentsInChildren<Turret>(true).Length + "t/" +
                              def.unitPrefab.GetComponentsInChildren<ShipPart>(true).Length + "p/" + def.unitPrefab.GetComponentsInChildren<Hangar>(true).Length + "h");
            result["ships"] = string.Join(" ", ships);
            result["kind"] = target.Layout.Key;
            result["name"] = target.Ship.unitName;
            result["disabled"] = target.Ship.disabled;
            result["exploded"] = target.Exploded ? 1 : 0;
            result["gone"] = gone;
            result["sections"] = parts;
            result["detached"] = detached;
            result["worstHp"] = worst;
            result["turrets"] = turrets;
            result["stations"] = stations;
            result["wornSections"] = worn;
            result["fallenPieces"] = pieces;
            result["height"] = target.transform.position.y - Datum.LocalSeaY;
            result["tilt"] = Vector3.Angle(target.transform.up, Vector3.up);
            return result;
        }
    }
}
#endif
