using UnityEngine;

namespace BoscaliSummer.Core.Contracts
{
    /// <summary>Optional local views that supply their own atmosphere. Mission weather keeps simulating.</summary>
    internal interface IWeatherCameraExclusion
    {
        bool Contains(Camera camera);
    }
}
