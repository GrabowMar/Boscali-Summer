using System;
using System.Collections.Generic;
using BoscaliSummer.Modules.Autopilot.Domain;

namespace BoscaliSummer.Tests.Features.Autopilot
{
    /// <summary>The prototype's scripted input self-tests (app.js runSelfTest), replayed against the pure controller.</summary>
    internal static class MenuControllerTests
    {
        public static void Run()
        {
            HoldFlowReleasesOnTheRowThePointerRestedOn();
            TapLatchesAndSlowHoldDoesNot();
            DigitsActOnOneLevel();
            BackSteps();
            HatNeverArmsARowForRelease();
            StaleFocusIsClearedByChrome();
            WheelAndEdgesScrollComms();
            DangerNeedsTheHold();
            DangerHoldAbortRules();
            TogglesStayOpenInLatchedAndBlockedRowsDeny();
            UnavailableCategoryIsSkippedByTheHat();
            HysteresisAndLockInHold();
            HandPathsLandOnTheRightCategoryAndRow();
            CorridorFocusesByY();
            PointerOwnershipAfterKeys();
            BackFromADockRecentresTheVirtualPointer();
            LatchedHoverIntent();
            GamepadStickAndHat();
            EscClosesAndOpenIsGated();
            HintAndReadoutWords();
            HintSegmentsFitAtEveryScreenSize();
        }

        private static MenuRig Rig() => new MenuRig(MenuWorlds.Default());

        private static void HoldFlowReleasesOnTheRowThePointerRestedOn()
        {
            var r = Rig();
            r.HoldOpen();
            TestAssert.That(r.C.Open && r.C.Mode == MenuMode.Hold && !r.C.Pending, "C held >= 180 ms is HOLD mode");
            r.Mouse(r.L.Cx + 170f * r.L.Rho, r.L.Cy);
            TestAssert.Eq(r.Cat, "flight", "flick right selects FLIGHT");
            TestAssert.That(r.C.Dock.Side == 1 && r.C.Dock.Rect.L > r.L.Cx, "the FLIGHT dock opens on the right");
            TestAssert.Eq(r.C.Focus, -1, "peek: no row focused before entering the dock");
            r.ToRow("flight/gear");
            TestAssert.Eq(r.Focused, "flight/gear", "the pointer in the dock focuses LOWER GEAR");
            TestAssert.That(MenuHint.Parts(r.C).Live.Contains("LOWER GEAR"), "the hint says what release does");
            TestAssert.Eq(MenuHint.Readout(r.C).Value.Desc, "DEPLOY LANDING GEAR", "the hub echoes the focused action");
            r.CUp();
            TestAssert.That(!r.C.Open && !r.Ran("flight/gear"), "a release inside 80 ms of the focus landing only closes (a jerk of the hand)");

            r = Rig();
            r.HoldOpen(); r.Mouse(r.L.Cx + 170f * r.L.Rho, r.L.Cy); r.ToRow("flight/gear"); r.Adv(0.1f); r.CUp();
            TestAssert.That(r.Ran("flight/gear"), "release C executes the rested-on row");
            TestAssert.That(!r.C.Open, "HOLD execution closes the menu");
            TestAssert.Eq(r.C.Toast.Text, "GEAR EXTENDING", "the toast shows the outcome");
            r.Adv(1.3f);
            TestAssert.That(!r.C.Showing, "the toast expires after 1.2 s");
            TestAssert.That(r.Kinds.Contains(MenuCommandKind.Close), "the host is told the menu closed");

            r = Rig();
            r.HoldOpen(); r.Mouse(r.L.Cx + 170f * r.L.Rho, r.L.Cy); r.CUp();
            TestAssert.That(!r.C.Open && r.Log.Count == 0, "release with no focused row only closes");
        }

        private static void TapLatchesAndSlowHoldDoesNot()
        {
            var r = Rig();
            r.CDown(); r.Adv(0.1f); r.CUp();
            TestAssert.That(r.C.Open && r.C.Mode == MenuMode.Latched && !r.C.Pending, "tap C (< 180 ms) latches");
            r.Esc();
            r.CDown(); r.Adv(0.3f); r.CUp();
            TestAssert.That(r.C.Open && r.C.Mode == MenuMode.Latched, "a 300 ms tap with no travel still latches (180 ms only decides when the reticle shows)");
            r.Esc();
            r.CDown(); r.Adv(0.45f); r.CUp();
            TestAssert.That(!r.C.Open, "450 ms with no travel is a hold: release with nothing focused closes");
            r.Mouse(r.L.Cx, r.L.Cy); r.CDown(); r.Adv(0.3f); r.Mouse(r.L.Cx + 120f, r.L.Cy); r.CUp();
            TestAssert.That(!r.C.Open, "a 300 ms press that travelled is a hold, not a tap");
        }

