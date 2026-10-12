using System;
using System.Collections.Generic;
using BoscaliSummer.Core.Contracts;
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
            Badges();
            BindAttachesRunToRunnableRows();
            FormationIsASubmenuOfFamilies();
            PerksGroupByFrontAndCool();
            DefenceNamesTheOutcome();
            ViewRowsKeepTheirPlace();
            TacBoardRowsFollowTheOptions();
            TacBoardOffKeepsItsRows();
            PitKeepsStationIdentityWhileTheListShrinks();
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
            TestAssert.Eq(m.Category("weapons").Badge, "AGM ×2", "badge follows the active station (AGM-48 does not fit beside its ammo, so the class tag is used)");
            TestAssert.Eq(m.Row("weapons/select/st3").Icon, "agm", "an air-to-surface missile wears the AGM glyph");
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

        private static void Badges()
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

            TestAssert.Eq(MenuCatalog.Build(MenuWorlds.Default()).AircraftName, "CI-22 CRICKET", "aircraft name");
        }

        private static void FormationIsASubmenuOfFamilies()
        {
            MenuModel m = MenuCatalog.Build(MenuWorlds.Default());
            MenuRow shape = m.Row("wing/shape");
            TestAssert.That(shape.Kind == MenuKind.Submenu && shape.Label == "FORMATION" && shape.Iconless, "WING FORMATION is an iconless submenu, not a stepper");
            TestAssert.Eq(shape.Children.Count, 22, "all 22 formations are listed");
            TestAssert.Eq(shape.State, "FORMATION: FINGER 4", "the row names the formation flying (the long name falls back to the short one)");
            TestAssert.Eq(shape.HintNoun, "FORMATIONS", "the hint says FORMATIONS");
            var groups = new List<string>();
            foreach (MenuRow k in shape.Children) if (groups.Count == 0 || groups[groups.Count - 1] != k.Group) groups.Add(k.Group);
            TestAssert.Eq(string.Join(",", groups), "CLASSIC,ESCORT,ROTARY,TACTICAL", "four dock groups, each family together");
            MenuRow active = m.Row("wing/shape/finger-four-strong-right");
            TestAssert.That(active.Kind == MenuKind.Picker && active.Selected && active.Label == "FINGER FOUR" && active.State == "ACTIVE · STRONG RIGHT", "the flying shape is the selected pick, its qualifier is the state");
            TestAssert.That(!m.Row("wing/shape/vic").Selected && m.Row("wing/shape/vic").State == "CLASSIC", "an unqualified shape states its family");
            TestAssert.Eq(m.Row("wing/shape/high-cover").ToastText(), "FORMATION: HIGH COVER", "the toast names the formation");
            TestAssert.Eq(m.Category("wing").Badge, "FINGER 4", "the ring badge is the short formation name");

            // families that arrive interleaved still run together
            MenuWorld w = MenuWorlds.Default();
            w.Shapes.Clear();
            w.Shapes.Add(new WorldShape { Family = "CLASSIC", Name = "VIC" });
            w.Shapes.Add(new WorldShape { Family = "ESCORT", Name = "HIGH COVER" });
            w.Shapes.Add(new WorldShape { Family = "CLASSIC", Name = "BOX" });
            var ids = MenuCatalog.Build(w).Row("wing/shape").Children.ConvertAll(r => r.Id);
            TestAssert.Eq(string.Join(",", ids), "wing/shape/vic,wing/shape/box,wing/shape/high-cover", "game order inside a family, no repeated group heading");

            TestAssert.Eq(MenuCatalog.ShortShape("COMBAT SPREAD"), "COMB SPR", "long names are cut word by word");
            TestAssert.Eq(MenuCatalog.ShortShape("FLUID FOUR"), "FLUID 4", "FOUR is a digit");
            foreach (WorldShape s in MenuWorlds.Default().Shapes) TestAssert.That(MenuCatalog.ShortShape(s.Name).Length <= MenuModel.BadgeChars, "short name fits a badge: " + s.Name);
            w = MenuWorlds.Default();
            w.Shapes.Clear(); w.Shape = "";
            TestAssert.That(MenuCatalog.Build(w).Row("wing/shape") == null && MenuCatalog.Build(w).Category("wing").Badge == "WING 3", "no shape list: no row, the badge counts the wing");
        }

        private static void PerksGroupByFrontAndCool()
        {
            MenuModel m = MenuCatalog.Build(MenuWorlds.Default());
            MenuRow perks = m.Row("support/perks");
            TestAssert.Eq(perks.Children.Count, 8, "the dock holds eight perks (IOpsPerks.MaxPerks)");
            var groups = new List<string>();
            foreach (MenuRow k in perks.Children) if (groups.Count == 0 || groups[groups.Count - 1] != k.Group) groups.Add(k.Group);
            TestAssert.Eq(string.Join(",", groups), "SPACE,CYBER,SOF", "perks group by front");
            TestAssert.Eq(m.Row("support/perks/recon-pass").Icon, "front-space", "a SPACE perk wears the SPACE glyph");
            TestAssert.Eq(m.Row("support/perks/elint-sweep").Icon, "front-cyber", "a CYBER perk wears the CYBER glyph");
            TestAssert.Eq(m.Row("support/perks/jtac-lase").Icon, "front-sof", "a SOF perk wears the SOF glyph");
            MenuRow cooling = m.Row("support/perks/prsm");
            TestAssert.That(cooling.IsBlocked && cooling.State == "COOLDOWN: 24 S", "a cooling perk keeps its place, blocked, with the seconds left");
            TestAssert.Eq(perks.State, "6 READY · 1 ARMED", "READY counts neither the armed nor the cooling perks");
            MenuWorld w = MenuWorlds.Default();
            w.Perks[1].Note = "ARMED · PRESS AGAIN";
            TestAssert.Eq(MenuCatalog.Build(w).Row("support/perks/sat-camera").Group, "SPACE", "the group comes from the front, not from the note that flips when armed");
            w.Perks.Clear();
            TestAssert.That(MenuCatalog.Build(w).Row("support/perks") == null, "no perks, no submenu");
        }

        private static void DefenceNamesTheOutcome()
        {
            MenuModel m = MenuCatalog.Build(MenuWorlds.Default());
            MenuRow cm = m.Row("defence/cm");
            TestAssert.Eq(cm.Label, "SWITCH TO RADAR JAMMER", "the label names where a press leads");
            TestAssert.Eq(cm.State, "ACTIVE: IR FLARES", "the state names what is active");
            TestAssert.Eq(cm.Desc, "IR FLARES → RADAR JAMMER", "the hub shows the switch");
            TestAssert.Eq(cm.ToastText(), "COUNTERMEASURE: RADAR JAMMER", "the toast names the new one");
            TestAssert.Eq(m.Row("defence/drones").State, "DRONES: 2 READY", "the drone count");
            TestAssert.Eq(m.Category("defence").Badge, "FLR 48", "flares in the first station: the flare count");
            m = MenuCatalog.Build(MenuWorlds.Jammer());
            TestAssert.That(m.Category("defence").Badge == "JAMMER" && m.Category("defence").BadgeShow, "another countermeasure active: the badge names it");
            TestAssert.Eq(m.Row("defence/cm").Label, "SWITCH TO IR FLARES", "and the row leads back");
            MenuWorld w = MenuWorlds.Default();
            w.NextCountermeasure = "";
            TestAssert.That(MenuCatalog.Build(w).Row("defence/cm") == null, "one countermeasure: nothing to switch to");
            w.Drones = 0;
            TestAssert.Eq(MenuCatalog.Build(w).Row("defence/drones").State, "DRONES: NONE READY", "no drone aloft");
        }

        private static void ViewRowsKeepTheirPlace()
        {
            MenuWorld w = MenuWorlds.Default();
            w.CanEnterMissileView = false; w.Missiles = 0;
            MenuRow view = MenuCatalog.Build(w).Row("view/mslview");
            TestAssert.That(view != null && view.IsBlocked && view.Blocked == "NO MISSILE IN FLIGHT", "no missile in flight is transient: the row stays, dimmed");
            w = MenuWorlds.Default();
            w.Camera = ViewKind.Orbit;
            TestAssert.Eq(MenuCatalog.Build(w).Category("view").Badge, "EXTERNAL", "the external camera outranks the missile count");
            MenuModel m = MenuCatalog.Build(MenuWorlds.Missile());
            TestAssert.Eq(m.Row("view/mslnext").State, "NEXT: MSL 2/2", "the next missile is named");
            TestAssert.Eq(m.Row("view/mslprev").State, "PREV: MSL 2/2", "and the previous one");
            MenuWorld two = MenuWorlds.Missile();
            two.MissileCurrent = "MSL 2/3 AAM-4";
            m = MenuCatalog.Build(two);
            TestAssert.That(m.Row("view/mslnext").State == "NEXT: MSL 3/3" && m.Row("view/mslprev").State == "PREV: MSL 1/3", "the steps wrap around the list");
            TestAssert.Eq(MenuCatalog.Build(MenuWorlds.Default()).Row("targets/filters/sam-sites").State, "SLOT 2", "a filter names its quick slot");
        }

        private static void TacBoardRowsFollowTheOptions()
        {
            MenuModel m = MenuCatalog.Build(MenuWorlds.Default());
            string[] ids = m.Category("view").Rows.ConvertAll(r => r.Id).ToArray();
            TestAssert.Eq(string.Join(" ", ids), "view/cam view/map view/mslview view/board view/declutter view/boardlayout", "the board rows follow the camera rows and keep their order");

            MenuRow board = m.Row("view/board");
            TestAssert.That(board.Kind == MenuKind.Toggle && board.IsOn && !board.Checkbox, "TAC BOARD is a toggle with distinct verbs, not a checkbox");
            TestAssert.Eq(board.Label, "HIDE TAC BOARD", "a shown board offers HIDE");
            TestAssert.Eq(board.State, "CURRENT: ON", "the toggle's state line says what is current");
            TestAssert.Eq(m.Row("view/declutter").Label, "SET HUD DECLUTTER LEAN", "FULL steps to LEAN");
            TestAssert.Eq(m.Row("view/declutter").State, "HUD DECLUTTER: FULL", "the declutter state line names the level");
            TestAssert.Eq(m.Row("view/declutter").ToastText(), "HUD DECLUTTER: LEAN", "and the toast names where the press led");
            TestAssert.Eq(m.Row("view/boardlayout").Label, "SET BOARD LAYOUT STACK", "WIDE steps to STACK");
            TestAssert.Eq(m.Row("view/boardlayout").State, "BOARD LAYOUT: WIDE", "the layout state line names the layout");

            // every declutter level and layout is reachable and the cycle wraps
            string[] levels = { "FULL", "LEAN", "MIN" };
            for (int i = 0; i < levels.Length; i++)
            {
                MenuWorld w = MenuWorlds.Default();
                w.BoardDeclutter = i;
                MenuRow row = MenuCatalog.Build(w).Row("view/declutter");
                TestAssert.Eq(row.State, "HUD DECLUTTER: " + levels[i], "declutter shows " + levels[i]);
                TestAssert.Eq(row.NextValue, levels[(i + 1) % levels.Length], "declutter " + levels[i] + " steps to " + levels[(i + 1) % levels.Length]);
            }
            MenuWorld stack = MenuWorlds.Default();
            stack.BoardLayout = 1;
            MenuRow layout = MenuCatalog.Build(stack).Row("view/boardlayout");
            TestAssert.That(layout.State == "BOARD LAYOUT: STACK" && layout.NextValue == "WIDE", "STACK steps back to WIDE");
            stack.BoardLayout = 9; stack.BoardDeclutter = -3;
            MenuModel clamped = MenuCatalog.Build(stack);
            TestAssert.That(clamped.Row("view/boardlayout").State == "BOARD LAYOUT: STACK" && clamped.Row("view/declutter").State == "HUD DECLUTTER: FULL",
                "an index outside the lists is held inside them");

            // the cycle order is the contract enums' order: the binding steps the enum by index
            TestAssert.Eq(MenuCatalog.BoardDeclutters.Length, Enum.GetValues(typeof(BoardDeclutter)).Length, "one declutter step per BoardDeclutter value");
            TestAssert.Eq(MenuCatalog.BoardLayouts.Length, Enum.GetValues(typeof(BoardLayout)).Length, "one layout step per BoardLayout value");
            string[] declutterNames = Enum.GetNames(typeof(BoardDeclutter)), layoutNames = Enum.GetNames(typeof(BoardLayout));
            for (int i = 0; i < declutterNames.Length; i++)
                TestAssert.That(declutterNames[i].ToUpperInvariant().StartsWith(MenuCatalog.BoardDeclutters[i], StringComparison.Ordinal), "declutter step " + i + " is " + declutterNames[i]);
            for (int i = 0; i < layoutNames.Length; i++)
                TestAssert.Eq(layoutNames[i].ToUpperInvariant(), MenuCatalog.BoardLayouts[i], "layout step " + i);
        }

        private static void TacBoardOffKeepsItsRows()
        {
            MenuModel m = MenuCatalog.Build(MenuWorlds.BoardOff());
            MenuRow board = m.Row("view/board");
            TestAssert.That(!board.IsOn && board.Label == "SHOW TAC BOARD" && board.State == "CURRENT: OFF", "a hidden board offers SHOW, state CURRENT: OFF");
            foreach (string id in new[] { "view/declutter", "view/boardlayout" })
            {
                MenuRow row = m.Row(id);
                TestAssert.That(row != null && row.IsBlocked && row.Blocked == "TAC BOARD: OFF" && row.State == "TAC BOARD: OFF", id + " stays, dimmed, with its reason");
            }
            TestAssert.That(m.Row("view/declutter").Value == "MIN" && m.Row("view/boardlayout").Value == "STACK", "a hidden board keeps the look the pilot left it on");

            // opened with the board off, turned on while the menu is open: the look rows come alive in place
            MenuModel live = MenuCatalog.Build(MenuWorlds.BoardOff());
            MenuWorld on = MenuWorlds.BoardOff();
            on.BoardOn = true;
            live.Reconcile(MenuCatalog.Build(on));
            TestAssert.That(live.Row("view/board").IsOn && !live.Row("view/declutter").IsBlocked && !live.Row("view/boardlayout").IsBlocked, "switching the board on frees the look rows");
            // and the other way: turned off while open, the rows stay in their place, dimmed
            live = MenuCatalog.Build(MenuWorlds.Default());
            live.Reconcile(MenuCatalog.Build(MenuWorlds.BoardOff()));
            TestAssert.That(!live.Row("view/board").IsOn && live.Row("view/declutter").Blocked == "TAC BOARD: OFF" && !live.Row("view/declutter").Gone, "switching the board off dims the look rows, never drops them");

            // no options contract: no board rows; while a missile is followed VIEW lists only the missile steppers
            MenuWorld none = MenuWorlds.Default();
            none.BoardAvailable = false;
            MenuModel without = MenuCatalog.Build(none);
            TestAssert.That(without.Row("view/board") == null && without.Row("view/declutter") == null && without.Row("view/boardlayout") == null, "no TAC BOARD options installed: no board rows");
            TestAssert.That(without.Row("view/cam") != null && without.Row("view/map") != null, "the camera rows are unaffected");
            TestAssert.That(MenuCatalog.Build(MenuWorlds.Missile()).Row("view/board") == null, "the missile view lists no board rows");
        }

        private static void PitKeepsStationIdentityWhileTheListShrinks()
        {
            MenuWorld world = MenuWorlds.Pit();
            MenuModel live = MenuCatalog.Build(world);
            TestAssert.That(live.Row("support/pit/mun").Selected && !live.Row("support/pit/fuel").Selected, "the host service order is shown");
            TestAssert.Eq(live.Row("support/pit/station/9").Index, 9, "priority addresses the real pylon, not its list position");
            world.PitStations.RemoveAt(0);
            live.Reconcile(MenuCatalog.Build(world));
            TestAssert.That(live.Row("support/pit/station/4").Gone, "a station that filled is unavailable until the menu closes");
            TestAssert.Eq(live.Row("support/pit/station/9").Index, 9, "the surviving station never changes its target index");
            world.PitAvailable = false;
            live.Reconcile(MenuCatalog.Build(world));
            TestAssert.That(live.Row("support/pit").Gone, "leaving service range makes the branch unavailable");
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
