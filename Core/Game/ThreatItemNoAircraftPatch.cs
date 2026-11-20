using HarmonyLib;

namespace BoscaliSummer.Core.Game
{
    /// <summary>
    /// The game's missile list asks the map for the player's aircraft icon with <c>CombatHUD.aircraft</c>, which is null
    /// once the player is shot down while missile lines are still live: <c>Dictionary.TryGetValue(null)</c> threw an
    /// ArgumentNullException every frame (981 in one soak, each with a logged stack trace). No aircraft: no icon found,
    /// and the game's own code hides the line.
    /// </summary>
    [HarmonyPatch(typeof(ThreatItem), nameof(ThreatItem.FoundIcon))]
    internal static class ThreatItemNoAircraftPatch
    {
        private static bool Prefix(ref bool __result)
        {
            CombatHUD hud = SceneSingleton<CombatHUD>.i;
            if (hud != null && hud.aircraft != null) return true;
            __result = false;
            return false;
        }
    }
}