        private static void DigitsActOnOneLevel()
        {
            var r = Rig();
            r.LatchOpen(); r.PressDigit(1);
            TestAssert.That(r.Cat == "weapons" && r.C.Focus == -1 && !r.C.DockFocus, "number 1 peeks WEAPONS (no focus, no dock focus)");
            r.Right();
            TestAssert.That(r.Cat == "lights" && r.C.Focus == -1 && !r.C.DockFocus, "arrow right rotates to LIGHTS and stays a peek");
            r.Down();
            TestAssert.That(r.C.Focus == 0 && r.C.DockFocus, "arrow down enters at the first visible row");
            r.Down();
            TestAssert.Eq(r.C.Focus, 1, "arrow down moves row focus");
            r.Left();
            TestAssert.That(r.Cat == "weapons" && r.C.Focus == -1 && !r.C.DockFocus, "left / right is the ring axis only: back on WEAPONS, no row armed");
            r.Up();
            TestAssert.That(r.C.Focus == -1 && !r.C.DockFocus, "arrow up from a peeked ring is a no-op");
            r.Down(); r.Down(); r.Down();
            TestAssert.Eq(r.Focused, "weapons/select", "three downs reach SELECT STATION");
            r.Enter();
            TestAssert.That(r.C.Sub != null && r.C.Sub.Id == "weapons/select", "Enter on a submenu swaps the depth in place");
            TestAssert.Eq(MenuHint.Crumb(r.C) + MenuHint.Title(r.C), "WEAPONS › SELECT STATION", "the depth-3 header is a breadcrumb");
            TestAssert.That(r.C.Dock.HasBack && r.C.Dock.Rows[0].Index == -2, "the depth-3 list starts with a BACK row");
            float left = r.C.Dock.Rect.L, width = r.C.Dock.Rect.W;
            r.Rmb();
            TestAssert.That(r.C.Sub == null && r.C.Cat == 0, "RMB returns to the dock level");
            TestAssert.That(r.C.Dock.Rect.L == left && r.C.Dock.Rect.W == width, "the dock does not slide sideways on a depth change");
            r.Rmb();
            TestAssert.That(r.C.Cat == -1 && r.C.Open, "RMB again returns to the ring");
            r.Rmb();
            TestAssert.That(!r.C.Open, "RMB at the ring closes");

            r = Rig();
            r.LatchOpen(); r.PressDigit(3); r.PressDigit(2);
            TestAssert.That(r.Cat == "flight" && r.Ran("flight/assist") && r.C.Open, "with FLIGHT peeked, 2 is ROW 2 (assist), not the LIGHTS category");
            r.PressDigit(9);
            TestAssert.That(r.Cat == "flight" && r.C.Denying && r.Kinds.Contains(MenuCommandKind.Pulse), "digit 9 in a dock is refused with feedback");
            r.Adv(MenuController.DenySec + 0.02f);
            TestAssert.That(!r.C.Denying, "the pulse clears");
            r.PressDigit(0);
            TestAssert.That(r.C.Cat == -1 && r.C.Open, "0 inside a dock is BACK");
            r.PressDigit(5);
            TestAssert.Eq(r.Cat, "comms", "with no dock up a digit picks the category");
            r.Esc(); r.LatchOpen(); r.PressDigit(0);
            TestAssert.Eq(r.Cat, "targets", "0 on the ring is TARGETS (the tenth category)");
        }

        private static void BackSteps()
        {
            var r = Rig();
            r.LatchOpen(); r.PressDigit(1); r.Down(); r.Down(); r.Down(); r.Enter();
            TestAssert.That(r.C.Sub != null, "inside SELECT STATION");
            r.Backspace();
            TestAssert.That(r.C.Sub == null && r.C.Focus == 2 && r.C.DockFocus, "Backspace returns to the dock level with the parent row focused");
            r.Backspace();
            TestAssert.That(r.C.Cat == -1 && r.C.Open, "Backspace again returns to the ring");
            r.Backspace();
            TestAssert.That(!r.C.Open, "Backspace at the ring closes");
            r.LatchOpen(); r.PressDigit(1); r.Down(); r.Down(); r.Down(); r.Enter();
            TestAssert.That(r.C.Focus == 1, "opening SELECT STATION focuses the active station (STA 2)");
            r.Up(); r.Up();
            TestAssert.That(r.C.Focus == -2, "arrow up from the first station focuses the BACK row");
            r.Enter();
            TestAssert.That(r.C.Sub == null, "pressing the BACK row goes back");
        }

        private static void HatNeverArmsARowForRelease()
        {
            var r = Rig();
            r.HoldOpen(); r.PressDigit(4);
            TestAssert.That(r.Cat == "wing" && r.C.Focus == -1, "hold C + 4 peeks WING without focusing FORM UP");
            r.CUp();
            TestAssert.That(!r.C.Open && !r.Ran("wing/formup"), "hold C + 4 + release sends nothing");

            r = Rig();
            r.HoldOpen(); r.Right(); r.Right(); r.Right(); r.Down(); r.Adv(0.2f);
            TestAssert.That(!r.C.FocusFromPointer && r.Focused == "flight/gear", "hat focus in HOLD mode is key-acquired");
            TestAssert.That(MenuHint.Parts(r.C).Live.StartsWith("ENTER · LOWER GEAR"), "the hint then says ENTER, not RELEASE C");
            r.CUp();
            TestAssert.That(!r.Ran("flight/gear"), "and releasing C does not fire it");
        }

