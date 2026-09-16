using System;

namespace BoscaliSummer.Features.Support.Domain.Orbital
{
    /// <summary>Wire-stable module bytes. <see cref="Cargo"/> is a resupply vehicle, never a cell.</summary>
    internal enum ModuleKind : byte
    {
        None = 0,
        Core = 1,
        Solar = 2,
        Battery = 3,
        Reactor = 4,
        Radiator = 5,
        Relay = 6,
        Gyro = 7,
        Shield = 8,
        Propulsion = 9,
        Habitat = 10,
        Imager = 11,
        Sigint = 12,
        Rods = 13,
        Emp = 14,
        Cargo = 15
    }

    internal enum ModuleCategory : byte
    {
        Core = 0,
        Power = 1,
        Utility = 2,
        Sensor = 3,
        Weapon = 4,
        Mobility = 5
    }

    internal readonly struct ModuleInfo
    {
        public readonly ModuleKind Kind;
        public readonly string Code;
        public readonly string Name;
        public readonly ModuleCategory Category;
        public readonly string Summary;
        public readonly float Mass;
        public readonly float Price;

        /// <summary>Solar output while sunlit, kW.</summary>
        public readonly float SolarKw;

        /// <summary>Output day and night, kW.</summary>
        public readonly float SteadyKw;

        /// <summary>Continuous draw, kW.</summary>
        public readonly float LoadKw;

        public readonly float StorageKj;
        public readonly float Fuel;
        public readonly byte Rods;

        /// <summary>Runs degraded without a radiator next to it.</summary>
        public readonly bool Hot;

        public readonly byte MaxCopies;

        public ModuleInfo(ModuleKind kind, string code, string name, ModuleCategory category, string summary,
                          float mass, float price, float solarKw, float steadyKw, float loadKw, float storageKj,
                          float fuel, byte rods, bool hot, byte maxCopies)
        {
            Kind = kind;
            Code = code;
            Name = name;
            Category = category;
            Summary = summary;
            Mass = mass;
            Price = price;
            SolarKw = solarKw;
            SteadyKw = steadyKw;
            LoadKw = loadKw;
            StorageKj = storageKj;
            Fuel = fuel;
            Rods = rods;
            Hot = hot;
            MaxCopies = maxCopies;
        }
    }

    internal readonly struct LaunchVehicle
    {
        public readonly string Code;
        public readonly float Capacity;
        public readonly float Price;

        public LaunchVehicle(string code, float capacity, float price)
        {
            Code = code;
            Capacity = capacity;
            Price = price;
        }
    }

