namespace BoscaliSummer.Core.Contracts
{
    /// <summary>Read-only claim on the map's Ctrl-drag selection gesture.</summary>
    internal interface IMapBoxInput
    {
        bool BlocksBoxSelection { get; }
    }
}
