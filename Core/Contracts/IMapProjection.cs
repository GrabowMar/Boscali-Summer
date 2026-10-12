namespace BoscaliSummer.Core.Contracts
{
    /// <summary>Read-only projection into the native map image's local coordinates.</summary>
    internal interface IMapProjection
    {
        bool IsActive { get; }
        int Revision { get; }
        bool TryProject(float worldX, float worldZ, out float mapX, out float mapY);
        /// <summary>Map-image units that `metres` spans on the ground at (worldX, worldZ): the mean of the projected east and north runs. False when not projecting.</summary>
        bool TryScale(float worldX, float worldZ, float metres, out float mapUnits);
    }
}
