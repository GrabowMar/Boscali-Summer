using System.Collections.Generic;
using BoscaliSummer.Core.Math;
using BoscaliSummer.Modules.FireAndDestruction.Domain;
using UnityEngine;
using UnityEngine.Rendering;

namespace BoscaliSummer.Fire
{
    /// <summary>
    /// Persistent rubble for a ruin. Slabs drop once onto a height field and freeze.
    /// The fallen facade is kept as static meshes (no colliders, no shadow casting)
    /// with a street bay. Only a nearby shell stays whole; the rest keep eight
    /// pieces out to 1600 m, then sleep. A later shot shoves one slab along its leash.
    /// Six piles, six slabs, oldest recycled. No rigidbodies.
    /// </summary>
    internal sealed class RuinDebrisPool
    {
        // ponytail: the two nearest shells stay whole inside 450 m; every shell
        // keeps eight pieces out to 1600 m. Per-piece LOD if a whole block is on screen.
        private const int CoarsePieces = 8;
        private const float FullDistanceSq = 450f * 450f;
        private const float ShellDistanceSq = 1600f * 1600f;
        private const float ShadowDistanceSq = 900f * 900f;
        private const float KickSeconds = 0.32f;
        private const float PokeGap = 0.18f;
        private const float Leash = 3.2f;
        internal struct FacadePiece
        {
            public Mesh Mesh;
            public Material[] Materials;
            public Vector3 Position;
            public Quaternion Rotation;
            public Vector3 Scale;
            public int Layer;
            public ShadowCastingMode Shadows;
            public bool ReceiveShadows;
        }

        private sealed class Pile
        {
            public GameObject Root;
            public Transform[] Pieces;
            public MeshRenderer[] Renderers;
            public Vector3[] Rest;
            public GameObject Shell;
            public Renderer[] Trim;
            public Vector3 Anchor;
            public Vector3 Face;
            public Vector2 Footprint;
            public int BornOrder;
            public int KickCursor;
            public int KickSlot;
            public float NextPoke;
            public float KickStart;
            public float KickUntil;
            public Vector3 KickFromPos;
            public Vector3 KickToPos;
            public Quaternion KickFromRot;
            public Quaternion KickToRot;
            public bool Kicking;
            public ShadowCastingMode SlabShadow;
        }

        private readonly List<Pile> piles = new List<Pile>(RuinDebrisMath.MaxPiles);
        private readonly float[] field = new float[RuinDebrisMath.Grid * RuinDebrisMath.Grid];
        private Material shared;
        private Texture2D concrete;
        private bool shaderSearched;
        private bool kicking;
        private int order;

