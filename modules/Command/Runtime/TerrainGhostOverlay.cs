using System.Collections.Generic;
using BoscaliSummer.Core.Game;
using BoscaliSummer.Core.Lifecycle;
using BoscaliSummer.Modules.Command.Configuration;
using BoscaliSummer.Modules.Command.Domain;
using BoscaliSummer.Modules.Command.Presentation.MapUi;
using NuclearOption.UIStyleSystem;
using UnityEngine;
using UnityEngine.Rendering;

namespace BoscaliSummer.Modules.Command.Runtime
{
    /// <summary>
    /// The visor's terrain ghost: a very faint wire relief of the real ground around and below the player's aircraft, so the pilot
    /// always senses the surroundings. One mesh and one material live for the scene; the mesh is the <see cref="TerrainGhostGrid"/>
    /// (heights rebuilt when the aircraft enters another cell, colours at most twice a second), drawn additively and without a depth
    /// test by the main gameplay camera, or, in the cockpit view, by the cockpit overlay camera instead (never both in one frame),
    /// so the glass and the cockpit's opaque parts do not cover it: see <see cref="GhostCockpitMath"/>. The grid is placed from the
    /// cell's global position every frame, after the floating origin has shifted, or it would flash. Strength is the pilot's TerrainGhost setting times a slow fade that is open only
    /// while the aircraft flies and the HUD overlays are up. Fails open: no baked terrain for the map, or no shader, and it draws nothing.
    /// </summary>
    // Native CameraStateManager (order 2) shifts the floating origin first.
    [DefaultExecutionOrder(100)]
    internal sealed class TerrainGhostOverlay : MonoBehaviour, ISceneService
    {
        private const float FadeSeconds = .4f;
        private const float ColourInterval = .5f;
        private const float MinSpeed = 30f;
        private const float MinHeight = 2f;
        private const float RetryInterval = 2f;
        // After the cloud and smoke particle queues (2958 to 3001), before the overlay band: the ghost sits on the finished scene.
        private const int Queue = 3500;
        // Which camera the last frame drew on, as the sim hook reports it.
        private const string None = "none", MainCamera = "main", CockpitView = "cockpit";
        private static readonly GhostTint FallbackInk = new GhostTint(56, 222, 82);
        private static readonly GhostTint FallbackCaution = new GhostTint(243, 217, 74);

        internal static TerrainGhostOverlay Live { get; private set; }

        private CommandSettings settings;
        private TerrainHeightField field;
        private float nextField;
        private Mesh mesh;
        private Material material;
        private bool shaderMissing;
        private GhostVertex[] vertices;
        private int cellX, cellZ;
        private bool placed;
        private float fade, paintedStrength, nextColours;
        private float planeX, planeY, planeZ;
        private bool seenPlane;
        private int glowing;
        private float rebuildMs;
        private Camera cockpitCamera;
        private string drawnOn = None;

        /// <summary>The sim hook's strength in place of the saved setting; memory only, cleared with the scene.</summary>
        internal float? StrengthOverride { get; set; }

        internal void Configure(CommandSettings value)
        {
            settings = value;
            Live = this;
        }

