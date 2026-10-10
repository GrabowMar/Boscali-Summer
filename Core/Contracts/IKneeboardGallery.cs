using UnityEngine;

namespace BoscaliSummer.Core.Contracts
{
    /// <summary>
    /// The cockpit kneeboard's card picker, for a cosmetics surface (SQD STUDIO) that must not import Immersion.
    /// Index 0 is the automatic mission card; the rest are picture cards. Client-local.
    /// </summary>
    internal interface IKneeboardGallery
    {
        int Count { get; }
        /// <summary>Wraps around; persists to the client config and refreshes the cockpit card.</summary>
        int Selected { get; set; }
        string Name(int index);
        /// <summary>The picture for a card, or null for the mission card.</summary>
        Texture2D Card(int index);
        Sprite Preview(int index);
    }
}
