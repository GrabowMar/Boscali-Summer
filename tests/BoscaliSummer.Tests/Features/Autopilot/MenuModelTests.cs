using System;
using System.Collections.Generic;
using System.Reflection;
using BoscaliSummer.Modules.Autopilot.Domain;

namespace BoscaliSummer.Tests.Features.Autopilot
{
    /// <summary>The v7 menu model: faces that flip, cycles that name their outcome, rebuilds that never re-index.</summary>
    internal static class MenuModelTests
    {
        public static void Run()
        {
            ToggleFlipsEveryWordWithItsState();
            CycleNamesTheOutcomeAndWraps();
            PickerStateFollowsSelection();
            BlockedRowKeepsItsPlaceAndExplainsItself();
            WhichRowsStayOpen();
            ReconcileKeepsOrderAndIndices();
            ReconcileNeverAddsRowsAndKeepsAnUnavailableCategoryUnavailable();
            ReconcileReachesChildren();
            ReconcileCopiesEveryLiveField();
        }

        internal static MenuRow Gear(bool on) => MenuRow.Toggle("flight/gear", "gear", on,
            new MenuFace("LOWER GEAR", "CURRENT: UP", "DEPLOY LANDING GEAR", "GEAR EXTENDING"),
            new MenuFace("RAISE GEAR", "CURRENT: DOWN", "RETRACT LANDING GEAR", "GEAR RETRACTING"), checkbox: false);

        internal static MenuRow Light(int index) => MenuRow.Cycle("lights/light", "landing-light", "SET LANDING LIGHT", "LANDING LIGHT: ",
            new[] { "OFF", "ON", "AUTO" }, index);

        private static void ToggleFlipsEveryWordWithItsState()
        {
            MenuRow gear = Gear(false);
            TestAssert.Eq(gear.Label, "LOWER GEAR", "off face labels the outcome");
            TestAssert.Eq(gear.State, "CURRENT: UP", "off face state is NOUN: VALUE");
            TestAssert.Eq(gear.Desc, "DEPLOY LANDING GEAR", "off face description");
            TestAssert.Eq(gear.ToastText(), "GEAR EXTENDING", "the toast describes the press that is about to happen");
            gear.IsOn = true;
            TestAssert.Eq(gear.Label, "RAISE GEAR", "on face labels the opposite outcome");
            TestAssert.Eq(gear.State, "CURRENT: DOWN", "on face state");
            TestAssert.Eq(gear.ToastText(), "GEAR RETRACTING", "on face toast");
            TestAssert.That(!gear.Checkbox, "a distinct-verb pair draws no checkbox");
            gear.IconOn = "autoland-cancel";
            TestAssert.Eq(gear.IconNow, "autoland-cancel", "icon flips while on when one is given");
            gear.IsOn = false;
            TestAssert.Eq(gear.IconNow, "gear", "icon flips back");
        }

        private static void CycleNamesTheOutcomeAndWraps()
        {
            MenuRow light = Light(2);
            TestAssert.Eq(light.Label, "SET LANDING LIGHT OFF", "cycle label names the NEXT value (AUTO wraps to OFF)");
            TestAssert.Eq(light.State, "LANDING LIGHT: AUTO", "cycle state shows the current value");
            TestAssert.Eq(light.Desc, "AUTO → OFF", "hub description shows current to next");
            TestAssert.Eq(light.ToastText(), "LANDING LIGHT: OFF", "toast shows the value the press produces");
            light.ValueIndex = 0;
            TestAssert.Eq(light.Label, "SET LANDING LIGHT ON", "OFF steps to ON");
            TestAssert.That(ReferenceEquals(light.Label, light.Label) && ReferenceEquals(light.State, light.State) && ReferenceEquals(light.Desc, light.Desc),
                "a cycle row builds its text once per value, not on every read (the painter reads it every frame)");
        }

