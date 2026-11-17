using System;
using System.Threading;

namespace BoscaliSummer.Core.Math
{
    /// <summary>Global-metre cloud displacement, shared by the GPU march and its immutable CPU shadow/view snapshot.</summary>
    internal readonly struct CloudClearanceSphere
    {
        public readonly float X, Y, Z, Radius, Strength, Edge;
        public CloudClearanceSphere(float x, float y, float z, float radius, float strength, float edge)
        { X = x; Y = y; Z = z; Radius = radius; Strength = strength; Edge = edge; }

        public float Keep(float x, float y, float z)
        {
            if (Radius <= 0f || Strength <= 0f) return 1f;
            float dx = x - X, dy = y - Y, dz = z - Z;
            float squared = dx * dx + dy * dy + dz * dz;
            float outer = Radius + Edge, inner = System.Math.Max(0f, Radius - Edge);
            if (squared >= outer * outer) return 1f;
            float boundary = squared <= inner * inner ? 0f : Scalar.SmoothStep(inner, outer, (float)System.Math.Sqrt(squared));
            return 1f - Scalar.Clamp01(Strength) * (1f - boundary);
        }
    }

    /// <summary>The effects owner replaces the array; cloud workers retain that frame's read-only value.</summary>
    internal static class CloudClearance
    {
        private static CloudClearanceSphere[] current = Array.Empty<CloudClearanceSphere>();
        private static int revision;
        public static CloudClearanceSphere[] Current => Volatile.Read(ref current);
        public static int Revision => Volatile.Read(ref revision);
        public static void Publish(CloudClearanceSphere[] spheres, int stamp)
        {
            Volatile.Write(ref current, spheres ?? Array.Empty<CloudClearanceSphere>());
            Volatile.Write(ref revision, stamp);
        }
        public static float Keep(CloudClearanceSphere[] spheres, float x, float y, float z)
        {
            float keep = 1f;
            if (spheres == null) return keep;
            foreach (CloudClearanceSphere sphere in spheres) keep *= sphere.Keep(x, y, z);
            return keep;
        }
    }
}
