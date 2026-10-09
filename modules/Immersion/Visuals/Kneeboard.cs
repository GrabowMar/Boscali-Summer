using System;
using System.Collections.Generic;
using System.Diagnostics;
using BoscaliSummer.Modules.Immersion.Domain;
using NOAvionics;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace BoscaliSummer.Modules.Immersion.Visuals
{
    /// <summary>
    /// Thigh kneeboard: one quad on the thigh away from the stick. Its mission card is rendered once per sortie
    /// (and again on rearm / loadout change) into a 512 px texture by a temporary camera that is destroyed at once.
    /// Steady state is one draw call and a bool check. If that one-off render ever exceeds 2 ms, later cards
    /// fall back to a plain paper texture with no camera.
    /// </summary>
    internal sealed class Kneeboard
    {
        private const int Size = 512, CardLayer = 31;
        private const float Width = .16f, Height = .22f;
        private static readonly Color Paper = new Color(.86f, .84f, .76f), Ink = new Color(.1f, .11f, .1f), Stamp = new Color(.62f, .1f, .08f, .55f);
        private static readonly int BaseMapId = Shader.PropertyToID("_BaseMap"), LightId = Shader.PropertyToID("_LightLevel");
        private readonly List<KeyValuePair<string, int>> loadout = new List<KeyValuePair<string, int>>(12);
        private readonly Action markDirty;
        private GameObject quad;
        private Mesh mesh;
        private Material material;
        private RenderTexture card;
        private Texture2D plain;
        private Aircraft aircraft;
        private bool dirty, cameraOverBudget, warned;

        internal Kneeboard() { markDirty = () => dirty = true; }

        internal bool Bound => quad != null;
        internal Renderer Renderer { get; private set; }
        internal float LastRenderMs { get; private set; } = -1f;
        internal string Side { get; private set; } = "none";

        /// <summary>Places the card on the thigh in its current (seated) pose; parenting keeps it there as the rig moves.</summary>
        internal void Bind(Transform thigh, Transform knee, Vector3 up, bool rightThigh, Aircraft owner, Shader shader)
        {
            Release();
            if (thigh == null || knee == null || owner == null || shader == null || !shader.isSupported) return;
            Vector3 along = knee.position - thigh.position;
            if (along.sqrMagnitude < .01f) return;
            Vector3 forward = along.normalized;
            Vector3 top = Vector3.ProjectOnPlane(up, forward).normalized;
            if (top.sqrMagnitude < .5f) return;
            aircraft = owner;
            Side = rightThigh ? "right" : "left";
            quad = new GameObject("Boscali.Kneeboard") { layer = 3 }; // PhysicsLayers.Cockpit, like the body
            quad.transform.SetPositionAndRotation(thigh.position + along * .62f + top * .095f, Quaternion.LookRotation(-top, forward));
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
            material = new Material(shader) { name = "Boscali.Kneeboard", hideFlags = HideFlags.HideAndDontSave };
            material.SetFloat("_BodyVisible", 1f);
            material.SetFloat("_HeadMaskMode", 0f);
            material.SetFloat("_Smoothness", .15f);
            renderer.sharedMaterial = material;
            owner.OnRearmUnit += markDirty;
            owner.onLoadoutChanged += markDirty;
            dirty = true;
        }

        /// <summary>Cheap per-frame check; the card is re-rendered only after bind, rearm or loadout change.</summary>
        internal void Tick()
        {
            if (!dirty || quad == null) return;
            dirty = false;
            Render();
        }

        internal void SetVisible(bool visible) { if (quad != null && quad.activeSelf != visible) quad.SetActive(visible); }
        internal void SetLight(float level) { if (material != null) material.SetFloat(LightId, level); }

        internal void Release()
        {
            if (aircraft != null) { aircraft.OnRearmUnit -= markDirty; aircraft.onLoadoutChanged -= markDirty; }
            if (quad != null) Object.Destroy(quad);
            if (mesh != null) Object.Destroy(mesh);
            if (material != null) Object.Destroy(material);
            if (card != null) { card.Release(); Object.Destroy(card); }
            if (plain != null) Object.Destroy(plain);
            quad = null; mesh = null; material = null; card = null; plain = null; aircraft = null; Renderer = null;
            dirty = false; Side = "none";
        }

        private void Render()
        {
            var watch = Stopwatch.StartNew();
            string[] lines = KneeboardCard.Lines(aircraft.unitName, aircraft.definition != null ? aircraft.definition.unitName : null,
                Loadout(), HomeBase(), MissionManager.CurrentMission != null ? MissionManager.CurrentMission.Name : "Free flight", TimeOfDay());
            if (!cameraOverBudget && RenderCard(lines)) material.SetTexture(BaseMapId, card);
            else material.SetTexture(BaseMapId, PlainCard());
            LastRenderMs = (float)watch.Elapsed.TotalMilliseconds;
            if (LastRenderMs > 2f && !cameraOverBudget)
            {
                cameraOverBudget = true;
                if (!warned) { warned = true; Plugin.Logger?.LogWarning($"[Immersion] Kneeboard card took {LastRenderMs:0.0} ms; later cards use the plain texture."); }
            }
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
            var root = new GameObject("Boscali.KneeboardCard") { layer = CardLayer };
            root.transform.position = new Vector3(0f, -50000f, 0f);
            var canvas = root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            var rect = (RectTransform)root.transform;
            rect.sizeDelta = new Vector2(Size, Size);
            Image(root.transform, "Paper", Paper, Vector2.zero, Vector2.one);
            Image(root.transform, "Clip", new Color(.22f, .23f, .22f), new Vector2(.3f, .94f), new Vector2(.7f, 1f));
            Text(root.transform, "<mspace=0.58em>" + string.Join("\n", lines) + "</mspace>", font, 25f, Ink, new Vector2(.06f, .04f), new Vector2(.97f, .92f), 0f);
            Text(root.transform, "TRAINING", font, 44f, Stamp, new Vector2(.45f, .04f), new Vector2(.98f, .2f), 12f);
            var holder = new GameObject("Boscali.KneeboardCamera");
            var camera = holder.AddComponent<Camera>();
            camera.enabled = false;
            camera.orthographic = true;
            camera.orthographicSize = Size / 2f;
            camera.transform.SetPositionAndRotation(root.transform.position - Vector3.forward * 10f, Quaternion.identity);
            camera.nearClipPlane = .1f; camera.farClipPlane = 50f;
            camera.cullingMask = 1 << CardLayer;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Paper;
            camera.allowHDR = false; camera.allowMSAA = false;
            camera.targetTexture = card;
            bool ok = true;
            try
            {
                Canvas.ForceUpdateCanvases();
                camera.Render();
            }
            catch (Exception) { ok = false; }
            camera.targetTexture = null;
            // Immediate: nothing on the card layer may outlive this call into another camera's frame.
            Object.DestroyImmediate(holder);
            Object.DestroyImmediate(root);
            return ok;
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

        private static void Image(Transform parent, string name, Color colour, Vector2 min, Vector2 max)
        {
            var node = new GameObject(name, typeof(RectTransform)) { layer = CardLayer };
            node.transform.SetParent(parent, false);
            var rect = (RectTransform)node.transform;
            rect.anchorMin = min; rect.anchorMax = max; rect.offsetMin = rect.offsetMax = Vector2.zero;
            node.AddComponent<UnityEngine.UI.Image>().color = colour;
        }

        private static void Text(Transform parent, string text, TMP_FontAsset font, float size, Color colour, Vector2 min, Vector2 max, float angle)
        {
            var node = new GameObject("Text", typeof(RectTransform)) { layer = CardLayer };
            node.transform.SetParent(parent, false);
            var rect = (RectTransform)node.transform;
            rect.anchorMin = min; rect.anchorMax = max; rect.offsetMin = rect.offsetMax = Vector2.zero;
            rect.localRotation = Quaternion.Euler(0f, 0f, angle);
            var label = node.AddComponent<TextMeshProUGUI>();
            label.font = font;
            label.fontSize = size;
            label.color = colour;
            label.enableWordWrapping = false;
            label.richText = true;
            label.alignment = angle != 0f ? TextAlignmentOptions.Center : TextAlignmentOptions.TopLeft;
            label.text = text;
        }
    }
}
