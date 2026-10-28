using System;
using BoscaliSummer.Core.Contracts;

namespace BoscaliSummer.Modules.DynamicOperations.Domain
{
    /// <summary>
    /// Which sortie kinds serve a live operation, how wide its site may be, which kinds the explicitly sent wing may advance, and
    /// what the board says when no tied card exists. Pure, so the tie table is testable offline (annex 03 section 5).
    /// </summary>
    internal static class SortieFocusRules
    {
        internal const float DefaultRadius = 8000f, MinimumRadius = 3000f, MaximumRadius = 15000f, MaximumAnchorMetres = 60000f;

        /// <summary>NaN or not above 0 gives DefaultRadius; otherwise clamped to 3000..15000.</summary>
        internal static float ClampRadius(float radius) =>
            !(radius > 0f) ? DefaultRadius : Math.Clamp(radius, MinimumRadius, MaximumRadius);

        /// <summary>The kind map of annex 03 section 5. Rappel, Rooftop, role None and anything the table does not list are never allowed.</summary>
        internal static bool Allows(FocusRole role, OperationKind kind)
        {
            switch (kind)
            {
                case OperationKind.Capture:
                case OperationKind.SupplyInterdict:
                case OperationKind.ElectronicWarfare:
                    return role == FocusRole.Assault;
                case OperationKind.Interdict:
                case OperationKind.Intercept:
                case OperationKind.StrikePost:
                    return role == FocusRole.Assault || role == FocusRole.Defend;
                case OperationKind.Jam:
                    return role == FocusRole.Assault || role == FocusRole.Recon;
                case OperationKind.Defend:
                case OperationKind.Patrol:
                case OperationKind.RepairCover:
                case OperationKind.SupplyEscort:
                case OperationKind.Rescue:
                    return role == FocusRole.Defend;
                case OperationKind.BattlefieldSurvey:
                    return role == FocusRole.Defend || role == FocusRole.Recon;
                case OperationKind.Recon:
                case OperationKind.DamageAssessment:
                case OperationKind.SortieReport:
                    return role == FocusRole.Recon;
                default:
                    return false;
            }
        }

        /// <summary>Status sentence for a focus with no tied card on the board; at most 128 characters, says what to do.</summary>
        internal static string ThinBoardStatus(string place)
        {
            string name = string.IsNullOrWhiteSpace(place) ? "THE OPERATION" : place.Trim();
            if (name.Length > SortieLink.MaximumPlace) name = name.Substring(0, SortieLink.MaximumPlace);
            return "No known targets near " + name + " yet. General sorties shown; scout or wait for contact.";
        }
    }
}
