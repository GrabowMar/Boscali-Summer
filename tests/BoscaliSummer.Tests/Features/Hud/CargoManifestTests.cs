using System;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Modules.Hud.Domain;

namespace BoscaliSummer.Tests.Features.Hud
{
    internal static class CargoManifestTests
    {
        public static void Run()
        {
            var d = new CargoScope { Kind = CargoKind.Paratroopers, Ammo = 16, Max = 32, ZoneAvailable = true, UsesLandingZone = true, HasReach = true, HasZone = true, Selected = true, DistanceM = 5000f, ReachM = 6000f };
            TestAssert.Eq(CargoManifestRules.Status(d), "IN DROP RANGE", "A selected armed HALO load inside its glide reach shows the range cue");
            d.Safe = true;
            TestAssert.Eq(CargoManifestRules.Status(d), "SAFETY ON", "Range must not override native safety");
            d.Ammo = 0;
            TestAssert.Eq(CargoManifestRules.Status(d), "LOAD EXPENDED", "Empty loads never tell the pilot to drop");
            d.Ammo = 16; d.Safe = false; d.Selected = false;
            TestAssert.Eq(CargoManifestRules.Status(d), "SELECT LOAD", "A standby load cannot masquerade as the active release station");
            d.Selected = true; d.DistanceM = 6000.1f;
            TestAssert.Eq(CargoManifestRules.Status(d), "OUT OF REACH", "Just beyond reach is outside");
            d.DistanceM = 6000f;
            TestAssert.That(CargoManifestRules.InRange(d), "Reach boundary is inclusive");
            d.DistanceM = float.NaN;
            TestAssert.That(!CargoManifestRules.InRange(d), "Invalid position never shows an in-range cue");
            d.HasZone = false;
            TestAssert.Eq(CargoManifestRules.Status(d), "SET DROP ZONE", "A removed zone clears the range state");
            d.ZoneAvailable = false;
            TestAssert.Eq(CargoManifestRules.Status(d), "NATIVE TARGET", "A missing zone owner keeps the designation fallback");
            d.ZoneAvailable = true;
            d.UsesLandingZone = false;
            TestAssert.Eq(CargoManifestRules.Status(d), "NATIVE RELEASE", "Native cargo and non-zoned scout gliders keep native targeting");
            d.UsesLandingZone = true; d.HasZone = true; d.HasReach = false;
            TestAssert.Eq(CargoManifestRules.Status(d), "ZONE ASSIGNED", "Powered gliders never borrow the unpowered glider's range");
            d.Reloading = true;
            TestAssert.Eq(CargoManifestRules.Status(d), "RELOADING", "Reloading is reported before release guidance");
            TestAssert.That(!CargoManifestRules.Present(default), "A non-cargo aircraft has no context card");
            TestAssert.That(!CargoManifestRules.Present(new CargoScope { Kind = CargoKind.Cargo, Cargo = 0 }), "A spent kit takes its tab off the scope");
            TestAssert.That(CargoManifestRules.Present(new CargoScope { Kind = CargoKind.Troops, Troops = 3 }), "Loads still aboard keep the tab");

            JumpLight();
            Checklist();
            WindowAndScale();
            Hints();

            BoardOptions manual = BoardOptions.Default;
            manual.Page1 = BoardPage.Cargo;
            manual.PageCargo = false; // an old whitelist value must not override the new manual selector
            manual.Declutter = BoardDeclutter.Minimal;
            TestAssert.That(CuePriority.Has(manual.Effective().Pages, ScopePage.Cargo), "A manually chosen CARGO page stays available at Minimal");
            manual.ScopeMode = BoardScopeMode.Off;
            TestAssert.That(manual.Effective().Pages == 0, "Tactical pages off includes CARGO");
        }

        private static CargoScope Halo() => new CargoScope
        {
            Kind = CargoKind.Paratroopers, Ammo = 16, Max = 16, Troops = 16, ZoneAvailable = true, UsesLandingZone = true, HasReach = true, HasZone = true, Selected = true, DistanceM = 5000f, ReachM = 6000f,
        };

