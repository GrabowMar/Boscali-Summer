using UnityEngine;

namespace BoscaliSummer.Core.Contracts
{
    // Optional read-only view of verified native glass. The consumer owns the bounded output buffer.
    internal interface ICanopyGlassView
    {
        int Resolve(Transform cockpit, Vector3 eye, int layerMask, CanopyPane[] output);
    }

    internal readonly struct CanopyPane
    {
        internal readonly MeshRenderer Renderer;
        internal readonly Mesh Mesh;
        internal readonly int Submesh;
        internal readonly LODGroup Lod;
        internal CanopyPane(MeshRenderer renderer, Mesh mesh, int submesh, LODGroup lod)
        { Renderer = renderer; Mesh = mesh; Submesh = submesh; Lod = lod; }
    }
}
