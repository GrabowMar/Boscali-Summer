using System;
using BoscaliSummer.Framework.Contracts;

namespace BoscaliSummer.Features.Progression.Runtime
{
    /// <summary>
    /// One perk: either a multiplier or a support capability, never both. Group is display-only.
    /// </summary>
    internal readonly struct PerkDefinition
    {
        public readonly byte Id;
        public readonly string Group;
        public readonly string Name;
        public readonly string Description;
        public readonly byte Cost;
        public readonly PerkEffect Effect;
        public readonly float Multiplier;
        public readonly string Capability;
        public readonly string Icon;

        public PerkDefinition(
            byte id, string group, string name, string description, byte cost,
            PerkEffect effect, float multiplier, string icon = null)
            : this(id, group, name, description, cost, effect, multiplier, null, icon)
        {
        }

        public PerkDefinition(
            byte id, string group, string name, string description, byte cost,
            string capability, string icon = null)
            : this(id, group, name, description, cost, PerkEffect.FuelUse, 1f, capability, icon)
        {
        }

        private PerkDefinition(
            byte id, string group, string name, string description, byte cost,
            PerkEffect effect, float multiplier, string capability, string icon)
        {
            Id = id;
            Group = group;
            Name = name;
            Description = description;
            Cost = cost;
            Effect = effect;
            Multiplier = multiplier;
            Capability = capability;
            Icon = icon;
        }
    }

    internal static class PerkCatalog
    {
        public const string FlightSystems = "FLIGHT SYSTEMS";
        public const string Allocation = "ALLOCATION";
        public const string Authorisations = "SUPPORT AUTHORISATIONS";

        /// <summary>
        /// Ordered so that <c>All[i].Id == i</c>. <c>PerkCatalogTests</c> asserts that, the
        /// mask width, and that this table and <c>SupportCatalog</c> agree on every capability.
        /// </summary>
        public static readonly PerkDefinition[] All =
        {
            new PerkDefinition(0, FlightSystems, "Fuel Discipline",
                "8% lower fuel consumption.", 1, PerkEffect.FuelUse, 0.92f, "fuel"),
            new PerkDefinition(1, Allocation, "Combat Pay",
                "15% more allocation from combat rewards.", 1, PerkEffect.CombatReward, 1.15f, "combat"),
            new PerkDefinition(2, Allocation, "Ground Crew",
                "20% more allocation from supply, refuel and repair.", 1, PerkEffect.ServiceReward, 1.20f, "ground"),
            new PerkDefinition(3, Allocation, "Objective Focus",
                "20% more allocation from captures and pilot rescue.", 1, PerkEffect.ObjectiveReward, 1.20f, "objective"),
            new PerkDefinition(4, Allocation, "Logistics Officer",
                "20% cheaper support requests.", 1, PerkEffect.SupportCost, 0.80f, "logistics"),
            new PerkDefinition(5, Authorisations, "Satellite Scan",
                "Authorises satellite reconnaissance sweeps.", 1, SupportCapabilities.Recon, "recon"),
            new PerkDefinition(6, Authorisations, "Combat Engineering",
                "Authorises controlled-zone fortification.", 2, SupportCapabilities.Fortify, "fortify"),
            new PerkDefinition(7, Authorisations, "Orbital Strike",
                "Authorises Rod-from-God kinetic strikes from a STRIKE satellite.", 2,
                SupportCapabilities.Artillery, "strike"),
            new PerkDefinition(8, Authorisations, "Electronic Warfare",
                "Authorises EMP and radar-disruption strikes from an EW satellite.", 2,
                SupportCapabilities.Emp, "ew")
        };

        /// <summary>The perk mask is a uint, so the catalogue cannot exceed 32 entries.</summary>
        public const int MaximumPerks = 32;

        /// <summary>Short display code: PAS for a passive perk, else the authorisation code.</summary>
        public static string CodeOf(PerkDefinition definition) =>
            definition.Capability == null ? "PAS" : CapabilityCode(definition.Capability);

        public static string CapabilityCode(string capability)
        {
            if (capability == SupportCapabilities.Recon) return "SAT";
            if (capability == SupportCapabilities.Fortify) return "ENG";
            if (capability == SupportCapabilities.Artillery) return "STK";
            if (capability == SupportCapabilities.Emp) return "EW";
            return "AUT";
        }

        public static bool IsDefined(byte id) => id < All.Length;

        public static PerkDefinition Get(byte id)
        {
            if (!IsDefined(id)) throw new ArgumentOutOfRangeException(nameof(id));
            return All[id];
        }
    }

    /// <summary>Score-derived point budget. Pure so it is testable without the game.</summary>
    internal static class PerkPoints
    {
        public static int Earned(int score, int scorePerPoint, int maximumPoints)
        {
            if (scorePerPoint <= 0 || maximumPoints <= 0 || score <= 0) return 0;
            return Math.Min(maximumPoints, score / scorePerPoint);
        }

        public static int EarnedForPilot(int score, int origin, int scorePerPoint, int maximumPoints, int bonus)
        {
            // Normalize before subtracting or adding so extreme inputs cannot wrap into rewards.
            int pilotScore = Math.Max(0, Math.Max(0, score) - Math.Max(0, origin));
            int scorePoints = Earned(pilotScore, scorePerPoint, Math.Min(20, maximumPoints));
            return Math.Min(20, scorePoints + Math.Max(0, Math.Min(20, bonus)));
        }
    }

    internal sealed class PerkState
    {
        public uint Mask { get; private set; }

        public PerkState(uint mask = 0u) => Mask = mask;

        public int SpentPoints
        {
            get
            {
                int spent = 0;
                for (int i = 0; i < PerkCatalog.All.Length; i++)
                    if (Has(PerkCatalog.All[i].Id)) spent += PerkCatalog.All[i].Cost;
                return spent;
            }
        }

        public int AvailablePoints(int earnedPoints) => Math.Max(0, earnedPoints - SpentPoints);

        public bool Has(byte id) => (Mask & (1u << id)) != 0u;

        public bool TryUnlock(byte id, int earnedPoints)
        {
            if (!PerkCatalog.IsDefined(id) || Has(id)) return false;
            if (AvailablePoints(earnedPoints) < PerkCatalog.Get(id).Cost) return false;
            Mask |= 1u << id;
            return true;
        }

        /// <summary>Debug bypass: grants a perk without spending a point.</summary>
        public bool ForceUnlock(byte id)
        {
            if (!PerkCatalog.IsDefined(id) || Has(id)) return false;
            Mask |= 1u << id;
            return true;
        }
    }
}
