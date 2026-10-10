using UnityEngine;

namespace BoscaliSummer.Core.Contracts
{
    /// <summary>The player's own squadron insignia as a card sprite, or null when none is available. Client-local.</summary>
    internal interface ISquadronInsignia
    {
        Sprite Current { get; }
    }
}
