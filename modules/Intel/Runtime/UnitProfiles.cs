using System.Collections.Generic;
using BoscaliSummer.Modules.Intel.Domain;

namespace BoscaliSummer.Modules.Intel.Runtime
{
    /// <summary>What Intel needs to know about one unit type, read once from the first live instance it meets.</summary>
    internal readonly struct UnitProfile
    {
        public readonly AirDefenceProfile Air;
        public readonly ForceRole Role;
        public readonly bool HasWeapon;

        public UnitProfile(AirDefenceProfile air, ForceRole role, bool hasWeapon)
        {
            Air = air;
            Role = role;
            HasWeapon = hasWeapon;
        }
    }

    /// <summary>
    /// Profile cache per UnitDefinition, at most 256 types. Weapon stations live on unit
    /// instances, not on definitions, so a type is read from the first instance Intel meets.
    /// </summary>
    internal sealed class UnitProfiles
    {
        public const int Capacity = 256;
        private const int MaximumStations = 16;

        private readonly Dictionary<UnitDefinition, short> lookup = new Dictionary<UnitDefinition, short>(Capacity);
        private readonly UnitProfile[] profiles = new UnitProfile[Capacity];
        private readonly StationSample[] stations = new StationSample[MaximumStations];
        private int count;
        private bool fullLogged;

        public UnitProfile this[short index] => profiles[index];

        public static UnitClass ClassOf(Unit unit)
        {
            if (unit is Missile) return UnitClass.Missile;
            if (unit is Aircraft) return UnitClass.Aircraft;
            if (unit is GroundVehicle) return UnitClass.GroundVehicle;
            if (unit is Ship) return UnitClass.Ship;
            if (unit is Building) return UnitClass.Building;
            return UnitClass.Other;
        }

        /// <summary>The cache index of this unit's type; -1 when it has no definition or the cache is full.</summary>
        public short IndexOf(Unit unit)
        {
            UnitDefinition definition = unit != null ? unit.definition : null;
            if (definition == null) return -1;
            if (lookup.TryGetValue(definition, out short known)) return known;
            if (count >= Capacity)
            {
                if (!fullLogged)
                {
                    fullLogged = true;
                    Plugin.Logger?.LogWarning("Intel: more than " + Capacity + " unit types seen; later types read as unknown.");
                }
                return -1;
            }
            profiles[count] = Read(unit, definition);
            short index = (short)count++;
            lookup.Add(definition, index);
            return index;
        }

        public void Clear()
        {
            lookup.Clear();
            count = 0;
            fullLogged = false;
        }

        private UnitProfile Read(Unit unit, UnitDefinition definition)
        {
            int n = 0;
            bool weapon = false;
            List<WeaponStation> list = unit.weaponStations;
            if (list != null)
            {
                for (int i = 0; i < list.Count && n < MaximumStations; i++)
                {
                    WeaponStation station = list[i];
                    WeaponInfo info = station != null ? station.WeaponInfo : null;
                    if (info == null) continue;
                    weapon = true;
                    TargetRequirements requirements = info.targetRequirements;
                    stations[n++] = new StationSample(info.effectiveness.antiAir, requirements.maxRange,
                        requirements.minAltitude, requirements.maxAltitude, requirements.minIR, requirements.minRadar,
                        info.gun, info.jammer);
                }
            }
            // The carrier's own radar. An R9 launcher borrows its truck's Radar through
            // Unit.RpcAssignRadar, so the borrowed reference alone would name the launcher as
            // the emitter; the definition says whether the type itself is a radar.
            bool emitter = unit.radar is Radar && definition.typeIdentity.radar > 0f;
            AirDefenceProfile air = AirDefenceProfile.Build(stations, n, emitter);
            ForceRole role = ForceRoles.Classify(definition.code, ClassOf(unit) == UnitClass.Building,
                definition.roleIdentity.antiSurface, definition.roleIdentity.antiAir);
            return new UnitProfile(air, role, weapon);
        }
    }
}
