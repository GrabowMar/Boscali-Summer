using System;
using System.Collections.Generic;
using BoscaliSummer.Modules.Autopilot.Domain;

namespace BoscaliSummer.Tests.Features.Autopilot
{
    /// <summary>The catalog as a pure function of the world: copy rules, flips, hybrid availability, derived state lines, badges.</summary>
    internal static class MenuCatalogTests
    {
        public static void Run()
        {
            RingIsFixed();
            CopyRulesHoldInEveryScenario();
            IdsAreUniqueAndStable();
            LabelsFlipWithLiveState();
            HybridAvailability();
            DerivedStateLines();
            CommsIsOneGroupedList();
            BadgesAndSummary();
            BindAttachesRunToRunnableRows();
        }

        internal static IEnumerable<MenuRow> AllRows(MenuModel model)
        {
            foreach (MenuCategory c in model.Categories)
                foreach (MenuRow r in c.Rows)
                {
                    yield return r;
                    if (r.Children != null) foreach (MenuRow k in r.Children) yield return k;
                }
        }

        private static void RingIsFixed()
        {
            string[] ids = { "weapons", "lights", "flight", "wing", "comms", "radio", "support", "view", "defence", "targets" };
            float[] angles = { 18, 54, 90, 126, 162, 198, 234, 270, 306, 342 };
            string[] keys = { "1", "2", "3", "4", "5", "6", "7", "8", "9", "0" };
            foreach (var scenario in MenuWorlds.All())
            {
                MenuModel m = MenuCatalog.Build(scenario.Value);
                TestAssert.Eq(m.Categories.Count, 10, scenario.Key + ": ten categories");
                for (int i = 0; i < 10; i++)
                {
                    TestAssert.Eq(m.Categories[i].Id, ids[i], scenario.Key + ": category order never changes");
                    TestAssert.Near(m.Categories[i].Angle, angles[i], scenario.Key + ": category angle never changes");
                    TestAssert.Eq(m.Categories[i].Key, keys[i], scenario.Key + ": ring key-cap");
                }
            }
        }

        private static void CopyRulesHoldInEveryScenario()
        {
            foreach (var scenario in MenuWorlds.All())
            {
                MenuModel m = MenuCatalog.Build(scenario.Value);
                foreach (MenuCategory c in m.Categories)
                {
                    TestAssert.That(c.Badge.Length <= MenuModel.BadgeChars, scenario.Key + ": badge too long: " + c.Badge);
                    TestAssert.That(c.Context.Length <= MenuCatalog.MaxState, scenario.Key + ": context too long: " + c.Context);
                }
                foreach (MenuRow r in AllRows(m))
                {
                    string where = scenario.Key + " " + r.Id + ": ";
                    foreach (string text in new[] { r.Label, r.State, r.Desc, r.ToastText() })
                        TestAssert.That(text == text.ToUpperInvariant(), where + "copy is upper case: " + text);
                    TestAssert.That(r.Label.Length <= MenuCatalog.MaxLabel, where + "label too long: " + r.Label);
                    TestAssert.That(r.State.Length <= MenuCatalog.MaxState, where + "state too long: " + r.State);
                    TestAssert.That(r.Desc.Length <= MenuCatalog.MaxDesc, where + "description too long: " + r.Desc);
                    TestAssert.That(!r.Desc.Contains(" · "), where + "description must not contain a middle dot");
                    if (r.Kind == MenuKind.Toggle)
                    {
                        foreach (MenuFace f in new[] { r.Face, r.OnFace })
                        {
                            TestAssert.That(f.Label.Length <= MenuCatalog.MaxLabel && f.State.Length <= MenuCatalog.MaxState && f.Desc.Length <= MenuCatalog.MaxDesc,
                                where + "both faces obey the copy limits: " + f.Label);
                            TestAssert.That(f.Label == f.Label.ToUpperInvariant() && f.State == f.State.ToUpperInvariant(), where + "both faces are upper case");
                        }
                    }
                }
            }
        }

        private static void IdsAreUniqueAndStable()
        {
            foreach (var scenario in MenuWorlds.All())
            {
                MenuModel a = MenuCatalog.Build(scenario.Value), b = MenuCatalog.Build(scenario.Value);
                var seen = new HashSet<string>();
                var order = new List<string>();
                foreach (MenuCategory c in a.Categories)
                    foreach (MenuRow r in AllRows(new MenuModel { Categories = { c } }))
                    {
                        TestAssert.That(seen.Add(r.Id), scenario.Key + ": duplicate id " + r.Id);
                        TestAssert.That(r.Id.StartsWith(c.Id + "/", StringComparison.Ordinal), scenario.Key + ": id carries its category " + r.Id);
                        order.Add(r.Id);
                    }
                int k = 0;
                foreach (MenuRow r in AllRows(b)) TestAssert.Eq(r.Id, order[k++], scenario.Key + ": a rebuild yields the same ids in the same order");
            }
        }