        private static void PickerStateFollowsSelection()
        {
            MenuRow sta = MenuRow.Pick("weapons/select/st2", "aam", "STA 2 · AAM-4 SKYWAY", "AMMO: ×2", "ACTIVE · ×2", false,
                "MAKE THIS THE ACTIVE WEAPON", "STA 2 · AAM-4 SKYWAY");
            TestAssert.Eq(sta.State, "AMMO: ×2", "unselected picker shows its own state");
            sta.Selected = true;
            TestAssert.Eq(sta.State, "ACTIVE · ×2", "selected picker shows the ACTIVE line");
        }

        private static void BlockedRowKeepsItsPlaceAndExplainsItself()
        {
            MenuRow gear = Gear(true);
            gear.Blocked = "GEAR: EXTENDING";
            TestAssert.Eq(gear.State, "GEAR: EXTENDING", "a blocked row shows why");
            TestAssert.Eq(gear.ToneNow, MenuTone.Dim, "a blocked row is dim, not a hazard");
            TestAssert.That(gear.IsBlocked, "blocked rows are unselectable");
            gear.Blocked = null;
            gear.Gone = true;
            TestAssert.Eq(gear.State, MenuRow.GoneText, "a vanished row reads UNAVAILABLE");
            TestAssert.That(gear.IsBlocked, "a vanished row is unselectable");
        }

        private static void WhichRowsStayOpen()
        {
            MenuRow eject = MenuRow.Danger("flight/eject", "eject", "warning", "EJECT", "CONFIRM REQUIRED", "EJECT NOW, NO UNDO", "HOLD TO EJECT", "EJECTING");
            TestAssert.That(eject.NeedsHold, "danger rows need the hold");
        }

        private static MenuModel Model(params MenuRow[] rows)
        {
            var model = new MenuModel { AircraftName = "A" };
            var cat = new MenuCategory("flight", "FLIGHT", 90f, "3", "flight");
            cat.Rows.AddRange(rows);
            model.Categories.Add(cat);
            return model;
        }

        private static MenuRow Act(string id, string state) => MenuRow.Action("flight/" + id, "gear", id.ToUpperInvariant(), state, "d");

        private static void ReconcileKeepsOrderAndIndices()
        {
            MenuModel live = Model(Act("a", "one"), Act("b", "two"), Act("c", "three"));
            MenuModel fresh = Model(Act("a", "ONE!"), Act("c", "THREE!"));
            fresh.AircraftName = "B";
            live.Reconcile(fresh);
            List<MenuRow> rows = live.Categories[0].Rows;
            TestAssert.Eq(rows.Count, 3, "a rebuild never removes a row");
            TestAssert.Eq(rows[0].Id + rows[1].Id + rows[2].Id, "flight/aflight/bflight/c", "a rebuild never reorders");
            TestAssert.Eq(rows[0].State, "ONE!", "live text follows the rebuild");
            TestAssert.That(rows[1].Gone && rows[1].IsBlocked, "the row the rebuild dropped stays, dimmed");
            TestAssert.Eq(rows[2].State, "THREE!", "later rows keep their slot and still update");
            TestAssert.Eq(live.AircraftName, "B", "hub text follows the rebuild");
            live.Reconcile(Model(Act("a", "x"), Act("b", "BACK"), Act("c", "y")));
            TestAssert.That(!rows[1].Gone && rows[1].State == "BACK", "a row that returns is live again in its old slot");
        }

        private static void ReconcileNeverAddsRowsAndKeepsAnUnavailableCategoryUnavailable()
        {
            MenuModel live = Model(Act("a", "one"));
            live.Reconcile(Model(Act("a", "one"), Act("new", "late")));
            TestAssert.Eq(live.Categories[0].Rows.Count, 1, "a row offered mid-session waits for the next open");

            var empty = Model();
            empty.Categories[0].Available = false;
            empty.Categories[0].Reason = "NO WING";
            MenuModel wing = Model(Act("form", "x"));
            wing.Categories[0].Available = true;
            empty.Reconcile(wing);
            TestAssert.That(!empty.Categories[0].Available, "a category built with no rows cannot become available mid-session");

            MenuModel open = Model(Act("a", "one"));
            MenuModel gone = Model(Act("a", "one"));
            gone.Categories[0].Available = false;
            gone.Categories[0].Reason = "NO WING";
            open.Reconcile(gone);
            TestAssert.That(!open.Categories[0].Available && open.Categories[0].Reason == "NO WING", "a category can lose availability and says why");
        }

