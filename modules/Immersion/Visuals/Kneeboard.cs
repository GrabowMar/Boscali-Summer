using System;
using System.Collections.Generic;
using System.Diagnostics;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Modules.Immersion.Domain;
using NOAvionics;
using TMPro;
using UnityEngine;
using Object = UnityEngine.Object;

namespace BoscaliSummer.Modules.Immersion.Visuals
{
    /// <summary>
    /// Thigh kneeboard: a small strapped clipboard on the thigh away from the stick. Its mission card is drawn once per aircraft
    /// (and again on rearm / loadout change) straight into a 512 px texture: one text mesh, one immediate draw,
    /// no camera and no canvas (a temporary URP camera cost 23 ms in game). Steady state is two draw calls (card, board)
    /// and a bool check. If a warm redraw ever exceeds 2 ms, later cards keep the last one.
    /// </summary>
    internal sealed class Kneeboard
    {
        private const int Size = 512;
        private const float ThighAlong = .62f, ThighLift = .10961551f, ThighPitch = 5.154955f;
        // Lit by the cockpit sun: mid-grey paper and near-black bold ink keep contrast without blowing out.
        private static readonly Color Paper = new Color(.4f, .39f, .34f), Ink = new Color(.02f, .022f, .02f);
        // Worn paper tint also tames the brighter gallery pictures.
        private static readonly Color PaperTint = new Color(.8f, .78f, .7f);
        private static readonly int BaseMapId = Shader.PropertyToID("_BaseMap"), LightId = Shader.PropertyToID("_LightLevel");
        private readonly List<KeyValuePair<string, int>> loadout = new List<KeyValuePair<string, int>>(12);
        private readonly Action markDirty;
        private readonly KneeboardSchedule schedule = new KneeboardSchedule();
        private GameObject quad;
        private Mesh mesh;
        private Material material, hardware, cardInk;
        private RenderTexture card;
        private Texture2D plain, hardwarePalette;
        private Aircraft aircraft;
        private bool cardValid, warned;
        /// <summary>Picked in SQD STUDIO: picture cards replace the written mission card (and need no text render).</summary>
        internal IKneeboardGallery Gallery;

        internal Kneeboard() { markDirty = () => schedule.MarkDirty(); }

        internal bool Bound => quad != null;
        internal Renderer Renderer { get; private set; }
        internal float LastRenderMs { get; private set; } = -1f;
        internal float LastLayoutMs { get; private set; } = -1f;
        internal float LastDrawMs { get; private set; } = -1f;
        internal int Renders { get; private set; }
        internal void MarkDirty() => schedule.MarkDirty();
        internal string Side { get; private set; } = "none";
        internal bool RightThigh { get; private set; }

        /// <summary>True once per cockpit entry: a failed bind is not retried every frame.</summary>
        internal bool TryBeginBind() => schedule.BeginBind();

