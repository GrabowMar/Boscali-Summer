using NOAvionics;
using System;
using System.Collections.Generic;
using UnityEngine;

using BoscaliSummer.Modules.Wing.Domain;
using BoscaliSummer.Modules.Wing.Domain.Pure;
using BoscaliSummer.Modules.Wing.Runtime;
using BoscaliSummer.Modules.Wing.Networking;
using BoscaliSummer.Modules.Wing.Configuration;
namespace BoscaliSummer.Modules.Wing.Presentation
{
    /// <summary>BEHAVIOUR (remade 2026-11-05; replaces STANCES + SORTIE): one page for tuning the wing's AI to the situation, top to
    /// bottom in the order it is used. NOW FLYING says in words what the scope does right now. SCENARIOS are the presets (stances): one
    /// tap flies one for the scope; the one it flies is lit, and SAVE NEW / UPDATE keep the live tuning as a preset. TUNE changes the
    /// scope's AI at once, a setting at a time (the lit or nearest preset's own value is outlined). WING RULES are the whole wing's
    /// after-action rules, set at once. OVERRIDES lists the aircraft flying settings of their own. Plans are queued and run on ORDERS.</summary>
    internal sealed class WmcBehaviour : IWmcPage
    {
        private const int MaxOverrides = WcSnapshot.MaxMembers;
        private const int MaxNameChars = 16;
        private const string MembersOnlyWhy = "Pick the wing or an element: this setting is the same for a whole element.";

        private enum Kind { Axis, Missiles, FallBack, Winchester, Bingo, Power, Radio }

        private sealed class Row
        {
            public WmcTuneRow Seg;
            public string[] Tips;
            public Kind Kind;
            public DoctrineAxis Axis;
            public int Value = -2, Base = -2;
        }

        private readonly List<Row> rows = new List<Row>(12);
        private readonly ConfirmGate gate = new ConfirmGate();
        private AvFlow flow;
        private WmcScopeBar scope;
        private AvSection nowSection, tuneSection, overrideSection;
        private WmcLines says;
        private WmcStanceSlots plates;
        private AvControl saveButton, updateButton, deleteButton;
        private WmcTuneRow keyRow;
        private WmcNameField nameField;
        private WmcCollapsible nameGate;
        private OverrideView overrides;
        private WmcContext last;
        private int serial = 1, version = 1;
        private long plateKey = long.MinValue, overrideKey = long.MinValue;
        private string saysShown;

        /// <summary>The preset the scope flies exactly (null: none, or a mixed scope), and the nearest one with the axes it differs in.</summary>
        private Stance lit, nearest;
        private int nearMask;
        private byte[] live;

        private readonly string[] ovName = new string[MaxOverrides], ovSub = new string[MaxOverrides];
        private readonly uint[] ovMember = new uint[MaxOverrides];

        public string Hint => "Tap a scenario to fly it. TUNE changes fly at once; SAVE NEW keeps them as a scenario.";

        public string Alert => null;

        private static StanceBook Book => WmcStanceActions.Book;

