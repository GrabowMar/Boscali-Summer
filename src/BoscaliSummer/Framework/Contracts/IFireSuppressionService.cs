namespace BoscaliSummer.Framework.Contracts
{
    internal interface IFireSuppressionService
    {
        int ActiveFireCount { get; }
        int ExtinguishInRadius(GlobalPosition position, float radius);
        int ClearForestInRadius(GlobalPosition position, float radius);
        void DeploySmokeMarker(GlobalPosition position);
    }
}