        /// <summary>Places the card on the thigh in its current (seated) pose; parenting keeps it there as the rig moves.
        /// The same aircraft keeps its rendered card across cockpit re-entries.</summary>
        internal void Bind(Transform thigh, Transform knee, Vector3 up, bool rightThigh, Aircraft owner, Shader shader)
        {
            Detach();
            schedule.BeginBind(); // Internal cleanup must not re-arm a failed bind every frame.
            if (thigh == null || knee == null || owner == null || shader == null || !shader.isSupported) return;
            Vector3 along = knee.position - thigh.position;
            if (along.sqrMagnitude < .01f) return;
            Vector3 forward = along.normalized;
            Vector3 top = Vector3.ProjectOnPlane(up, forward).normalized;
            if (top.sqrMagnitude < .5f) return;
            if (!ReferenceEquals(owner, aircraft))
            {
                Unsubscribe();
                aircraft = owner;
                cardValid = false;
                owner.OnRearmUnit += markDirty;
                owner.onLoadoutChanged += markDirty;
            }
            schedule.Bound(owner.GetInstanceID());
            Side = rightThigh ? "right" : "left";
            RightThigh = rightThigh;
            quad = new GameObject("Boscali.Kneeboard") { layer = 3 }; // PhysicsLayers.Cockpit, like the body
            // Origin is the board's underside resting on the thigh; local -z points up out of the leg, +y toward the knee.
            quad.transform.SetPositionAndRotation(thigh.position + along * ThighAlong + top * ThighLift,
                Quaternion.LookRotation(-top, forward) * Quaternion.Euler(ThighPitch, 0, 0));
            quad.transform.SetParent(thigh, true);
            mesh = BoardMesh(rightThigh);
            quad.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = quad.AddComponent<MeshRenderer>();
            Renderer = renderer;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            renderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            if (material == null)
            {
                material = BodyMaterial(shader, "Boscali.Kneeboard", PaperTint, .05f);
                hardware = BodyMaterial(shader, "Boscali.KneeboardBoard", Color.white, .22f);
                hardwarePalette = new Texture2D(8, 1, TextureFormat.RGBA32, false)
                    { name = "Boscali.KneeboardHardware", filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
                hardwarePalette.SetPixels(new[] {
                    new Color(.075f, .085f, .073f), // olive composite shell
                    new Color(.035f, .041f, .035f), // elastic and padded saddle
                    new Color(.27f, .29f, .27f),    // brushed clip / buckles
                    new Color(.46f, .44f, .37f),    // exposed paper edges
                    new Color(.28f, .19f, .07f),    // ochre grease pencil
                    new Color(.10f, .11f, .095f),   // stitched edging
                    new Color(.018f, .021f, .018f), // pencil tip / clip grip
                    new Color(.36f, .34f, .25f) }); // stitch thread
                hardwarePalette.Apply(false, true);
                hardware.SetTexture(BaseMapId, hardwarePalette);
                if (PilotShaderBundle.GetKneeboardTextures(out Texture2D albedo, out Texture2D normalMap, out Texture2D mask))
                {
                    hardware.SetTexture(BaseMapId, albedo);
                    hardware.SetTexture("_BumpMap", normalMap);
                    hardware.SetTexture("_MaskMap", mask);
                    hardware.SetFloat("_HasNormalMap", 1f);
                    hardware.SetFloat("_OwnedMaps", 1f);
                }
            }
            renderer.sharedMaterials = new[] { material, hardware };
            if (!ApplyPicture() && cardValid) material.SetTexture(BaseMapId, card);
        }

        private bool ApplyPicture()
        {
            Texture2D picture = Gallery != null ? Gallery.Card(Gallery.Selected) : null;
            if (picture == null) return false;
            material.SetTexture(BaseMapId, picture);
            return true;
        }

        /// <summary>Cheap per-frame check; the card is re-rendered only for a new aircraft, a rearm or a loadout change.</summary>
        internal void Tick()
        {
            if (quad == null || !schedule.TakeRender()) return;
            Render();
        }

        internal void SetVisible(bool visible) { if (quad != null && quad.activeSelf != visible) quad.SetActive(visible); }
        internal void SetLight(float level)
        {
            if (material != null) material.SetFloat(LightId, level);
            if (hardware != null) hardware.SetFloat(LightId, level);
        }

        private static Material BodyMaterial(Shader shader, string name, Color tint, float smoothness)
        {
            var made = new Material(shader) { name = name, hideFlags = HideFlags.HideAndDontSave };
            made.SetFloat("_BodyVisible", 1f);
            made.SetFloat("_HeadMaskMode", 0f);
            made.SetFloat("_Smoothness", smoothness);
            made.SetColor("_BaseColor", tint);
            return made;
        }

        /// <summary>One mesh / two draws: the card and a palette-mapped flight kneeboard. The saddle and two complete
        /// elastic bands follow the thigh cross-section; the shell, paper edges, clip and pencil share the hardware draw.</summary>
        private static Mesh BoardMesh(bool rightThigh)
        {
            var vertices = new List<Vector3>(1800);
            var normals = new List<Vector3>(1800);
            var uvs = new List<Vector2>(1800);
            var card = new List<int>(6);
            var board = new List<int>(3000);
            void Face(List<int> into, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 n, int swatch = 0, bool mapped = false)
            {
                int i = vertices.Count;
                vertices.Add(a); vertices.Add(b); vertices.Add(c); vertices.Add(d);
                normals.Add(n); normals.Add(n); normals.Add(n); normals.Add(n);
                float left = (swatch + .04f) / 8f, right = (swatch + .96f) / 8f;
                uvs.Add(mapped ? new Vector2(0, 0) : new Vector2(left, .04f)); uvs.Add(mapped ? new Vector2(1, 0) : new Vector2(right, .04f));
                uvs.Add(mapped ? new Vector2(1, 1) : new Vector2(right, .96f)); uvs.Add(mapped ? new Vector2(0, 1) : new Vector2(left, .96f));
                bool flip = Vector3.Dot(Vector3.Cross(b - a, c - a), n) < 0f;
                if (flip) into.AddRange(new[] { i, i + 2, i + 1, i, i + 3, i + 2 });
                else into.AddRange(new[] { i, i + 1, i + 2, i, i + 2, i + 3 });
            }
            void Triangle(Vector3 a, Vector3 b, Vector3 c, Vector3 n, int swatch)
            {
                int i = vertices.Count;
                vertices.Add(a); vertices.Add(b); vertices.Add(c);
                normals.Add(n); normals.Add(n); normals.Add(n);
                uvs.Add(new Vector2((swatch + .5f) / 8f, .5f));
                uvs.Add(new Vector2((swatch + .04f) / 8f, .04f)); uvs.Add(new Vector2((swatch + .96f) / 8f, .04f));
                board.Add(i);
                bool flip = Vector3.Dot(Vector3.Cross(b - a, c - a), n) < 0f;
                board.Add(i + (flip ? 2 : 1)); board.Add(i + (flip ? 1 : 2));
            }
            void Box(Vector3 min, Vector3 max, int swatch)
            {
                Vector3 center = (min + max) * .5f, half = (max - min) * .5f;
                float bevel = Mathf.Min(.0006f, Mathf.Min(half.x, Mathf.Min(half.y, half.z)) * .35f);
                // A real highlight-bearing edge on the clip, keepers and paper stack.
                for (int axis = 0; axis < 3; axis++)
                {
                    int u = (axis + 1) % 3, v = (axis + 2) % 3;
                    for (int side = -1; side <= 1; side += 2)
                    {
                        Vector3 Point(float a, float b)
                        {
                            Vector3 p = center; p[axis] += side * half[axis];
                            p[u] += a * (half[u] - bevel); p[v] += b * (half[v] - bevel); return p;
                        }
                        Vector3 n = Vector3.zero; n[axis] = side;
                        Face(board, Point(-1, -1), Point(1, -1), Point(1, 1), Point(-1, 1), n, swatch);
                    }
                    for (int su = -1; su <= 1; su += 2) for (int sv = -1; sv <= 1; sv += 2)
                    {
                        Vector3 Edge(bool faceU, float end)
                        {
                            Vector3 p = center; p[axis] += end * (half[axis] - bevel);
                            p[u] += su * (half[u] - (faceU ? 0 : bevel));
                            p[v] += sv * (half[v] - (faceU ? bevel : 0)); return p;
                        }
                        Vector3 n = Vector3.zero; n[u] = su; n[v] = sv;
                        Face(board, Edge(true, -1), Edge(false, -1), Edge(false, 1), Edge(true, 1), n.normalized, swatch);
                    }
                }
                for (int x = -1; x <= 1; x += 2) for (int y = -1; y <= 1; y += 2) for (int z = -1; z <= 1; z += 2)
                    Triangle(center + new Vector3(x * half.x, y * (half.y - bevel), z * (half.z - bevel)),
                        center + new Vector3(x * (half.x - bevel), y * half.y, z * (half.z - bevel)),
                        center + new Vector3(x * (half.x - bevel), y * (half.y - bevel), z * half.z), new Vector3(x, y, z).normalized, swatch);
            }
            // Chamfered shell: the bevel catches the cockpit light without a blocky square silhouette.
            var rim = new[] { new Vector2(-.068f, -.100f), new Vector2(.068f, -.100f), new Vector2(.076f, -.092f),
                new Vector2(.076f, .092f), new Vector2(.068f, .100f), new Vector2(-.068f, .100f),
                new Vector2(-.076f, .092f), new Vector2(-.076f, -.092f) };
            for (int i = 0; i < rim.Length; i++)
            {
                Vector2 a = rim[i], b = rim[(i + 1) % rim.Length];
                Vector2 insetA = a * .98f, insetB = b * .98f;
                Vector3 pa = new Vector3(a.x, a.y, -.004f), pb = new Vector3(b.x, b.y, -.004f);
                Vector3 qa = new Vector3(insetA.x, insetA.y, -.006f), qb = new Vector3(insetB.x, insetB.y, -.006f);
                Vector3 side = new Vector3(b.y - a.y, a.x - b.x, 0).normalized;
                Triangle(Vector3.back * .006f, qa, qb, Vector3.back, 0);
                Face(board, pa, pb, qb, qa, (side + Vector3.back).normalized);
                Face(board, new Vector3(a.x, a.y, 0), new Vector3(b.x, b.y, 0), pb, pa, side);
                Triangle(Vector3.zero, new Vector3(b.x, b.y, 0), new Vector3(a.x, a.y, 0), Vector3.forward, 0);
            }
            // Measured original mesh, including mixed thigh/knee weights. Left/right cloth folds differ.
            // The writing surface follows the original thigh taper. Rows run toward the knee.
            float[] contactX = { -.055f, -.040f, -.020f, 0, .020f, .040f, .055f };
            float[,] skin = rightThigh ? new float[,] {
                { .04921f, .04054f, .01243f, .00642f, .01346f, .02095f, .02656f },
                { .04705f, .03993f, .01165f, .00631f, .01291f, .02039f, .02600f },
                { .04618f, .03596f, .01085f, .00620f, .01271f, .01984f, .02545f },
                { .04709f, .03817f, .01005f, .00608f, .01260f, .01928f, .02490f },
                { .04946f, .04013f, .00925f, .00597f, .01248f, .01899f, .02434f },
                { .04885f, .03822f, .00844f, .00586f, .01237f, .01888f, .02379f },
                { .04460f, .03046f, .00764f, .00575f, .01226f, .01877f, .02365f }
            } : new float[,] {
                { .02693f, .02179f, .01493f, .00807f, .01246f, .03946f, .04806f },
                { .02705f, .02190f, .01504f, .00818f, .01210f, .04064f, .04709f },
                { .02723f, .02201f, .01515f, .00829f, .01170f, .03854f, .04672f },
                { .02743f, .02217f, .01527f, .00841f, .01129f, .03858f, .04858f },
                { .02763f, .02236f, .01538f, .00852f, .01088f, .03833f, .05062f },
                { .02782f, .02256f, .01555f, .00863f, .01048f, .03550f, .04969f },
                { .02802f, .02276f, .01574f, .00875f, .01007f, .02991f, .04647f }
            };
            float Saddle(float x, float y)
            {
                int col = 0;
                while (col < 5 && x > contactX[col + 1]) col++;
                float tx = Mathf.InverseLerp(contactX[col], contactX[col + 1], x);
                float row = Mathf.InverseLerp(-.075f, .075f, y) * 6;
                int r = Mathf.Min(5, (int)row);
                return Mathf.Lerp(Mathf.Lerp(skin[r, col], skin[r, col + 1], tx),
                    Mathf.Lerp(skin[r + 1, col], skin[r + 1, col + 1], tx), row - r);
            }
            // A smaller foam cradle leaves a normal shell overhang instead of a deep block at the leg edges.
            const int saddleSteps = 12, saddleRows = 6;
            for (int row = 0; row < saddleRows; row++) for (int i = 0; i < saddleSteps; i++)
            {
                float x0 = Mathf.Lerp(-.055f, .055f, i / (float)saddleSteps), x1 = Mathf.Lerp(-.055f, .055f, (i + 1f) / saddleSteps);
                float y0 = Mathf.Lerp(-.075f, .075f, row / (float)saddleRows), y1 = Mathf.Lerp(-.075f, .075f, (row + 1f) / saddleRows);
                Vector3 a = new Vector3(x0, y0, Saddle(x0, y0)), b = new Vector3(x1, y0, Saddle(x1, y0));
                Vector3 c = new Vector3(x1, y1, Saddle(x1, y1)), d = new Vector3(x0, y1, Saddle(x0, y1));
                Face(board, a, b, c, d, Vector3.Cross(b - a, d - a).normalized, 1);
                if (row == 0) Face(board, new Vector3(x0, y0, 0), new Vector3(x1, y0, 0), b, a, Vector3.down, 1);
                if (row == saddleRows - 1) Face(board, new Vector3(x0, y1, 0), d, c, new Vector3(x1, y1, 0), Vector3.up, 1);
                if (i == 0) Face(board, new Vector3(x0, y0, 0), a, d, new Vector3(x0, y1, 0), Vector3.left, 1);
                if (i == saddleSteps - 1) Face(board, new Vector3(x1, y0, 0), new Vector3(x1, y1, 0), c, b, Vector3.right, 1);
            }
            // Four measured taut contours surround the original thigh across each complete band width.
            float[] wrap = rightThigh ? new[] {
                -0.00324338f, 0.00372354f, -0.00294933f, 0.00375250f, 0.05936663f, 0.02265584f, 0.07997687f, 0.03367224f,
                0.08079601f, 0.03434449f, 0.08238103f, 0.03627585f, 0.09007677f, 0.05067355f, 0.09110542f, 0.05406457f,
                0.09510340f, 0.09465668f, 0.08989879f, 0.14749990f, 0.08027904f, 0.17921196f, 0.06908086f, 0.20016229f,
                0.04982936f, 0.22362031f, 0.03954141f, 0.23206342f, 0.02833696f, 0.23805232f, 0.01670446f, 0.24158100f,
                0.00778944f, 0.24245905f, -0.01097320f, 0.24061109f, -0.02130898f, 0.23747577f, -0.03646732f, 0.22937347f,
                -0.05759784f, 0.21203210f, -0.07202358f, 0.19445429f, -0.08377060f, 0.17247716f, -0.08925154f, 0.15440890f,
                -0.09257006f, 0.12071541f, -0.08998117f, 0.09442992f, -0.08369935f, 0.07372153f, -0.07917341f, 0.06525409f,
                -0.05606600f, 0.03709763f, -0.02489311f, 0.01151470f, -0.01933848f, 0.00854569f, -0.00353743f, 0.00375250f,
                -0.00711651f, 0.00193890f, -0.00682246f, 0.00196786f, 0.06487596f, 0.02371734f, 0.07569055f, 0.02949786f,
                0.07650970f, 0.03017012f, 0.08201273f, 0.03687558f, 0.08731466f, 0.04679480f, 0.08769370f, 0.04804430f,
                0.08939481f, 0.06531597f, 0.08101868f, 0.15036023f, 0.07591900f, 0.16717161f, 0.06869962f, 0.18067813f,
                0.05718112f, 0.19471346f, 0.04238102f, 0.20685959f, 0.03726520f, 0.20959405f, 0.02175635f, 0.21429861f,
                0.01689637f, 0.21477728f, -0.01094468f, 0.21203517f, -0.03636606f, 0.20432368f, -0.04844402f, 0.19786788f,
                -0.06514450f, 0.18416215f, -0.06909125f, 0.17935302f, -0.07899898f, 0.16081695f, -0.08548039f, 0.13945061f,
                -0.08724743f, 0.12150958f, -0.08398787f, 0.08841468f, -0.07885722f, 0.07150119f, -0.07649273f, 0.06707754f,
                -0.02898842f, 0.00919337f, -0.02876001f, 0.00900593f, -0.02632980f, 0.00770695f, -0.00741056f, 0.00196786f,
            } : new[] {
                0.00529931f, 0.00487962f, 0.00559336f, 0.00490859f, 0.01999101f, 0.00927606f, 0.02676444f, 0.01289653f,
                0.05454865f, 0.03569845f, 0.08175311f, 0.06884718f, 0.08308469f, 0.07133840f, 0.08906455f, 0.09105133f,
                0.09175793f, 0.11839774f, 0.08817148f, 0.15481161f, 0.08270252f, 0.17284036f, 0.06925702f, 0.19799513f,
                0.06263423f, 0.20606502f, 0.03553588f, 0.22830406f, 0.01581388f, 0.23884568f, 0.01401468f, 0.23939147f,
                -0.00757462f, 0.24151783f, -0.01141638f, 0.24113945f, -0.02808246f, 0.23608384f, -0.03795400f, 0.23080740f,
                -0.06007850f, 0.21265029f, -0.07051302f, 0.19993579f, -0.08335122f, 0.17591720f, -0.09187839f, 0.14780689f,
                -0.09564286f, 0.10958567f, -0.09009233f, 0.05323025f, -0.09000656f, 0.05294750f, -0.08161333f, 0.03724486f,
                -0.07875369f, 0.03376038f, -0.07852528f, 0.03357294f, -0.06374760f, 0.02567410f, 0.00456379f, 0.00495207f,
                0.00832035f, 0.00436670f, 0.00861440f, 0.00439566f, 0.02415626f, 0.00911023f, 0.02957110f, 0.01200452f,
                0.02979951f, 0.01219197f, 0.07743277f, 0.07023327f, 0.07969977f, 0.07447452f, 0.08406361f, 0.08886017f,
                0.08734029f, 0.12212890f, 0.08564395f, 0.13935220f, 0.07874712f, 0.16208798f, 0.07133641f, 0.17595244f,
                0.06359673f, 0.18538327f, 0.05078762f, 0.19589544f, 0.03177068f, 0.20606020f, 0.01704200f, 0.21052810f,
                -0.01752421f, 0.21393257f, -0.02017292f, 0.21367170f, -0.04111024f, 0.20732043f, -0.04257178f, 0.20653922f,
                -0.05859869f, 0.19338628f, -0.07058840f, 0.17877678f, -0.07559793f, 0.16940461f, -0.08158891f, 0.14965498f,
                -0.08911021f, 0.07328996f, -0.08694199f, 0.05127561f, -0.08685622f, 0.05099286f, -0.08216632f, 0.04221868f,
                -0.07485393f, 0.03330851f, -0.07462552f, 0.03312106f, -0.05843062f, 0.02446471f, 0.00758482f, 0.00443914f,
            };
            // Broad elastic straps go all the way around the thigh. Their upper runs sit beneath the shell.
            for (int band = -1; band <= 1; band += 2)
            {
                float y0 = band * .058f - .013f, y1 = y0 + .026f;
                const int segments = 32;
                Vector2 WrapPoint(int i)
                {
                    int k = (band < 0 ? 0 : 64) + ((i + segments) % segments) * 2;
                    return new Vector2(wrap[k], wrap[k + 1]);
                }
                Vector3 Ring(int i, float y, float thickness)
                {
                    Vector2 p = WrapPoint(i), before = p - WrapPoint(i - 1), after = WrapPoint(i + 1) - p;
                    Vector2 normal = (new Vector2(before.y, -before.x).normalized + new Vector2(after.y, -after.x).normalized).normalized;
                    p += normal * thickness;
                    return new Vector3(p.x, y, p.y);
                }
                for (int i = 0; i < segments; i++)
                {
                    int a = i, b = i + 1;
                    Vector2 edge = WrapPoint(b) - WrapPoint(a);
                    Vector3 n = new Vector3(edge.y, 0, -edge.x).normalized;
                    Face(board, Ring(a, y0, .0015f), Ring(b, y0, .0015f), Ring(b, y1, .0015f), Ring(a, y1, .0015f), n, 1);
                    Face(board, Ring(a, y0, 0), Ring(a, y1, 0), Ring(b, y1, 0), Ring(b, y0, 0), -n, 1);
                    Face(board, Ring(a, y0, 0), Ring(b, y0, 0), Ring(b, y0, .0015f), Ring(a, y0, .0015f), Vector3.down, 5);
                    Face(board, Ring(a, y1, 0), Ring(a, y1, .0015f), Ring(b, y1, .0015f), Ring(b, y1, 0), Vector3.up, 5);
                }
                // Flush keepers at the board edge visually tie the elastic into the shell.
                for (int side = -1; side <= 1; side += 2)
                {
                    float joinZ = float.MaxValue, joinX = side * .078f;
                    for (int i = 0; i < segments; i++)
                    {
                        Vector2 a = WrapPoint(i), b = WrapPoint(i + 1);
                        if (joinX < Mathf.Min(a.x, b.x) || joinX > Mathf.Max(a.x, b.x) || Mathf.Abs(a.x - b.x) < .000001f) continue;
                        joinZ = Mathf.Min(joinZ, Mathf.Lerp(a.y, b.y, (joinX - a.x) / (b.x - a.x)));
                    }
                    Face(board, new Vector3(side * .075f, y0, -.004f), new Vector3(side * .075f, y1, -.004f),
                        new Vector3(side * .078f, y1, joinZ), new Vector3(side * .078f, y0, joinZ), Vector3.right * side, 1);
                    Box(new Vector3(side * .073f - .003f, y0 - .002f, -.006f),
                        new Vector3(side * .073f + .003f, y1 + .002f, -.002f), 2);
                }
            }
            // A paper block, thin exposed lower leaves, and a recessed printed sheet retain the existing card UVs.
            Box(new Vector3(-.0658f, -.086f, -.0085f), new Vector3(.0658f, .080f, -.0062f), 3);
            const float cardX = .065f, cardLow = -.085f, cardHigh = .079f, cardZ = -.009f;
            Face(card, new Vector3(-cardX, cardLow, cardZ), new Vector3(cardX, cardLow, cardZ), new Vector3(cardX, cardHigh, cardZ),
                new Vector3(-cardX, cardHigh, cardZ), Vector3.back, mapped: true);
            // Spring clip: a narrow bridge, raised hinge and two jaws, leaving the mission heading exposed.
            Box(new Vector3(-.024f, .079f, -.0115f), new Vector3(.024f, .092f, -.007f), 2);
            Box(new Vector3(-.024f, .081f, -.015f), new Vector3(-.019f, .091f, -.011f), 6);
            Box(new Vector3(.019f, .081f, -.015f), new Vector3(.024f, .091f, -.011f), 6);
            Box(new Vector3(-.018f, .089f, -.016f), new Vector3(.018f, .094f, -.0115f), 2);
            // Secured grease pencil on the outer rail, with a dark point and metal pocket clip.
            void Pencil(float low, float high, float radius, int swatch)
            {
                const int sides = 8;
                for (int i = 0; i < sides; i++)
                {
                    float a = i * Mathf.PI * 2 / sides, b = (i + 1) * Mathf.PI * 2 / sides;
                    Vector3 p(float angle, float y) => new Vector3(.070f + Mathf.Sin(angle) * radius, y, -.012f + Mathf.Cos(angle) * radius);
                    Vector3 n = new Vector3(Mathf.Sin((a + b) * .5f), 0, Mathf.Cos((a + b) * .5f));
                    Face(board, p(a, low), p(b, low), p(b, high), p(a, high), n, swatch);
                    Triangle(new Vector3(.070f, high, -.012f), p(a, high), p(b, high), Vector3.up, swatch);
                }
            }
            Pencil(-.069f, .050f, .0028f, 4);
            Pencil(.036f, .053f, .0032f, 6);
            for (int i = 0; i < 8; i++)
            {
                float a = i * Mathf.PI * .25f, b = (i + 1) * Mathf.PI * .25f;
                Triangle(new Vector3(.070f + Mathf.Sin(a) * .0028f, -.069f, -.012f + Mathf.Cos(a) * .0028f),
                    new Vector3(.070f, -.078f, -.012f),
                    new Vector3(.070f + Mathf.Sin(b) * .0028f, -.069f, -.012f + Mathf.Cos(b) * .0028f),
                    new Vector3(Mathf.Sin((a + b) * .5f), -.3f, Mathf.Cos((a + b) * .5f)).normalized, 4);
            }
            Pencil(-.079f, -.076f, .00065f, 6);
            Box(new Vector3(.066f, -.042f, -.015f), new Vector3(.074f, -.036f, -.011f), 1);
            Box(new Vector3(.066f, .015f, -.015f), new Vector3(.074f, .021f, -.011f), 1);
            Box(new Vector3(.068f, .030f, -.016f), new Vector3(.070f, .051f, -.014f), 2);
            var built = new Mesh { name = "Boscali.Kneeboard", subMeshCount = 2 };
            built.SetVertices(vertices); built.SetNormals(normals); built.SetUVs(0, uvs);
            built.SetTriangles(card, 0); built.SetTriangles(board, 1);
            built.RecalculateTangents(); built.RecalculateBounds();
            return built;
        }

        /// <summary>Leaving the cockpit view: drop the quad, keep the aircraft's rendered card and its rearm subscription.</summary>
        internal void Detach()
        {
            if (quad != null) Object.Destroy(quad);
            if (mesh != null) Object.Destroy(mesh);
            quad = null; mesh = null; Renderer = null; Side = "none";
            schedule.Detach();
        }

        internal void Release()
        {
            Detach();
            Unsubscribe();
            if (material != null) Object.Destroy(material);
            if (hardware != null) Object.Destroy(hardware);
            if (cardInk != null) Object.Destroy(cardInk);
            cardInk = null;
            hardware = null;
            if (card != null) { card.Release(); Object.Destroy(card); }
            if (plain != null) Object.Destroy(plain);
            if (hardwarePalette != null) Object.Destroy(hardwarePalette);
            material = null; card = null; plain = null; hardwarePalette = null; aircraft = null; cardValid = false;
            schedule.Reset();
        }

        private void Unsubscribe()
        {
            // A destroyed aircraft still holds the managed delegate list; detach regardless of Unity's null.
            if ((object)aircraft == null) return;
            aircraft.OnRearmUnit -= markDirty;
            aircraft.onLoadoutChanged -= markDirty;
        }

        private void Render()
        {
            if (ApplyPicture()) return;
            string[] lines = KneeboardCard.Lines(aircraft.unitName, aircraft.definition != null ? aircraft.definition.unitName : null,
                Loadout(), HomeBase(), MissionManager.CurrentMission != null ? MissionManager.CurrentMission.Name : "Free flight", TimeOfDay());
            if (schedule.UseCamera)
            {
                var watch = Stopwatch.StartNew();
                bool rendered = RenderCard(lines);
                LastRenderMs = (float)watch.Elapsed.TotalMilliseconds;
                schedule.Recorded(LastRenderMs);
                Renders++;
                if (rendered) cardValid = true;
                if (!schedule.UseCamera && !warned)
                {
                    warned = true;
                    Plugin.Logger?.LogWarning($"[Immersion] Kneeboard card took {LastRenderMs:0.0} ms; later cards keep the last one.");
                }
            }
            // Over budget: keep the last rendered card (stale after a rearm) rather than blanking it.
            material.SetTexture(BaseMapId, cardValid ? (Texture)card : PlainCard());
        }

        private bool RenderCard(string[] lines)
        {
            TMP_FontAsset font = AvType.Face(AvFace.Mono);
            if (font == null) return false;
            if (card == null)
            {
                card = new RenderTexture(Size, Size, 16, RenderTextureFormat.ARGB32)
                    { name = "Boscali.KneeboardCard", useMipMap = true, autoGenerateMips = true, filterMode = FilterMode.Trilinear, anisoLevel = 4 };
                card.Create();
            }
            GameObject holder = null;
            RenderTexture previous = RenderTexture.active;
            bool pushed = false;
            try
            {
                // A 3D text object only to lay the mesh out; it is destroyed before any camera can see it.
                holder = new GameObject("Boscali.KneeboardText");
                holder.transform.position = new Vector3(0f, -50000f, 0f);
                var text = holder.AddComponent<TextMeshPro>();
                text.font = font;
                text.isOrthographic = true; // one font point = one pixel of the card
                text.fontSize = 26f;
                text.enableAutoSizing = true;
                text.fontSizeMin = 18f;
                text.fontSizeMax = 26f;
                text.fontStyle = FontStyles.Normal;
                text.color = Ink;
                text.enableWordWrapping = false;
                text.richText = false;
                text.alignment = TextAlignmentOptions.TopLeft;
                const float left = Size * .06f, right = Size * .97f, bottom = Size * .04f, top = Size * .92f;
                text.rectTransform.sizeDelta = new Vector2(right - left, top - bottom);
                text.text = string.Join("\n", lines);
                var phase = Stopwatch.StartNew();
                text.ForceMeshUpdate(true, true);
                Material ink = text.fontSharedMaterial;
                Shader inkShader = PilotShaderBundle.GetKneeboardInk();
                if (inkShader != null && font.atlasTexture != null)
                {
                    if (cardInk == null) cardInk = new Material(inkShader) { name = "Boscali.KneeboardInk", hideFlags = HideFlags.HideAndDontSave };
                    cardInk.SetTexture("_MainTex", font.atlasTexture);
                    cardInk.SetColor("_Color", Ink);
                    ink = cardInk;
                }
                LastLayoutMs = (float)phase.Elapsed.TotalMilliseconds;
                phase.Restart();
                RenderTexture.active = card;
                GL.PushMatrix();
                pushed = true;
                GL.LoadPixelMatrix(0f, Size, 0f, Size);
                GL.Clear(true, true, Paper);
                bool drawn = ink != null && text.mesh != null && ink.SetPass(0);
                Matrix4x4 place = Matrix4x4.Translate(new Vector3((left + right) * .5f, (bottom + top) * .5f, 0f));
                if (drawn) Graphics.DrawMeshNow(text.mesh, place);
                LastDrawMs = (float)phase.Elapsed.TotalMilliseconds;
                return drawn;
            }
            catch (Exception error)
            {
                if (!warned) { warned = true; Plugin.Logger?.LogWarning("[Immersion] Kneeboard card render failed: " + error.Message); }
                return false;
            }
            finally
            {
                if (pushed) GL.PopMatrix();
                RenderTexture.active = previous;
                if (holder != null) Object.DestroyImmediate(holder);
            }
        }

        private Texture2D PlainCard()
        {
            if (plain != null) return plain;
            plain = new Texture2D(8, 8, TextureFormat.RGBA32, false) { name = "Boscali.KneeboardPlain", wrapMode = TextureWrapMode.Clamp };
            var pixels = new Color32[64];
            for (int i = 0; i < 64; i++) pixels[i] = i >= 56 ? new Color(.22f, .23f, .22f) : Paper;
            plain.SetPixels32(pixels);
            plain.Apply(false, true);
            return plain;
        }

        private List<KeyValuePair<string, int>> Loadout()
        {
            loadout.Clear();
            List<WeaponStation> stations = aircraft.weaponStations;
            if (stations == null) return loadout;
            for (int i = 0; i < stations.Count; i++)
            {
                WeaponStation station = stations[i];
                if (station == null || station.Cargo || station.WeaponInfo == null) continue;
                loadout.Add(new KeyValuePair<string, int>(station.WeaponInfo.weaponName, station.Ammo));
            }
            return loadout;
        }

        private string HomeBase()
        {
            Airbase best = null;
            float bestDistance = float.MaxValue;
            Vector3 at = aircraft.transform.position;
            foreach (Airbase airbase in FactionRegistry.airbaseLookup.Values)
            {
                if (airbase == null || airbase.disabled || airbase.CurrentHQ != aircraft.NetworkHQ) continue;
                float distance = (airbase.transform.position - at).sqrMagnitude;
                if (distance < bestDistance) { bestDistance = distance; best = airbase; }
            }
            if (best == null) return null;
            return best.SavedAirbase != null && !string.IsNullOrEmpty(best.SavedAirbase.DisplayName) ? best.SavedAirbase.DisplayName : best.name;
        }

        private static float TimeOfDay()
        {
            LevelInfo level = NetworkSceneSingleton<LevelInfo>.i;
            return level != null ? level.timeOfDay : float.NaN;
        }
    }
}