        public void Place(GlobalPosition position, Vector2 halfExtents)
        {
            if (GameManager.IsHeadless) return;
            Pile pile = Acquire();
            if (pile == null) return;
            if (pile.Shell != null)
            {
                Object.Destroy(pile.Shell);
                pile.Shell = null;
            }
            pile.Trim = null;
            pile.Kicking = false;
            pile.NextPoke = 0f;
            Vector3 local = position.ToLocalPosition();
            float ground = local.y - 0.5f;
            float span = Mathf.Max(halfExtents.x, halfExtents.y) * 1.35f;
            if (span < 8f) span = 8f;
            Vector3 face = halfExtents.x >= halfExtents.y ? Vector3.forward : Vector3.right;
            Camera camera = Camera.main;
            if (camera != null && Vector3.Dot(camera.transform.position - local, face) < 0f)
                face = -face;
            float reach = Mathf.Abs(face.x) > 0.5f ? halfExtents.x : halfExtents.y;
            float rad = Mathf.Clamp(span * 0.42f, 5f, 14f);
            Vector3 origin = new Vector3(local.x, ground, local.z) + face * (reach + rad * 0.2f);
            Vector3 tangent = Vector3.Cross(Vector3.up, face);
            if (tangent.sqrMagnitude < 0.0001f) tangent = Vector3.right;
            tangent.Normalize();
            pile.Anchor = local;
            pile.Face = face;
            pile.Footprint = halfExtents;
            RuinDebrisMath.Clear(field);
            uint seed = Deterministic.Hash(
                Mathf.RoundToInt(position.x * 10f),
                Mathf.RoundToInt(position.z * 10f),
                91);
            for (int i = 0; i < RuinDebrisMath.ShardsPerPile; i++)
            {
                uint h = Deterministic.Hash((int)(seed ^ (uint)(i * 29 + 5)), i, 17);
                float along = (Deterministic.UnitFloat(h) - 0.5f) * rad * 0.46f;
                float len = rad * (i == 0 ? 0.44f : 0.2f + Deterministic.UnitFloat(h ^ 7u) * 0.16f);
                float wid = len * (0.5f + Deterministic.UnitFloat(h ^ 9u) * 0.4f);
                float thick = Mathf.Max(0.28f, len * 0.2f);
                Vector3 start = origin + Vector3.up * (rad * (0.28f + 0.05f * i)) + tangent * along;
                Vector3 vel = face * (0.3f + Deterministic.UnitFloat(h ^ 2u) * 0.8f)
                    + Vector3.down * (1.3f + Deterministic.UnitFloat(h ^ 4u) * 0.6f)
                    + tangent * ((Deterministic.UnitFloat(h ^ 8u) - 0.5f) * 0.8f);
                var shard = new RuinDebrisMath.Shard
                {
                    X = start.x,
                    Y = start.y,
                    Z = start.z,
                    Vx = vel.x,
                    Vy = vel.y,
                    Vz = vel.z,
                    Thick = thick,
                };
                RuinDebrisMath.Drop(ref shard, field, origin.x, origin.z, Mathf.Max(span, rad * 2.1f), ground);
                bool lean = i == 2 || i == 4;
                Vector3 scale = new Vector3(len, thick, wid);
                Quaternion rot = SlabRotation(face, h, lean);
                Vector3 half = scale * 0.5f;
                float lowest = 0f;
                for (int sx = -1; sx <= 1; sx += 2)
                    for (int sy = -1; sy <= 1; sy += 2)
                        for (int sz = -1; sz <= 1; sz += 2)
                        {
                            float cornerY = (rot * new Vector3(half.x * sx, half.y * sy, half.z * sz)).y;
                            if (cornerY < lowest) lowest = cornerY;
                        }
                Transform piece = pile.Pieces[i];
                float pileTop = shard.Y - shard.Thick * 0.5f;
                Vector3 settled = new Vector3(shard.X, pileTop - lowest, shard.Z);
                piece.SetPositionAndRotation(settled, rot);
                piece.localScale = scale;
                pile.Rest[i] = settled;
            }
            pile.Root.SetActive(true);
        }

        internal GameObject AttachShell(GlobalPosition position, List<FacadePiece> pieces, int buildingId)
        {
            if (GameManager.IsHeadless || pieces == null || pieces.Count == 0) return null;
            Vector3 local = position.ToLocalPosition();
            Pile pile = null;
            float best = 64f;
            for (int i = 0; i < piles.Count; i++)
            {
                float distance = (piles[i].Anchor - local).sqrMagnitude;
                if (distance >= best) continue;
                best = distance;
                pile = piles[i];
            }
            if (pile == null || pile.Shell != null || pile.Root == null) return null;

            var shell = new GameObject("BoscaliSummer.RuinShell");
            shell.transform.SetParent(pile.Root.transform, false);
            float yMin = float.MaxValue;
            float yMax = float.MinValue;
            var made = new Renderer[24];
            var bulk = new float[24];
            int kept = 0;
            for (int i = 0; i < pieces.Count && kept < 24; i++)
            {
                FacadePiece piece = pieces[i];
                if (piece.Mesh == null) continue;
                var go = new GameObject("shell");
                go.layer = piece.Layer;
                go.transform.SetParent(shell.transform, false);
                go.transform.SetPositionAndRotation(piece.Position, piece.Rotation);
                go.transform.localScale = piece.Scale;
                go.AddComponent<MeshFilter>().sharedMesh = piece.Mesh;
                var renderer = go.AddComponent<MeshRenderer>();
                if (piece.Materials != null && piece.Materials.Length > 0)
                    renderer.sharedMaterials = piece.Materials;
                // The live building already cast this shadow. Drawing it again for the
                // clone is a second shadow pass of the whole mesh.
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = piece.ReceiveShadows;
                made[kept] = renderer;
                bulk[kept] = Bulk(piece);
                EncapsulateY(piece, ref yMin, ref yMax);
                kept++;
            }
            if (kept == 0)
            {
                Object.Destroy(shell);
                return null;
            }
            pile.Shell = shell;
            pile.Trim = TrimPast(made, bulk, kept);
            float ground = pile.Anchor.y - 0.5f;
            float height = yMax > yMin ? yMax - yMin : 12f;
            float facade = Mathf.Abs(pile.Face.z) > 0.5f ? pile.Footprint.x * 2f : pile.Footprint.y * 2f;
            float rad = Mathf.Clamp(Mathf.Min(Mathf.Max(height, 1f) * 0.48f, Mathf.Max(facade, 1f) * 0.28f), 5f, 14f);
            float reach = Mathf.Abs(pile.Face.x) > 0.5f ? pile.Footprint.x : pile.Footprint.y;
            Vector3 wall = pile.Anchor + pile.Face * reach;
            wall.y = ground + rad * 0.5f;
            BreachCards.CreateBay(shell.transform, wall, pile.Face, rad);
            BuildingHitLedger.Instance?.AdoptBreaches(buildingId, shell.transform);
            return shell;
        }

