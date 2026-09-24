using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

namespace BoscaliSummer.Features.Events.Presentation
{
    /// <summary>
    /// A selected aircraft's render meshes, with no Unit, colliders, audio or network objects.
    /// One 512x256 target is rendered on selection/rotation only, then released on desk close.
    /// </summary>
    internal sealed class EventAircraftPreview
    {
        private const int PreviewLayer = 31;
        private const int MeshLimit = 96;
        private readonly RawImage output;
        private readonly RenderTexture texture;
        private readonly Camera camera;
        private readonly Light light;
        private GameObject model;
        private Bounds bounds;
        private float yaw = 24f;

        internal EventAircraftPreview(RawImage output)
        {
            this.output = output;
            texture = new RenderTexture(512, 256, 16, RenderTextureFormat.ARGB32)
            {
                name = "BoscaliFieldArchiveAircraft",
                antiAliasing = 2,
                useMipMap = false,
            };
            texture.Create();
            output.texture = texture;

            var cameraObject = new GameObject("FieldArchivePreviewCamera", typeof(Camera));
            camera = cameraObject.GetComponent<Camera>();
            camera.enabled = false;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(.035f, .065f, .075f, 1f);
            camera.cullingMask = 1 << PreviewLayer;
            camera.fieldOfView = 32f;
            camera.targetTexture = texture;

            var lightObject = new GameObject("FieldArchivePreviewLight", typeof(Light));
            light = lightObject.GetComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.5f;
            light.color = new Color(.82f, .92f, 1f);
            light.cullingMask = 1 << PreviewLayer;
            light.transform.rotation = Quaternion.Euler(35f, -40f, 0f);
        }

        internal bool Load(AircraftDefinition definition)
        {
            ClearModel();
            if (definition == null || definition.unitPrefab == null) return false;
            var excluded = new HashSet<Renderer>();
            foreach (LODGroup group in definition.unitPrefab.GetComponentsInChildren<LODGroup>(true))
            {
                LOD[] lods = group.GetLODs();
                for (int i = 1; i < lods.Length; i++)
                    foreach (Renderer renderer in lods[i].renderers)
                        if (renderer != null) excluded.Add(renderer);
            }

            model = new GameObject("FieldArchiveMeshCopy");
            model.layer = PreviewLayer;
            Renderer[] source = definition.unitPrefab.GetComponentsInChildren<Renderer>(true);
            bool knownBounds = false;
            int copied = 0;
            foreach (Renderer renderer in source)
            {
                if (renderer == null || !renderer.enabled || excluded.Contains(renderer) ||
                    copied >= MeshLimit) continue;
                Mesh mesh = renderer is SkinnedMeshRenderer skin ? skin.sharedMesh :
                    renderer.GetComponent<MeshFilter>()?.sharedMesh;
                if (mesh == null || mesh.vertexCount == 0) continue;
                var part = new GameObject("PreviewMesh", typeof(MeshFilter), typeof(MeshRenderer));
                part.layer = PreviewLayer;
                part.transform.SetParent(model.transform, false);
                Transform sourceRoot = definition.unitPrefab.transform;
                part.transform.localPosition = sourceRoot.InverseTransformPoint(renderer.transform.position);
                part.transform.localRotation = Quaternion.Inverse(sourceRoot.rotation) * renderer.transform.rotation;
                Vector3 rootScale = sourceRoot.lossyScale;
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
            if (!knownBounds) { ClearModel(); return false; }
            yaw = 24f;
            Render();
            return true;
        }

        internal void Rotate(float degrees)
        {
            if (model == null) return;
            yaw = Mathf.Repeat(yaw + degrees, 360f);
            Render();
        }

        private void Render()
        {
            float radius = Mathf.Max(1f, bounds.extents.magnitude);
            Vector3 direction = Quaternion.Euler(0f, yaw, 0f) * new Vector3(0f, .34f, -1f);
            camera.transform.position = bounds.center + direction.normalized * radius * 3.3f;
            camera.transform.LookAt(bounds.center);
            camera.nearClipPlane = .05f;
            camera.farClipPlane = Mathf.Max(100f, radius * 9f);
            camera.Render();
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
            output.texture = null;
            camera.targetTexture = null;
            texture.Release();
            Object.Destroy(texture);
            Object.Destroy(camera.gameObject);
            Object.Destroy(light.gameObject);
        }
    }
}