        private static void StaleFocusIsClearedByChrome()
        {
            var r = Rig();
            r.HoldOpen(); r.Mouse(r.L.Cx + 170f * r.L.Rho, r.L.Cy); r.ToRow("flight/engine"); r.Adv(0.1f);
            MenuRect er = r.RowRect("flight/engine");
            r.Mouse(er.L + 100f, r.C.Dock.Rect.B - 6f);
            TestAssert.That(r.C.Focus == -1 && r.C.DockFocus, "the pointer over the dock footer clears the focus");
            r.CUp();
            TestAssert.That(!r.Ran("flight/engine"), "release over the footer fires nothing");

            r = Rig();
            r.HoldOpen();
            float[] p = r.Polar(162f, 170f * r.L.Rho);
            r.Mouse(p[0], p[1]); r.ToRow("comms/csplash-one"); r.Adv(0.1f);
            MenuRect sr = r.RowRect("comms/csplash-one");
            r.Mouse(sr.L + 100f, sr.B + 5f);
            TestAssert.Eq(r.Focused, "comms/csplash-one", "a 5 px dip into the group heading keeps the focused row (10 px hysteresis)");
            r.CUp();
            TestAssert.That(r.Ran("comms/csplash-one"), "and release still executes it");
        }

        private static void WheelAndEdgesScrollComms()
        {
            var r = Rig();
            r.LatchOpen(); r.PressDigit(5);
            TestAssert.That(r.Cat == "comms" && r.C.Items.Count == 12, "number 5 peeks COMMS with 12 calls");
            int cap = r.C.Dock.Cap;
            TestAssert.That(cap >= 7 && r.C.Dock.Rows.Count == cap, "COMMS shows at least 7 rows at 1080p");
            TestAssert.That(r.InDock("comms/ccopy") && r.InDock("comms/cnegative"), "the first window needs no scrolling for COPY and NEGATIVE");
            float h0 = r.C.Dock.Rect.H;
            r.Wheel(1); r.Wheel(1); r.Wheel(1);
            TestAssert.Eq(r.C.Offset, 3, "the wheel scrolls the COMMS window");
            TestAssert.That(r.C.Dock.Overflow && r.C.Dock.RailThumb.H > 0f, "a rail is drawn only when the list overflows");
            TestAssert.Near(r.C.Dock.Rect.H, h0, "the dock height is stable while scrolling");
            MenuRect dn = r.C.Dock.ChipDown;
            r.Mouse((dn.L + dn.R) / 2f, (dn.T + dn.B) / 2f); r.Adv(0.15f); r.Adv(0.15f);
            TestAssert.That(r.C.Offset > 3, "resting on the down chip keeps scrolling without moving the mouse");

            r = Rig();
            r.LatchOpen(); r.PressDigit(5);
            DockRow last = r.C.Dock.Rows[r.C.Dock.Rows.Count - 1];
            r.Mouse(r.L.Cx + 10f, r.L.Cy - 300f); r.Mouse(last.Rect.L + 100f, last.Rect.B - 4f); r.Adv(0.15f); r.Adv(0.15f);
            TestAssert.That(r.C.Offset > 0 && r.C.Focus == -1, "resting on the last row's bottom edge band scrolls");
            r.Esc();
            TestAssert.That(!r.C.Open, "Esc closes");

            r = Rig();
            r.LatchOpen(); r.PressDigit(5);
            MenuRect clickDown = r.C.Dock.ChipDown;
            r.Mouse((clickDown.L + clickDown.R) / 2f, (clickDown.T + clickDown.B) / 2f);
            int before = r.C.Offset;
            r.Click();
            TestAssert.That(r.C.Offset > before, "clicking the down chip pages the list");
        }

        private static void DangerNeedsTheHold()
        {
            var r = Rig();
            r.LatchOpen(); r.PressDigit(3);
            r.Digit(6); r.Adv(0.5f);
            TestAssert.That(r.C.Hold != null && r.C.Hold.Progress > 0.55f && r.C.Hold.Progress < 0.7f, "the hold bar fills while the key is held");
            MenuHintParts hint = MenuHint.Parts(r.C);
            TestAssert.That(hint.Live == "HOLDING EJECT · RELEASE TO ABORT" && hint.Warn, "the hint turns amber and says how to abort");
            r.DigitUp();
            TestAssert.That(r.C.Hold == null && !r.Ran("flight/eject") && r.C.Open, "release before 0.8 s cancels");
            r.Digit(6); r.Adv(0.79f);
            TestAssert.That(!r.Ran("flight/eject") && r.C.Hold != null, "0.79 s is still not enough");
            r.Adv(0.03f);
            TestAssert.That(r.Ran("flight/eject") && !r.C.Open, "held >= 0.8 s fires EJECT");

            r = Rig();
            r.LatchOpen(); r.PressDigit(3); for (int i = 0; i < 6; i++) r.Down();
            r.EnterDown(); r.Adv(0.85f); r.EnterUp();
            TestAssert.That(r.Ran("flight/eject"), "Enter held 0.85 s also fires EJECT");
            r = Rig();
            r.LatchOpen(); r.PressDigit(3); for (int i = 0; i < 6; i++) r.Down();
            r.Enter();
            TestAssert.That(!r.Ran("flight/eject") && r.C.Open, "a plain Enter press never fires EJECT");

            r = Rig();
            r.LatchOpen(); r.PressDigit(3); r.Digit(5); r.DigitUp();
            TestAssert.That(!r.Ran("flight/engine") && r.Model.Row("flight/engine").IsOn, "STOP ENGINE needs the hold while airborne");
            r.Digit(5); r.Adv(0.85f);
            TestAssert.That(r.Ran("flight/engine"), "holding 0.85 s stops the engine");

            var g = new MenuRig(MenuWorlds.Ground());
            g.LatchOpen(); g.PressDigit(3);
            TestAssert.Eq(g.C.Items[0].Id, "flight/assist", "on the ground the list starts at ASSIST");
            g.PressDigit(3);
            TestAssert.That(g.Ran("flight/engine"), "on the ground START ENGINE is a plain press");
        }

