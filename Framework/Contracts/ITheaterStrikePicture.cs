namespace BoscaliSummer.Framework.Contracts
{
    /// <summary>Read-only, locally observed support fire near a known objective.</summary>
    internal interface ITheaterStrikePicture
    {
        /// <summary>Find an active strike within the objective's vicinity; no call is placed.</summary>
        bool TryGetNear(float x, float z, float vicinity, out string label, out float secondsToImpact);
    }
}
