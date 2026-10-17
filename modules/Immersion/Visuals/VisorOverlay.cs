using System.Threading;
using BoscaliSummer.Modules.Immersion.Domain;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Modules.Immersion.Visuals
{
    /// <summary>
    /// Near-future helmet visor on one Screen Space Overlay quad: electrochromic tint, breath fog, a
    /// faint coating that shifts hue toward the rim, the brow shade above 6 G, and hairline scratches
    /// that catch the sun. Sorted under the vanilla HUD so symbology is never tinted. There is no
    /// helmet frame: an outline imitating the shell reads as a cheap overlay, so the glass carries
    /// everything. One generated soft mask (G fog, B brow); scratches are drawn by the shader.
    /// The quad is disabled - no draw at all - while there is nothing to show.
    /// </summary>
    internal sealed class VisorOverlay
    {
        private const int Width = 512, Height = 288;
        private static readonly AccessTools.FieldRef<FlightHud, Canvas> HudCanvas = HudRef();
        private static readonly int TintId = Shader.PropertyToID("_Tint"), SunId = Shader.PropertyToID("_SunLit"),
            FogId = Shader.PropertyToID("_Fog"), BrowId = Shader.PropertyToID("_Brow"), LightId = Shader.PropertyToID("_Light"),
            CurveId = Shader.PropertyToID("_Curve"), ReflectId = Shader.PropertyToID("_Reflect"),
            SunViewId = Shader.PropertyToID("_SunView");
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
        private float tint, brow, sunLit, curve, reflect, light = 1f;
        private Vector4 sunView;
        private readonly float[] written = new float[8];
        private bool writtenValid;

        internal bool Visible => image != null && image.enabled;
        internal float Tint => tint;
        internal float SunLit => sunLit;
        internal float Fog => fog.Value;
        internal float Brow => brow;
        internal float Curve => curve;
        internal int SortingOrder => canvas != null ? canvas.sortingOrder : 0;
        internal string HudMode => hud != null ? hud.renderMode.ToString() : "none";

        /// <summary>
        /// One frame of the visor. <paramref name="sunViewport"/> is the sun's position on the view
        /// (viewport x, y in 0..1; z > 0 in front of the camera); curvature and reflection are the
        /// pilot's 0..1 settings.
        /// </summary>
        internal void Tick(PilotCue cues, PilotPhysiology p, float sunLit01, float light01, float temperatureC,
            bool temperatureKnown, bool enabled, float dt, Vector3 sunViewport, float sunFacing01,
            float curvature01, float reflection01)
        {
            if (!enabled || p == null) { Hide(); return; }
            LevelInfo level = NetworkSceneSingleton<LevelInfo>.i;
            Light sun = level != null ? level.sun : null;
            float elevation = sun != null ? Mathf.Asin(Mathf.Clamp(-sun.transform.forward.y, -1f, 1f)) * Mathf.Rad2Deg : -90f;
            curve = HelmetMath.Sanitize(curvature01, HelmetMath.DefaultCurvature);
            reflect = HelmetMath.Sanitize(reflection01, HelmetMath.DefaultReflection);
            float targetTint = HelmetMath.ElectroTint(elevation, sunFacing01, light01);
            tint = VisorMath.Ease(tint, targetTint, dt, 2f);
            light = Mathf.Clamp01(light01); sunLit = Mathf.Clamp01(sunLit01);
            sunView = new Vector4(sunViewport.x, sunViewport.y, sunViewport.z, 0f);
            if ((cues & PilotCue.Exhale) != 0 && VisorMath.FogsOnExhale(p.Exertion01, temperatureC, temperatureKnown))
                fog.Trigger(.6f + .4f * p.Exertion01);
            fog.Step(dt);
            brow = VisorMath.Ease(brow, VisorMath.Brow(p.LoadG), dt, .4f);
            bool active = Mathf.Max(Mathf.Max(tint, sunLit), Mathf.Max(fog.Value, brow)) > VisorMath.Idle;
            if (!active || !Ensure()) { SetEnabled(false); return; }
            bool hudVisible = IgnoreHud || hud == null || hud.gameObject.activeInHierarchy;
            SetEnabled(hudVisible);
            if (!hudVisible) return;
            WriteAll();
        }

        internal void Hide()
        {
            SetEnabled(false);
            fog = default;
            brow = sunLit = 0f; // the sun visor stays where it was; it eases with the sun, not with view changes
        }

        internal void Release()
        {
            Hide();
            if (root != null) Object.Destroy(root);
            if (material != null) Object.Destroy(material);
            if (texture != null) Object.Destroy(texture);
            root = null; canvas = null; image = null; hud = null;
            material = null; texture = null;
            writtenValid = false;
        }

        private void SetEnabled(bool on)
        {
            if (image != null && image.enabled != on) image.enabled = on;
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
                material = new Material(shader) { name = "Boscali.VisorGlass", hideFlags = HideFlags.HideAndDontSave };
            }
            ResolveOrder(out int order, out int layerId);
            root = new GameObject("Boscali.VisorGlass");
            canvas = root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = order;
            canvas.sortingLayerID = layerId;
            var node = new GameObject("Visor", typeof(RectTransform));
            node.transform.SetParent(root.transform, false);
            var rect = (RectTransform)node.transform;
            rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = rect.offsetMax = Vector2.zero;
            image = node.AddComponent<RawImage>();
            image.raycastTarget = false;
            image.texture = texture;
            image.material = material;
            image.enabled = false;
            writtenValid = false;
            return true;
        }

        // One below the lower of HUD and blackout: the glass never tints symbology and the blackout always wins.
        private void ResolveOrder(out int order, out int layerId)
        {
            FlightHud flight = SceneSingleton<FlightHud>.i;
            hud = flight != null && HudCanvas != null ? HudCanvas(flight) : null;
            int hudOrder = hud != null ? hud.rootCanvas.sortingOrder : 0;
            layerId = hud != null ? hud.rootCanvas.sortingLayerID : 0;
            CameraStateManager cameras = SceneSingleton<CameraStateManager>.i;
            Image blackout = cameras != null ? cameras.GetBlackoutImage() : null;
            int blackoutOrder = blackout != null && blackout.canvas != null ? blackout.canvas.rootCanvas.sortingOrder : int.MaxValue;
            order = Mathf.Min(hudOrder, blackoutOrder) - 1;
        }

        private void WriteAll()
        {
            Write(0, TintId, tint); Write(1, SunId, sunLit);
            Write(2, FogId, fog.Value); Write(3, BrowId, brow);
            Write(4, LightId, light); Write(5, CurveId, curve);
            Write(6, ReflectId, reflect);
            // The sun moves across the view every frame; it bypasses the dead-band cache.
            material.SetVector(SunViewId, sunView);
            writtenValid = true;
        }

        private void Write(int slot, int id, float value)
        {
            if (writtenValid && Mathf.Abs(value - written[slot]) <= .002f) return;
            written[slot] = value;
            material.SetFloat(id, value);
        }

        private static AccessTools.FieldRef<FlightHud, Canvas> HudRef()
        {
            try { return AccessTools.FieldRefAccess<FlightHud, Canvas>("canvas"); }
            catch (System.Exception) { return null; }
        }
    }
}
