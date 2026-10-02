using NOAvionics;
using System;
using System.Collections.Generic;
using UnityEngine;

using BoscaliSummer.Modules.Wing.Domain;
using BoscaliSummer.Modules.Wing.Runtime;
using BoscaliSummer.Modules.Wing.Presentation;
using BoscaliSummer.Modules.Wing.Patches;
using BoscaliSummer.Modules.Wing.Networking;
using BoscaliSummer.Modules.Wing.Configuration;
using BoscaliSummer.Core.Math;
using BoscaliSummer.Core.Util;
using BoscaliSummer.Core.Storage;
namespace BoscaliSummer.Modules.Wing.Presentation
{
    /// <summary>BEHAVIOUR › TUNING (spec tactical v3 §5; was OPTIONS; kit v2 since phase B2): the granular side of TACTICAL's posture
    /// strip, as an <see cref="IWmcPage"/> that builds into a flow (BEHAVIOUR hosts it as its TUNING sub-page). The scope's ENGAGEMENT in
    /// full words, the whole wing's standing rules, the radio, and OVERRIDES: every element and aircraft whose doctrine differs from what
    /// it would inherit, with RESET. Only settings the AI actually reads show here: no MISSILE DEFENCE or FORMATION DISCIPLINE axes (the
    /// AI does not act on them yet). The scope is the shared <see cref="WmcScopeBar"/>. Control ids are unchanged.</summary>
    internal sealed class WmcOptions : IWmcPage
    {
        private const int MaxOverrides = ElementRoster.MaxElements - 1 + WcSnapshot.MaxMembers;

        /// <summary>One row of the doctrine picks: the kit's segmented row, what it says per segment, and the value it latches.</summary>
        private sealed class SegRow
        {
            public AvSegmented Seg;
            public string[] Tips;
            public int Value = -2;
        }

        private readonly WmcControls ids;
        private readonly List<AvPopupEntry> popupEntries = new List<AvPopupEntry>(3);
        private AvFlow flow;
        private WmcScopeBar scope;
        private AvPopup popup;
        private AvControl profileButton;
        private SegRow targets, reach, weapons, radar, fallBack, winchester, bingo, power, radio, contacts;
        private string profileShown;
        private WmcContext last;

        private AvSection overridesSection;
        private OverridesView overrides;
        private string overridesNote;
        private readonly string[] overrideShown = new string[MaxOverrides];
        // What each line's RESET sends: an element (>= 0) or an aircraft id (Element = -1).
        private readonly int[] overrideElement = new int[MaxOverrides];
        private readonly uint[] overrideMember = new uint[MaxOverrides];
        private int overridesShown = -1;

        public WmcOptions(WmcControls controls)
        {
            ids = controls;
        }

        public string Hint => "TACTICAL has the quick switches; here you tune each element and aircraft.";

        public string Alert => null;

