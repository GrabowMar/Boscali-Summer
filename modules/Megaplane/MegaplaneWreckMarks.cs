using UnityEngine;
using UnityEngine.Rendering;

namespace BoscaliSummer.Modules.Megaplane
{
    /// <summary>Ground scours under the heaviest sliding pieces. All coordinates and lifetimes belong to the wreck field.</summary>
    internal sealed class MegaplaneWreckMarks : MonoBehaviour
    {
        private static readonly float[] Across = { -1f, -.72f, -.40f, 0f, .39f, .72f, 1f };
        private static readonly byte[] Opacity = { 0, 94, 152, 80, 143, 104, 0 };
        private static Material dirt;
        private Mesh strip;
        private int rows, shown = -1;
        private float begins, duration;

        internal static void Add(Transform field, Vector3 start, Vector3 end, float width, bool water,
            float startsAt = 0f, float duration = 0f, float age = 0f)
        {
            Vector3 along = Vector3.ProjectOnPlane(end - start, Vector3.up);
            float length = along.magnitude;
            if (water || length < 8f) return;
            if (dirt == null)
            {
                Shader shader = Shader.Find("Sprites/Default");
                if (shader == null) return;
                dirt = new Material(shader) { name = "Groza ground scrape", hideFlags = HideFlags.DontSave };
            }
            var go = new GameObject("Groza skid mark");
            go.transform.SetParent(field, false);
            var mark = go.AddComponent<MegaplaneWreckMarks>();
            mark.rows = Mathf.Clamp(Mathf.CeilToInt(length / 6f), 5, 40);
            mark.begins = Time.time - age + startsAt;
            mark.duration = duration;
            mark.strip = mark.Build(field, start, end, Vector3.Cross(Vector3.up, along / length), Mathf.Clamp(width * .5f, .45f, 8f));
            go.AddComponent<MeshFilter>().sharedMesh = mark.strip;
            MeshRenderer renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = dirt;
            renderer.receiveShadows = false;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            mark.Update();
        }

        private Mesh Build(Transform field, Vector3 start, Vector3 end, Vector3 sideways, float radius)
        {
            int columns = Across.Length;
            var vertices = new Vector3[(rows + 1) * columns];
            var colors = new Color32[vertices.Length];
            var triangles = new int[rows * (columns - 1) * 6];
            int index = 0;
            for (int row = 0; row <= rows; row++)
            {
                float t = row / (float)rows;
                float fade = Mathf.Min(1f, t * 11f, (1f - t) * 8f);
                Vector3 center = Vector3.Lerp(start, end, t);
                float grain = Mathf.PerlinNoise(center.x * .08f + .16f, center.z * .08f + .73f);
                center += sideways * (grain - .5f) * radius * .32f;
                float spread = radius * (.35f + fade * .65f) * (.65f + .55f * grain);
                Vector3 left = center - sideways * spread, right = center + sideways * spread;
                bool leftOnGround = Ground(field, ref left);
                bool centerOnGround = Ground(field, ref center);
                bool rightOnGround = Ground(field, ref right);
                if (!leftOnGround || !centerOnGround || !rightOnGround) fade = 0f;
                for (int c = 0; c < columns; c++)
                {
                    int at = row * columns + c;
                    vertices[at] = Vector3.Lerp(center, Across[c] < 0f ? left : right, Mathf.Abs(Across[c]));
                    colors[at] = new Color32(46, 40, 34, (byte)(Opacity[c] * fade * (.63f + grain * .37f)));
                    if (row == rows || c == columns - 1) continue;
                    triangles[index++] = at; triangles[index++] = at + columns; triangles[index++] = at + 1;
                    triangles[index++] = at + 1; triangles[index++] = at + columns; triangles[index++] = at + columns + 1;
                }
            }
            var mesh = new Mesh { name = "Groza ground scrape", vertices = vertices, colors32 = colors, triangles = triangles };
            mesh.RecalculateBounds();
            return mesh;
        }

        private static bool Ground(Transform field, ref Vector3 local)
        {
            Vector3 point = field.TransformPoint(local);
            if (!Physics.Raycast(point + Vector3.up * 24f, Vector3.down, out RaycastHit surface, 48f,
                PhysicsLayers.StaticsMask, QueryTriggerInteraction.Ignore) || surface.normal.y < .5f ||
                surface.point.y < Datum.LocalSeaY - .2f) return false;
            local = field.InverseTransformPoint(surface.point + surface.normal * .12f);
            return true;
        }

        private void Update()
        {
            float elapsed = Time.time - begins;
            float fraction = elapsed < 0f ? 0f : duration > 0f ? Mathf.Clamp01(elapsed / duration) : 1f;
            int count = Mathf.FloorToInt(rows * (1f - (1f - fraction) * (1f - fraction)));
            if (shown != count)
            {
                strip.SetSubMesh(0, new SubMeshDescriptor(0, count * (Across.Length - 1) * 6), MeshUpdateFlags.DontRecalculateBounds);
                shown = count;
            }
            if (fraction >= 1f) enabled = false;
        }

        private void OnDestroy() { if (strip != null) Destroy(strip); }
    }
}
