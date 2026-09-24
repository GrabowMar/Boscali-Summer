namespace BoscaliSummer.Fire
{
    /// <summary>
    /// Which side of the vanilla cloud deck a fire plume draws on. Unity-free so tests can
    /// compile it: <c>CloudDeckSorting</c> owns the material swap, this owns the rule.
    /// </summary>
    internal static class CloudDeckPolicy
    {
        /// <summary>
        /// Transparent renderers sort by render queue before distance. Vanilla draws the cloud
        /// layer plane at 2958 and its cloud puffs at 2996-2997, but smoke at 2998-3001 and
        /// fire at 2998-3002, so a plume under the deck is painted over it. One below the
        /// plane still draws after water (2502) and glass, lights and the sun (2950).
        /// </summary>
        internal const int BehindDeckQueue = 2957;

        /// <summary>The deck hides a plume only when it lies between the plume and the camera.</summary>
        internal static bool IsBehindDeck(float cameraY, float plumeY, float deckY)
        {
            return (cameraY > deckY) != (plumeY > deckY);
        }
    }
}
