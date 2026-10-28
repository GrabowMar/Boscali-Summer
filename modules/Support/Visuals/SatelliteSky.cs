using BoscaliSummer.Modules.Support.Runtime;
using BoscaliSummer.Core.Lifecycle;
using NuclearOption.Networking;
using UnityEngine;
using UnityEngine.Rendering;
using BoscaliSummer.Modules.Support.Domain.Space;

namespace BoscaliSummer.Modules.Support.Visuals
{
    /// <summary>
    /// Three persistent typed birds (<see cref="SatelliteModels"/>: telescope, side-looking radar, rod magazine), lit by the sun, sensor face to nadir,
    /// wings turned to the sun. Client-local meshes have no collider or targeting identity.
    /// </summary>
    internal sealed class SatelliteSky : MonoBehaviour, ISceneService
    {
        private const float DisplayRange = 60000f;
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

        private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
#if !PUBLIC_RELEASE
        /// <summary>Dev only (OpsFxDemo "birds"): all three birds show whatever the local faction owns, for look checks from a spectator camera.</summary>
        internal static bool DemoAll;
#endif

        private SupportManager support;
        private readonly Transform[] birds = new Transform[SpaceRules.BirdCount];
        private readonly Transform[] wings = new Transform[SpaceRules.BirdCount];
        private readonly float[] spans = new float[SpaceRules.BirdCount];
        private readonly Mesh[] meshes = new Mesh[SpaceRules.BirdCount * 2];
        private readonly bool[] has = new bool[SpaceRules.BirdCount];
        private bool present, built;
        private float nextState, failedUntil;
        private Material[] materials;
        private Material glintMaterial;
        private Mesh glintMesh;
        private readonly Transform[] glints = new Transform[SpaceRules.BirdCount];
        private readonly MeshRenderer[] glintRenderers = new MeshRenderer[SpaceRules.BirdCount];
        private MaterialPropertyBlock glintBlock;

        public void Configure(SupportManager manager) => support = manager;

