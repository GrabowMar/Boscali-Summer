using NOAvionics;
using System;
using System.Collections.Generic;
using UnityEngine;

using BoscaliSummer.Modules.Wing.Domain;
using BoscaliSummer.Modules.Wing.Domain.Pure;
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
    /// <summary>BEHAVIOUR › STANCES (spec 2026-10-04 §4.1; was TUNING): which stance each element flies, the stance list and the full
    /// editor for the selected one, and the player's radio. A stance is a named doctrine preset (<see cref="Stance"/>): ENGAGE (targets,
    /// range, weapons, radar), DEFEND (missile guard, response, spread, fall back), FLY · AFTER (interval, power, winchester, bingo),
    /// the slot (wing key + 1-6) it sits in and the orders it is the default for. Built-ins are read-only (SAVE AS copies one); yours
    /// save to stances.user.json at each pick. APPLY flies the stance for the scope (<see cref="WmcStanceActions.Apply"/>).
    /// EDIT › LIVE keeps what TUNING did: the same rows then set the scope's own doctrine and the wing's follow-ons at once. IN USE shows
    /// each element's stance with its changed axes and every per-aircraft override, each with a clear. Control ids are TUNING's.</summary>
    internal sealed class WmcOptions : IWmcPage
    {
        private const int MaxInUse = ElementRoster.MaxElements + WcSnapshot.MaxMembers;
        private const int MaxNameChars = 16;
        private static readonly string[] OrderKeys = { "move", "orbit", "patrol", "cap", "sweep", "escort", "scout", "attack" };
        private const string BuiltInTip = "Built-in: SAVE AS makes a copy you can edit.";

        private enum Kind { Axis, FallBack, Winchester, Bingo, Power, Radio, Contacts }

        /// <summary>One row of the picks: the kit's segmented row, what it says per segment, and the value it latches.</summary>
        private sealed class SegRow
        {
            public AvSegmented Seg;
            public string[] Tips;
            public int Value = -2;
            public Kind Kind;
            public DoctrineAxis Axis;
            /// <summary>Segments before the first value (a follow-on's KEEP, which leaves the setting as it is).</summary>
            public int Shift;
        }

        private readonly WmcControls ids;
        private readonly List<WmcCollapsible> stanceOnly = new List<WmcCollapsible>(12);
        private readonly List<SegRow> rows = new List<SegRow>(14);
        private readonly ConfirmGate gate = new ConfirmGate();
        private AvFlow flow;
        private WmcScopeBar scope;
        private AvSegmented modeSeg;
        private AvSection inUseSection, editSection;
        private InUseView inUse;
        private AvList stanceList;
        private AvControl newButton, copyButton, deleteButton, applyButton;
        private WmcNameField nameField;
        private WmcLines says;
        private AvSegmented slotSeg;
        private AvControl[] defaults;
        private WmcContext last;

        private bool live;
        private string selId;
        private int version = 1, serial = 1, slotValue = -1, modeValue;
        private long listKey = long.MinValue;
        private string saysShown, captionShown, applyShown;

        // IN USE: what each line says, and what its CLEAR sends — an element (>= 0) or an aircraft id (Element = -1).
        private readonly string[] inName = new string[MaxInUse], inSub = new string[MaxInUse];
        private readonly AvState[] inState = new AvState[MaxInUse];
        private readonly bool[] inClear = new bool[MaxInUse];
        private readonly int[] inElement = new int[MaxInUse];
        private readonly uint[] inMember = new uint[MaxInUse];
        private int inUseShown = -1;
        private long inUseKey = long.MinValue;
        private readonly int[] usedBy = new int[StanceBook.MaxStances];

        public WmcOptions(WmcControls controls)
        {
            ids = controls;
        }

        public string Hint => live ? "LIVE: these rows set what the scope flies now. TACTICAL has the quick switches."
            : "A stance is how the wing fights and flies. Pick one, edit it (SAVE AS for a built-in), APPLY it to the scope.";

        public string Alert => null;

        /// <summary>The selected stance (automation, the page itself).</summary>
        public Stance Selected => Book.Find(selId);

        private static StanceBook Book => WmcStanceActions.Book;

        public void Build(AvFlow pageFlow, AvTicker ticker, int pageIndex)
        {
            flow = pageFlow;
            scope = flow.Add(new WmcScopeBar(flow.Content, ids, "opt.scope.", "APPLY TO"));

            inUseSection = flow.Section(AvIcon.Target, "IN USE", "");
            inUse = flow.Add(new InUseView(flow.Content, ticker, MaxInUse, BindInUse));
            // Bind every line once so each CLEAR answers its id from the start (the rows are pooled; the extra ones stay hidden).
            inUse.Prime(MaxInUse);

            modeSeg = flow.Add(new AvSegmented(flow.Content, "EDIT", new[] { "STANCE", "LIVE" }, () => modeValue, PickMode));
            modeSeg.Options[0].Help = "Edit a stance, then APPLY it.";
            modeSeg.Options[1].Help = "Set the scope's own doctrine and the wing's follow-ons now (what TUNING did).";
            ids.Add("opt.mode.stance", modeSeg.Options[0]);
            ids.Add("opt.mode.live", modeSeg.Options[1]);

            // ---- the stance list
            Gated(new AvSection(flow.Content, AvIcon.ListDetails, "STANCES", "WING KEY + 1-6"));
            stanceList = new AvList(flow.Content, ticker, StanceBook.MaxStances, BindStance);
            stanceList.RowClicked = SelectStance;
            Gated(stanceList);
            var bar = new AvButtons(flow.Content, new[]
            {
                new AvControl.Spec("+ NEW", NewStance, AvButtonStyle.Default, AvIcon.Plus),
                new AvControl.Spec("SAVE AS", CopyStance, AvButtonStyle.Default, AvIcon.Bookmark),
                new AvControl.Spec("DELETE", DeleteStance, AvButtonStyle.Danger, AvIcon.X),
            });
            newButton = bar.Controls[0];
            copyButton = bar.Controls[1];
            deleteButton = bar.Controls[2];
            newButton.Help = "A new stance, a copy of this one.";
            copyButton.Help = "A copy of this stance you can edit (a built-in is never changed).";
            deleteButton.Help = "Remove this stance (yours only; asks first).";
            ids.Add("opt.stance.new", newButton);
            ids.Add("opt.stance.copy", copyButton);
            ids.Add("opt.stance.delete", deleteButton);
            Gated(bar);

            // ---- the editor
            editSection = new AvSection(flow.Content, AvIcon.Pencil, "EDIT", "");
            flow.Add(editSection);
            nameField = new WmcNameField(flow.Content, "NAME", MaxNameChars, CommitName, "The stance's name: Enter keeps it (16 characters at most).");
            Gated(nameField);
            says = new WmcLines(flow.Content, 1);
            Gated(says);

            flow.Section(AvIcon.Target, "ENGAGE", "");
            Row("TARGETS", new[] { "HOLD", "AIR", "GROUND", "BOTH", "COVER" },
                new[]
                {
                    "Hold fire: shoot only when ordered.", "Shoot at enemy aircraft in reach on their own.",
                    "Shoot at ground targets in reach on their own.", "Shoot at air and ground targets in reach on their own.",
                    "Cover: take the one air threat nearest the protected aircraft.",
                }, "opt.targets.", new[] { "hold", "air", "ground", "both", "cover" }, Kind.Axis, DoctrineAxis.Targets);
            Row("RANGE", new[] { "6 KM", "12 KM" },
                new[] { "Close: shoot at what comes within 6 km on their own.", "Long: reach out to 12 km on their own." },
                "opt.reach.", new[] { "slot", "long" }, Kind.Axis, DoctrineAxis.Reach);
            Row("WEAPONS", new[] { "AUTO", "MISSILE", "GUN", "NO A-G" },
                new[] { "Every weapon aboard.", "Missiles only: no guns, no bombs.", "Guns only.", "Every weapon, at air targets only." },
                "opt.weapons.", new[] { "auto", "missiles", "guns", "noag" }, Kind.Axis, DoctrineAxis.Weapons);
            Row("RADAR", new[] { "RDR ON", "SILENT", "OFF" },
                new[]
                {
                    "Radar on: the wing sees and shares its picture.",
                    "Silent: radar off until engaged, then on (enemy warners stay quiet).",
                    "Off: no radar, no radar-guided (SARH) shots. The aircraft finds little on its own.",
                }, "opt.radar.", new[] { "on", "silent", "off" }, Kind.Axis, DoctrineAxis.Radar);

            flow.Section(AvIcon.AlertTriangle, "DEFEND", "");
            Row("MSL GUARD", new[] { "OFF", "SELF", "WING", "LEAD" },
                new[]
                {
                    "No missile guard: an aircraft only breaks and expends countermeasures for itself.",
                    "Each aircraft guards itself.", "An anti-missile store may protect any wingman.", "The leader is guarded first.",
                }, "opt.guard.", new[] { "off", "self", "wing", "lead" }, Kind.Axis, DoctrineAxis.Guard);
            Row("RESPONSE", new[] { "BREAK", "PRESS" },
                new[] { "Break: notch, beam or flare out of a missile.", "Press: hold heading to kill a radar-guided missile's launcher." },
                "opt.response.", new[] { "break", "press" }, Kind.Axis, DoctrineAxis.Response);
            Row("SPREAD", new[] { "OFF", "ON" },
                new[] { "Stay in formation when threatened.", "Spread out when threatened." },
                "opt.spread.", new[] { "off", "on" }, Kind.Axis, DoctrineAxis.Spread);
            Row("FALL BACK", new[] { "KEEP", "NEVER", "1.5 : 1", "2 : 1", "3 : 1" },
                new[]
                {
                    "Leave the wing's fall-back rule as it is.", "Never fall back, however outnumbered.",
                    "Fall back into formation facing 1.5 enemy aircraft per fighting wingman.",
                    "Fall back facing 2 enemy aircraft per fighting wingman.", "Fall back facing 3 enemy aircraft per fighting wingman.",
                }, "opt.fallback.", new[] { "keep", "never", "1.5", "2", "3" }, Kind.FallBack, DoctrineAxis.Guard, 1);

            flow.Section(AvIcon.Plane, "FLY · AFTER", "");
            Row("INTERVAL", new[] { "CLOSE", "STD", "OPEN" },
                new[] { "Close: stick to the slot.", "Standard slot interval.", "Open: a wider slot interval." },
                "opt.interval.", new[] { "close", "std", "open" }, Kind.Axis, DoctrineAxis.Interval);
            Row("POWER", new[] { "BUSTER", "GATE" },
                new[] { "Full power, no afterburner (the whole wing, at once: not kept in a stance).", "Afterburner allowed (the whole wing, at once: not kept in a stance)." },
                "opt.power.", new[] { "buster", "gate" }, Kind.Power, DoctrineAxis.Guard);
            Row("WINCHESTER", new[] { "KEEP", "REJOIN", "RTB", "REFIT" },
                new[]
                {
                    "Leave the wing's winchester rule as it is.", "Out of ammunition in a fight: rejoin the formation.",
                    "Out of ammunition: land and return to the reserve.", "Out of ammunition: land, rearm and take off again.",
                }, "opt.winchester.", new[] { "keep", "rejoin", "rtb", "refit" }, Kind.Winchester, DoctrineAxis.Guard, 1);
            Row("BINGO", new[] { "KEEP", "RTB", "REFIT" },
                new[]
                {
                    "Leave the wing's bingo rule as it is.", "At bingo fuel: land and return to the reserve.",
                    "At bingo fuel: land, refuel and take off again.",
                }, "opt.bingo.", new[] { "keep", "rtb", "refit" }, Kind.Bingo, DoctrineAxis.Guard, 1);

            // ---- slot, default orders, APPLY
            var slotLabels = new[] { "W+1", "W+2", "W+3", "W+4", "W+5", "W+6" };
            slotSeg = new AvSegmented(flow.Content, "SLOT", slotLabels, () => slotValue, PickSlot);
            for (int i = 0; i < slotLabels.Length; i++)
            {
                slotSeg.Options[i].Help = "Wing key + " + (i + 1) + " flies this stance (ORDERS' slot " + (i + 1) + ").";
                ids.Add("opt.slot." + (i + 1), slotSeg.Options[i]);
            }
            Gated(slotSeg);
            Gated(new AvSection(flow.Content, AvIcon.Flag, "DEFAULT FOR", "ORDERS"));
            defaults = new AvControl[OrderKeys.Length];
            for (int row = 0; row < 2; row++)
            {
                var specs = new AvControl.Spec[4];
                for (int i = 0; i < 4; i++)
                {
                    string key = OrderKeys[row * 4 + i];
                    specs[i] = new AvControl.Spec(key.ToUpperInvariant(), () => ToggleDefault(key));
                }
                var chips = new AvButtons(flow.Content, specs);
                for (int i = 0; i < 4; i++)
                {
                    defaults[row * 4 + i] = chips.Controls[i];
                    chips.Controls[i].Help = "This stance goes with a " + OrderKeys[row * 4 + i] + " order unless you pick another.";
                    ids.Add("opt.default." + OrderKeys[row * 4 + i], chips.Controls[i]);
                }
                Gated(chips);
            }
            var apply = new AvButtons(flow.Content, new[] { new AvControl.Spec("APPLY", Apply, AvButtonStyle.Primary, AvIcon.PlayerPlay) });
            applyButton = apply.Controls[0];
            applyButton.Help = "Fly this stance: the scope's doctrine, and for the whole wing its fall-back, winchester and bingo too.";
            ids.Add("opt.apply", applyButton);
            Gated(apply);

            // ---- the player's radio
            flow.Section(AvIcon.Radio, "RADIO", "YOURS");
            Row("CALLS", new[] { "OFF", "ESSENTIAL", "FULL" },
                new[] { "No wingman radio calls.", "Emergencies, tactical and status calls only.", "Everything, chatter included." },
                "opt.radio.", new[] { "off", "essential", "full" }, Kind.Radio, DoctrineAxis.Guard);
            Row("CONTACTS", new[] { "CALL", "QUIET" },
                new[]
                {
                    "Wingmen call new enemy aircraft within 40 km with bearing, range, altitude and aspect.",
                    "No contact calls (SCOUT still reports ground contacts).",
                }, "opt.contacts.", new[] { "call", "quiet" }, Kind.Contacts, DoctrineAxis.Guard);
        }

        /// <summary>A part that only shows while a stance is being edited (EDIT › STANCE).</summary>
        private void Gated(AvPart part) => stanceOnly.Add(flow.Add(new WmcCollapsible(part)));

        private SegRow Row(string key, string[] labels, string[] tips, string prefix, string[] keys, Kind kind, DoctrineAxis axis, int shift = 0)
        {
            var row = new SegRow { Tips = tips, Kind = kind, Axis = axis, Shift = shift };
            row.Seg = flow.Add(new AvSegmented(flow.Content, key, labels, () => row.Value, i => Pick(row, i)));
            for (int i = 0; i < labels.Length; i++)
            {
                row.Seg.Options[i].Help = tips[i];
                ids.Add(prefix + keys[i], row.Seg.Options[i]);
            }
            rows.Add(row);
            return row;
        }

        public void Shown(WmcContext c)
        {
            listKey = long.MinValue;
            inUseKey = long.MinValue;
            if (Book.Find(selId) != null) return;
            selId = null;
            if (c != null && c.CanOrder)
            {
                c.ScopeDoctrine(out WingDoctrine d);
                Stance m = WmcStanceActions.Matching(StanceDoctrine.Axes(d));
                if (m != null) selId = m.Id;
            }
        }

        // ---------------------------------------------------------------- refresh

        public void Refresh(WmcContext c)
        {
            last = c;
            scope.Refresh(c);
            if (Book.Find(selId) == null)
            {
                selId = Book.All.Count > 0 ? Book.All[0].Id : null;
                listKey = long.MinValue;
            }
            bool changed = false;
            foreach (WmcCollapsible g in stanceOnly) changed |= g.Set(!live);
            RefreshInUse(c);
            RefreshEditor(c, ref changed);
            if (changed) flow.RequestRelayout();
        }

        private void RefreshEditor(WmcContext c, ref bool changed)
        {
            Stance sel = Selected;
            bool host = c.CanOrder, own = sel != null && !sel.BuiltIn;
            WingConfig cfg = WingSettings.Instance;
            modeValue = live ? 1 : 0;
            modeSeg.Refresh();
            Enable(modeSeg.Options[1], host, WmcPostureActions.Cannot(c));

            string caption = live ? "LIVE · " + c.ScopeLabel : sel == null ? "" : sel.Name + (sel.BuiltIn ? " · BUILT-IN" : " · YOURS");
            if (caption != captionShown)
            {
                captionShown = caption;
                editSection.SetCaption(caption);
            }

            if (!live)
            {
                long key = version * 131L + (sel == null ? 0 : sel.Name.GetHashCode()) + Book.All.Count * 7L + (gate.IsArmed("del" + selId, Time.unscaledTime) ? 1 : 0);
                if (key != listKey)
                {
                    listKey = key;
                    stanceList.SetCount(Book.All.Count);
                    changed = true;
                }
                nameField.EditingId = selId;
                nameField.SetText(sel?.Name ?? "");
                nameField.SetInteractable(own);
                string text = sel == null ? "" : StanceWords.Says(sel.Axes, sel.FallBack, sel.Winchester, sel.Bingo);
                if (text != saysShown)
                {
                    saysShown = text;
                    says.Set(0, text);
                    changed = true;
                }
                Enable(newButton, Book.All.Count < StanceBook.MaxStances, "The stance book is full.");
                copyButton.Label = sel != null && sel.BuiltIn ? "SAVE AS" : "DUPLICATE";
                Enable(copyButton, sel != null && Book.All.Count < StanceBook.MaxStances, "The stance book is full.");
                deleteButton.Label = gate.IsArmed("del" + selId, Time.unscaledTime) ? "DELETE?" : "DELETE";
                Enable(deleteButton, own, "A built-in stance cannot be deleted.");
                slotValue = SlotOf(sel);
                slotSeg.Refresh();
                for (int i = 0; i < defaults.Length; i++) defaults[i].Latched = sel != null && Book.DefaultFor(OrderKeys[i]) == sel.Id;
                string apply = host ? "APPLY TO " + c.ScopeLabel : "APPLY";
                if (apply != applyShown)
                {
                    applyShown = apply;
                    applyButton.Label = apply;
                }
                Enable(applyButton, host && sel != null, WmcPostureActions.Cannot(c));
            }

            foreach (SegRow r in rows)
            {
                int v = HasValue(r, c, sel, cfg) ? RowValue(r, c, sel, cfg) : -1;
                if (r.Value != v)
                {
                    r.Value = v;
                    r.Seg.Refresh();
                }
                RowEnable(r, c, own, host);
            }
        }

        /// <summary>Whether the row has a value to show at all (a client reads no live doctrine).</summary>
        private bool HasValue(SegRow r, WmcContext c, Stance sel, WingConfig cfg)
        {
            switch (r.Kind)
            {
                case Kind.Radio:
                case Kind.Contacts: return cfg != null;
                case Kind.Power: return cfg != null && c.Wing != null && !c.Client;
                case Kind.Axis: return live ? c.CanOrder : sel != null;
                default: return live ? cfg != null : sel != null;
            }
        }

        /// <summary>The segment the row latches: the live value (EDIT › LIVE), the selected stance's, or the setting itself.</summary>
        private int RowValue(SegRow r, WmcContext c, Stance sel, WingConfig cfg)
        {
            switch (r.Kind)
            {
                case Kind.Radio: return (int)cfg.Radio.Value;
                case Kind.Contacts: return cfg.ContactCalls.Value ? 0 : 1;
                case Kind.Power: return c.Wing.AfterburnerAllowed ? 1 : 0;
                case Kind.Axis: return live ? c.ScopeValue(r.Axis) : sel.Axes[(int)r.Axis];
                case Kind.FallBack: return live ? Shifted(r, WmcPostureActions.FallBackIndex(cfg.FallBackRatio.Value)) : Shifted(r, sel.FallBack);
                case Kind.Winchester: return live ? Shifted(r, (int)cfg.AfterWinchester.Value) : Shifted(r, sel.Winchester);
                default: return live ? Shifted(r, (int)cfg.AfterBingo.Value) : Shifted(r, sel.Bingo);
            }
        }

        /// <summary>A follow-on's segment: its value after the KEEP segment; a stance's -1 (leave it) is KEEP, a live unknown is none.</summary>
        private int Shifted(SegRow r, int value) => value >= 0 ? value + r.Shift : !live && r.Shift > 0 ? 0 : -1;

        /// <summary>Whether the row's segments answer a press, and what each says when it does not.</summary>
        private void RowEnable(SegRow r, WmcContext c, bool own, bool host)
        {
            bool on;
            string why = null;
            switch (r.Kind)
            {
                case Kind.Radio:
                case Kind.Contacts: on = true; break;
                case Kind.Power: on = host; why = WmcPostureActions.Cannot(c); break;
                default:
                    if (live) { on = host; why = WmcPostureActions.Cannot(c); }
                    else { on = own; why = BuiltInTip; }
                    break;
            }
            AvControl[] options = r.Seg.Options;
            for (int i = 0; i < options.Length; i++)
            {
                // In LIVE, KEEP has nothing to keep: the setting is changed right there.
                bool optionOn = on && !(live && r.Shift > 0 && i == 0);
                if (options[i].Interactable != optionOn) options[i].Interactable = optionOn;
                string tip = on ? (live && r.Shift > 0 && i == 0 ? "Not in LIVE: pick a value." : r.Tips[i]) : why;
                if (options[i].Help != tip) options[i].Help = tip;
            }
        }

        private static void Enable(AvControl b, bool on, string why = null)
        {
            if (b.Interactable != on) b.Interactable = on;
            if (!on && why != null && b.Help != why) b.Help = why;
        }

        // ---------------------------------------------------------------- picks

        private void Pick(SegRow r, int i)
        {
            WmcContext c = last;
            if (c == null) return;
            switch (r.Kind)
            {
                case Kind.Radio:
                    if (WingSettings.Instance != null) WingSettings.Instance.Radio.Value = (RadioLevel)i;
                    return;
                case Kind.Contacts:
                    if (WingSettings.Instance != null) WingSettings.Instance.ContactCalls.Value = i == 0;
                    return;
                case Kind.Power:
                    WmcPostureActions.Power(c, i);
                    return;
            }
            if (live)
            {
                int v = i - r.Shift;
                if (v < 0) return;
                if (r.Kind == Kind.Axis) WmcPostureActions.Axis(c, r.Axis, v);
                else if (r.Kind == Kind.FallBack) WmcPostureActions.FallBack(c, v);
                else if (r.Kind == Kind.Winchester) WmcPostureActions.Winchester(c, v);
                else WmcPostureActions.Bingo(c, v);
                return;
            }
            Stance s = Selected;
            if (s == null) return;
            if (s.BuiltIn)
            {
                WingToast.Show(BuiltInTip);
                return;
            }
            if (r.Kind == Kind.Axis) s.Axes[(int)r.Axis] = (byte)i;
            else if (r.Kind == Kind.FallBack) s.FallBack = (sbyte)(i - r.Shift);
            else if (r.Kind == Kind.Winchester) s.Winchester = (sbyte)(i - r.Shift);
            else s.Bingo = (sbyte)(i - r.Shift);
            Edited();
        }

        private void PickMode(int i)
        {
            live = i == 1;
            AvPopup.CloseAny();
            listKey = long.MinValue;
            captionShown = null;
            if (last != null) Refresh(last);
            flow.RequestRelayout();
        }

        private void SelectStance(int i)
        {
            if (i < 0 || i >= Book.All.Count) return;
            selId = Book.All[i].Id;
            gate.Press("", Time.unscaledTime);
            listKey = long.MinValue;
            saysShown = null;
            if (last != null) Refresh(last);
        }

        private void Edited()
        {
            if (!WmcStanceFiles.Save()) WingToast.Show("Could not save your stances (see the log)");
            version++;
            saysShown = null;
            listKey = long.MinValue;
            if (last != null) Refresh(last);
        }

        private string NewId()
        {
            string id;
            do id = "user" + serial++;
            while (Book.Find(id) != null);
            return id;
        }

        private string NewName(string basis)
        {
            string stem = basis.Length > MaxNameChars - 2 ? basis.Substring(0, MaxNameChars - 2) : basis;
            for (int n = 2; n < 100; n++)
            {
                string name = stem + " " + n;
                bool taken = false;
                foreach (Stance s in Book.All) taken |= s.Name == name;
                if (!taken) return name;
            }
            return stem;
        }

        private void NewStance()
        {
            Stance basis = Selected;
            var s = new Stance { Id = NewId(), Name = NewName("STANCE"), Axes = basis != null ? (byte[])basis.Axes.Clone() : StanceDoctrine.Axes(WingDoctrine.Reserve) };
            if (basis != null) { s.FallBack = basis.FallBack; s.Winchester = basis.Winchester; s.Bingo = basis.Bingo; }
            AddAndSelect(s);
        }

        /// <summary>SAVE AS (a built-in) and DUPLICATE (yours): the same copy.</summary>
        private void CopyStance()
        {
            Stance basis = Selected;
            if (basis == null) return;
            Stance s = basis.Copy();
            s.Id = NewId();
            s.Name = NewName(basis.Name);
            s.BuiltIn = false;
            AddAndSelect(s);
        }

        private void AddAndSelect(Stance s)
        {
            if (!Book.Add(s))
            {
                WingToast.Show("The stance book is full (" + StanceBook.MaxStances + ")");
                return;
            }
            selId = s.Id;
            Edited();
            WingToast.Show("Made " + s.Name);
        }

        private void DeleteStance()
        {
            Stance s = Selected;
            if (s == null || s.BuiltIn) return;
            if (!gate.Press("del" + s.Id, Time.unscaledTime))
            {
                WingToast.Show("DELETE again to remove " + s.Name);
                listKey = long.MinValue;
                if (last != null) Refresh(last);
                return;
            }
            int at = IndexOf(s.Id);
            Book.Remove(s.Id);
            selId = Book.All.Count == 0 ? null : Book.All[Mathf.Clamp(at - 1, 0, Book.All.Count - 1)].Id;
            Edited();
        }

        private static int IndexOf(string id)
        {
            for (int i = 0; i < Book.All.Count; i++) if (Book.All[i].Id == id) return i;
            return -1;
        }

        private void CommitName(string id, string typed)
        {
            Stance s = Book.Find(id);
            string name = (typed ?? "").Trim().ToUpperInvariant();
            if (s == null || s.BuiltIn || name.Length == 0) return;
            s.Name = name.Length > MaxNameChars ? name.Substring(0, MaxNameChars) : name;
            Edited();
        }

        private void PickSlot(int i)
        {
            Stance s = Selected;
            if (s == null || !Book.Assign(i, s.Id)) return;
            Edited();
            WingToast.Show(s.Name + " is on wing key + " + (i + 1));
        }

        private static int SlotOf(Stance s)
        {
            if (s == null) return -1;
            for (int i = 0; i < StanceBook.Slots; i++)
                if (Book.Slot(i) == s) return i;
            return -1;
        }

        private void ToggleDefault(string key)
        {
            Stance s = Selected;
            if (s == null) return;
            Book.SetDefault(key, Book.DefaultFor(key) == s.Id ? null : s.Id);
            Edited();
        }

        private void Apply()
        {
            Stance s = Selected;
            if (s != null) WmcStanceActions.Apply(last, s);
        }

        // ---------------------------------------------------------------- the stance list

        private void BindStance(int item, AvRow row)
        {
            if (item >= Book.All.Count) return;
            Stance s = Book.All[item];
            int slot = SlotOf(s);
            int used = usedBy[Mathf.Min(item, usedBy.Length - 1)];
            string by = "";
            for (int e = 0; e < ElementRoster.MaxElements && used != 0; e++)
                if ((used & (1 << e)) != 0) by += (by.Length > 0 ? " " : "") + ElementRoster.Letter(e);
            row.Set(s.Name, (s.BuiltIn ? "BUILT-IN · " : "") + StanceWords.Brief(s.Axes) + (by.Length > 0 ? " · IN USE " + by : ""),
                slot >= 0 ? "W+" + (slot + 1) : "", used != 0 ? AvState.Ready : AvState.Info);
            row.Armed = s.Id == selId;
            ids.Add("opt.stance.row" + item, row);
            row.Help = "Edit this stance.";
        }

        // ---------------------------------------------------------------- IN USE

        /// <summary>Every element in use with the stance it flies (its changed axes beside it), then every aircraft that differs from its
        /// element, each with a CLEAR where there is something to clear (host).</summary>
        private void RefreshInUse(WmcContext c)
        {
            WingService w = c.CanOrder ? c.Wing : null;
            long key = version * 7919L + Book.All.Count * 31L;
            if (w != null)
            {
                for (int e = 0; e < ElementRoster.MaxElements; e++)
                {
                    if (!w.Roster.InUse(e)) continue;
                    WingDoctrine d = w.DoctrineOf(e);
                    key = key * 31L + e * 17L + (int)d.Targets * 5L + (int)d.Reach * 3L + (int)d.Weapons * 7L + (int)d.Radar * 11L + (int)d.Guard * 13L
                          + (int)d.Response * 19L + (int)d.Interval * 23L + (d.SpreadWhenThreatened ? 29L : 0L);
                }
                foreach (WingMember m in w.Members)
                {
                    if (m.Released || !m.Alive || (object)m.Aircraft == null) continue;
                    WingDoctrine d = w.DoctrineFor(m);
                    key = key * 31L + m.Number * 3L + (int)d.Targets + (int)d.Reach * 2L + (int)d.Weapons * 5L + (int)d.Radar * 7L;
                }
            }
            if (key == inUseKey) return;
            inUseKey = key;
            Array.Clear(usedBy, 0, usedBy.Length);
            int n = 0;
            if (w != null)
            {
                for (int e = 0; e < ElementRoster.MaxElements && n < MaxInUse; e++)
                {
                    if (!w.Roster.InUse(e)) continue;
                    byte[] axes = StanceDoctrine.Axes(w.DoctrineOf(e));
                    Stance s = StanceWords.Closest(Book.All, axes, out int mask);
                    int at = s == null ? -1 : IndexOf(s.Id);
                    if (at >= 0 && mask == 0) usedBy[at] |= 1 << e;
                    bool offWing = e > 0 && StanceDiff.Mask(axes, StanceDoctrine.Axes(w.Doctrine)) != 0;
                    string name = ElementRoster.Letter(e) + " · " + (s == null ? "CUSTOM" : s.Name + (mask != 0 ? " *" : ""));
                    SetInUse(n++, name, mask != 0 ? StanceWords.ChangedWords(mask, axes) : "", mask != 0 ? AvState.Caution : AvState.Ready, offWing, e, 0u);
                }
                foreach (WingMember m in w.Members)
                {
                    if (n >= MaxInUse) break;
                    if (m.Released || !m.Alive || (object)m.Aircraft == null) continue;
                    byte[] own = StanceDoctrine.Axes(w.DoctrineFor(m));
                    byte[] element = StanceDoctrine.Axes(w.DoctrineOf(w.ElementOf(m)));
                    int mask = StanceDiff.Mask(own, element);
                    if (mask == 0) continue;
                    SetInUse(n++, "#" + AvNum.Fixed(m.Number, 0), StanceWords.ChangedWords(mask, own), AvState.Caution, true, -1, m.Aircraft.persistentID.Id);
                }
            }
            if (n != inUseShown) inUseShown = n;
            inUse.SetCount(n);
            string note = n == 0 ? "" : AvNum.Fixed(n, 0) + (n == 1 ? " LINE" : " LINES");
            inUseSection.SetCaption(note);
            // A rebuilt stance list shows who flies what.
            listKey = long.MinValue;
            flow.RequestRelayout();
        }

        private void SetInUse(int i, string name, string sub, AvState state, bool clear, int element, uint member)
        {
            inName[i] = name;
            inSub[i] = sub;
            inState[i] = state;
            inClear[i] = clear;
            inElement[i] = element;
            inMember[i] = member;
        }

        /// <summary>A pooled IN USE line: its words, and a CLEAR (made and registered the first time the line is bound).</summary>
        private void BindInUse(int item, AvRow row)
        {
            row.Set(inName[item] ?? "", inSub[item] ?? "", "", inState[item]);
            if (inUse.Clears[item] == null)
            {
                int k = item;
                AvControl clear = row.AddTrailing(new AvControl.Spec("CLEAR", () => Clear(k)));
                if (clear == null) return;
                clear.Help = "Back to what it inherits: an element to the wing's doctrine, an aircraft to its element's.";
                inUse.Clears[item] = clear;
                ids.Add("opt.override" + item + ".reset", clear);
            }
            if (inUse.Clears[item].gameObject.activeSelf != inClear[item]) inUse.Clears[item].gameObject.SetActive(inClear[item]);
        }

        /// <summary>An element back to the wing's doctrine; an aircraft back to its element's (a whole profile, since the executor
        /// refuses element-wide axes one aircraft at a time). ponytail: no new order kind — the values match, so the line goes.</summary>
        private void Clear(int i)
        {
            WingService w = last?.Wing;
            if (w == null || !inClear[i]) return;
            int e = inElement[i];
            if (e >= 0)
            {
                WmcUi.Order(last, () => WingOrders.Run(new WingOrder
                {
                    Kind = OrderKind.SetDoctrine, Text = w.Doctrine.ToString(), Scope = WingScope.OfElement(e),
                }));
                return;
            }
            WingMember m = last.MemberOf(inMember[i]);
            if (m == null) return;
            WmcUi.Order(last, () => WingOrders.Run(new WingOrder
            {
                Kind = OrderKind.SetDoctrine, Text = w.DoctrineOf(w.ElementOf(m)).ToString(), Scope = WingScope.OfMembers(inMember[i]),
            }));
        }

        /// <summary>The IN USE lines (a pooled <see cref="AvList"/> that never pages: it holds the most a wing can have), or the one
        /// line that says there are none; only the one that shows takes room.</summary>
        private sealed class InUseView : AvPart
        {
            private readonly AvList list;
            private readonly WmcLines none;
            private int count;

            public readonly AvControl[] Clears;

            public InUseView(RectTransform parent, AvTicker ticker, int max, Action<int, AvRow> binder)
            {
                Rect = AvLay.Child(parent, "InUse");
                Clears = new AvControl[max];
                none = new WmcLines(Rect, 1, AvTextRole.ProseSmall, "row-sub");
                none.Set(0, "The host's elements and aircraft show here.");
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
