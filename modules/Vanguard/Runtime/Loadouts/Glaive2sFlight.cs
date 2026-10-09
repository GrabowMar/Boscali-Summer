namespace BoscaliSummer.Modules.Vanguard.Runtime
{
    /// <summary>GLAIVE-2S SPECTRE: the flight hands the airframe to its GlaiveDrone (server only).</summary>
    internal sealed partial class VanguardFlight
    {
        private GlaiveDrone gunDrone;

        private void InitGunship(Unit requested, GlobalPosition aimpoint)
        {
            gunDrone = GlaiveDrone.Attach(missile);
            gunDrone?.Configure(requested, aimpoint);
        }

        private void TickGunship()
        {
            if (gunDrone == null) Detonate(); // no gun: nothing to fly for
            else gunDrone.TickHost();
        }
    }
}
