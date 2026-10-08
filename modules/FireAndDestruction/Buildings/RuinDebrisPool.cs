using System;
using System.Collections.Generic;
using BoscaliSummer.Core.Math;
using BoscaliSummer.Modules.FireAndDestruction.Domain;
using UnityEngine;
using UnityEngine.Rendering;

namespace BoscaliSummer.Fire
{
    /// <summary>Six bounded ruins, six fractured concrete sections each. All motion is local to Datum.</summary>
    internal sealed class RuinDebrisPool
    {
        internal struct FacadePiece
        {
            public Mesh Mesh;
            public Material[] Materials;
            public Vector3 Position;
            public Quaternion Rotation;
            public Vector3 Scale;
            public int Layer;
            public ShadowCastingMode Shadows;
            public bool ReceiveShadows;
        }
        private sealed class Pile
        {
            internal GameObject Root, Shell;
            internal GlobalPosition Position;
            internal Vector2 Footprint;
            internal readonly Transform[] Sections = new Transform[6];
            internal readonly Vector3[] Start = new Vector3[6], Rest = new Vector3[6], SpinAxis = new Vector3[6];
            internal readonly Quaternion[] Rotation = new Quaternion[6];
            internal readonly float[] Flight = new float[6], Spin = new float[6];
            internal readonly float[] Ground = new float[6];
            internal readonly bool[] Landed = new bool[6];
            internal Renderer[] Trim;
            internal float Born, PokeAt, KickAt;
            internal int Order, Cursor, KickSlot;
            internal Vector3 KickFrom, KickTo;
            internal Quaternion KickRotationFrom, KickRotationTo;
            internal bool Falling, Kicking;
            internal bool GroundSampled;
        }
        private readonly List<Pile> piles = new List<Pile>(6);
        private Mesh[] meshes;
        private Material material;
        private Texture2D concrete;
        private int order;
        internal Action<GlobalPosition, Vector3, float> Landing;

