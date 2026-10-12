using System.Collections.Generic;
using BoscaliSummer.Core.Fx;
using NOAvionics;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

namespace BoscaliSummer.Modules.Events.Presentation
{
    /// <summary>
    /// The recognition plate's model: a selected prefab's render meshes, with no Unit, colliders, audio or
    /// network objects. It is drawn orthographically, framed by the mesh bounds, so one scale bar is true
    /// anywhere on the plate and a patrol boat and a fleet carrier both fill it. One 768x384 target, counted
    /// against the shared render-target ledger, is rendered on selection, on a view change and when the plate
    /// changes shape; it is released when the desk closes. If the ledger has no room the plate stays empty.
    /// </summary>
    internal sealed class EventAircraftPreview
    {
        internal enum View { Port, Plan, HeadOn, ThreeQuarter }

        private const int PreviewLayer = 31;
        private const int MeshLimit = 256;
        private const float Margin = 1.16f;

        /// <summary>Where the copy is parked: far under the sea, away from every game camera.</summary>
        private static readonly Vector3 Park = new Vector3(0f, -4000f, 0f);

        private readonly RawImage output;
        private readonly RenderTexture texture;
        private readonly Camera camera;
        private readonly Light light;
        private GameObject model;
        private Bounds bounds;
        private float yaw = 270f, pitch, aspect = 2f;
        private View view = View.Port;
        private bool preset = true;

        /// <summary>World metres across the full height of the last render; 0 while nothing is drawn.</summary>
        internal float SpanMetres { get; private set; }

        /// <summary>"PORT", "PLAN", "HEAD-ON", "3-QTR", or the bearing the pilot turned the model to.</summary>
        internal string ViewName => !preset ? "BRG " + Mathf.RoundToInt(yaw).ToString("000", System.Globalization.CultureInfo.InvariantCulture)
            : view == View.Port ? "PORT" : view == View.Plan ? "PLAN" : view == View.HeadOn ? "HEAD-ON" : "3-QTR";

        /// <summary>The preset in force, or null once the pilot has turned the model off it.</summary>
        internal View? Preset => preset ? view : (View?)null;

        internal EventAircraftPreview(RawImage output)
        {
            this.output = output;
            texture = new RenderTexture(768, 384, 16, RenderTextureFormat.ARGB32)
            {
                name = "BoscaliFieldArchiveModel",
                antiAliasing = 4,
                useMipMap = false,
            };
            texture.Create();
            if (!FxRtPool.Own(texture))
            {
                texture.Release();
                Object.Destroy(texture);
                texture = null;
            }
            output.texture = texture;

            var cameraObject = new GameObject("FieldArchivePreviewCamera", typeof(Camera));
            camera = cameraObject.GetComponent<Camera>();
            camera.enabled = false;
            camera.orthographic = true;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.cullingMask = 1 << PreviewLayer;
            camera.targetTexture = texture;

            var lightObject = new GameObject("FieldArchivePreviewLight", typeof(Light));
            light = lightObject.GetComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.5f;
            light.color = new Color(.82f, .92f, 1f);
            light.cullingMask = 1 << PreviewLayer;
        }

        internal bool Load(GameObject prefab)
        {
            ClearModel();
            SpanMetres = 0f;
            if (prefab == null || texture == null) return false;
            var excluded = new HashSet<Renderer>();
            foreach (LODGroup group in prefab.GetComponentsInChildren<LODGroup>(true))
            {
                LOD[] lods = group.GetLODs();
                for (int i = 1; i < lods.Length; i++)
                    foreach (Renderer renderer in lods[i].renderers)
                        if (renderer != null) excluded.Add(renderer);
            }

            Renderer[] source = prefab.GetComponentsInChildren<Renderer>(true);
            // Parts a prefab keeps switched off (wreck meshes, alternate fits) stay off the plate; a prefab that
            // is switched off wholesale is drawn in full rather than not at all.
            if (!Copy(prefab.transform, source, excluded, true) && !Copy(prefab.transform, source, excluded, false))
            {
                ClearModel();
                return false;
            }
            Look(View.ThreeQuarter);
            return true;
        }

        private bool Copy(Transform root, Renderer[] source, HashSet<Renderer> excluded, bool activeOnly)
        {
            ClearModel();
            model = new GameObject("FieldArchiveMeshCopy");
            model.layer = PreviewLayer;
            model.transform.position = Park;
            bool knownBounds = false;
            int copied = 0;
            foreach (Renderer renderer in source)
            {
                if (renderer == null || !renderer.enabled || excluded.Contains(renderer) || copied >= MeshLimit) continue;
                if (activeOnly && !ActiveUnder(renderer.transform, root)) continue;
                Mesh mesh = renderer is SkinnedMeshRenderer skin ? skin.sharedMesh :
                    renderer.GetComponent<MeshFilter>()?.sharedMesh;
                if (mesh == null || mesh.vertexCount == 0) continue;
                var part = new GameObject("PreviewMesh", typeof(MeshFilter), typeof(MeshRenderer));
                part.layer = PreviewLayer;
                part.transform.SetParent(model.transform, false);
                part.transform.localPosition = root.InverseTransformPoint(renderer.transform.position);
                part.transform.localRotation = Quaternion.Inverse(root.rotation) * renderer.transform.rotation;
                Vector3 rootScale = root.lossyScale;
                Vector3 scale = renderer.transform.lossyScale;
                part.transform.localScale = new Vector3(
                    scale.x / Mathf.Max(.0001f, Mathf.Abs(rootScale.x)),
                    scale.y / Mathf.Max(.0001f, Mathf.Abs(rootScale.y)),
                    scale.z / Mathf.Max(.0001f, Mathf.Abs(rootScale.z)));
                part.GetComponent<MeshFilter>().sharedMesh = mesh;
                MeshRenderer copy = part.GetComponent<MeshRenderer>();
                copy.sharedMaterials = renderer.sharedMaterials;
                copy.shadowCastingMode = ShadowCastingMode.Off;
                copy.receiveShadows = false;
                if (!knownBounds) { bounds = copy.bounds; knownBounds = true; }
                else bounds.Encapsulate(copy.bounds);
                copied++;
            }
            return knownBounds;
        }

