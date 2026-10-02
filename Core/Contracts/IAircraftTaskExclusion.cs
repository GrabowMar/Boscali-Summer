namespace BoscaliSummer.Core.Contracts
{
    /// <summary>Read-only ownership check for aircraft another feature flies.</summary>
    internal interface IAircraftTaskExclusion
    {
        bool IsExcluded(int persistentIdHash);
    }
}