        internal static List<FacadePiece> CaptureFacade(Component building)
        {
            var list = new List<FacadePiece>(8);
            if (building == null || GameManager.IsHeadless) return list;
            Renderer[] chosen = null;
            LODGroup lod = building.GetComponentInChildren<LODGroup>(true);
            if (lod != null)
            {
                LOD[] lods = lod.GetLODs();
                if (lods.Length > 0) chosen = lods[0].renderers;
            }
            if (chosen == null || chosen.Length == 0)
                chosen = building.GetComponentsInChildren<MeshRenderer>(false);
            for (int i = 0; i < chosen.Length; i++)
            {
                var renderer = chosen[i] as MeshRenderer;
                if (renderer == null) continue;
                MeshFilter filter = renderer.GetComponent<MeshFilter>();
                if (filter == null || filter.sharedMesh == null) continue;
                Transform t = renderer.transform;
                list.Add(new FacadePiece
                {
                    Mesh = filter.sharedMesh,
                    Materials = renderer.sharedMaterials,
                    Position = t.position,
                    Rotation = t.rotation,
                    Scale = t.lossyScale,
                    Layer = renderer.gameObject.layer,
                    Shadows = renderer.shadowCastingMode,
                    ReceiveShadows = renderer.receiveShadows,
                });
            }
            if (list.Count > 24)
            {
                list.Sort((a, b) => Bulk(b).CompareTo(Bulk(a)));
                list.RemoveRange(24, list.Count - 24);
            }
            return list;
        }

        public void Clear()
        {
            for (int i = 0; i < piles.Count; i++)
                if (piles[i].Root != null) Object.Destroy(piles[i].Root);
            piles.Clear();
            if (shared != null) Object.Destroy(shared);
            if (concrete != null) Object.Destroy(concrete);
            shared = null;
            concrete = null;
            shaderSearched = false;
            order = 0;
        }

        internal void Tick(float now)
        {
            if (!kicking) return;
            kicking = false;
            for (int i = 0; i < piles.Count; i++)
            {
                Pile pile = piles[i];
                if (!pile.Kicking) continue;
                Transform piece = pile.Pieces[pile.KickSlot];
                if (piece == null) { pile.Kicking = false; continue; }
                float span = pile.KickUntil - pile.KickStart;
                float u = span <= 0.0001f ? 1f : (now - pile.KickStart) / span;
                if (u >= 1f)
                {
                    piece.SetPositionAndRotation(pile.KickToPos, pile.KickToRot);
                    pile.Kicking = false;
                    continue;
                }
                kicking = true;
                float s = 1f - (1f - u) * (1f - u);
                piece.SetPositionAndRotation(
                    Vector3.Lerp(pile.KickFromPos, pile.KickToPos, s),
                    Quaternion.Slerp(pile.KickFromRot, pile.KickToRot, s));
            }
        }