        /// <summary>True when nothing between <paramref name="part"/> and the prefab root is switched off (the root itself may be).</summary>
        private static bool ActiveUnder(Transform part, Transform root)
        {
            for (Transform x = part; x != null && x != root; x = x.parent)
                if (!x.gameObject.activeSelf) return false;
            return true;
        }

        /// <summary>One of the four recognition views. The nose points left in PORT and PLAN, as on a printed plate.</summary>
        internal void Look(View preset)
        {
            view = preset;
            this.preset = true;
            yaw = preset == View.HeadOn ? 0f : preset == View.ThreeQuarter ? 315f : 270f;
            pitch = preset == View.Plan ? 90f : preset == View.ThreeQuarter ? 24f : 0f;
            Render();
        }

        internal void Rotate(float degrees)
        {
            if (model == null) return;
            preset = false;
            yaw = Mathf.Repeat(yaw + degrees, 360f);
            Render();
        }

        /// <summary>The plate's width over its height; the frame is refitted when the plate changes shape.</summary>
        internal void SetAspect(float value)
        {
            value = Mathf.Clamp(value, 0.5f, 6f);
            if (Mathf.Abs(value - aspect) < 0.01f) return;
            aspect = value;
            Render();
        }

        private void Render()
        {
            if (texture == null || model == null) return;
            float yawRad = yaw * Mathf.Deg2Rad, pitchRad = pitch * Mathf.Deg2Rad;
            var flat = new Vector3(Mathf.Sin(yawRad), 0f, Mathf.Cos(yawRad));
            Vector3 toCamera = flat * Mathf.Cos(pitchRad) + Vector3.up * Mathf.Sin(pitchRad);
            Quaternion look = Quaternion.LookRotation(-toCamera, pitch > 89f ? -flat : Vector3.up);

            // Fit the bounds' eight corners to the frame: the larger of the height and the width at this aspect.
            Vector3 right = look * Vector3.right, up = look * Vector3.up, e = bounds.extents;
            float halfWidth = 0f, halfHeight = 0f;
            for (int i = 0; i < 8; i++)
            {
                var corner = new Vector3((i & 1) == 0 ? -e.x : e.x, (i & 2) == 0 ? -e.y : e.y, (i & 4) == 0 ? -e.z : e.z);
                halfWidth = Mathf.Max(halfWidth, Mathf.Abs(Vector3.Dot(corner, right)));
                halfHeight = Mathf.Max(halfHeight, Mathf.Abs(Vector3.Dot(corner, up)));
            }
            float size = Mathf.Max(0.05f, Mathf.Max(halfHeight, halfWidth / aspect) * Margin);
            float radius = Mathf.Max(0.05f, e.magnitude);
            camera.orthographicSize = size;
            camera.aspect = aspect;
            camera.transform.SetPositionAndRotation(bounds.center + toCamera * (radius + 1f), look);
            camera.nearClipPlane = .01f;
            camera.farClipPlane = radius * 2f + 2f;
            camera.backgroundColor = AvInk.Inert.WithAlpha(1f);
            // The key light rides over the viewer's left shoulder, so every view is lit the same way.
            light.transform.rotation = look * Quaternion.Euler(32f, 28f, 0f);

            // Scene fog would wash a ship out over the few hundred metres an orthographic frame spans.
            bool fog = RenderSettings.fog;
            RenderSettings.fog = false;
            try { camera.Render(); }
            finally { RenderSettings.fog = fog; }
            SpanMetres = size * 2f;
        }

        private void ClearModel()
        {
            if (model == null) return;
            model.SetActive(false);
            Object.Destroy(model);
            model = null;
        }

        internal void Dispose()
        {
            ClearModel();
            SpanMetres = 0f;
            if (output != null) output.texture = null;
            camera.targetTexture = null;
            FxRtPool.Disown(texture);
            if (texture != null)
            {
                texture.Release();
                Object.Destroy(texture);
            }
            // Switched off first: the light must stop lighting the preview layer the moment the viewer is let go.
            light.enabled = false;
            Object.Destroy(camera.gameObject);
            Object.Destroy(light.gameObject);
        }
    }
}