        private static void ReconcileReachesChildren()
        {
            MenuRow Station(string id, bool selected) => MenuRow.Pick("weapons/select/" + id, "gun", id, "s", "ACTIVE", selected, "d", "t");
            MenuRow Sub(bool second) => MenuRow.Submenu("weapons/select", "station-select", "SELECT STATION", "ACTIVE", "d", "ctx", "STATIONS",
                new List<MenuRow> { Station("st1", !second), Station("st2", second) });
            var live = new MenuModel();
            var cat = new MenuCategory("weapons", "WEAPONS", 18f, "1", "weapons");
            cat.Rows.Add(Sub(false));
            live.Categories.Add(cat);
            var fresh = new MenuModel();
            var fc = new MenuCategory("weapons", "WEAPONS", 18f, "1", "weapons");
            fc.Rows.Add(Sub(true));
            fresh.Categories.Add(fc);
            live.Reconcile(fresh);
            TestAssert.That(live.Row("weapons/select/st2").Selected && !live.Row("weapons/select/st1").Selected, "children follow the rebuild");
            TestAssert.That(live.Row("nope") == null, "unknown ids resolve to null");
        }

        private static void ReconcileCopiesEveryLiveField()
        {
            var excluded = new HashSet<string> { "Group", "Children" };
            foreach (MenuKind kind in new[] { MenuKind.Toggle, MenuKind.Cycle, MenuKind.Picker })
            {
                MenuRow old = kind == MenuKind.Toggle ? Gear(false) : kind == MenuKind.Cycle ? Light(0)
                    : MenuRow.Pick("x/p", "gun", "P", "s", "S", false, "d", "t");
                MenuRow fresh = kind == MenuKind.Toggle ? Gear(false) : kind == MenuKind.Cycle ? Light(0)
                    : MenuRow.Pick("x/p", "gun", "P", "s", "S", false, "d", "t");
                foreach (FieldInfo f in typeof(MenuRow).GetFields(BindingFlags.Public | BindingFlags.Instance))
                {
                    if (excluded.Contains(f.Name)) continue;
                    object value = f.FieldType == typeof(string) ? "v-" + f.Name
                        : f.FieldType == typeof(bool) ? (object)true
                        : f.FieldType == typeof(int) ? 2
                        : f.FieldType == typeof(string[]) ? new[] { "p", "q", "r" }
                        : f.FieldType == typeof(MenuFace) ? new MenuFace("L" + f.Name, "S", "D", "T", MenuTone.Caution)
                        : f.FieldType == typeof(Action) ? (Action)(() => { })
                        : null;
                    TestAssert.That(value != null, "MenuRow." + f.Name + " has a type this test cannot fill: extend the test and CopyLiveFrom together");
                    f.SetValue(fresh, value);
                }
                fresh.Gone = false;
                old.Gone = true;
                old.CopyLiveFrom(fresh);
                foreach (FieldInfo f in typeof(MenuRow).GetFields(BindingFlags.Public | BindingFlags.Instance))
                {
                    if (excluded.Contains(f.Name)) continue;
                    if (f.Name == "Gone") { TestAssert.That(!old.Gone, "a surviving row is no longer gone"); continue; }
                    TestAssert.That(Equals(f.GetValue(old), f.GetValue(fresh)), "Reconcile forgot to copy MenuRow." + f.Name);
                }
            }
        }
    }
}
