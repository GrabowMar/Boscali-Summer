namespace BoscaliSummer.Framework.Contracts
{
    /// <summary>
    /// Read-only forest cover from the fire module's tree index. Trenches sites its
    /// positions at forest edges through this; a missing or unready index simply means
    /// no foliage bias. Coordinates are global metres.
    /// </summary>
    internal interface IFoliageCover
    {
        bool Ready { get; }
        bool Contains(float x, float z);
    }
}
