using System.Collections.Generic;
using BoscaliSummer.Modules.Autopilot.Domain;

namespace BoscaliSummer.Tests.Features.Autopilot
{
    /// <summary>The generated glyph table (tools/Build-MenuIcons.py): every id the catalog uses exists and every stroke sits inside the 24 x 24 box.</summary>
    internal static class MenuIconTests
    {
        private const string SheetIds =
            "weapons lights flight wing comms radio support view defence targets aircraft gear flight-assist auto-hover autoland autoland-cancel " +
            "engine eject warning nav-lights landing-light night-vision station-next station-prev station-select hardpoint link-guns turret-auto " +
            "target-keep target-drop target-clear target-filter flare countermeasure-cycle radar drone-strike drone-screen cockpit-view external-view " +
            "map camera-mark missile-view missile-next missile-prev missile-exit call-at-mark mark-clear exfil skywell perk music track-next " +
            "track-prev receiver seek stop-audio call-tactical call-logistics call-chatter form-up engage break-off clear-six bogey-dope escort shape rtb back";

        public static void Run()
        {
            TheSixtyEightSheetGlyphsExist();
            EveryStrokeIsInsideTheBox();
            EveryIconTheCatalogUsesExists();
        }

        private static void TheSixtyEightSheetGlyphsExist()
        {
            string[] ids = SheetIds.Split(' ');
            TestAssert.Eq(ids.Length, 68, "the prototype sheet lists 68 glyphs");
            foreach (string id in ids) TestAssert.That(MenuIcons.Table.ContainsKey(id), "glyph missing: " + id);
            TestAssert.That(MenuIcons.Table.Count >= 68, "the table holds the 68 sheet glyphs plus the weapon-class, front and ping extras");
            foreach (string id in new[] { "gun", "aam", "agm", "bomb", "turret", "ping-help", "ping-attack", "ping-spike", "ping-rally" })
                TestAssert.That(MenuIcons.Table.ContainsKey(id), "extra glyph missing: " + id);
        }

        private static void EveryStrokeIsInsideTheBox()
        {
            foreach (KeyValuePair<string, IconPoly[]> icon in MenuIcons.Table)
            {
                TestAssert.That(icon.Value.Length > 0, icon.Key + " has no strokes");
                foreach (IconPoly poly in icon.Value)
                {
                    TestAssert.That(poly.Points.Length >= 4 && poly.Points.Length % 2 == 0, icon.Key + ": a stroke needs at least two x,y pairs");
                    foreach (float v in poly.Points) TestAssert.That(v >= 0f && v <= 24f, icon.Key + ": point outside 0..24 (" + v + ")");
                    if (poly.Filled) TestAssert.That(poly.Closed && poly.Points.Length == 8, icon.Key + ": a filled stroke is a square");
                }
            }
        }

        private static void EveryIconTheCatalogUsesExists()
        {
            foreach (var scenario in MenuWorlds.All())
            {
                MenuModel m = MenuCatalog.Build(scenario.Value);
                foreach (MenuCategory c in m.Categories) TestAssert.That(MenuIcons.Table.ContainsKey(c.Icon), scenario.Key + ": category glyph " + c.Icon);
                foreach (MenuRow r in MenuCatalogTests.AllRows(m))
                    foreach (string id in new[] { r.Icon, r.IconOn, r.WarnIcon, r.Ping })
                        if (id != null) TestAssert.That(MenuIcons.Table.ContainsKey(id), scenario.Key + ": " + r.Id + " uses a glyph that does not exist: " + id);
            }
            TestAssert.That(MenuIcons.Table.ContainsKey("aircraft") && MenuIcons.Table.ContainsKey("back"), "the hub and the BACK row have their glyphs");
        }
    }
}
