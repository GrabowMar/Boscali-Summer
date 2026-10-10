using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Threading.Tasks;
using UnityEngine;

namespace BoscaliSummer.Fire
{
    /// <summary>One native building mesh copied offline (the game ships them CPU-unreadable).</summary>
    internal sealed class NativeSurface
    {
        internal Vector3[] P, N;
        internal Vector2[] U;
        internal int[][] Sub;
        internal Bounds Bounds;
    }

    /// <summary>Decodes tools/Extract-BuildingMeshes.py output once, off the main thread.</summary>
    internal static class NativeSurfaceLibrary
    {
        private static Dictionary<string, NativeSurface> map;
        private static Task loading;
        internal static double LoadMs { get; private set; }
        internal static int Count => map != null ? map.Count : 0;

        internal static void BeginLoad() { if (loading == null) loading = Task.Run(Load); }

        internal static bool TryGet(string name, out NativeSurface surface)
        {
            surface = null;
            BeginLoad();
            if (!loading.IsCompleted) loading.Wait(); // Only if a hit lands before scene warm-up finished.
            return map != null && map.TryGetValue(name, out surface);
        }

        private static void Load()
        {
            var clock = System.Diagnostics.Stopwatch.StartNew();
            var result = new Dictionary<string, NativeSurface>();
            using (Stream stream = typeof(NativeSurfaceLibrary).Assembly.GetManifestResourceStream("BoscaliSummer.Destruction.buildings.mesh.gz"))
            {
                if (stream == null) { map = result; return; }
                using (var zip = new GZipStream(stream, CompressionMode.Decompress))
                using (var reader = new BinaryReader(zip))
                {
                    if (reader.ReadInt32() != 2) throw new InvalidDataException("Unsupported building mesh table");
                    int count = reader.ReadInt32();
                    for (int m = 0; m < count; m++)
                    {
                        string name = System.Text.Encoding.UTF8.GetString(reader.ReadBytes(reader.ReadInt32()));
                        int n = reader.ReadInt32();
                        var s = new NativeSurface { P = new Vector3[n], N = new Vector3[n], U = new Vector2[n] };
                        for (int i = 0; i < n; i++)
                        {
                            s.P[i] = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
                            s.N[i] = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
                            s.U[i] = new Vector2(reader.ReadSingle(), reader.ReadSingle());
                        }
                        s.Sub = new int[reader.ReadInt32()][];
                        for (int k = 0; k < s.Sub.Length; k++)
                        {
                            s.Sub[k] = new int[reader.ReadInt32()];
                            for (int i = 0; i < s.Sub[k].Length; i++) s.Sub[k][i] = reader.ReadUInt16();
                        }
                        s.Bounds = new Bounds(n > 0 ? s.P[0] : Vector3.zero, Vector3.zero);
                        for (int i = 1; i < n; i++) s.Bounds.Encapsulate(s.P[i]);
                        result[name] = s;
                    }
                }
            }
            map = result;
            LoadMs = clock.Elapsed.TotalMilliseconds;
        }
    }

    internal struct Lobe { internal Vector3 C; internal float R; }

    /// <summary>A storey slab exposed by a breach: centre on the slab plane and the unit direction into the building.</summary>
    internal struct Slab { internal Vector3 C, Inward; internal float R; }

    /// <summary>
    /// Signed removal field in one part's local space: positive means the material is gone.
    /// Holes are unions of noisy spheres; the ruin break line and Voronoi section borders are
    /// noise-warped, so nothing follows a grid.
    /// </summary>
    internal sealed class CarveField
    {
        internal enum Kind { Keep, Section, Fresh }
        internal Kind Mode;
        internal Lobe[] Lobes = new Lobe[0];
        internal int FreshFrom = int.MaxValue;
        internal bool Break;
        internal float BreakBase, BreakAmp;
        internal Vector3[] Sites;
        internal int Cell;
        internal float Seed;
        internal float Leaf = 0.75f;
        internal Slab[] Slabs;