        public void Build(AvFlow pageFlow, AvTicker ticker, int pageIndex)
        {
            flow = pageFlow;
            scope = flow.Add(new WmcScopeBar(flow.Content, "APPLY TO"));

            // ---- what the scope does right now, in words
            nowSection = flow.Section(AvIcon.Activity, "NOW FLYING", "");
            says = flow.Add(new WmcLines(flow.Content, 1));

            // ---- the presets
            flow.Section(AvIcon.ListDetails, "SCENARIOS", "TAP TO FLY · WING KEY + 1-6");
            plates = flow.Add(new WmcStanceSlots(flow.Content, PickPreset, null, StanceBook.MaxStances, true));
            AvControl[] bar = flow.Buttons(
                new AvControl.Spec("SAVE NEW", SaveNew, AvButtonStyle.Default, AvIcon.Bookmark),
                new AvControl.Spec("UPDATE", Update, AvButtonStyle.Default, AvIcon.Refresh),
                new AvControl.Spec("DELETE", Delete, AvButtonStyle.Danger, AvIcon.X)).Controls;
            saveButton = bar[0];
            updateButton = bar[1];
            deleteButton = bar[2];
            nameField = new WmcNameField(flow.Content, "NAME", MaxNameChars, CommitName, "The scenario's name: Enter keeps it (16 characters at most).");
            nameGate = flow.Add(new WmcCollapsible(nameField));
            var keys = new[] { "W+1", "W+2", "W+3", "W+4", "W+5", "W+6" };
            keyRow = flow.Add(new WmcTuneRow(flow.Content, "WING KEY", keys, PickKey));
            for (int i = 0; i < keys.Length; i++) keyRow.Options[i].Help = "Wing key + " + (i + 1) + " flies the lit scenario (ORDERS' slot " + (i + 1) + ").";

            // ---- live tuning of the scope's AI
            tuneSection = flow.Section(AvIcon.AdjustmentsHorizontal, "TUNE", "");
            Add("SHOOT AT", new[] { "HOLD", "AIR", "GROUND", "BOTH", "COVER" },
                new[]
                {
                    "Hold fire: shoot only when ordered.", "Engage enemy aircraft in range on their own.",
                    "Engage ground targets in range on their own.", "Engage air and ground targets in range on their own.",
                    "Cover: take the one air threat nearest the protected aircraft.",
                }, Kind.Axis, DoctrineAxis.Targets);
            Add("RANGE", new[] { "6 KM", "12 KM" },
                new[] { "Short: engage what comes within 6 km.", "Long: reach out to 12 km." }, Kind.Axis, DoctrineAxis.Reach);
            Add("WEAPONS", new[] { "AUTO", "MISSILE", "GUN", "NO A-G" },
                new[] { "Every weapon aboard.", "Missiles only: no guns, no bombs.", "Guns only.", "Every weapon, at air targets only (keep the bombs)." },
                Kind.Axis, DoctrineAxis.Weapons);
            Add("RADAR", new[] { "ON", "SILENT", "OFF" },
                new[]
                {
                    "Radar on: the wing sees and shares its picture.",
                    "Silent: radar off until engaged (enemy warners stay quiet).",
                    "Off: no radar, no radar-guided shots; they find little on their own.",
                }, Kind.Axis, DoctrineAxis.Radar);
            Add("MISSILES", new[] { "HOLD SLOT", "BREAK", "PRESS" },
                new[]
                {
                    "Incoming missile: stay in formation, flares and chaff only.",
                    "Incoming missile: notch, beam or flare out of it.",
                    "Incoming radar missile while ours is in the air: hold heading (chaff) until it is 3 s out, then break.",
                }, Kind.Missiles, DoctrineAxis.Guard);
            Add("SPACING", new[] { "CLOSE", "STD", "OPEN" },
                new[] { "Close: three quarters of the formation spacing.", "Standard formation spacing.", "Open: one and a half times the spacing." },
                Kind.Axis, DoctrineAxis.Interval);

            // ---- the whole wing's rules
            flow.Section(AvIcon.Radio, "WING RULES", "WHOLE WING · AT ONCE");
            Add("FALL BACK", new[] { "NEVER", "1.5 : 1", "2 : 1", "3 : 1" },
                new[]
                {
                    "Never fall back, however outnumbered.", "Fall back into formation facing 1.5 enemy aircraft per fighting wingman.",
                    "Fall back facing 2 enemy aircraft per fighting wingman.", "Fall back facing 3 enemy aircraft per fighting wingman.",
                }, Kind.FallBack, DoctrineAxis.Guard);
            Add("WINCHESTER", new[] { "REJOIN", "RTB", "REFIT" },
                new[] { "Out of ammunition: rejoin the formation.", "Out of ammunition: land and return to the reserve.", "Out of ammunition: land, rearm and take off again." },
                Kind.Winchester, DoctrineAxis.Guard);
            Add("BINGO", new[] { "RTB", "REFIT" },
                new[] { "At bingo fuel: land and return to the reserve.", "At bingo fuel: land, refuel and take off again." },
                Kind.Bingo, DoctrineAxis.Guard);
            Add("POWER", new[] { "BUSTER", "GATE" },
                new[] { "Full power, no afterburner.", "Afterburner allowed." }, Kind.Power, DoctrineAxis.Guard);
            Add("CALLS", new[] { "OFF", "ESSENTIAL", "FULL" },
                new[] { "No wingman radio calls.", "Emergencies, tactical and status calls only.", "Everything, chatter included." },
                Kind.Radio, DoctrineAxis.Guard);

            // ---- aircraft flying settings of their own
            overrideSection = flow.Section(AvIcon.Target, "OVERRIDES", "");
            overrides = flow.Add(new OverrideView(flow.Content, ticker, MaxOverrides, BindOverride));
            overrides.Prime(MaxOverrides);
        }