        public void ResetForScene()
        {
#if !PUBLIC_RELEASE
            DemoAll = false;
#endif
            System.Array.Clear(hasGeo, 0, hasGeo.Length);
            for (int i = 0; i < birds.Length; i++)
            {
                if (birds[i] != null) Destroy(birds[i].gameObject);
                if (glints[i] != null) Destroy(glints[i].gameObject);
                birds[i] = null; wings[i] = null; glints[i] = null;
            }
            for (int i = 0; i < meshes.Length; i++) { if (meshes[i] != null) Destroy(meshes[i]); meshes[i] = null; }
            if (materials != null)
                foreach (Material m in materials)
                {
                    if (m == null) continue;
                    // Only the textures SatelliteModels made; an unset _BaseMap returns the shared default, which must never be destroyed.
                    Texture map = m.HasProperty("_BaseMap") ? m.GetTexture("_BaseMap") : null;
                    if (map != null && map.name.StartsWith("BoscaliSat", System.StringComparison.Ordinal)) Destroy(map);
                    Destroy(m);
                }
            materials = null;
            built = false;
            present = false;
            nextState = 0f;
            failedUntil = 0f;
            if (glintMaterial != null) Destroy(glintMaterial);
            if (glintMesh != null) Destroy(glintMesh);
            glintMaterial = null; glintMesh = null;
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
#if !PUBLIC_RELEASE
                if (DemoAll) { present = true; for (int i = 0; i < has.Length; i++) has[i] = true; }
#endif
            }
            if (GameManager.GetLocalPlayer<Player>(out Player me) && me != null)
                for (int i = 0; i < hasGeo.Length; i++) hasGeo[i] = support.TryOwnGeo(me.HQ, i, out latest[i]);
            if (!present) { Hide(); return; }
            if (!built) Build();
            LevelInfo level = NetworkSceneSingleton<LevelInfo>.i;
            float night = level != null ? 1f - Mathf.InverseLerp(0.02f, 0.4f, level.GetAmbientLight()) : 0f;
            Vector3 toSun = level != null && level.sun != null ? -level.sun.transform.forward : Vector3.up;
            float apparent = Mathf.Max(MinimumSpan, DisplayRange * ApparentSize);
            var csm = SceneSingleton<CameraStateManager>.i;
            Camera cam = csm != null ? csm.mainCamera : Camera.main;
            float now = SupportManager.MissionNow();
            // Dusk and night: the bird still in sunlight shows as a glint that flares as the panels catch the sun; by day the lit hull is enough.
            float dusk = Mathf.Clamp01(night * 1.6f);
            glintBlock ??= new MaterialPropertyBlock();
            for (int i = 0; i < birds.Length; i++)
            {
                float size = apparent / spans[i];
                Vector3 offset = Track(i, now) * DisplayRange;
                birds[i].position = new GlobalPosition(offset.x, offset.y, offset.z).ToLocalPosition();
                birds[i].localScale = new Vector3(size, size, size);
                if (birds[i].gameObject.activeSelf != has[i]) birds[i].gameObject.SetActive(has[i]);
                if (has[i])
                {
                    // The wings turn about their boom (local X) until the cells face the sun.
                    Vector3 s = birds[i].InverseTransformDirection(toSun);
                    wings[i].localRotation = Quaternion.AngleAxis(Mathf.Atan2(s.z, s.y) * Mathf.Rad2Deg, Vector3.right);
                }
                Transform glint = glints[i];
                if (glint == null || !has[i] || cam == null) { if (glint != null && glint.gameObject.activeSelf) glint.gameObject.SetActive(false); continue; }
                float flare = 0.3f + 0.7f * Mathf.Pow(Mathf.Max(0f, Mathf.Sin(now * 0.7f + i * 2.4f)), 10f);
                bool glinting = dusk > 0.02f;
                if (glint.gameObject.activeSelf != glinting) glint.gameObject.SetActive(glinting);
                if (!glinting) continue; // hidden: no transform or colour writes (they are all rewritten the frame it shows)
                glint.position = birds[i].position;
                glint.rotation = cam.transform.rotation;
                float span = apparent * (3f + 4f * flare);
                glint.localScale = new Vector3(span, span, span);
                glintBlock.SetColor(BaseColor, new Color(1f, 0.93f, 0.75f, dusk * flare * 0.9f));
                glintRenderers[i].SetPropertyBlock(glintBlock);
            }
        }

        private void Build()
        {
            built = true;
            materials = SatelliteModels.Materials();
            glintMesh = new Mesh { name = "BirdGlint" };
            glintMesh.vertices = new[] { new Vector3(-.5f, -.5f, 0), new Vector3(.5f, -.5f, 0), new Vector3(-.5f, .5f, 0), new Vector3(.5f, .5f, 0) };
            glintMesh.uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 1), new Vector2(1, 1) };
            glintMesh.triangles = new[] { 0, 2, 1, 2, 3, 1 };
            glintMesh.RecalculateBounds();
            glintMaterial = new Material(SupportParticles.Material(true)) { name = "BoscaliBirdGlint" };
            for (int i = 0; i < birds.Length; i++)
            {
                var kind = (BirdKind)i;
                Mesh body = meshes[i * 2] = SatelliteModels.Body(kind);
                Mesh wing = meshes[i * 2 + 1] = SatelliteModels.Wings(kind);
                spans[i] = Mathf.Max(1f, wing.bounds.size.x);
                var go = new GameObject("BoscaliBird:" + kind);
                go.transform.SetParent(transform, false);
                go.transform.localRotation = Quaternion.Euler(0f, i * 120f + 20f, 0f);
                birds[i] = go.transform;
                Dress(go, body);
                var w = new GameObject("Wings");
                w.transform.SetParent(go.transform, false);
                wings[i] = w.transform;
                Dress(w, wing);
                var glint = new GameObject("BoscaliGlint:" + kind);
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

        private void Dress(GameObject go, Mesh mesh)
        {
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.sharedMaterials = materials;
        }

        private void Hide()
        {
            for (int i = 0; i < birds.Length; i++)
            {
                if (birds[i] != null && birds[i].gameObject.activeSelf) birds[i].gameObject.SetActive(false);
                if (glints[i] != null && glints[i].gameObject.activeSelf) glints[i].gameObject.SetActive(false);
            }
        }
    }
}