        /// <summary>The jump light follows the release chain: anything that holds the drop is RED, a zone-capable load with no zone AMBER, everything else GREEN.</summary>
        private static void JumpLight()
        {
            CargoScope d = Halo();
            TestAssert.Eq(CargoManifestRules.Light(d), CargoLight.Green, "A selected, armed HALO stick inside its reach is a green light");
            d.Safe = true;
            TestAssert.Eq(CargoManifestRules.Light(d), CargoLight.Red, "Safety on holds the drop whatever the range");
            d = Halo(); d.Selected = false;
            TestAssert.Eq(CargoManifestRules.Light(d), CargoLight.Red, "A load that is not the release station is red");
            d = Halo(); d.Ammo = 0;
            TestAssert.Eq(CargoManifestRules.Light(d), CargoLight.Red, "An expended load is red");
            d = Halo(); d.Reloading = true;
            TestAssert.Eq(CargoManifestRules.Light(d), CargoLight.Red, "A reloading load is red");
            d = Halo(); d.DistanceM = 7000f;
            TestAssert.Eq(CargoManifestRules.Light(d), CargoLight.Red, "A zone out of reach is red");
            d = Halo(); d.HasZone = false;
            TestAssert.Eq(CargoManifestRules.Light(d), CargoLight.Amber, "No zone set: amber, the stick follows its designation");
            d.ZoneAvailable = false;
            TestAssert.Eq(CargoManifestRules.Light(d), CargoLight.Green, "No zone owner at all: the game's own aim, green");
            d = Halo(); d.UsesLandingZone = false; d.HasZone = false;
            TestAssert.Eq(CargoManifestRules.Light(d), CargoLight.Green, "Native cargo releases on the game's own terms");
            d = Halo(); d.HasReach = false;
            TestAssert.Eq(CargoManifestRules.Light(d), CargoLight.Green, "A zone with no rated reach (a powered glider) is green");
            TestAssert.That(CargoManifestRules.LightWord(CargoLight.Green) == "GREEN LIGHT" && CargoManifestRules.LightWord(CargoLight.Red) == "RED LIGHT" && CargoManifestRules.LightWord(CargoLight.Amber) == "AMBER LIGHT", "The light's words");
        }

        /// <summary>Each checklist line passes, fails or stands aside, and a green light is exactly every applicable line passed.</summary>
        private static void Checklist()
        {
            CargoScope d = Halo();
            TestAssert.That(CargoManifestRules.LoadCheck(d) == CargoCheck.Pass && CargoManifestRules.SafetyCheck(d) == CargoCheck.Pass && CargoManifestRules.ZoneCheck(d) == CargoCheck.Pass && CargoManifestRules.ReachCheck(d) == CargoCheck.Pass,
                "A green HALO drop passes every line");
            d.DistanceM = 9000f;
            TestAssert.Eq(CargoManifestRules.ReachCheck(d), CargoCheck.Fail, "Out of reach fails REACH");
            d.HasZone = false;
            TestAssert.That(CargoManifestRules.ZoneCheck(d) == CargoCheck.Fail && CargoManifestRules.ReachCheck(d) == CargoCheck.NotApplicable, "No zone fails DROP ZONE and leaves REACH aside");
            d.UsesLandingZone = false;
            TestAssert.Eq(CargoManifestRules.ZoneCheck(d), CargoCheck.NotApplicable, "Native cargo has no zone line");
            d = Halo(); d.HasReach = false;
            TestAssert.Eq(CargoManifestRules.ReachCheck(d), CargoCheck.NotApplicable, "A load with no rated reach has no reach line");
            d = Halo(); d.Safe = true; d.Selected = false;
            TestAssert.That(CargoManifestRules.SafetyCheck(d) == CargoCheck.Fail && CargoManifestRules.LoadCheck(d) == CargoCheck.Fail, "Safety and selection fail on their own lines");

            // A green light is every applicable line passed, over a grid of states.
            foreach (bool selected in new[] { false, true })
                foreach (bool safe in new[] { false, true })
                    foreach (bool zone in new[] { false, true })
                        foreach (bool reach in new[] { false, true })
                            foreach (float distance in new[] { 3000f, 9000f })
                            {
                                CargoScope s = Halo();
                                s.Selected = selected; s.Safe = safe; s.HasZone = zone; s.HasReach = reach; s.DistanceM = distance;
                                bool lines = CargoManifestRules.LoadCheck(s) != CargoCheck.Fail && CargoManifestRules.SafetyCheck(s) != CargoCheck.Fail &&
                                             CargoManifestRules.ZoneCheck(s) != CargoCheck.Fail && CargoManifestRules.ReachCheck(s) != CargoCheck.Fail;
                                TestAssert.That((CargoManifestRules.Light(s) == CargoLight.Green) == lines, "Green exactly when no line fails: " + selected + safe + zone + reach + distance);
                            }
        }

