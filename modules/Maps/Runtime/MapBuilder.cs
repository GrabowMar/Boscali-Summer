using System;
using System.Collections.Generic;
using System.Linq;
using BoscaliSummer.Modules.Maps.Domain;
using HarmonyLib;
using Mirage;
using NuclearOption.SavedMission;
using NuclearOption.SceneLoading;
using RoadPathfinding;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace BoscaliSummer.Modules.Maps.Runtime
{
    /// <summary>
    /// Turns a baked map (engine types only) into the inactive template the game instantiates as its map prefab: adds
    /// <see cref="MapSettings"/> and <see cref="NetworkMap"/>, marks the terrain, clones a vanilla airstrip onto each strip
    /// pad, lays the sea lanes and places the scenery. Everything game-typed is made here, at load, from today's game
    /// classes, so a game update cannot leave stale serialized data in the bundle. One map is live at a time, so every
    /// map uses the same network prefix.
    /// </summary>
    internal static class MapBuilder
    {
        private const int MapPrefix = 77; // the high byte of this map's network object ids; vanilla maps use small numbers
        private static readonly AccessTools.FieldRef<MapSettings, object> musicRef = AccessTools.FieldRefAccess<MapSettings, object>("factionMusic");
        private static readonly AccessTools.FieldRef<Airbase, RoadNetwork> taxiRef = AccessTools.FieldRefAccess<Airbase, RoadNetwork>("taxiNetwork");
        private static readonly AccessTools.FieldRef<Airbase, Transform[]> serviceRef = AccessTools.FieldRefAccess<Airbase, Transform[]>("servicePoints");
        private static readonly AccessTools.FieldRef<Airbase, SavedAirbase> savedRef = AccessTools.FieldRefAccess<Airbase, SavedAirbase>("airbaseSettings");
        private static GameObject holder;

        public static MapSettings Build(MapEntry entry, MapSettingsManager manager)
        {
            MapManifest manifest = entry.Manifest;
            try
            {
                MapSettings donor = manager.Maps.Where(m => m != null && m.Prefab != null && !MapGround.IsOurs(m.Prefab)).Select(m => m.Prefab).FirstOrDefault();
                var asset = entry.Bundle.LoadAsset<GameObject>("MapRoot");
                if (donor == null || asset == null)
                {
                    Plugin.Logger?.LogError("[Maps] " + manifest.displayName + " cannot be built: " +
                        (donor == null ? "the game has no map to borrow from." : "the bundle has no MapRoot."));
                    return null;
                }
                if (holder == null)
                {
                    holder = new GameObject("BoscaliMapTemplates");
                    holder.SetActive(false);
                    Object.DontDestroyOnLoad(holder);
                }
                GameObject root = Object.Instantiate(asset, holder.transform);
                root.name = manifest.prefabName;
                root.SetActive(false); // the game activates its copy after it has registered the map's network objects

                var settings = root.AddComponent<MapSettings>();
                Fill(settings, entry, donor);
                int chunks = MarkTerrain(root.transform.Find("Terrain"), manager);
                MapGround.Dress(root.transform.Find("Terrain"), entry, manager);
                List<NetworkIdentity> identities = BuildAirbases(root.transform.Find("Airfields"), manifest, manager);
                NetworkIdentity towns = MapTowns.Begin(root.transform, manager);
                if (towns != null) identities.Add(towns);
                int strips = identities.Count - (towns != null ? 1 : 0);
                var network = root.AddComponent<NetworkMap>();
                network.MapPrefix = MapPrefix;
                network.NetworkObjects = identities.ToArray();
                settings.NetworkMap = network;
                int lakes = Water(root.transform.Find("Water"));
                MapTowns.DressRoads(root.transform.Find("Roads"), manager);
                MapTowns.DressPlots(root.transform.Find("Plots"), manager);
                int scenery = MapScenery.Place(root.transform, entry);
                int buildings = MapTowns.End();
                Plugin.Logger?.LogInfo("[Maps] Built " + manifest.displayName + ": " + chunks + " terrain tiles, " + strips +
                    " airstrips, " + (settings.SeaLanes != null ? settings.SeaLanes.RoadNetwork.roads.Count : 0) + " sea lane legs, " + scenery + " scenery items, " + buildings + " of them the game's buildings" +
                    (lakes > 0 ? ", " + lakes + " lakes" : ""));
                return settings;
            }
            catch (Exception error)
            {
                Plugin.Logger?.LogError("[Maps] " + manifest.displayName + " could not be built: " + error);
                return null;
            }
        }

        /// <summary>Lake surfaces are looks only (the game's water is the sea plane at 0 m): they cast no shadow, and a
        /// collider the bake may have left would make a lake solid, so it is switched off.</summary>
        private static int Water(Transform water)
        {
            if (water == null) return 0;
            foreach (Renderer lake in water.GetComponentsInChildren<Renderer>(true)) lake.shadowCastingMode = ShadowCastingMode.Off;
            foreach (Collider solid in water.GetComponentsInChildren<Collider>(true)) solid.enabled = false;
            return water.childCount;
        }

        private static void Fill(MapSettings settings, MapEntry entry, MapSettings donor)
        {
            MapManifest m = entry.Manifest;
            settings.MapSize = new Vector2(m.sizeX, m.sizeY);
            settings.GridSizeX = m.gridX;
            settings.GridSizeY = m.gridY;
            settings.OffsetX = m.offsetX;
            settings.OffsetY = m.offsetY;
            settings.Latitude = m.latitude;
            settings.OceanBasecolor = entry.Bundle.LoadAsset<Texture2D>("ocean_color");
            settings.OceanDepthmap = entry.Bundle.LoadAsset<Texture2D>("ocean_depth");
            settings.TerrainColorMap = entry.Bundle.LoadAsset<Texture2D>("colormap");
            settings.MapImage = entry.Details.MapImage; // the bundle's chart since MapCatalog opened the bundle
            settings.CameraPositionRotation = new PositionRotation
            {
                Position = new GlobalPosition(m.camera.x, m.camera.y, m.camera.z),
                Rotation = Quaternion.Euler(m.camera.pitch, m.camera.yaw, m.camera.roll),
            };
            var probe = new GameObject("ReflectionProbePoint");
            probe.transform.SetParent(settings.transform, false);
            probe.transform.localPosition = new Vector3(0f, 200f, 0f);
            settings.ReflectionProbePoint = probe.transform;
            musicRef(settings) = musicRef(donor); // the factions' start, tactical and strategic music
            settings.SeaLanes = SeaLanes(m);
            settings.RoadNetwork = Roads(m);
            entry.Owned.Add(settings.RoadNetwork);
            entry.Owned.Add(settings.SeaLanes);
        }

        /// <summary>One road per lane leg: the game only joins roads at their end points, so every lane point becomes a node.</summary>
        private static RoadNetworkSO SeaLanes(MapManifest manifest)
        {
            var asset = ScriptableObject.CreateInstance<RoadNetworkSO>();
            asset.hideFlags = HideFlags.DontUnloadUnusedAsset;
            asset.RoadNetwork = new RoadNetwork();
            foreach (MapManifest.Lane lane in manifest.lanes ?? new MapManifest.Lane[0])
                for (int i = 0; i + 3 < lane.points.Length; i += 2)
                {
                    var road = new Road();
                    road.AddPoint(new GlobalPosition(lane.points[i], 0f, lane.points[i + 1]));
                    road.AddPoint(new GlobalPosition(lane.points[i + 2], 0f, lane.points[i + 3]));
                    road.CalcLength();
                    asset.RoadNetwork.roads.Add(road);
                }
            return asset;
        }

        /// <summary>The map's roads for the game's ground units: one road per leg, like the sea lanes, so roads join
        /// wherever they share a point.</summary>
        private static RoadNetworkSO Roads(MapManifest manifest)
        {
            var asset = ScriptableObject.CreateInstance<RoadNetworkSO>();
            asset.hideFlags = HideFlags.DontUnloadUnusedAsset;
            asset.RoadNetwork = new RoadNetwork();
            foreach (MapManifest.Way way in manifest.roads ?? new MapManifest.Way[0])
                for (int i = 0; way != null && way.points != null && i + 5 < way.points.Length; i += 3)
                {
                    var road = new Road();
                    road.AddPoint(new GlobalPosition(way.points[i], way.points[i + 1], way.points[i + 2]));
                    road.AddPoint(new GlobalPosition(way.points[i + 3], way.points[i + 4], way.points[i + 5]));
                    road.CalcLength();
                    asset.RoadNetwork.roads.Add(road);
                }
            return asset;
        }

        /// <summary>The game knows ground by its physics material: grass, tree scatter and landing-gear sink all test for it.</summary>
        private static int MarkTerrain(Transform terrain, MapSettingsManager manager)
        {
            if (terrain == null || terrain.childCount == 0) throw new InvalidOperationException("the bundle has no Terrain chunks");
            PhysicMaterial ground = GameAssets.i != null ? GameAssets.i.terrainMaterial : null;
            if (ground == null) throw new InvalidOperationException("the game's terrain physics material was not found");
            foreach (Transform chunk in terrain)
            {
                foreach (Transform part in chunk.GetComponentsInChildren<Transform>(true)) part.gameObject.layer = PhysicsLayers.Statics;
                foreach (MeshCollider collider in chunk.GetComponentsInChildren<MeshCollider>(true)) collider.sharedMaterial = ground;
            }
            return terrain.childCount;
        }

        /// <summary>
        /// A strip is a copy of a vanilla airstrip turned and moved so its runway lies on the map's flattened pad —
        /// but nothing else of the donor survives: its paved mesh is hidden (the bake paves the airfield the
        /// generator planned) and its taxi routes, runway exits, service points and vertical pad are REPLACED by
        /// that plan, so what the AI taxis over is what is drawn and reaches where the hangars stand. Hangars come
        /// from the mission, as on any map: hangar_med, shelter1, revetment1 and the Helipad all carry the game's
        /// Hangar. A map has two to four strips; whatever the surface (asphalt, gravel, ice), the baked slab is the
        /// runway and rolls like a paved one.
        /// </summary>
        private static List<NetworkIdentity> BuildAirbases(Transform airfields, MapManifest manifest, MapSettingsManager manager)
        {
            var identities = new List<NetworkIdentity>();
            Airbase donor = manager.Maps.Where(m => m != null && m.Prefab != null && !MapGround.IsOurs(m.Prefab))
                .SelectMany(m => m.Prefab.GetComponentsInChildren<Airbase>(true))
                .Where(a => a.runways != null && a.runways.Length == 1 && a.runways[0].Start != null && a.runways[0].End != null)
                .OrderBy(a => a.name.IndexOf("airstrip", StringComparison.OrdinalIgnoreCase) >= 0 ? 0 : 1)
                .ThenBy(a => Vector3.Distance(a.runways[0].Start.position, a.runways[0].End.position))
                .FirstOrDefault();
            if (airfields == null || donor == null)
            {
                Plugin.Logger?.LogWarning("[Maps] No vanilla airstrip to copy: " + manifest.displayName + " has no land bases, carriers still work.");
                return identities;
            }
            // The game's own airfield surfaces, by name from every vanilla field (the materials are shared
            // assets): runway1 for the runway, asphalt for the taxiway and apron, and the marking set — the
            // painted lines, the skidmarks, the dirt edges — for the donor meshes the bake emits. A strip the
            // game has no surface for keeps the bake's plain colours.
            var surfaces = new Dictionary<string, Material>();
            foreach (MapSettings map in manager.Maps.Where(m => m != null && m.Prefab != null && !MapGround.IsOurs(m.Prefab)).Select(m => m.Prefab))
                foreach (MeshRenderer part in map.GetComponentsInChildren<MeshRenderer>(true))
                    foreach (Material one in part.sharedMaterials)
                        if (one != null && !surfaces.ContainsKey(one.name)) surfaces[one.name] = one;
            surfaces.TryGetValue("asphalt", out Material pavedLook);
            surfaces.TryGetValue("runway1", out Material runwayLook);
            surfaces.TryGetValue("runway_markings", out Material markingsLook);
            surfaces.TryGetValue("runway_skidmarks", out Material skidLook);
            surfaces.TryGetValue("asphalt_dirtedge", out Material edgeLook);
            if (runwayLook == null) runwayLook = pavedLook;
            for (int n = 0; n < manifest.strips.Length; n++)
            {
                MapManifest.Strip strip = manifest.strips[n];
                Transform pad = airfields.Find("strip_" + n);
                if (pad == null) continue;
                GameObject copy = Object.Instantiate(donor.gameObject, airfields);
                copy.name = "airbase_" + manifest.id + "_" + (n + 1);
                var airbase = copy.GetComponent<Airbase>();
                airbase.MapTower = null; // a reference into the vanilla map's prefab, not into this copy
                Airbase.Runway runway = airbase.runways[0];
                Vector3 along = runway.End.position - runway.Start.position;
                Quaternion turn = Quaternion.FromToRotation(new Vector3(along.x, 0f, along.z), Quaternion.Euler(0f, strip.heading, 0f) * Vector3.forward);
                copy.transform.rotation = turn * copy.transform.rotation;
                Vector3 middle = (runway.Start.position + runway.End.position) * .5f;
                copy.transform.position += new Vector3(strip.x, strip.y + .15f, strip.z) - middle;

                // The donor's own paved mesh is the aprons and taxiways of its home field, in its own layout: ours
                // are baked. Its COLLIDERS go too — hidden meshes still collide, and an invisible wall where the
                // donor's apron stood is what a landing AI taxis into at 60 km/h. Only its edge lights stay (they
                // ride its runway, which lies on ours, and a strip without night lights is a hole in the dark).
                foreach (Collider solid in copy.GetComponentsInChildren<Collider>(true)) solid.enabled = false;
                foreach (Renderer mesh in copy.GetComponentsInChildren<Renderer>(true))
                    mesh.enabled = mesh.name == "lights" || mesh.sharedMaterial != null && mesh.sharedMaterial.name == "MeshLight";

                // The airfield's plan, written by the generator: the AI's taxi network, the runway exits, the
                // service points and the vertical pad — all in map metres, on the flat pad. The donor's routes
                // were drawn for the donor's own apron and had nothing to do with where our hangars stand.
                taxiRef(airbase) = Network(strip.taxi);
                SavedAirbase saved = savedRef(airbase);
                if (saved != null)
                {
                    saved.UniqueName = manifest.displayName + " Strip " + (n + 1);
                    saved.DisplayName = string.IsNullOrEmpty(strip.name) ? saved.UniqueName : strip.name;
                    saved.faction = "";
                    saved.Tower = "";
                    saved.roads = Network(strip.taxi);
                    saved.ServicePoints = Points(strip.service);
                    saved.VerticalLandingPoints = Points(PadOf(strip));
                    if (saved.runways != null && saved.runways.Count > 0 && saved.runways[0] != null)
                        saved.runways[0].exitPoints = Exits(strip);
                }
                Exits(airbase, runway, strip);
                Service(airbase, strip);
                Pads(airbase, strip);
                // The strip's own paving, dressed with the game's own airfield surfaces: a donor strip is the
                // game's own meshes (runway1, asphalt, the marking set), a baked strip keeps its named slabs.
                int dressed = 0;
                foreach (MeshRenderer baked in pad.GetComponentsInChildren<MeshRenderer>(true))
                {
                    if (baked == null || baked.sharedMaterial == null) continue;
                    string kind = baked.name;
                    if (kind == "runway_markings") { baked.sharedMaterial = markingsLook ?? baked.sharedMaterial; dressed++; }
                    else if (kind == "runway_skidmarks") { baked.sharedMaterial = skidLook ?? baked.sharedMaterial; dressed++; }
                    else if (kind == "asphalt_dirtedge") { baked.sharedMaterial = edgeLook ?? baked.sharedMaterial; dressed++; }
                    else if (kind == "runway1" || kind.StartsWith("runway")) { baked.sharedMaterial = runwayLook ?? baked.sharedMaterial; dressed++; }
                    else if (kind == "asphalt" || kind.StartsWith("taxi") || kind.StartsWith("apron")) { baked.sharedMaterial = pavedLook ?? baked.sharedMaterial; dressed++; }
                }
                Plugin.Logger?.LogInfo("[Maps] Strip " + (n + 1) + " of " + manifest.displayName + ": " + dressed + " paving pieces dressed with the game's own " +
                    "surfaces (runway " + (runwayLook != null ? runwayLook.name : "none") + ", apron " + (pavedLook != null ? pavedLook.name : "none") +
                    ", markings " + (markingsLook != null ? markingsLook.name : "none") + ").");
                foreach (Collider collider in pad.GetComponentsInChildren<Collider>(true)) collider.gameObject.layer = PhysicsLayers.Statics;
                identities.Add(copy.GetComponent<NetworkIdentity>());
                float room = Mathf.Max(strip.length + 300f, strip.padLength);
                if (along.magnitude > room + 20f)
                    Plugin.Logger?.LogWarning("[Maps] The copied airstrip is " + Mathf.RoundToInt(along.magnitude) + " m long; the " + strip.surface +
                        " pad of strip " + (n + 1) + " allows " + Mathf.RoundToInt(room) + " m.");
            }
            return identities;
        }

        /// <summary>One road per pair of flat x, y, z, x, y, z points: straight roads joining at their shared end
        /// points, the shape the game's road pathfinder is built for.</summary>
        private static RoadNetwork Network(float[] flat)
        {
            var network = new RoadNetwork();
            for (int i = 0; flat != null && i + 5 < flat.Length; i += 6)
            {
                var road = new Road();
                road.AddPoint(new GlobalPosition(flat[i], flat[i + 1], flat[i + 2]));
                road.AddPoint(new GlobalPosition(flat[i + 3], flat[i + 4], flat[i + 5]));
                road.UpdateBB();   // the pathfinder's spatial lookup: without it every road is nowhere
                road.CalcLength();
                network.roads.Add(road);
            }
            return network;
        }

        private static List<GlobalPosition> Points(float[] flat)
        {
            var list = new List<GlobalPosition>();
            for (int i = 0; flat != null && i + 2 < flat.Length; i += 3) list.Add(new GlobalPosition(flat[i], flat[i + 1], flat[i + 2]));
            return list;
        }

        private static float[] PadOf(MapManifest.Strip strip) => strip.pad == null ? new float[0] : strip.pad;

        /// <summary>Where a landed aircraft turns off: the first three of every four flat numbers are a position.</summary>
        private static GlobalPosition[] Exits(MapManifest.Strip strip)
        {
            var points = new List<GlobalPosition>();
            for (int i = 0; strip.exits != null && i + 2 < strip.exits.Length; i += 4)
                points.Add(new GlobalPosition(strip.exits[i], strip.exits[i + 1], strip.exits[i + 2]));
            return points.ToArray();
        }

        /// <summary>Replaces the donor's runway exits with the plan's: two turn-offs down the length for whichever
        /// way the rollout runs, two off the ends, each turned to the heading the generator gave it.</summary>
        private static void Exits(Airbase airbase, Airbase.Runway runway, MapManifest.Strip strip)
        {
            foreach (Transform point in runway.exitPoints ?? Array.Empty<Transform>()) if (point != null) Object.Destroy(point.gameObject);
            var made = new List<Transform>();
            for (int i = 0; strip.exits != null && i + 3 < strip.exits.Length; i += 4)
            {
                Transform exit = new GameObject("exit_" + made.Count).transform;
                exit.parent = airbase.transform;
                exit.position = new Vector3(strip.exits[i], strip.exits[i + 1], strip.exits[i + 2]);
                exit.rotation = Quaternion.Euler(0f, strip.exits[i + 3], 0f);
                made.Add(exit);
            }
            runway.exitPoints = made.Count > 0 ? made.ToArray() : new Transform[] { runway.Start, runway.End };
        }

        /// <summary>Where aircraft are serviced: the donor's points are moved onto the apron's own places (extras
        /// beyond the plan's are destroyed, so the count is exactly the plan's).</summary>
        private static void Service(Airbase airbase, MapManifest.Strip strip)
        {
            Transform[] points = serviceRef(airbase) ?? Array.Empty<Transform>();
            for (int i = 0; i < points.Length; i++)
            {
                if (points[i] == null) continue;
                if (strip.service == null || i * 3 + 2 >= strip.service.Length)
                {
                    Object.Destroy(points[i].gameObject);
                    points[i] = null;
                    continue;
                }
                points[i].position = new Vector3(strip.service[i * 3], strip.service[i * 3 + 1], strip.service[i * 3 + 2]);
            }
            serviceRef(airbase) = points;
        }

        /// <summary>The vertical pad the VTOLs come down on, where the generator stood the Helipad.</summary>
        private static void Pads(Airbase airbase, MapManifest.Strip strip)
        {
            foreach (Airbase.VerticalLandingPoint old in airbase.verticalLandingPoints ?? new Airbase.VerticalLandingPoint[0])
                if (old != null && old.point != null) Object.Destroy(old.point.gameObject);
            if (strip.pad == null || strip.pad.Length < 3) return;
            airbase.verticalLandingPoints = new[] { Airbase.VerticalLandingPoint.FromSaved(airbase, new GlobalPosition(strip.pad[0], strip.pad[1], strip.pad[2])) };
        }

    }
}
