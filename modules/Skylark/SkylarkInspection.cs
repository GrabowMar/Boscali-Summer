#if !PUBLIC_RELEASE
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Modules.Skylark
{
    /// <summary>Read-only mesh rays for inspecting a seated cockpit, including separately parented native pilot meshes.</summary>
    public static class SkylarkInspection
    {
        public static object Cockpit(Dictionary<string, object> args)
        {
            Aircraft aircraft = args.TryGetValue("idUnit", out object unit) ? unit as Aircraft : null;
            if (aircraft == null) return new { ok = false };
            CameraStateManager camera = CameraStateManager.i;
            Renderer[] renderers = UnityEngine.Object.FindObjectsOfType<Renderer>().Where(r => r.enabled
                && r.gameObject.activeInHierarchy && (r.bounds.center - aircraft.transform.position).sqrMagnitude < 144f).ToArray();
            var samples = new List<object>();
            foreach (float height in new[] { .15f, .3f, .5f })
            {
                Ray ray = camera.mainCamera.ViewportPointToRay(new Vector3(.5f, height, 0));
                var hits = new List<Tuple<Renderer, float>>();
                foreach (Renderer renderer in renderers)
                {
                    if (renderer.sharedMaterial == null || renderer.sharedMaterial.renderQueue >= 3000 || !renderer.bounds.IntersectRay(ray)) continue;
                    MeshFilter filter = renderer.GetComponent<MeshFilter>();
                    Mesh mesh = filter != null ? filter.sharedMesh : null;
                    bool temporary = renderer is SkinnedMeshRenderer;
                    if (temporary) { mesh = new Mesh(); ((SkinnedMeshRenderer)renderer).BakeMesh(mesh); }
                    try
                    {
                        if (mesh == null || !mesh.isReadable) continue;
                        Matrix4x4 inverse = renderer.transform.worldToLocalMatrix;
                        Vector3 origin = inverse.MultiplyPoint3x4(ray.origin), direction = inverse.MultiplyVector(ray.direction);
                        Vector3[] vertices = mesh.vertices; int[] triangles = mesh.triangles;
                        float nearest = float.PositiveInfinity;
                        for (int i = 0; i < triangles.Length; i += 3)
                            nearest = Mathf.Min(nearest, Hit(origin, direction, vertices[triangles[i]], vertices[triangles[i + 1]], vertices[triangles[i + 2]]));
                        if (!float.IsInfinity(nearest)) hits.Add(Tuple.Create(renderer, nearest));
                    }
                    finally { if (temporary && mesh != null) UnityEngine.Object.Destroy(mesh); }
                }
                samples.Add(new { viewportY = height, hits = hits.OrderBy(h => h.Item2).Take(8).Select(h => new {
                    name = h.Item1.name, path = Path(h.Item1.transform), distance = h.Item2, shader = h.Item1.sharedMaterial.shader.name }) });
            }
            SkinnedMeshRenderer pilot = SkylarkTemplate.Get<SkinnedMeshRenderer>(aircraft.pilots[0], "skinnedMeshRenderer");
            SkylarkFlightStrip strip = aircraft.cockpit != null ? aircraft.cockpit.GetComponentInChildren<SkylarkFlightStrip>(true) : null;
            SkylarkCockpitDetails details = aircraft.cockpit != null ? aircraft.cockpit.GetComponentInChildren<SkylarkCockpitDetails>(true) : null;
            // The donor cockpit can be reparented with the native seat and damage parts. The scene's MFD tracks the live screen.
            MFDAppManager mfd = MFDAppManager.i;
            TacScreen screen = mfd != null && SkylarkTemplate.Get<Aircraft>(mfd, "aircraft") == aircraft ? mfd.GetComponent<TacScreen>() : null;
            PylonIndicator pylons = screen != null ? screen.GetComponentInChildren<PylonIndicator>(true) : null;
            Image profile = pylons != null ? pylons.GetComponent<Image>() : null;
            SkylarkTacticalVector vector = screen != null ? screen.GetComponentInChildren<SkylarkTacticalVector>(true) : null;
            return new { ok = true, camera = V(aircraft.transform.InverseTransformPoint(camera.transform.position)),
                anchor = V(aircraft.transform.InverseTransformPoint(aircraft.cockpitViewPoint.position)),
                pilot = new { pilot.name, pilot.enabled, center = V(aircraft.transform.InverseTransformPoint(pilot.bounds.center)), size = V(pilot.bounds.size) }, samples,
                flightStrip = strip != null ? strip.Inspect() : null,
                details = details != null ? details.Inspect() : null,
                tactical = new { caption = vector != null ? vector.Caption : null,
                    storesProfile = profile != null && profile.sprite != null ? profile.sprite.name : null,
                    storesCount = pylons != null ? SkylarkTemplate.Get<Array>(pylons, "pylonElementSets").Length : 0 } };
        }

        private static float Hit(Vector3 origin, Vector3 direction, Vector3 a, Vector3 b, Vector3 c)
        {
            Vector3 ab = b - a, ac = c - a, p = Vector3.Cross(direction, ac);
            float determinant = Vector3.Dot(ab, p);
            if (Mathf.Abs(determinant) < .0000001f) return float.PositiveInfinity;
            Vector3 offset = origin - a;
            float u = Vector3.Dot(offset, p) / determinant;
            if (u < 0 || u > 1) return float.PositiveInfinity;
            Vector3 q = Vector3.Cross(offset, ab);
            float v = Vector3.Dot(direction, q) / determinant;
            if (v < 0 || u + v > 1) return float.PositiveInfinity;
            float t = Vector3.Dot(ac, q) / determinant;
            return t > 0 ? t : float.PositiveInfinity;
        }

        private static string Path(Transform node) => node.parent == null ? node.name : Path(node.parent) + "/" + node.name;
        private static float[] V(Vector3 v) => new[] { v.x, v.y, v.z };
    }
}
#endif
