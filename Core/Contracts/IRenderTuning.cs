using UnityEngine;

namespace BoscaliSummer.Core.Contracts
{
    /// <summary>Optional client render controls and relief-camera ownership shared with display consumers.</summary>
    internal interface IRenderTuning
    {
        bool IndependentSharpening { get; }
        bool OptimizeCinematicStreaks { get; }
        float SharpenStrength { get; }
        void RegisterReliefCamera(Camera camera);
        void UnregisterReliefCamera(Camera camera);
    }
}