        private void Add(string key, string[] labels, string[] tips, Kind kind, DoctrineAxis axis)
        {
            var row = new Row { Tips = tips, Kind = kind, Axis = axis };
            row.Seg = flow.Add(new WmcTuneRow(flow.Content, key, labels, i => Pick(row, i)));
            for (int i = 0; i < labels.Length; i++) row.Seg.Options[i].Help = tips[i];
            rows.Add(row);
        }

        public void Shown(WmcContext c)
        {
            plateKey = overrideKey = long.MinValue;
            saysShown = null;
        }

        // ---------------------------------------------------------------- refresh

        public void Refresh(WmcContext c)
        {
            last = c;
            scope.Refresh(c);
            bool host = c.CanOrder, changed = false;
            WingDoctrine d = default;
            live = host && c.ScopeDoctrine(out d) ? StanceDoctrine.Axes(d) : null;
            lit = live != null ? WmcStanceActions.Matching(live) : null;
            nearMask = 0;
            nearest = lit ?? (live != null ? StanceWords.Closest(Book.All, live, out nearMask) : null);

            WingConfig cfg = WingSettings.Instance;
            string now = !host ? (c.Client ? "The host tunes the wing." : "Wing Command is not ready.")
                : live == null ? "The aircraft in scope fly different settings: tap a scenario or a TUNE setting to line them up."
                : StanceWords.Says(live, FallBackIndex(cfg), cfg != null ? (int)cfg.AfterWinchester.Value : -1, cfg != null ? (int)cfg.AfterBingo.Value : -1);
            if (now != saysShown)
            {
                saysShown = now;
                says.Set(0, now);
                changed = true;
            }
            int diff = StanceDiff.Count(nearMask);
            nowSection.SetCaption(!host ? "" : live == null ? c.ScopeLabel + " · MIXED"
                : lit != null ? c.ScopeLabel + " · " + lit.Name
                : nearest != null ? c.ScopeLabel + " · " + nearest.Name + " +" + diff + (diff == 1 ? " CHANGE" : " CHANGES")
                : c.ScopeLabel + " · CUSTOM");
            tuneSection.SetCaption(host ? c.ScopeLabel + " · CHANGES FLY AT ONCE" : "");

            RefreshPresets(c, ref changed);
            RefreshRows(c, cfg);
            RefreshOverrides(c);
            if (changed) flow.RequestRelayout();
        }

        private void RefreshPresets(WmcContext c, ref bool changed)
        {
            bool host = c.CanOrder, own = lit != null && !lit.BuiltIn;
            long key = version * 131L + Book.All.Count * 7L + (lit?.Id.GetHashCode() ?? 0) + (own && gate.IsArmed("del" + lit.Id, Time.unscaledTime) ? 1 : 0);
            if (key != plateKey)
            {
                plateKey = key;
                int n = Book.All.Count;
                plates.SetShown(n);
                for (int i = 0; i < n; i++)
                {
                    Stance s = Book.All[i];
                    int slot = SlotOf(s);
                    plates.SetHint(i, slot >= 0 ? "W+" + (slot + 1) : "");
                    plates.Set(i, s.Name, StanceSlotWords.Summary(s.Axes), true, s == lit);
                    plates.SetHelp(i, StanceWords.Says(s.Axes, s.FallBack, s.Winchester, s.Bingo) + (s.BuiltIn ? " (BUILT-IN)" : " (YOURS)") + " Tap to fly it.");
                }
                changed = true;
            }

            Enable(saveButton, host && live != null && Book.All.Count < StanceBook.MaxStances,
                !host ? WmcPostureActions.Cannot(c) : live == null ? "Line the scope up on one setting first." : "The scenario list is full.",
                "Keep what the scope flies now (and the wing rules) as a new scenario.");
            bool canUpdate = host && live != null && nearest != null && !nearest.BuiltIn && nearMask != 0;
            updateButton.Label = canUpdate ? "UPDATE " + nearest.Name : "UPDATE";
            Enable(updateButton, canUpdate, "Tune one of your scenarios, then UPDATE writes the changes into it (built-ins stay as they are).",
                "Write what the scope flies now into " + (nearest?.Name ?? "") + ".");
            deleteButton.Label = own && gate.IsArmed("del" + lit.Id, Time.unscaledTime) ? "DELETE?" : "DELETE";
            Enable(deleteButton, own, "Fly one of your scenarios to delete it (built-ins stay).", "Remove the lit scenario (asks first).");

            changed |= nameGate.Set(own);
            if (own)
            {
                nameField.EditingId = lit.Id;
                nameField.SetText(lit.Name);
                nameField.SetInteractable(true);
            }
            keyRow.Show(SlotOf(lit), -1, false);
            foreach (AvControl o in keyRow.Options)
                if (o.Interactable != (lit != null)) o.Interactable = lit != null;
        }