        internal float Eval(Vector3 p)
        {
            float n1 = Noise(p * 0.45f + new Vector3(Seed, 0f, 0f));
            float n2 = Noise(p * 1.2f + new Vector3(0f, Seed, 3.1f));
            float f;
            if (Mode == Kind.Fresh) f = Mathf.Max(-Hole(p, FreshFrom, Lobes.Length, n1, n2), Hole(p, 0, FreshFrom, n1, n2));
            else f = Hole(p, 0, Lobes.Length, n1, n2);
            if (Break)
            {
                float above = p.y - (BreakBase + BreakAmp * (Noise(new Vector3(p.x * 0.075f, Seed, p.z * 0.075f)) + n1 * 0.25f + n2 * 0.18f));
                f = Mathf.Max(f, Mode == Kind.Section ? -above : above);
            }
            if (Sites != null)
            {
                float own = (p - Sites[Cell]).sqrMagnitude, other = float.MaxValue;
                for (int i = 0; i < Sites.Length; i++)
                    if (i != Cell) other = Mathf.Min(other, (p - Sites[i]).sqrMagnitude);
                f = Mathf.Max(f, Mathf.Sqrt(own) - Mathf.Sqrt(other) + n1 * 2.2f + n2 * 0.5f);
            }
            return f;
        }

        private float Hole(Vector3 p, int from, int to, float n1, float n2)
        {
            float best = -1000f;
            for (int i = from; i < to; i++)
            {
                Lobe lobe = Lobes[i];
                Vector3 v = p - lobe.C; v.y *= 1.25f; // Breaches run along storeys more than across them.
                float d = v.magnitude;
                if (d > lobe.R * 1.6f + 1.5f) { best = Mathf.Max(best, lobe.R * 1.6f + 1.5f - d - 2f); continue; }
                best = Mathf.Max(best, lobe.R * (1f + 0.5f * n1) + 0.8f * n2 - d);
            }
            return best;
        }

        private static float Hash(int x, int y, int z)
        {
            unchecked
            {
                uint h = (uint)(x * 374761393 + y * 668265263 + z * 1274126177);
                h = (h ^ (h >> 13)) * 1103515245u; h ^= h >> 16;
                return (h & 0xffff) / 32767.5f - 1f;
            }
        }

        /// <summary>Trilinear value noise in [-1, 1].</summary>
        internal static float Noise(Vector3 p)
        {
            int x = Mathf.FloorToInt(p.x), y = Mathf.FloorToInt(p.y), z = Mathf.FloorToInt(p.z);
            float fx = p.x - x, fy = p.y - y, fz = p.z - z;
            fx = fx * fx * (3f - 2f * fx); fy = fy * fy * (3f - 2f * fy); fz = fz * fz * (3f - 2f * fz);
            float a = Mathf.Lerp(Hash(x, y, z), Hash(x + 1, y, z), fx), b = Mathf.Lerp(Hash(x, y + 1, z), Hash(x + 1, y + 1, z), fx);
            float c = Mathf.Lerp(Hash(x, y, z + 1), Hash(x + 1, y, z + 1), fx), d = Mathf.Lerp(Hash(x, y + 1, z + 1), Hash(x + 1, y + 1, z + 1), fx);
            return Mathf.Lerp(Mathf.Lerp(a, b, fy), Mathf.Lerp(c, d, fy), fz);
        }
    }

    /// <summary>
    /// Cuts a native surface with a removal field. Triangles subdivide only where the cut can
    /// pass (Lipschitz bound), leaves are cut with marching triangles. Every kept triangle gets
    /// an inset interior twin and every cut segment a thick broken-concrete band joining them,
    /// so openings are real geometry with depth and no detached rim.
    /// </summary>
    internal sealed class SurfaceCutter
    {
        private struct V { internal Vector3 P, N; internal Vector2 U; }
        private const float Slope = 5f, Thickness = 0.45f, Storey = 3.3f, SlabDepth = 0.32f;

        internal readonly List<Vector3> Positions = new List<Vector3>(4096);
        internal readonly List<Vector3> Normals = new List<Vector3>(4096);
        internal readonly List<Vector2> Uvs = new List<Vector2>(4096);
        internal List<int>[] Triangles;
        internal int InteriorSlot, BandSlot;
        internal Bounds Bounds;
        private bool any;
        private CarveField field;
        private NativeSurface source;

