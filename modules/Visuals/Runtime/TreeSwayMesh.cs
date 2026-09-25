using System;
using BoscaliSummer.Features.Visuals.Domain;
using HarmonyLib;
using NuclearOption.Effects;
using UnityEngine;
using UnityEngine.Rendering;

namespace BoscaliSummer.Features.Visuals.Runtime
{
    /// <summary>
    /// Makes one <see cref="TreeRenderer"/> sway without touching its shader. Every tree on the
    /// map is an instance of one small shared clump mesh, drawn by <c>Graphics.RenderMeshIndirect</c>
    /// from the renderer's private <c>mesh</c> field each frame. So: read that mesh back from the
    /// GPU once (it is not CPU-readable in the build), draw a dynamic copy in its place, and bend
    /// the copy's vertices a little every frame. Shadows use the same mesh, so they sway too.
    /// The indirect draw args (index count, start, base vertex) were taken from the original and
    /// stay valid because the copy has the same index buffer and sub-mesh layout.
    /// </summary>
    internal sealed class TreeSwayMesh
    {
        private const MeshUpdateFlags QuietUpdate =
            MeshUpdateFlags.DontValidateIndices | MeshUpdateFlags.DontRecalculateBounds |
            MeshUpdateFlags.DontNotifyMeshUsers | MeshUpdateFlags.DontResetBoneBounds;

        /// <summary>Tree clumps are a few hundred vertices; refuse anything that would make the
        /// per-frame CPU pass expensive.</summary>
        private const int MaxVertices = 20000;

        private static readonly AccessTools.FieldRef<TreeRenderer, Mesh> MeshRef =
            AccessTools.FieldRefAccess<TreeRenderer, Mesh>("mesh");

        private readonly TreeRenderer renderer;
        private readonly Mesh original;
        private Mesh copy;
        private float[] rest;
        private float[] live;
        private int strideFloats;
        private int positionFloat;
        private int vertexCount;
        private float height;

        public TreeRenderer Renderer => renderer;
        public bool Active => copy != null && renderer != null && MeshRef(renderer) == copy;
        public int VertexCount => vertexCount;
        public int Frames { get; private set; }

        private TreeSwayMesh(TreeRenderer renderer, Mesh original)
        {
            this.renderer = renderer;
            this.original = original;
        }

        /// <summary>Builds the dynamic copy and swaps it in; null (with a reason) when the mesh
        /// cannot be read back or has a layout this does not understand.</summary>
        public static TreeSwayMesh TryCreate(TreeRenderer renderer, out string failure)
        {
            failure = null;
            Mesh source = renderer != null ? MeshRef(renderer) : null;
            if (source == null)
            {
                failure = "tree renderer has no mesh yet";
                return null;
            }

            var sway = new TreeSwayMesh(renderer, source);
            try
            {
                if (!sway.Build(out failure)) return null;
            }
            catch (Exception e)
            {
                sway.Dispose();
                failure = e.GetType().Name + ": " + e.Message;
                return null;
            }
            MeshRef(renderer) = sway.copy;
            return sway;
        }