        private void RefreshRows(WmcContext c, WingConfig cfg)
        {
            bool host = c.CanOrder, members = c.Scope.Kind == ScopeKind.Members;
            foreach (Row r in rows)
            {
                int v = Value(r, c, cfg), b = BaseValue(r);
                if (r.Value != v || r.Base != b)
                {
                    r.Value = v;
                    r.Base = b;
                    r.Seg.Show(v, b, b >= 0 && v >= 0 && b != v);
                }
                bool perElement = r.Kind == Kind.Missiles || (r.Kind == Kind.Axis && !MemberDoctrines.PerAircraft(r.Axis));
                bool on = r.Kind == Kind.Radio || (host && !(members && perElement));
                string why = !host ? WmcPostureActions.Cannot(c) : MembersOnlyWhy;
                AvControl[] options = r.Seg.Options;
                for (int i = 0; i < options.Length; i++)
                {
                    if (options[i].Interactable != on) options[i].Interactable = on;
                    string tip = on ? r.Tips[i] : why;
                    if (options[i].Help != tip) options[i].Help = tip;
                }
            }
        }

        /// <summary>The choice the row latches now: the scope's setting (-1 mixed or unknown) or the wing's rule.</summary>
        private static int Value(Row r, WmcContext c, WingConfig cfg)
        {
            switch (r.Kind)
            {
                case Kind.Radio: return cfg != null ? (int)cfg.Radio.Value : -1;
                case Kind.Power: return c.Wing != null && !c.Client ? (c.Wing.AfterburnerAllowed ? 1 : 0) : -1;
                case Kind.FallBack: return c.Client ? -1 : FallBackIndex(cfg);
                case Kind.Winchester: return cfg != null && !c.Client ? (int)cfg.AfterWinchester.Value : -1;
                case Kind.Bingo: return cfg != null && !c.Client ? (int)cfg.AfterBingo.Value : -1;
                case Kind.Missiles:
                {
                    if (!c.CanOrder) return -1;
                    int g = c.ScopeValue(DoctrineAxis.Guard), p = c.ScopeValue(DoctrineAxis.Response);
                    return g < 0 ? -1 : g == 0 ? 0 : p < 0 ? -1 : 1 + p;
                }
                default: return c.CanOrder ? c.ScopeValue(r.Axis) : -1;
            }
        }

        /// <summary>The lit or nearest preset's own value for the row (outlined when the scope has moved off it); -1 for the wing rules.</summary>
        private int BaseValue(Row r)
        {
            if (nearest == null) return -1;
            switch (r.Kind)
            {
                case Kind.Axis: return nearest.Axes[(int)r.Axis];
                case Kind.Missiles: return nearest.Axes[(int)DoctrineAxis.Guard] == 0 ? 0 : 1 + nearest.Axes[(int)DoctrineAxis.Response];
                default: return -1;
            }
        }

        /// <summary>The fall-back ratio as its row's choice (the nearest of 0 · 1.5 · 2 · 3).</summary>
        private static int FallBackIndex(WingConfig cfg)
        {
            if (cfg == null) return -1;
            float v = cfg.FallBackRatio.Value;
            float[] ratios = WmcPostureActions.FallBackRatios;
            int best = 0;
            for (int i = 1; i < ratios.Length; i++)
                if (Mathf.Abs(ratios[i] - v) < Mathf.Abs(ratios[best] - v)) best = i;
            return best;
        }

        private static void Enable(AvControl b, bool on, string why, string tip)
        {
            if (b.Interactable != on) b.Interactable = on;
            string help = on ? tip : why;
            if (help != null && b.Help != help) b.Help = help;
        }

        // ---------------------------------------------------------------- picks

