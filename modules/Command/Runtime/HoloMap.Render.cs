using System.Diagnostics;
using System.Reflection;
using BoscaliSummer.Modules.Command.Domain;
using HarmonyLib;
using NuclearOption.UIStyleSystem;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

namespace BoscaliSummer.Modules.Command.Runtime
{
    /// <summary>
    /// The hologram's picture. The wire relief is one mesh (the <see cref="TerrainGhostGrid"/> topology with <see cref="HoloGrid"/>'s cell size), refilled only when the aircraft enters
    /// another cell or the range or the HUD ink changes; turning the picture (heading up) is the view matrix, never a rebuild. At 20 Hz while the holo is up it is drawn into a render texture of the
    /// mod's own by a command buffer executed on the spot (clear to opaque black, view and projection set by hand, additive lines with no depth test), so no game camera is touched; a RawImage
    /// under the HUD canvas then shows the texture through the game's additive UI material, where black adds nothing.
    /// </summary>
    internal sealed partial class HoloMap
    {
        private const int TextureSize = 768;
        private const string LineShader = "Hidden/Internal-Colored";
        private const string AdditiveShader = "Unlit/AdditiveTextShader";
        private static readonly GhostTint FallbackInk = new GhostTint(127, 223, 154);
        private static readonly FieldInfo WeaponStatusField = AccessTools.Field(typeof(CombatHUD), "weaponStatus");
        private static readonly FieldInfo WeaponImageField = AccessTools.Field(typeof(WeaponStatus), "weaponImage");

        private Mesh mesh;
        private Material lineMaterial;
        private Material additive;
        private bool additiveOwned;
        private RenderTexture rt;
        private CommandBuffer commands;
        private GhostVertex[] vertices;
        private readonly float[] viewValues = new float[HoloView.Elements];
        private readonly float[] projectionValues = new float[HoloView.Elements];
        private Matrix4x4 viewMatrix, projectionMatrix;
        private int cellX, cellZ, gridRange;
        private bool placed;
        private int inkR = -1, inkG, inkB;
        private Color inkColour = new Color(.5f, .87f, .6f, 1f);
        private float highest, lastRenderMs;
        private int litVertices;

        private bool PrepareRender()
        {
            if (mesh != null && lineMaterial != null && rt != null && commands != null) return true;
            ReleaseRender();
            Shader shader = Shader.Find(LineShader);
            if (shader == null) return false;
            lineMaterial = new Material(shader) { name = "BoscaliHoloLines", hideFlags = HideFlags.HideAndDontSave };
            lineMaterial.SetInt("_SrcBlend", (int)BlendMode.One);
            lineMaterial.SetInt("_DstBlend", (int)BlendMode.One);
            lineMaterial.SetInt("_Cull", (int)CullMode.Off);
            lineMaterial.SetInt("_ZWrite", 0);
            lineMaterial.SetInt("_ZTest", (int)CompareFunction.Always);

            vertices = new GhostVertex[TerrainGhostGrid.VertexCount];
            mesh = new Mesh { name = "BoscaliHoloMap", hideFlags = HideFlags.HideAndDontSave };
            mesh.MarkDynamic();
            mesh.SetVertexBufferParams(vertices.Length,
                new VertexAttributeDescriptor(VertexAttribute.Position, VertexAttributeFormat.Float32, 3),
                new VertexAttributeDescriptor(VertexAttribute.Color, VertexAttributeFormat.UNorm8, 4));
            mesh.SetVertexBufferData(vertices, 0, 0, vertices.Length);
            mesh.SetIndices(TerrainGhostGrid.BuildIndices(), MeshTopology.Lines, 0, false);
            mesh.bounds = new Bounds(Vector3.zero, new Vector3(200000f, 60000f, 200000f));

            // Linear, so the bytes written are the bytes shown, as with any other UI colour; no depth, the lines are drawn without a test.
            rt = new RenderTexture(TextureSize, TextureSize, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear)
            {
                name = "BoscaliHoloMap", antiAliasing = 4, useMipMap = false, autoGenerateMips = false,
                filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.HideAndDontSave,
            };
            if (!rt.Create())
            {
                ReleaseRender();
                return false;
            }
            commands = new CommandBuffer { name = "BoscaliHoloMap" };
            placed = false;
            return true;
        }

