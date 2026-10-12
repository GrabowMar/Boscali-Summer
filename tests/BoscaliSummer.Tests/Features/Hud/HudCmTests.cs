using BoscaliSummer.Modules.Hud.Domain;

namespace BoscaliSummer.Tests.Features.Hud
{
    /// <summary>Countermeasure tags, LOW and EMPTY, and which station counters which seeker.</summary>
    internal static class HudCmTests
    {
        public static void Run()
        {
            TestAssert.That(HudCm.TagOf(HudCm.MaskOf(true, false), false, "IR Flares") == "IR", "Flares wear IR");
            TestAssert.That(HudCm.TagOf(HudCm.MaskOf(false, true), false, "Chaff") == "RDR", "Chaff wears RDR");
            TestAssert.That(HudCm.TagOf(HudCm.MaskOf(false, true), true, "Radar Jammer") == "JAM", "A charging radar countermeasure is a jammer");
            TestAssert.That(HudCm.TagOf(0, false, "Smoke") == "SMO" && HudCm.TagOf(0, false, null) == "CM", "An unknown kind wears its name or CM");

            CmStation flares = new CmStation { Tag = "IR", Ammo = 72, Max = 72, Mask = HudCm.IrMask };
            CmStation chaff = new CmStation { Tag = "RDR", Ammo = 30, Max = 30, Mask = HudCm.RadarMask };
            CmStation jammer = new CmStation { Tag = "JAM", Ammo = 1, Max = 1, Chargeable = true, Mask = HudCm.RadarMask };

            TestAssert.That(HudCm.Level(flares) == CmLevel.Ok, "A full load is fine");
            flares.Ammo = 19; TestAssert.That(HudCm.Level(flares) == CmLevel.Ok, "19 of 72 is just above a quarter");
            flares.Ammo = 18; TestAssert.That(HudCm.Level(flares) == CmLevel.Low, "A quarter or less is LOW");
            flares.Ammo = 0; TestAssert.That(HudCm.Level(flares) == CmLevel.Empty, "None left is EMPTY");
            TestAssert.That(HudCm.Level(jammer) == CmLevel.Ok, "A jammer never counts down");
            TestAssert.That(HudCm.Level(new CmStation { Ammo = 5, Max = 0 }) == CmLevel.Ok, "An unknown full load is not LOW");

            TestAssert.That(HudCm.Tile(chaff) == "RDR 30" && HudCm.Tile(jammer) == "JAM", "Tiles read tag and count; a jammer reads JAM");
            chaff.Ammo = 7; TestAssert.That(HudCm.Words(chaff) == "RDR 7 LOW", "LOW is a word on the tile, not only a colour");
            chaff.Ammo = 0; TestAssert.That(HudCm.Words(chaff) == "RDR 0 EMPTY", "EMPTY too");
            chaff.Ammo = 30; TestAssert.That(HudCm.Words(chaff) == "RDR 30" && HudCm.Words(jammer) == "JAM", "A healthy tile adds nothing");
            TestAssert.That(HudCm.Word(flares) == "FLR" && HudCm.Word(chaff) == "CHF" && HudCm.Word(jammer) == "JAM", "A dispenser's row says FLR, CHF or JAM");
            TestAssert.That(HudCm.Word(new CmStation { Tag = "SMO" }) == "SMO" && HudCm.Word(default) == "CM", "...else its tag, else CM");

            flares.Ammo = 72;
            TestAssert.That(HudCm.Counters(flares, "IR") && !HudCm.Counters(flares, "SARH"), "Flares answer IR seekers only");
            TestAssert.That(HudCm.Counters(chaff, "ARH") && HudCm.Counters(chaff, "SARH") && !HudCm.Counters(chaff, "IR"), "Chaff answers ARH and SARH");
            TestAssert.That(HudCm.Counters(jammer, "SARH"), "A jammer answers radar seekers too");
            foreach (string seeker in new[] { "ARAD", "Laser", "Optical", "INS", "INS / Opt.", "", null })
                TestAssert.That(!HudCm.Counters(flares, seeker) && !HudCm.Counters(chaff, seeker), "Nothing on the board counters " + (seeker ?? "null"));
        }
    }
}
