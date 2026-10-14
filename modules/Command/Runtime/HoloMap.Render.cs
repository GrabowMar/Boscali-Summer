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
    /// The hologram's picture: a solid terrain tile. The ground is one lattice mesh (<see cref="HoloGrid"/>: a vertex per node and one wherever the shore cuts a segment, five index sets: the land's east-west lines, its north-south lines, the few lines over open water, the shoreline (<see cref="HoloGrid.BuildLines"/>) and the filled surface),
    /// refilled only when the aircraft enters another cell or the range, the orientation, the presentation or the HUD ink changes; turning the picture (heading up) is the view matrix, never a rebuild. Every frame (a
    /// picture is a tenth of a millisecond; at 10 Hz the turning map stuttered) it is drawn into a render texture of the mod's own by a command buffer executed on the spot: clear colour and depth to opaque black, a viewport as many
    /// pixels as the frame shows (times up to one and a half), the camera of <see cref="HoloView.Fit"/>, then THREE passes with the built-in line shader, so no game camera is touched. 1 the surface as filled triangles with
    /// depth written, the land at <see cref="HoloGrid.SurfaceFill"/> of its lines' light and the sea black (it hides the lines behind a ridge either way); 2 the grid lines and the shoreline, additive, depth tested LessEqual and lifted <see cref="HoloGrid.LineLift"/> cells above the surface so they do not
    /// fight it, the lines running across the view at full strength and the ones running away at <see cref="HoloGrid.AwayStrength"/> (the material's colour is the strength, the vertex colour is the height shade and the relief's light); 3 the edge fade: a ring
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
        private const int EastWestSub = 0, NorthSouthSub = 1, SurfaceSub = 2, WaterSub = 3, CoastSub = 4;
        private static readonly GhostTint FallbackInk = new GhostTint(127, 223, 154);
        private static readonly FieldInfo WeaponStatusField = AccessTools.Field(typeof(CombatHUD), "weaponStatus");
        private static readonly FieldInfo WeaponImageField = AccessTools.Field(typeof(WeaponStatus), "weaponImage");
        private static readonly int ColourId = Shader.PropertyToID("_Color");

        private Mesh mesh, fadeMesh;
        private Material surfaceMaterial, lineMaterial, fadeMaterial, opaqueMaterial;
        private Material additive;
        private bool additiveOwned;
        private MaterialPropertyBlock eastWestBlock, northSouthBlock, waterBlock, coastBlock;
        private RenderTexture rt;
        private CommandBuffer commands;
        private LatticeJob job;
        private int landEastWestCount, landNorthSouthCount, waterCount, coastCount;
        private readonly float[] viewValues = new float[HoloView.Elements];
        private readonly float[] projectionValues = new float[HoloView.Elements];
        private Matrix4x4 viewMatrix, projectionMatrix;
        private HoloCamera camera;
        // The lattice in the mesh: its cell, its snapped centre and how far it reaches (cells from the centre to a side); and the triangles the mesh holds, which only change with the lattice's size.
        private float cellMetres;
        private int cellX, cellZ, halfX, halfZ, nodes, surfaceHalfX, surfaceHalfZ;
        private bool placed;
        // The lattice the picture wants: its cell and size, the metres of ground the frame can see, and what those were worked out for.
        private float wantCell, reach;
        private int wantHalfX, wantHalfZ;
        private HoloShape latticeShape;
        private bool latticeNorth;
        private int latticeRange;
        private int inkR = -1, inkG, inkB;
        private Color inkColour = new Color(.5f, .87f, .6f, 1f);
        private float lastRenderMs, worstRenderMs, lastScanMs, worstScanMs, scanMs, lastFillMs, worstFillMs, lastWorkMs, worstWorkMs;
        private int litVertices, renders, scans, fills;
        // The part of the texture a picture is drawn into, in pixels (from its bottom-left corner), and whether the texture has a depth buffer (the solid look) or not (the see-through one).
        private int pixelsWide = TextureWidth, pixelsHigh = TextureHeight;
        private bool depthPath;

        private bool PrepareRender()
        {
            if (mesh != null && lineMaterial != null && surfaceMaterial != null && fadeMaterial != null && opaqueMaterial != null && fadeMesh != null && rt != null && commands != null) return true;
            ReleaseRender();
            Shader shader = Shader.Find(LineShader);
            if (shader == null) return false;
            // The surface: opaque, black (the material's colour multiplies the vertex colour), depth written and tested. The lines: additive, tested but never written. The fade: multiplies what is there.
            // The depth test is set through a material property, which Unity does not turn around for a reversed depth buffer (D3D11 and the like), so it is chosen here: "in front" is a greater depth there.
            CompareFunction nearer = SystemInfo.usesReversedZBuffer ? CompareFunction.GreaterEqual : CompareFunction.LessEqual;
            // The surface is drawn with its vertex alpha (land 1, sea 0): the land a dim fill of its own light, the sea black; either way it hides the lines behind it.
            surfaceMaterial = Line(shader, "BoscaliHoloSurface", BlendMode.SrcAlpha, BlendMode.Zero, 1, nearer, new Color(HoloGrid.SurfaceFill, HoloGrid.SurfaceFill, HoloGrid.SurfaceFill, 1f));
            lineMaterial = Line(shader, "BoscaliHoloLines", BlendMode.One, BlendMode.One, 0, nearer, Color.white);
            fadeMaterial = Line(shader, "BoscaliHoloFade", BlendMode.Zero, BlendMode.SrcColor, 0, CompareFunction.Always, Color.white);
            // The surface leaves the sea at alpha 0 and the HUD's additive material shows a texel by its alpha, so a last pass adds alpha alone over the whole picture.
            opaqueMaterial = Line(shader, "BoscaliHoloOpaque", BlendMode.One, BlendMode.One, 0, CompareFunction.Always, new Color(0f, 0f, 0f, 1f));
            eastWestBlock = new MaterialPropertyBlock();
            northSouthBlock = new MaterialPropertyBlock();
            waterBlock = new MaterialPropertyBlock();
            coastBlock = new MaterialPropertyBlock();

            job = new LatticeJob();
            mesh = new Mesh { name = "BoscaliHoloMap", hideFlags = HideFlags.HideAndDontSave };
            mesh.MarkDynamic();
            mesh.SetVertexBufferParams(job.Vertices.Length,
                new VertexAttributeDescriptor(VertexAttribute.Position, VertexAttributeFormat.Float32, 3),
                new VertexAttributeDescriptor(VertexAttribute.Color, VertexAttributeFormat.UNorm8, 4));
            mesh.SetVertexBufferData(job.Vertices, 0, 0, job.Vertices.Length);
            mesh.subMeshCount = 5;
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
            lineMaterial.SetInt("_ZTest", (int)(depthPath ? nearer : CompareFunction.Always));
            commands = new CommandBuffer { name = "BoscaliHoloMap" };
            placed = false;
            latticeRange = 0;
            halfX = halfZ = nodes = surfaceHalfX = surfaceHalfZ = 0;
            return true;
        }

        /// <summary>
        /// One fill of the lattice, made away from the frame: the heights, the line sort with the shoreline, the colours and the relief's light are plain arithmetic on the baked elevation (<see cref="HoloGrid"/>, no Unity
        /// types), so they run on a pool thread into these arrays while the picture goes on drawing the lattice the mesh already holds; the frame then only hands the finished arrays to the mesh. One fill at a
        /// time: the arrays are the worker's from <see cref="Running"/> being set until it is complete.
        /// </summary>
        private sealed class LatticeJob
        {
            internal readonly GhostVertex[] Vertices = new GhostVertex[HoloGrid.MaxVertices];
            internal readonly int[] EastWest = new int[HoloGrid.MaxLineIndices], NorthSouth = new int[HoloGrid.MaxLineIndices], Water = new int[HoloGrid.MaxLineIndices];
            internal readonly int[] Coast = new int[HoloGrid.MaxLineIndices], Crossings = new int[HoloGrid.MaxLineIndices], Triangles = new int[HoloGrid.MaxTriangleIndices];
            internal readonly System.Action Run;
            // Asked for.
            internal TerrainHeightField Field;
            internal float Cell, Reach;
            internal int HalfX, HalfZ, CellX, CellZ;
            internal GhostTint Ink;
            // Made.
            internal int TriangleCount, EastWestCount, NorthSouthCount, WaterCount, CoastCount, VertexCount, Lit;
            internal float Ms;
            internal System.Exception Error;
            internal System.Threading.Tasks.Task Running;
            private int trianglesHalfX = -1, trianglesHalfZ = -1;

            internal LatticeJob() { Run = Work; }

            internal void Work()
            {
                long started = Stopwatch.GetTimestamp();
                Error = null;
                try
                {
                    if (HalfX != trianglesHalfX || HalfZ != trianglesHalfZ)
                    {
                        HoloGrid.BuildIndices(HalfX, HalfZ, EastWest, NorthSouth, Triangles, out _, out _, out TriangleCount);
                        trianglesHalfX = HalfX;
                        trianglesHalfZ = HalfZ;
                    }
                    float highest = HoloGrid.FillHeights(Field, Cell, CellX, CellZ, HalfX, HalfZ, Reach, Vertices);
                    HoloGrid.BuildLines(Field, Cell, HalfX, HalfZ, CellX, CellZ, Field.SeaLevel, Vertices, Crossings, EastWest, NorthSouth, Water, Coast,
                        out EastWestCount, out NorthSouthCount, out WaterCount, out CoastCount, out VertexCount);
                    Lit = HoloGrid.FillColours(Field.SeaLevel, highest, Ink, HoloGrid.Nodes(HalfX, HalfZ), Vertices);
                    HoloGrid.Shade(HalfX, HalfZ, Cell, Field.SeaLevel, Ink, VertexCount, Vertices);
                }
                catch (System.Exception error)
                {
                    Error = error;
                }
                Ms = Milliseconds(started);
            }
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
            // The outermost ring's corners are the viewport's: the same four vertices cover the whole picture for the alpha pass.
            fade.subMeshCount = 2;
            fade.SetTriangles(new[] { 0, 1, 2, 0, 2, 3 }, 1);
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
        /// covered) and its centre. When the aircraft entered another cell, or the picture's own settings or the ink changed, a fill is asked for (<see cref="LatticeJob"/>, on a pool thread) and taken into the mesh on the
        /// first frame that finds it done; it used to be made on the frame, a stall of 2 to 4 ms every few seconds of flight. Timed apart: the frame's share and the worker's.
        /// </summary>
        private void Fill(int range)
        {
            bool northUp = NorthUp;
            if (latticeShape != shape || latticeNorth != northUp || latticeRange != range)
            {
                wantCell = HoloGrid.CellMetres(range, HoloPresentation.GridCells(shape));
                HoloCamera smallest = HoloView.Fit(HoloPresentation.Tilt(shape), HoloPresentation.ReferenceAspect, HoloPresentation.AircraftUp(shape, northUp), radius);
                HoloGrid.Extent(smallest, wantCell, northUp, out wantHalfX, out wantHalfZ, out reach);
                latticeShape = shape;
                latticeNorth = northUp;
                latticeRange = range;
            }
            HoloGrid.Snap(planeX, planeZ, wantCell, out int cx, out int cz);
            ColorTheme theme = ThemeManager.Active != null ? ThemeManager.Active.ColorTheme : null;
            GhostTint ink = theme != null ? Tint(theme.AllClear) : FallbackInk;
            inkColour = new Color(ink.R / 255f, ink.G / 255f, ink.B / 255f, 1f);
            if (job.Running != null)
            {
                // The lattice the mesh holds is drawn meanwhile: a cell or a size behind for a frame or two.
                if (!job.Running.IsCompleted) return;
                job.Running = null;
                Take(false);
            }
            if (placed && halfX == wantHalfX && halfZ == wantHalfZ && cellMetres == wantCell && cellX == cx && cellZ == cz && ink.R == inkR && ink.G == inkG && ink.B == inkB) return;
            job.Field = field;
            job.Cell = wantCell;
            job.Reach = reach;
            job.HalfX = wantHalfX;
            job.HalfZ = wantHalfZ;
            job.CellX = cx;
            job.CellZ = cz;
            job.Ink = ink;
            if (placed) job.Running = System.Threading.Tasks.Task.Run(job.Run);
            else
            {
                // Nothing to draw meanwhile (the first picture of a mission, or of another map): made here and now.
                job.Work();
                Take(true);
            }
        }

        /// <summary>The finished fill goes into the mesh: the frame's whole share of a fill, timed (with the fill itself when that was made on the frame too). A fill of another map's heights is dropped.</summary>
        private void Take(bool inline)
        {
            if (job.Error != null) throw new System.InvalidOperationException("The holo lattice fill failed.", job.Error);
            if (job.Field != field) return;
            long started = Stopwatch.GetTimestamp();
            if (job.HalfX != surfaceHalfX || job.HalfZ != surfaceHalfZ)
            {
                mesh.SetIndices(job.Triangles, 0, job.TriangleCount, MeshTopology.Triangles, SurfaceSub, false);
                surfaceHalfX = job.HalfX;
                surfaceHalfZ = job.HalfZ;
            }
            mesh.SetIndices(job.EastWest, 0, job.EastWestCount, MeshTopology.Lines, EastWestSub, false);
            mesh.SetIndices(job.NorthSouth, 0, job.NorthSouthCount, MeshTopology.Lines, NorthSouthSub, false);
            mesh.SetIndices(job.Water, 0, job.WaterCount, MeshTopology.Lines, WaterSub, false);
            mesh.SetIndices(job.Coast, 0, job.CoastCount, MeshTopology.Lines, CoastSub, false);
            mesh.SetVertexBufferData(job.Vertices, 0, 0, job.VertexCount, 0,
                MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices | MeshUpdateFlags.DontNotifyMeshUsers);
            halfX = job.HalfX;
            halfZ = job.HalfZ;
            nodes = HoloGrid.Nodes(halfX, halfZ);
            cellX = job.CellX;
            cellZ = job.CellZ;
            cellMetres = job.Cell;
            landEastWestCount = job.EastWestCount;
            landNorthSouthCount = job.NorthSouthCount;
            waterCount = job.WaterCount;
            coastCount = job.CoastCount;
            litVertices = job.Lit;
            inkR = job.Ink.R;
            inkG = job.Ink.G;
            inkB = job.Ink.B;
            placed = true;
            lastWorkMs = job.Ms;
            if (lastWorkMs > worstWorkMs) worstWorkMs = lastWorkMs;
            lastFillMs = Milliseconds(started) + (inline ? job.Ms : 0f);
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
            // The MINI's are a little stronger overall; the water lines are one strength for both families.
            float gain = HoloPresentation.LineGain(shape);
            // Seen from straight above (the 2D MINI) no line runs away from the viewer: both families at full strength.
            bool flat = camera.Tilt >= 89f;
            float across = (flat ? 1f : HoloGrid.LineStrength(90f, camera.Heading)) * gain, away = (flat ? 1f : HoloGrid.LineStrength(0f, camera.Heading)) * gain, wet = HoloGrid.WaterStrength * gain;
            eastWestBlock.SetColor(ColourId, new Color(across, across, across, 1f));
            northSouthBlock.SetColor(ColourId, new Color(away, away, away, 1f));
            waterBlock.SetColor(ColourId, new Color(wet, wet, wet, 1f));
            coastBlock.SetColor(ColourId, new Color(gain, gain, gain, 1f));
            commands.Clear();
            commands.SetRenderTarget(rt);
            commands.ClearRenderTarget(true, true, Color.black, HoloGrid.DepthClear(SystemInfo.usesReversedZBuffer));
            // After the clear, which takes the whole texture: the picture fills a part of it as big as the frame shows.
            commands.SetViewport(new Rect(0f, 0f, pixelsWide, pixelsHigh));
            commands.SetViewProjectionMatrices(viewMatrix, GL.GetGPUProjectionMatrix(projectionMatrix, true));
            if (depthPath) commands.DrawMesh(mesh, surfaceModel, surfaceMaterial, SurfaceSub, 0);
            if (landEastWestCount > 0) commands.DrawMesh(mesh, lineModel, lineMaterial, EastWestSub, 0, eastWestBlock);
            if (landNorthSouthCount > 0) commands.DrawMesh(mesh, lineModel, lineMaterial, NorthSouthSub, 0, northSouthBlock);
            if (waterCount > 0) commands.DrawMesh(mesh, lineModel, lineMaterial, WaterSub, 0, waterBlock);
            if (coastCount > 0) commands.DrawMesh(mesh, lineModel, lineMaterial, CoastSub, 0, coastBlock);
            commands.SetViewProjectionMatrices(Matrix4x4.identity, Matrix4x4.identity);
            commands.DrawMesh(fadeMesh, Matrix4x4.identity, fadeMaterial, 0, 0);
            commands.DrawMesh(fadeMesh, Matrix4x4.identity, opaqueMaterial, 1, 0);
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
            if (opaqueMaterial != null) Destroy(opaqueMaterial);
            opaqueMaterial = null;
            if (additive != null && additiveOwned) Destroy(additive);
            additive = null;
            additiveOwned = false;
            eastWestBlock = northSouthBlock = waterBlock = coastBlock = null;
            // A fill still being made finishes into arrays nobody reads any more.
            job = null;
            landEastWestCount = landNorthSouthCount = waterCount = coastCount = 0;
            placed = false;
            depthPath = false;
            halfX = halfZ = nodes = surfaceHalfX = surfaceHalfZ = 0;
            latticeRange = 0;
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
