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
    /// The hologram's picture: a solid terrain tile. The ground is one lattice mesh (<see cref="HoloGrid"/>: a vertex per node, three index sets: the east-west lines, the north-south lines and the filled surface),
    /// refilled only when the aircraft enters another cell or the range, the orientation, the presentation or the HUD ink changes; turning the picture (heading up) is the view matrix, never a rebuild. At 10 Hz as the
    /// MINI and 20 Hz as the BIG one (every frame while it changes size) it is drawn into a render texture of the mod's own by a command buffer executed on the spot: clear colour and depth to opaque black, a viewport as many
    /// pixels as the frame shows (times up to one and a half), the camera of <see cref="HoloView.Fit"/>, then THREE passes with the built-in line shader, so no game camera is touched. 1 the surface as filled triangles in pure black with
    /// depth written (black adds nothing on the additive display, it only hides the lines behind a ridge); 2 the grid lines, additive, depth tested LessEqual and lifted <see cref="HoloGrid.LineLift"/> cells above the surface so they do not
    /// fight it, the lines running across the view at full strength and the ones running away at <see cref="HoloGrid.AwayStrength"/> (the material's colour is the strength, the vertex colour is the height shade); 3 the edge fade: a ring
    /// of black-to-white quads in clip space multiplied over the last <see cref="HoloGrid.FadeShare"/> of the frame, so the grid simply ends there. A RawImage under the HUD canvas shows that part of the texture through the game's
    /// additive UI material, where black adds nothing. Without a depth buffer (the texture could not be created with one) the surface pass is skipped and the lines are drawn with no depth test: the see-through look.
    /// Every picture is timed (last and worst milliseconds, the scan's share taken out; the mesh fill timed apart) for the sim hook.
    /// </summary>
    internal sealed partial class HoloMap
    {
        private const int TextureWidth = HoloPresentation.TextureWidth, TextureHeight = HoloPresentation.TextureHeight;
        private const string LineShader = "Hidden/Internal-Colored";
        private const string AdditiveShader = "Unlit/AdditiveTextShader";
        // The mesh's sub-meshes.
        private const int EastWestSub = 0, NorthSouthSub = 1, SurfaceSub = 2;
        private static readonly GhostTint FallbackInk = new GhostTint(127, 223, 154);
        private static readonly FieldInfo WeaponStatusField = AccessTools.Field(typeof(CombatHUD), "weaponStatus");
        private static readonly FieldInfo WeaponImageField = AccessTools.Field(typeof(WeaponStatus), "weaponImage");
        private static readonly int ColourId = Shader.PropertyToID("_Color");

        private Mesh mesh, fadeMesh;
        private Material surfaceMaterial, lineMaterial, fadeMaterial;
        private Material additive;
        private bool additiveOwned;
        private MaterialPropertyBlock eastWestBlock, northSouthBlock;
        private RenderTexture rt;
        private CommandBuffer commands;
        private GhostVertex[] vertices;
        private int[] eastWestIndices, northSouthIndices, triangleIndices;
        private readonly float[] viewValues = new float[HoloView.Elements];
        private readonly float[] projectionValues = new float[HoloView.Elements];
        private Matrix4x4 viewMatrix, projectionMatrix;
        private HoloCamera camera;
        // The lattice: its cell, its snapped centre, how far it reaches (cells from the centre to a side, and metres of the ground the frame can see) and what it was made for.
        private float cellMetres, reach;
        private int cellX, cellZ, halfX, halfZ, nodes;
        private HoloShape latticeShape;
        private bool latticeNorth, placed;
        private int latticeRange;
        private int inkR = -1, inkG, inkB;
        private Color inkColour = new Color(.5f, .87f, .6f, 1f);
        private float highest, lastRenderMs, worstRenderMs, lastScanMs, worstScanMs, scanMs, lastFillMs, worstFillMs;
        private int litVertices, renders, scans, fills;
        // The part of the texture a picture is drawn into, in pixels (from its bottom-left corner), and whether the texture has a depth buffer (the solid look) or not (the see-through one).
        private int pixelsWide = TextureWidth, pixelsHigh = TextureHeight;
        private bool depthPath;

        private bool PrepareRender()
        {
            if (mesh != null && lineMaterial != null && surfaceMaterial != null && fadeMaterial != null && fadeMesh != null && rt != null && commands != null) return true;
            ReleaseRender();
            Shader shader = Shader.Find(LineShader);
            if (shader == null) return false;
            // The surface: opaque, black (the material's colour multiplies the vertex colour), depth written and tested. The lines: additive, tested but never written. The fade: multiplies what is there.
            surfaceMaterial = Line(shader, "BoscaliHoloSurface", BlendMode.One, BlendMode.Zero, 1, CompareFunction.LessEqual, Color.black);
            lineMaterial = Line(shader, "BoscaliHoloLines", BlendMode.One, BlendMode.One, 0, CompareFunction.LessEqual, Color.white);
            fadeMaterial = Line(shader, "BoscaliHoloFade", BlendMode.Zero, BlendMode.SrcColor, 0, CompareFunction.Always, Color.white);
            eastWestBlock = new MaterialPropertyBlock();
            northSouthBlock = new MaterialPropertyBlock();

            vertices = new GhostVertex[HoloGrid.MaxNodes];
            eastWestIndices = new int[HoloGrid.MaxLineIndices];
            northSouthIndices = new int[HoloGrid.MaxLineIndices];
            triangleIndices = new int[HoloGrid.MaxTriangleIndices];
            mesh = new Mesh { name = "BoscaliHoloMap", hideFlags = HideFlags.HideAndDontSave };
            mesh.MarkDynamic();
            mesh.SetVertexBufferParams(vertices.Length,
                new VertexAttributeDescriptor(VertexAttribute.Position, VertexAttributeFormat.Float32, 3),
                new VertexAttributeDescriptor(VertexAttribute.Color, VertexAttributeFormat.UNorm8, 4));
            mesh.SetVertexBufferData(vertices, 0, 0, vertices.Length);
            mesh.subMeshCount = 3;
            mesh.bounds = new Bounds(Vector3.zero, new Vector3(400000f, 60000f, 400000f));
            fadeMesh = BuildFadeMesh();

            // Linear, so the bytes written are the bytes shown, as with any other UI colour. With a depth buffer when the platform gives one; without, the lines are all there is.
            rt = Texture(24);
            depthPath = rt != null;
            if (rt == null) rt = Texture(0);
            if (rt == null)
            {
                ReleaseRender();
                return false;
            }
            lineMaterial.SetInt("_ZTest", (int)(depthPath ? CompareFunction.LessEqual : CompareFunction.Always));
            commands = new CommandBuffer { name = "BoscaliHoloMap" };
            placed = false;
            latticeRange = 0;
            halfX = halfZ = nodes = 0;
            return true;
        }

        private static Material Line(Shader shader, string name, BlendMode source, BlendMode destination, int zWrite, CompareFunction zTest, Color colour)
        {
            var material = new Material(shader) { name = name, hideFlags = HideFlags.HideAndDontSave };
            material.SetInt("_SrcBlend", (int)source);
            material.SetInt("_DstBlend", (int)destination);
            material.SetInt("_Cull", (int)CullMode.Off);
            material.SetInt("_ZWrite", zWrite);
            material.SetInt("_ZTest", (int)zTest);
            material.SetColor(ColourId, colour);
            return material;
        }

        /// <summary>The picture's texture with the given depth buffer bits, created; null when the platform will not (or throws): the caller then tries again without depth.</summary>
        private static RenderTexture Texture(int depthBits)
        {
            RenderTexture texture = null;
            try
            {
                texture = new RenderTexture(TextureWidth, TextureHeight, depthBits, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear)
                {
                    name = "BoscaliHoloMap", antiAliasing = 4, useMipMap = false, autoGenerateMips = false,
                    filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.HideAndDontSave,
                };
                if (texture.Create()) return texture;
            }
            catch (System.Exception)
            {
                // fall through: no texture of this kind
            }
            if (texture != null) Object.Destroy(texture);
            return null;
        }

        /// <summary>
        /// The edge fade: four rings of quads in clip space (-1..1 across the viewport) between the frame's edge and <see cref="HoloGrid.FadeShare"/> of it in, white inside and black at the edge, in steps of
        /// <see cref="HoloGrid.EdgeFade"/>; drawn with a multiply blend after the picture, it takes the last 5 % of the frame at each edge down to nothing. Built once.
        /// </summary>
        private static Mesh BuildFadeMesh()
        {
            const int steps = 3, layers = steps + 1;
            var points = new Vector3[layers * 4];
            var colours = new Color32[layers * 4];
            var triangles = new int[steps * 4 * 6];
            for (int k = 0; k < layers; k++)
            {
                float share = HoloGrid.FadeShare * k / steps, half = 1f - 2f * share;
                byte level = (byte)(HoloGrid.EdgeFade(share) * 255f + .5f);
                points[k * 4] = new Vector3(-half, -half, .5f);
                points[k * 4 + 1] = new Vector3(half, -half, .5f);
                points[k * 4 + 2] = new Vector3(half, half, .5f);
                points[k * 4 + 3] = new Vector3(-half, half, .5f);
                for (int c = 0; c < 4; c++) colours[k * 4 + c] = new Color32(level, level, level, 255);
            }
            int at = 0;
            for (int k = 0; k < steps; k++)
            for (int side = 0; side < 4; side++)
            {
                int outerA = k * 4 + side, outerB = k * 4 + (side + 1) % 4, innerA = (k + 1) * 4 + side, innerB = (k + 1) * 4 + (side + 1) % 4;
                triangles[at++] = outerA;
                triangles[at++] = outerB;
                triangles[at++] = innerB;
                triangles[at++] = outerA;
                triangles[at++] = innerB;
                triangles[at++] = innerA;
            }
            var fade = new Mesh { name = "BoscaliHoloFade", hideFlags = HideFlags.HideAndDontSave };
            fade.SetVertices(points);
            fade.SetColors(colours);
            fade.SetTriangles(triangles, 0);
            fade.bounds = new Bounds(Vector3.zero, new Vector3(4f, 4f, 4f));
            return fade;
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

        /// <summary>One picture: the camera fitted to the frame, the lattice refilled if needed, the aircraft's place and heading read, the texture drawn, the marks laid over it. Timed, without the scan that sometimes rides on it.</summary>
        private void Refresh()
        {
            long started = Stopwatch.GetTimestamp();
            if (plane == null || field == null || rt == null || marks == null || frameH < 1f) return;
            GlobalPosition at = plane.GlobalPosition();
            planeX = at.x;
            planeY = at.y;
            planeZ = at.z;
            int range = RangeKm;
            radius = HoloGrid.Radius(range);
            heading = NorthUp ? 0f : plane.transform.eulerAngles.y;
            scanMs = 0f;
            camera = HoloView.Fit(HoloPresentation.Tilt(shape), frameW / frameH, HoloPresentation.AircraftUp(shape, NorthUp), radius);
            camera.Heading = heading;
            Fill(range);
            Draw();
            DrawMarks(range);
            lastRenderMs = Mathf.Max(0f, Milliseconds(started) - scanMs);
            if (lastRenderMs > worstRenderMs) worstRenderMs = lastRenderMs;
            renders++;
        }

        private static float Milliseconds(long since) => (Stopwatch.GetTimestamp() - since) * 1000f / Stopwatch.Frequency;

        /// <summary>
        /// The lattice for this picture: its cell (<see cref="HoloGrid.CellMetres"/>), the size the frame needs (<see cref="HoloGrid.Extent"/>, from the camera of the smallest frame so every frame the picture grows or shrinks through is
        /// covered), its index sets when its size changed, and its nodes when the aircraft entered another cell or the picture's own settings or the ink changed. Timed apart (the fill is the one cost that grows with the grid).
        /// </summary>
        private void Fill(int range)
        {
            bool northUp = NorthUp;
            float cell = HoloGrid.CellMetres(range, HoloPresentation.GridCells(shape));
            bool remade = !placed || latticeShape != shape || latticeNorth != northUp || latticeRange != range;
            if (remade)
            {
                HoloCamera smallest = HoloView.Fit(HoloPresentation.Tilt(shape), HoloPresentation.ReferenceAspect, HoloPresentation.AircraftUp(shape, northUp), radius);
                HoloGrid.Extent(smallest, cell, northUp, out int hx, out int hz, out reach);
                if (hx != halfX || hz != halfZ)
                {
                    halfX = hx;
                    halfZ = hz;
                    nodes = HoloGrid.Nodes(hx, hz);
                    HoloGrid.BuildIndices(hx, hz, eastWestIndices, northSouthIndices, triangleIndices, out int eastWestCount, out int northSouthCount, out int triangleCount);
                    mesh.SetIndices(eastWestIndices, 0, eastWestCount, MeshTopology.Lines, EastWestSub, false);
                    mesh.SetIndices(northSouthIndices, 0, northSouthCount, MeshTopology.Lines, NorthSouthSub, false);
                    mesh.SetIndices(triangleIndices, 0, triangleCount, MeshTopology.Triangles, SurfaceSub, false);
                }
                latticeShape = shape;
                latticeNorth = northUp;
                latticeRange = range;
                cellMetres = cell;
            }
            HoloGrid.Snap(planeX, planeZ, cell, out int cx, out int cz);
            ColorTheme theme = ThemeManager.Active != null ? ThemeManager.Active.ColorTheme : null;
            GhostTint ink = theme != null ? Tint(theme.AllClear) : FallbackInk;
            bool moved = remade || cx != cellX || cz != cellZ;
            bool inked = ink.R != inkR || ink.G != inkG || ink.B != inkB;
            if (!moved && !inked) return;
            long started = Stopwatch.GetTimestamp();
            if (moved)
            {
                highest = HoloGrid.FillHeights(field, cell, cx, cz, halfX, halfZ, reach, vertices);
                cellX = cx;
                cellZ = cz;
                placed = true;
            }
            litVertices = HoloGrid.FillColours(field.SeaLevel, highest, ink, nodes, vertices);
            inkR = ink.R;
            inkG = ink.G;
            inkB = ink.B;
            inkColour = new Color(ink.R / 255f, ink.G / 255f, ink.B / 255f, 1f);
            mesh.SetVertexBufferData(vertices, 0, 0, nodes, 0,
                MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices | MeshUpdateFlags.DontNotifyMeshUsers);
            lastFillMs = Milliseconds(started);
            if (lastFillMs > worstFillMs) worstFillMs = lastFillMs;
            fills++;
        }

        private void Draw()
        {
            // Mesh vertices are metres from the snapped cell centre with global elevations: the scene is metres from the aircraft, heights from the sea. The lines ride a little above the surface.
            var offset = new Vector3(HoloGrid.Anchor(cellX, cellMetres) - planeX, -field.SeaLevel, HoloGrid.Anchor(cellZ, cellMetres) - planeZ);
            Matrix4x4 surfaceModel = Matrix4x4.Translate(offset), lineModel = Matrix4x4.Translate(offset + new Vector3(0f, HoloGrid.LineLift * cellMetres, 0f));
            HoloView.View(camera, viewValues);
            HoloView.Projection(camera, projectionValues);
            ToMatrix(viewValues, ref viewMatrix);
            ToMatrix(projectionValues, ref projectionMatrix);
            // The material's colour is the line's strength: the east-west lines (bearing 90) and the north-south ones (bearing 0) against the way the camera looks.
            float across = HoloGrid.LineStrength(90f, camera.Heading), away = HoloGrid.LineStrength(0f, camera.Heading);
            eastWestBlock.SetColor(ColourId, new Color(across, across, across, 1f));
            northSouthBlock.SetColor(ColourId, new Color(away, away, away, 1f));
            commands.Clear();
            commands.SetRenderTarget(rt);
            commands.ClearRenderTarget(true, true, Color.black);
            // After the clear, which takes the whole texture: the picture fills a part of it as big as the frame shows.
            commands.SetViewport(new Rect(0f, 0f, pixelsWide, pixelsHigh));
            commands.SetViewProjectionMatrices(viewMatrix, GL.GetGPUProjectionMatrix(projectionMatrix, true));
            if (depthPath) commands.DrawMesh(mesh, surfaceModel, surfaceMaterial, SurfaceSub, 0);
            commands.DrawMesh(mesh, lineModel, lineMaterial, EastWestSub, 0, eastWestBlock);
            commands.DrawMesh(mesh, lineModel, lineMaterial, NorthSouthSub, 0, northSouthBlock);
            commands.SetViewProjectionMatrices(Matrix4x4.identity, Matrix4x4.identity);
            commands.DrawMesh(fadeMesh, Matrix4x4.identity, fadeMaterial, 0, 0);
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
            if (fadeMesh != null) Destroy(fadeMesh);
            fadeMesh = null;
            if (surfaceMaterial != null) Destroy(surfaceMaterial);
            surfaceMaterial = null;
            if (lineMaterial != null) Destroy(lineMaterial);
            lineMaterial = null;
            if (fadeMaterial != null) Destroy(fadeMaterial);
            fadeMaterial = null;
            if (additive != null && additiveOwned) Destroy(additive);
            additive = null;
            additiveOwned = false;
            eastWestBlock = northSouthBlock = null;
            vertices = null;
            eastWestIndices = northSouthIndices = triangleIndices = null;
            placed = false;
            depthPath = false;
            halfX = halfZ = nodes = 0;
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
