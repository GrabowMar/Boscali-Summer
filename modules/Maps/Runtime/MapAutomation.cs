using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NuclearOption.SavedMission;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace BoscaliSummer.Modules.Maps.Runtime
{
    /// <summary>
    /// Dev tooling for the map generator, called from sim scenarios (the "call" op) and never by the game.
    /// <see cref="Catalogue"/> writes what the generator needs to know about the game's own content: every building and
    /// scenery definition with its size and role, every airbase of the shipped maps with its runway, and the buildings
    /// the shipped missions stand on those maps (so a generated airfield can be laid out like a shipped one).
    /// </summary>
    public static class MapAutomation
    {
        /// <summary>Logs the picker's tabs and every mission of the mod's one tab by its label, for a sim to match.</summary>
        public static Dictionary<string, object> Picker(Dictionary<string, object> args)
        {
            MissionGroup.Init();
            MissionGroup[] tabs = MissionGroup.MissionSelectGroups;
            string[] ours = tabs.Where(g => g != null && g.Name.StartsWith("Boscali Summer", StringComparison.Ordinal)).Select(g => g.Name).ToArray();
            string[] listed = tabs.Where(g => g != null && g.Name == "Boscali Summer").SelectMany(g => g.GetMissions()).Select(k => k.Name).ToArray();
            Plugin.Logger?.LogInfo("[Maps] Picker tabs of the mod: " + string.Join(" | ", ours) + ". Missions: " + string.Join(" | ", listed));
            foreach (MissionKey key in tabs.Where(g => g != null && g.Name == "Boscali Summer").SelectMany(g => g.GetMissions()))
            {
                bool loaded = key.TryLoad(out Mission mission, out string error);
                var said = loaded && mission?.LoadErrors != null ? mission.LoadErrors.Warnings.Concat(mission.LoadErrors.Errors).ToList() : new List<string> { error };
                said.RemoveAll(string.IsNullOrEmpty);
                if (said.Count > 0) Plugin.Logger?.LogWarning("[Maps] Load warnings of '" + key.Name + "' (" + said.Count + "): " + string.Join(" ;; ", said));
            }
            return new Dictionary<string, object> { ["tabs"] = ours.Length, ["missions"] = listed.Length, ["list"] = string.Join(" | ", listed) };
        }

        /// <summary>Checks a hosted game mode. Args: <c>ships</c> and <c>vehicles</c>, the least number of each that must
        /// be in the world; <c>airbases</c>, unique name to owning faction ("" for nobody). Logs "Mode check passed" or an error.</summary>
        public static Dictionary<string, object> Mode(Dictionary<string, object> args)
        {
            int Least(string key) => args != null && args.TryGetValue(key, out object v) && v != null ? Convert.ToInt32(v) : 0;
            int ships = UnityEngine.Object.FindObjectsOfType<Ship>().Length, vehicles = UnityEngine.Object.FindObjectsOfType<GroundVehicle>().Length, bases = 0;
            var wrong = new List<string>();
            if (ships < Least("ships")) wrong.Add(ships + " ships of " + Least("ships"));
            if (vehicles < Least("vehicles")) wrong.Add(vehicles + " vehicles of " + Least("vehicles"));
            if (args != null && args.TryGetValue("airbases", out object given) && given != null)
                foreach (KeyValuePair<string, JToken> want in given as JObject ?? JObject.FromObject(given))
                {
                    bases++;
                    if (!FactionRegistry.airbaseLookup.TryGetValue(want.Key, out Airbase airbase) || airbase == null) { wrong.Add(want.Key + " is missing"); continue; }
                    string owner = airbase.CurrentHQ != null && airbase.CurrentHQ.faction != null ? airbase.CurrentHQ.faction.factionName : "";
                    if (owner != (string)want.Value) wrong.Add(want.Key + " is held by '" + owner + "', not '" + (string)want.Value + "'");
                }
            LoadErrors said = MissionManager.CurrentMission?.LoadErrors;
            if (said != null) wrong.AddRange(said.Warnings.Concat(said.Errors).Concat(said.Exceptions.Select(e => e.ToString())).Select(w => "load warning: " + w));
            if (wrong.Count == 0) Plugin.Logger?.LogInfo("[Maps] Mode check passed: " + ships + " ships, " + vehicles + " vehicles, " + bases + " airbases.");
            else Plugin.Logger?.LogError("[Maps] Mode check failed: " + string.Join("; ", wrong));
            return new Dictionary<string, object> { ["ok"] = wrong.Count == 0, ["ships"] = ships, ["vehicles"] = vehicles, ["wrong"] = string.Join("; ", wrong) };
        }

        /// <summary>The airfield AI acceptance probe: with no args it reports, for every aircraft in the world,
        /// whether it is airborne (over 50 m or climbing), its altitude, speed and the state its pilot is in. A sim
        /// spawns aircraft in hangars of an `airbase` and, minutes later, calls this and asserts they flew. The
        /// verdict is the scenario's `result_above` check; this only measures, so it never returns ok=false (which
        /// the call op treats as an error).</summary>
        public static Dictionary<string, object> Departure(Dictionary<string, object> args)
        {
            var lines = new List<string>();
            int airborne = 0, fleet = 0;
            foreach (Aircraft aircraft in UnityEngine.Object.FindObjectsOfType<Aircraft>())
            {
                if (aircraft == null || aircraft.disabled) continue;
                fleet++;
                float height = aircraft.transform.position.y;
                Pilot pilot = aircraft.pilots != null && aircraft.pilots.Length > 0 ? aircraft.pilots[0] : null;
                string state = pilot != null && pilot.currentState != null ? pilot.currentState.stateDisplayName : "?";
                bool flying = height > 50f || (aircraft.speed > 40f && height > 12f);
                if (flying) airborne++;
                lines.Add((aircraft.unitName ?? "?") + ": " + (flying ? "AIRBORNE" : "ground") + " at " + height.ToString("F0") + " m, " +
                          aircraft.speed.ToString("F0") + " m/s, " + state);
            }
            Plugin.Logger?.LogInfo("[Maps] Departure: " + airborne + " of " + fleet + " airborne" + (lines.Count > 0 ? " — " + string.Join(" | ", lines) : ""));
            return new Dictionary<string, object> { ["airborne"] = airborne, ["fleet"] = fleet, ["list"] = string.Join(" | ", lines) };
        }

        /// <summary>The donor vanilla airstrip that MapBuilder copies, taken apart for the airfield work: every child
        /// of every vanilla land airbase with its transform, meshes (name, vertices, bounds in metres) and materials
        /// (shader, textures, tiling), written to a JSON file. Nothing is spawned or changed. Both the source
        /// prefabs AND the live scene instances are dumped — the game bakes some meshes at load, and only the live
        /// ones carry what the shaders actually read.</summary>
        public static Dictionary<string, object> Donor(Dictionary<string, object> args)
        {
            string path = args != null && args.TryGetValue("path", out object given) && given != null ? given.ToString() : Path.Combine(Path.GetTempPath(), "bs_map_donor.json");
            MapSettingsManager manager = MapSettingsManager.i;
            var airbases = new JArray();
            if (manager != null)
                foreach (MapSettings map in manager.Maps.Where(m => m != null && m.Prefab != null && !MapGround.IsOurs(m.Prefab)).Select(m => m.Prefab))
                    foreach (Airbase airbase in map.GetComponentsInChildren<Airbase>(true))
                    {
                        if (airbase == null) continue;
                        airbases.Add(new JObject { ["map"] = map.name, ["name"] = airbase.name, ["children"] = Parts(airbase.transform) });
                    }
            var live = new JArray();
            foreach (Airbase airbase in Object.FindObjectsOfType<Airbase>())
            {
                if (airbase == null) continue;
                MapSettings settings = null;
                for (Transform up = airbase.transform.parent; up != null && settings == null; up = up.parent)
                    settings = up.GetComponent<MapSettings>();
                if (settings != null && MapGround.IsOurs(settings)) continue;
                live.Add(new JObject { ["name"] = airbase.name, ["children"] = Parts(airbase.transform) });
            }
            File.WriteAllText(path, new JObject { ["prefabs"] = airbases, ["live"] = live }.ToString(Formatting.None));
            Plugin.Logger?.LogInfo("[Maps] Donor: " + airbases.Count + " vanilla airbases, " + live.Count + " live -> " + path);
            return new Dictionary<string, object> { ["ok"] = true, ["path"] = path, ["airbases"] = airbases.Count, ["live"] = live.Count };
        }

        /// <summary>Scene performance facts: every renderer grouped by material — draw calls, triangles,
        /// vertices — plus LOD groups and the totals, written to a JSON file. The measurement step of the
        /// game-optimization discipline: whole-scene limits matter more than single assets.</summary>
        public static Dictionary<string, object> Perf(Dictionary<string, object> args)
        {
            string path = args != null && args.TryGetValue("path", out object given) && given != null ? given.ToString() : Path.Combine(Path.GetTempPath(), "bs_map_perf.json");
            var byMaterial = new Dictionary<string, JObject>();
            long triangles = 0, vertices = 0;
            int renderers = 0, lodGroups = 0, culled = 0;
            foreach (MeshRenderer renderer in Object.FindObjectsOfType<MeshRenderer>())
            {
                if (renderer == null || !renderer.gameObject.activeInHierarchy) continue;
                renderers++;
                if (renderer.GetComponent<LODGroup>() != null) lodGroups++;
                MeshFilter filter = renderer.GetComponent<MeshFilter>();
                Mesh mesh = filter != null ? filter.sharedMesh : null;
                if (mesh == null) { culled++; continue; }
                int tris = 0;
                for (int s = 0; s < mesh.subMeshCount; s++) tris += (int)mesh.GetIndexCount(s) / 3;
                triangles += tris;
                vertices += mesh.vertexCount;
                Material[] mats = renderer.sharedMaterials;
                for (int s = 0; s < mats.Length; s++)
                {
                    Material m = mats[s];
                    if (m == null) continue;
                    string key = m.name + "|" + (m.shader != null ? m.shader.name : "?");
                    if (!byMaterial.TryGetValue(key, out JObject row))
                        byMaterial[key] = row = new JObject { ["name"] = m.name, ["shader"] = m.shader != null ? m.shader.name : "?",
                                                              ["draws"] = 0, ["tris"] = 0L, ["verts"] = 0L, ["instances"] = 0 };
                    row["draws"] = row.Value<int>("draws") + 1;
                    row["tris"] = row.Value<long>("tris") + (s < mesh.subMeshCount ? (int)mesh.GetIndexCount(s) / 3 : 0);
                    row["verts"] = row.Value<long>("verts") + (s == 0 ? mesh.vertexCount : 0);
                    row["instances"] = row.Value<int>("instances") + 1;
                }
            }
            var instances = Object.FindObjectOfType<MapSceneryInstances>();
            bool sceneryReady = instances != null && instances.Ready;
            int sceneryInstances = instances != null ? instances.InstanceCount : 0;
            long sceneryBufferBytes = instances != null ? instances.AllocatedBytes : 0L;
            int sceneryVisible = 0;
            if (sceneryReady && args != null && args.TryGetValue("visibleInstances", out object measure) && Convert.ToBoolean(measure))
                instances.TryGetVisibleCount(out sceneryVisible);
            var root = new JObject
            {
                ["renderers"] = renderers, ["lodGroups"] = lodGroups, ["noMesh"] = culled,
                ["triangles"] = triangles, ["vertices"] = vertices,
                ["sceneryReady"] = sceneryReady, ["sceneryInstances"] = sceneryInstances,
                ["sceneryBufferBytes"] = sceneryBufferBytes, ["sceneryVisible"] = sceneryVisible,
                ["materials"] = new JArray(byMaterial.Values.OrderByDescending(r => r.Value<long>("tris")).Take(40)),
            };
            File.WriteAllText(path, root.ToString(Formatting.None));
            Plugin.Logger?.LogInfo("[Maps] Perf: " + renderers + " renderers, " + triangles + " triangles, " + byMaterial.Count + " materials -> " + path);
            return new Dictionary<string, object>
            {
                ["ok"] = true, ["path"] = path, ["renderers"] = renderers, ["triangles"] = triangles,
                ["sceneryReady"] = sceneryReady, ["sceneryInstances"] = sceneryInstances,
                ["sceneryBufferBytes"] = sceneryBufferBytes, ["sceneryVisible"] = sceneryVisible,
            };
        }

        /// <summary>A mesh's vertex colours, or null when it has none or was imported without Read/Write — a
        /// non-readable mesh answers every runtime attribute query with an exception, so its data is read from
        /// the serialized assets instead (donor_airfield.py).</summary>
        private static Color32[] ReadColours(Mesh mesh)
        {
            if (!mesh.isReadable) return null;
            try { return mesh.colors32; } catch { return null; }
        }

        /// <summary>A mesh's first texture-coordinate channel as Vector2s (null when it has none), for readable
        /// meshes only — see <see cref="ReadColours"/>.</summary>
        private static Vector2[] ReadUv(Mesh mesh, out int channel, out int dims)
        {
            channel = -1; dims = 0;
            if (!mesh.isReadable) return null;
            try
            {
                Vector2[] uv = mesh.uv;
                if (uv.Length > 0) { channel = 0; dims = 2; return uv; }
                for (int c = 1; c <= 3; c++)
                {
                    var list = new List<Vector4>();
                    mesh.GetUVs(c, list);
                    if (list.Count == mesh.vertexCount && list.Count > 0)
                    {
                        channel = c; dims = 4;
                        return list.Select(v => new Vector2(v.x, v.y)).ToArray();
                    }
                }
                return null;
            }
            catch { return null; }
        }

        /// <summary>A mesh's vertex positions, for readable meshes only — see <see cref="ReadColours"/>.</summary>
        private static Vector3[] ReadVertices(Mesh mesh)
        {
            if (!mesh.isReadable) return null;
            try { return mesh.vertices; } catch { return null; }
        }

        /// <summary>One submesh's triangle indices, for readable meshes only — see <see cref="ReadColours"/>.</summary>
        private static int[] ReadTriangles(Mesh mesh, int submesh)
        {
            if (mesh.isReadable) return mesh.GetTriangles(submesh);
            return new int[0];
        }

        /// <summary>One node of the hierarchy: its name, local position and rotation, its renderer's bounds and
        /// materials (with every shader property the material carries), its mesh's vertex-colour and UV facts,
        /// and its children. The leaves that matter are the meshes; their bounds say what they are, their
        /// colours and UVs say how the game paints them.</summary>
        private static JObject Parts(Transform at)
        {
            var node = new JObject { ["name"] = at.name, ["pos"] = new JArray(at.localPosition.x, at.localPosition.y, at.localPosition.z),
                                     ["rot"] = new JArray(at.localEulerAngles.x, at.localEulerAngles.y, at.localEulerAngles.z) };
            var filter = at.GetComponent<MeshFilter>();
            var render = at.GetComponent<MeshRenderer>();
            if (filter != null && render != null && filter.sharedMesh != null)
            {
                Mesh mesh = filter.sharedMesh;
                // The base meshes carry no UVs and no colours: those live in the renderer's additional
                // vertex streams, a second mesh of per-vertex attributes over the same vertex count.
                Mesh streams = render.additionalVertexStreams;
                if (streams != null) node["streams"] = new JObject { ["mesh"] = streams.name, ["vertices"] = streams.vertexCount };
                Bounds bounds = mesh.bounds;
                var meshNode = new JObject { ["name"] = mesh.name, ["vertices"] = mesh.vertexCount,
                                             ["bounds"] = new JArray(bounds.size.x, bounds.size.y, bounds.size.z),
                                             ["submeshes"] = mesh.subMeshCount };
                var colours = mesh.isReadable ? mesh.colors32 : ReadColours(mesh);   // how the game paints markings: vertex colour + alpha clip
                if (colours != null && colours.Length > 0)
                {
                    Color32 lo = colours[0], hi = colours[0];
                    byte alphaSeen = 0;
                    foreach (Color32 c in colours)
                    {
                        if (c.r < lo.r) lo.r = c.r; if (c.g < lo.g) lo.g = c.g; if (c.b < lo.b) lo.b = c.b; if (c.a < lo.a) lo.a = c.a;
                        if (c.r > hi.r) hi.r = c.r; if (c.g > hi.g) hi.g = c.g; if (c.b > hi.b) hi.b = c.b; if (c.a > hi.a) hi.a = c.a;
                        alphaSeen |= c.a;
                    }
                    meshNode["colour"] = new JObject { ["min"] = new JArray(lo.r, lo.g, lo.b, lo.a), ["max"] = new JArray(hi.r, hi.g, hi.b, hi.a),
                                                       ["alphaValues"] = alphaSeen };
                }
                else meshNode["colour"] = "none";
                Vector2[] flat = ReadUv(mesh, out int used, out int dims);
                if (flat != null && flat.Length > 0)
                {
                    Vector2 lo = flat[0], hi = flat[0];
                    foreach (Vector2 uv in flat)
                    {
                        lo.x = Math.Min(lo.x, uv.x); lo.y = Math.Min(lo.y, uv.y);
                        hi.x = Math.Max(hi.x, uv.x); hi.y = Math.Max(hi.y, uv.y);
                    }
                    meshNode["uv0"] = new JArray(lo.x, lo.y, hi.x, hi.y);
                }
                // Which UV channel the game's meshes carry, and the atlas rects they paint from: every distinct
                // quantised UV rectangle of every submesh with the world size of one instance — the glyph inventory
                // an airfield generator composes runways from. A non-readable mesh answers every attribute query
                // with an empty array (why the first dumps saw no UVs at all); its data is acquired read-only.
                meshNode["uvChannel"] = used;
                meshNode["uvDims"] = dims;
                if (used >= 0 && mesh.subMeshCount <= 8 && mesh.vertexCount <= 40000)
                {
                    Vector3[] points = ReadVertices(mesh);
                    var perSubmesh = new JArray();
                    for (int s = 0; s < mesh.subMeshCount; s++)
                    {
                        int[] tris = ReadTriangles(mesh, s);
                        var islands = new Dictionary<string, JObject>();
                        for (int t = 0; t + 2 < tris.Length; t += 3)
                        {
                            Vector2 a = flat[tris[t]], b = flat[tris[t + 1]], c2 = flat[tris[t + 2]];
                            float x0 = Math.Min(a.x, Math.Min(b.x, c2.x)), x1 = Math.Max(a.x, Math.Max(b.x, c2.x));
                            float y0 = Math.Min(a.y, Math.Min(b.y, c2.y)), y1 = Math.Max(a.y, Math.Max(b.y, c2.y));
                            string key = Mathf.RoundToInt(x0 * 256) + "_" + Mathf.RoundToInt(y0 * 256) + "_" + Mathf.RoundToInt(x1 * 256) + "_" + Mathf.RoundToInt(y1 * 256);
                            if (!islands.TryGetValue(key, out JObject island))
                            {
                                Vector3 p0 = points[tris[t]], p1 = points[tris[t + 1]], p2 = points[tris[t + 2]];
                                Vector3 lo3 = Vector3.Min(p0, Vector3.Min(p1, p2)), hi3 = Vector3.Max(p0, Vector3.Max(p1, p2));
                                islands[key] = island = new JObject { ["uv"] = new JArray(x0, y0, x1, y1),
                                    ["size"] = new JArray(hi3.x - lo3.x, hi3.y - lo3.y, hi3.z - lo3.z), ["at"] = new JArray(lo3.x, lo3.y, lo3.z) };
                                island["count"] = 0;
                            }
                            island["count"] = island.Value<int>("count") + 1;
                        }
                        if (islands.Count > 0)
                            perSubmesh.Add(new JObject { ["submesh"] = s, ["islands"] = new JArray(islands.Values.OrderByDescending(o => o.Value<int>("count"))) });
                    }
                    if (perSubmesh.Count > 0) meshNode["islands"] = perSubmesh;
                }
                node["mesh"] = meshNode;
                var materials = new JArray();
                Material[] shared = render.sharedMaterials;
                for (int s = 0; s < shared.Length; s++)
                {
                    Material material = shared[s];
                    if (material == null) continue;
                    var textures = new JArray();
                    foreach (string slot in material.GetTexturePropertyNames())
                    {
                        Texture bound = material.GetTexture(slot);
                        if (bound == null) continue;
                        textures.Add(new JObject { ["slot"] = slot, ["name"] = bound.name, ["size"] = new JArray(bound.width, bound.height),
                                                   ["scale"] = new JArray(material.GetTextureScale(slot).x, material.GetTextureScale(slot).y),
                                                   ["offset"] = new JArray(material.GetTextureOffset(slot).x, material.GetTextureOffset(slot).y) });
                    }
                    var scalars = new JArray();
                    Shader shader = material.shader;
                    for (int i = 0; shader != null && i < shader.GetPropertyCount(); i++)
                    {
                        string one = shader.GetPropertyName(i);
                        ShaderPropertyType kind = shader.GetPropertyType(i);
                        if (kind == ShaderPropertyType.Float || kind == ShaderPropertyType.Range)
                            scalars.Add(new JArray(one, material.GetFloat(one)));
                        else if (kind == ShaderPropertyType.Vector)
                            scalars.Add(new JArray(one, material.GetVector(one).x, material.GetVector(one).y, material.GetVector(one).z, material.GetVector(one).w));
                    }
                    materials.Add(new JObject { ["name"] = material.name, ["shader"] = shader != null ? shader.name : "",
                                                ["submesh"] = s, ["textures"] = textures, ["scalars"] = scalars,
                                                ["keywords"] = string.Join(",", material.shaderKeywords) });
                }
                node["materials"] = materials;
            }
            var children = new JArray();
            for (int i = 0; i < at.childCount; i++) children.Add(Parts(at.GetChild(i)));
            if (children.Count > 0) node["children"] = children;
            return node;
        }

        /// <summary>Args: <c>path</c>, the JSON file to write (default: bs_map_catalogue.json in the temp folder).</summary>
        public static Dictionary<string, object> Catalogue(Dictionary<string, object> args)
        {
            string path = args != null && args.TryGetValue("path", out object given) && given != null ? given.ToString() : Path.Combine(Path.GetTempPath(), "bs_map_catalogue.json");
            var root = new JObject();
            Encyclopedia book = Encyclopedia.i;
            var defs = new JArray();
            if (book != null)
                foreach (UnitDefinition def in book.buildings.Cast<UnitDefinition>().Concat(book.scenery))
                {
                    if (def == null) continue;
                    GameObject prefab = def.unitPrefab;
                    defs.Add(new JObject
                    {
                        ["key"] = def.jsonKey, ["name"] = def.unitName, ["kind"] = def is BuildingDefinition b ? b.buildingType.ToString() : "SCENERY",
                        ["length"] = def.length, ["width"] = def.width, ["height"] = def.height,
                        ["parts"] = prefab == null ? "" : string.Join(",", prefab.GetComponentsInChildren<MonoBehaviour>(true).Where(c => c != null)
                            .Select(c => c.GetType().Name).Distinct().Where(n => n == "Hangar" || n == "Factory" || n == "VehicleDepot" || n == "Radar" || n == "Turret" || n == "Building")),
                    });
                }
            root["definitions"] = defs;

            var bases = new JArray();
            MapSettingsManager manager = MapSettingsManager.i;
            if (manager != null)
                foreach (MapSettings map in manager.Maps.Where(m => m != null && m.Prefab != null && !MapGround.IsOurs(m.Prefab)).Select(m => m.Prefab))
                    foreach (Airbase airbase in map.GetComponentsInChildren<Airbase>(true))
                    {
                        var runways = new JArray();
                        foreach (Airbase.Runway runway in airbase.runways ?? new Airbase.Runway[0])
                            if (runway != null && runway.Start != null && runway.End != null)
                                runways.Add(new JArray(runway.Start.position.x, runway.Start.position.y, runway.Start.position.z, runway.End.position.x, runway.End.position.y, runway.End.position.z));
                        SavedAirbase saved = airbase.SavedAirbase;
                        bases.Add(new JObject
                        {
                            ["map"] = map.name, ["object"] = airbase.name, ["unique"] = saved != null ? saved.UniqueName : "", ["display"] = saved != null ? saved.DisplayName : "",
                            ["position"] = new JArray(airbase.transform.position.x, airbase.transform.position.y, airbase.transform.position.z),
                            ["center"] = saved != null ? new JArray(saved.Center.x, saved.Center.y, saved.Center.z) : new JArray(), ["runways"] = runways,
                        });
                    }
            root["airbases"] = bases;

            var missions = new JArray();
            MissionGroup.Init();
            foreach (MissionGroup group in new MissionGroup[] { MissionGroup.BuiltIn, MissionGroup.Default })
                foreach (MissionKey key in group.GetMissions())
                {
                    try
                    {
                        if (!group.TryGetJson(key, out string json) || string.IsNullOrEmpty(json)) continue;
                        JObject mission;
                        using (var reader = new JsonTextReader(new StringReader(json)) { DateParseHandling = DateParseHandling.None }) mission = JObject.Load(reader);
                        missions.Add(new JObject { ["name"] = key.Key, ["group"] = group.Name, ["map"] = mission["MapKey"], ["airbases"] = mission["airbases"], ["buildings"] = mission["buildings"] });
                    }
                    catch (Exception error) when (error is JsonException || error is InvalidOperationException || error is IOException)
                    {
                        missions.Add(new JObject { ["name"] = key.Key, ["error"] = error.Message });
                    }
                }
            root["missions"] = missions;
            File.WriteAllText(path, root.ToString(Formatting.None));
            Plugin.Logger?.LogInfo("[Maps] Catalogue: " + defs.Count + " definitions, " + bases.Count + " airbases, " + missions.Count + " missions -> " + path);
            return new Dictionary<string, object> { ["ok"] = true, ["path"] = path, ["definitions"] = defs.Count, ["airbases"] = bases.Count, ["missions"] = missions.Count };
        }
    }
}