        public void Build(AvFlow pageFlow, AvTicker ticker, int pageIndex)
        {
            flow = pageFlow;
            scope = flow.Add(new WmcScopeBar(flow.Content, ids, "opt.scope.", "SET FOR"));

            flow.Section(AvIcon.Target, "ENGAGEMENT", "SCOPE");
            profileButton = flow.Buttons(new AvControl.Spec("RESERVE ›", OpenProfiles, AvButtonStyle.Default, AvIcon.AdjustmentsHorizontal)).Controls[0];
            profileButton.Help = "Sets the rows below at once: RESERVE holds fire in close formation, ESCORT covers you, SWEEP hunts wide.";
            ids.Add("opt.profile", profileButton);
            targets = Row("TARGETS", new[] { "HOLD", "AIR", "GROUND", "BOTH", "COVER" },
                new[]
                {
                    "Hold fire: shoot only when ordered.", "Shoot at enemy aircraft in reach on their own.",
                    "Shoot at ground targets in reach on their own.", "Shoot at air and ground targets in reach on their own.",
                    "Cover: take the one air threat nearest the protected aircraft.",
                }, "opt.targets.", new[] { "hold", "air", "ground", "both", "cover" }, i => Axis(DoctrineAxis.Targets, i));
            reach = Row("RANGE", new[] { "6 KM", "12 KM" },
                new[] { "Close: shoot at what comes within 6 km on their own.", "Long: reach out to 12 km on their own." },
                "opt.reach.", new[] { "slot", "long" }, i => Axis(DoctrineAxis.Reach, i));
            weapons = Row("WEAPONS", new[] { "AUTO", "MISSILE", "GUN", "NO A-G" },
                new[] { "Every weapon aboard.", "Missiles only: no guns, no bombs.", "Guns only.", "Every weapon, at air targets only." },
                "opt.weapons.", new[] { "auto", "missiles", "guns", "noag" }, i => Axis(DoctrineAxis.Weapons, i));
            radar = Row("RADAR", new[] { "RDR ON", "SILENT", "OFF" },
                new[]
                {
                    "Radar on: the wing sees and shares its picture.",
                    "Silent: radar off until engaged, then on (enemy warners stay quiet).",
                    "Off: no radar, no radar-guided (SARH) shots. The aircraft finds little on its own.",
                }, "opt.radar.", new[] { "on", "silent", "off" }, i => Axis(DoctrineAxis.Radar, i));

            // No MISSILE DEFENCE (GUARD, RESPONSE) or FORMATION DISCIPLINE (INTERVAL, SPREAD) rows: the AI does not read those
            // doctrine axes yet, and TUNING only shows settings it actually acts on.
            flow.Section(AvIcon.AlertTriangle, "DEFENCE", "WHOLE WING");
            fallBack = Row("FALL BACK", WmcPostureActions.FallBackWords,
                new[]
                {
                    "Never fall back, however outnumbered.", "Fall back into formation facing 1.5 enemy aircraft per fighting wingman.",
                    "Fall back facing 2 enemy aircraft per fighting wingman.", "Fall back facing 3 enemy aircraft per fighting wingman.",
                }, "opt.fallback.", new[] { "never", "1.5", "2", "3" }, i => WmcPostureActions.FallBack(last, i));

            flow.Section(AvIcon.Refresh, "FOLLOW-ONS", "WHOLE WING");
            winchester = Row("WINCHESTER", WmcPostureActions.WinchesterWords,
                new[]
                {
                    "Out of ammunition in a fight: rejoin the formation.", "Out of ammunition: land and return to the reserve.",
                    "Out of ammunition: land, rearm and take off again.",
                }, "opt.winchester.", new[] { "rejoin", "rtb", "refit" }, i => WmcPostureActions.Winchester(last, i));
            bingo = Row("BINGO", WmcPostureActions.BingoWords,
                new[] { "At bingo fuel: land and return to the reserve.", "At bingo fuel: land, refuel and take off again." },
                "opt.bingo.", new[] { "rtb", "refit" }, i => WmcPostureActions.Bingo(last, i));

            flow.Section(AvIcon.Plane, "FLIGHT", "WHOLE WING");
            power = Row("POWER", new[] { "BUSTER", "GATE" },
                new[] { "Full power, no afterburner.", "Afterburner allowed." }, "opt.power.", new[] { "buster", "gate" },
                i => WmcPostureActions.Power(last, i));

            flow.Section(AvIcon.Radio, "RADIO", "YOURS");
            radio = Row("CALLS", new[] { "OFF", "ESSENTIAL", "FULL" },
                new[]
                {
                    "No wingman radio calls.", "Emergencies, tactical and status calls only.", "Everything, chatter included.",
                }, "opt.radio.", new[] { "off", "essential", "full" }, PickRadio);
            contacts = Row("CONTACTS", new[] { "CALL", "QUIET" },
                new[]
                {
                    "Wingmen call new enemy aircraft within 40 km with bearing, range, altitude and aspect.",
                    "No contact calls (SCOUT still reports ground contacts).",
                }, "opt.contacts.", new[] { "call", "quiet" }, PickContacts);

            // OVERRIDES last: its line count changes, and nothing sits under it.
            overridesSection = flow.Section(AvIcon.AlertTriangle, "OVERRIDES", "NONE");
            overridesNote = "NONE";
            overrides = flow.Add(new OverridesView(flow.Content, ticker, MaxOverrides, BindOverride));
            // Bind every line once so each RESET answers its id from the start (the rows are pooled; the extra ones stay hidden).
            overrides.Prime(MaxOverrides);
            popup = new AvPopup(flow.Content, flow.Width);
        }

        private SegRow Row(string key, string[] labels, string[] tips, string prefix, string[] keys, Action<int> pick)
        {
            var row = new SegRow { Tips = tips };
            row.Seg = flow.Add(new AvSegmented(flow.Content, key, labels, () => row.Value, pick));
            for (int i = 0; i < labels.Length; i++)
            {
                row.Seg.Options[i].Help = tips[i];
                ids.Add(prefix + keys[i], row.Seg.Options[i]);
            }
            return row;
        }

