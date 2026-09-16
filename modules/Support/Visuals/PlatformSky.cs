using System.Collections.Generic;
using BoscaliSummer.Features.Support.Domain.Orbital;
using BoscaliSummer.Features.Support.Runtime;
using BoscaliSummer.Framework.Lifecycle;
using BoscaliSummer.Runtime;
using NuclearOption.Networking;
using UnityEngine;
using UnityEngine.Rendering;

namespace BoscaliSummer.Features.Support.Visuals
{
    /// <summary>
    /// Orbital stations as real objects in the sky. A station on a theatre pass — own and
    /// foreign — is a cluster of cubes laid out like its grid (solar wings and radiators flat,
    /// the core larger), placed along its true line of sight from the theatre centre at a 1/20
    /// display scale, so it rises, crosses and sets where the orbit says at the angular rate
    /// the orbit says while staying inside the 80 km render distance. It fades in and out at
    /// the pass edges; a core or module launch leaves a climbing streak at the map edge.
    /// Client-local presentation: nothing is networked, no cube carries a collider, so neither
    /// physics nor radar rays can hit one.
    /// </summary>
    internal sealed class PlatformSky : MonoBehaviour, ISceneService
    {
        public const float DisplayScale = 0.05f;
        private const float MaximumDisplayRange = 76000f;
        private const float MinimumCubeSize = 40f;
        private const float ApparentSize = 0.0022f;
        private const float Pitch = 1.2f;
        private const float FadeSeconds = 6f;
        private const int Stations = 1 + SpaceOperations.MaximumForeign;
        private const int MaximumRememberedLaunches = 16;

        private static readonly Color HullDay = new Color(0.86f, 0.92f, 0.96f, 1f);
        private static readonly Color HullNight = new Color(0.72f, 1f, 0.94f, 1f);
        private static readonly Color Wing = new Color(0.16f, 0.26f, 0.52f, 1f);
        private static readonly Color Radiator = new Color(0.95f, 0.95f, 0.9f, 1f);
        private static readonly Color Dead = new Color(0.3f, 0.3f, 0.32f, 1f);
        private static readonly Color HostileDay = new Color(1f, 0.78f, 0.74f, 1f);
        private static readonly Color HostileNight = new Color(1f, 0.5f, 0.42f, 1f);
        private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");

        private readonly Transform[] roots = new Transform[Stations];
        private readonly Transform[,] cubes = new Transform[Stations, OrbitalPlatform.CellCount];
        private readonly Renderer[,] renderers = new Renderer[Stations, OrbitalPlatform.CellCount];
        private readonly List<long> launched = new List<long>(MaximumRememberedLaunches);
        private MaterialPropertyBlock block;
        private SupportManager support;
        private Material material;

        public void Configure(SupportManager manager) => support = manager;

        public void ResetForScene()
        {
            for (int s = 0; s < Stations; s++)
            {
                if (roots[s] != null) Destroy(roots[s].gameObject);
                roots[s] = null;
                for (int c = 0; c < OrbitalPlatform.CellCount; c++)
                {
                    cubes[s, c] = null;
                    renderers[s, c] = null;
                }
            }
            launched.Clear();
        }

        private void OnDestroy()
        {
            ResetForScene();
            if (material != null) Destroy(material);
        }

        private void LateUpdate()
        {
            if (support == null || Application.isBatchMode) return;
            if (OrbitalBounds.Radius() <= 0f || support.Settings == null || !support.Settings.Enabled.Value)
            {
                HideFrom(0);
                return;
            }

            double now = support.OrbitNow;
            OrbitClock clock = support.OrbitClock;
            LevelInfo level = NetworkSceneSingleton<LevelInfo>.i;
            float night = level != null ? 1f - Mathf.InverseLerp(0.02f, 0.4f, level.GetAmbientLight()) : 0f;
            int used = 0;

            OrbitalPlatform own = support.LocalPlatform;
            if (own != null && own.Exists)
            {
                NoteLaunches(own, now);
                if (Place(used, own.State(now, clock), night, own, 0, now)) used++;
            }

            IReadOnlyList<ForeignPlatform> others = support.Space.Foreign;
            for (int i = 0; i < others.Count && used < Stations; i++)
            {
                if (Place(used, others[i].State(now, clock), night, null, others[i].Layout, now)) used++;
            }

            HideFrom(used);
        }

