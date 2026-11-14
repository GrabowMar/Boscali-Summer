namespace BoscaliSummer.Core.Contracts
{
    /// <summary>MY AIRCRAFT: the aircraft the local player flies (status card, damage, engine map, flight data, systems, stores, the
    /// worst parts), built by Progression into another console's page (WMC LOADOUT). Optional: without it the host shows one dim line.</summary>
    internal interface IMyAircraft
    {
        /// <summary>Adds the section to <paramref name="flow"/>, once at build; it refreshes on the flow's ticker while
        /// <paramref name="page"/> shows.</summary>
        void Build(int page, NOAvionics.AvFlow flow);
    }
}
