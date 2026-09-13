using System.Collections.Generic;

namespace BoscaliSummer.Features.DynamicOperations.Domain
{
    /// <summary>
    /// The one place a contract kind becomes player-facing text. The wire carries the title,
    /// so the client maps it back to the family for its vanilla-style marker icon.
    /// </summary>
    internal static class OperationTitles
    {
        private static readonly Dictionary<OperationKind, string> ByKind = new Dictionary<OperationKind, string>
        {
            { OperationKind.Capture, "SECURE THE FRONT" },
            { OperationKind.Defend, "HOLD THE LINE" },
            { OperationKind.Interdict, "BREAK ENEMY PRESSURE" },
            { OperationKind.Intercept, "AIR INTERCEPT" },
            { OperationKind.Patrol, "WATCH THE APPROACH" },
            { OperationKind.Jam, "SILENCE THE RADAR" },
            { OperationKind.Rappel, "ESTABLISH A BEACHHEAD" },
            { OperationKind.Rooftop, "ROOFTOP INSERTION" },
            { OperationKind.Rescue, "BRING THEM HOME" },
            { OperationKind.Recon, "RECONNAISSANCE PASS" },
            { OperationKind.DamageAssessment, "CONFIRM THE STRIKE" },
            { OperationKind.SupplyEscort, "COVER THE SUPPLY RUN" },
            { OperationKind.SupplyInterdict, "CUT THE SUPPLY LINE" },
            { OperationKind.RepairCover, "COVER THE ENGINEERS" },
            { OperationKind.ElectronicWarfare, "HUNT THE JAMMER" },
            { OperationKind.SortieReport, "BRING BACK THE INTELLIGENCE" },
            { OperationKind.BattlefieldSurvey, "SURVEY THE AFTERMATH" },
        };

        private static readonly Dictionary<string, OperationKind> ByTitle = Invert();

        public static string Title(OperationKind kind) =>
            ByKind.TryGetValue(kind, out string title) ? title : "SECONDARY OBJECTIVE";

        public static bool TryKind(string title, out OperationKind kind)
        {
            if (!string.IsNullOrEmpty(title)) return ByTitle.TryGetValue(title, out kind);
            kind = default;
            return false;
        }

        private static Dictionary<string, OperationKind> Invert()
        {
            var inverted = new Dictionary<string, OperationKind>(ByKind.Count);
            foreach (KeyValuePair<OperationKind, string> pair in ByKind) inverted[pair.Value] = pair.Key;
            return inverted;
        }
    }
}