        private static MenuRig AtEject()
        {
            var r = Rig();
            r.LatchOpen(); r.PressDigit(3); for (int i = 0; i < 6; i++) r.Down();
            TestAssert.Eq(r.Focused, "flight/eject", "arrows reach EJECT");
            return r;
        }

        private static void DangerHoldAbortRules()
        {
            var r = AtEject(); r.EnterDown(); r.Adv(0.3f); r.Up(); r.Adv(0.9f); r.EnterUp();
            TestAssert.That(!r.Ran("flight/eject") && r.C.Hold == null, "abort: Enter held, then ArrowUp, never ejects");
            r = AtEject(); r.EnterDown(); r.Adv(0.3f); r.Backspace(); r.Adv(0.9f); r.EnterUp();
            TestAssert.That(!r.Ran("flight/eject"), "abort: Enter held, then Backspace, never ejects");
            r = AtEject(); r.EnterDown(); r.Adv(0.3f); r.Left(); r.Adv(0.9f); r.EnterUp();
            TestAssert.That(!r.Ran("flight/eject"), "abort: Enter held, then ArrowLeft (rotate), never ejects");
            r = AtEject(); r.EnterDown(); r.Adv(0.3f); r.Rmb(); r.Adv(0.9f); r.EnterUp();
            TestAssert.That(!r.Ran("flight/eject"), "abort: Enter held, then RMB, never ejects");
            r = AtEject(); r.EnterDown(); r.Adv(0.3f); r.Wheel(-1); r.Adv(0.9f); r.EnterUp();
            TestAssert.That(!r.Ran("flight/eject"), "abort: Enter held, then the wheel, never ejects");

            r = Rig();
            r.LatchOpen(); r.PressDigit(3); r.ToRow("flight/eject"); r.LmbDown(); r.Adv(0.3f); r.ToRow("flight/gear"); r.Adv(0.9f); r.LmbUp();
            TestAssert.That(!r.Ran("flight/eject") && r.C.Open, "abort: LMB held, then dragged to another row, never ejects");
            r = Rig();
            r.LatchOpen(); r.PressDigit(3); r.ToRow("flight/eject"); r.LmbDown(); r.Adv(0.3f);
            MenuRect ej = r.RowRect("flight/eject");
            r.Mouse(ej.L + 100f, ej.T - 4f); r.Adv(0.6f); r.LmbUp();
            TestAssert.That(r.Ran("flight/eject"), "a 4 px tremor on a held click does not abandon it (hysteresis) and it fires");
            r = Rig();
            r.LatchOpen(); r.PressDigit(3); r.ToRow("flight/eject"); r.Click();
            TestAssert.That(!r.Ran("flight/eject"), "a click is not a hold");
        }

        private static void TogglesStayOpenInLatchedAndBlockedRowsDeny()
        {
            var r = Rig();
            r.LatchOpen(); r.PressDigit(3); r.PressDigit(2);
            TestAssert.That(r.Ran("flight/assist") && r.C.Open && r.C.Mode == MenuMode.Latched, "number 2 on FLIGHT toggles assist and keeps the menu open");
            TestAssert.That(r.C.FlashId == "flight/assist", "the row flashes");
            r.Adv(0.16f);
            TestAssert.That(r.C.FlashId == null, "the flash clears after 150 ms");

            var w = MenuWorlds.Default();
            w.Gear = GearPhase.Extending;
            r = new MenuRig(w);
            r.LatchOpen(); r.PressDigit(3);
            TestAssert.Eq(r.C.Items[0].Id, "flight/gear", "the busy row keeps the first slot");
            r.PressDigit(1);
            TestAssert.That(!r.Ran("flight/gear") && r.C.Denying, "pressing a blocked row does nothing and says so");
            TestAssert.Eq(r.C.Items[1].Id, "flight/assist", "rows keep their numbers while the gear moves");

            r = Rig();
            r.HoldOpen(); r.Mouse(r.L.Cx + 170f * r.L.Rho, r.L.Cy); r.ToRow("flight/assist"); r.Adv(0.1f); r.CUp();
            TestAssert.That(r.Ran("flight/assist") && !r.C.Open && r.C.Toast.Text == "FLIGHT ASSIST OFF", "in HOLD mode a toggle closes the menu and toasts the outcome");
        }

        private static void UnavailableCategoryIsSkippedByTheHat()
        {
            var r = new MenuRig(MenuWorlds.NoWing());
            r.LatchOpen();
            var seq = new List<string>();
            for (int i = 0; i < 4; i++) { r.Right(); seq.Add(r.Cat); }
            TestAssert.Eq(string.Join(",", seq), "weapons,lights,flight,comms", "hat rotation skips the unavailable WING");
            r.PressDigit(0); r.PressDigit(4);
            TestAssert.That(r.Cat == "wing" && r.C.Focus == -1 && r.C.Dock.Empty, "a number key on the unavailable WING only peeks, with an empty dock");
            r.Down();
            TestAssert.Eq(r.C.Focus, -1, "there is nothing to enter");
        }

