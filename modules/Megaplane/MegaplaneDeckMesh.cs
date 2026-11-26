using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;

namespace BoscaliSummer.Modules.Megaplane
{
    /// <summary>Share material draws among fixed cockpit fittings in the same local frame.</summary>
    internal static class MegaplaneDeckMesh
    {
        internal static void Batch(Dictionary<string, Transform> nodes, List<Renderer> near)
        {
            if (GameManager.IsHeadless || !nodes.TryGetValue("Fuselage_Cockpit", out Transform cockpit)) return;
            Combine("GrozaDeckBinnacle", cockpit, nodes, near,
                "FlightDeck_Glareshield", "FlightDeck_InstrumentBridge");
            Combine("GrozaDeckConsole", cockpit, nodes, near,
                "FlightDeck_Panel", "FlightDeck_Pedestal");
            Combine("GrozaDeckWatchStations", cockpit, nodes, near,
                "FlightDeck_AftPressureDoor", "FlightDeck_CommandPost", "FlightDeck_WatchConsoles", "FlightDeck_WatchSeats");
        }

        private static void Combine(string name, Transform cockpit, Dictionary<string, Transform> nodes,
            List<Renderer> near, params string[] names)
        {
            var sources = new List<(MeshFilter Filter, MeshRenderer Renderer, Material[] Materials)>();
            int vertexCount = 0;
            foreach (string nodeName in names)
            {
                if (!nodes.TryGetValue(nodeName, out Transform node)) continue;
                // Installed far renderers and attached fittings are children of their source node.
                if (node.parent != cockpit || node.childCount != 0 ||
                    !node.gameObject.activeSelf || !node.TryGetComponent(out MeshFilter filter) || filter.sharedMesh == null ||
                    !node.TryGetComponent(out MeshRenderer renderer) || !near.Contains(renderer) ||
                    node.GetComponents<Component>().Length != 3 || !CanBatch(renderer)) return;

                if (sources.Count > 0 && !Matches(renderer, sources[0].Renderer)) return;
                Material[] materials = renderer.sharedMaterials;
                Mesh mesh = filter.sharedMesh;
                if (materials.Length != mesh.subMeshCount || !CanCopy(mesh) ||
                    materials.Any(m => m == null || m.renderQueue > (int)RenderQueue.GeometryLast)) return;
                vertexCount += mesh.vertexCount;
                if (vertexCount > ushort.MaxValue) return;
                sources.Add((filter, renderer, materials));
            }

            if (sources.Count < 2) return;
            Material[] palette = sources.SelectMany(s => s.Materials).Distinct().ToArray();
            if (palette.Length == sources.Sum(s => s.Materials.Length)) return;

            var points = new Vector3[vertexCount];
            var normals = new Vector3[vertexCount];
            var tangents = new Vector4[vertexCount];
            var uvs = new Vector2[vertexCount];
            var triangles = palette.ToDictionary(m => m, _ => new List<int>());
            int offset = 0;
            foreach (var source in sources)
            {
                Mesh mesh = source.Filter.sharedMesh;
                mesh.vertices.CopyTo(points, offset);
                mesh.normals.CopyTo(normals, offset);
                mesh.tangents.CopyTo(tangents, offset);
                mesh.uv.CopyTo(uvs, offset);
                for (int slot = 0; slot < source.Materials.Length; slot++)
                {
                    List<int> destination = triangles[source.Materials[slot]];
                    foreach (int index in mesh.GetTriangles(slot)) destination.Add(offset + index);
                }
                offset += mesh.vertexCount;
            }

            var combined = new Mesh { name = name, vertices = points, normals = normals, tangents = tangents, uv = uvs,
                subMeshCount = palette.Length };
            for (int slot = 0; slot < palette.Length; slot++) combined.SetTriangles(triangles[palette[slot]], slot, false);
            combined.RecalculateBounds();

            GameObject batch = Object.Instantiate(sources[0].Renderer.gameObject, cockpit, false);
            batch.name = name;
            batch.GetComponent<MeshFilter>().sharedMesh = combined;
            var batchRenderer = batch.GetComponent<MeshRenderer>();
            batchRenderer.sharedMaterials = palette;
            near.Add(batchRenderer);

            foreach (var source in sources)
            {
                Mesh mesh = source.Filter.sharedMesh;
                near.Remove(source.Renderer);
                // Keep the authored node and discard its dedicated render data after copying it.
                Object.DestroyImmediate(source.Renderer);
                Object.DestroyImmediate(source.Filter);
                if (!nodes.Values.Any(t => t.TryGetComponent(out MeshFilter other) && other.sharedMesh == mesh))
                    Object.DestroyImmediate(mesh);
            }
        }

        private static bool CanCopy(Mesh mesh) => mesh.isReadable && mesh.vertexAttributeCount == 4 &&
            mesh.HasVertexAttribute(VertexAttribute.Position) && mesh.HasVertexAttribute(VertexAttribute.Normal) &&
            mesh.HasVertexAttribute(VertexAttribute.Tangent) && mesh.HasVertexAttribute(VertexAttribute.TexCoord0);

        private static bool CanBatch(MeshRenderer renderer) => renderer.enabled && !renderer.forceRenderingOff &&
            !renderer.HasPropertyBlock() && renderer.additionalVertexStreams == null && !renderer.isPartOfStaticBatch &&
            !HasLightmap(renderer.lightmapIndex) && !HasLightmap(renderer.realtimeLightmapIndex);

        private static bool HasLightmap(int index) => index >= 0 && index < 65534;

        private static bool Matches(MeshRenderer source, MeshRenderer reference)
        {
            Transform a = source.transform;
            Transform b = reference.transform;
            return a.localPosition.Equals(b.localPosition) && a.localRotation.Equals(b.localRotation) &&
                a.localScale.Equals(b.localScale) && a.gameObject.layer == b.gameObject.layer &&
                source.shadowCastingMode == reference.shadowCastingMode && source.receiveShadows == reference.receiveShadows &&
                source.allowOcclusionWhenDynamic == reference.allowOcclusionWhenDynamic &&
                source.lightProbeUsage == reference.lightProbeUsage && source.reflectionProbeUsage == reference.reflectionProbeUsage &&
                source.probeAnchor == reference.probeAnchor && source.lightProbeProxyVolumeOverride == reference.lightProbeProxyVolumeOverride &&
                source.motionVectorGenerationMode == reference.motionVectorGenerationMode && source.renderingLayerMask == reference.renderingLayerMask &&
                source.sortingLayerID == reference.sortingLayerID && source.sortingOrder == reference.sortingOrder;
        }
    }
}
