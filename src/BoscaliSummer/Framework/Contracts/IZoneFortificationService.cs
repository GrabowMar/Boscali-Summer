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
        /// Occupies and fortifies a specific civilian building with rooftop AA, ground bunkers, and markings.
        /// </summary>
        bool TryOccupyBuilding(GameObject shell, FactionHQ owner, Airbase airbase);

        /// <summary>
        /// Deploys an authentic infantry combat encampment on open ground.
        /// </summary>
        bool TryDeployEncampment(Vector3 groundPos, FactionHQ owner, Airbase airbase);
    }
}