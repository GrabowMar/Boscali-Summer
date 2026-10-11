using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Modules.Hud.Domain;

namespace BoscaliSummer.Tests.Features.Hud
{
    /// <summary>The TAC BOARD's options as one value: the defaults, the ranges, the page mask and what each declutter level leaves of the switches.</summary>
    internal static class BoardOptionsTests
    {
        private static readonly BoardDeclutter[] Levels = { BoardDeclutter.Full, BoardDeclutter.Lean, BoardDeclutter.Minimal };

        private static BoardOptions At(BoardDeclutter level)
        {
            BoardOptions o = BoardOptions.Default;
            o.Declutter = level;
            return o;
        }

        private static ushort Mask(params ScopePage[] pages)
        {
            ushort mask = 0;
            foreach (ScopePage p in pages) mask |= CuePriority.Bit(p);
            return mask;
        }

        public static void Run()
        {
            Defaults();
            Clamps();
            NotANumber();
            PageMask();
            DeclutterLevels();
            SwitchesStayOff();
            VoicedPages();
            SystemsStrip();
        }

        private static void Defaults()
        {
            BoardOptions d = BoardOptions.Default;
            TestAssert.That(d.Enabled && d.Layout == BoardLayout.Wide, "On, Wide");
            TestAssert.Near(d.Scale, 1f, "Scale 1");
            TestAssert.Near(d.OffsetX, 0f, "OffsetX 0");
            TestAssert.Near(d.OffsetY, 0f, "OffsetY 0");
            TestAssert.Near(d.Opacity, 1f, "Opacity 1");
            TestAssert.Near(d.Shade, 0.45f, "Shade 0.45");
            TestAssert.That(d.Declutter == BoardDeclutter.Full && d.ScopeMode == BoardScopeMode.Auto, "Full, pages Auto");
            TestAssert.That(d.PageThreat && d.PageShot && d.PageMissileCam && d.PageApproach && d.PageWing && d.PageStrike && d.PageDock && d.PageObjective && d.PageSystems, "Every page on");
            TestAssert.Near(d.ThreatHoldSeconds, 6f, "Threat hold 6 s");
            TestAssert.Near(d.PageMinSeconds, 3f, "Page minimum 3 s");
            TestAssert.That(d.ShowLamps && d.ShowSystemsStrip && d.ShowGauges && d.ShowTicker, "Every block on");
            TestAssert.That(d.Sway && d.Corners, "Sway and Corners on");

            BoardOptions clean = d;
            clean.Sanitize();
            TestAssert.Eq(clean.Scale, d.Scale, "The defaults are inside their own ranges");
            TestAssert.That(clean.Opacity == d.Opacity && clean.Shade == d.Shade && clean.ThreatHoldSeconds == d.ThreatHoldSeconds && clean.PageMinSeconds == d.PageMinSeconds, "...all of them");

            BoardEffective e = d.Effective();
            TestAssert.Eq(e.Pages, Mask(ScopePage.Threat, ScopePage.Shot, ScopePage.MissileCam, ScopePage.Approach, ScopePage.Wing, ScopePage.Strike, ScopePage.Dock, ScopePage.Objective, ScopePage.Systems), "The defaults allow all nine pages");
            TestAssert.Eq(e.Pages, (ushort)0x1FF, "...nine bits");
        }