        public void Shown(WmcContext c) => profileShown = null;

        public void Refresh(WmcContext c)
        {
            last = c;
            scope.Refresh(c);
            bool host = c.CanOrder;
            string cannot = WmcPostureActions.Cannot(c);
            bool same = c.ScopeDoctrine(out WingDoctrine d);
            string pattern = c.Client ? WmcText.Unknown : same ? d.PatternName : "MIXED";
            if (pattern != profileShown)
            {
                profileShown = pattern;
                profileButton.Label = pattern + " ›";
            }
            if (profileButton.Interactable != host) profileButton.Interactable = host;
            Doctrine(targets, c, DoctrineAxis.Targets, host, cannot);
            Doctrine(reach, c, DoctrineAxis.Reach, host, cannot);
            Doctrine(weapons, c, DoctrineAxis.Weapons, host, cannot);
            Doctrine(radar, c, DoctrineAxis.Radar, host, cannot);

            WingConfig cfg = WingSettings.Instance;
            if (cfg == null) return;
            Latch(fallBack, WmcPostureActions.FallBackIndex(cfg.FallBackRatio.Value));
            Enable(fallBack, host, cannot);
            Latch(winchester, (int)cfg.AfterWinchester.Value);
            Enable(winchester, host, cannot);
            Latch(bingo, (int)cfg.AfterBingo.Value);
            Enable(bingo, host, cannot);
            Latch(power, c.Wing != null && !c.Client ? (c.Wing.AfterburnerAllowed ? 1 : 0) : -1);
            Enable(power, host, cannot);
            Latch(radio, (int)cfg.Radio.Value);
            Latch(contacts, cfg.ContactCalls.Value ? 0 : 1);
            RefreshOverrides(c);
        }

        /// <summary>Latches segment <paramref name="value"/> (-1 latches none: MIXED).</summary>
        private static void Latch(SegRow row, int value)
        {
            if (row.Value == value) return;
            row.Value = value;
            row.Seg.Refresh();
        }

        /// <summary>Enables the row for the host; a disabled row says why in every segment's help.</summary>
        private static void Enable(SegRow row, bool on, string why)
        {
            AvControl[] options = row.Seg.Options;
            for (int i = 0; i < options.Length; i++)
            {
                if (options[i].Interactable != on) options[i].Interactable = on;
                string tip = on ? row.Tips[i] : why;
                if (options[i].Help != tip) options[i].Help = tip;
            }
        }

        private static void Doctrine(SegRow row, WmcContext c, DoctrineAxis axis, bool on, string why)
        {
            Latch(row, c.CanOrder ? c.ScopeValue(axis) : -1);
            Enable(row, on, why);
        }

        /// <summary>Elements B-D that differ from the wing, then aircraft that differ from their element (host).</summary>
        private void RefreshOverrides(WmcContext c)
        {
            int n = 0;
            bool dirty = false;
            WingService w = c.CanOrder ? c.Wing : null;
            if (w != null)
            {
                for (int e = 1; e < ElementRoster.MaxElements && n < MaxOverrides; e++)
                {
                    if (!w.Roster.InUse(e)) continue;
                    string diff = DoctrineDiff.Words(w.DoctrineOf(e), w.Doctrine);
                    if (diff.Length == 0) continue;
                    dirty |= SetOverride(n++, ElementRoster.Letter(e) + " · " + w.Roster.Name(e) + " · " + diff, e, 0u);
                }
                foreach (WingMember m in w.Members)
                {
                    if (n >= MaxOverrides) break;
                    if (m.Released || !m.Alive || (object)m.Aircraft == null) continue;
                    string diff = DoctrineDiff.Words(w.DoctrineFor(m), w.DoctrineOf(w.ElementOf(m)));
                    if (diff.Length == 0) continue;
                    dirty |= SetOverride(n++, "#" + AvNum.Fixed(m.Number, 0) + " · " + diff, -1, m.Aircraft.persistentID.Id);
                }
            }
            if (n != overridesShown) dirty = true;
            if (!dirty) return;
            overridesShown = n;
            overrides.SetCount(n);
            string note = n > 0 ? AvNum.Fixed(n, 0) + " DIFFER" : "NONE";
            if (note != overridesNote)
            {
                overridesNote = note;
                overridesSection.SetCaption(note);
            }
            flow.RequestRelayout();
        }

