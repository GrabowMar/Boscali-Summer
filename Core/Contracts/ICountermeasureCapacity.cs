namespace BoscaliSummer.Core.Contracts
{
    /// <summary>Full load of a custom countermeasure, for the native and TAC BOARD readouts.</summary>
    internal interface ICountermeasureCapacity
    {
        int MaxAmmo { get; }
    }
}
