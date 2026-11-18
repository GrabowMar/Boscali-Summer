using UnityEngine;

namespace BoscaliSummer.Core.Contracts
{
    internal interface ICloudVolumeView
    {
        /// <summary>Starts warming the shared RGBA volume. Returns null until the complete size³ texture is available.</summary>
        byte[] GetNoise(out int size);

        /// <summary>True if the cloud pass resolved its color and depth for this camera in the current frame.</summary>
        bool HasResolved(Camera camera);
    }
}