        /// <summary>
        /// A shot near a wreck. One slab slides along its leash. When the shot lands
        /// on the wreck, the caller also gets a point on the street face for a new hole.
        /// </summary>
        internal bool Nudge(
            Vector3 point, float power, out bool struck,
            out Transform shell, out Vector3 holePoint, out Vector3 holeNormal, out float holeSize)
        {
            struck = false;
            shell = null;
            holePoint = point;
            holeNormal = Vector3.forward;
            holeSize = 0f;
            if (GameManager.IsHeadless || piles.Count == 0) return false;
            float now = Time.timeSinceLevelLoad;
            Pile pile = null;
            float best = float.MaxValue;
            for (int i = 0; i < piles.Count; i++)
            {
                Pile candidate = piles[i];
                float span = Mathf.Abs(candidate.Face.x) > 0.5f ? candidate.Footprint.x : candidate.Footprint.y;
                float limit = power < 1f
                    ? Mathf.Max(10f, span + 4f)
                    : Mathf.Clamp(span + 8f + power * 0.25f, 14f, 40f);
                Vector3 flat = candidate.Anchor - point;
                flat.y = 0f;
                float distance = flat.sqrMagnitude;
                if (distance >= limit * limit || distance >= best) continue;
                best = distance;
                pile = candidate;
            }
            if (pile == null || pile.Pieces == null || now < pile.NextPoke) return false;
            pile.NextPoke = now + PokeGap;
            int slot = pile.KickCursor % pile.Pieces.Length;
            pile.KickCursor = (slot + 1) % pile.Pieces.Length;
            Transform piece = pile.Pieces[slot];
            if (piece == null || pile.Rest == null) return false;
            Vector3 pos = piece.position;
            Vector3 away = pos - point;
            float x = pos.x;
            float z = pos.z;
            float yaw = RuinDebrisMath.Kick(
                ref x, ref z, away.x, away.z, power, pile.Rest[slot].x, pile.Rest[slot].z, Leash);
            float sign = away.x + away.z >= 0f ? 1f : -1f;
            pile.KickSlot = slot;
            pile.KickFromPos = pos;
            pile.KickFromRot = piece.rotation;
            pile.KickToPos = new Vector3(x, pos.y, z);
            pile.KickToRot = Quaternion.AngleAxis(yaw * sign, Vector3.up) * piece.rotation;
            pile.KickStart = now;
            pile.KickUntil = now + KickSeconds;
            pile.Kicking = true;
            kicking = true;
            float reach = Mathf.Abs(pile.Face.x) > 0.5f ? pile.Footprint.x : pile.Footprint.y;
            float struckR = Mathf.Max(reach, 4f) + 6f;
            struck = best <= struckR * struckR;
            if (!struck || pile.Shell == null) return true;
            shell = pile.Shell.transform;
            float facade = Mathf.Abs(pile.Face.z) > 0.5f ? pile.Footprint.x * 2f : pile.Footprint.y * 2f;
            Vector3 wall = pile.Anchor + pile.Face * reach;
            Vector3 tangent = Vector3.Cross(Vector3.up, pile.Face);
            if (tangent.sqrMagnitude < 0.0001f) tangent = Vector3.right;
            tangent.Normalize();
            float side = Mathf.Clamp(Vector3.Dot(point - wall, tangent), -facade * 0.32f, facade * 0.32f);
            float top = pile.Anchor.y + Mathf.Max(6f, facade * 0.45f);
            float y = Mathf.Clamp(point.y, pile.Anchor.y + 1.2f, top);
            holePoint = new Vector3(wall.x, y, wall.z) + tangent * side + pile.Face * 0.16f;
            holeNormal = pile.Face.sqrMagnitude > 0.0001f ? pile.Face : Vector3.forward;
            holeSize = power < 1f ? 2.6f : Mathf.Clamp(3.2f + power * 0.1f, 3.2f, 8f);
            return true;
        }

