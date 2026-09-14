using System.Collections.Generic;
using BoscaliSummer.Features.Command.Presentation.MapUi;

namespace BoscaliSummer.Tests.Features.Command
{
    internal static class TargetPresetTests
    {
        public static void Run()
        {
            NameHygiene();
            RoundTrip();
            RejectsMalformed();
            LibraryOperations();
            Matching();
            Summary();
        }

        private static void NameHygiene()
        {
            TestAssert.That(TargetPresetRules.SanitiseName(null) == "" &&
                TargetPresetRules.SanitiseName("   ") == "", "blank names sanitise to nothing");
            TestAssert.That(TargetPresetRules.SanitiseName("a|b;c+d=e") == "A B C D E",
                "separators become single spaces, text uppercases");
            TestAssert.That(TargetPresetRules.SanitiseName("ABCDEFGHIJKLMNOP") == "ABCDEFGHIJKLMN",
                "names truncate at the 14 character ceiling");
            TestAssert.That(TargetPresetRules.SanitiseName("  fly low  ") == "FLY LOW",
                "outer whitespace trims");
            TestAssert.That(TargetPresetRules.IsReservedName("AIR") &&
                TargetPresetRules.IsReservedName("CUSTOM") &&
                !TargetPresetRules.IsReservedName("MY PRESET"),
                "built-in profile names are reserved for the library");
            TestAssert.That(TargetPresetRules.SanitiseToken("IR_SAM") == "IR_SAM" &&
                TargetPresetRules.SanitiseToken("a b|") == "ab",
                "tokens keep only identifier characters");
        }

        private static void RoundTrip()
        {
            var library = new TargetPresetLibrary();
            var preset = new TargetPresetSnapshot
            {
                Name = "aaa hunter",
                Laser = true,
                Friendly = false,
                Hostile = true,
            };
            preset.Classes.Add("AircraftDefinition");
            preset.Vehicles.Add("AAA");
            preset.Vehicles.Add("IR_SAM");
            TestAssert.That(library.Save(preset, false) == TargetPresetSaveResult.Ok, "save succeeds");
            TestAssert.That(library.At(0).Name == "AAA HUNTER", "stored name is sanitised");
            TestAssert.That(library.Assign("AAA HUNTER", 2), "quick slot assignment succeeds");

            var decoded = TargetPresetLibrary.Decode(library.Encode(), library.EncodeSlots());
            TestAssert.That(decoded.Count == 1, "round trip keeps the preset");
            TestAssert.That(decoded.At(0).Name == "AAA HUNTER" && decoded.At(0).Laser &&
                !decoded.At(0).Friendly && decoded.At(0).Hostile, "round trip keeps flags");
            TestAssert.That(decoded.At(0).Classes.Count == 1 &&
                decoded.At(0).Classes[0] == "AircraftDefinition", "round trip keeps classes");
            TestAssert.That(decoded.At(0).Vehicles.Count == 2 &&
                decoded.At(0).Vehicles[0] == "AAA" && decoded.At(0).Vehicles[1] == "IR_SAM",
                "round trip keeps vehicles");
            TestAssert.That(decoded.SlotName(0) == "" && decoded.SlotName(1) == "" &&
                decoded.SlotName(2) == "AAA HUNTER", "round trip keeps slot positions");
            TestAssert.That(decoded.Encode() == library.Encode(), "encode is stable across a decode");
        }

        private static void RejectsMalformed()
        {
            TestAssert.That(TargetPresetLibrary.Decode(null, null).Count == 0, "null storage loads empty");
            TestAssert.That(TargetPresetLibrary.Decode("", "").Count == 0, "empty storage loads empty");
            TestAssert.That(TargetPresetLibrary.Decode("garbage", "").Count == 0,
                "text without a version reads nothing");
            TestAssert.That(TargetPresetLibrary.Decode("2|FOO;0;0;0;;", "").Count == 0,
                "unknown version reads nothing");
            TestAssert.That(TargetPresetLibrary.Decode(new string('A', TargetPresetLibrary.MaxStoredLength + 1),
                new string('B', TargetPresetLibrary.MaxStoredLength + 1)).Count == 0,
                "oversized storage is refused before parsing");

            var mixed = TargetPresetLibrary.Decode("1|GOOD;0;1;0;AircraftDefinition;AAA|bad|" +
                "CUSTOM;0;0;0;;|TOO_LONG_NAME_FOR_SURE;0;0;0;;", "GOOD|GOOD|");
            TestAssert.That(mixed.Count == 2, "malformed and reserved records are skipped");
            TestAssert.That(mixed.At(0).Name == "GOOD" && mixed.At(1).Name == "TOO_LONG_NAME_",
                "valid records survive, overlong names truncate");
            TestAssert.That(mixed.SlotName(0) == "GOOD" && mixed.SlotName(1) == "" &&
                mixed.SlotName(2) == "", "duplicate slot names are dropped");
        }

