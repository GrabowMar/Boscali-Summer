using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Core.Fx;
using BoscaliSummer.Modules.Support.Domain.Space;
using UnityEngine.Rendering.Universal;

namespace BoscaliSummer.Modules.Support.Visuals
{
    /// <summary>
    /// A host-revealed contact projected into the optical image: where its bracket belongs (0..1 across the image, origin
    /// bottom-left like a RawImage) and its words. Only contacts the faction mirror reveals ever get one.
    /// </summary>
    internal readonly struct OpticalBracket
    {
        public readonly int Id;
        public readonly Vector2 Uv;
        public readonly ProbableClass Class;
        public readonly bool Moving;
        public readonly string Label;

        public OpticalBracket(int id, Vector2 uv, ProbableClass probable, bool moving, string label)
        {
            Id = id; Uv = uv; Class = probable; Moving = moving; Label = label;
        }
    }

    /// <summary>
    /// The orbital platform's live optical sensor. A dedicated camera renders
    /// the scene into a target texture, positioned on the station's true line-of-sight vector
    /// but with an adaptive standoff distance dynamically scaled to the framed footprint. This
    /// maintains a stable, natural focal length (10° to 50° FOV) across all zoom levels down to
    /// deep 60 m close-ups, eliminating floating-point depth buffer collapse, shadow clipping
    /// and terrain occlusion.
    ///
    /// The camera shows the world as a camera sees it: terrain, scenery and units alike, nothing is
    /// hidden in the pixels. Fog of war is enforced by what can be MARKed, never by what is drawn:
    /// only host-revealed contacts (the faction mirror, <see cref="SetApprovedContacts"/>) get a
    /// bracket and a label, and nothing is bracketed just because it is visible. Cloud and rain soften
    /// the image; at night, with no thermal path in the game, the camera refuses and draws nothing.
    /// Radar products are formed separately by SarCollector when the host accepts a scan; the live
    /// view does no GPU readback.
    /// </summary>
    internal sealed class SatelliteImager : MonoBehaviour
    {
        public const float MaxStandoff = 24000f;
        public const float MinStandoff = 250f;
        // Haze at full softness: image transmittance across the standoff, e^-HazeOpticalDepth.
        private const float HazeOpticalDepth = 1.2f;
        private static readonly Color HazeColour = new Color(0.62f, 0.66f, 0.70f, 1f);

        private int width = 640;
        private int height = 400;
        private Camera cam;
        private RenderTexture colour;
        private float nextFrame;
        private int enabledFrame = -1;
        private float standoff = MinStandoff;
        private float softness;
        private OpticalVerdict verdict = OpticalVerdict.Ok;
        private bool posed, cleared;

        // Fog changes made while the feed camera renders, and the proof they were put back.
        private bool fogSaved;
        private int fogSavedFrame;
        private bool fogWas = true;
        private FogMode fogModeWas;
        private float fogDensityWas;
        private Color fogColourWas;

        private readonly FeedContact[] rows = new FeedContact[SpaceWire.MaxContacts];
        private readonly GlobalPosition[] rowPoints = new GlobalPosition[SpaceWire.MaxContacts];
        private readonly string[] rowLabels = new string[SpaceWire.MaxContacts];
        private int rowCount;
        private readonly List<OpticalBracket> brackets = new List<OpticalBracket>(SpaceWire.MaxContacts);

        /// <summary>The picture, or null while the camera refuses (night, no sky state): a refused feed shows words, never the last frame.</summary>
        public Texture Output => verdict == OpticalVerdict.Ok ? colour : null;
        public Camera Camera => cam;
        public int FramesRendered { get; private set; }

        /// <summary>False while the feed is not on screen: the camera does not render and spends nothing.</summary>
        public bool Visible { get; set; } = true;

        /// <summary>Why the camera is not rendering (night, no sky state), or Ok.</summary>
        public OpticalVerdict Verdict => verdict;

        /// <summary>The refusal in words, empty while the camera works.</summary>
        public string Words => SpaceFeedRules.OpticalRefusal(verdict);

        /// <summary>0 sharp .. 1 fully overcast haze.</summary>
        public float Softness => softness;

        /// <summary>Brackets for the revealed contacts that currently fall inside the image. Valid until the next Aim or contact update.</summary>
        public IReadOnlyList<OpticalBracket> Brackets => brackets;

        public static SatelliteImager Create(int pixelsWide, int pixelsHigh)
        {
            var go = new GameObject("BoscaliStationImager");
            DontDestroyOnLoad(go);
            go.SetActive(false);
            SatelliteImager imager = go.AddComponent<SatelliteImager>();
            float scale = FxBus.Scales.RenderTargets;
            int width = Mathf.RoundToInt(pixelsWide * scale);
            int height = Mathf.RoundToInt(pixelsHigh * scale);
            imager.width = Mathf.Clamp(width - width % 2, 64, 2048);
            imager.height = Mathf.Clamp(height - height % 2, 64, 2048);
            go.SetActive(true);
            return imager;
        }

