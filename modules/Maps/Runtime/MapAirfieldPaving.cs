using System;
using System.Collections.Generic;
using System.Linq;
using BoscaliSummer.Modules.Maps.Domain;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace BoscaliSummer.Modules.Maps.Runtime
{
    /// <summary>The installed game's runway and lighting on a flat pad, with its off-runway apron removed.</summary>
    internal static class MapAirfieldPaving
    {
        private static bool RunwayMaterial(Material material) => material != null && material.name == "runway1";
        private static bool MarkingMaterial(Material material) => material != null &&
            (material.name == "runway_markings" || material.name == "runway_skidmarks");

        public static bool TryBorrow(Airbase airbase, Transform pad, MapEntry entry, MapManifest.Strip strip, float along, float across)
        {
            var parts = new List<(MeshRenderer Renderer, Mesh Mesh, bool Paint)>();
            try
            {
                foreach (MeshRenderer renderer in airbase.GetComponentsInChildren<MeshRenderer>(true))
                {
                    Material[] materials = renderer.sharedMaterials;
                    bool paint = materials.Any(MarkingMaterial);
                    if (!paint && !materials.Any(RunwayMaterial)) continue;
                    var filter = renderer.GetComponent<MeshFilter>();
                    if (filter == null || filter.sharedMesh == null) continue;
                    Mesh mesh = Clip(filter.sharedMesh, materials, pad.worldToLocalMatrix * renderer.localToWorldMatrix,
                        strip.width / across, strip.length / along, paint);
                    if (mesh != null) parts.Add((renderer, mesh, paint));
                }
            }
            catch (Exception error)
            {
                Plugin.Logger?.LogWarning($"[Maps] {strip.name}: could not read installed runway art: {error.Message}");
            }

            if (!parts.Any(p => !p.Paint) || !parts.Any(p => p.Paint))
            {
                foreach (var part in parts) Object.Destroy(part.Mesh);
                return false;
            }

            Transform paving = Surface(pad, "VanillaRunway", .135f);
            Transform decals = Surface(pad, "VanillaRunwayDecals", .175f);
            foreach (var part in parts)
            {
                var segment = new GameObject(part.Paint ? "Native markings" : "Native pavement");
                segment.transform.SetPositionAndRotation(part.Renderer.transform.position, part.Renderer.transform.rotation);
                segment.transform.localScale = part.Renderer.transform.lossyScale;
                segment.transform.SetParent(part.Paint ? decals : paving, true);
                segment.AddComponent<MeshFilter>().sharedMesh = part.Mesh;
                var renderer = segment.AddComponent<MeshRenderer>();
                renderer.sharedMaterials = part.Renderer.sharedMaterials;
                renderer.additionalVertexStreams = part.Renderer.additionalVertexStreams;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                var properties = new MaterialPropertyBlock();
                part.Renderer.GetPropertyBlock(properties);
                renderer.SetPropertyBlock(properties);
                entry.Owned.Add(part.Mesh);
            }
            paving.localScale = decals.localScale = new Vector3(across, .004f, along);
            return true;
        }

        public static void Lights(Airbase airbase, Transform pad, float along, float across)
        {
            Transform lights = Surface(pad, "VanillaRunwayLights", .25f);
            foreach (Renderer renderer in airbase.GetComponentsInChildren<Renderer>(true))
            {
                Material material = renderer.sharedMaterial;
                bool keep = renderer.transform != airbase.transform && (renderer.name == "lights" ||
                    (material != null && material.name == "MeshLight"));
                if (keep) renderer.transform.SetParent(lights, true);
                else renderer.enabled = false;
            }
            lights.localScale = new Vector3(across, .06f, along);
        }

        private static Transform Surface(Transform pad, string name, float height)
        {
            Transform plane = new GameObject(name).transform;
            plane.SetParent(pad, false);
            plane.localPosition = Vector3.up * height;
            return plane;
        }

        private static Mesh Clip(Mesh original, Material[] materials, Matrix4x4 toPad, float width, float length, bool paint)
        {
            Vector3[] points;
            int[][] indices;
            Mesh mesh;
            if (original.isReadable)
            {
                points = original.vertices;
                indices = Enumerable.Range(0, original.subMeshCount).Select(i => original.GetIndices(i)).ToArray();
                mesh = Object.Instantiate(original);
            }
            else
            {
                if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) return null;
                mesh = CopyVertexBuffers(original, out points, out indices);
                if (mesh == null) return null;
            }

            try
            {
                int kept = 0;
                mesh.subMeshCount = original.subMeshCount;
                for (int i = 0; i < indices.Length; i++)
                {
                    var runway = new List<int>();
                    if (i < materials.Length && (paint ? MarkingMaterial(materials[i]) : RunwayMaterial(materials[i])))
                    {
                        int[] triangles = indices[i];
                        for (int t = 0; t + 2 < triangles.Length; t += 3)
                        {
                            Vector3 center = toPad.MultiplyPoint3x4((points[triangles[t]] + points[triangles[t + 1]] + points[triangles[t + 2]]) / 3f);
                            if (Mathf.Abs(center.x) > width * .5f + 6f || Mathf.Abs(center.z) > length * .5f + 30f) continue;
                            runway.Add(triangles[t]);
                            runway.Add(triangles[t + 1]);
                            runway.Add(triangles[t + 2]);
                        }
                    }
                    kept += runway.Count;
                    mesh.SetIndices(runway, MeshTopology.Triangles, i, false);
                }
                if (kept == 0)
                {
                    Object.Destroy(mesh);
                    return null;
                }
                mesh.name = original.name + " Flat Runway";
                mesh.bounds = original.bounds;
                return mesh;
            }
            catch
            {
                Object.Destroy(mesh);
                throw;
            }
        }

        /// <summary>
        /// Runtime-renderable stock meshes may have no CPU copy. Read their installed graphics buffers once per
        /// runway while building the map. Preserve the original vertex layouts and restrict only triangle indices.
        /// </summary>
        private static Mesh CopyVertexBuffers(Mesh original, out Vector3[] points, out int[][] indices)
        {
            points = null;
            indices = null;
            if (original.GetVertexAttributeFormat(VertexAttribute.Position) != VertexAttributeFormat.Float32 ||
                original.GetVertexAttributeDimension(VertexAttribute.Position) != 3) return null;
            int positionStream = original.GetVertexAttributeStream(VertexAttribute.Position);
            int offset = original.GetVertexAttributeOffset(VertexAttribute.Position);
            var mesh = new Mesh { indexFormat = original.indexFormat };
            try
            {
                mesh.SetVertexBufferParams(original.vertexCount, original.GetVertexAttributes());
                for (int stream = 0; stream < original.vertexBufferCount; stream++)
                {
                    using GraphicsBuffer data = original.GetVertexBuffer(stream);
                    int stride = original.GetVertexBufferStride(stream);
                    var bytes = new byte[original.vertexCount * stride];
                    data.GetData(bytes);
                    mesh.SetVertexBufferData(bytes, 0, 0, bytes.Length, stream);
                    if (stream != positionStream) continue;
                    points = new Vector3[original.vertexCount];
                    for (int i = 0; i < points.Length; i++)
                    {
                        int at = i * stride + offset;
                        points[i] = new Vector3(BitConverter.ToSingle(bytes, at), BitConverter.ToSingle(bytes, at + 4),
                            BitConverter.ToSingle(bytes, at + 8));
                    }
                }

                using GraphicsBuffer indexBuffer = original.GetIndexBuffer();
                int[] allIndices;
                if (original.indexFormat == IndexFormat.UInt16)
                {
                    var shortIndices = new ushort[indexBuffer.count];
                    indexBuffer.GetData(shortIndices);
                    allIndices = Array.ConvertAll(shortIndices, i => (int)i);
                }
                else
                {
                    allIndices = new int[indexBuffer.count];
                    indexBuffer.GetData(allIndices);
                }
                indices = new int[original.subMeshCount][];
                for (int i = 0; i < indices.Length; i++)
                {
                    SubMeshDescriptor part = original.GetSubMesh(i);
                    indices[i] = new int[part.indexCount];
                    for (int j = 0; j < part.indexCount; j++) indices[i][j] = allIndices[part.indexStart + j] + part.baseVertex;
                }
                return mesh;
            }
            catch
            {
                Object.Destroy(mesh);
                throw;
            }
        }
    }
}
