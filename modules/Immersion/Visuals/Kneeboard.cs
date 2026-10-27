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
        // Lit by the cockpit sun: mid-grey paper and near-black bold ink keep contrast without blowing out.
        private static readonly Color Paper = new Color(.4f, .39f, .34f), Ink = new Color(.02f, .022f, .02f);
        // Worn paper tint (also tames the brighter gallery pictures) and a dark olive board close to the flight suit.
        private static readonly Color PaperTint = new Color(.8f, .78f, .7f), Board = new Color(.075f, .08f, .07f);
        private static readonly int BaseMapId = Shader.PropertyToID("_BaseMap"), LightId = Shader.PropertyToID("_LightLevel");
        private readonly List<KeyValuePair<string, int>> loadout = new List<KeyValuePair<string, int>>(12);
        private readonly Action markDirty;
        private readonly KneeboardSchedule schedule = new KneeboardSchedule();
        private GameObject quad;
        private Mesh mesh;
        private Material material, hardware;
        private RenderTexture card;
        private Texture2D plain;
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
            quad.transform.SetPositionAndRotation(thigh.position + along * .68f + top * .104f, Quaternion.LookRotation(-top, forward));
            quad.transform.SetParent(thigh, true);
            mesh = BoardMesh();
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
                hardware = BodyMaterial(shader, "Boscali.KneeboardBoard", Board, .22f);
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

        /// <summary>A strapped clipboard instead of a bare sheet: dark board with thickness, the card inset on it, a clip at the
        /// knee end and elastic tabs that run down the board's sides into the thigh. Submesh 0 is the card, 1 the hardware.</summary>
        private static Mesh BoardMesh()
        {
            var vertices = new List<Vector3>(96);
            var normals = new List<Vector3>(96);
            var uvs = new List<Vector2>(96);
            var card = new List<int>(6);
            var board = new List<int>(140);
            void Face(List<int> into, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 n, bool mapped)
            {
                int i = vertices.Count;
                vertices.Add(a); vertices.Add(b); vertices.Add(c); vertices.Add(d);
                normals.Add(n); normals.Add(n); normals.Add(n); normals.Add(n);
                uvs.Add(mapped ? new Vector2(0, 0) : Vector2.zero); uvs.Add(mapped ? new Vector2(1, 0) : Vector2.zero);
                uvs.Add(mapped ? new Vector2(1, 1) : Vector2.zero); uvs.Add(mapped ? new Vector2(0, 1) : Vector2.zero);
                // Unity's front face is the side its triangle normal points to; wind each face to its outward normal.
                bool flip = Vector3.Dot(Vector3.Cross(b - a, c - a), n) < 0f;
                if (flip) into.AddRange(new[] { i, i + 2, i + 1, i, i + 3, i + 2 });
                else into.AddRange(new[] { i, i + 1, i + 2, i, i + 2, i + 3 });
            }
            void Box(Vector3 min, Vector3 max)
            {
                Vector3 p(float x, float y, float z) => new Vector3(x == 0 ? min.x : max.x, y == 0 ? min.y : max.y, z == 0 ? min.z : max.z);
                Face(board, p(0, 0, 0), p(1, 0, 0), p(1, 1, 0), p(0, 1, 0), Vector3.back, false);   // top (out of the leg)
                Face(board, p(0, 0, 1), p(0, 1, 1), p(1, 1, 1), p(1, 0, 1), Vector3.forward, false); // underside
                Face(board, p(0, 0, 0), p(0, 1, 0), p(0, 1, 1), p(0, 0, 1), Vector3.left, false);
                Face(board, p(1, 0, 0), p(1, 0, 1), p(1, 1, 1), p(1, 1, 0), Vector3.right, false);
                Face(board, p(0, 0, 0), p(0, 0, 1), p(1, 0, 1), p(1, 0, 0), Vector3.down, false);
                Face(board, p(0, 1, 0), p(1, 1, 0), p(1, 1, 1), p(0, 1, 1), Vector3.up, false);
            }
            const float boardX = .071f, boardLow = -.092f, boardHigh = .098f, thick = .005f;
            Box(new Vector3(-boardX, boardLow, -thick), new Vector3(boardX, boardHigh, 0f));
            // Card sits 0.5 mm proud of the board so it never fights it for depth.
            const float cardX = .065f, cardLow = -.085f, cardHigh = .079f, cardZ = -thick - .0005f;
            Face(card, new Vector3(-cardX, cardLow, cardZ), new Vector3(cardX, cardLow, cardZ), new Vector3(cardX, cardHigh, cardZ),
                new Vector3(-cardX, cardHigh, cardZ), Vector3.back, true);
            Box(new Vector3(-.032f, .071f, -.012f), new Vector3(.032f, .094f, -thick)); // clip over the card's top edge
            // Elastic tabs: from the board edge, sloping in and down, ending inside the thigh so the board reads as strapped on.
            for (int side = -1; side <= 1; side += 2)
            {
                float edge = side * (boardX + .003f), sunk = side * .062f;
                const float low = -.022f, high = .008f, topZ = -thick + .001f, endZ = .045f;
                Vector3 outward = new Vector3(side * (endZ - topZ), 0f, side * (edge - sunk)).normalized;
                Face(board, new Vector3(edge, low, topZ), new Vector3(edge, high, topZ), new Vector3(sunk, high, endZ), new Vector3(sunk, low, endZ), outward, false);
                Face(board, new Vector3(side * boardX, low, topZ), new Vector3(edge, low, topZ), new Vector3(edge, high, topZ),
                    new Vector3(side * boardX, high, topZ), Vector3.back, false);
            }
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
            hardware = null;
            if (card != null) { card.Release(); Object.Destroy(card); }
            if (plain != null) Object.Destroy(plain);
            material = null; card = null; plain = null; aircraft = null; cardValid = false;
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
            TMP_FontAsset font = AvType.VanillaFallback;
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
                text.fontSize = 28f;
                text.fontStyle = FontStyles.Bold;
                text.color = Ink;
                text.enableWordWrapping = false;
                text.richText = true;
                text.alignment = TextAlignmentOptions.TopLeft;
                const float left = Size * .06f, right = Size * .97f, bottom = Size * .04f, top = Size * .92f;
                text.rectTransform.sizeDelta = new Vector2(right - left, top - bottom);
                text.text = "<mspace=0.54em>" + string.Join("\n", lines) + "</mspace>";
                var phase = Stopwatch.StartNew();
                text.ForceMeshUpdate(true, true);
                Material ink = text.fontSharedMaterial;
                LastLayoutMs = (float)phase.Elapsed.TotalMilliseconds;
                phase.Restart();
                RenderTexture.active = card;
                GL.PushMatrix();
                pushed = true;
                GL.LoadPixelMatrix(0f, Size, 0f, Size);
                GL.Clear(true, true, Paper);
                bool drawn = ink != null && text.mesh != null && ink.SetPass(0);
                // Signed-distance glyphs come out soft in an immediate draw; four passes build solid ink (0.03 ms each).
                Matrix4x4 place = Matrix4x4.Translate(new Vector3((left + right) * .5f, (bottom + top) * .5f, 0f));
                for (int pass = 0; drawn && pass < 4; pass++) Graphics.DrawMeshNow(text.mesh, place);
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
