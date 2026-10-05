using BoscaliSummer.Core.Contracts;
using UnityEngine;

namespace BoscaliSummer.Modules.Weather.Visuals
{
    internal sealed class CanopyGlassView : ICanopyGlassView
    {
        public int Resolve(Transform cockpit, Vector3 eye, int layerMask, CanopyPane[] output)
        {
            if (output == null) return 0;
            var surfaces = CanopyGlassResolver.Resolve(cockpit, eye, layerMask);
            int count = Mathf.Min(output.Length, surfaces.Count);
            for (int i = 0; i < count; i++)
            {
                CanopySurface surface = surfaces[i];
                output[i] = new CanopyPane(surface.Renderer, surface.Mesh, surface.Submesh, surface.Lod);
            }
            for (int i = count; i < output.Length; i++) output[i] = default;
            return count;
        }
    }
}
