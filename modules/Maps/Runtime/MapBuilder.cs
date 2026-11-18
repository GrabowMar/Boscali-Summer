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
        /// A strip is a copy of a vanilla airstrip (its paved mesh, runway ends, exits, service points and taxi routes),
        /// turned and moved so its runway lies on the map's flattened pad. Hangars come from the mission, as on any map.
        /// A map has two to four strips; whatever the surface (asphalt, gravel, ice), the baked slab is the runway and
        /// rolls like a paved one.
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
            Matrix4x4 donorPose = donor.transform.localToWorldMatrix;
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

                // Taxi routes and saved points are stored in map coordinates, not under the transform: move them the same way.
                Matrix4x4 move = copy.transform.localToWorldMatrix * donorPose.inverse;
                Move(taxiRef(airbase), move);
                SavedAirbase saved = savedRef(airbase);
                if (saved != null)
                {
                    saved.UniqueName = manifest.displayName + " Strip " + (n + 1);
                    saved.DisplayName = string.IsNullOrEmpty(strip.name) ? saved.UniqueName : strip.name;
                    saved.faction = "";
                    saved.Tower = "";
                    Move(saved.roads, move);
                    Move(saved.ServicePoints, move);
                    Move(saved.VerticalLandingPoints, move);
                }
                // The vanilla strip's own mesh is only the aprons and decals of its home field (its runway is part of that
                // map's ground), so it is hidden and the map's baked runway slab stays.
                foreach (Renderer part in copy.GetComponentsInChildren<Renderer>(true)) part.enabled = false;
                foreach (Collider part in copy.GetComponentsInChildren<Collider>(true)) part.enabled = false;
                foreach (Collider paved in pad.GetComponentsInChildren<Collider>(true)) paved.gameObject.layer = PhysicsLayers.Statics;
                identities.Add(copy.GetComponent<NetworkIdentity>());
                float room = Mathf.Max(strip.length + 300f, strip.padLength);
                if (along.magnitude > room + 20f)
                    Plugin.Logger?.LogWarning("[Maps] The copied airstrip is " + Mathf.RoundToInt(along.magnitude) + " m long; the " + strip.surface +
                        " pad of strip " + (n + 1) + " allows " + Mathf.RoundToInt(room) + " m.");
            }
            return identities;
        }

        private static void Move(RoadNetwork network, Matrix4x4 move)
        {
            if (network == null || network.roads == null) return;
            foreach (Road road in network.roads)
            {
                Move(road.points, move);
                road.UpdateBB();
                road.CalcLength();
            }
        }

        private static void Move(List<GlobalPosition> points, Matrix4x4 move)
        {
            if (points == null) return;
            for (int i = 0; i < points.Count; i++) points[i] = new GlobalPosition(move.MultiplyPoint3x4(points[i].AsVector3()));
        }
    }
}
