using BoscaliSummer.Core.Contracts;

namespace BoscaliSummer.Modules.Nation.Domain
{
    /// <summary>Every number of the NATION layer's economy and the formulas over them. Pure: no game, no clock. Politics: <see cref="PoliticsRules"/>.</summary>
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
        public const float AircraftSeconds = 420f;
        public const float VehicleSeconds = 150f;
        /// <summary>The staff builds only with this multiple of the cost in the treasury, so a build never starves convoys.</summary>
        public const float StaffBuildReserve = 1.25f;

        /// <summary>Production speed: the economy law and stability 0..100 as 0.9..1.1 (politics multiplies it on top).</summary>
        public static float Output(EconomyPolicy economy, float stability)
        {
            float law = economy == EconomyPolicy.War ? 1.25f : economy == EconomyPolicy.Civilian ? 0.8f : 1f;
            return law * (0.9f + 0.2f * Clamp01(stability / 100f));
        }

        /// <summary>Multiplier on the faction's regular income.</summary>
        public static float Income(EconomyPolicy economy) =>
            economy == EconomyPolicy.Civilian ? 1.2f : economy == EconomyPolicy.War ? 0.8f : 1f;

        /// <summary>Room for plants: 2 + held airbases, never fewer than the mission gave the faction (<paramref name="initial"/>).</summary>
        public static int Slots(int heldAirbases, int initial = 0)
        {
            int room = BaseSlots + (heldAirbases > 0 ? heldAirbases : 0);
            int slots = room > initial ? room : initial;
            return slots > MaxPlants ? MaxPlants : slots;
        }

        public static float BuildSeconds(int constructionPlants, float baseSeconds = FactoryBuildSeconds)
        {
            float seconds = baseSeconds * (1f - ConstructionCut * (constructionPlants > 0 ? constructionPlants : 0));
            return seconds < MinBuildSeconds ? MinBuildSeconds : seconds;
        }

        public static float UnitSeconds(bool aircraft) => aircraft ? AircraftSeconds : VehicleSeconds;

        private static float Clamp01(float v) => v < 0f ? 0f : v > 1f ? 1f : v;
    }
}