        private bool Build(out string failure)
        {
            failure = null;
            vertexCount = original.vertexCount;
            if (vertexCount <= 0 || vertexCount > MaxVertices)
            {
                failure = $"unexpected vertex count {vertexCount}";
                return false;
            }
            if (original.vertexBufferCount != 1)
            {
                failure = $"expected one vertex stream, found {original.vertexBufferCount}";
                return false;
            }

            VertexAttributeDescriptor[] layout = original.GetVertexAttributes();
            int stride = original.GetVertexBufferStride(0);
            if (stride <= 0 || stride % 4 != 0)
            {
                failure = $"vertex stride {stride} is not float-aligned";
                return false;
            }
            positionFloat = -1;
            int offset = 0;
            foreach (VertexAttributeDescriptor attribute in layout)
            {
                if (attribute.attribute == VertexAttribute.Position)
                {
                    if (attribute.format != VertexAttributeFormat.Float32 || attribute.dimension < 3)
                    {
                        failure = $"position is {attribute.format}x{attribute.dimension}, not Float32x3";
                        return false;
                    }
                    positionFloat = offset / 4;
                }
                offset += ByteSize(attribute.format) * attribute.dimension;
            }
            if (positionFloat < 0)
            {
                failure = "mesh has no position attribute";
                return false;
            }
            strideFloats = stride / 4;

            // The build's meshes are GPU-only, so read the buffers back once.
            rest = new float[vertexCount * strideFloats];
            using (GraphicsBuffer vertices = original.GetVertexBuffer(0))
                vertices.GetData(rest);

            int indexCount;
            int[] indices32 = null;
            ushort[] indices16 = null;
            using (GraphicsBuffer indexBuffer = original.GetIndexBuffer())
            {
                indexCount = indexBuffer.count;
                if (original.indexFormat == IndexFormat.UInt16)
                {
                    indices16 = new ushort[indexCount];
                    indexBuffer.GetData(indices16);
                }
                else
                {
                    indices32 = new int[indexCount];
                    indexBuffer.GetData(indices32);
                }
            }

            height = 0f;
            bool anyNonZero = false;
            for (int v = 0; v < vertexCount; v++)
            {
                int p = v * strideFloats + positionFloat;
                float y = rest[p + 1];
                if (y > height) height = y;
                if (rest[p] != 0f || y != 0f || rest[p + 2] != 0f) anyNonZero = true;
            }
            if (!anyNonZero || height <= 0.5f)
            {
                failure = "GPU readback returned empty positions";
                return false;
            }

            copy = new Mesh { name = original.name + " (Boscali sway)" };
            copy.SetVertexBufferParams(vertexCount, layout);
            copy.SetVertexBufferData(rest, 0, 0, rest.Length, 0, QuietUpdate);
            copy.SetIndexBufferParams(indexCount, original.indexFormat);
            if (indices16 != null) copy.SetIndexBufferData(indices16, 0, 0, indexCount, QuietUpdate);
            else copy.SetIndexBufferData(indices32, 0, 0, indexCount, QuietUpdate);
            copy.subMeshCount = original.subMeshCount;
            for (int i = 0; i < original.subMeshCount; i++)
                copy.SetSubMesh(i, original.GetSubMesh(i), QuietUpdate);
            // Room for the sway so no culling path ever trims a bent crown.
            Bounds bounds = original.bounds;
            bounds.Expand(4f);
            copy.bounds = bounds;
            copy.MarkDynamic();

            live = (float[])rest.Clone();
            return true;
        }

        /// <summary>Bends the copy for this frame. <paramref name="amplitude"/> is canopy-top metres.</summary>
        public void Update(float time, float windX, float windZ, float amplitude)
        {
            if (!Active) return;
            for (int v = 0; v < vertexCount; v++)
            {
                int p = v * strideFloats + positionFloat;
                float x = rest[p], y = rest[p + 1], z = rest[p + 2];
                (float dx, float dy, float dz) = VisualsMath.TreeSway(time, x, y, z, height, windX, windZ, amplitude);
                live[p] = x + dx;
                live[p + 1] = y + dy;
                live[p + 2] = z + dz;
            }
            copy.SetVertexBufferData(live, 0, 0, live.Length, 0, QuietUpdate);
            Frames++;
        }

        /// <summary>Hands the original mesh back and destroys the copy.</summary>
        public void Dispose()
        {
            if (renderer != null && copy != null && MeshRef(renderer) == copy)
                MeshRef(renderer) = original;
            if (copy != null) UnityEngine.Object.Destroy(copy);
            copy = null;
        }

        private static int ByteSize(VertexAttributeFormat format)
        {
            switch (format)
            {
                case VertexAttributeFormat.Float32:
                case VertexAttributeFormat.UInt32:
                case VertexAttributeFormat.SInt32:
                    return 4;
                case VertexAttributeFormat.Float16:
                case VertexAttributeFormat.UNorm16:
                case VertexAttributeFormat.SNorm16:
                case VertexAttributeFormat.UInt16:
                case VertexAttributeFormat.SInt16:
                    return 2;
                default:
                    return 1;
            }
        }
    }
}
