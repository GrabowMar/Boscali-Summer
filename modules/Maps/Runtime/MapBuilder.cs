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
        private static readonly AccessTools.FieldRef<Airbase.Runway, float> runwayWidth = AccessTools.FieldRefAccess<Airbase.Runway, float>("width");
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
                List<NetworkIdentity> identities = BuildAirbases(root.transform.Find("Airfields"), entry, manager);
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

        /// <summary>Native Airbase controllers for the map's strip footprints and mission-owned hangars.</summary>
        private static List<NetworkIdentity> BuildAirbases(Transform airfields, MapEntry entry, MapSettingsManager manager)
        {
            MapManifest manifest = entry.Manifest;
            var identities = new List<NetworkIdentity>();
            MapSettings[] vanilla = manager.Maps.Where(m => m != null && m.Prefab != null && !MapGround.IsOurs(m.Prefab))
                .Select(m => m.Prefab).ToArray();
            Airbase[] donors = vanilla.SelectMany(m => m.GetComponentsInChildren<Airbase>(true))
                .Where(a => a.runways != null && a.runways.Length == 1 && a.runways[0].Start != null && a.runways[0].End != null)
                .ToArray();
            if (airfields == null || donors.Length == 0)
            {
                Plugin.Logger?.LogWarning("[Maps] No vanilla airstrip to copy: " + manifest.displayName + " has no land bases, carriers still work.");
                return identities;
            }
            var materials = new Dictionary<string, Material>();
            foreach (MapSettings map in vanilla)
                foreach (MeshRenderer part in map.GetComponentsInChildren<MeshRenderer>(true))
                    foreach (Material material in part.sharedMaterials)
                        if (material != null && !materials.ContainsKey(material.name)) materials.Add(material.name, material);

            for (int n = 0; n < manifest.strips.Length; n++)
            {
                MapManifest.Strip strip = manifest.strips[n];
                Transform pad = airfields.Find("strip_" + n);
                if (pad == null) continue;
                bool paved = strip.surface == "asphalt";
                var donor = donors.FirstOrDefault(a => a.name.Equals(strip.donor, StringComparison.OrdinalIgnoreCase));
                if (donor == null && paved) donor = donors.FirstOrDefault(a => a.name == "airbase_desert1");
                if (donor == null) donor = donors
                    .OrderBy(a => a.name.StartsWith("highwaystrip", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                    .ThenBy(a => Mathf.Abs(RunwaySpan(a.runways[0]) - strip.length)).First();

                var copy = Object.Instantiate(donor.gameObject, airfields);
                copy.name = "airbase_" + manifest.id + "_" + (n + 1);
                var airbase = copy.GetComponent<Airbase>();
                airbase.MapTower = null; // the mission registers its own tower and hangars
                var runway = airbase.runways[0];
                Vector3 forward = pad.forward;
                Vector3 along = runway.End.position - runway.Start.position;
                along.y = 0f;
                float originalLength = along.magnitude;
                float originalWidth = runway.GetWidth();
                var midpoint = new Vector3(strip.x, strip.y + .15f, strip.z);
                Quaternion turn = Quaternion.FromToRotation(along, forward);
                copy.transform.rotation = turn * copy.transform.rotation;
                copy.transform.position += midpoint - (runway.Start.position + runway.End.position) * .5f;

                float length = paved && Mathf.Abs(originalLength - strip.length) < 1f ? originalLength : strip.length;
                float width = strip.width > 0f ? strip.width : originalWidth;
                runway.Start.position = midpoint - forward * (length * .5f);
                runway.End.position = midpoint + forward * (length * .5f);
                runwayWidth(runway) = width;
                foreach (Collider collider in copy.GetComponentsInChildren<Collider>(true)) collider.enabled = false;
                bool native = paved && MapAirfieldPaving.TryBorrow(airbase, pad, entry, strip, length / originalLength, width / originalWidth);
                MapAirfieldPaving.Lights(airbase, pad, length / originalLength, width / originalWidth);
                foreach (Collider collider in pad.GetComponentsInChildren<Collider>(true)) collider.gameObject.layer = PhysicsLayers.Statics;
                DressPaving(pad, materials, native);

                // Hangars and objective buildings belong to the mission at its authored coordinates. Keep the local
                // taxi plan connecting those hangars instead of sending aircraft to the donor's off-runway apron.
                taxiRef(airbase) = Network(strip.taxi);
                Exits(airbase, runway, strip);
                Service(airbase, strip);
                Pads(airbase, strip);
                SavedAirbase saved = savedRef(airbase);
                if (saved != null)
                {
                    saved.UniqueName = manifest.displayName + " Strip " + (n + 1);
                    saved.DisplayName = string.IsNullOrEmpty(strip.name) ? saved.UniqueName : strip.name;
                    saved.faction = "";
                    saved.Tower = "";
                    saved.roads = taxiRef(airbase);
                    saved.ServicePoints = Points(strip.service);
                    saved.VerticalLandingPoints = Points(strip.pad);
                    if (saved.runways != null && saved.runways.Count > 0 && saved.runways[0] != null)
                    {
                        SavedRunway layout = saved.runways[0];
                        layout.Width = width;
                        layout.Start = new GlobalPosition(runway.Start.position);
                        layout.End = new GlobalPosition(runway.End.position);
                        layout.exitPoints = runway.exitPoints.Select(p => new GlobalPosition(p.position)).ToArray();
                    }
                }
                identities.Add(copy.GetComponent<NetworkIdentity>());
                Plugin.Logger?.LogInfo($"[Maps] {manifest.displayName} Strip {n + 1}: {donor.name}, {length:F1} x {width:F1} m, " +
                    $"{(native ? "native runway and markings" : "generated runway surface")}, local taxi and mission hangars.");
            }
            return identities;
        }

        private static float RunwaySpan(Airbase.Runway runway)
        {
            Vector3 along = runway.End.position - runway.Start.position;
            along.y = 0f;
            return along.magnitude;
        }

        private static void DressPaving(Transform pad, Dictionary<string, Material> materials, bool native)
        {
            foreach (MeshRenderer baked in pad.GetComponentsInChildren<MeshRenderer>(true))
            {
                switch (baked.name)
                {
                    case "runway":
                    case "marks":
                    case "runway1":
                    case "runway_markings":
                    case "runway_skidmarks":
                    case "asphalt_dirtedge":
                        baked.enabled = !native;
                        if (!native && materials.TryGetValue(baked.name, out Material surface)) baked.sharedMaterial = surface;
                        break;
                    case "apron":
                    case "taxi":
                    case "asphalt":
                        if (materials.TryGetValue("asphalt", out Material asphalt)) baked.sharedMaterial = asphalt;
                        break;
                }
            }
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

        /// <summary>Replaces the donor's runway exits with the plan's: two turn-offs down the length for whichever
        /// way the rollout runs, two off the ends, each turned to the heading the generator gave it.</summary>
        private static void Exits(Airbase airbase, Airbase.Runway runway, MapManifest.Strip strip)
        {
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

        /// <summary>Service locations come from the local apron. Borrow the donor's first orientation.</summary>
        private static void Service(Airbase airbase, MapManifest.Strip strip)
        {
            Transform[] original = serviceRef(airbase) ?? Array.Empty<Transform>();
            Transform first = original.FirstOrDefault(p => p != null);
            Quaternion heading = first != null ? first.rotation : airbase.transform.rotation;
            var points = new Transform[(strip.service?.Length ?? 0) / 3];
            for (int i = 0; i < points.Length; i++)
            {
                Transform point = new GameObject("service_" + i).transform;
                point.SetParent(airbase.transform, false);
                point.SetPositionAndRotation(new Vector3(strip.service[i * 3], strip.service[i * 3 + 1], strip.service[i * 3 + 2]), heading);
                points[i] = point;
            }
            serviceRef(airbase) = points;
        }

        /// <summary>The vertical pad the VTOLs come down on, where the generator stood the Helipad.</summary>
        private static void Pads(Airbase airbase, MapManifest.Strip strip)
        {
            airbase.verticalLandingPoints = Array.Empty<Airbase.VerticalLandingPoint>();
            if (strip.pad == null || strip.pad.Length < 3) return;
            var pad = Airbase.VerticalLandingPoint.FromSaved(airbase, new GlobalPosition(strip.pad[0], strip.pad[1], strip.pad[2]));
            if (strip.pad.Length >= 4) pad.point.rotation = Quaternion.Euler(0f, strip.pad[3], 0f);
            airbase.verticalLandingPoints = new[] { pad };
        }

    }
}