        /// <summary>Carve every native triangle. Submeshes: native slots, interior, broken edges.</summary>
        internal void Run(NativeSurface surface, CarveField removal, bool interior)
        {
            field = removal;
            source = surface;
            int slots = surface.Sub.Length;
            InteriorSlot = slots; BandSlot = slots + 1;
            Triangles = new List<int>[slots + 2];
            for (int i = 0; i < Triangles.Length; i++) Triangles[i] = new List<int>(1024);
            any = false;
            for (int s = 0; s < slots; s++)
            {
                int[] t = surface.Sub[s];
                for (int i = 0; i + 2 < t.Length; i += 3)
                {
                    V a = Vertex(surface, t[i]), b = Vertex(surface, t[i + 1]), c = Vertex(surface, t[i + 2]);
                    if (removal == null) Emit(s, a, b, c, interior);
                    else Carve(s, a, b, c, 0, interior);
                }
            }
            if (removal?.Slabs != null) foreach (Slab slab in removal.Slabs) SlabRing(slab);
        }

        /// <summary>
        /// Storey slab behind a breach: a jagged broken inner edge, clipped to stay behind the
        /// facade and inside the footprint, so floors cross the opening instead of open ground.
        /// </summary>
        private void SlabRing(Slab slab)
        {
            const int segments = 28;
            var inner = new Vector3[segments + 1];
            var outer = new Vector3[segments + 1];
            var valid = new bool[segments + 1];
            // The native collider can sit off the real facade; if the slab centre fell outside the
            // shell, walk it in along the inward direction before measuring anything.
            if (float.IsInfinity(MeshHit(slab.C, -slab.Inward)))
            {
                float enter = MeshHit(slab.C, slab.Inward);
                if (float.IsInfinity(enter)) return;
                slab.C += slab.Inward * (enter + slab.R * 0.3f);
            }
            for (int i = 0; i <= segments; i++)
            {
                float angle = (i % segments) * Mathf.PI * 2f / segments;
                var d = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                float r0 = slab.R * (0.45f + 0.3f * CarveField.Noise(new Vector3(angle * 2.3f, slab.C.y * 0.7f, field.Seed))
                    + 0.1f * CarveField.Noise(new Vector3(angle * 7f, field.Seed, 1.7f)));
                // Each spoke stops just inside the real shell (flat, curved or set back alike).
                float r1 = Mathf.Min(slab.R * 1.4f + 1.5f, MeshHit(slab.C, d) - Thickness - 0.05f);
                valid[i] = r1 > r0 + 0.2f;
                inner[i] = slab.C + d * r0;
                outer[i] = slab.C + d * Mathf.Max(r0, r1);
            }
            Vector3 down = Vector3.down * SlabDepth;
            for (int i = 0; i < segments; i++)
            {
                if (!valid[i] || !valid[i + 1]) continue;
                Vector3 a = inner[i], b = outer[i], c = outer[i + 1], e = inner[i + 1];
                Vector2 ua = new Vector2(a.x, a.z) * 0.3f, ub = new Vector2(b.x, b.z) * 0.3f, uc = new Vector2(c.x, c.z) * 0.3f, ue = new Vector2(e.x, e.z) * 0.3f;
                Facing(BandSlot, a, b, c, Vector3.up, ua, ub, uc); Facing(BandSlot, a, c, e, Vector3.up, ua, uc, ue);
                Facing(InteriorSlot, a + down, b + down, c + down, Vector3.down, ua, ub, uc);
                Facing(InteriorSlot, a + down, c + down, e + down, Vector3.down, ua, uc, ue);
                Vector3 edge = slab.C - (a + e) * 0.5f; edge.y = 0f;
                float u0 = i * 0.4f, u1 = u0 + 0.4f;
                Facing(BandSlot, a, e, e + down, edge, new Vector2(u0, 0f), new Vector2(u1, 0f), new Vector2(u1, 0.2f));
                Facing(BandSlot, a, e + down, a + down, edge, new Vector2(u0, 0f), new Vector2(u1, 0.2f), new Vector2(u0, 0.2f));
            }
        }

        private static V Vertex(NativeSurface s, int i) => new V { P = s.P[i], N = s.N[i], U = s.U[i] };