        public void Place(GlobalPosition position, Vector2 halfExtents, bool animate = false)
        {
            if (GameManager.IsHeadless || !EnsureAssets()) return;
            Pile pile;
            if (piles.Count < RuinDebrisMath.MaxPiles)
            {
                pile = new Pile { Root = new GameObject("BoscaliSummer.RuinMound") };
                pile.Root.transform.SetParent(Datum.origin, false);
                for (int i = 0; i < 6; i++)
                {
                    var section = new GameObject("FracturedSection");
                    section.transform.SetParent(pile.Root.transform, false);
                    section.AddComponent<MeshFilter>().sharedMesh = meshes[i];
                    section.AddComponent<MeshRenderer>().sharedMaterial = material;
                    pile.Sections[i] = section.transform;
                }
                piles.Add(pile);
            }
            else
            {
                pile = piles[0];
                foreach (Pile candidate in piles) if (candidate.Order < pile.Order) pile = candidate;
            }
            if (pile.Shell != null) { pile.Shell.SetActive(false); UnityEngine.Object.Destroy(pile.Shell); }
            pile.Shell = null; pile.Trim = null; pile.Kicking = false; pile.Cursor = 0; pile.PokeAt = 0f;
            pile.Order = ++order; pile.Position = position; pile.Footprint = halfExtents;
            pile.Born = Time.timeSinceLevelLoad; pile.Falling = animate;
            pile.GroundSampled = false;
            pile.Root.transform.SetPositionAndRotation(position.ToLocalPosition(), Quaternion.identity);
            pile.Root.SetActive(true);
            uint seed = Deterministic.Hash(Mathf.RoundToInt(position.x * 10f), Mathf.RoundToInt(position.z * 10f), 137);
            for (int i = 0; i < 6; i++)
            {
                float angle = i * 2.399963f + FractureGeometry.Rand(seed, 11) * 3f;
                float length = Mathf.Clamp(Mathf.Max(halfExtents.x, halfExtents.y) * (0.25f + FractureGeometry.Rand(seed, i + 17) * 0.17f), 4f, 14f);
                Vector3 scale = new Vector3(length, Mathf.Clamp(length * 0.055f, 0.3f, 0.8f), length * (0.52f + FractureGeometry.Rand(seed, i + 31) * 0.38f));
                float radius = 0.55f + FractureGeometry.Rand(seed, i + 41) * 0.42f;
                Vector3 rest = new Vector3(Mathf.Cos(angle) * (halfExtents.x + length * 0.2f) * radius, -0.5f,
                    Mathf.Sin(angle) * (halfExtents.y + length * 0.2f) * radius);
                Quaternion rotation = Quaternion.Euler(i == 1 || i == 4 ? 34f : 5f + i * 3f, angle * Mathf.Rad2Deg,
                    (FractureGeometry.Rand(seed, i + 51) - 0.5f) * 21f);
                float low = 0f;
                foreach (Vector3 vertex in meshes[i].vertices)
                    low = Mathf.Min(low, (rotation * Vector3.Scale(vertex, scale)).y);
                rest.y -= low;
                Vector3 start = new Vector3(rest.x * 0.55f,
                    rest.y + Mathf.Clamp(length * (0.55f + FractureGeometry.Rand(seed, i + 61)), 3f, 16f), rest.z * 0.55f);
                pile.Start[i] = start; pile.Rest[i] = rest; pile.Rotation[i] = rotation;
                pile.Ground[i] = -0.5f;
                pile.Flight[i] = Mathf.Sqrt(2f * (start.y - rest.y) / RuinDebrisMath.Gravity);
                pile.Spin[i] = 105f + FractureGeometry.Rand(seed, i + 71) * 175f;
                pile.SpinAxis[i] = new Vector3(Mathf.Cos(angle), 0.3f, Mathf.Sin(angle)).normalized;
                pile.Landed[i] = !animate;
                Transform section = pile.Sections[i]; section.localScale = scale;
                section.localPosition = animate ? start : rest;
                section.localRotation = animate ? Quaternion.AngleAxis(pile.Spin[i], pile.SpinAxis[i]) * rotation : rotation;
            }
        }

        internal GameObject AttachShell(GlobalPosition position, List<FacadePiece> pieces, int buildingId, bool nativeRubble = false)
        {
            if (GameManager.IsHeadless) return null;
            Pile pile = Nearest(position.ToLocalPosition(), 8f);
            if (pile == null || pile.Shell != null) return null;
            var shell = new GameObject("BoscaliSummer.RuinShell");
            shell.transform.SetParent(pile.Root.transform, false);
            var renderers = new List<Renderer>(24);
            if (nativeRubble && pieces != null)
                for (int i = 0; i < pieces.Count && renderers.Count < 24; i++)
                {
                    FacadePiece piece = pieces[i];
                    if (piece.Mesh == null) continue;
                    var go = new GameObject("BrokenNativeStructure") { layer = piece.Layer };
                    go.transform.SetParent(shell.transform, false);
                    go.transform.SetPositionAndRotation(piece.Position, piece.Rotation);
                    go.transform.localScale = piece.Scale;
                    go.AddComponent<MeshFilter>().sharedMesh = piece.Mesh;
                    var renderer = go.AddComponent<MeshRenderer>(); renderer.sharedMaterials = piece.Materials;
                    renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = true;
                    renderers.Add(renderer);
                }
            // Unknown structures fully collapse to the section field; no intact facade or dark fake bay.
            pile.Shell = shell;
            if (renderers.Count > 8) pile.Trim = renderers.GetRange(8, renderers.Count - 8).ToArray();
            return shell;
        }