        private static void LabelsFlipWithLiveState()
        {
            MenuWorld w = MenuWorlds.Default();
            MenuRow gear = MenuCatalog.Build(w).Row("flight/gear");
            TestAssert.Eq(gear.Label, "LOWER GEAR", "gear up offers LOWER GEAR");
            TestAssert.Eq(gear.State, "CURRENT: UP", "gear up state line");
            w.Gear = GearPhase.Down;
            gear = MenuCatalog.Build(w).Row("flight/gear");
            TestAssert.Eq(gear.Label, "RAISE GEAR", "gear down offers RAISE GEAR");
            w.Gear = GearPhase.Extending;
            MenuModel moving = MenuCatalog.Build(w);
            gear = moving.Row("flight/gear");
            TestAssert.That(gear.IsBlocked && gear.State == "GEAR: EXTENDING", "a moving gear keeps its row, blocked, with the reason");
            TestAssert.Eq(moving.Category("flight").Rows[0].Id, "flight/gear", "and keeps its place at the top");
            TestAssert.That(moving.Summary.Exists(s => s.Key == "GEAR" && s.Value == "MOVING" && s.Caution), "the hub summary says GEAR MOVING");

            w = MenuWorlds.Default();
            TestAssert.Eq(MenuCatalog.Build(w).Row("lights/light").Label, "SET LANDING LIGHT OFF", "AUTO steps to OFF");
            w.LandingLight = 0;
            MenuRow light = MenuCatalog.Build(w).Row("lights/light");
            TestAssert.Eq(light.Label, "SET LANDING LIGHT ON", "OFF steps to ON");
            TestAssert.Eq(light.State, "LANDING LIGHT: OFF", "state names the current value");
            TestAssert.Eq(MenuCatalog.Build(MenuWorlds.Default()).Row("lights/nav").Label, "TURN NAV LIGHTS OFF", "nav lights cannot read as their own state");
            TestAssert.Eq(MenuCatalog.Build(MenuWorlds.Default()).Row("defence/radar").Label, "GO SILENT", "radar emitting offers GO SILENT");
            TestAssert.That(!MenuCatalog.Build(MenuWorlds.Default()).Row("defence/radar").Checkbox, "GO SILENT / RADIATE is a distinct-verb pair: no checkbox");
            TestAssert.That(MenuCatalog.Build(MenuWorlds.Default()).Row("flight/assist").Checkbox, "ENABLE / DISABLE is an on/off flip: checkbox");
        }

        private static void HybridAvailability()
        {
            MenuModel noWing = MenuCatalog.Build(MenuWorlds.NoWing());
            MenuCategory wing = noWing.Category("wing");
            TestAssert.That(!wing.Available && wing.Rows.Count == 0, "no wingmen: the WING category dims in place");
            TestAssert.That(wing.Badge == "NO WING" && wing.BadgeShow, "with a reason badge");
            TestAssert.Eq(wing.EmptyTitle, "NO WINGMEN", "and an explanation");
            TestAssert.Eq(noWing.Categories[3].Id, "wing", "and keeps its slot");
            TestAssert.That(!noWing.Summary.Exists(s => s.Key == "WING"), "the hub summary drops the WING figure");

            MenuModel ground = MenuCatalog.Build(MenuWorlds.Ground());
            TestAssert.That(ground.Row("flight/gear") == null && ground.Row("flight/autoland") == null && ground.Row("flight/eject") == null && ground.Row("defence/flares") == null,
                "on the apron gear, autoland, eject and flares are absent");
            MenuRow engine = ground.Row("flight/engine");
            TestAssert.That(engine.Label == "START ENGINE" && !engine.NeedsHold, "a shut-down engine on the ground starts with a plain press");
            TestAssert.Eq(ground.Category("flight").Badge, "GROUND", "ground badge");
            string[] expect = { "flight/assist", "flight/hover", "flight/engine" };
            var ids = ground.Category("flight").Rows.ConvertAll(r => r.Id);
            TestAssert.That(ids.Count == 3 && ids[0] == expect[0] && ids[1] == expect[1] && ids[2] == expect[2], "the order of what remains is kept");

            MenuModel air = MenuCatalog.Build(MenuWorlds.Default());
            TestAssert.That(air.Row("flight/engine").NeedsHold && air.Row("flight/engine").HoldLabel == "HOLD TO STOP", "stopping the engine in flight needs the hold");
            TestAssert.That(air.Row("flight/eject").NeedsHold && air.Row("flight/eject").HoldLabel == "HOLD TO EJECT", "eject needs the hold");
            TestAssert.Eq(air.Category("flight").Rows[air.Category("flight").Rows.Count - 1].Id, "flight/eject", "EJECT is the last row, below the rule");

            MenuWorld w = MenuWorlds.Default();
            w.Targets = 0; w.NearestKm = -1f;
            MenuRow keep = MenuCatalog.Build(w).Row("targets/keep");
            TestAssert.That(keep.IsBlocked && keep.Blocked == "NO TARGETS", "a transient absence keeps the row and says why");

            MenuModel ibis = MenuCatalog.Build(MenuWorlds.Ibis());
            TestAssert.That(ibis.Row("flight/assist") == null && ibis.Row("support/exfil") != null, "airframe differences hide and show rows permanently");
        }