        private void LateUpdate()
        {
            drawnOn = None;
            if (Application.isBatchMode || settings == null) return;
            float setting = Mathf.Clamp01(StrengthOverride ?? settings.TerrainGhost.Value);
            if (setting <= 0f)
            {
                fade = 0f;
                return;
            }
            // Another map's heights (the last mission's, or the map image changed under us): drop them and fetch this map's.
            if (field != null && !MfdTerrainRelief.IsCurrent(field))
            {
                field = null;
                placed = false;
                nextField = 0f;
            }
            bool flying = LocalAircraft(out Aircraft plane);
            if (plane != null)
            {
                // Loaded while the aircraft still sits on the ramp, not as a hitch in flight.
                if (field == null) Acquire();
                GlobalPosition at = plane.GlobalPosition();
                planeX = at.x;
                planeY = at.y;
                planeZ = at.z;
                seenPlane = true;
            }
            fade = Mathf.MoveTowards(fade, flying && CockpitOverlayGate.Open() ? 1f : 0f, Time.unscaledDeltaTime / FadeSeconds);
            float strength = setting * fade;
            if (strength <= .001f || !seenPlane || field == null || !Prepare()) return;
            CameraStateManager cameras = SceneSingleton<CameraStateManager>.i;
            Camera camera = cameras != null ? cameras.mainCamera : null;
            if (camera == null) return;

            TerrainGhostGrid.Snap(planeX, planeZ, out int cx, out int cz);
            bool moved = !placed || cx != cellX || cz != cellZ;
            if (moved || Time.unscaledTime >= nextColours || Mathf.Abs(strength - paintedStrength) > .01f)
                Refresh(cx, cz, strength, moved);
            Vector3 centre = new GlobalPosition(TerrainGhostGrid.Anchor(cellX), 0f, TerrainGhostGrid.Anchor(cellZ)).ToLocalPosition();
            Matrix4x4 model = Matrix4x4.Translate(centre);
            Camera target = camera;
            int layer = 0;
            Camera cockpit = CockpitOverlay(cameras, out int cockpitLayer);
            if (cockpit != null)
            {
                // Same screen position, inside the overlay camera's 5 m of depth: inverse(V_cockpit) x Scale x V_main x M.
                GhostCockpitMath.Scales(cockpit.fieldOfView, camera.fieldOfView, cockpit.farClipPlane, out float lateral, out float depth);
                model = cockpit.cameraToWorldMatrix * Matrix4x4.Scale(new Vector3(lateral, lateral, depth)) * camera.worldToCameraMatrix * model;
                target = cockpit;
                layer = cockpitLayer;
            }
            drawnOn = cockpit != null ? CockpitView : MainCamera;
            Graphics.DrawMesh(mesh, model, material, layer, target, 0, null,
                ShadowCastingMode.Off, false, null, LightProbeUsage.Off);
        }

        /// <summary>
        /// The cockpit overlay camera while it is rendering (the cockpit view), through the game's own reference, cached and found again
        /// once destroyed; null in every other view, or when it sees no layer to draw on (the main camera draws the ghost then).
        /// </summary>
        private Camera CockpitOverlay(CameraStateManager cameras, out int layer)
        {
            layer = 0;
            if (cockpitCamera == null) cockpitCamera = cameras.cockpitCamRender;
            Camera overlay = cockpitCamera;
            if (overlay == null || !overlay.isActiveAndEnabled || cameras.currentState != cameras.cockpitState) return null;
            layer = GhostCockpitMath.Layer(overlay.cullingMask);
            return layer >= 0 ? overlay : null;
        }

        /// <summary>The living local aircraft, and whether it is airborne and fast enough for the ghost to be wanted.</summary>
        private static bool LocalAircraft(out Aircraft plane)
        {
            plane = null;
            if (!GameManager.GetLocalAircraft(out Aircraft local) || local == null || local.disabled) return false;
            plane = local;
            return local.speed >= MinSpeed && local.radarAlt > MinHeight;
        }

        private bool Acquire()
        {
            if (field != null) return true;
            if (Time.unscaledTime < nextField) return false;
            nextField = Time.unscaledTime + RetryInterval;
            return MfdTerrainRelief.TryGetHeightField(out field);
        }

