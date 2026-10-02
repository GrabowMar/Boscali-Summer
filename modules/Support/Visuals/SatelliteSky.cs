using BoscaliSummer.Modules.Support.Runtime;
using BoscaliSummer.Core.Lifecycle;
using BoscaliSummer.Core.Ui;
using BoscaliSummer.Core.Game;
using NuclearOption.Networking;
using UnityEngine;
using UnityEngine.Rendering;

namespace BoscaliSummer.Modules.Support.Visuals
{
    /// <summary>
    /// The A1 tasking satellite as a real object in the sky. A fixed schedule annunciator,
    /// not an orbit: it parks at one bearing from the theatre centre at an apparent size that
    /// matches the stations, and it is only there while the local faction's tasking window is
    /// open — the mesh renders the real window schedule, never decoration. Client-local
    /// presentation: nothing is networked, no collider, so neither physics nor radar rays hit it.
    /// </summary>
    internal sealed class SatelliteSky : MonoBehaviour, ISceneService
    {
        private const float DisplayRange = 60000f;
        private const float MeshSpan = 12f;
        private const float ApparentSize = 0.0022f;
        private const float MinimumSpan = 40f;

        private static readonly Vector3 AnchorDirection = new Vector3(0.5f, 1.1f, -0.65f).normalized;
        private static readonly Color HullDay = new Color(0.86f, 0.92f, 0.96f, 1f);
        private static readonly Color HullNight = new Color(0.72f, 1f, 0.94f, 1f);
        private static readonly Color Panel = new Color(0.16f, 0.26f, 0.52f, 1f);
        private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");

        private SupportManager support;
        private Transform root;
        private Renderer renderer;
        private Material hullMaterial;
        private Material panelMaterial;

        public void Configure(SupportManager manager) => support = manager;

        public void ResetForScene()
        {
            if (root != null) Destroy(root.gameObject);
            root = null;
            renderer = null;
            if (hullMaterial != null) Destroy(hullMaterial);
            if (panelMaterial != null) Destroy(panelMaterial);
            hullMaterial = null;
            panelMaterial = null;
        }

        private void OnDestroy() => ResetForScene();

        private void LateUpdate()
        {
            if (support == null || Application.isBatchMode || support.Settings == null ||
                !support.Settings.Enabled.Value || !support.LocalWindowOpen)
            {
                Hide();
                return;
            }
            if (root == null) Build();
            LevelInfo level = NetworkSceneSingleton<LevelInfo>.i;
            float night = level != null ? 1f - Mathf.InverseLerp(0.02f, 0.4f, level.GetAmbientLight()) : 0f;
            Vector3 offset = AnchorDirection * DisplayRange;
            root.position = new GlobalPosition(offset.x, offset.y, offset.z).ToLocalPosition();
            float size = Mathf.Max(MinimumSpan, DisplayRange * ApparentSize) / MeshSpan;
            root.localScale = new Vector3(size, size, size);
            Color hull = Color.Lerp(HullDay, HullNight, night);
            hullMaterial.SetColor(BaseColor, hull);
            hullMaterial.SetColor(ColorId, hull);
            if (!root.gameObject.activeSelf) root.gameObject.SetActive(true);
        }

        private void Build()
        {
            var go = new GameObject("BoscaliTaskingSatellite");
            go.transform.SetParent(transform, false);
            root = go.transform;
            Mesh mesh = new Mesh { name = "TaskingSatellite" };
            mesh.vertices = ToVectors(SatelliteMeshData.HullVerts, SatelliteMeshData.PanelVerts);
            mesh.subMeshCount = 2;
            mesh.SetTriangles(SatelliteMeshData.HullTris, 0);
            mesh.SetTriangles(Offset(SatelliteMeshData.PanelTris, SatelliteMeshData.HullVerts.Length / 3), 1);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            renderer = go.AddComponent<MeshRenderer>();
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit")
                ?? Shader.Find("Sprites/Default")
                ?? Shader.Find("Unlit/Color");
            hullMaterial = new Material(shader) { name = "BoscaliSatHull" };
            panelMaterial = new Material(shader) { name = "BoscaliSatPanel" };
            panelMaterial.SetColor(BaseColor, Panel);
            panelMaterial.SetColor(ColorId, Panel);
            renderer.sharedMaterials = new[] { hullMaterial, panelMaterial };
        }

        private void Hide()
        {
            if (root != null && root.gameObject.activeSelf) root.gameObject.SetActive(false);
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
