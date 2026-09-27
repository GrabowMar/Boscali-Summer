namespace BoscaliSummer.Framework.Contracts
{
    /// <summary>Read-only projection into the native map image's local coordinates.</summary>
    internal interface IMapProjection
    {
        bool IsActive { get; }
        int Revision { get; }
        bool TryProject(float worldX, float worldZ, out float mapX, out float mapY);
    }
}
