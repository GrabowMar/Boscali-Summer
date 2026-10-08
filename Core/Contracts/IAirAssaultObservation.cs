using System;

namespace BoscaliSummer.Core.Contracts
{
    // Server observations only; global coordinates survive floating-origin shifts.
    internal interface IAirAssaultObservation
    {
        bool Available { get; }
        event Action<int, float, float, int> Landed;
        bool TryRooftop(float x, float z, out int shellId, out float roofX, out float roofZ);
        bool IsRooftopAvailable(int shellId);
    }

    /// <summary>Pilot orders for the troops aboard the local helicopter (the C-key menu).</summary>
    internal interface IAirAssaultOrders
    {
        /// <summary>Local aircraft can take a squad off the rope: troop bench with room, slow hover in rope range.</summary>
        bool CanRequestExfil { get; }
        /// <summary>Asks the host to extract the nearest friendly squad position below; the host validates.</summary>
        void RequestExfil();
    }

    internal interface IOperationOutcomeSource
    {
        event Action<int, float> MoraleAwarded;

        /// <summary>Host-only mission-scoped record that this faction paid a secondary objective.</summary>
        bool HasCompletedContract(int factionInstanceId);
    }
}