        private static void HysteresisAndLockInHold()
        {
            var r = Rig();
            r.HoldOpen();
            float[] p = r.Polar(90f, 150f * r.L.Rho); r.Mouse(p[0], p[1]);
            TestAssert.Eq(r.Cat, "flight", "90 degrees selects FLIGHT");
            p = r.Polar(111f, 150f * r.L.Rho); r.Mouse(p[0], p[1]);
            TestAssert.Eq(r.Cat, "flight", "3 degrees past the sector edge still FLIGHT (hysteresis)");
            p = r.Polar(117f, 150f * r.L.Rho); r.Mouse(p[0], p[1]);
            TestAssert.Eq(r.Cat, "wing", "9 degrees past the edge switches to WING");
            r.Mouse(r.L.Cx + 10f, r.L.Cy);
            TestAssert.That(r.C.Cat == -1, "the dead zone at the hub clears the selection");
            r.CUp();

            r = Rig();
            r.HoldOpen();
            p = r.Polar(90f, 0.9f * r.L.R); r.Mouse(p[0], p[1]);
            TestAssert.That(r.Cat == "flight" && r.C.Locked, "past 0.85 R the choice locks");
            TestAssert.That(MenuHint.Parts(r.C).Live.Contains("PULL BACK TO RE-AIM"), "the hint offers the way out while locked");
            p = r.Polar(180f, 0.9f * r.L.R); r.Mouse(p[0], p[1]);
            TestAssert.Eq(r.Cat, "flight", "locked: swinging the angle at 0.9 R does not retarget");
            p = r.Polar(126f, 0.7f * r.L.R); r.Mouse(p[0], p[1]);
            TestAssert.Eq(r.Cat, "flight", "locked: still ignoring the angle at 0.7 R");
            p = r.Polar(126f, 0.5f * r.L.R); r.Mouse(p[0], p[1]);
            TestAssert.That(r.Cat == "wing" && !r.C.Locked, "pulled back inside 0.6 R the lock releases and WING is picked");
            r.CUp();

            r = Rig();
            r.HoldOpen(); r.Mouse(r.L.Cx - 300f, r.L.Cy);
            TestAssert.Eq(r.Cat, "view", "one 300 px event from the hub selects VIEW (no outer cap)");
            r.CUp();

            r = Rig();
            r.HoldOpen(); p = r.Polar(90f, 0.9f * r.L.R); r.Mouse(p[0], p[1]);
            float[] disc = r.DiscOf(r.Model.IndexOf("wing")); r.Mouse(disc[0], disc[1]); r.Adv(0.2f);
            TestAssert.Eq(r.Cat, "wing", "resting on another disc while committed side-steps to it");
        }

        private static void HandPathsLandOnTheRightCategoryAndRow()
        {
            var bad = new List<string>();
            var probe = Rig();
            foreach (MenuCategory c in probe.Model.Categories)
            {
                if (!c.Available) continue;
                int ci = probe.Model.IndexOf(c.Id);
                var ids = new List<string> { c.Rows[0].Id };
                if (c.Id == "weapons") ids.Add("weapons/prev");
                if (c.Id == "radio") { ids.Add("radio/seek"); ids.Add("radio/stopall"); }
                if (c.Id == "targets") { ids.Add("targets/keep"); ids.Add("targets/clear"); ids.Add("targets/droplast"); }
                foreach (string id in ids)
                {
                    var a = Rig();
                    a.LatchOpen(); a.PressDigit(ci == 9 ? 0 : ci + 1);
                    float[] target = a.At(id);
                    var r = Rig();
                    r.HoldOpen();
                    r.Walk(new[] { new[] { r.L.Cx, r.L.Cy }, r.DiscOf(ci), target }, 20f);
                    r.Adv(0.1f);
                    if (r.Cat != c.Id || r.Focused != id) bad.Add(c.Id + "/" + id + "->" + r.Cat + "/" + r.Focused);
                }
            }
            TestAssert.That(bad.Count == 0, "hub -> disc -> row lands on the right category AND row for every category: " + string.Join(" | ", bad));

            int bow = 0;
            foreach (float k in new[] { 0.12f, -0.12f })
            {
                var a = Rig(); a.LatchOpen(); a.PressDigit(1);
                float[] target = a.At("weapons/prev");
                var r = Rig();
                r.HoldOpen();
                var pts = new List<float[]> { new[] { r.L.Cx, r.L.Cy } };
                float[] disc = r.DiscOf(0);
                pts.AddRange(Bow(pts[0], disc, k)); pts.AddRange(Bow(disc, target, k));
                r.Walk(pts, 20f); r.Adv(0.1f);
                if (r.Cat != "weapons" || r.Focused != "weapons/prev") bow++;
            }
            TestAssert.Eq(bow, 0, "bowed hand arcs to WEAPONS > PREV STATION also land");

            int fast = 0;
            foreach (string cn in new[] { "radio", "targets" })
            {
                var probe2 = Rig();
                int ci = probe2.Model.IndexOf(cn);
                var a = Rig(); a.LatchOpen(); a.PressDigit(ci == 9 ? 0 : ci + 1);
                float[] target = a.At(probe2.Model.Categories[ci].Rows[1].Id);
                var r = Rig();
                r.HoldOpen();
                r.Walk(new[] { new[] { r.L.Cx, r.L.Cy }, r.DiscOf(ci), target }, 130f); r.Adv(0.1f);
                if (r.Cat != cn) fast++;
            }
            TestAssert.Eq(fast, 0, "fast (130 px per frame) flicks to RADIO and TARGETS keep their category");
        }

