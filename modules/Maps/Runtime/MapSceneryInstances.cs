using System;
using System.Collections.Generic;
using NuclearOption.Effects;
using NuclearOption.Networking;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace BoscaliSummer.Modules.Maps.Runtime
{
    /// <summary>Runs the game's tree frustum culler over persistent scenery positions and draws the visible instances.</summary>
    [DefaultExecutionOrder(9)]
    internal sealed class MapSceneryInstances : MonoBehaviour
    {
        private static readonly int PositionsId = Shader.PropertyToID("_SceneryPositions");
        private static readonly int PosesId = Shader.PropertyToID("_SceneryPoses");
        private static readonly int VisibleId = Shader.PropertyToID("_SceneryVisible");
        private static readonly int WorldOffsetId = Shader.PropertyToID("_SceneryWorldOffset");

        private sealed class Batch
        {
            public Mesh Mesh;
            public Material Material;
            public Material FallbackMaterial;
            public Vector3[] Positions;
            public Vector4[] Poses;
            public float Radius;
            public float Range;
            public Bounds Bounds;
        }

        private sealed class Buffers : IDisposable
        {
            public GraphicsBuffer Positions;
            public GraphicsBuffer Poses;
            public GraphicsBuffer Visible;
            public RenderParams Render;
            public bool InView;

            public void Dispose()
            {
                Positions?.Dispose();
                Poses?.Dispose();
                Visible?.Dispose();
            }
        }

        // Batches are configured on the live map, after Unity clones the inactive template.
        private readonly List<Batch> batches = new List<Batch>();
        private readonly Plane[] planes = new Plane[6];
        private readonly Vector4[] planeVectors = new Vector4[6];
        private GraphicsBuffer arguments;
        private uint[] argumentData;
        private CommandBuffer commands;
        private Buffers[] uploaded;
        private bool fallback;
        private bool fallbackPending;
        private readonly List<Mesh> fallbackMeshes = new List<Mesh>();

        internal static bool IsSupported => !GameManager.IsHeadless && DetailRenderer.IsSupported()
            && SystemInfo.supportsComputeShaders && SystemInfo.supportsInstancing
            && GameAssets.i != null && GameAssets.i.FrustumCullingCompute != null;

        internal int InstanceCount { get; private set; }
        internal long AllocatedBytes => uploaded == null ? 0L : InstanceCount * 32L
            + (long)arguments.count * DetailRenderer.RendererOffsets.BYTE_STRIDE;
        internal bool Ready { get; private set; }

        internal void Configure(Mesh mesh, Material material, Vector3[] positions, Vector4[] poses,
            float radius, float range, Bounds bounds, Material fallbackMaterial = null)
        {
            if (mesh == null || material == null || positions.Length == 0 || positions.Length != poses.Length)
                throw new ArgumentException("An instanced scenery batch needs a mesh, material and matching positions and poses.");
            batches.Add(new Batch
            {
                Mesh = mesh, Material = material, FallbackMaterial = fallbackMaterial,
                Positions = positions, Poses = poses, Radius = radius, Range = range, Bounds = bounds,
            });
            InstanceCount += positions.Length;
        }

        /// <summary>Reads the last draw counts on demand for a diagnostic snapshot.</summary>
        internal bool TryGetVisibleCount(out int count)
        {
            count = 0;
            if (!Ready) return false;
            arguments.GetData(argumentData);
            for (int i = 0; i < uploaded.Length; i++)
                if (uploaded[i].InView)
                    count += (int)argumentData[Offsets(i).DrawArgs().InstanceCount().Uint];
            return true;
        }

        private void LateUpdate()
        {
            if (InstanceCount == 0 || GameManager.IsHeadless || NetworkManagerNuclearOption.IsLoadingScene) return;
            if (fallback)
            {
                if (fallbackPending)
                {
                    fallbackPending = false;
                    CreateFallback();
                }
                return;
            }
            CameraStateManager cameraState = SceneSingleton<CameraStateManager>.i;
            Camera camera = cameraState != null ? cameraState.mainCamera : null;
            if (camera == null) camera = Camera.main;
            if (camera == null) return;

            try
            {
                if (uploaded == null) Upload();
                CullAndDraw(camera);
                Ready = true;
            }
            catch (Exception exception)
            {
                Release();
                fallback = true;
                // Previously submitted draws can still reach this frame. Start the merged path on the next one.
                fallbackPending = true;
                Debug.LogWarning("[Maps] GPU scenery fell back to merged meshes. " + exception.Message, this);
            }
        }

        private DetailRenderer.RendererOffsets Offsets(int index)
        {
            return new DetailRenderer.RendererOffsets((uint)(index * DetailRenderer.RendererOffsets.STRIDE_PER_RENDERER),
                argumentData, arguments);
        }

        private void Upload()
        {
            if (!IsSupported) throw new InvalidOperationException("The game's tree culler is unavailable.");
            int stride = DetailRenderer.RendererOffsets.BYTE_STRIDE;
            int records = batches.Count * DetailRenderer.RendererOffsets.STRIDE_PER_RENDERER;
            arguments = new GraphicsBuffer(GraphicsBuffer.Target.Structured | GraphicsBuffer.Target.IndirectArguments, records, stride)
            { name = "MapSceneryArguments" };
            argumentData = new uint[records * DetailRenderer.RendererOffsets.INT_STRIDE];
            commands = new CommandBuffer { name = "Map scenery culling" };
            uploaded = new Buffers[batches.Count];
            for (int i = 0; i < batches.Count; i++)
            {
                Batch batch = batches[i];
                if (batch.Material == null || !batch.Material.shader.isSupported)
                    throw new InvalidOperationException("An instanced scenery shader is unavailable.");
                int count = batch.Positions.Length;
                var gpu = new Buffers();
                uploaded[i] = gpu;
                gpu.Positions = new GraphicsBuffer(GraphicsBuffer.Target.Structured, count, 12) { name = "MapSceneryPositions" };
                gpu.Poses = new GraphicsBuffer(GraphicsBuffer.Target.Structured, count, 16) { name = "MapSceneryPoses" };
                gpu.Visible = new GraphicsBuffer(GraphicsBuffer.Target.Structured | GraphicsBuffer.Target.Append, count, 4)
                { name = "MapSceneryVisible" };
                gpu.Positions.SetData(batch.Positions);
                gpu.Poses.SetData(batch.Poses);

                var props = new MaterialPropertyBlock();
                props.SetBuffer(PositionsId, gpu.Positions);
                props.SetBuffer(PosesId, gpu.Poses);
                props.SetBuffer(VisibleId, gpu.Visible);
                gpu.Render = new RenderParams(batch.Material)
                {
                    matProps = props,
                    layer = gameObject.layer,
                    receiveShadows = true,
                    shadowCastingMode = ShadowCastingMode.On,
                };

                DetailRenderer.RendererOffsets offsets = Offsets(i);
                offsets.Set(offsets.GroupCount_1().Stride, new GroupCount
                {
                    groupX = ((uint)count + 63u) / 64u, groupY = 1u, groupZ = 1u, total = (uint)count,
                });
                offsets.Set(offsets.DrawArgs().Stride, new GraphicsBuffer.IndirectDrawIndexedArgs
                {
                    indexCountPerInstance = batch.Mesh.GetIndexCount(0),
                    startIndex = batch.Mesh.GetIndexStart(0),
                    baseVertexIndex = batch.Mesh.GetBaseVertex(0),
                });
            }
            arguments.SetData(argumentData);
        }

        private void CullAndDraw(Camera camera)
        {
            Vector3 origin = Datum.originPosition;
            Vector3 cameraPosition = camera.transform.position;
            GeometryUtility.CalculateFrustumPlanes(camera, planes);
            for (int i = 0; i < planes.Length; i++)
            {
                Vector3 normal = planes[i].normal;
                planeVectors[i] = new Vector4(normal.x, normal.y, normal.z, planes[i].distance);
            }

            // Use the same world space as the tree culler, including when the grass renderer has no active window.
            commands.Clear();
            commands.SetGlobalVectorArray(ShaderGlobalManager.ID_Global_FrustumPlanes, planeVectors);
            commands.SetGlobalVector(ShaderGlobalManager.ID_Global_CameraPosition, cameraPosition);
            for (int i = 0; i < batches.Count; i++)
            {
                Batch batch = batches[i];
                Buffers gpu = uploaded[i];
                Bounds bounds = batch.Bounds;
                bounds.center += origin;
                gpu.InView = bounds.SqrDistance(cameraPosition) <= batch.Range * batch.Range
                    && GeometryUtility.TestPlanesAABB(planes, bounds);
                if (!gpu.InView) continue;

                DetailRenderer.RendererOffsets offsets = Offsets(i);
                ComputeFrustumCulling.RunCull(commands,
                    ComputeFrustumCulling.RunFlags.UseGlobal | ComputeFrustumCulling.RunFlags.UseIndex,
                    offsets.GroupCount_1(), gpu.Positions, gpu.Visible, batch.Radius, batch.Range + batch.Radius);
                commands.CopyCounterValue(gpu.Visible, arguments, offsets.DrawArgs().InstanceCount().Byte);
                gpu.Render.worldBounds = bounds;
                gpu.Render.matProps.SetVector(WorldOffsetId, origin);
            }
            Graphics.ExecuteCommandBuffer(commands);
            for (int i = 0; i < batches.Count; i++)
            {
                Buffers gpu = uploaded[i];
                if (gpu.InView)
                    Graphics.RenderMeshIndirect(in gpu.Render, batches[i].Mesh, arguments, 1, (int)Offsets(i).DrawArgs().Stride);
            }
        }

        private void CreateFallback()
        {
            const float cellSize = 2560f;
            var cells = new Dictionary<(int x, int z, char range, Material material), List<CombineInstance>>();
            foreach (Batch batch in batches)
            {
                if (batch.Mesh == null || batch.FallbackMaterial == null) continue;
                char range = batch.Range <= 3500f ? 's' : batch.Range <= 12000f ? 'm' : 'l';
                for (int i = 0; i < batch.Positions.Length; i++)
                {
                    Vector3 position = batch.Positions[i];
                    int x = Mathf.FloorToInt(position.x / cellSize), z = Mathf.FloorToInt(position.z / cellSize);
                    Vector3 origin = new Vector3((x + .5f) * cellSize, 0f, (z + .5f) * cellSize);
                    Vector4 pose = batch.Poses[i];
                    Quaternion rotation = Quaternion.Euler(0f, Mathf.Atan2(pose.z, pose.y) * Mathf.Rad2Deg, 0f);
                    var key = (x, z, range, batch.FallbackMaterial);
                    if (!cells.TryGetValue(key, out List<CombineInstance> pieces)) cells.Add(key, pieces = new List<CombineInstance>());
                    pieces.Add(new CombineInstance
                    {
                        mesh = batch.Mesh,
                        transform = Matrix4x4.TRS(position - origin, rotation, Vector3.one * pose.x),
                    });
                }
            }

            var parent = new GameObject("Scenery fallback");
            parent.SetActive(false);
            parent.transform.SetParent(transform, false);
            try
            {
                foreach (var cell in cells)
                {
                    var (x, z, range, material) = cell.Key;
                    var child = new GameObject($"{range}_{x}_{z}");
                    child.transform.SetParent(parent.transform, false);
                    child.transform.localPosition = new Vector3((x + .5f) * cellSize, 0f, (z + .5f) * cellSize);
                    var mesh = new Mesh { name = "Merged scenery fallback", indexFormat = IndexFormat.UInt32 };
                    fallbackMeshes.Add(mesh);
                    mesh.CombineMeshes(cell.Value.ToArray(), true, true);
                    mesh.RecalculateBounds();
                    mesh.UploadMeshData(true);
                    child.AddComponent<MeshFilter>().sharedMesh = mesh;
                    child.AddComponent<MeshRenderer>().sharedMaterial = material;
                }
                parent.AddComponent<MapSceneryCuller>();
                parent.SetActive(true);
            }
            catch (Exception exception)
            {
                Object.Destroy(parent);
                DestroyFallbackMeshes();
                Debug.LogWarning("[Maps] Scenery fallback could not be drawn. " + exception.Message, this);
            }
        }

        private void OnDisable() => Release();

        private void Release()
        {
            Ready = false;
            commands?.Dispose();
            commands = null;
            arguments?.Dispose();
            arguments = null;
            argumentData = null;
            if (uploaded != null)
                foreach (Buffers gpu in uploaded) gpu?.Dispose();
            uploaded = null;
        }

        private void OnDestroy()
        {
            Release();
            DestroyFallbackMeshes();
        }

        private void DestroyFallbackMeshes()
        {
            foreach (Mesh mesh in fallbackMeshes)
                if (mesh != null) Object.Destroy(mesh);
            fallbackMeshes.Clear();
        }
    }
}
