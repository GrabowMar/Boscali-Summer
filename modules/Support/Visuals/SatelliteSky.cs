using BoscaliSummer.Modules.Support.Runtime;
using BoscaliSummer.Core.Lifecycle;
using BoscaliSummer.Core.Ui;
using BoscaliSummer.Core.Game;
using NuclearOption.Networking;
using UnityEngine;
using UnityEngine.Rendering;
using BoscaliSummer.Modules.Support.Domain.Space;

namespace BoscaliSummer.Modules.Support.Visuals
{
    /// <summary>
    /// Three persistent typed birds. Client-local meshes have no collider or targeting identity.
    /// </summary>
    internal sealed class SatelliteSky : MonoBehaviour, ISceneService
    {
        private const float DisplayRange = 60000f;
        private const float MeshSpan = 12f;
        private const float ApparentSize = 0.0022f;
        private const float MinimumSpan = 40f;

        private static readonly Vector3[] Directions = { new Vector3(0.5f, 1.1f, -0.65f).normalized,
            new Vector3(-0.8f, 1.2f, -0.15f).normalized, new Vector3(0.2f, 1.1f, 0.8f).normalized };
        /// <summary>The direction from the ground up to a bird: the feed's line of sight for that bird's sensor.</summary>
        internal static Vector3 LineOfSight(BirdKind bird) => Directions[Mathf.Clamp((int)bird, 0, Directions.Length - 1)];

        private static readonly Color HullDay = new Color(0.86f, 0.92f, 0.96f, 1f);
        private static readonly Color HullNight = new Color(0.72f, 1f, 0.94f, 1f);
        private static readonly Color Panel = new Color(0.16f, 0.26f, 0.52f, 1f);
        private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");

        private SupportManager support;
        private readonly Transform[] birds = new Transform[SpaceRules.BirdCount];
        private Mesh mesh;
        private readonly bool[] has = new bool[SpaceRules.BirdCount];
        private bool present;
        private float nextState, failedUntil;
        private Material hullMaterial;
        private Material panelMaterial;

        public void Configure(SupportManager manager) => support = manager;

        public void ResetForScene()
        {
            for (int i = 0; i < birds.Length; i++)
            {
                if (birds[i] != null) Destroy(birds[i].gameObject);
                birds[i] = null;
            }
            if (mesh != null) Destroy(mesh);
            mesh = null;
            present = false;
            nextState = 0f;
            failedUntil = 0f;
            if (hullMaterial != null) Destroy(hullMaterial);
            if (panelMaterial != null) Destroy(panelMaterial);
            hullMaterial = null;
            panelMaterial = null;
        }

        private void OnDestroy() => ResetForScene();

        private void LateUpdate()
        {
            if (Time.unscaledTime < failedUntil) return;
            try { Frame(); }
            catch (System.Exception e)
            {
                failedUntil = Time.unscaledTime + 2f; // latch: log once per fault, then back off instead of faulting every frame
                Plugin.Logger?.LogError("[Support.Space] Satellite sky failed: " + e);
                Hide();
            }
        }

        private void Frame()
        {
            if (support == null || Application.isBatchMode || support.Settings == null ||
                !support.Settings.Enabled.Value)
            {
                Hide();
                return;
            }
            if (SupportManager.MissionNow() >= nextState)
            {
                nextState = SupportManager.MissionNow() + 5f;
                // Host: the faction's actual SPACE state. Client: the mirror of it, so the sky matches the host.
                present = false;
                if (GameManager.GetLocalPlayer<Player>(out Player player) && player != null && support.TryGetSpaceFamily(player.HQ, out _))
                    for (int i = 0; i < has.Length; i++) { has[i] = support.HasSpaceBird(player.HQ, (BirdKind)i); present |= has[i]; }
            }
            if (!present) { Hide(); return; }
            if (mesh == null) Build();
            LevelInfo level = NetworkSceneSingleton<LevelInfo>.i;
            float night = level != null ? 1f - Mathf.InverseLerp(0.02f, 0.4f, level.GetAmbientLight()) : 0f;
            float size = Mathf.Max(MinimumSpan, DisplayRange * ApparentSize) / MeshSpan;
            for (int i = 0; i < birds.Length; i++)
            {
                Vector3 offset = Directions[i] * DisplayRange;
                birds[i].position = new GlobalPosition(offset.x, offset.y, offset.z).ToLocalPosition();
                birds[i].localScale = new Vector3(size, size, size);
                birds[i].gameObject.SetActive(has[i]);
            }
            Color hull = Color.Lerp(HullDay, HullNight, night);
            hullMaterial.SetColor(BaseColor, hull);
            hullMaterial.SetColor(ColorId, hull);
        }

        private void Build()
        {
            mesh = new Mesh { name = "PersistentSatellite" };
            mesh.vertices = ToVectors(SatelliteMeshData.HullVerts, SatelliteMeshData.PanelVerts);
            mesh.subMeshCount = 2;
            mesh.SetTriangles(SatelliteMeshData.HullTris, 0);
            mesh.SetTriangles(Offset(SatelliteMeshData.PanelTris, SatelliteMeshData.HullVerts.Length / 3), 1);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit")
                ?? Shader.Find("Sprites/Default")
                ?? Shader.Find("Unlit/Color");
            hullMaterial = new Material(shader) { name = "BoscaliSatHull" };
            panelMaterial = new Material(shader) { name = "BoscaliSatPanel" };
            panelMaterial.SetColor(BaseColor, Panel);
            panelMaterial.SetColor(ColorId, Panel);
            for (int i = 0; i < birds.Length; i++)
            {
                var go = new GameObject("BoscaliBird:" + (BirdKind)i);
                go.transform.SetParent(transform, false);
                go.transform.localRotation = Quaternion.Euler(0f, i * 120f, 0f);
                birds[i] = go.transform;
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var renderer = go.AddComponent<MeshRenderer>();
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                renderer.sharedMaterials = new[] { hullMaterial, panelMaterial };
            }
        }

        private void Hide()
        {
            for (int i = 0; i < birds.Length; i++)
                if (birds[i] != null && birds[i].gameObject.activeSelf) birds[i].gameObject.SetActive(false);
        }

        private static Vector3[] ToVectors(float[] first, float[] second)
        {
            var merged = new Vector3[first.Length / 3 + second.Length / 3];
            for (int i = 0; i < merged.Length; i++)
            {
                float[] source = i < first.Length / 3 ? first : second;
                int at = i < first.Length / 3 ? i * 3 : (i - first.Length / 3) * 3;
                merged[i] = new Vector3(source[at], source[at + 1], source[at + 2]);
            }
            return merged;
        }

        private static int[] Offset(int[] tris, int baseIndex)
        {
            var shifted = new int[tris.Length];
            for (int i = 0; i < tris.Length; i++) shifted[i] = tris[i] + baseIndex;
            return shifted;
        }
    }
}