        /// <summary>Nearest two-sided hit of a ray with the native shell; +inf when it misses.</summary>
        private float MeshHit(Vector3 origin, Vector3 direction)
        {
            float best = float.PositiveInfinity;
            foreach (int[] t in source.Sub)
                for (int i = 0; i + 2 < t.Length; i += 3)
                {
                    Vector3 a = source.P[t[i]], e1 = source.P[t[i + 1]] - a, e2 = source.P[t[i + 2]] - a;
                    Vector3 p = Vector3.Cross(direction, e2);
                    float det = Vector3.Dot(e1, p);
                    if (Mathf.Abs(det) < 1e-8f) continue;
                    float inv = 1f / det;
                    Vector3 s = origin - a;
                    float u = Vector3.Dot(s, p) * inv;
                    if (u < 0f || u > 1f) continue;
                    Vector3 q = Vector3.Cross(s, e1);
                    float v = Vector3.Dot(direction, q) * inv;
                    if (v < 0f || u + v > 1f) continue;
                    float distance = Vector3.Dot(e2, q) * inv;
                    if (distance > 0.01f && distance < best) best = distance;
                }
            return best;
        }

        /// <summary>True when the whole triangle survived; such subtrees collapse back to one triangle.</summary>
        private bool Carve(int slot, V a, V b, V c, int depth, bool interior)
        {
            Vector3 centre = (a.P + b.P + c.P) / 3f;
            float radius = Mathf.Sqrt(Mathf.Max((a.P - centre).sqrMagnitude, Mathf.Max((b.P - centre).sqrMagnitude, (c.P - centre).sqrMagnitude)));
            if (radius > field.Leaf && depth < 14)
            {
                float f = field.Eval(centre);
                if (Mathf.Abs(f) > Slope * radius)
                {
                    if (f >= 0f) return false;
                    Emit(slot, a, b, c, interior); return true;
                }
                int positions = Positions.Count, own = Triangles[slot].Count, inner = Triangles[InteriorSlot].Count;
                V ab = Mid(a, b), bc = Mid(b, c), ca = Mid(c, a);
                bool whole = Carve(slot, a, ab, ca, depth + 1, interior);
                whole &= Carve(slot, ab, b, bc, depth + 1, interior);
                whole &= Carve(slot, ca, bc, c, depth + 1, interior);
                whole &= Carve(slot, ab, bc, ca, depth + 1, interior);
                if (!whole) return false;
                Positions.RemoveRange(positions, Positions.Count - positions);
                Normals.RemoveRange(positions, Normals.Count - positions);
                Uvs.RemoveRange(positions, Uvs.Count - positions);
                Triangles[slot].RemoveRange(own, Triangles[slot].Count - own);
                Triangles[InteriorSlot].RemoveRange(inner, Triangles[InteriorSlot].Count - inner);
                Emit(slot, a, b, c, interior);
                return true;
            }
            float fa = field.Eval(a.P), fb = field.Eval(b.P), fc = field.Eval(c.P);
            bool ka = fa < 0f, kb = fb < 0f, kc = fc < 0f;
            int kept = (ka ? 1 : 0) + (kb ? 1 : 0) + (kc ? 1 : 0);
            if (kept == 3) { Emit(slot, a, b, c, interior); return true; }
            if (kept == 0) return false;
            // Rotate so 'a' is the odd vertex while preserving winding.
            if (ka == kb) { Rotate(ref a, ref b, ref c, ref fa, ref fb, ref fc); Rotate(ref a, ref b, ref c, ref fa, ref fb, ref fc); }
            else if (ka == kc) Rotate(ref a, ref b, ref c, ref fa, ref fb, ref fc);
            V p = Lerp(a, b, fa / (fa - fb)), q = Lerp(a, c, fa / (fa - fc));
            Vector3 across = (b.P + c.P) * 0.5f - a.P;
            if (fa < 0f) { Emit(slot, a, p, q, interior); Band(p, q, across); }
            else { Emit(slot, p, b, c, interior); Emit(slot, p, c, q, interior); Band(p, q, -across); }
            return false;
        }

