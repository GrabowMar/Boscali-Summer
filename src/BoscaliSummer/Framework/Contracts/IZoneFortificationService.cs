using NuclearOption.Networking;
using UnityEngine;

namespace BoscaliSummer.Framework.Contracts
{
    internal interface IZoneFortificationService
    {
        /// <summary>
        /// Reinforces the garrison of a zone the requester's faction controls by deploying
        /// an authentic vanilla infantry encampment.
        /// </summary>
        bool TryFortify(Airbase airbase, FactionHQ owner, Player requester, Vector3 targetPosition = default);

        /// <summary>
        /// Erects perimeter revetments and barriers to establish an engineered firebreak line.
        /// </summary>
        bool TryDeployFirebreak(Vector3 targetPosition, float radius);
    }
}