        /// <summary>The line under the light says the next action, never the header's state again, and fits the 210-unit column (26 characters of 12).</summary>
        private static void Hints()
        {
            CargoScope d = Halo();
            TestAssert.Eq(CargoManifestRules.Hint(d), "IN THE WINDOW - DROP", "Green in the window: drop");
            d.DistanceM = 9000f;
            TestAssert.Eq(CargoManifestRules.Hint(d), "FLY TOWARD THE DZ", "Out of reach: close the zone");
            d = Halo(); d.Safe = true;
            TestAssert.Eq(CargoManifestRules.Hint(d), "TURN THE SAFETY OFF", "Safety on: what to do about it");
            d = Halo(); d.Selected = false;
            TestAssert.Eq(CargoManifestRules.Hint(d), "SELECT IT IN WEAPONS", "Not the release station: where to pick it");
            d = Halo(); d.HasZone = false;
            TestAssert.Eq(CargoManifestRules.Hint(d), "DROPS FOLLOW THE TARGET", "No zone: where the stick goes instead");
            foreach (bool reload in new[] { false, true })
                foreach (int ammo in new[] { 0, 4 })
                    foreach (bool selected in new[] { false, true })
                        foreach (bool safe in new[] { false, true })
                            foreach (bool uses in new[] { false, true })
                                foreach (bool zone in new[] { false, true })
                                    foreach (bool reach in new[] { false, true })
                                    {
                                        CargoScope s = Halo();
                                        s.Reloading = reload; s.Ammo = ammo; s.Selected = selected; s.Safe = safe; s.UsesLandingZone = uses; s.HasZone = zone; s.HasReach = reach;
                                        string hint = CargoManifestRules.Hint(s);
                                        TestAssert.That(!string.IsNullOrEmpty(hint) && hint.Length <= 26 && hint != CargoManifestRules.Status(s), "Every state has a short action that is not its status: " + hint);
                                    }
        }

        private static void WindowAndScale()
        {
            CargoScope d = Halo();
            TestAssert.Near(CargoManifestRules.WindowSeconds(d), 0f, "Inside the reach the window is open now");
            d.DistanceM = 6000f + 1000f; d.ClosingMps = 100f;
            TestAssert.Near(CargoManifestRules.WindowSeconds(d), 10f, "A kilometre short at 100 m/s closing: ten seconds");
            d.ClosingMps = -50f;
            TestAssert.That(float.IsPositiveInfinity(CargoManifestRules.WindowSeconds(d)), "Flying away, the window never comes");
            d.HasZone = false;
            TestAssert.That(float.IsNaN(CargoManifestRules.WindowSeconds(d)), "No zone, no window");

            TestAssert.Near(CargoManifestRules.PlotRangeM(5000f, 6000f), 10000f, "The rim holds the reach ring with room");
            TestAssert.Near(CargoManifestRules.PlotRangeM(80000f, 3000f), 15000f, "A far zone rides the rim: the rim stays within four reaches");
            TestAssert.Near(CargoManifestRules.PlotRangeM(11000f, 7775f), 15000f, "A HALO run 11 km out at 3200 m: a 15 km rim, not 20");
            TestAssert.Near(CargoManifestRules.PlotRangeM(800f, float.NaN), 1000f, "No reach: the zone sets the scale");
            TestAssert.Near(CargoManifestRules.PlotRangeM(0f, 300f), 500f, "A low drop's short reach gets the smallest step");
            TestAssert.Near(CargoManifestRules.PlotRangeM(1e7f, float.NaN), 100000f, "Beyond the last step the rim stays at the last");
            for (float dist = 100f; dist < 200000f; dist *= 1.37f)
                foreach (float reach in new[] { 300f, 2500f, 9000f, 45000f })
                {
                    float rim = CargoManifestRules.PlotRangeM(dist, reach);
                    TestAssert.That(rim >= Math.Min(reach * 1.3f, 100000f) - 0.5f, "The reach ring always fits inside the rim: " + dist + " / " + reach);
                    TestAssert.That(rim <= Math.Max(reach * 4f * 2.5f, 500f) + 0.5f, "The rim never runs far past four reaches: " + dist + " / " + reach);
                }
        }
    }
}