    /// <summary>
    /// The module designs. The station carries far fewer than this — a 40 t structure and a
    /// 15-cell truss make every design a choice. Power is in kW, storage in kJ (kW × s).
    /// </summary>
    internal static class PlatformModules
    {
        public static readonly ModuleInfo[] Designs =
        {
            new ModuleInfo(ModuleKind.Core, "COR", "CORE MODULE", ModuleCategory.Core,
                "Command, docking hub and body-mounted cells. Everything docks to it.",
                12f, 800f, 4f, 0f, 0f, 600f, 0f, 0, false, 1),
            new ModuleInfo(ModuleKind.Solar, "SOL", "SOLAR ARRAY", ModuleCategory.Power,
                "Deployable wings. +12 kW while sunlit, nothing in eclipse.",
                1.5f, 150f, 12f, 0f, 0f, 0f, 0f, 0, false, 4),
            new ModuleInfo(ModuleKind.Battery, "BAT", "BATTERY BANK", ModuleCategory.Power,
                "Lithium-ion string. +1,500 kJ storage for night passes and big bursts.",
                2.5f, 200f, 0f, 0f, 0f, 1500f, 0f, 0, false, 3),
            new ModuleInfo(ModuleKind.Reactor, "RTG", "REACTOR", ModuleCategory.Power,
                "Compact fission unit. +10 kW day and night; runs at half output uncooled.",
                6f, 650f, 0f, 10f, 0f, 0f, 0f, 0, true, 1),
            new ModuleInfo(ModuleKind.Radiator, "RAD", "RADIATOR", ModuleCategory.Utility,
                "Cools every neighbouring module. Hot modules need one next to them.",
                1.2f, 120f, 0f, 0f, 0f, 0f, 0f, 0, false, 3),
            new ModuleInfo(ModuleKind.Relay, "REL", "DATA RELAY", ModuleCategory.Utility,
                "Wideband downlink. Neighbouring sensors scan 35 % wider.",
                1.5f, 250f, 0f, 0f, 1f, 0f, 0f, 0, false, 2),
            new ModuleInfo(ModuleKind.Gyro, "CMG", "GYRO CLUSTER", ModuleCategory.Utility,
                "Control moment gyros. Faster uplink slew, rod scatter halved.",
                2f, 300f, 0f, 0f, 0.5f, 0f, 0f, 0, false, 1),
            new ModuleInfo(ModuleKind.Shield, "SHD", "WHIPPLE SHIELD", ModuleCategory.Utility,
                "Debris bumper. Protects itself and its neighbours from strikes.",
                2f, 180f, 0f, 0f, 0f, 0f, 0f, 0, false, 3),
            new ModuleInfo(ModuleKind.Propulsion, "PRP", "PROPULSION", ModuleCategory.Mobility,
                "Tanks and thrusters. +100 fuel for rephase burns and orbit changes; LOW needs it.",
                4f, 400f, 0f, 0f, 0f, 0f, 100f, 0, false, 2),
            new ModuleInfo(ModuleKind.Habitat, "HAB", "HABITAT", ModuleCategory.Utility,
                "Crew of three on console. Every ability recharges 25 % faster.",
                6f, 550f, 0f, 0f, 1.5f, 0f, 0f, 0, false, 1),
            new ModuleInfo(ModuleKind.Imager, "IMG", "SPY IMAGER", ModuleCategory.Sensor,
                "EO/IR telescope and X-band radar. UPLINK video and RADAR SCAN.",
                3.5f, 600f, 0f, 0f, 2f, 0f, 0f, 0, false, 2),
            new ModuleInfo(ModuleKind.Sigint, "SIG", "SIGINT ARRAY", ModuleCategory.Sensor,
                "Passive antenna farm. ELINT SWEEP finds radars that are emitting.",
                2.5f, 450f, 0f, 0f, 1.5f, 0f, 0f, 0, false, 1),
            new ModuleInfo(ModuleKind.Rods, "ROD", "ROD MAGAZINE", ModuleCategory.Weapon,
                "Three tungsten penetrators. ROD STRIKE. Heavy; resupply to reload.",
                5.5f, 700f, 0f, 0f, 0f, 0f, 0f, 3, false, 2),
            new ModuleInfo(ModuleKind.Emp, "EMP", "EMP EMITTER", ModuleCategory.Weapon,
                "Burst package and capacitor bank. EMP BURST needs 900 kJ; hot.",
                4.5f, 900f, 0f, 0f, 1f, 0f, 0f, 0, true, 1)
        };

        /// <summary>The resupply vehicle: refuels and rearms, docks at the core.</summary>
        public static readonly ModuleInfo Cargo = new ModuleInfo(ModuleKind.Cargo, "CGO", "CARGO RESUPPLY",
            ModuleCategory.Mobility, "Uncrewed freighter. Refills every tank and rod magazine.",
            2f, 250f, 0f, 0f, 0f, 0f, 0f, 0, false, 0);

        public static readonly LaunchVehicle[] Vehicles =
        {
            new LaunchVehicle("LIGHT", 2.5f, 100f),
            new LaunchVehicle("MEDIUM", 5f, 250f),
            new LaunchVehicle("HEAVY", 12.5f, 500f)
        };

        public static bool Placeable(int kind) => kind >= (int)ModuleKind.Core && kind <= (int)ModuleKind.Emp;

        public static ModuleInfo Info(ModuleKind kind)
        {
            if (kind == ModuleKind.Cargo) return Cargo;
            for (int i = 0; i < Designs.Length; i++)
                if (Designs[i].Kind == kind) return Designs[i];
            return new ModuleInfo(ModuleKind.None, "---", "EMPTY", ModuleCategory.Utility, "", 0f, 0f, 0f, 0f, 0f,
                0f, 0f, 0, false, 0);
        }

