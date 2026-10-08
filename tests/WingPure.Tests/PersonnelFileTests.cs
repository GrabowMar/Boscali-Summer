using BoscaliSummer.Modules.Wing.Domain.Pure;
using BoscaliSummer.Tests;

namespace WingPure.Tests
{
    internal static class PersonnelFileTests
    {
        public static void Run()
        {
            string a = PersonnelFile.Number("NIGHTJAR", "Alexandra Kowalski");
            TestAssert.That(a.StartsWith("PF-") && a.Length == 7, "file number is PF- and four digits");
            TestAssert.That(a == PersonnelFile.Number("nightjar", "ALEXANDRA KOWALSKI"), "file number ignores case");
            TestAssert.That(a != PersonnelFile.Number("KESTREL", "Ines Varga"), "another pilot, another number");
            TestAssert.That(PersonnelFile.Number(null, null).Length == 7, "null identity still numbers");

            var x = new int[PersonnelFile.Bars];
            var y = new int[PersonnelFile.Bars];
            TestAssert.That(PersonnelFile.Pattern("NIGHTJAR", "A", x) == PersonnelFile.Bars, "pattern fills every bar");
            PersonnelFile.Pattern("NIGHTJAR", "A", y);
            bool same = true, ranged = true;
            for (int i = 0; i < x.Length; i++)
            {
                same &= x[i] == y[i];
                ranged &= x[i] >= 1 && x[i] <= 3;
            }
            TestAssert.That(same && ranged, "pattern is stable and each bar is 1..3 units");
            TestAssert.That(PersonnelFile.Pattern("A", "B", null) == 0 && PersonnelFile.Pattern("A", "B", new int[3]) == 3, "short or missing buffers are safe");

            TestAssert.That(PersonnelFile.Excerpt("  ", 40) == "" && PersonnelFile.Excerpt(null, 40) == "", "blank bio, blank excerpt");
            TestAssert.That(PersonnelFile.Excerpt("Short bio.", 40) == "Short bio.", "short bio is whole");
            string cut = PersonnelFile.Excerpt("Coastal patrol instructor. Prefers a quiet radio and precise formation flying.", 40);
            TestAssert.That(cut == "Coastal patrol instructor. Prefers a" && !cut.Contains("…"), "cuts at a word, no ellipsis");
            TestAssert.That(PersonnelFile.Excerpt("Abcdefghijklmnopqrstuvwxyz", 10) == "Abcdefghij", "one long word is cut hard");

            TestAssert.That(PersonnelFile.Cell(0.56f, 0) == 1f && PersonnelFile.Cell(0.56f, 4) == 0f, "ladder: passed ranks full, higher empty");
            TestAssert.That(System.Math.Abs(PersonnelFile.Cell(0.56f, 2) - 0.8f) < 0.001f, "ladder: current rank shows its progress");
            TestAssert.That(PersonnelFile.Cell(1f, 4) == 1f && PersonnelFile.Cell(0f, 0) == 0f, "ladder ends");
        }
    }
}