        private void Awake()
        {
            colour = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32) { name = "BoscaliStationEO" };
            colour.Create();
            if (!FxRtPool.Own(colour))
            {
                colour.Release();
                Destroy(colour);
                colour = null;
            }
            cam = gameObject.AddComponent<Camera>();
            cam.enabled = false;
            cam.targetTexture = colour;
            cam.aspect = width / (float)height;
            cam.nearClipPlane = 20f;
            cam.farClipPlane = MaxStandoff * 3.5f;
            cam.clearFlags = CameraClearFlags.Skybox;
            cam.allowHDR = false;
            cam.allowMSAA = false;
            cam.depth = -10f;
            cam.cullingMask = WorldMask();

            UniversalAdditionalCameraData data = cam.GetUniversalAdditionalCameraData();
            if (data != null)
            {
                data.renderPostProcessing = false;
                data.antialiasing = AntialiasingMode.None;
                data.renderShadows = true;
            }

            RenderPipelineManager.beginCameraRendering += OnBeginCameraRendering;
            RenderPipelineManager.endCameraRendering += OnEndCameraRendering;
        }

        /// <summary>
        /// The world a camera sees: terrain, water, structures, ground units, ships and effects. The cockpit, HUD and UI layers stay
        /// out. Nothing here depends on what the faction has revealed.
        /// </summary>
        private static int WorldMask()
        {
            int excluded = (1 << PhysicsLayers.UI) | (1 << PhysicsLayers.HUD) | (1 << PhysicsLayers.Cockpit);
            int world = (int)PhysicsLayers.StaticsMask | (int)PhysicsLayers.DefaultMask | (int)PhysicsLayers.WaterMask |
                        (int)PhysicsLayers.ShipsMask | (int)PhysicsLayers.EffectsMask | (int)PhysicsLayers.TransparentFXMask;
            return world & ~excluded;
        }

        /// <summary>
        /// Tell the camera what the sky is at the aim. Night and an unknown sky stop it rendering (<see cref="Words"/> says why);
        /// cloud and rain soften the image. Call before <see cref="Aim"/> whenever the aim moves.
        /// </summary>
        public void SetSky(bool known, in WeatherViewSample sky)
        {
            verdict = SpaceFeedRules.Optical(known, sky);
            softness = verdict == OpticalVerdict.Ok ? SpaceFeedRules.Softness(sky) : 0f;
            if (verdict != OpticalVerdict.Ok)
            {
                posed = false;
                brackets.Clear();
                ClearPicture();
            }
        }

        /// <summary>Blank the render target so a refusal can never leave the last daytime frame behind.</summary>
        private void ClearPicture()
        {
            if (colour == null || cleared) return;
            cleared = true;
            if (cam != null) cam.enabled = false;
            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = colour;
            GL.Clear(true, true, Color.black);
            RenderTexture.active = previous;
        }

        /// <summary>
        /// The host-revealed contacts (the faction mirror rows) that may be bracketed and labelled over the image. Anything the
        /// camera happens to show that is not in this list gets no bracket and cannot be MARKed. Rows past their reveal are dropped.
        /// </summary>
        public void SetApprovedContacts(IReadOnlyList<FeedContact> hostMirroredContacts)
        {
            rowCount = 0;
            if (hostMirroredContacts != null)
            {
                float now = Runtime.SupportManager.MissionNow();
                bool filter = SpaceRules.MissionTime(now);
                for (int i = 0; i < hostMirroredContacts.Count && rowCount < rows.Length; i++)
                {
                    FeedContact row = hostMirroredContacts[i];
                    if (float.IsNaN(row.X) || float.IsInfinity(row.X) || float.IsNaN(row.Z) || float.IsInfinity(row.Z)) continue;
                    if (filter && row.Expires <= now) continue;
                    var point = new GlobalPosition(row.X, 0f, row.Z);
                    Vector3 local = point.ToLocalPosition();
                    if (Runtime.SupportTargeting.TryMapPoint(point, out Vector3 ground)) local = ground;
                    rows[rowCount] = row;
                    rowPoints[rowCount] = local.ToGlobalPosition(); // stored global: a floating-origin shift must not strand it
                    rowLabels[rowCount] = SpaceFeedRules.ContactLabel(row.Class, row.Percent);
                    rowCount++;
                }
            }
            Project();
        }