        public static LaunchVehicle VehicleFor(float mass)
        {
            for (int i = 0; i < Vehicles.Length; i++)
                if (mass <= Vehicles[i].Capacity) return Vehicles[i];
            return Vehicles[Vehicles.Length - 1];
        }

        /// <summary>Module plus vehicle, before any cost multiplier.</summary>
        public static float LaunchPrice(ModuleKind kind)
        {
            ModuleInfo info = Info(kind);
            return info.Price + VehicleFor(info.Mass).Price;
        }
    }

    /// <summary>Wire-stable ability bytes; also the order of the recharge array.</summary>
    internal enum PlatformAbility : byte
    {
        Uplink = 0,
        RadarScan = 1,
        Elint = 2,
        RodStrike = 3,
        EmpBurst = 4,
        Rephase = 5,
        OrbitShift = 6
    }

    internal enum AbilityWindow : byte
    {
        Overhead = 0,
        Away = 1,
        Any = 2
    }

    internal readonly struct AbilityInfo
    {
        public readonly PlatformAbility Ability;
        public readonly string Code;
        public readonly string Name;
        public readonly string Summary;
        public readonly ModuleKind Module;
        public readonly float EnergyKj;
        public readonly float RechargeSeconds;
        public readonly float Fuel;
        public readonly AbilityWindow Window;

        public AbilityInfo(PlatformAbility ability, string code, string name, string summary, ModuleKind module,
                           float energyKj, float rechargeSeconds, float fuel, AbilityWindow window)
        {
            Ability = ability;
            Code = code;
            Name = name;
            Summary = summary;
            Module = module;
            EnergyKj = energyKj;
            RechargeSeconds = rechargeSeconds;
            Fuel = fuel;
            Window = window;
        }
    }

    internal static class PlatformAbilities
    {
        public const int Count = 7;

        /// <summary>Fuel per band for a raise or lower; charged per step.</summary>
        public const float ShiftFuelPerBand = 35f;

        public static readonly AbilityInfo[] All =
        {
            new AbilityInfo(PlatformAbility.Uplink, "UPL", "UPLINK",
                "Full-screen EO/IR feed you steer. Information for you only.",
                ModuleKind.Imager, 0f, 0f, 0f, AbilityWindow.Overhead),
            new AbilityInfo(PlatformAbility.RadarScan, "SAR", "RADAR SCAN",
                "Radar-image a scene; stationary ground contacts are revealed.",
                ModuleKind.Imager, 240f, 45f, 0f, AbilityWindow.Overhead),
            new AbilityInfo(PlatformAbility.Elint, "ELT", "ELINT SWEEP",
                "Locate enemy ground radars that are emitting.",
                ModuleKind.Sigint, 180f, 60f, 0f, AbilityWindow.Overhead),
            new AbilityInfo(PlatformAbility.RodStrike, "ROD", "ROD STRIKE",
                "Kinetic penetrator onto the mark. Spends a rod.",
                ModuleKind.Rods, 80f, 20f, 0f, AbilityWindow.Overhead),
            new AbilityInfo(PlatformAbility.EmpBurst, "EMP", "EMP BURST",
                "High-altitude burst: radars jammed across a wide area, friend and foe.",
                ModuleKind.Emp, 900f, 180f, 0f, AbilityWindow.Overhead),
            new AbilityInfo(PlatformAbility.Rephase, "PHS", "REPHASE",
                "Phasing burn: the next pass begins in 10 s.",
                ModuleKind.Propulsion, 0f, 30f, 25f, AbilityWindow.Away),
            new AbilityInfo(PlatformAbility.OrbitShift, "ORB", "ORBIT CHANGE",
                "Raise or lower one band. 30 s transfer, abilities offline.",
                ModuleKind.Propulsion, 0f, 0f, ShiftFuelPerBand, AbilityWindow.Any)
        };

        public static bool Valid(int ability) => ability >= 0 && ability < Count;

        public static AbilityInfo Info(PlatformAbility ability) => All[Math.Min(Count - 1, (int)ability)];
    }
}