        private static IEnumerable<float[]> Bow(float[] p0, float[] p1, float k)
        {
            float dx = p1[0] - p0[0], dy = p1[1] - p0[1], d = (float)Math.Sqrt(dx * dx + dy * dy), px = -dy / d, py = dx / d;
            for (int i = 1; i <= 10; i++)
            {
                float t = i / 10f, bulge = (float)Math.Sin(Math.PI * t) * k * d;
                yield return new[] { p0[0] + dx * t + px * bulge, p0[1] + dy * t + py * bulge };
            }
        }

        private static void CorridorFocusesByY()
        {
            var r = Rig();
            r.HoldOpen(); r.Mouse(r.L.Cx + 170f * r.L.Rho, r.L.Cy);
            MenuRect dock = r.C.Dock.Rect, ptr = r.L.PointerDock(r.C.Dock);
            TestAssert.That(ptr.L < dock.L && dock.L - ptr.L >= 40f * r.L.Rho && ptr.R == dock.R, "the pointer dock reaches toward the hub by the gap");
            MenuRect gear = r.RowRect("flight/gear");
            r.Mouse(ptr.L + 4f, (gear.T + gear.B) / 2f); r.Adv(0.1f);
            TestAssert.Eq(r.Focused, "flight/gear", "a pointer in the corridor focuses the row by Y");
            r.CUp();
        }

        private static void PointerOwnershipAfterKeys()
        {
            var r = Rig();
            r.HoldOpen(); r.Right();
            TestAssert.Eq(r.Cat, "weapons", "the hat picks WEAPONS");
            r.Mouse(r.L.Cx + 2f, r.L.Cy + 1f);
            TestAssert.Eq(r.Cat, "weapons", "2 px of mouse jitter keeps the hat-chosen category");
            r.Mouse(r.L.Cx + 8f, r.L.Cy + 6f);
            TestAssert.Eq(r.Cat, "weapons", "14 px of travel is still ignored");
            r.Mouse(r.L.Cx + 70f, r.L.Cy);
            TestAssert.That(r.Cat != "weapons", "real travel hands the pointer back");
            r.CUp();

            r = Rig();
            r.LatchOpen(); r.PressDigit(3); r.ToRow("flight/assist"); r.Down(); r.Down();
            TestAssert.Eq(r.Focused, "flight/autoland", "LATCHED: arrow focus beats a resting pointer");
            r.Mouse(r.Mx + 3f, r.My + 2f);
            TestAssert.Eq(r.Focused, "flight/autoland", "LATCHED: 3 px of jitter does not steal it back");

            r = Rig();
            r.LatchOpen(); r.PressDigit(3); r.ToRow("flight/assist"); r.Wheel(1);
            TestAssert.Eq(r.Focused, "flight/hover", "the wheel on a 6-row dock moves focus (pointer resting in the dock)");
            r.Wheel(1);
            TestAssert.Eq(r.Focused, "flight/autoland", "and keeps moving instead of snapping back");
        }

        private static void BackFromADockRecentresTheVirtualPointer()
        {
            var r = Rig();
            r.HoldOpen(); r.Mouse(r.L.Cx + 170f * r.L.Rho, r.L.Cy); r.ToRow("flight/gear"); r.Adv(0.1f);
            float mx = r.Mx, my = r.My;
            r.Rmb();
            TestAssert.That(r.C.Cat == -1 && r.C.Open, "RMB from a dock row returns to the ring");
            TestAssert.That(Math.Abs(r.C.PtrX - r.L.Cx) < 1f && Math.Abs(r.C.PtrY - r.L.Cy) < 1f, "the virtual pointer is back at the hub");
            r.Mouse(mx - 170f * r.L.Rho, my);
            TestAssert.Eq(r.Cat, "view", "the next flick re-selects (VIEW)");
            r.CUp();
        }

        private static void LatchedHoverIntent()
        {
            var r = Rig();
            r.LatchOpen();
            float[] d = r.DiscOf(r.Model.IndexOf("flight")); r.Mouse(d[0], d[1]);
            TestAssert.Eq(r.Cat, "flight", "the first peek from nothing is instant");
            float[] d2 = r.DiscOf(r.Model.IndexOf("lights")); r.Mouse(d2[0], d2[1]);
            TestAssert.That(r.Cat == "flight" && r.C.HoverCategory == r.Model.IndexOf("lights"), "passing over another disc does not retarget");
            r.Adv(0.13f);
            TestAssert.Eq(r.Cat, "lights", "resting 120 ms on it does");
            float[] d3 = r.DiscOf(r.Model.IndexOf("flight")); r.Mouse(d3[0], d3[1]); r.Click();
            TestAssert.Eq(r.Cat, "flight", "a click retargets instantly");

            var bad = new List<string>();
            foreach (string cn in new[] { "weapons", "comms", "radio", "targets" })
            {
                var probe = Rig(); int ci = probe.Model.IndexOf(cn);
                var a = Rig(); a.LatchOpen(); a.PressDigit(ci == 9 ? 0 : ci + 1);
                string first = probe.Model.Categories[ci].Rows[0].Id;
                float[] target = a.At(first);
                var q = Rig(); q.LatchOpen();
                float[] disc = q.DiscOf(ci); q.Mouse(disc[0], disc[1]);
                q.Walk(new[] { disc, target }, 20f); q.Adv(0.04f);
                if (q.Cat != cn || q.Focused != first) bad.Add(cn + "->" + q.Cat + "/" + q.Focused);
            }
            TestAssert.That(bad.Count == 0, "LATCHED: a straight path from the selected disc to a row crosses other discs without retargeting: " + string.Join(" | ", bad));

            r = Rig();
            r.LatchOpen(); r.PressDigit(3); r.ToRow("flight/assist"); r.Click();
            TestAssert.That(r.Ran("flight/assist") && r.C.Open, "a click on a row runs it and keeps the menu open");
            r.ToRow("flight/engine"); r.Click();
            TestAssert.That(!r.Ran("flight/engine"), "a click on a hold-to-stop row does not run it");
        }

