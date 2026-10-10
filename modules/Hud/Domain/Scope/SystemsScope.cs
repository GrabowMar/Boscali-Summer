using BoscaliSummer.Core.Contracts;
using NOAvionics;

namespace BoscaliSummer.Modules.Hud.Domain
{
    /// <summary>
    /// The SYSTEMS page's feed (Hud's own; read through <see cref="ScopeFeeds"/> like every page's): the airframe's damage zones, the fuel
    /// state with its BINGO / JOKER marks and the way home, the wind and whether the aircraft is in cloud (the countermeasure stations are the
    /// stores header's). Always live while there is an aircraft.
    /// </summary>
    internal struct SystemsScope
    {
        /// <summary>The airframe is a helicopter (rotor disc and tail boom silhouette) rather than a fixed wing.</summary>
        public bool Rotary;
        public DamageState Damage;
        public FuelState Fuel;
        /// <summary>The nose's compass heading, degrees: the wind dial turns with it.</summary>
        public float HeadingDeg;
        /// <summary>Wind the air is coming FROM (compass degrees) and its speed; <see cref="HasWind"/> false: the weather module has no reading.</summary>
        public bool HasWind;
        public float WindFromDeg, WindMps;
        /// <summary>The aircraft is between a cloud deck's base and top (<see cref="HasCloud"/> false: no weather sample).</summary>
        public bool HasCloud, InCloud;
        public AvUnits Units;
        /// <summary>Load factor and the airframe's G limit (0 = unknown) for the G gauge.</summary>
        public float G, GLimit;
    }
}
