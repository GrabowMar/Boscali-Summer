using System;
using BoscaliSummer.Framework.Contracts;

namespace BoscaliSummer.Features.Progression.Runtime
{
    internal enum SkillId : byte
    {
        FuelConservation1 = 0,
        FuelConservation2 = 1,
        ServiceSpecialist = 2,
        CombatPay1 = 3,
        CombatPay2 = 4,
        ObjectiveBonus = 5,
        VehicleRequisition = 6,
        FireMission = 7,
        CombatEngineering = 8
    }

    internal readonly struct SkillDefinition
    {
        public readonly SkillId Id;
        public readonly string Name;
        public readonly string Description;
        public readonly int MinimumRank;
        public readonly SkillId? Prerequisite;
        public readonly string Entitlement;

        public SkillDefinition(
            SkillId id, string name, string description, int minimumRank,
            SkillId? prerequisite = null, string entitlement = null)
        {
            Id = id;
            Name = name;
            Description = description;
            MinimumRank = minimumRank;
            Prerequisite = prerequisite;
            Entitlement = entitlement;
        }
    }

    internal static class SkillCatalog
    {
        public static readonly SkillDefinition[] All =
        {
            new SkillDefinition(SkillId.FuelConservation1, "Fuel Conservation I", "5% lower fuel consumption.", 1),
            new SkillDefinition(SkillId.FuelConservation2, "Fuel Conservation II", "10% lower fuel consumption.", 2, SkillId.FuelConservation1),
            new SkillDefinition(SkillId.ServiceSpecialist, "Service Specialist", "10% more allocation from supply, refuel and repair.", 2),
            new SkillDefinition(SkillId.CombatPay1, "Combat Pay I", "5% more allocation from combat rewards.", 1),
            new SkillDefinition(SkillId.CombatPay2, "Combat Pay II", "10% more allocation from combat rewards.", 3, SkillId.CombatPay1),
            new SkillDefinition(SkillId.ObjectiveBonus, "Objective Bonus", "15% more allocation from captures and pilot rescue.", 2),
            new SkillDefinition(SkillId.VehicleRequisition, "Vehicle Requisition", "Unlock vehicle airdrops.", 1, null, SupportEntitlements.VehicleAirdrop),
            new SkillDefinition(SkillId.FireMission, "Fire Mission", "Unlock artillery fire missions.", 3, SkillId.VehicleRequisition, SupportEntitlements.Artillery),
            new SkillDefinition(SkillId.CombatEngineering, "Combat Engineering", "Unlock controlled-zone fortification.", 2, SkillId.VehicleRequisition, SupportEntitlements.Fortification)
        };

        public static SkillDefinition Get(SkillId id)
        {
            for (int i = 0; i < All.Length; i++)
                if (All[i].Id == id) return All[i];
            throw new ArgumentOutOfRangeException(nameof(id));
        }

        public static bool IsDefined(SkillId id) => (int)id >= 0 && (int)id < All.Length;
    }

    internal sealed class ProgressionState
    {
        public ushort SkillMask { get; private set; }

        public ProgressionState(ushort skillMask = 0) => SkillMask = skillMask;

        public int SpentPoints
        {
            get
            {
                int value = SkillMask;
                int count = 0;
                while (value != 0) { count += value & 1; value >>= 1; }
                return count;
            }
        }

        public int AvailablePoints(int vanillaRank) => Math.Max(0, vanillaRank - SpentPoints);

        public bool Has(SkillId id) => (SkillMask & (1 << (int)id)) != 0;

        public bool TryUnlock(SkillId id, int vanillaRank)
        {
            if (!SkillCatalog.IsDefined(id) || Has(id) || AvailablePoints(vanillaRank) <= 0)
                return false;
            SkillDefinition definition = SkillCatalog.Get(id);
            if (vanillaRank < definition.MinimumRank) return false;
            if (definition.Prerequisite.HasValue && !Has(definition.Prerequisite.Value)) return false;
            SkillMask |= (ushort)(1 << (int)id);
            return true;
        }
    }
}