        private static void GamepadStickAndHat()
        {
            var r = Rig();
            r.PadY(true); r.Adv(0.2f);
            TestAssert.That(r.C.Open && r.C.Mode == MenuMode.Hold && r.C.Device == MenuDevice.Pad, "pad: Y held opens HOLD mode");
            r.Stick(1f, 0f); r.Stick(1f, 0f);
            TestAssert.That(r.Cat == "flight" && r.C.DockFocus, "pad: full right deflection selects FLIGHT and reaches the dock rows");
            r.Stick(0f, 0.1f);
            TestAssert.Eq(r.Cat, "flight", "pad: a released stick leaves the selection alone");
            r.PadY(false);

            r = Rig();
            r.PadY(true); r.Adv(0.1f); r.PadY(false);
            TestAssert.That(r.C.Open && r.C.Mode == MenuMode.Latched, "pad: Y tap latches");
            MenuInput step = MenuInput.Idle(r.Time); step.FromPad = true; step.Right = true; step.CHeld = false; step.CanOpen = true;
            r.Send(step);
            TestAssert.That(r.Cat == "weapons" && r.C.Device == MenuDevice.Pad, "pad: the d-pad steps the ring");
            MenuInput back = MenuInput.Idle(r.Time); back.FromPad = true; back.Back = true;
            r.Send(back);
            TestAssert.That(r.C.Cat == -1, "pad: B goes back");
            MenuInput press = MenuInput.Idle(r.Time); press.FromPad = true; press.Digit = 3;
            r.Send(press);
            MenuInput down = MenuInput.Idle(r.Time); down.FromPad = true; down.Down = true;
            r.Send(down); r.Send(down);
            MenuInput a = MenuInput.Idle(r.Time); a.FromPad = true; a.Press = true; a.PressSource = PressSource.Pad; a.PadAHeld = true;
            r.PadAHeld = true; r.Send(a); r.PadAHeld = false; r.Send(MenuInput.Idle(r.Time));
            TestAssert.That(r.Ran("flight/assist"), "pad: A presses the focused row");
        }

        private static void EscClosesAndOpenIsGated()
        {
            var r = Rig();
            r.CanOpen = false; r.CDown();
            TestAssert.That(!r.C.Open, "C is ignored while the host says the menu cannot open (paused, map, native wheel)");
            r.CUp(); r.CanOpen = true;
            r.LatchOpen(); r.PressDigit(3);
            r.Esc();
            TestAssert.That(!r.C.Open && r.Kinds.Contains(MenuCommandKind.Close), "Esc closes everything");
            r.CDown(); r.Adv(0.1f); r.CUp();
            TestAssert.That(r.C.Open, "and it reopens");
            r.CDown();
            TestAssert.That(!r.C.Open, "C again closes a latched menu");
            r.CUp();
            r.LatchOpen();
            r.C.Cancel();
            TestAssert.That(!r.C.Open, "Cancel (focus lost, aircraft gone) closes");
        }

