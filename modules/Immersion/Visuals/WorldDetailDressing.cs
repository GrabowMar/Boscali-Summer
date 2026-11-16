using System;
using System.Collections.Generic;
using NuclearOption.Effects;
using UnityEngine;
using UnityEngine.Rendering;

namespace BoscaliSummer.Modules.Immersion.Visuals
{
    /// <summary>Camera-local authored details: at most five instanced draws, no per-prop objects.</summary>
    internal sealed class WorldDetailDressing
    {
        private const int CellSize = 32, Ring = 3, MaxCells = 49, PerCell = 6, MaxVisible = 192, MaxBlasts = 256;
        private const float PlantRange = 105f, PropRange = 125f;
        private struct Point
        {
            internal Vector3 Global;
            internal Matrix4x4 Matrix;
            internal float Radius;
            internal int Kind;
            internal bool Removed;
        }
        private sealed class Cell
        {
            internal int X, Z, Count;
            internal readonly Point[] Points = new Point[PerCell];
        }
        private struct Burn { internal Vector3 Center; internal float Radius; }
        private readonly List<Cell> cells = new List<Cell>(MaxCells);
        private readonly Stack<Cell> spare = new Stack<Cell>(MaxCells);
        private readonly WorldDetailPlacement placement = new WorldDetailPlacement();
        private readonly Plane[] planes = new Plane[6];
        private readonly Burn[] burns = new Burn[MaxBlasts];
        private readonly Matrix4x4[][] draws = { new Matrix4x4[MaxVisible], new Matrix4x4[MaxVisible],
            new Matrix4x4[MaxVisible], new Matrix4x4[MaxVisible], new Matrix4x4[MaxVisible] };
        private readonly int[] counts = new int[5];
        private Mesh[] meshes;
        private Material foliage, solid;
        private Transform map;
        private Vector3 datum;
        private int centerX = int.MaxValue, centerZ = int.MaxValue, burnCount;
        private bool complete, damageOverflow;
        private float nextHeightProbe, groundY;
        internal int Visible { get; private set; }
        internal int DrawCalls { get; private set; }
        internal int CachedCells => cells.Count;
        internal int DamageRecords => burnCount;
#if !PUBLIC_RELEASE
        internal double CpuMilliseconds { get; private set; }
        internal long CpuFrames { get; private set; }
#endif

        internal void Tick(Camera camera, bool enabled)
        {
#if !PUBLIC_RELEASE
            long began = System.Diagnostics.Stopwatch.GetTimestamp();
            try { TickCore(camera, enabled); }
            finally
            {
                CpuMilliseconds += (System.Diagnostics.Stopwatch.GetTimestamp() - began) *
                    (1000.0 / System.Diagnostics.Stopwatch.Frequency);
                CpuFrames++;
            }
#else
            TickCore(camera, enabled);
#endif
        }

