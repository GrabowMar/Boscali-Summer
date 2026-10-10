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
    /// Thigh kneeboard: one quad on the thigh away from the stick. Its mission card is drawn once per aircraft
    /// (and again on rearm / loadout change) straight into a 512 px texture: one text mesh, one immediate draw,
    /// no camera and no canvas (a temporary URP camera cost 23 ms in game). Steady state is one draw call and a
    /// bool check. If a warm redraw ever exceeds 2 ms, later cards keep the last one.
    /// </summary>
    internal sealed class Kneeboard
    {
        private const int Size = 512;
        private const float Width = .14f, Height = .18f;
        // Lit by the cockpit sun: mid-grey paper and near-black bold ink keep contrast without blowing out.
        private static readonly Color Paper = new Color(.4f, .39f, .34f), Ink = new Color(.02f, .022f, .02f);
        private static readonly int BaseMapId = Shader.PropertyToID("_BaseMap"), LightId = Shader.PropertyToID("_LightLevel");
        private readonly List<KeyValuePair<string, int>> loadout = new List<KeyValuePair<string, int>>(12);
        private readonly Action markDirty;
        private readonly KneeboardSchedule schedule = new KneeboardSchedule();
        private GameObject quad;
        private Mesh mesh;
        private Material material;
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
            quad.transform.SetPositionAndRotation(thigh.position + along * .68f + top * .112f, Quaternion.LookRotation(-top, forward));
            quad.transform.SetParent(thigh, true);
            mesh = new Mesh { name = "Boscali.Kneeboard" };
            mesh.vertices = new[] { new Vector3(-Width / 2, -Height / 2), new Vector3(Width / 2, -Height / 2), new Vector3(-Width / 2, Height / 2), new Vector3(Width / 2, Height / 2) };
            mesh.uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 1), new Vector2(1, 1) };
            mesh.triangles = new[] { 0, 3, 1, 3, 0, 2 };
            mesh.RecalculateNormals(); mesh.RecalculateTangents(); mesh.RecalculateBounds();
            quad.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = quad.AddComponent<MeshRenderer>();
            Renderer = renderer;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            renderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            if (material == null)
            {
                material = new Material(shader) { name = "Boscali.Kneeboard", hideFlags = HideFlags.HideAndDontSave };
                material.SetFloat("_BodyVisible", 1f);
                material.SetFloat("_HeadMaskMode", 0f);
                material.SetFloat("_Smoothness", .05f);
            }
            renderer.sharedMaterial = material;
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
        internal void SetLight(float level) { if (material != null) material.SetFloat(LightId, level); }

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