        private static void DerivedStateLines()
        {
            MenuWorld w = MenuWorlds.Default();
            MenuModel m = MenuCatalog.Build(w);
            TestAssert.Eq(m.Row("weapons/select").State, "ACTIVE: AAM-4 SKYWAY ×2", "SELECT STATION shows the active station");
            TestAssert.Eq(m.Row("weapons/next").State, "NEXT: STA 3 · AGM-48 ×2", "NEXT STATION previews STA 3");
            TestAssert.Eq(m.Row("weapons/prev").State, "PREV: STA 1 · 12.7MM GUN ×1K", "PREV STATION previews STA 1");
            w.ActiveStation = 2;
            m = MenuCatalog.Build(w);
            TestAssert.Eq(m.Category("weapons").Badge, "MSL ×2", "badge follows the active station (AGM-48 does not fit beside its ammo, so the class tag is used)");
            TestAssert.Eq(m.Row("weapons/next").State, "NEXT: STA 4 · CBU-12 ×0", "NEXT follows the pick");
            TestAssert.That(m.Row("weapons/select/st3").Selected && !m.Row("weapons/select/st2").Selected, "the picker marks the active station");
            TestAssert.That(m.Row("weapons/select/st4").Hatched && m.Row("weapons/select/st4").ToneNow == MenuTone.Dim, "an empty station is hatched and dim, not amber");

            m = MenuCatalog.Build(MenuWorlds.Default());
            TestAssert.Eq(m.Row("support/perks").State, "6 READY · 1 ARMED", "OPS PERKS counts ready and armed");
            TestAssert.Eq(m.Row("support/callmark").State, "ARMED: SAT CAMERA", "CALL AT MARK names the armed support");
            TestAssert.Eq(m.Row("support/mark").State, "MARK: SET · 42 S AGO", "mark state");
            TestAssert.Eq(m.Row("support/clearmark").State, m.Row("support/mark").State, "mark and clear mark agree");
            TestAssert.Eq(m.Row("targets/filters").State, "ACTIVE: AIR THREATS", "TARGET FILTERS names the active filter");
            TestAssert.Eq(m.Row("targets/keep").State, "SELECTED: 2 · NEAREST 4.2 KM", "KEEP NEAREST shows the distance");
            TestAssert.That(m.Row("support/perks/sat-camera").IsOn && m.Row("support/perks/sat-camera").Label == "EXECUTE SAT CAMERA", "an armed perk offers EXECUTE");
            TestAssert.Eq(m.Row("support/perks/recon-pass").Label, "ARM RECON PASS", "a ready perk offers ARM");

            w = MenuWorlds.Default();
            w.Armed = ""; w.CanCallAtMark = false;
            m = MenuCatalog.Build(w);
            TestAssert.That(m.Row("support/callmark").State == "NOTHING ARMED" && m.Row("support/callmark").Blocked == "NOTHING ARMED", "nothing armed warns and blocks");
            w.HasMark = false; w.CanCapture = true;
            m = MenuCatalog.Build(w);
            TestAssert.That(m.Row("support/clearmark").Blocked == "MARK: NONE" && m.Row("support/mark").State == "MARK: NONE", "no mark: clear is blocked");
        }