        private void TickCore(Camera camera, bool enabled)
        {
            SynchronizeMap();
            Visible = DrawCalls = 0;
            if (!enabled || camera == null || map == null || Datum.origin == null || Application.isBatchMode ||
                GameManager.IsHeadless || GameManager.gameState == GameState.Encyclopedia ||
                !PlayerSettings.DetailSettings.GrassEnabled ||
                SceneSingleton<TerrainHeightMap>.i == null || !SceneSingleton<TerrainHeightMap>.i.IsActive || damageOverflow)
            { ClearCells(); return; }
            Vector3 eye = camera.transform.position.ToGlobalPosition().AsVector3();
            if (Time.unscaledTime >= nextHeightProbe)
            {
                nextHeightProbe = Time.unscaledTime + .5f;
                Vector3 top = new GlobalPosition(eye.x, 10000f, eye.z).ToLocalPosition();
                groundY = Physics.Raycast(top, Vector3.down, out RaycastHit hit, 20000f, PhysicsLayers.StaticsMask,
                    QueryTriggerInteraction.Ignore) ? hit.point.GlobalY() : float.NegativeInfinity;
            }
            if (eye.y - groundY > PropRange + 20f || !EnsureAssets()) return;
            int cx = Mathf.FloorToInt(eye.x / CellSize), cz = Mathf.FloorToInt(eye.z / CellSize);
            if (cx != centerX || cz != centerZ)
            {
                centerX = cx; centerZ = cz; complete = false;
                for (int i = cells.Count - 1; i >= 0; i--)
                    if (Math.Abs(cells[i].X - cx) > Ring || Math.Abs(cells[i].Z - cz) > Ring)
                    { spare.Push(cells[i]); cells.RemoveAt(i); }
            }
            if (!complete) BuildNextCell(); // Six placement rays; the occasional height probe adds one.
            if (datum != Datum.originPosition)
            {
                datum = Datum.originPosition;
                foliage.SetFloat("_WindDatum", datum.x * .17f + datum.z * .11f);
                foreach (Cell cell in cells)
                    for (int i = 0; i < cell.Count; i++) SetTranslation(ref cell.Points[i]);
            }
            GeometryUtility.CalculateFrustumPlanes(camera, planes);
            Array.Clear(counts, 0, counts.Length);
            foreach (Cell cell in cells)
                for (int i = 0; i < cell.Count && Visible < MaxVisible; i++)
                {
                    ref Point point = ref cell.Points[i];
                    if (point.Removed) continue;
                    float range = point.Kind < WorldDetailMeshes.ShrubCount ? PlantRange : PropRange;
                    if ((point.Global - eye).sqrMagnitude > range * range) continue;
                    Vector3 local = point.Global + datum;
                    bool culled = false;
                    for (int p = 0; p < planes.Length; p++)
                        if (planes[p].GetDistanceToPoint(local) < -point.Radius) { culled = true; break; }
                    if (culled) continue;
                    draws[point.Kind][counts[point.Kind]++] = point.Matrix;
                    Visible++;
                }
            for (int kind = 0; kind < meshes.Length; kind++)
            {
                if (counts[kind] == 0) continue;
                Graphics.DrawMeshInstanced(meshes[kind], 0, kind < WorldDetailMeshes.ShrubCount ? foliage : solid,
                    draws[kind], counts[kind], null, ShadowCastingMode.Off, true, 0, camera, LightProbeUsage.Off);
                DrawCalls++;
            }
        }

        private bool EnsureAssets()
        {
            if (meshes != null) return true;
            if (!SystemInfo.supportsInstancing || !WorldDetailAssets.TryGet(out Shader shader, out Texture2D atlas)) return false;
            meshes = WorldDetailMeshes.Build();
            foliage = new Material(shader) { name = "Boscali Brush", enableInstancing = true, hideFlags = HideFlags.HideAndDontSave };
            foliage.SetTexture("_MainTex", atlas);
            foliage.SetFloat("_Cull", (float)CullMode.Off);
            Vector3 origin = Datum.originPosition;
            foliage.SetFloat("_WindDatum", origin.x * .17f + origin.z * .11f);
            foliage.SetVector("_Fade", new Vector4(85, PlantRange, 0, 0));
            foliage.EnableKeyword("_LEAF_CUTOUT");
            solid = new Material(shader) { name = "Boscali Rock and Deadwood", enableInstancing = true, hideFlags = HideFlags.HideAndDontSave };
            solid.SetTexture("_MainTex", atlas);
            solid.SetFloat("_Cull", (float)CullMode.Back);
            solid.SetVector("_Fade", new Vector4(105, PropRange, 0, 0));
            return true;
        }

