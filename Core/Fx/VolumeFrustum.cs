using UnityEngine;

namespace BoscaliSummer.Core.Fx
{
    internal static class VolumeFrustum
    {
        /// <summary>World rays through the four corners (bottom-left, bottom-right, top-left, top-right), each of unit length along the camera axis.</summary>
        internal static Matrix4x4 Rays(Matrix4x4 inverseView, Matrix4x4 inverseProjection)
        {
            var m = new Matrix4x4();
            for (int i = 0; i < 4; i++)
            {
                Vector3 corner = inverseProjection.MultiplyPoint(new Vector3((i & 1) == 0 ? -1f : 1f,
                    i < 2 ? -1f : 1f, -1f));
                m.SetRow(i, inverseView.MultiplyVector(corner / -corner.z));
            }
            return m;
        }
    }
}
