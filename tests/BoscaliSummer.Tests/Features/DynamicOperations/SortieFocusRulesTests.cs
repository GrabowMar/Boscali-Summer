using System;
using System.Collections.Generic;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Modules.DynamicOperations.Domain;

namespace BoscaliSummer.Tests.Features.DynamicOperations
{
    /// <summary>Which sortie kinds serve which operation, the site radius, the wing's hold credit and the thin-board sentence (annex 03 C2).</summary>
    internal static class SortieFocusRulesTests
    {
        private const FocusRole A = FocusRole.Assault, D = FocusRole.Defend, R = FocusRole.Recon;

        // The annex 03 section 5 table, written out independently of the rule so a drift in either one fails.
        private static readonly Dictionary<OperationKind, FocusRole[]> Table = new Dictionary<OperationKind, FocusRole[]>
        {
            [OperationKind.Capture] = new[] { A },
            [OperationKind.Interdict] = new[] { A, D },
            [OperationKind.Intercept] = new[] { A, D },
            [OperationKind.SupplyInterdict] = new[] { A },
            [OperationKind.ElectronicWarfare] = new[] { A },
            [OperationKind.Jam] = new[] { A, R },
            [OperationKind.Defend] = new[] { D },
            [OperationKind.Patrol] = new[] { D },
            [OperationKind.RepairCover] = new[] { D },
            [OperationKind.SupplyEscort] = new[] { D },
            [OperationKind.Rescue] = new[] { D },
            [OperationKind.BattlefieldSurvey] = new[] { D, R },
            [OperationKind.Recon] = new[] { R },
            [OperationKind.DamageAssessment] = new[] { R },
            [OperationKind.SortieReport] = new[] { R },
            [OperationKind.Rappel] = new FocusRole[0],
            [OperationKind.Rooftop] = new FocusRole[0],
        };

        public static void Run()
        {
            EveryKindIsInTheTieTable();
            AllowsMatchesTheTable();
            NoRoleAndUndefinedRolesAllowNothing();
            AirborneSitesAreNeverTied();
            ClampRadiusBounds();
            OnlyDefendAndPatrolLetTheWingCount();
            ThinBoardStatusIsBoundedAndSaysWhatToDo();
        }

        private static void EveryKindIsInTheTieTable()
        {
            foreach (OperationKind kind in Enum.GetValues(typeof(OperationKind)))
                TestAssert.That(Table.ContainsKey(kind), "Add " + kind + " to the sortie tie table and to SortieFocusRules.Allows");
        }

        private static void AllowsMatchesTheTable()
        {
            foreach (OperationKind kind in Enum.GetValues(typeof(OperationKind)))
                foreach (FocusRole role in new[] { A, D, R })
                    TestAssert.That(SortieFocusRules.Allows(role, kind) == Array.IndexOf(Table[kind], role) >= 0,
                        kind + " for " + role + " follows the kind map");
        }

        private static void NoRoleAndUndefinedRolesAllowNothing()
        {
            foreach (OperationKind kind in Enum.GetValues(typeof(OperationKind)))
            {
                TestAssert.That(!SortieFocusRules.Allows(FocusRole.None, kind), "an untied card (role None) allows no kind: " + kind);
                TestAssert.That(!SortieFocusRules.Allows((FocusRole)9, kind), "an undefined role allows no kind: " + kind);
            }
            TestAssert.That(!SortieFocusRules.Allows(A, (OperationKind)200), "an undefined kind is never allowed");
        }

        private static void AirborneSitesAreNeverTied()
        {
            foreach (FocusRole role in Enum.GetValues(typeof(FocusRole)))
                TestAssert.That(!SortieFocusRules.Allows(role, OperationKind.Rappel) && !SortieFocusRules.Allows(role, OperationKind.Rooftop),
                    "Rappel and Rooftop stay on the general board for " + role);
        }

        private static void ClampRadiusBounds()
        {
            TestAssert.Near(SortieFocusRules.DefaultRadius, 8000f, "the default radius is 8 km");
            TestAssert.Near(SortieFocusRules.ClampRadius(float.NaN), 8000f, "NaN takes the default");
            TestAssert.Near(SortieFocusRules.ClampRadius(0f), 8000f, "zero takes the default");
            TestAssert.Near(SortieFocusRules.ClampRadius(-5f), 8000f, "a negative radius takes the default");
            TestAssert.Near(SortieFocusRules.ClampRadius(float.NegativeInfinity), 8000f, "minus infinity takes the default");
            TestAssert.Near(SortieFocusRules.ClampRadius(100f), 3000f, "a tiny radius rises to the minimum");
            TestAssert.Near(SortieFocusRules.ClampRadius(3000f), 3000f, "the minimum passes");
            TestAssert.Near(SortieFocusRules.ClampRadius(8000f), 8000f, "an in-range radius passes");
            TestAssert.Near(SortieFocusRules.ClampRadius(15000f), 15000f, "the maximum passes");
            TestAssert.Near(SortieFocusRules.ClampRadius(20000f), 15000f, "a wide radius falls to the maximum");
            TestAssert.Near(SortieFocusRules.ClampRadius(1e9f), 15000f, "a huge radius falls to the maximum");
            TestAssert.Near(SortieFocusRules.ClampRadius(float.PositiveInfinity), 15000f, "plus infinity falls to the maximum");
            TestAssert.Near(SortieFocusRules.MaximumAnchorMetres, 60000f, "an owned-base anchor may be 60 km away");
        }

        private static void OnlyDefendAndPatrolLetTheWingCount()
        {
            foreach (OperationKind kind in Enum.GetValues(typeof(OperationKind)))
                TestAssert.That(SortieFocusRules.WingMayCount(kind) == (kind == OperationKind.Defend || kind == OperationKind.Patrol),
                    "the wing's on-station status advances only Defend and Patrol: " + kind);
        }

        private static void ThinBoardStatusIsBoundedAndSaysWhatToDo()
        {
            string forty = new string('P', SortieLink.MaximumPlace);
            string status = SortieFocusRules.ThinBoardStatus(forty);
            TestAssert.That(status.Length <= 128 && status.Contains(forty), "a 40-character place fits 128 characters and is named");
            TestAssert.That(status.Contains("scout or wait for contact"), "the sentence says what to do");
            string clipped = SortieFocusRules.ThinBoardStatus(new string('Q', 90));
            TestAssert.That(clipped.Length <= 128 && clipped.Contains(new string('Q', SortieLink.MaximumPlace)),
                "an over-long place is clipped to 40 characters, never past 128 in the sentence");
            foreach (string place in new[] { "", null, "   " })
            {
                string empty = SortieFocusRules.ThinBoardStatus(place);
                TestAssert.That(empty.Length > 0 && empty.Length <= 128 && empty.Contains("scout or wait for contact"),
                    "a missing place still gives a sentence that says what to do");
            }
            TestAssert.That(SortieFocusRules.ThinBoardStatus("PORT AURELIA").StartsWith("No known targets near PORT AURELIA"),
                "the sentence names the operation place");
        }
    }
}