        private void BuildNextCell()
        {
            for (int ring = 0; ring <= Ring; ring++)
                for (int x = -ring; x <= ring; x++)
                    for (int z = -ring; z <= ring; z++)
                    {
                        if (Math.Max(Math.Abs(x), Math.Abs(z)) != ring) continue;
                        int gx = centerX + x, gz = centerZ + z;
                        bool exists = false;
                        foreach (Cell known in cells) if (known.X == gx && known.Z == gz) { exists = true; break; }
                        if (exists) continue;
                        Cell cell = spare.Count != 0 ? spare.Pop() : new Cell();
                        cell.X = gx; cell.Z = gz; cell.Count = 0;
                        var center = new GlobalPosition((gx + .5f) * CellSize, 0, (gz + .5f) * CellSize);
                        if (!placement.Begin(center, CellSize))
                        {
                            if (placement.Retryable) spare.Push(cell);
                            else cells.Add(cell); // An over-budget cell stays empty rather than rescanning forever.
                            return;
                        }
                        cells.Add(cell);
                        uint seed = Hash(unchecked((uint)gx * 73856093u ^ (uint)gz * 19349663u));
                        float ax = gx * CellSize + 6 + Unit(seed) * 20;
                        float az = gz * CellSize + 6 + Unit(Hash(seed)) * 20;
                        for (int j = 0; j < PerCell; j++)
                        {
                            uint h = Hash(seed + (uint)j * 2654435761u);
                            float px = ax + (Unit(h) - .5f) * 9f, pz = az + (Unit(Hash(h)) - .5f) * 9f;
                            if (!placement.TryGround(px, pz, out GlobalPosition at, out Vector3 normal)) continue;
                            Vector3 global = at.AsVector3();
                            if (Burned(global)) continue;
                            int kind = j < 3 ? (int)(h % 3u) : j < 5 ? WorldDetailMeshes.Rock : WorldDetailMeshes.Log;
                            // Fewer logs than plants/rocks; size differences are deterministic per site.
                            if (kind == WorldDetailMeshes.Log && (h & 3u) != 0u) continue;
                            float scale = kind < 3 ? Mathf.Lerp(.8f, 1.45f, Unit(Hash(h + 4))) :
                                kind == WorldDetailMeshes.Rock ? Mathf.Lerp(.7f, 1.65f, Unit(Hash(h + 4))) : 1f;
                            float yaw = Unit(Hash(h + 9)) * 360;
                            Quaternion rotation = Quaternion.FromToRotation(Vector3.up, Vector3.Slerp(Vector3.up, normal, .6f)) *
                                Quaternion.Euler(0, yaw, 0);
                            global.y -= kind == WorldDetailMeshes.Rock ? .2f * scale : .025f;
                            var point = new Point { Global = global, Kind = kind, Radius = 2.5f * scale,
                                Matrix = Matrix4x4.TRS(Vector3.zero, rotation, Vector3.one * scale) };
                            SetTranslation(ref point);
                            cell.Points[cell.Count++] = point;
                        }
                        return;
                    }
            complete = true;
        }

        internal void RecordBlast(GlobalPosition position, float radius)
        {
            SynchronizeMap();
            if (map == null || !float.IsFinite(radius) || radius <= 0 ||
                !float.IsFinite(position.x) || !float.IsFinite(position.y) || !float.IsFinite(position.z)) return;
            if (burnCount >= burns.Length) { damageOverflow = true; ClearCells(); return; }
            Vector3 global = position.AsVector3();
            burns[burnCount++] = new Burn { Center = global, Radius = radius };
            foreach (Cell cell in cells)
                for (int i = 0; i < cell.Count; i++)
                    if ((cell.Points[i].Global - global).sqrMagnitude <= radius * radius) cell.Points[i].Removed = true;
        }

        private bool Burned(Vector3 position)
        {
            for (int i = 0; i < burnCount; i++)
                if ((position - burns[i].Center).sqrMagnitude <= burns[i].Radius * burns[i].Radius) return true;
            return false;
        }
        private void SynchronizeMap()
        {
            Transform current = LevelInfo.i?.LoadedMapSettings?.transform;
            if (ReferenceEquals(current, map)) return;
            ClearCells(); placement.Reset(); map = current; burnCount = 0; damageOverflow = false;
            nextHeightProbe = 0;
        }
        private void SetTranslation(ref Point point)
        {
            Vector3 local = point.Global + Datum.originPosition;
            point.Matrix.m03 = local.x; point.Matrix.m13 = local.y; point.Matrix.m23 = local.z;
        }
        private void ClearCells()
        {
            foreach (Cell cell in cells) spare.Push(cell);
            cells.Clear(); centerX = centerZ = int.MaxValue; complete = false;
        }
        internal void Reset()
        {
            ClearCells(); placement.Reset();
            if (meshes != null) foreach (Mesh mesh in meshes) UnityEngine.Object.Destroy(mesh);
            meshes = null;
            if (foliage != null) UnityEngine.Object.Destroy(foliage);
            if (solid != null) UnityEngine.Object.Destroy(solid);
            foliage = solid = null;
            Visible = DrawCalls = 0;
            // Blast history belongs to the map, so toggle/reset cannot regrow cleared detail.
        }
        private static uint Hash(uint value) { unchecked { value ^= value >> 16; value *= 0x7feb352du; value ^= value >> 15; value *= 0x846ca68bu; return value ^ (value >> 16); } }
        private static float Unit(uint value) => (value & 65535u) / 65535f;
    }
}
