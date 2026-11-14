using BoscaliSummer.Core.Contracts;

namespace BoscaliSummer.Modules.Nation.Domain
{
    /// <summary>Every number of the NATION layer and the formulas over them. Pure: no game, no clock.</summary>
    internal static class NationRules
    {
        public const int MaxFactions = 4;
        public const int MaxPlants = 16;
        public const int MaxStockRows = 24;
        public const int BaseSlots = 2;
        public const float FactoryCost = 2500f;
        public const float FactoryBuildSeconds = 240f;
        public const float MinBuildSeconds = 60f;
        /// <summary>Each plant on CONSTRUCTION cuts the build time by this share.</summary>
        public const float ConstructionCut = 0.25f;
        public const float PolicyLockSeconds = 300f;
        public const float PickLockSeconds = 60f;
        public const float FocusSeconds = 240f;
        public const float AircraftSeconds = 420f;
        public const float VehicleSeconds = 150f;
        public const float WarBondsFunds = 5000f;
        public const float UnityMorale = 15f;
        /// <summary>The staff builds only with this multiple of the cost in the treasury, so a build never starves convoys.</summary>
        public const float StaffBuildReserve = 1.25f;

        public static bool Has(ushort done, FocusId id) => (done & (1 << (int)id)) != 0;

        /// <summary>Production speed: the economy law, stability 0..100 as 0.9..1.1 and the INDUSTRY focuses.</summary>
        public static float Output(EconomyPolicy economy, float stability, ushort done)
        {
            float law = economy == EconomyPolicy.War ? 1.25f : economy == EconomyPolicy.Civilian ? 0.8f : 1f;
            float mood = 0.9f + 0.2f * Clamp01(stability / 100f);
            float focus = 1f + (Has(done, FocusId.WarIndustry) ? 0.10f : 0f) + (Has(done, FocusId.TotalMobilisation) ? 0.15f : 0f);
            return law * mood * focus;
        }

        /// <summary>Multiplier on the faction's regular income.</summary>
        public static float Income(EconomyPolicy economy) =>
            economy == EconomyPolicy.Civilian ? 1.2f : economy == EconomyPolicy.War ? 0.8f : 1f;

        /// <summary>Room for plants: 2 + held airbases, never fewer than the mission gave the faction (<paramref name="initial"/>), plus the INDUSTRY focuses.</summary>
        public static int Slots(int heldAirbases, ushort done, int initial = 0)
        {
            int room = BaseSlots + (heldAirbases > 0 ? heldAirbases : 0);
            int slots = (room > initial ? room : initial) +
                (Has(done, FocusId.NewWorks) ? 1 : 0) + (Has(done, FocusId.TotalMobilisation) ? 2 : 0);
            return slots > MaxPlants ? MaxPlants : slots;
        }

        public static float BuildSeconds(int constructionPlants, float baseSeconds = FactoryBuildSeconds)
        {
            float seconds = baseSeconds * (1f - ConstructionCut * (constructionPlants > 0 ? constructionPlants : 0));
            return seconds < MinBuildSeconds ? MinBuildSeconds : seconds;
        }

        public static float Cost(ushort done, float baseCost = FactoryCost) =>
            baseCost * (Has(done, FocusId.StateContracts) ? 0.75f : 1f);

        /// <summary>What a focus does to a number another module owns; 1 when no finished focus touches it.</summary>
        public static float Scale(NationStat stat, ushort done)
        {
            switch (stat)
            {
                case NationStat.ConvoyCost: return Has(done, FocusId.SupplyCorps) ? 0.75f : 1f;
                case NationStat.WingCallCost: return Has(done, FocusId.AirArmada) ? 0.75f : 1f;
                case NationStat.FrontIncome: return Has(done, FocusId.SignalsBureau) ? 1.5f : 1f;
                case NationStat.PerkPrice: return Has(done, FocusId.BlackBudget) ? 0.8f : 1f;
                default: return 1f;
            }
        }

        public static float UnitSeconds(bool aircraft) => aircraft ? AircraftSeconds : VehicleSeconds;

        private static float Clamp01(float v) => v < 0f ? 0f : v > 1f ? 1f : v;
    }

    /// <summary>The one focus tree every faction runs: three branches of four tiers, each focus needing the one above it.</summary>
    internal static class FocusTree
    {
        public const int Count = 12;
        public const int Tiers = 4;

        private static readonly string[] Names =
        {
            "NEW WORKS", "WAR INDUSTRY", "STATE CONTRACTS", "TOTAL MOBILISATION",
            "FIGHTER RESERVE", "SUPPLY CORPS", "ARMOURED RESERVE", "AIR ARMADA",
            "NATIONAL UNITY", "WAR BONDS", "SIGNALS BUREAU", "BLACK BUDGET",
        };

        private static readonly string[] Effects =
        {
            "+1 slot, free factory", "Output +10 %", "Factory cost -25 %", "+2 slots, output +15 %",
            "4 fighters to stock", "Convoys -25 %", "6 armour, 3 air defence", "Wingman calls -25 %",
            "Stability +15", "5 000 funds now", "OPS income +50 %", "Perk prices -20 %",
        };

        private static readonly string[] Branches = { "INDUSTRY", "MILITARY", "STATE" };

        public static bool Valid(int id) => id >= 0 && id < Count;
        public static int Branch(FocusId id) => (int)id / Tiers;
        public static int Tier(FocusId id) => (int)id % Tiers;
        public static string Name(FocusId id) => Valid((int)id) ? Names[(int)id] : "";
        public static string Effect(FocusId id) => Valid((int)id) ? Effects[(int)id] : "";
        public static string BranchName(int branch) => branch >= 0 && branch < Branches.Length ? Branches[branch] : "";

        /// <summary>The focus above it in its branch, or -1 for a first tier.</summary>
        public static int Requires(FocusId id) => Tier(id) == 0 ? -1 : (int)id - 1;

        /// <summary>The last tier waits for the mission's first escalation stage.</summary>
        public static bool NeedsEscalation(FocusId id) => Tier(id) == Tiers - 1;

        /// <summary>Open to start now: known, not done, its parent done (or counted in <paramref name="done"/>) and the stage reached.</summary>
        public static bool CanStart(ushort done, FocusId id, int escalationStage)
        {
            if (!Valid((int)id) || NationRules.Has(done, id)) return false;
            int parent = Requires(id);
            if (parent >= 0 && !NationRules.Has(done, (FocusId)parent)) return false;
            return !NeedsEscalation(id) || escalationStage >= 1;
        }
    }
}
