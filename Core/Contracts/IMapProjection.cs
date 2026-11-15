namespace BoscaliSummer.Core.Contracts
{
    /// <summary>Projection into the native map image's local coordinates, and the one way another module moves the open map.</summary>
    internal interface IMapProjection
    {
        bool IsActive { get; }
        int Revision { get; }
        bool TryProject(float worldX, float worldZ, out float mapX, out float mapY);

        /// <summary>LOCATE: flies the open map to a ground point and zooms in; false while the map is closed or the point is not finite.</summary>
        bool Focus(float worldX, float worldZ);
    }
}