        internal void Tick(float now)
        {
            foreach (Pile pile in piles)
            {
                if (!pile.GroundSampled && now - pile.Born >= 0.06f)
                {
                    pile.GroundSampled = true;
                    for (int i = 0; i < 6; i++)
                    {
                        Vector3 probe = pile.Root.transform.TransformPoint(pile.Rest[i]);
                        probe.y = pile.Root.transform.position.y + pile.Start[i].y + 2f;
                        if (Physics.Raycast(probe, Vector3.down, out RaycastHit hit, 300f,
                                PhysicsLayers.StaticsMask, QueryTriggerInteraction.Ignore))
                        {
                            float ground = pile.Root.transform.InverseTransformPoint(hit.point).y;
                            pile.Rest[i].y += ground - pile.Ground[i];
                            pile.Ground[i] = ground;
                            pile.Flight[i] = Mathf.Sqrt(2f * Mathf.Max(0.5f, pile.Start[i].y - pile.Rest[i].y) / RuinDebrisMath.Gravity);
                            if (!pile.Falling) pile.Sections[i].localPosition = pile.Rest[i];
                        }
                    }
                }
                if (pile.Falling)
                {
                    bool falling = false;
                    for (int i = 0; i < 6; i++)
                    {
                        float elapsed = Mathf.Max(0f, now - pile.Born - i * 0.065f);
                        float flight = pile.Flight[i];
                        float u = Mathf.Clamp01(elapsed / flight);
                        Vector3 point = Vector3.Lerp(pile.Start[i], pile.Rest[i], u);
                        point.y = Mathf.Max(pile.Rest[i].y, pile.Start[i].y - RuinDebrisMath.Gravity * elapsed * elapsed * 0.5f);
                        if (u >= 1f)
                        {
                            float settle = Mathf.Clamp01((elapsed - flight) / 0.3f);
                            point.y += Mathf.Sin(settle * Mathf.PI) * 0.13f;
                            if (!pile.Landed[i])
                            {
                                pile.Landed[i] = true;
                                Vector3 contact = pile.Rest[i]; contact.y = pile.Ground[i];
                                Landing?.Invoke(new GlobalPosition(pile.Position.x + contact.x, pile.Position.y + contact.y,
                                    pile.Position.z + contact.z), Vector3.up, pile.Sections[i].localScale.x * 0.4f);
                            }
                            falling |= settle < 1f;
                        }
                        else falling = true;
                        pile.Sections[i].localPosition = point;
                        pile.Sections[i].localRotation = Quaternion.AngleAxis(pile.Spin[i] * (1f - u), pile.SpinAxis[i]) * pile.Rotation[i];
                    }
                    pile.Falling = falling;
                }
                if (!pile.Kicking) continue;
                float kick = Mathf.Clamp01((now - pile.KickAt) / 0.4f);
                float ease = 1f - (1f - kick) * (1f - kick);
                Transform section = pile.Sections[pile.KickSlot];
                section.localPosition = Vector3.Lerp(pile.KickFrom, pile.KickTo, ease);
                section.localRotation = Quaternion.Slerp(pile.KickRotationFrom, pile.KickRotationTo, ease);
                pile.Kicking = kick < 1f;
            }
        }

        internal bool Nudge(Vector3 point, float power)
        {
            Pile pile = Nearest(point, Mathf.Clamp(20f + power * 0.5f, 20f, 50f));
            float now = Time.timeSinceLevelLoad;
            if (pile == null || pile.Falling || now < pile.PokeAt) return false;
            pile.PokeAt = now + 0.18f;
            int slot = pile.Cursor++ % 6;
            Transform section = pile.Sections[slot];
            Vector3 from = section.localPosition;
            Vector3 push = pile.Root.transform.InverseTransformPoint(point);
            float x = from.x, z = from.z;
            float yaw = RuinDebrisMath.Kick(ref x, ref z, from.x - push.x, from.z - push.z, power,
                pile.Rest[slot].x, pile.Rest[slot].z, 3.2f);
            pile.KickFrom = from; pile.KickTo = new Vector3(x, from.y, z);
            pile.KickRotationFrom = section.localRotation;
            pile.KickRotationTo = Quaternion.AngleAxis(yaw, Vector3.up) * section.localRotation;
            pile.KickSlot = slot; pile.KickAt = now; pile.Kicking = true;
            return true;
        }

