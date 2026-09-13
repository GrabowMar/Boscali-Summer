using System;
using BoscaliSummer.Features.DynamicOperations.Domain;

namespace BoscaliSummer.Tests.Features.DynamicOperations
{
    internal static class OperationTitlesTests
    {
        public static void Run()
        {
            OperationKind[] kinds = (OperationKind[])Enum.GetValues(typeof(OperationKind));
            foreach (OperationKind kind in kinds)
            {
                string title = OperationTitles.Title(kind);
                TestAssert.That(!string.IsNullOrEmpty(title), "Every contract family needs a title: " + kind);
                TestAssert.That(OperationTitles.TryKind(title, out OperationKind roundTrip) && roundTrip == kind,
                    "Title must map back to its family: " + title);
            }

            TestAssert.That(!OperationTitles.TryKind("SECONDARY OBJECTIVE", out _),
                "The fallback title belongs to no family");
            TestAssert.That(!OperationTitles.TryKind(null, out _) && !OperationTitles.TryKind("", out _),
                "Empty titles map to no family");
            TestAssert.That(OperationTitles.Title((OperationKind)200) == "SECONDARY OBJECTIVE",
                "Unknown families fall back to a neutral title");
        }
    }
}