        private static void CommsIsOneGroupedList()
        {
            MenuCategory comms = MenuCatalog.Build(MenuWorlds.Default()).Category("comms");
            TestAssert.Eq(comms.Rows.Count, 12, "twelve calls in one flat list");
            TestAssert.That(comms.Iconless, "the list has no icon column");
            var groups = new List<string>();
            foreach (MenuRow r in comms.Rows) if (groups.Count == 0 || groups[groups.Count - 1] != r.Group) groups.Add(r.Group);
            TestAssert.Eq(string.Join(",", groups), "TACTICAL,REPLY,STATUS", "grouped TACTICAL / REPLY / STATUS, in that order");
            TestAssert.Eq(comms.Rows[0].Label, "NEED SUPPORT", "the first window starts with the tactical calls");
            TestAssert.Eq(comms.Rows[0].Verb, "SEND", "calls are sent");
            int pings = 0;
            foreach (MenuRow r in comms.Rows) if (r.Ping != null) pings++;
            TestAssert.Eq(pings, 4, "four calls drop a map ping");
            TestAssert.That(comms.Rows[0].Index == 1, "a row remembers the provider index of its call (NEED SUPPORT is call 1)");
            TestAssert.Eq(comms.Rows[0].Id, "comms/cneed-support", "ids come from the label so a reordered provider cannot shuffle them");
            MenuWorld w = MenuWorlds.Default();
            w.CallLabels.Add("BRAND NEW CALL");
            MenuCategory more = MenuCatalog.Build(w).Category("comms");
            TestAssert.That(more.Rows[more.Rows.Count - 1].Group == "MORE", "a call the table does not know lands in MORE");
        }

        private static void BadgesAndSummary()
        {
            MenuModel m = MenuCatalog.Build(MenuWorlds.Default());
            TestAssert.Eq(m.Category("weapons").Badge, "AAM-4 ×2", "weapons badge");
            TestAssert.Eq(m.Category("lights").Badge, "NAV ON", "lights badge at rest");
            TestAssert.Eq(m.Category("flight").Badge, "ASSIST", "flight badge");
            TestAssert.Eq(m.Category("comms").Badge, "NET OPEN", "comms badge");
            TestAssert.Eq(m.Category("view").Badge, "2 MSL", "VIEW shows missiles in flight");
            TestAssert.Eq(m.Category("defence").Badge, "FLR 48", "defence badge");
            TestAssert.Eq(m.Category("targets").Badge, "2 SEL", "targets badge");
            TestAssert.Eq(m.Category("support").Badge, "1 ARMED", "support badge when a perk is armed");
            TestAssert.That(m.Category("support").BadgeShow && m.Category("support").BadgeAlert, "armed support is worth the second line, in amber");
            TestAssert.Eq(m.Category("radio").Badge, "BOSCALI", "radio badge is the station");

            MenuWorld w = MenuWorlds.Default();
            w.Assist = false; w.NavOn = false;
            m = MenuCatalog.Build(w);
            TestAssert.That(m.Category("flight").Badge == "FA OFF" && m.Category("flight").BadgeAlert, "assist off is an amber FA OFF");
            TestAssert.That(m.Category("lights").Badge == "NAV OFF" && m.Category("lights").BadgeShow, "nav off shows at rest");

            m = MenuCatalog.Build(MenuWorlds.Ibis());
            TestAssert.Eq(m.Category("flight").Badge, "HOVER ON", "a helicopter in hover");
            TestAssert.Eq(m.Category("weapons").Badge, "GUN ×500", "door guns badge keeps the ammo");

            w = MenuWorlds.Default();
            w.Fuel = 0.72f;
            TestAssert.Eq(MenuCatalog.Build(w).Summary[0].Value, "72%", "the centre line reads the live fuel level");
            w.Fuel = 0.1f;
            TestAssert.That(MenuCatalog.Build(w).Summary[0].Caution, "low fuel is a caution");
            TestAssert.Eq(MenuCatalog.Build(MenuWorlds.Default()).AircraftName, "CI-22 CRICKET", "aircraft name");
        }

        private static void BindAttachesRunToRunnableRows()
        {
            var bound = new List<string>();
            MenuModel m = MenuCatalog.Build(MenuWorlds.Default(), row => { bound.Add(row.Id); return row.Kind == MenuKind.Submenu ? null : (Action)(() => { }); });
            TestAssert.That(bound.Contains("weapons/select/st1") && bound.Contains("targets/filters/air-threats"), "children are bound too");
            TestAssert.That(m.Row("weapons/select").Run == null, "a submenu has no game call");
            foreach (MenuRow r in AllRows(m))
                if (r.Kind != MenuKind.Submenu) TestAssert.That(r.Run != null, r.Id + " is runnable");
        }
    }
}