        private void Pick(Row r, int i)
        {
            WmcContext c = last;
            if (c == null) return;
            WingConfig cfg = WingSettings.Instance;
            switch (r.Kind)
            {
                case Kind.Radio:
                    if (cfg != null) cfg.Radio.Value = (RadioLevel)i;
                    break;
                case Kind.Power:
                    WmcPostureActions.Power(c, i);
                    break;
                case Kind.FallBack:
                    if (cfg != null && c.CanOrder) cfg.FallBackRatio.Value = WmcPostureActions.FallBackRatios[i];
                    break;
                case Kind.Winchester:
                    if (cfg != null && c.CanOrder) cfg.AfterWinchester.Value = (WinchesterAction)i;
                    break;
                case Kind.Bingo:
                    if (cfg != null && c.CanOrder) cfg.AfterBingo.Value = (BingoAction)i;
                    break;
                case Kind.Missiles:
                    // Guard first: the doctrine flies BREAK while the guard is off, so the response is set after it.
                    WmcPostureActions.Axis(c, DoctrineAxis.Guard, i == 0 ? 0 : 1);
                    if (i > 0) WmcPostureActions.Axis(c, DoctrineAxis.Response, i - 1);
                    break;
                default:
                    WmcPostureActions.Axis(c, r.Axis, i);
                    break;
            }
            saysShown = null;
            Refresh(c);
        }

        private void PickPreset(int i)
        {
            if (last == null || i < 0 || i >= Book.All.Count) return;
            WmcMotion.Punch(plates.Rect);
            WmcStanceActions.Apply(last, Book.All[i]);
            gate.Press("", Time.unscaledTime);
            plateKey = long.MinValue;
            saysShown = null;
            Refresh(last);
        }

        private void PickKey(int i)
        {
            if (lit == null || !Book.Assign(i, lit.Id)) return;
            WingToast.Show(lit.Name + " is on wing key + " + (i + 1));
            Saved();
        }

        /// <summary>The live tuning as a stance: the scope's 8 axes and the wing's three rules.</summary>
        private Stance FromLive()
        {
            WingConfig cfg = WingSettings.Instance;
            return new Stance
            {
                Axes = (byte[])live.Clone(),
                FallBack = (sbyte)FallBackIndex(cfg),
                Winchester = cfg != null ? (sbyte)cfg.AfterWinchester.Value : (sbyte)-1,
                Bingo = cfg != null ? (sbyte)cfg.AfterBingo.Value : (sbyte)-1,
            };
        }

        private void SaveNew()
        {
            if (live == null) return;
            Stance s = FromLive();
            do s.Id = "user" + serial++;
            while (Book.Find(s.Id) != null);
            s.Name = NewName(nearest != null ? nearest.Name : "CUSTOM");
            if (!Book.Add(s))
            {
                WingToast.Show("The scenario list is full (" + StanceBook.MaxStances + ")");
                return;
            }
            WingToast.Show("Saved " + s.Name);
            Saved();
        }

        private void Update()
        {
            if (live == null || nearest == null || nearest.BuiltIn) return;
            Stance s = FromLive();
            nearest.Axes = s.Axes;
            nearest.FallBack = s.FallBack;
            nearest.Winchester = s.Winchester;
            nearest.Bingo = s.Bingo;
            WingToast.Show("Updated " + nearest.Name);
            Saved();
        }

        private void Delete()
        {
            Stance s = lit;
            if (s == null || s.BuiltIn) return;
            if (!gate.Press("del" + s.Id, Time.unscaledTime))
            {
                WingToast.Show("DELETE again to remove " + s.Name);
                plateKey = long.MinValue;
                if (last != null) Refresh(last);
                return;
            }
            Book.Remove(s.Id);
            WingToast.Show("Deleted " + s.Name);
            Saved();
        }

        private void CommitName(string id, string typed)
        {
            Stance s = Book.Find(id);
            string name = (typed ?? "").Trim().ToUpperInvariant();
            if (s == null || s.BuiltIn || name.Length == 0) return;
            s.Name = name.Length > MaxNameChars ? name.Substring(0, MaxNameChars) : name;
            Saved();
        }

        private void Saved()
        {
            if (!WmcStanceFiles.Save()) WingToast.Show("Could not save your scenarios (see the log)");
            version++;
            plateKey = long.MinValue;
            saysShown = null;
            if (last != null) Refresh(last);
        }