        internal void ApplyAttention(Vector3 cameraPosition)
        {
            Pile nearest = Nearest(cameraPosition, 450f);
            foreach (Pile pile in piles)
            {
                float distance = Vector3.Distance(cameraPosition, pile.Root.transform.position);
                bool show = distance < 1600f;
                pile.Root.SetActive(show);
                if (pile.Trim != null)
                    foreach (Renderer renderer in pile.Trim) if (renderer != null) renderer.enabled = pile == nearest;
                foreach (Transform section in pile.Sections)
                    section.GetComponent<MeshRenderer>().shadowCastingMode = distance < 900f ? ShadowCastingMode.On : ShadowCastingMode.Off;
            }
        }

        private Pile Nearest(Vector3 point, float reach)
        {
            Pile nearest = null; float best = reach * reach;
            foreach (Pile pile in piles)
            {
                if (pile.Root == null) continue;
                float distance = (pile.Root.transform.position - point).sqrMagnitude;
                if (distance < best) { best = distance; nearest = pile; }
            }
            return nearest;
        }

        private bool EnsureAssets()
        {
            if (material != null) return true;
            Shader shader = UnityEngine.Shader.Find("Universal Render Pipeline/Lit") ?? UnityEngine.Shader.Find("Standard");
            if (shader == null) return false;
            concrete = DestructionAssets.ConcreteTexture();
            material = new Material(shader) { name = "Broken structural concrete" };
            material.SetTexture("_BaseMap", concrete); material.SetTexture("_MainTex", concrete);
            material.SetFloat("_Smoothness", 0.025f);
            meshes = new Mesh[6];
            for (int i = 0; i < 6; i++) meshes[i] = FractureGeometry.Section(Deterministic.Hash(i, 71, 137));
            return true;
        }

        public void Clear()
        {
            foreach (Pile pile in piles)
                if (pile.Root != null) { pile.Root.SetActive(false); UnityEngine.Object.Destroy(pile.Root); }
            piles.Clear();
            if (meshes != null) foreach (Mesh mesh in meshes) UnityEngine.Object.Destroy(mesh);
            if (material != null) UnityEngine.Object.Destroy(material);
            if (concrete != null) UnityEngine.Object.Destroy(concrete);
            meshes = null; material = null; concrete = null; order = 0;
        }

        internal static List<FacadePiece> CaptureFacade(Component building)
        {
            var pieces = new List<FacadePiece>(8);
            if (building == null || GameManager.IsHeadless) return pieces;
            Renderer[] renderers = null;
            LODGroup group = building.GetComponentInChildren<LODGroup>(true);
            if (group != null)
            {
                LOD[] lods = group.GetLODs();
                if (lods.Length > 0) renderers = lods[0].renderers;
            }
            if (renderers == null || renderers.Length == 0) renderers = building.GetComponentsInChildren<MeshRenderer>(true);
            foreach (Renderer renderer in renderers)
            {
                if (!(renderer is MeshRenderer)) continue;
                MeshFilter filter = renderer.GetComponent<MeshFilter>();
                if (filter == null || filter.sharedMesh == null) continue;
                Transform transform = renderer.transform;
                pieces.Add(new FacadePiece { Mesh = filter.sharedMesh, Materials = renderer.sharedMaterials,
                    Position = transform.position, Rotation = transform.rotation, Scale = transform.lossyScale,
                    Layer = renderer.gameObject.layer, Shadows = renderer.shadowCastingMode, ReceiveShadows = renderer.receiveShadows });
                if (pieces.Count == 24) break;
            }
            return pieces;
        }
    }
}
