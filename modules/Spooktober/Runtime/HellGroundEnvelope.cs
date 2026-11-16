using UnityEngine;

namespace BoscaliSummer.Modules.Spooktober.Runtime
{
    /// <summary>One-time local clearance for a sampled ground grid; no terrain or collider is changed.</summary>
    internal static class HellGroundEnvelope
    {
        internal static int Raise(Vector3[] vertices, bool[] valid, int side, Matrix4x4 localToWorld, float rayTop, float clearance)
        {
            var lift = new float[vertices.Length];
            int samples = 0;
            for (int z = 0; z < side - 1; z++)
                for (int x = 0; x < side - 1; x++)
                {
                    int a = z * side + x, b = a + side, c = a + 1, d = b + 1;
                    if (valid != null && (!valid[a] || !valid[b] || !valid[c] || !valid[d])) continue;
                    Vector3 wa = localToWorld.MultiplyPoint3x4(vertices[a]), wb = localToWorld.MultiplyPoint3x4(vertices[b]);
                    Vector3 wc = localToWorld.MultiplyPoint3x4(vertices[c]), wd = localToWorld.MultiplyPoint3x4(vertices[d]);
                    float min = Mathf.Min(Mathf.Min(wa.y, wb.y), Mathf.Min(wc.y, wd.y));
                    float max = Mathf.Max(Mathf.Max(wa.y, wb.y), Mathf.Max(wc.y, wd.y));
                    if (max - min > 60f) continue; // retain existing cliff/hole separation
                    Vector3 middle = (wb + wc) * .5f; // shared diagonal of the two rendered triangles
                    samples++;
                    if (!HellTerritory.Surface(new Vector3(middle.x, rayTop, middle.z), out RaycastHit hit, out _) || hit.normal.y < .5f) continue;
                    float deficit = hit.point.y + clearance - middle.y;
                    if (deficit <= 0f) continue;
                    lift[a] = Mathf.Max(lift[a], deficit); lift[b] = Mathf.Max(lift[b], deficit);
                    lift[c] = Mathf.Max(lift[c], deficit); lift[d] = Mathf.Max(lift[d], deficit);
                }
            Matrix4x4 worldToLocal = localToWorld.inverse;
            for (int i = 0; i < vertices.Length; i++)
                if (lift[i] > 0f) vertices[i] += worldToLocal.MultiplyVector(Vector3.up * lift[i]);
            return samples;
        }
    }
}