        private static string NewName(string basis)
        {
            string stem = basis.Length > MaxNameChars - 3 ? basis.Substring(0, MaxNameChars - 3) : basis;
            for (int n = 2; n < 100; n++)
            {
                string name = stem + " " + n;
                bool taken = false;
                foreach (Stance s in Book.All) taken |= s.Name == name;
                if (!taken) return name;
            }
            return stem;
        }

        private static int SlotOf(Stance s)
        {
            if (s == null) return -1;
            for (int i = 0; i < StanceBook.Slots; i++)
                if (Book.Slot(i) == s) return i;
            return -1;
        }

        // ---------------------------------------------------------------- OVERRIDES

        /// <summary>Every aircraft that keeps doctrine of its own (it outlives its element's next scenario), each with a CLEAR.</summary>
        private void RefreshOverrides(WmcContext c)
        {
            WingService w = c.CanOrder ? c.Wing : null;
            long key = c.Client ? 19L : 17L;
            if (w != null)
                foreach (WingMember m in w.Members)
                {
                    if (m.Released || !m.Alive || (object)m.Aircraft == null) continue;
                    key = Hash(Hash(key * 31L + m.Number, w.DoctrineFor(m)), w.DoctrineOf(w.ElementOf(m))) * 2L + (w.KeepsOwn(m) ? 1L : 0L);
                }
            if (key == overrideKey) return;
            overrideKey = key;
            int n = 0;
            if (w != null)
                foreach (WingMember m in w.Members)
                {
                    if (n >= MaxOverrides) break;
                    if (m.Released || !m.Alive || (object)m.Aircraft == null) continue;
                    byte[] own = StanceDoctrine.Axes(w.DoctrineFor(m));
                    int mask = StanceDiff.Mask(own, StanceDoctrine.Axes(w.DoctrineOf(w.ElementOf(m))));
                    if (mask == 0 && !w.KeepsOwn(m)) continue;
                    ovName[n] = "#" + AvNum.Fixed(m.Number, 0);
                    ovSub[n] = mask == 0 ? "SAME AS ITS ELEMENT · KEEPS ITS OWN" : StanceWords.ChangedWords(mask, own);
                    ovMember[n++] = m.Aircraft.persistentID.Id;
                }
            overrides.SetCount(n, c.Client ? "The host sets each aircraft's own settings." : "Every aircraft flies its element's settings.");
            overrideSection.SetCaption(n == 0 ? "" : AvNum.Fixed(n, 0) + " AIRCRAFT");
            flow.RequestRelayout();
        }

        private static long Hash(long key, in WingDoctrine d)
        {
            for (int a = 0; a < Stance.AxisCount; a++) key = key * 31L + d.Get((DoctrineAxis)a);
            return key;
        }

        private void BindOverride(int item, AvRow row)
        {
            row.Set(ovName[item] ?? "", ovSub[item] ?? "", "", AvState.Caution);
            if (overrides.Clears[item] != null) return;
            int k = item;
            AvControl clear = row.AddTrailing(new AvControl.Spec("CLEAR", () => Clear(k)));
            if (clear == null) return;
            clear.Help = "Back to what its element flies.";
            overrides.Clears[item] = clear;
        }

        private void Clear(int i)
        {
            uint member = ovMember[i];
            if (last?.Wing == null || member == 0u) return;
            WmcUi.Order(last, () => WingOrders.Run(new WingOrder { Kind = OrderKind.ClearDoctrine, Scope = WingScope.OfMembers(member) }));
        }

        /// <summary>The OVERRIDES lines (a pooled <see cref="AvList"/> that holds the most a wing can have), or the one line that says
        /// there are none; only the one that shows takes room.</summary>
        private sealed class OverrideView : AvPart
        {
            private readonly AvList list;
            private readonly WmcLines none;
            private int count;

            public readonly AvControl[] Clears;

            public OverrideView(RectTransform parent, AvTicker ticker, int max, Action<int, AvRow> binder)
            {
                Rect = AvLay.Child(parent, "Overrides");
                Clears = new AvControl[max];
                none = new WmcLines(Rect, 1, AvTextRole.ProseSmall, "row-sub");
                ticker?.Register(none);
                list = new AvList(Rect, ticker, max, binder);
                SetCount(0);
            }

            public void Prime(int max)
            {
                list.SetCount(max);
                SetCount(0);
            }

            public void SetCount(int n, string noneText = null)
            {
                count = n;
                if (noneText != null) none.Set(0, noneText);
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