        private static void LibraryOperations()
        {
            var library = new TargetPresetLibrary();
            TestAssert.That(library.Save(new TargetPresetSnapshot { Name = "AIR" }, false) ==
                TargetPresetSaveResult.ReservedName, "reserved names are rejected");
            TestAssert.That(library.Save(new TargetPresetSnapshot { Name = "  " }, false) ==
                TargetPresetSaveResult.EmptyName, "empty names are rejected");

            var preset = new TargetPresetSnapshot { Name = "AAA" };
            TestAssert.That(library.Save(preset, false) == TargetPresetSaveResult.Ok, "first save");
            TestAssert.That(library.Save(preset, false) == TargetPresetSaveResult.NameInUse,
                "duplicate save without overwrite is rejected");

            var replacement = new TargetPresetSnapshot { Name = "AAA", Laser = true };
            TestAssert.That(library.Save(replacement, true) == TargetPresetSaveResult.Ok, "overwrite succeeds");
            TestAssert.That(library.At(0).Laser, "overwrite replaces the stored state");

            TestAssert.That(!library.Rename("MISSING", "X") && !library.Rename("AAA", "AIR") &&
                !library.Rename("AAA", "CUSTOM"), "rename rejects unknown, reserved and custom names");
            TestAssert.That(library.Assign("AAA", 0), "assign slot 0");
            TestAssert.That(library.Assign("AAA", 2) && library.SlotName(0) == "" &&
                library.SlotName(2) == "AAA", "assigning a new slot moves the preset");
            TestAssert.That(library.Rename("AAA", "BBB") && library.SlotName(2) == "BBB",
                "rename follows through to the quick slots");
            TestAssert.That(library.Delete("BBB") && library.Count == 0 && library.SlotName(2) == "",
                "delete removes the preset and its slot");

            for (int i = 0; i < TargetPresetLibrary.MaxCustomPresets; i++)
                TestAssert.That(library.Save(new TargetPresetSnapshot { Name = "P" + i }, false) ==
                    TargetPresetSaveResult.Ok, "library fills to its ceiling");
            TestAssert.That(library.Save(new TargetPresetSnapshot { Name = "OVERFLOW" }, false) ==
                TargetPresetSaveResult.LibraryFull, "library refuses past its ceiling");

            var moved = new TargetPresetLibrary();
            var alpha = new TargetPresetSnapshot { Name = "ALPHA" };
            alpha.Classes.Add("AircraftDefinition");
            alpha.Classes.Add("AircraftDefinition");
            alpha.Classes.Add("bad token");
            TestAssert.That(moved.Save(alpha, false) == TargetPresetSaveResult.Ok, "token normalising save");
            TestAssert.That(moved.At(0).Classes.Count == 2 && moved.At(0).Classes[0] == "AircraftDefinition" &&
                moved.At(0).Classes[1] == "badtoken", "duplicate and unsafe tokens are dropped");
        }

        private static void Matching()
        {
            var preset = new TargetPresetSnapshot { Name = "X", Friendly = true, Hostile = false };
            preset.Classes.Add("AircraftDefinition");
            preset.Vehicles.Add("AAA");

            var state = new TargetPresetState
            {
                Laser = false,
                Friendly = true,
                Hostile = false,
                ClassTokens = new[] { new[] { "AircraftDefinition" }, new[] { "VehicleDefinition" } },
                ClassEnabled = new[] { true, false },
                VehicleTokens = new[] { new[] { "AAA" }, new[] { "MBT" } },
                VehicleEnabled = new[] { true, false },
            };
            TestAssert.That(TargetPresetRules.Matches(preset, state), "identical state matches");

            state.Laser = true;
            TestAssert.That(!TargetPresetRules.Matches(preset, state), "laser difference breaks the match");
            state.Laser = false;

            state.ClassEnabled = new[] { true, true };
            TestAssert.That(!TargetPresetRules.Matches(preset, state), "extra enabled class breaks the match");
            state.ClassEnabled = new[] { true, false };

            state.FollowHud = true;
            TestAssert.That(!TargetPresetRules.Matches(preset, state), "HUD link never claims a profile");
            state.FollowHud = false;

            TestAssert.That(TargetPresetRules.FactionExpected(preset, true) &&
                !TargetPresetRules.FactionExpected(preset, false), "faction expectation follows the side");
            TestAssert.That(TargetPresetRules.Includes(new[] { "AAA" }, new List<string> { "AAA", "MBT" }),
                "token intersection enables an entry");
            TestAssert.That(!TargetPresetRules.Includes(new[] { "AAA" }, new List<string> { "MBT" }) &&
                !TargetPresetRules.Includes(null, new List<string> { "AAA" }) &&
                !TargetPresetRules.Includes(new[] { "AAA" }, null),
                "disjoint and missing token sets never enable an entry");
        }

        private static void Summary()
        {
            var preset = new TargetPresetSnapshot { Name = "X", Friendly = true, Hostile = true, Laser = true };
            preset.Classes.Add("AircraftDefinition");
            TestAssert.That(TargetPresetRules.Summary(preset) == "ALL FACTIONS · AIR · LASER",
                "summary lists factions, classes and laser");

            preset.Classes.Add("VehicleDefinition");
            preset.Classes.Add("BuildingDefinition");
            preset.Vehicles.Add("AAA");
            preset.Vehicles.Add("RDR");
            preset.Laser = false;
            TestAssert.That(TargetPresetRules.Summary(preset) == "ALL FACTIONS · AIR/GND/BLD · 2 PLATFORMS (AAA RDR)",
                "summary sorts classes and counts platforms");

            var empty = new TargetPresetSnapshot { Name = "X" };
            TestAssert.That(TargetPresetRules.Summary(empty) == "NO FACTION · NO CLASS",
                "an empty capture reads as empty, not as everything");
            TestAssert.That(TargetPresetRules.Summary(null) == "NO PRESET", "missing preset reads as a dash noun");
        }
    }
}
