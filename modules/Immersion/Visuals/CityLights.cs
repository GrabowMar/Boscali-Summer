using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace BoscaliSummer.Modules.Immersion.Visuals
{
    /// <summary>
    /// Night city lights (2026-11-06, after EVE's dusk-faded city lights and the street lighting in Kelly's Client
    /// Effects): every map building gets a few lit windows and some get a sodium street lamp, placed from a hash of
    /// its position so the town looks the same every night. Lights fade in with the sun 6 degrees above the horizon
    /// and are out once it is 4 degrees under (<see cref="LightPoints.NightLevel"/>). Built in 3 km chunks within
    /// <see cref="Reach"/> of the camera, two chunks a tick; a destroyed building goes dark (its chunk is rebuilt).
    /// ponytail: lights never switch off late at night; vary by mission hour if towns should sleep.
    /// </summary>
    internal sealed class CityLights
    {
        private const float Cell = 3000f, Reach = 30000f, Drop = 36000f;
        private const int BuildsPerTick = 2, MaxPerChunk = 900, MaxLive = 24000;
        private static readonly Color32 Window = new Color32(255, 196, 120, 255), CoolWindow = new Color32(205, 220, 255, 255),
            Sodium = new Color32(255, 150, 55, 255);

        private sealed class Chunk
        {
            public Vector3 Center;
            public readonly List<MapBuilding> Buildings = new List<MapBuilding>();
            public GameObject Root;
            public ParticleSystemRenderer Renderer;
            public int Lights;
            public bool Dirty;
        }

        private static readonly AccessTools.FieldRef<MapBuilding, float> Radius = Ref("radius"), HitPoints = Ref("hitPoints");
        private readonly Dictionary<long, Chunk> chunks = new Dictionary<long, Chunk>();
        private bool indexed;
        private int live;

        internal int Lights => live;

        internal void Tick(Vector3 eye, bool enabled, float night)
        {
            if (!enabled || Datum.origin == null) { Reset(); return; }
            if (night <= .01f && !indexed) return;   // nothing to build by day
            if (!indexed) Index();
            int budget = BuildsPerTick;
            foreach (Chunk c in chunks.Values)
            {
                float d = (c.Center - eye).sqrMagnitude;
                if (c.Root != null && (d > Drop * Drop || c.Dirty)) Clear(c);
                if (c.Root != null) { if (c.Renderer != null && c.Renderer.enabled != night > .01f) c.Renderer.enabled = night > .01f; continue; }
                if (night <= .01f || d > Reach * Reach || budget <= 0 || live >= MaxLive) continue;
                budget--;
                Build(c);
            }
        }

        private void Index()
        {
            indexed = true;
            foreach (MapBuilding b in Object.FindObjectsOfType<MapBuilding>())
            {
                if (b == null) continue;
                Vector3 p = b.transform.GlobalPosition().AsVector3();
                long key = ((long)Mathf.FloorToInt(p.x / Cell) << 32) ^ (uint)Mathf.FloorToInt(p.z / Cell);
                if (!chunks.TryGetValue(key, out Chunk c))
                    chunks[key] = c = new Chunk { Center = new Vector3((Mathf.Floor(p.x / Cell) + .5f) * Cell, p.y, (Mathf.Floor(p.z / Cell) + .5f) * Cell) };
                c.Buildings.Add(b);
                Chunk owner = c;
                b.onBuildingDestroy += () => owner.Dirty = true;
            }
        }

        private void Build(Chunk c)
        {
            c.Dirty = false;
            var pts = new List<Vector3>();
            var cols = new List<Color32>();
            foreach (MapBuilding b in c.Buildings)
            {
                if (b == null || !b.gameObject.activeInHierarchy || (HitPoints != null && HitPoints(b) <= 0f) || pts.Count >= MaxPerChunk) continue;
                Vector3 p = b.transform.GlobalPosition().AsVector3() - c.Center;
                uint h = Hash(p);
                float r = Mathf.Clamp(Radius != null ? Radius(b) : 10f, 4f, 60f);
                int windows = 1 + (int)(h % 3u) + (r > 20f ? 2 : 0);
                float top = Mathf.Clamp(r * 1.1f, 5f, 45f);
                for (int i = 0; i < windows; i++)
                {
                    uint k = h * 2654435761u + (uint)i * 40503u;
                    float a = (k & 1023u) / 1023f * Mathf.PI * 2f, y = 2.5f + ((k >> 10) & 255u) / 255f * (top - 2.5f);
                    pts.Add(p + new Vector3(Mathf.Cos(a) * r * .55f, y, Mathf.Sin(a) * r * .55f));
                    cols.Add(((k >> 18) & 7u) == 0u ? CoolWindow : Window);
                }
                if (h % 10u < 3u)
                {
                    float a = (h >> 8 & 1023u) / 1023f * Mathf.PI * 2f;
                    pts.Add(p + new Vector3(Mathf.Cos(a) * (r + 5f), 7f, Mathf.Sin(a) * (r + 5f)));
                    cols.Add(Sodium);
                }
            }
            c.Root = new GameObject("BoscaliCityLights") { hideFlags = HideFlags.DontSave };
            c.Root.transform.SetParent(Datum.origin, false);
            c.Root.transform.localPosition = c.Center;
            ParticleSystem lights = LightPoints.Build(c.Root.transform, "Lights", pts, cols, 2f, .0016f, .005f);
            c.Renderer = lights != null ? lights.GetComponent<ParticleSystemRenderer>() : null;
            c.Lights = pts.Count;
            live += c.Lights;
        }

        private static AccessTools.FieldRef<MapBuilding, float> Ref(string name)
        {
            try { return AccessTools.FieldRefAccess<MapBuilding, float>(name); }
            catch (System.Exception) { return null; }
        }

        private static uint Hash(Vector3 p)
        {
            unchecked
            {
                uint h = (uint)Mathf.RoundToInt(p.x * 3f) * 73856093u ^ (uint)Mathf.RoundToInt(p.z * 3f) * 19349663u;
                h ^= h >> 13;
                h *= 0x5bd1e995u;
                return h ^ (h >> 15);
            }
        }

        private void Clear(Chunk c)
        {
            if (c.Root != null) Object.Destroy(c.Root);
            c.Root = null;
            live -= c.Lights;
            c.Lights = 0;
        }

        internal void Reset()
        {
            foreach (Chunk c in chunks.Values) if (c.Root != null) Object.Destroy(c.Root);
            chunks.Clear();
            indexed = false;
            live = 0;
        }
    }
}