        private static void Clamps()
        {
            BoardOptions o = BoardOptions.Default;
            o.Scale = 0.1f; o.OffsetX = -5f; o.OffsetY = -1f; o.Opacity = 0f; o.Shade = -1f; o.ThreatHoldSeconds = 0f; o.PageMinSeconds = -3f;
            o.Sanitize();
            TestAssert.Near(o.Scale, 0.6f, "Scale floor");
            TestAssert.Near(o.OffsetX, 0f, "OffsetX floor");
            TestAssert.Near(o.OffsetY, 0f, "OffsetY floor");
            TestAssert.Near(o.Opacity, 0.25f, "Opacity floor");
            TestAssert.Near(o.Shade, 0f, "Shade floor");
            TestAssert.Near(o.ThreatHoldSeconds, 2f, "Threat hold floor");
            TestAssert.Near(o.PageMinSeconds, 0f, "Page minimum floor");

            o.Scale = 9f; o.OffsetX = 5000f; o.OffsetY = 5000f; o.Opacity = 3f; o.Shade = 7f; o.ThreatHoldSeconds = 99f; o.PageMinSeconds = 99f;
            o.Sanitize();
            TestAssert.Near(o.Scale, 1.4f, "Scale ceiling");
            TestAssert.Near(o.OffsetX, 1200f, "OffsetX ceiling");
            TestAssert.Near(o.OffsetY, 600f, "OffsetY ceiling");
            TestAssert.Near(o.Opacity, 1f, "Opacity ceiling");
            TestAssert.Near(o.Shade, 1f, "Shade ceiling");
            TestAssert.Near(o.ThreatHoldSeconds, 30f, "Threat hold ceiling");
            TestAssert.Near(o.PageMinSeconds, 15f, "Page minimum ceiling");

            o.Scale = float.PositiveInfinity; o.OffsetX = float.NegativeInfinity;
            o.Sanitize();
            TestAssert.That(o.Scale == 1.4f && o.OffsetX == 0f, "Infinity clamps like any other out-of-range value");

            o.Scale = 0.6f; o.OffsetX = 1200f; o.Opacity = 0.25f; o.ThreatHoldSeconds = 30f;
            o.Sanitize();
            TestAssert.That(o.Scale == 0.6f && o.OffsetX == 1200f && o.Opacity == 0.25f && o.ThreatHoldSeconds == 30f, "The ends of a range are inside it");

            o.Layout = (BoardLayout)7; o.Declutter = (BoardDeclutter)(-1); o.ScopeMode = (BoardScopeMode)9;
            o.Sanitize();
            TestAssert.That(o.Layout == BoardLayout.Wide && o.Declutter == BoardDeclutter.Full && o.ScopeMode == BoardScopeMode.Auto, "An enum that names nothing falls back to its default");
            o.Layout = BoardLayout.Stack; o.Declutter = BoardDeclutter.Minimal; o.ScopeMode = BoardScopeMode.Off;
            o.Sanitize();
            TestAssert.That(o.Layout == BoardLayout.Stack && o.Declutter == BoardDeclutter.Minimal && o.ScopeMode == BoardScopeMode.Off, "...and a real one is kept");
        }

        private static void NotANumber()
        {
            BoardOptions o = BoardOptions.Default;
            o.Scale = float.NaN; o.OffsetX = float.NaN; o.OffsetY = float.NaN; o.Opacity = float.NaN; o.Shade = float.NaN; o.ThreatHoldSeconds = float.NaN; o.PageMinSeconds = float.NaN;
            o.Sanitize();
            BoardOptions d = BoardOptions.Default;
            TestAssert.That(o.Scale == d.Scale && o.OffsetX == d.OffsetX && o.OffsetY == d.OffsetY && o.Opacity == d.Opacity, "NaN becomes the default (size and place)");
            TestAssert.That(o.Shade == d.Shade && o.ThreatHoldSeconds == d.ThreatHoldSeconds && o.PageMinSeconds == d.PageMinSeconds, "...and the rest");
            TestAssert.That(!float.IsNaN(o.Scale + o.OffsetX + o.OffsetY + o.Opacity + o.Shade + o.ThreatHoldSeconds + o.PageMinSeconds), "Nothing NaN is left");
        }

        private static void PageMask()
        {
            BoardOptions none = BoardOptions.Default;
            none.PageThreat = none.PageShot = none.PageMissileCam = none.PageApproach = none.PageWing = none.PageStrike = none.PageDock = none.PageObjective = none.PageSystems = false;
            TestAssert.Eq(none.PageMask(), (ushort)0, "Every page switch off: no bits");

            // One switch on, one bit: the bit is the page's own (ScopePage order), not the switch's place in the struct.
            ScopePage[] order = { ScopePage.Threat, ScopePage.Shot, ScopePage.MissileCam, ScopePage.Approach, ScopePage.Wing, ScopePage.Strike, ScopePage.Dock, ScopePage.Objective, ScopePage.Systems };
            for (int i = 0; i < order.Length; i++)
            {
                BoardOptions one = none;
                switch (order[i])
                {
                    case ScopePage.Threat: one.PageThreat = true; break;
                    case ScopePage.Shot: one.PageShot = true; break;
                    case ScopePage.MissileCam: one.PageMissileCam = true; break;
                    case ScopePage.Approach: one.PageApproach = true; break;
                    case ScopePage.Wing: one.PageWing = true; break;
                    case ScopePage.Strike: one.PageStrike = true; break;
                    case ScopePage.Dock: one.PageDock = true; break;
                    case ScopePage.Objective: one.PageObjective = true; break;
                    default: one.PageSystems = true; break;
                }
                TestAssert.Eq(one.PageMask(), CuePriority.Bit(order[i]), "The " + order[i] + " switch is the " + order[i] + " bit");
            }
            TestAssert.Eq(order.Length, CuePriority.PageCount, "Nine switches, nine pages");

            BoardOptions dropped = BoardOptions.Default;
            dropped.PageShot = false;
            TestAssert.Eq(dropped.PageMask(), (ushort)(0x1FF & ~CuePriority.Bit(ScopePage.Shot)), "One switch off clears only its own bit");
        }