        internal void ApplyAttention(Vector3 cameraPosition)
        {
            if (piles.Count == 0) return;
            int first = -1;
            int second = -1;
            float firstD = float.MaxValue;
            float secondD = float.MaxValue;
            for (int i = 0; i < piles.Count; i++)
            {
                float distance = (piles[i].Anchor - cameraPosition).sqrMagnitude;
                if (distance < firstD)
                {
                    secondD = firstD;
                    second = first;
                    firstD = distance;
                    first = i;
                }
                else if (distance < secondD)
                {
                    secondD = distance;
                    second = i;
                }
            }
            for (int i = 0; i < piles.Count; i++)
            {
                Pile pile = piles[i];
                float distance = (pile.Anchor - cameraPosition).sqrMagnitude;
                bool show = distance < ShellDistanceSq;
                if (pile.Shell != null && pile.Shell.activeSelf != show)
                    pile.Shell.SetActive(show);
                if (show && pile.Trim != null)
                {
                    bool full = distance < FullDistanceSq && (i == first || i == second);
                    for (int t = 0; t < pile.Trim.Length; t++)
                        if (pile.Trim[t] != null) pile.Trim[t].enabled = full;
                }
                ShadowCastingMode mode = distance < ShadowDistanceSq
                    ? ShadowCastingMode.On : ShadowCastingMode.Off;
                if (pile.SlabShadow == mode || pile.Renderers == null) continue;
                pile.SlabShadow = mode;
                for (int s = 0; s < pile.Renderers.Length; s++)
                    if (pile.Renderers[s] != null) pile.Renderers[s].shadowCastingMode = mode;
            }
        }

        private static Renderer[] TrimPast(Renderer[] made, float[] bulk, int count)
        {
            if (count <= CoarsePieces) return null;
            var rank = new int[count];
            for (int i = 0; i < count; i++) rank[i] = i;
            for (int i = 0; i < count - 1; i++)
            {
                int best = i;
                for (int j = i + 1; j < count; j++)
                    if (bulk[rank[j]] > bulk[rank[best]]) best = j;
                int swap = rank[i];
                rank[i] = rank[best];
                rank[best] = swap;
            }
            var trim = new Renderer[count - CoarsePieces];
            for (int i = CoarsePieces; i < count; i++) trim[i - CoarsePieces] = made[rank[i]];
            return trim;
        }

        private static float Bulk(FacadePiece piece)
        {
            if (piece.Mesh == null) return 0f;
            Vector3 size = Vector3.Scale(piece.Mesh.bounds.size, piece.Scale);
            return Mathf.Abs(size.x * size.y * size.z);
        }

        private static void EncapsulateY(FacadePiece piece, ref float yMin, ref float yMax)
        {
            Bounds bounds = piece.Mesh.bounds;
            Vector3 center = bounds.center;
            Vector3 extents = bounds.extents;
            for (int x = -1; x <= 1; x += 2)
                for (int y = -1; y <= 1; y += 2)
                    for (int z = -1; z <= 1; z += 2)
                    {
                        Vector3 corner = center + Vector3.Scale(extents, new Vector3(x, y, z));
                        float worldY = (piece.Rotation * Vector3.Scale(corner, piece.Scale)).y + piece.Position.y;
                        if (worldY < yMin) yMin = worldY;
                        if (worldY > yMax) yMax = worldY;
                    }
        }

        private static Quaternion SlabRotation(Vector3 faceOut, uint seed, bool lean)
        {
            faceOut.y = 0f;
            if (faceOut.sqrMagnitude < 0.0001f) faceOut = Vector3.forward;
            faceOut.Normalize();
            float pitch = ((lean ? 38f : 22f) + Deterministic.UnitFloat(seed ^ 9u) * (lean ? 20f : 16f)) * Mathf.Deg2Rad;
            float yaw = (Deterministic.UnitFloat(seed ^ 12u) - 0.5f) * (lean ? 70f : 48f);
            Vector3 slabUp = faceOut * Mathf.Cos(pitch) + Vector3.up * Mathf.Sin(pitch);
            Vector3 slabRight = Vector3.Cross(slabUp, faceOut);
            if (slabRight.sqrMagnitude < 0.0001f) slabRight = Vector3.Cross(slabUp, Vector3.right);
            slabRight.Normalize();
            slabRight = Quaternion.AngleAxis(yaw, slabUp) * slabRight;
            Vector3 slabForward = Vector3.Cross(slabRight, slabUp);
            return Quaternion.LookRotation(slabForward, slabUp);
        }