        private bool SetOverride(int i, string text, int element, uint member)
        {
            overrideElement[i] = element;
            overrideMember[i] = member;
            if (text == overrideShown[i]) return false;
            overrideShown[i] = text;
            return true;
        }

        /// <summary>A pooled OVERRIDES line: its words, and a RESET (made and registered the first time the line is bound).</summary>
        private void BindOverride(int item, AvRow row)
        {
            row.Set(overrideShown[item] ?? "", "", "", AvState.Caution);
            if (overrides.Resets[item] != null) return;
            int k = item;
            AvControl reset = row.AddTrailing(new AvControl.Spec("RESET", () => Reset(k)));
            if (reset == null) return;
            reset.Help = "Back to what it inherits: an element to the wing's doctrine, an aircraft to its element's.";
            overrides.Resets[item] = reset;
            ids.Add("opt.override" + item + ".reset", reset);
        }

        /// <summary>An element back to the wing's doctrine; an aircraft back to its element's (a whole profile, since the executor
        /// refuses element-wide axes one aircraft at a time). ponytail: no new order kind — the values match, so the line goes.</summary>
        private void Reset(int i)
        {
            WingService w = last?.Wing;
            if (w == null) return;
            int e = overrideElement[i];
            if (e >= 0)
            {
                WmcUi.Order(last, () => WingOrders.Run(new WingOrder
                {
                    Kind = OrderKind.SetDoctrine, Text = w.Doctrine.ToString(), Scope = WingScope.OfElement(e),
                }));
                return;
            }
            WingMember m = last.MemberOf(overrideMember[i]);
            if (m == null) return;
            WmcUi.Order(last, () => WingOrders.Run(new WingOrder
            {
                Kind = OrderKind.SetDoctrine, Text = w.DoctrineOf(w.ElementOf(m)).ToString(), Scope = WingScope.OfMembers(overrideMember[i]),
            }));
        }

        private void OpenProfiles()
        {
            if (last == null || !last.CanOrder) return;
            string current = last.ScopeDoctrine(out WingDoctrine scoped) ? scoped.PatternName : null;
            popupEntries.Clear();
            foreach (WingDoctrine d in WmcPostureActions.Profiles)
                popupEntries.Add(new AvPopupEntry(d.PatternName, null, d.PatternName == current));
            popup.Show(WmcPopup.Area(flow.Content, profileButton.Rect, popupEntries.Count), popupEntries, i => WmcPostureActions.Profile(last, i));
        }

        private void Axis(DoctrineAxis axis, int i) => WmcPostureActions.Axis(last, axis, i);

        // The radio is this player's own: no host needed.
        private void PickRadio(int i)
        {
            if (WingSettings.Instance != null) WingSettings.Instance.Radio.Value = (RadioLevel)i;
        }

        private void PickContacts(int i)
        {
            if (WingSettings.Instance != null) WingSettings.Instance.ContactCalls.Value = i == 0;
        }

        /// <summary>The OVERRIDES lines (a pooled <see cref="AvList"/> that never pages: it holds the most a wing can have), or the
        /// one line that says there are none; only the one that shows takes room.</summary>
        private sealed class OverridesView : AvPart
        {
            private readonly AvList list;
            private readonly WmcLines none;
            private int count;

            public readonly AvControl[] Resets;

            public OverridesView(RectTransform parent, AvTicker ticker, int max, Action<int, AvRow> binder)
            {
                Rect = AvLay.Child(parent, "Overrides");
                Resets = new AvControl[max];
                none = new WmcLines(Rect, 1, AvTextRole.ProseSmall, "row-sub");
                none.Set(0, "Every element and aircraft flies what it inherits.");
                ticker?.Register(none);
                list = new AvList(Rect, ticker, max, binder);
                SetCount(0);
            }

            /// <summary>Binds all <paramref name="max"/> lines once, then shows none.</summary>
            public void Prime(int max)
            {
                list.SetCount(max);
                SetCount(0);
            }

            public void SetCount(int n)
            {
                count = n;
                list.SetCount(n);
                list.Rect.gameObject.SetActive(n > 0);
                none.Rect.gameObject.SetActive(n == 0);
            }

            public override float Measure(float width) => count > 0 ? list.Measure(width) : none.Measure(width);

            public override void Place(AvSlot s)
            {
                base.Place(s);
                var inner = new AvSlot(0f, 0f, s.W, s.H);
                if (count > 0) list.Place(inner);
                else none.Place(inner);
            }

            public override void Restyle() => none.Restyle();
        }
    }
}