        private static void DeclutterLevels()
        {
            BoardEffective full = At(BoardDeclutter.Full).Effective();
            TestAssert.That(full.ShowLamps && full.DarkLamps && !full.ThreatLampsOnly, "Full: every lamp, the dark ones too");
            TestAssert.That(full.ShowSystemsStrip && !full.SystemsStripOnlyWhenNotNominal && !full.SystemsCriticalOnly, "Full: the systems strip always");
            TestAssert.That(full.ShowGauges && full.ShowTicker && full.Furniture, "Full: gauges, ticker and captions");
            TestAssert.Eq(full.Pages, (ushort)0x1FF, "Full: all nine pages");

            BoardEffective lean = At(BoardDeclutter.Lean).Effective();
            TestAssert.That(lean.ShowLamps && !lean.DarkLamps && !lean.ThreatLampsOnly, "Lean: lit lamps only, all of them");
            TestAssert.That(lean.ShowSystemsStrip && lean.SystemsStripOnlyWhenNotNominal && !lean.SystemsCriticalOnly, "Lean: the systems strip only while something is not nominal");
            TestAssert.That(!lean.ShowGauges && lean.ShowTicker && !lean.Furniture, "Lean: no gauge row, no captions, the ticker stays");
            TestAssert.Eq(lean.Pages, (ushort)0x1FF, "Lean: all nine pages");

            BoardEffective min = At(BoardDeclutter.Minimal).Effective();
            TestAssert.That(min.ShowLamps && !min.DarkLamps && min.ThreatLampsOnly, "Minimal: lit lamps, and only LOCK / SPKE / MSL");
            TestAssert.That(min.ShowSystemsStrip && min.SystemsStripOnlyWhenNotNominal && min.SystemsCriticalOnly, "Minimal: SYSTEMS only in its critical form");
            TestAssert.That(!min.ShowGauges && min.ShowTicker && !min.Furniture, "Minimal: no gauge row, no captions, the ticker stays");
            TestAssert.Eq(min.Pages, Mask(ScopePage.Threat, ScopePage.MissileCam, ScopePage.Shot, ScopePage.Approach, ScopePage.Systems), "Minimal: THREAT, MISSILE CAM, SHOT, APPROACH, SYSTEMS");
            TestAssert.That((min.Pages & Mask(ScopePage.Wing, ScopePage.Strike, ScopePage.Dock, ScopePage.Objective)) == 0, "Minimal: no WING, STRIKE, DOCK or OBJECTIVE");

            BoardOptions off = At(BoardDeclutter.Full);
            off.ScopeMode = BoardScopeMode.Off;
            TestAssert.Eq(off.Effective().Pages, (ushort)0, "Scope mode Off: no pages");
            TestAssert.That(off.Effective().ShowLamps && off.Effective().ShowSystemsStrip, "...the blocks around the scope stay");
        }

        /// <summary>The pages that may post a notice or call a dispenser: the switches and the level, not the scope mode.</summary>
        private static void VoicedPages()
        {
            BoardOptions o = BoardOptions.Default;
            TestAssert.Eq(o.Effective().Voiced, (ushort)0x1FF, "The defaults let every page speak");

            o.ScopeMode = BoardScopeMode.Off;
            TestAssert.That(o.Effective().Pages == 0 && o.Effective().Voiced == 0x1FF, "Scope mode Off hides the scope, not the warnings: every page may still speak");

            o.PageThreat = false;
            TestAssert.Eq((ushort)(o.Effective().Voiced & CuePriority.Bit(ScopePage.Threat)), (ushort)0, "A page whose switch is off is silent, scope mode Off or not");
            TestAssert.Eq(o.Effective().Voiced, (ushort)(0x1FF & ~CuePriority.Bit(ScopePage.Threat)), "...and only that page");
            o.ScopeMode = BoardScopeMode.Auto;
            TestAssert.Eq(o.Effective().Voiced, o.Effective().Pages, "In Auto the pages that may speak are the pages that may show");

            BoardOptions min = At(BoardDeclutter.Minimal);
            min.ScopeMode = BoardScopeMode.Off;
            TestAssert.Eq(min.Effective().Voiced, Mask(ScopePage.Threat, ScopePage.MissileCam, ScopePage.Shot, ScopePage.Approach, ScopePage.Systems), "Minimal drops the quiet pages' voice too, scope mode Off or not");
        }