        private static void HintAndReadoutWords()
        {
            var r = Rig();
            r.LatchOpen();
            TestAssert.Eq(MenuHint.Parts(r.C).Back, "BKSP BACK · ESC CLOSE", "right after the key press the last device is the keyboard");
            r.Mouse(r.L.Cx + 3f, r.L.Cy + 3f);
            MenuHintParts idle = MenuHint.Parts(r.C);
            TestAssert.That(idle.Mode == "LATCHED" && idle.Live == "CLICK OR 1–0 · PICK A CATEGORY", "latched idle hint");
            TestAssert.Eq(idle.Back, "RMB BACK · ESC CLOSE", "mouse: RMB is back");
            TestAssert.That(!MenuHint.Readout(r.C).HasValue, "nothing focused: the hub shows the status summary");
            r.PressDigit(3);
            TestAssert.Eq(MenuHint.Parts(r.C).Back, "BKSP BACK · ESC CLOSE", "after a key, BKSP is back");
            TestAssert.Eq(MenuHint.FooterLeft(r.C), "↑↓ ROW · ←→ RING", "and the dock footer shows the hat legend");
            TestAssert.Eq(MenuHint.FooterRight(r.C), "BKSP/0 · BACK", "with BKSP/0 as the back legend");
            r.Mouse(r.L.Cx, r.L.Cy); r.Mouse(r.L.Cx + 40f, r.L.Cy + 40f);
            TestAssert.That(r.C.Device == MenuDevice.Mouse && MenuHint.Parts(r.C).Back.StartsWith("RMB BACK"), "moving the mouse flips the legend back to RMB");
            float[] disc = r.DiscOf(0); r.Mouse(disc[0], disc[1]); r.Adv(0.13f);
            TestAssert.That(MenuHint.Parts(r.C).Live.StartsWith("CLICK OR 1–8"), "LATCHED hint says CLICK when the last input was the pointer");
            r.ToRow("weapons/select");
            TestAssert.Eq(MenuHint.Parts(r.C).Live, "CLICK · OPEN STATIONS", "submenu rows say OPEN STATIONS");
            r.ToRow("weapons/next");
            TestAssert.Eq(MenuHint.Parts(r.C).Live, "CLICK · NEXT STATION", "action rows say the verb");
            TestAssert.Eq(MenuHint.Readout(r.C).Value.Title, "NEXT STATION", "the hub echoes the label");
            r.Esc(); r.LatchOpen(); r.PressDigit(5); r.ToRow("comms/ccopy");
            TestAssert.Eq(MenuHint.Parts(r.C).Live, "CLICK · SEND COPY", "comms calls are SENT");
            TestAssert.Eq(MenuHint.Readout(r.C).Value.Title, "SEND COPY", "and the hub says SEND COPY");
            r.Esc(); r.LatchOpen(); r.PressDigit(3); r.ToRow("flight/eject");
            MenuHintParts eject = MenuHint.Parts(r.C);
            TestAssert.That(eject.Live == "HOLD LMB 0.8 S · EJECT" && eject.Warn, "danger rows say how to confirm");
            r.Esc(); r.LatchOpen(); r.PressDigit(1); r.ToRow("weapons/select"); r.Click();
            r.ToRow("weapons/select/st2");
            TestAssert.Eq(MenuHint.Parts(r.C).Live, "CLICK · SELECT STA 2", "pickers say SELECT STA 2");
            MenuRect back = r.C.Dock.Rows[0].Rect;
            r.Mouse(back.L + 20f, (back.T + back.B) / 2f);
            TestAssert.That(r.C.Focus == -2 && MenuHint.Readout(r.C).Value.Desc == "BACK TO WEAPONS" && MenuHint.Parts(r.C).Live == "CLICK · BACK", "the BACK row reads BACK TO WEAPONS");

            var g = new MenuRig(MenuWorlds.Default());
            g.LatchOpen(); g.PressDigit(3);
            TestAssert.Eq(MenuHint.Parts(g.C).Back, "BKSP BACK · ESC CLOSE", "hold hint segments differ only by mode");
            var ground = new MenuRig(MenuWorlds.Default());
            ground.HoldOpen();
            TestAssert.That(MenuHint.Parts(ground.C).Mode == "HOLD MODE" && MenuHint.Parts(ground.C).Back.EndsWith("ESC CANCEL"), "HOLD hint says ESC CANCEL");
            TestAssert.Eq(MenuHint.Parts(ground.C).Live, "FLICK OR 1–0 · PICK A CATEGORY", "HOLD idle hint");
        }

        private static void HintSegmentsFitAtEveryScreenSize()
        {
            var metrics = new MenuTestMetrics();
            foreach (int[] s in MenuLayoutTests.Screens)
                foreach (var scenario in MenuWorlds.All())
                {
                    var rig = new MenuRig(scenario.Value, s[0], s[1]);
                    float t = rig.L.T, modeW = 124f * t, liveW = 396f * t, backW = 256f * t, pad = 16f * t;
                    foreach (MenuMode mode in new[] { MenuMode.Latched, MenuMode.Hold })
                        foreach (MenuDevice dev in new[] { MenuDevice.Mouse, MenuDevice.Key, MenuDevice.Pad })
                        {
                            MenuRig r = new MenuRig(scenario.Value, s[0], s[1]);
                            if (mode == MenuMode.Hold) r.HoldOpen(); else r.LatchOpen();
                            if (dev == MenuDevice.Key) r.Right(); else if (dev == MenuDevice.Pad) { MenuInput i = MenuInput.Idle(r.Time); i.FromPad = true; i.Right = true; r.Send(i); }
                            MenuHintParts p = MenuHint.Parts(r.C);
                            string at = " " + scenario.Key + " @" + s[0] + "x" + s[1] + " " + mode + " " + dev;
                            TestAssert.That(metrics.Width(p.Mode, MenuFont.Hint) * t <= modeW - pad, "hint mode segment fits" + at + ": " + p.Mode);
                            TestAssert.That(metrics.Width(p.Back, MenuFont.Hint) * t <= backW - pad, "hint back segment fits" + at + ": " + p.Back);
                            foreach (MenuCategory cat in r.Model.Categories)
                            {
                                if (!cat.Available) continue;
                                int ci = r.Model.IndexOf(cat.Id);
                                foreach (MenuRow row in cat.Rows)
                                {
                                    var q = new MenuRig(scenario.Value, s[0], s[1]);
                                    q.LatchOpen(); q.PressDigit(ci == 9 ? 0 : ci + 1); q.ToRow(row.Id);
                                    string live = MenuHint.Parts(q.C).Live;
                                    TestAssert.That(metrics.Width(live, MenuFont.Hint) * t <= liveW - pad, "hint live text fits" + at + ": " + live);
                                }
                            }
                        }
                }
        }
    }
}
