namespace BoscaliSummer.Framework.Contracts
{
    internal interface IFireSuppressionService
    {
        int ActiveFireCount { get; }
        void DeploySmokeMarker(GlobalPosition position);
    }
}
