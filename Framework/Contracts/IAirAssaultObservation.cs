using System;

namespace BoscaliSummer.Framework.Contracts
{
    // Server observations only; global coordinates survive floating-origin shifts.
    internal interface IAirAssaultObservation
    {
        bool Available { get; }
        event Action<int, float, float, int> Landed;
        bool TryRooftop(float x, float z, out int shellId, out float roofX, out float roofZ);
        bool IsRooftopAvailable(int shellId);
    }

    internal interface IOperationOutcomeSource
    {
        event Action<int, float> MoraleAwarded;

        /// <summary>Host-only mission-scoped record that this faction paid a secondary objective.</summary>
        bool HasCompletedContract(int factionInstanceId);
    }
}