        /// <summary>
        /// Point the sensor. <paramref name="lineOfSight"/> is the unit vector from the
        /// aim point up to the orbital platform; <paramref name="footprint"/> is the ground width framed.
        /// Standoff distance adapts to footprint to maintain a stable, non-collapsing field of view.
        /// </summary>
        public void Aim(Vector3 aimLocal, Vector3 lineOfSight, Vector3 along, float footprint, float framesPerSecond)
        {
            if (cam == null || colour == null || verdict != OpticalVerdict.Ok) return;
            cleared = false;
            Vector3 los = lineOfSight.sqrMagnitude > 1e-6f ? lineOfSight.normalized : Vector3.up;

            // Adaptive standoff: scale camera distance proportional to footprint
            standoff = Mathf.Clamp(footprint * 2.8f, MinStandoff, MaxStandoff);
            Vector3 camPos = aimLocal + los * standoff;

            // Terrain clearance guard: ensure camera does not clip through mountains or hills
            float minAlt = aimLocal.y + Mathf.Max(50f, standoff * 0.12f);
            if (camPos.y < minAlt) camPos.y = minAlt;
            transform.position = camPos;

            Vector3 up = Vector3.ProjectOnPlane(along, los);
            if (up.sqrMagnitude < 1e-4f) up = Vector3.ProjectOnPlane(Vector3.forward, los);
            transform.rotation = Quaternion.LookRotation((aimLocal - camPos).normalized, up.normalized);

            // Dynamic FOV and clipping planes
            float tall = footprint / Mathf.Max(0.1f, cam.aspect);
            cam.fieldOfView = Mathf.Clamp(2f * Mathf.Atan(tall * 0.5f / standoff) * Mathf.Rad2Deg, 8f, 55f);
            cam.nearClipPlane = Mathf.Max(2f, standoff * 0.015f);
            cam.farClipPlane = standoff * 4.0f;
            posed = true;
            Project();

            if (!Visible) return;
            float interval = 1f / Mathf.Clamp(framesPerSecond, 0.5f, 15f);
            if (Time.unscaledTime >= nextFrame)
            {
                nextFrame = Time.unscaledTime + interval;
                cam.enabled = true;
                enabledFrame = Time.frameCount;
            }
        }

        private void Project()
        {
            brackets.Clear();
            if (cam == null || !posed || verdict != OpticalVerdict.Ok) return;
            for (int i = 0; i < rowCount; i++)
            {
                Vector3 view = cam.WorldToViewportPoint(rowPoints[i].ToLocalPosition());
                if (view.z <= 0f || view.x < 0f || view.x > 1f || view.y < 0f || view.y > 1f) continue;
                brackets.Add(new OpticalBracket(rows[i].Id, new Vector2(view.x, view.y), rows[i].Class, rows[i].Moving, rowLabels[i]));
            }
        }

        private void LateUpdate()
        {
            // A camera enabled for one frame renders once; switch it off again afterwards.
            if (cam != null && cam.enabled && Time.frameCount > enabledFrame) cam.enabled = false;
            // The end callback restores the scene's fog; if it never ran, put it back now rather than leave the game hazy.
            if (fogSaved && Time.frameCount > fogSavedFrame) RestoreFog();
        }

        private void OnBeginCameraRendering(ScriptableRenderContext context, Camera camera)
        {
            if (camera != cam) return;
            if (!fogSaved)
            {
                fogWas = RenderSettings.fog;
                fogModeWas = RenderSettings.fogMode;
                fogDensityWas = RenderSettings.fogDensity;
                fogColourWas = RenderSettings.fogColor;
                fogSaved = true;
                fogSavedFrame = Time.frameCount;
            }
            // The feed is a clear satellite look unless cloud or rain hazes it: soften with a bounded exponential fog that never
            // outlives this camera's render.
            if (softness > 0.01f)
            {
                RenderSettings.fog = true;
                RenderSettings.fogMode = FogMode.ExponentialSquared;
                RenderSettings.fogColor = HazeColour;
                RenderSettings.fogDensity = Mathf.Sqrt(HazeOpticalDepth * softness) / Mathf.Max(MinStandoff, standoff);
            }
            else RenderSettings.fog = false;
        }

        private void OnEndCameraRendering(ScriptableRenderContext context, Camera camera)
        {
            if (camera != cam) return;
            RestoreFog();
            FramesRendered++;
        }

        private void RestoreFog()
        {
            if (!fogSaved) return;
            RenderSettings.fog = fogWas;
            RenderSettings.fogMode = fogModeWas;
            RenderSettings.fogDensity = fogDensityWas;
            RenderSettings.fogColor = fogColourWas;
            fogSaved = false;
        }

        private void OnDisable() => RestoreFog();

        private void OnDestroy()
        {
            RenderPipelineManager.beginCameraRendering -= OnBeginCameraRendering;
            RenderPipelineManager.endCameraRendering -= OnEndCameraRendering;
            RestoreFog();
            brackets.Clear();
            rowCount = 0;
            if (cam != null) cam.targetTexture = null;
            if (colour != null)
            {
                colour.Release();
                Destroy(colour);
            }
            FxRtPool.Disown(colour);
        }
    }
}
