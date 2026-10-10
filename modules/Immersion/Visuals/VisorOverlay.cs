using System.Threading;
using BoscaliSummer.Modules.Immersion.Domain;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Modules.Immersion.Visuals
{
    /// <summary>
    /// Glass-only helmet visor: one Screen Space Overlay quad sorted under the vanilla HUD and blackout
    /// image, one mask generated off the main thread, five uniforms. The quad is disabled - no draw at
    /// all - whenever every layer is idle (night, sun out of view, calm breathing, below 6 G).
    /// </summary>
    internal sealed class VisorOverlay
    {
        private const int Width = 512, Height = 288;
        private static readonly AccessTools.FieldRef<FlightHud, Canvas> HudCanvas = HudRef();
        private static readonly int TintId = Shader.PropertyToID("_Tint"), SunId = Shader.PropertyToID("_SunLit"),
            FogId = Shader.PropertyToID("_Fog"), BrowId = Shader.PropertyToID("_Brow"), LightId = Shader.PropertyToID("_Light");
        private GameObject root;
        private Canvas canvas;
        private RawImage image;
        private Canvas hud;
        private Material material;
        private Texture2D texture;
        private volatile byte[] mask;
        private int baking;
        private bool shaderWarned;
        /// <summary>Automation only: scripted sim flight hides the vanilla HUD the visor normally follows.</summary>
        internal bool IgnoreHud;
        private FogPulse fog;
        private float tint, brow, sunLit, wTint = -1f, wSun = -1f, wFog = -1f, wBrow = -1f, wLight = -1f;

        internal bool Visible => image != null && image.enabled;
        internal float Tint => tint;
        internal float SunLit => sunLit;
        internal float Fog => fog.Value;
        internal float Brow => brow;
        internal int SortingOrder => canvas != null ? canvas.sortingOrder : 0;
        internal string HudMode => hud != null ? hud.renderMode.ToString() : "none";

        internal void Tick(PilotCue cues, PilotPhysiology p, float sunLit01, float light01, float temperatureC,
            bool temperatureKnown, bool enabled, float dt)
        {
            if (!enabled || p == null) { Hide(); return; }
            LevelInfo level = NetworkSceneSingleton<LevelInfo>.i;
            Light sun = level != null ? level.sun : null;
            float elevation = sun != null ? Mathf.Asin(Mathf.Clamp(-sun.transform.forward.y, -1f, 1f)) * Mathf.Rad2Deg : -90f;
            tint = VisorMath.Ease(tint, VisorMath.SunVisor(elevation), dt, 2f);
            sunLit = Mathf.Clamp01(sunLit01);
            if ((cues & PilotCue.Exhale) != 0 && VisorMath.FogsOnExhale(p.Exertion01, temperatureC, temperatureKnown))
                fog.Trigger(.6f + .4f * p.Exertion01);
            fog.Step(dt);
            brow = VisorMath.Ease(brow, VisorMath.Brow(p.LoadG), dt, .4f);
            bool active = Mathf.Max(Mathf.Max(tint, sunLit), Mathf.Max(fog.Value, brow)) > VisorMath.Idle;
            if (!active || !Ensure()) { if (image != null && image.enabled) image.enabled = false; return; }
            bool hudVisible = IgnoreHud || hud == null || hud.gameObject.activeInHierarchy;
            if (image.enabled != hudVisible) image.enabled = hudVisible;
            if (!hudVisible) return;
            Write(TintId, tint, ref wTint);
            Write(SunId, sunLit, ref wSun);
            Write(FogId, fog.Value, ref wFog);
            Write(BrowId, brow, ref wBrow);
            Write(LightId, Mathf.Clamp01(light01), ref wLight);
        }

        internal void Hide()
        {
            if (image != null && image.enabled) image.enabled = false;
            fog = default;
            brow = sunLit = 0f; // the sun visor stays where it was; it eases with the sun, not with view changes
        }

        internal void Release()
        {
            Hide();
            if (root != null) Object.Destroy(root);
            if (material != null) Object.Destroy(material);
            if (texture != null) Object.Destroy(texture);
            root = null; canvas = null; image = null; hud = null; material = null; texture = null;
            wTint = wSun = wFog = wBrow = wLight = -1f;
        }

        private bool Ensure()
        {
            if (root != null && image != null) return true;
            if (texture == null)
            {
                byte[] data = mask;
                if (data == null)
                {
                    if (Interlocked.Exchange(ref baking, 1) == 0)
                        ThreadPool.QueueUserWorkItem(_ =>
                        {
                            try { mask = VisorMask.Build(Width, Height); }
                            catch (System.Exception) { mask = null; }
                        });
                    return false;
                }
                texture = new Texture2D(Width, Height, TextureFormat.RGBA32, false, true)
                    { name = "Boscali.VisorMask", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, hideFlags = HideFlags.HideAndDontSave };
                texture.LoadRawTextureData(data);
                texture.Apply(false, true);
                mask = null;
            }
            if (material == null)
            {
                Shader shader = PilotShaderBundle.GetVisor();
                if (shader == null)
                {
                    if (!shaderWarned) { shaderWarned = true; Plugin.Logger?.LogWarning("[Immersion] Helmet visor shader missing from pilot.bundle; visor disabled."); }
                    return false;
                }
                material = new Material(shader) { name = "Boscali.Visor", hideFlags = HideFlags.HideAndDontSave };
            }
            root = new GameObject("Boscali.VisorOverlay");
            canvas = root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var node = new GameObject("Visor", typeof(RectTransform));
            node.transform.SetParent(root.transform, false);
            var rect = (RectTransform)node.transform;
            rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = rect.offsetMax = Vector2.zero;
            image = node.AddComponent<RawImage>();
            image.raycastTarget = false;
            image.texture = texture;
            image.material = material;
            image.enabled = false;
            canvas.sortingOrder = UnderHud();
            if (hud != null) canvas.sortingLayerID = hud.rootCanvas.sortingLayerID;
            wTint = wSun = wFog = wBrow = wLight = -1f;
            return true;
        }

        // One below the lower of the vanilla HUD and blackout canvases: HUD text is never tinted, blackout covers the visor.
        private int UnderHud()
        {
            FlightHud flight = SceneSingleton<FlightHud>.i;
            hud = flight != null && HudCanvas != null ? HudCanvas(flight) : null;
            int order = int.MaxValue;
            if (hud != null) order = hud.rootCanvas.sortingOrder;
            CameraStateManager cameras = SceneSingleton<CameraStateManager>.i;
            Image blackout = cameras != null ? cameras.GetBlackoutImage() : null;
            if (blackout != null && blackout.canvas != null) order = Mathf.Min(order, blackout.canvas.rootCanvas.sortingOrder);
            return order == int.MaxValue ? 0 : order - 1;
        }

        private void Write(int id, float value, ref float written)
        {
            if (Mathf.Abs(value - written) <= .002f) return;
            written = value;
            material.SetFloat(id, value);
        }

        private static AccessTools.FieldRef<FlightHud, Canvas> HudRef()
        {
            try { return AccessTools.FieldRefAccess<FlightHud, Canvas>("canvas"); }
            catch (System.Exception) { return null; }
        }
    }
}