        private bool Place(int station, in OrbitState state, float night, OrbitalPlatform own, ushort layout, double now)
        {
            if (!state.InPass) return false;
            LookAngles look = TheaterTrack.Look(state, 0.0, 0.0);
            if (!look.Visible) return false;
            float range = (float)look.SlantRange * DisplayScale;
            if (range > MaximumDisplayRange) return false;

            float cosEl = Mathf.Cos((float)look.Elevation);
            var direction = new Vector3((float)look.AzimuthX * cosEl, Mathf.Sin((float)look.Elevation),
                                        (float)look.AzimuthZ * cosEl);
            Vector3 offset = direction * range;
            var global = new GlobalPosition(offset.x, offset.y, offset.z);

            float edge = Mathf.Min((float)state.TimeInPass, (float)state.TimeToPassEnd);
            float fade = Mathf.Clamp01(edge / FadeSeconds);
            float size = Mathf.Max(MinimumCubeSize, range * ApparentSize) * Mathf.Lerp(0.15f, 1f, fade);

            Transform root = EnsureRoot(station);
            root.position = global.ToLocalPosition();
            root.rotation = Quaternion.LookRotation(new Vector3((float)state.Pass.DirX, 0f, (float)state.Pass.DirZ));
            if (!root.gameObject.activeSelf) root.gameObject.SetActive(true);

            Color hull = own != null ? Color.Lerp(HullDay, HullNight, night) : Color.Lerp(HostileDay, HostileNight, night);
            for (int cell = 0; cell < OrbitalPlatform.CellCount; cell++)
            {
                ModuleKind kind = own != null ? own.Cell(cell)
                    : ((layout >> cell) & 1) != 0 ? (cell == OrbitalPlatform.CoreCell ? ModuleKind.Core : ModuleKind.Battery)
                    : ModuleKind.None;
                Transform cube = cubes[station, cell];
                if (kind == ModuleKind.None)
                {
                    if (cube != null && cube.gameObject.activeSelf) cube.gameObject.SetActive(false);
                    continue;
                }

                cube = EnsureCube(station, cell);
                float x = (OrbitalPlatform.Column(cell) - 2) * size * Pitch;
                float z = (1 - OrbitalPlatform.Row(cell)) * size * Pitch;
                cube.localPosition = new Vector3(x, 0f, z);
                cube.localScale = Shape(kind) * size;

                Color colour = own == null ? hull
                    : !own.IsOnline(cell, now) ? Dead
                    : kind == ModuleKind.Solar ? Wing
                    : kind == ModuleKind.Radiator ? Radiator
                    : Tint(hull, PlatformModules.Info(kind).Category);
                block.SetColor(BaseColor, colour);
                block.SetColor(ColorId, colour);
                renderers[station, cell].SetPropertyBlock(block);
                if (!cube.gameObject.activeSelf) cube.gameObject.SetActive(true);
            }
            return true;
        }

        private static Vector3 Shape(ModuleKind kind)
        {
            switch (kind)
            {
                case ModuleKind.Core: return new Vector3(1.3f, 1.3f, 1.6f);
                case ModuleKind.Solar: return new Vector3(1.9f, 0.08f, 0.95f);
                case ModuleKind.Radiator: return new Vector3(0.95f, 0.08f, 1.5f);
                case ModuleKind.Habitat: return new Vector3(1f, 1f, 1.35f);
                case ModuleKind.Imager: return new Vector3(0.75f, 1.2f, 0.75f);
                case ModuleKind.Rods: return new Vector3(0.7f, 0.7f, 1.3f);
                case ModuleKind.Shield: return new Vector3(1.05f, 1.05f, 0.35f);
                case ModuleKind.Relay: return new Vector3(0.6f, 1.1f, 0.6f);
                default: return new Vector3(0.9f, 0.9f, 0.9f);
            }
        }