        private bool Prepare()
        {
            if (mesh != null && material != null) return true;
            if (shaderMissing) return false;
            Shader shader = Shader.Find("Hidden/Internal-Colored");
            if (shader == null)
            {
                shaderMissing = true;
                return false;
            }
            material = new Material(shader) { name = "BoscaliTerrainGhost", hideFlags = HideFlags.HideAndDontSave, renderQueue = Queue };
            material.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
            material.SetInt("_DstBlend", (int)BlendMode.One);
            material.SetInt("_Cull", (int)CullMode.Off);
            material.SetInt("_ZWrite", 0);
            material.SetInt("_ZTest", (int)CompareFunction.Always);
            vertices = new GhostVertex[TerrainGhostGrid.VertexCount];
            mesh = new Mesh { name = "BoscaliTerrainGhost", hideFlags = HideFlags.HideAndDontSave };
            mesh.MarkDynamic();
            mesh.SetVertexBufferParams(vertices.Length,
                new VertexAttributeDescriptor(VertexAttribute.Position, VertexAttributeFormat.Float32, 3),
                new VertexAttributeDescriptor(VertexAttribute.Color, VertexAttributeFormat.UNorm8, 4));
            mesh.SetVertexBufferData(vertices, 0, 0, vertices.Length);
            mesh.SetIndices(TerrainGhostGrid.BuildIndices(), MeshTopology.Lines, 0, false);
            // Vertices stay in the grid's own frame under the cell centre; the box only has to keep the draw from being culled.
            mesh.bounds = new Bounds(Vector3.zero, new Vector3(2f * TerrainGhostGrid.Radius, 30000f, 2f * TerrainGhostGrid.Radius));
            return true;
        }

        private void Refresh(int cx, int cz, float strength, bool moved)
        {
            long started = System.Diagnostics.Stopwatch.GetTimestamp();
            if (moved)
            {
                TerrainGhostGrid.FillHeights(field, cx, cz, vertices);
                cellX = cx;
                cellZ = cz;
                placed = true;
            }
            ColorTheme theme = ThemeManager.Active != null ? ThemeManager.Active.ColorTheme : null;
            glowing = TerrainGhostGrid.FillColours(cellX, cellZ, planeX, planeY, planeZ, strength,
                theme != null ? Tint(theme.AllClear) : FallbackInk, theme != null ? Tint(theme.Warning) : FallbackCaution,
                field.SeaLevel, vertices);
            mesh.SetVertexBufferData(vertices, 0, 0, vertices.Length, 0,
                MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices | MeshUpdateFlags.DontNotifyMeshUsers);
            paintedStrength = strength;
            nextColours = Time.unscaledTime + ColourInterval;
            rebuildMs = (System.Diagnostics.Stopwatch.GetTimestamp() - started) * 1000f / System.Diagnostics.Stopwatch.Frequency;
        }

        private static GhostTint Tint(Color c) => new GhostTint(Byte(c.r), Byte(c.g), Byte(c.b));

        private static byte Byte(float v) => (byte)(Mathf.Clamp01(v) * 255f + .5f);

        /// <summary>The overlay as the sim hook reports it. Asks for the heightfield and the material first, so a probe before the first draw still answers them.</summary>
        internal Dictionary<string, object> State()
        {
            if (settings != null && !Application.isBatchMode)
            {
                if (field == null) MfdTerrainRelief.TryGetHeightField(out field);
                if (field != null) Prepare();
            }
            return new Dictionary<string, object>
            {
                { "ok", true },
                { "saved", false },
                { "strength", Mathf.Clamp01(StrengthOverride ?? settings?.TerrainGhost.Value ?? 0f) },
                { "supportedMap", field != null },
                { "materialFound", material != null },
                { "vertexCount", mesh != null ? mesh.vertexCount : 0 },
                { "lastRebuildMs", rebuildMs },
                { "fade", fade },
                { "cautionVertices", glowing },
                { "camera", drawnOn },
                { "cockpitCamera", cockpitCamera != null },
            };
        }

        public void ResetForScene()
        {
            if (mesh != null) Destroy(mesh);
            if (material != null) Destroy(material);
            mesh = null;
            material = null;
            vertices = null;
            field = null;
            shaderMissing = false;
            cockpitCamera = null;
            drawnOn = None;
            placed = seenPlane = false;
            fade = paintedStrength = nextColours = nextField = rebuildMs = 0f;
            glowing = 0;
            StrengthOverride = null;
        }

        private void OnDestroy()
        {
            if (Live == this) Live = null;
            ResetForScene();
        }
    }
}
