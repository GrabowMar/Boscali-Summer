using BoscaliSummer.Core.Contracts;

namespace BoscaliSummer.Modules.DynamicOperations.Domain
{
    /// <summary>Which wing task a contract kind allows, and the reason when none does (D6: an explicit press only; reconnaissance never counts for the wing).</summary>
    internal static class OperationWing
    {
        /// <summary>The generic refusal. The wing rules carry the same sentence for their own callers; a test pins the two together.</summary>
        internal const string NoTask = "No wing task fits this sortie: fly it yourself.";

        private const string JamWhy = "A wing cannot hold a jammer on an emitter for you: fly this one yourself.";
        private const string InsertionWhy = "Needs your Ibis and its troops: the wing cannot fast-rope for you.";
        private const string RescueWhy = "The rescue and the landing are yours: the card counts only your aircraft.";

        /// <summary>Destroy for the strike kinds, Cover for the hold kinds, Observe for reconnaissance, None (with why) for Jam, Rappel, Rooftop, Rescue and any unmapped kind. why is empty unless the intent is None.</summary>
        public static WingSortieIntent IntentFor(OperationKind kind, out string why)
        {
            why = "";
            switch (kind)
            {
                case OperationKind.Interdict:
                case OperationKind.Intercept:
                case OperationKind.SupplyInterdict:
                case OperationKind.ElectronicWarfare:
                    return WingSortieIntent.Destroy;
                case OperationKind.Capture:
                case OperationKind.Defend:
                case OperationKind.Patrol:
                case OperationKind.SupplyEscort:
                case OperationKind.RepairCover:
                    return WingSortieIntent.Cover;
                case OperationKind.Recon:
                case OperationKind.DamageAssessment:
                case OperationKind.SortieReport:
                case OperationKind.BattlefieldSurvey:
                    return WingSortieIntent.Observe;
                case OperationKind.Jam: why = JamWhy; return WingSortieIntent.None;
                case OperationKind.Rappel:
                case OperationKind.Rooftop: why = InsertionWhy; return WingSortieIntent.None;
                case OperationKind.Rescue: why = RescueWhy; return WingSortieIntent.None;
                default: why = NoTask; return WingSortieIntent.None;
            }
        }
    }
}