        /// <summary>The compact SYSTEMS strip in cruise: the switch, and what each level adds.</summary>
        private static void SystemsStrip()
        {
            BoardEffective full = At(BoardDeclutter.Full).Effective(), lean = At(BoardDeclutter.Lean).Effective(), min = At(BoardDeclutter.Minimal).Effective();
            TestAssert.That(full.SystemsStripShows(false, false) && full.SystemsStripShows(true, false) && full.SystemsStripShows(true, true), "Full: the strip shows nominal or not");
            TestAssert.That(!lean.SystemsStripShows(false, false) && !lean.SystemsStripShows(false, true), "Lean: nothing wrong, no strip");
            TestAssert.That(lean.SystemsStripShows(true, false) && lean.SystemsStripShows(true, true), "Lean: a caution or a warning brings it");
            TestAssert.That(!min.SystemsStripShows(false, false) && !min.SystemsStripShows(true, false), "Minimal: a caution is not enough");
            TestAssert.That(min.SystemsStripShows(true, true), "Minimal: only the critical form shows");

            foreach (BoardDeclutter level in Levels)
            {
                BoardOptions o = At(level);
                o.ShowSystemsStrip = false;
                BoardEffective e = o.Effective();
                TestAssert.That(!e.SystemsStripShows(false, false) && !e.SystemsStripShows(true, false) && !e.SystemsStripShows(true, true), level + ": the switch off, no strip even in a fire");

                // The strip is the SYSTEMS page in its cruise form: with the page's switch off it is gone too, whatever the strip's own switch and the level say.
                o = At(level);
                o.PageSystems = false;
                e = o.Effective();
                TestAssert.That(!e.ShowSystemsStrip && !e.SystemsStripShows(false, false) && !e.SystemsStripShows(true, false) && !e.SystemsStripShows(true, true), level + ": SYSTEMS switched off, no strip even in a fire");
                o.PageSystems = true;
                o.PageThreat = o.PageWing = false;
                TestAssert.That(o.Effective().ShowSystemsStrip, level + ": another page's switch leaves the strip alone");
            }
        }

        private static void SwitchesStayOff()
        {
            foreach (BoardDeclutter level in Levels)
            {
                BoardOptions o = At(level);
                o.ShowLamps = o.ShowSystemsStrip = o.ShowGauges = o.ShowTicker = false;
                o.PageThreat = o.PageMissileCam = o.PageApproach = o.PageWing = o.PageSystems = false;
                BoardEffective e = o.Effective();
                TestAssert.That(!e.ShowLamps && !e.ShowSystemsStrip && !e.ShowGauges && !e.ShowTicker, level + ": a block whose switch is off stays off");
                TestAssert.Eq((ushort)(e.Pages & Mask(ScopePage.Threat, ScopePage.MissileCam, ScopePage.Approach, ScopePage.Wing, ScopePage.Systems)), (ushort)0, level + ": a page whose switch is off stays off");
                TestAssert.That((e.Pages & CuePriority.Bit(ScopePage.Shot)) != 0, level + ": SHOT, whose switch is on, is still allowed");
            }

            // Everything off, in every combination of level and scope mode: nothing comes back.
            foreach (BoardDeclutter level in Levels)
                foreach (BoardScopeMode mode in new[] { BoardScopeMode.Auto, BoardScopeMode.Off })
                {
                    BoardOptions o = At(level);
                    o.ScopeMode = mode;
                    o.ShowLamps = o.ShowSystemsStrip = o.ShowGauges = o.ShowTicker = false;
                    o.PageThreat = o.PageShot = o.PageMissileCam = o.PageApproach = o.PageWing = o.PageStrike = o.PageDock = o.PageObjective = o.PageSystems = false;
                    BoardEffective e = o.Effective();
                    TestAssert.That(e.Pages == 0 && !e.ShowLamps && !e.ShowSystemsStrip && !e.ShowGauges && !e.ShowTicker, level + "/" + mode + ": all switches off, nothing shows");
                }
        }
    }
}