        private Pile Acquire()
        {
            if (piles.Count < RuinDebrisMath.MaxPiles)
            {
                Pile created = Create();
                if (created == null) return null;
                created.BornOrder = ++order;
                piles.Add(created);
                return created;
            }
            int oldest = 0;
            for (int i = 1; i < piles.Count; i++)
                if (piles[i].BornOrder < piles[oldest].BornOrder) oldest = i;
            piles[oldest].BornOrder = ++order;
            return piles[oldest];
        }

        private Pile Create()
        {
            Material material = ResolveMaterial();
            var root = new GameObject("BoscaliSummer.RuinMound");
            root.transform.SetParent(Datum.origin, false);
            var pieces = new Transform[RuinDebrisMath.ShardsPerPile];
            var renderers = new MeshRenderer[RuinDebrisMath.ShardsPerPile];
            for (int i = 0; i < pieces.Length; i++)
            {
                var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
                Collider collider = cube.GetComponent<Collider>();
                if (collider != null) Object.DestroyImmediate(collider);
                cube.transform.SetParent(root.transform, false);
                MeshRenderer renderer = cube.GetComponent<MeshRenderer>();
                if (material != null) renderer.sharedMaterial = material;
                renderer.shadowCastingMode = ShadowCastingMode.On;
                renderer.receiveShadows = true;
                pieces[i] = cube.transform;
                renderers[i] = renderer;
            }
            return new Pile
            {
                Root = root,
                Pieces = pieces,
                Renderers = renderers,
                Rest = new Vector3[RuinDebrisMath.ShardsPerPile],
                SlabShadow = ShadowCastingMode.On,
            };
        }

        private Material ResolveMaterial()
        {
            if (shaderSearched) return shared;
            shaderSearched = true;
            Shader shader = Shader.Find("Universal Render Pipeline/Lit")
                ?? Shader.Find("Standard")
                ?? Shader.Find("Legacy Shaders/Diffuse")
                ?? Shader.Find("Diffuse");
            if (shader == null) return null;
            concrete = BuildConcrete();
            shared = new Material(shader) { color = Color.white };
            if (shared.HasProperty("_BaseMap"))
            {
                shared.SetTexture("_BaseMap", concrete);
                shared.SetTextureScale("_BaseMap", new Vector2(2f, 1f));
            }
            if (shared.HasProperty("_MainTex")) shared.SetTexture("_MainTex", concrete);
            if (shared.HasProperty("_BaseColor")) shared.SetColor("_BaseColor", Color.white);
            if (shared.HasProperty("_Smoothness")) shared.SetFloat("_Smoothness", 0.08f);
            if (shared.HasProperty("_Glossiness")) shared.SetFloat("_Glossiness", 0.08f);
            if (shared.HasProperty("_Metallic")) shared.SetFloat("_Metallic", 0f);
            shared.mainTextureScale = new Vector2(2f, 1f);
            return shared;
        }

        private static Texture2D BuildConcrete()
        {
            const int n = 128;
            var pixels = new Color32[n * n];
            for (int y = 0; y < n; y++)
            {
                for (int x = 0; x < n; x++)
                {
                    float noise = Deterministic.UnitFloat(Deterministic.Hash(x, y, 3));
                    float shade = 0.82f + noise * 0.26f;
                    if (y % 24 == 0 || x % 56 == 0) shade *= 0.7f;
                    pixels[y * n + x] = new Color32(
                        (byte)Mathf.Clamp(0.62f * shade * 255f, 0f, 255f),
                        (byte)Mathf.Clamp(0.50f * shade * 255f, 0f, 255f),
                        (byte)Mathf.Clamp(0.42f * shade * 255f, 0f, 255f),
                        255);
                }
            }
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false)
            {
                name = "BoscaliSummer.Rubble",
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Bilinear,
            };
            tex.SetPixels32(pixels);
            tex.Apply(false, true);
            return tex;
        }
    }
}