        /// <summary>
        /// The game's additive UI material: the one its weapon panel draws the weapon picture with (white lines on an opaque black ground), taken from the panel's image; failing that, a
        /// material of the same shader (<c>Unlit/AdditiveTextShader</c>). Without either the holo is unavailable.
        /// </summary>
        private bool PrepareAdditive()
        {
            if (additive != null) return true;
            additive = null;
            additiveOwned = false;
            CombatHUD hud = SceneSingleton<CombatHUD>.i;
            WeaponStatus status = hud != null && WeaponStatusField != null ? WeaponStatusField.GetValue(hud) as WeaponStatus : null;
            Image weapon = status != null && WeaponImageField != null ? WeaponImageField.GetValue(status) as Image : null;
            Material native = weapon != null ? weapon.material : null;
            if (native != null && native != Canvas.GetDefaultCanvasMaterial() && native.shader != null && native.shader.name == AdditiveShader)
            {
                additive = native;
                return true;
            }
            Shader shader = Shader.Find(AdditiveShader);
            if (shader == null) return false;
            additive = new Material(shader) { name = "BoscaliHoloAdditive", hideFlags = HideFlags.HideAndDontSave };
            additiveOwned = true;
            return true;
        }

        /// <summary>One picture: the mesh refilled if needed, the aircraft's place and heading read, the texture drawn, the marks laid over it.</summary>
        private void Refresh()
        {
            long started = Stopwatch.GetTimestamp();
            if (plane == null || field == null || rt == null || marks == null) return;
            GlobalPosition at = plane.GlobalPosition();
            planeX = at.x;
            planeY = at.y;
            planeZ = at.z;
            int range = RangeKm;
            radius = HoloGrid.Radius(range);
            heading = NorthUp ? 0f : plane.transform.eulerAngles.y;
            Fill(range);
            Draw(range);
            DrawMarks(range);
            lastRenderMs = (Stopwatch.GetTimestamp() - started) * 1000f / Stopwatch.Frequency;
        }

        private void Fill(int range)
        {
            float cell = HoloGrid.CellMetres(range);
            HoloGrid.Snap(planeX, planeZ, cell, out int cx, out int cz);
            ColorTheme theme = ThemeManager.Active != null ? ThemeManager.Active.ColorTheme : null;
            GhostTint ink = theme != null ? Tint(theme.AllClear) : FallbackInk;
            bool moved = !placed || cx != cellX || cz != cellZ || range != gridRange;
            bool inked = ink.R != inkR || ink.G != inkG || ink.B != inkB;
            if (!moved && !inked) return;
            if (moved)
            {
                highest = HoloGrid.FillHeights(field, cell, cx, cz, vertices);
                cellX = cx;
                cellZ = cz;
                gridRange = range;
                placed = true;
            }
            litVertices = HoloGrid.FillColours(cell, field.SeaLevel, highest, ink, vertices);
            inkR = ink.R;
            inkG = ink.G;
            inkB = ink.B;
            inkColour = new Color(ink.R / 255f, ink.G / 255f, ink.B / 255f, 1f);
            mesh.SetVertexBufferData(vertices, 0, 0, vertices.Length, 0,
                MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices | MeshUpdateFlags.DontNotifyMeshUsers);
        }

        private void Draw(int range)
        {
            float cell = HoloGrid.CellMetres(range);
            // Mesh vertices are metres from the snapped cell centre with global elevations: the scene is metres from the aircraft, heights from the sea.
            var model = Matrix4x4.Translate(new Vector3(HoloGrid.Anchor(cellX, cell) - planeX, -field.SeaLevel, HoloGrid.Anchor(cellZ, cell) - planeZ));
            HoloView.View(heading, radius, viewValues);
            HoloView.Projection(radius, projectionValues);
            ToMatrix(viewValues, ref viewMatrix);
            ToMatrix(projectionValues, ref projectionMatrix);
            commands.Clear();
            commands.SetRenderTarget(rt);
            commands.ClearRenderTarget(false, true, Color.black);
            commands.SetViewProjectionMatrices(viewMatrix, GL.GetGPUProjectionMatrix(projectionMatrix, true));
            commands.DrawMesh(mesh, model, lineMaterial, 0, 0);
            Graphics.ExecuteCommandBuffer(commands);
        }

        private static void ToMatrix(float[] values, ref Matrix4x4 matrix)
        {
            for (int row = 0; row < 4; row++)
            for (int column = 0; column < 4; column++)
                matrix[row, column] = values[row * 4 + column];
        }

        private static GhostTint Tint(Color c) => new GhostTint(Byte(c.r), Byte(c.g), Byte(c.b));

        private static byte Byte(float v) => (byte)(Mathf.Clamp01(v) * 255f + .5f);

        private void ReleaseRender()
        {
            if (commands != null) commands.Release();
            commands = null;
            if (rt != null)
            {
                rt.Release();
                Destroy(rt);
            }
            rt = null;
            if (mesh != null) Destroy(mesh);
            mesh = null;
            if (lineMaterial != null) Destroy(lineMaterial);
            lineMaterial = null;
            if (additive != null && additiveOwned) Destroy(additive);
            additive = null;
            additiveOwned = false;
            vertices = null;
            placed = false;
            inkR = -1;
        }

        private void ReleaseAll()
        {
            RestoreMinimap();
            ReleaseUi();
            ReleaseRender();
            ResetMarks();
        }
    }
}