        private static void Rotate(ref V a, ref V b, ref V c, ref float fa, ref float fb, ref float fc)
        { V v = a; a = b; b = c; c = v; float f = fa; fa = fb; fb = fc; fc = f; }

        private static V Mid(V a, V b) => Lerp(a, b, 0.5f);
        private static V Lerp(V a, V b, float t) => new V
        { P = Vector3.LerpUnclamped(a.P, b.P, t), N = Vector3.LerpUnclamped(a.N, b.N, t).normalized, U = Vector2.LerpUnclamped(a.U, b.U, t) };

        private void Emit(int slot, V a, V b, V c, bool interior)
        {
            Add(slot, a.P, b.P, c.P, a.N, b.N, c.N, a.U, b.U, c.U);
            if (!interior) return;
            Vector3 ia = a.P - a.N * Thickness, ib = b.P - b.N * Thickness, ic = c.P - c.N * Thickness;
            // Interior faces look inward; storey bands give the void floors without extra geometry.
            Add(InteriorSlot, ia, ic, ib, -a.N, -c.N, -b.N, InteriorUv(ia), InteriorUv(ic), InteriorUv(ib));
        }

        private static Vector2 InteriorUv(Vector3 p) => new Vector2((p.x + p.z) * 0.21f, p.y / Storey);

        private void Band(V p, V q, Vector3 towardRemoved)
        {
            Vector3 along = q.P - p.P;
            if (along.sqrMagnitude < 1e-6f) return;
            Vector3 facing = towardRemoved - along * (Vector3.Dot(towardRemoved, along) / along.sqrMagnitude);
            facing = facing.sqrMagnitude > 1e-8f ? facing.normalized : Vector3.Cross(along, p.N).normalized;
            Vector3 pi = p.P - p.N * Thickness, qi = q.P - q.N * Thickness;
            float u0 = (p.P.x + p.P.y + p.P.z) * 0.5f, u1 = u0 + along.magnitude * 0.5f;
            Facing(BandSlot, p.P, q.P, qi, facing, new Vector2(u0, 0f), new Vector2(u1, 0f), new Vector2(u1, 0.3f));
            Facing(BandSlot, p.P, qi, pi, facing, new Vector2(u0, 0f), new Vector2(u1, 0.3f), new Vector2(u0, 0.3f));
        }

        private void Facing(int slot, Vector3 a, Vector3 b, Vector3 c, Vector3 n, Vector2 ua, Vector2 ub, Vector2 uc)
        {
            n = n.normalized;
            if (Vector3.Dot(Vector3.Cross(b - a, c - a), n) < 0f) Add(slot, a, c, b, n, n, n, ua, uc, ub);
            else Add(slot, a, b, c, n, n, n, ua, ub, uc);
        }

        private void Add(int slot, Vector3 a, Vector3 b, Vector3 c, Vector3 na, Vector3 nb, Vector3 nc, Vector2 ua, Vector2 ub, Vector2 uc)
        {
            int i = Positions.Count;
            Positions.Add(a); Positions.Add(b); Positions.Add(c);
            Normals.Add(na); Normals.Add(nb); Normals.Add(nc);
            Uvs.Add(ua); Uvs.Add(ub); Uvs.Add(uc);
            List<int> t = Triangles[slot]; t.Add(i); t.Add(i + 1); t.Add(i + 2);
            if (!any) { Bounds = new Bounds(a, Vector3.zero); any = true; }
            Bounds.Encapsulate(a); Bounds.Encapsulate(b); Bounds.Encapsulate(c);
        }

        internal int TriangleCount => Positions.Count / 3;

        /// <summary>Main thread only. Optionally recentres vertices on 'origin'.</summary>
        internal void Apply(Mesh mesh, Vector3 origin)
        {
            mesh.Clear();
            mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            if (origin != Vector3.zero) for (int i = 0; i < Positions.Count; i++) Positions[i] -= origin;
            mesh.SetVertices(Positions); mesh.SetNormals(Normals); mesh.SetUVs(0, Uvs);
            mesh.subMeshCount = Triangles.Length;
            for (int s = 0; s < Triangles.Length; s++) mesh.SetTriangles(Triangles[s], s, false);
            mesh.RecalculateBounds();
        }
    }
}
