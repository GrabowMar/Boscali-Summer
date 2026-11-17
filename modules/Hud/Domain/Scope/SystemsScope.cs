using BoscaliSummer.Core.Contracts;
using NOAvionics;

namespace BoscaliSummer.Modules.Hud.Domain
{
    /// <summary>
    /// The SYSTEMS page's feed (Hud's own; read through <see cref="ScopeFeeds"/> like every page's): the airframe's damage zones, the fuel
    /// state with its BINGO / JOKER marks, the wind, the clock, the sky and whether the aircraft is in cloud (the countermeasure stations are the
    /// stores header's). Always live while there is an aircraft.
    /// </summary>
    internal struct SystemsScope
    {
        public DamageState Damage;
        public FuelState Fuel;
        /// <summary>The nose's compass heading, degrees: the wind dial turns with it.</summary>
        public float HeadingDeg;
        /// <summary>Wind the air is coming FROM (compass degrees) and its speed; <see cref="HasWind"/> false: the weather module has no reading.</summary>
        public bool HasWind;
        public float WindFromDeg, WindMps;
        /// <summary>The gust as an increment over the mean wind (m/s), turbulence 0..1 and the air temperature (C); only meaningful while <see cref="HasWind"/>.</summary>
        public float GustMps, Turb01, TempC;
        /// <summary>The local time of day in hours (NaN: no level), the sky cover 0..1 with its base (m) and the rain 0..1; the sky ones read while <see cref="HasCloud"/>.</summary>
        public float TodH, Cover, CloudBaseM, Rain01;
        /// <summary>The aircraft is between a cloud deck's base and top (<see cref="HasCloud"/> false: no weather sample).</summary>
        public bool HasCloud, InCloud;
        /// <summary>The mission clock in seconds, under the wind (2026-10-21, moved here from NAV DATA); negative or NaN: none.</summary>
        public float ClockS;
        public AvUnits Units;
        /// <summary>Load factor and the airframe's G limit (0 = unknown) for the G gauge.</summary>
        public float G, GLimit;
        /// <summary>Enemy HEAT on the pilot, 0..1 of the ace threshold (Squad's <see cref="ISquadView.Heat"/>); NaN: ace hunts off, no row. <see cref="AcesInbound"/>: an ace wing is hunting the pilot.</summary>
        public float Heat;
        public bool AcesInbound;
    }
}
