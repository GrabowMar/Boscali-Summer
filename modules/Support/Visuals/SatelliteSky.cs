using BoscaliSummer.Modules.Support.Runtime;
using BoscaliSummer.Core.Lifecycle;
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

        // Geostationary: each bird hangs at a fixed point high in the southern sky (about 50-60 deg up), spread in bearing so the three never overlap.
        private static readonly Vector3[] Directions = { new Vector3(0.55f, 1.15f, -0.75f).normalized,
            new Vector3(-0.15f, 1.35f, -0.6f).normalized, new Vector3(-0.7f, 1.1f, -0.85f).normalized };
        /// <summary>The direction from the ground up to a bird: the feed's line of sight for that bird's sensor.</summary>
        internal static Vector3 LineOfSight(BirdKind bird) => Track((int)bird, SupportManager.MissionNow());

        /// <summary>
        /// A bird's place in the sky. Geostationary, so a constant direction; the host's GeoBird position (mirrored on a client) places it and a burn slides it, so the sky and
        /// the feed's line of sight follow the same slot.
        /// </summary>
        internal static Vector3 Track(int bird, float missionSeconds)
        {
            int k = Mathf.Clamp(bird, 0, Directions.Length - 1);
            if (!hasGeo[k]) return Directions[k];
            // Toward the bird's map point (u east, v south), still high in the sky and slightly spread by its default slot so two birds over one point never overlap; a burn slides it.
            GeoBird g = latest[k];
            return new Vector3((g.U(missionSeconds) - 0.5f) * 1.4f + Directions[k].x * 0.25f, 1.15f, (0.5f - g.V(missionSeconds)) * 1.4f + Directions[k].z * 0.25f).normalized;
        }

        private static readonly GeoBird[] latest = new GeoBird[SpaceRules.BirdCount];
        private static readonly bool[] hasGeo = new bool[SpaceRules.BirdCount];

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
        private Material glintMaterial;
        private Mesh glintMesh;
        private readonly Transform[] glints = new Transform[SpaceRules.BirdCount];
        private readonly MeshRenderer[] glintRenderers = new MeshRenderer[SpaceRules.BirdCount];
        private MaterialPropertyBlock glintBlock;

        public void Configure(SupportManager manager) => support = manager;

        public void ResetForScene()
        {
            System.Array.Clear(hasGeo, 0, hasGeo.Length);
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
            if (glintMaterial != null) Destroy(glintMaterial);
            if (glintMesh != null) Destroy(glintMesh);
            glintMaterial = null; glintMesh = null;
            System.Array.Clear(glints, 0, glints.Length);
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
            if (GameManager.GetLocalPlayer<Player>(out Player me) && me != null)
                for (int i = 0; i < hasGeo.Length; i++) hasGeo[i] = support.TryOwnGeo(me.HQ, i, out latest[i]);
            if (!present) { Hide(); return; }
            if (mesh == null) Build();
            LevelInfo level = NetworkSceneSingleton<LevelInfo>.i;
            float night = level != null ? 1f - Mathf.InverseLerp(0.02f, 0.4f, level.GetAmbientLight()) : 0f;
            float size = Mathf.Max(MinimumSpan, DisplayRange * ApparentSize) / MeshSpan;
            var csm = SceneSingleton<CameraStateManager>.i;
            Camera cam = csm != null ? csm.mainCamera : Camera.main;
            float now = SupportManager.MissionNow();
            // Dusk and night: the hull shows as a glint that flares as the panels catch the sun; by day the plain hull is enough.
            float dusk = Mathf.Clamp01(night * 1.6f);
            glintBlock = glintBlock ?? new MaterialPropertyBlock();
            for (int i = 0; i < birds.Length; i++)
            {
                Vector3 offset = Track(i, now) * DisplayRange;
                birds[i].position = new GlobalPosition(offset.x, offset.y, offset.z).ToLocalPosition();
                birds[i].localScale = new Vector3(size, size, size);
                birds[i].gameObject.SetActive(has[i]);
                Transform glint = glints[i];
                if (glint == null || !has[i] || cam == null) { if (glint != null) glint.gameObject.SetActive(false); continue; }
                float flare = 0.3f + 0.7f * Mathf.Pow(Mathf.Max(0f, Mathf.Sin(now * 0.7f + i * 2.4f)), 10f);
                glint.gameObject.SetActive(dusk > 0.02f);
                glint.position = birds[i].position;
                glint.rotation = cam.transform.rotation;
                float span = size * MeshSpan * (3f + 4f * flare);
                glint.localScale = new Vector3(span, span, span);
                glintBlock.SetColor(BaseColor, new Color(1f, 0.93f, 0.75f, dusk * flare * 0.9f));
                glintRenderers[i].SetPropertyBlock(glintBlock);
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
            glintMesh = new Mesh { name = "BirdGlint" };
            glintMesh.vertices = new[] { new Vector3(-.5f, -.5f, 0), new Vector3(.5f, -.5f, 0), new Vector3(-.5f, .5f, 0), new Vector3(.5f, .5f, 0) };
            glintMesh.uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 1), new Vector2(1, 1) };
            glintMesh.triangles = new[] { 0, 2, 1, 2, 3, 1 };
            glintMesh.RecalculateBounds();
            glintMaterial = new Material(SupportParticles.Material(true)) { name = "BoscaliBirdGlint" };
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
                var glint = new GameObject("BoscaliGlint:" + (BirdKind)i);
                glint.transform.SetParent(transform, false);
                glint.AddComponent<MeshFilter>().sharedMesh = glintMesh;
                var glintRenderer = glint.AddComponent<MeshRenderer>();
                glintRenderer.sharedMaterial = glintMaterial;
                glintRenderer.shadowCastingMode = ShadowCastingMode.Off;
                glintRenderer.receiveShadows = false;
                glints[i] = glint.transform;
                glintRenderers[i] = glintRenderer;
                glint.SetActive(false);
            }
        }

        private void Hide()
        {
            for (int i = 0; i < birds.Length; i++)
            {
                if (birds[i] != null && birds[i].gameObject.activeSelf) birds[i].gameObject.SetActive(false);
                if (glints[i] != null && glints[i].gameObject.activeSelf) glints[i].gameObject.SetActive(false);
            }
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