        private static Color Tint(Color hull, ModuleCategory category)
        {
            switch (category)
            {
                case ModuleCategory.Weapon: return Color.Lerp(hull, new Color(1f, 0.45f, 0.3f, 1f), 0.35f);
                case ModuleCategory.Sensor: return Color.Lerp(hull, new Color(0.45f, 1f, 0.6f, 1f), 0.3f);
                case ModuleCategory.Power: return Color.Lerp(hull, new Color(1f, 0.85f, 0.35f, 1f), 0.3f);
                case ModuleCategory.Mobility: return Color.Lerp(hull, new Color(1f, 0.65f, 0.25f, 1f), 0.25f);
                default: return hull;
            }
        }

        /// <summary>A streak for the core launch and for every module or cargo launch, once each.</summary>
        private void NoteLaunches(OrbitalPlatform platform, double now)
        {
            if (platform.HoldAt(now) == PlatformHold.Insertion)
                Streak(((long)platform.Seed << 8) | 0xC0, platform.Seed, (float)(platform.CycleStart - now));
            if (platform.Pending != ModuleKind.None && platform.DockAt > now)
            {
                long key = ((long)Mathf.RoundToInt((float)platform.DockAt) << 16) | ((long)platform.Pending << 8) | 1;
                Streak(key, platform.Seed ^ (int)platform.Pending ^ platform.PendingCell * 7919, (float)(platform.DockAt - now));
            }
        }

        private void Streak(long key, int seed, float duration)
        {
            if (duration <= 1f || launched.Contains(key)) return;
            if (launched.Count >= MaximumRememberedLaunches) launched.RemoveAt(0);
            launched.Add(key);

            float radius = OrbitalBounds.Radius();
            float angle = (seed & 0xffff) / 65535f * Mathf.PI * 2f;
            var site = new GlobalPosition(Mathf.Sin(angle) * radius * 0.92f, 0f, Mathf.Cos(angle) * radius * 0.92f);
            Vector3 local = site.ToLocalPosition();
            local.y = Mathf.Max(local.y, Datum.LocalSeaY) + 40f;
            SatelliteLaunchVisuals.Play(local, duration);
        }

        private Transform EnsureRoot(int station)
        {
            if (roots[station] != null) return roots[station];
            var go = new GameObject("BoscaliStation_" + station);
            go.transform.SetParent(transform, false);
            roots[station] = go.transform;
            return go.transform;
        }

        private Transform EnsureCube(int station, int cell)
        {
            if (cubes[station, cell] != null) return cubes[station, cell];
            if (block == null) block = new MaterialPropertyBlock();
            GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = "Module_" + OrbitalPlatform.CellName(cell);
            Collider collider = cube.GetComponent<Collider>();
            if (collider != null) DestroyImmediate(collider);
            Renderer renderer = cube.GetComponent<Renderer>();
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.sharedMaterial = SharedMaterial();
            cube.transform.SetParent(EnsureRoot(station), false);
            cubes[station, cell] = cube.transform;
            renderers[station, cell] = renderer;
            return cube.transform;
        }

        private void HideFrom(int station)
        {
            for (int s = station; s < Stations; s++)
                if (roots[s] != null && roots[s].gameObject.activeSelf) roots[s].gameObject.SetActive(false);
        }

        private Material SharedMaterial()
        {
            if (material != null) return material;
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit")
                         ?? Shader.Find("Sprites/Default")
                         ?? Shader.Find("Unlit/Color");
            material = new Material(shader) { name = "BoscaliStationSky" };
            return material;
        }
    }
}
