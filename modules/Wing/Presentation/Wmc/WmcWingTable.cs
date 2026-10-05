using NOAvionics;
using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

using BoscaliSummer.Modules.Wing.Domain;
using BoscaliSummer.Modules.Wing.Runtime;
using BoscaliSummer.Modules.Wing.Networking;
using BoscaliSummer.Core.Math;
namespace BoscaliSummer.Modules.Wing.Presentation
{
    /// <summary>The dense wing table (spec 2026-10-04 §4.2; mockup board/orders.html): a header row, one row per element (letter, name, its
    /// task) and one per member (number, callsign, type, task, fuel tape, ammo, damage). It is also TACTICAL's scope: ALL, an element
    /// row or a member row sets who orders go to (shift adds a member), and what the scope holds is outlined. Full on ORDERS; on
    /// FORMATION and ROUTE it collapses to one summary line that a tap expands. Kit gap: no table part, so the rows are drawn from kit
    /// primitives (<see cref="AvFrame"/>, <see cref="AvText"/>, <see cref="AvHit"/>) the way the FLIGHT list was.</summary>
    internal sealed class WmcWingTable : AvPart
    {
        public const int MaxMembers = WcSnapshot.MaxMembers, MaxElements = ElementRoster.MaxElements;
        private const float RowH = 19f, HeadH = 15f, SumH = 22f, Pad = 6f, Gap = 4f, RowGap = 1f;
        private const float NumW = 28f, NameW = 82f, TypeW = 44f, FuelW = 52f, AmmoW = 34f, DmgW = 36f;
        private const int KindHead = 0, KindRow = 1;

        private sealed class Cell
        {
            public AvFrame Frame;
            public Image Rail, FuelTrack, FuelFill;
            public TMP_Text Num, Name, Type, Task, Ammo, Dmg;
            public uint Id;
            public int Key = int.MinValue;
            public bool Selected, Styled, Hover;
            public bool IsHead;
            public string FuelRail = "ready";
            public float Fuel;
            public int Element;
        }

        private readonly Cell[] heads = new Cell[MaxElements];
        private readonly Cell[] rows = new Cell[MaxMembers];
        private readonly AvFrame sumFrame, headBack;
        private readonly TMP_Text sumWing, sumElements, sumAlert, sumAction;
        private readonly TMP_Text hAll, hName, hType, hTask, hFuel, hAmmo, hDmg;
        private readonly AvFrame allHit;
        private readonly AvRow seats;
        private readonly int[] order = new int[MaxMembers];
        private readonly int[] kind = new int[MaxMembers + MaxElements], which = new int[MaxMembers + MaxElements];
        private readonly int[] perElement = new int[MaxElements];
        private int count, shown = -1;
        private bool collapsed, collapsible, seatsOn, allLatched, summaryHover;
        private float measuredWidth = 464f;
        private string alertWord = "ALL CLEAR";
        private AvState alertState = AvState.Ready;
        private WmcContext last;

        /// <summary>Set by the owner: ALL, an element row, a member row (index into the rows; shift adds), the summary line, SUPPLY.</summary>
        public Action AllPicked, SummaryPicked, SupplyPicked;
        public Action<int> ElementPicked;
        public Action<int, bool> RowPicked;
        /// <summary>Offline renders and tests: callsign and type of an aircraft id (null: the host's own lookup).</summary>
        public Func<uint, string[]> PreviewIdentity;
        /// <summary>Offline renders: an element row's task words (null: the host's planner).</summary>
        public Func<int, string> PreviewTask;
        /// <summary>Until this unscaled time the scope's rows flash (an order went to them).</summary>
        public float FlashUntil;

        public WmcWingTable(RectTransform parent)
        {
            Rect = AvLay.Child(parent, "WingTable");
            sumFrame = AvFrame.Add(Rect, "Summary", default(AvChamfer));
            AvHit sh = AvHit.On(sumFrame);
            sh.Hover = h => { summaryHover = h; Restyle(); };
            sh.Click = e => SummaryPicked?.Invoke();
            AvHelpTip.Attach(sumFrame.gameObject, "Show or hide the flight list. Orders go to the scope it outlines.");
            sumWing = Label("SumWing", AvTextRole.Label);
            sumElements = Label("SumElements", AvTextRole.Label);
            sumAlert = Label("SumAlert", AvTextRole.Label, TextAlignmentOptions.MidlineRight);
            sumAction = Label("SumAction", AvTextRole.Micro, TextAlignmentOptions.MidlineRight);
            headBack = AvFrame.Add(Rect, "HeadBack", default(AvChamfer));
            headBack.raycastTarget = false;
            allHit = AvFrame.Add(Rect, "AllHit", default(AvChamfer));
            AvHit ah = AvHit.On(allHit);
            ah.Click = e => AllPicked?.Invoke();
            AvHelpTip.Attach(allHit.gameObject, "Orders go to the whole wing.");
            hAll = Label("HAll", AvTextRole.Micro);
            hName = Label("HName", AvTextRole.Micro);
            hType = Label("HType", AvTextRole.Micro);
            hTask = Label("HTask", AvTextRole.Micro);
            hFuel = Label("HFuel", AvTextRole.Micro);
            hAmmo = Label("HAmmo", AvTextRole.Micro);
            hDmg = Label("HDmg", AvTextRole.Micro);
            hAll.text = "ALL";
            hName.text = "CALLSIGN";
            hType.text = "TYPE";
            hTask.text = "TASK";
            hFuel.text = "FUEL";
            hAmmo.text = "AMMO";
            hDmg.text = "DMG";
            for (int e = 0; e < heads.Length; e++) heads[e] = MakeCell("Head" + e, true, e);
            for (int i = 0; i < rows.Length; i++) rows[i] = MakeCell("Row" + i, false, i);
            seats = new AvRow(Rect);
            seats.Rect.gameObject.SetActive(false);
            AvControl toSupply = seats.AddTrailing(new AvControl.Spec("SUPPLY", () => SupplyPicked?.Invoke()));
            toSupply.Help = "Requisition a wingman: pilot, airframe, fit and base.";
            SupplyButton = toSupply;
            Restyle();
        }

        public AvControl SupplyButton { get; }
        public AvRow Seats => seats;
        public Transform AllTarget => allHit.transform;
        public Transform SummaryTarget => sumFrame.transform;
        public Transform HeadTarget(int e) => heads[e].Frame.transform;
        public Transform RowTarget(int r) => rows[r].Frame.transform;
        public uint IdAt(int r) => r >= 0 && r < rows.Length ? rows[r].Id : 0u;
        public bool Collapsed => collapsed;
        public int RowsShown => shownRows;
        public int HeadsShown => shownHeads;
        private int shownRows, shownHeads;

        /// <summary>Whether the full table is hidden behind its summary line; a collapsible table always draws the summary line.</summary>
        public void SetMode(bool collapsibleTable, bool isCollapsed)
        {
            bool c = collapsibleTable && isCollapsed;
            if (collapsible == collapsibleTable && collapsed == c) return;
            collapsible = collapsibleTable;
            collapsed = c;
            Apply();
        }

        private TMP_Text Label(string name, AvTextRole role, TextAlignmentOptions align = TextAlignmentOptions.MidlineLeft)
        {
            TMP_Text t = AvText.Make(Rect, name, role, "", align);
            AvText.Fit(t, false);
            return t;
        }

        private Cell MakeCell(string name, bool head, int index)
        {
            var c = new Cell { IsHead = head, Element = index };
            c.Frame = AvFrame.Add(Rect, name, default(AvChamfer));
            AvHit hit = AvHit.On(c.Frame);
            hit.Hover = h => { c.Hover = h; Style(c); };
            if (head) hit.Click = e => ElementPicked?.Invoke(index);
            else hit.Click = e => RowPicked?.Invoke(index, Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift));
            AvHelpTip.Attach(c.Frame.gameObject, head ? "Orders go to this whole element." : "Click: orders go to this wingman. Shift-click: add or remove it.");
            c.Rail = AvLay.Solid(Rect, name + "Rail", Color.clear);
            c.Num = Label(name + "Num", head ? AvTextRole.Label : AvTextRole.DataSmall, TextAlignmentOptions.MidlineLeft);
            c.Name = Label(name + "Name", head ? AvTextRole.Micro : AvTextRole.Label);
            c.Task = Label(name + "Task", head ? AvTextRole.Micro : AvTextRole.Micro);
            if (!head)
            {
                c.Type = Label(name + "Type", AvTextRole.Micro);
                c.FuelTrack = AvLay.Solid(Rect, name + "FuelTrack", Color.clear);
                c.FuelFill = AvLay.Solid(Rect, name + "FuelFill", Color.clear);
                c.Ammo = Label(name + "Ammo", AvTextRole.DataSmall, TextAlignmentOptions.MidlineRight);
                c.Dmg = Label(name + "Dmg", AvTextRole.Micro);
            }
            SetCellShown(c, false);
            return c;
        }

        private static void SetCellShown(Cell c, bool on)
        {
            if (c.Frame.gameObject.activeSelf == on) return;
            c.Frame.gameObject.SetActive(on);
            c.Rail.gameObject.SetActive(on);
            c.Num.gameObject.SetActive(on);
            c.Name.gameObject.SetActive(on);
            c.Task.gameObject.SetActive(on);
            if (c.IsHead) return;
            c.Type.gameObject.SetActive(on);
            c.FuelTrack.gameObject.SetActive(on);
            c.FuelFill.gameObject.SetActive(on);
            c.Ammo.gameObject.SetActive(on);
            c.Dmg.gameObject.SetActive(on);
        }

        private void Apply()
        {
            sumFrame.gameObject.SetActive(collapsible);
            sumWing.gameObject.SetActive(collapsible);
            sumElements.gameObject.SetActive(collapsible);
            sumAlert.gameObject.SetActive(collapsible);
            sumAction.gameObject.SetActive(collapsible);
            bool full = !collapsed;
            headBack.gameObject.SetActive(full);
            allHit.gameObject.SetActive(full);
            foreach (TMP_Text t in new[] { hAll, hName, hType, hTask, hFuel, hAmmo, hDmg }) t.gameObject.SetActive(full);
            if (!full)
            {
                foreach (Cell c in heads) SetCellShown(c, false);
                foreach (Cell c in rows) SetCellShown(c, false);
                if (seats.Rect.gameObject.activeSelf) seats.Rect.gameObject.SetActive(false);
            }
            if (last != null) Refresh(last, alertWord, alertState);
            Changed();
        }

        private float ContentHeight()
        {
            float h = collapsible ? SumH + RowGap : 0f;
            if (collapsed) return Mathf.Max(0f, h - RowGap);
            h += HeadH + RowGap;
            for (int i = 0; i < count; i++) h += RowH + RowGap;
            if (seatsOn) h += seats.Measure(measuredWidth) + RowGap;
            return Mathf.Max(0f, h - RowGap);
        }

        public override float Measure(float width)
        {
            measuredWidth = width;
            return ContentHeight();
        }

        public override void Place(AvSlot s)
        {
            base.Place(s);
            measuredWidth = s.W;
            float w = s.W, y = 0f;
            if (collapsible)
            {
                AvLay.Place(sumFrame.rectTransform, 0f, y, w, SumH);
                AvLay.Place(sumWing.rectTransform, Pad + 4f, y, 112f, SumH);
                AvLay.Place(sumElements.rectTransform, Pad + 120f, y, 100f, SumH);
                AvLay.Place(sumAction.rectTransform, w - Pad - 96f, y, 96f, SumH);
                AvLay.Place(sumAlert.rectTransform, Pad + 224f, y, w - Pad - 96f - Pad - 224f - Gap, SumH);
                y += SumH + RowGap;
            }
            if (collapsed) return;
            AvLay.Place(headBack.rectTransform, 0f, y, w, HeadH);
            float x = Pad + 4f;
            AvLay.Place(allHit.rectTransform, 0f, y, NumW + Pad + 4f, HeadH);
            AvLay.Place(hAll.rectTransform, x, y, NumW, HeadH);
            x += NumW + Gap;
            AvLay.Place(hName.rectTransform, x, y, NameW, HeadH);
            x += NameW + Gap;
            AvLay.Place(hType.rectTransform, x, y, TypeW, HeadH);
            x += TypeW + Gap;
            float taskW = w - x - Pad - FuelW - AmmoW - DmgW - 3f * Gap;
            AvLay.Place(hTask.rectTransform, x, y, taskW, HeadH);
            x += taskW + Gap;
            AvLay.Place(hFuel.rectTransform, x, y, FuelW, HeadH);
            x += FuelW + Gap;
            AvLay.Place(hAmmo.rectTransform, x, y, AmmoW, HeadH);
            x += AmmoW + Gap;
            AvLay.Place(hDmg.rectTransform, x, y, DmgW, HeadH);
            y += HeadH + RowGap;
            for (int i = 0; i < count; i++)
            {
                if (kind[i] == KindHead) PlaceHead(heads[which[i]], y, w);
                else PlaceRow(rows[which[i]], y, w);
                y += RowH + RowGap;
            }
            if (seatsOn)
            {
                float h = seats.Measure(w);
                seats.Place(new AvSlot(0f, y, w, h));
            }
        }

        private static void PlaceFrame(Cell c, float y, float w)
        {
            AvLay.Place(c.Frame.rectTransform, 0f, y, w, RowH);
            AvLay.Place(c.Rail.rectTransform, 0f, y, 2f, RowH);
        }

        private void PlaceHead(Cell c, float y, float w)
        {
            PlaceFrame(c, y, w);
            float x = Pad + 4f;
            AvLay.Place(c.Num.rectTransform, x, y, NumW, RowH);
            x += NumW + Gap;
            AvLay.Place(c.Name.rectTransform, x, y, NameW + Gap + TypeW, RowH);
            x += NameW + Gap + TypeW + Gap;
            AvLay.Place(c.Task.rectTransform, x, y, w - x - Pad, RowH);
        }

        private void PlaceRow(Cell c, float y, float w)
        {
            PlaceFrame(c, y, w);
            float x = Pad + 4f;
            AvLay.Place(c.Num.rectTransform, x, y, NumW, RowH);
            x += NumW + Gap;
            AvLay.Place(c.Name.rectTransform, x, y, NameW, RowH);
            x += NameW + Gap;
            AvLay.Place(c.Type.rectTransform, x, y, TypeW, RowH);
            x += TypeW + Gap;
            float taskW = w - x - Pad - FuelW - AmmoW - DmgW - 3f * Gap;
            AvLay.Place(c.Task.rectTransform, x, y, taskW, RowH);
            x += taskW + Gap;
            AvLay.Place(c.FuelTrack.rectTransform, x, y + (RowH - 5f) * 0.5f, FuelW, 5f);
            c.Fuel = Mathf.Clamp01(c.Fuel);
            AvLay.Place(c.FuelFill.rectTransform, x, y + (RowH - 5f) * 0.5f, FuelW * c.Fuel, 5f);
            x += FuelW + Gap;
            AvLay.Place(c.Ammo.rectTransform, x, y, AmmoW, RowH);
            x += AmmoW + Gap;
            AvLay.Place(c.Dmg.rectTransform, x, y, DmgW, RowH);
        }

        public override void Restyle()
        {
            AvStyle row = AvStyleHost.FuiStyle("row");
            Color inert = AvStyleHost.Resolve(row.Background, AvTheme.SurfaceInert);
            sumFrame.Paint(inert, row.Border.HasValue ? AvStyleHost.Resolve(row.Border, Color.clear) : Color.clear);
            headBack.Paint(Color.clear, Color.clear);
            allHit.Paint(Color.clear, Color.clear);
            Color key = AvStyleHost.FuiColor("info", AvTheme.RailInfo);
            foreach (TMP_Text t in new[] { hAll, hName, hType, hTask, hFuel, hAmmo, hDmg }) t.color = key;
            Color name = AvStyleHost.Resolve(AvStyleHost.FuiStyle("row-name").Color, AvTheme.TextPrimary);
            sumWing.color = sumElements.color = name;
            sumAction.color = AvStyleHost.Resolve(AvStyleHost.FuiStyle("row-sub").Color, AvTheme.Dim);
            sumAlert.color = WmcState.Color(Rail(alertState));
            foreach (Cell c in heads) Style(c);
            foreach (Cell c in rows) Style(c);
            seats.Restyle();
        }

        private static string Rail(AvState s) =>
            s == AvState.Danger ? "danger" : s == AvState.Caution ? "caution" : s == AvState.Ready ? "ready" : s == AvState.Info ? "info" : "inert";

        private static Color ElementColor(int e)
        {
            switch (e)
            {
                case 0: return AvStyleHost.FuiColor("friendly", AvTheme.Friendly);
                case 1: return AvStyleHost.FuiColor("caution", AvTheme.RailCaution);
                case 2: return AvStyleHost.FuiColor("ready", AvTheme.RailReady);
                default: return AvStyleHost.FuiColor("info", AvTheme.RailInfo);
            }
        }

        private void Style(Cell c)
        {
            Color rail = ElementColor(c.Element);
            if (!c.IsHead) rail = rail.WithAlpha(0.55f);
            bool flash = c.Selected && Time.unscaledTime < FlashUntil;
            Color surface = AvStyleHost.FuiColor(c.IsHead ? "surface" : "surface-inert", c.IsHead ? AvTheme.Surface : AvTheme.SurfaceInert);
            Color select = AvStyleHost.FuiColor("select", AvTheme.Accent);
            Color back = c.Selected ? Color.Lerp(surface, select, flash ? 0.34f : c.IsHead ? 0.2f : 0.16f) : c.Hover ? Color.Lerp(surface, Color.white, 0.07f) : surface;
            Color border = c.Selected && c.IsHead ? select : c.Hover ? AvStyleHost.FuiColor("frame", AvTheme.Frame) : Color.clear;
            c.Frame.Paint(back, border);
            c.Rail.color = rail;
            Color name = AvStyleHost.Resolve(AvStyleHost.FuiStyle("row-name").Color, AvTheme.TextPrimary);
            Color sub = AvStyleHost.Resolve(AvStyleHost.FuiStyle("row-sub").Color, AvTheme.Dim);
            c.Num.color = c.IsHead ? rail : sub;
            c.Name.color = c.IsHead ? name : name;
            if (!c.IsHead)
            {
                c.Type.color = sub;
                c.FuelTrack.color = AvStyleHost.FuiColor("frame", AvTheme.Frame).WithAlpha(0.8f);
                c.FuelFill.color = WmcState.Color(c.FuelRail);
            }
        }

        /// <summary>Rebuilds the table from the refresh's rows. Text and layout only change when a row's content does.</summary>
        public void Refresh(WmcContext c, string alert, AvState alertAs)
        {
            last = c;
            if (alert != alertWord || alertAs != alertState)
            {
                alertWord = alert;
                alertState = alertAs;
                WmcKit.Set(sumAlert, alert);
                sumAlert.color = WmcState.Color(Rail(alertAs));
            }
            for (int e = 0; e < perElement.Length; e++) perElement[e] = 0;
            for (int i = 0; i < c.Count; i++)
                if (c.Rows[i].Element < perElement.Length) perElement[c.Rows[i].Element]++;
            string letters = "";
            for (int e = 0; e < perElement.Length; e++)
                if (perElement[e] > 0) letters += (letters.Length > 0 ? " · " : "") + ElementRoster.Letter(e) + " " + perElement[e];
            WmcKit.Set(sumWing, "WING · " + c.Count + " AC");
            WmcKit.Set(sumElements, letters);
            WmcKit.Set(sumAction, collapsed ? "FLIGHT LIST ›" : "HIDE LIST ‹");
            allLatched = c.Scope.Kind == ScopeKind.Wing;
            hAll.color = allLatched ? AvStyleHost.FuiColor("select", AvTheme.Accent) : AvStyleHost.FuiColor("info", AvTheme.RailInfo);
            if (collapsed)
            {
                if (count != 0) { count = 0; Changed(); }
                return;
            }

            ElementGroups.Order(c.Rows, c.Count, order);
            int n = 0, r = 0, lastElement = -1, shapeKey = 0;
            for (int k = 0; k < c.Count; k++)
            {
                int ri = order[k], e = c.Rows[ri].Element;
                if (e != lastElement)
                {
                    lastElement = e;
                    if (n < kind.Length && e < heads.Length)
                    {
                        kind[n] = KindHead;
                        which[n++] = e;
                    }
                }
                if (r < rows.Length && n < kind.Length)
                {
                    kind[n] = KindRow;
                    which[n++] = r;
                    FillRow(rows[r], c.Rows[ri], c);
                    r++;
                }
            }
            for (int i = 0; i < n; i++)
                if (kind[i] == KindHead) FillHead(heads[which[i]], which[i], c);
            for (int i = 0; i < n; i++) shapeKey = shapeKey * 31 + kind[i] * 8 + which[i];
            bool empty = c.Count == 0;
            int open = c.Client ? 0 : Mathf.Max(0, WingService.MaxMembers - c.Count - Pending(c));
            bool wantSeats = empty || open > 0;
            SeatsRow(c, empty, open, wantSeats);
            // Show exactly the cells named; hide the rest.
            int hn = 0, rn = 0;
            Span<bool> headOn = stackalloc bool[MaxElements];
            Span<bool> rowOn = stackalloc bool[MaxMembers];
            for (int i = 0; i < n; i++)
            {
                if (kind[i] == KindHead) { headOn[which[i]] = true; hn++; }
                else { rowOn[which[i]] = true; rn++; }
            }
            for (int e = 0; e < heads.Length; e++) SetCellShown(heads[e], headOn[e]);
            for (int i = 0; i < rows.Length; i++)
            {
                SetCellShown(rows[i], rowOn[i]);
                if (!rowOn[i]) rows[i].Id = 0u;
            }
            shownHeads = hn;
            shownRows = rn;
            int sig = shapeKey * 7 + n + (wantSeats ? 1000003 : 0) + (seatsKey);
            if (n != count || sig != shown)
            {
                count = n;
                shown = sig;
                Changed();
            }
        }

        private int seatsKey = int.MinValue;

        private static int Pending(WmcContext c) => !c.Client && SpawnService.Instance != null ? SpawnService.Instance.PendingTotal : 0;

        private void SeatsRow(WmcContext c, bool empty, int open, bool on)
        {
            if (seats.Rect.gameObject.activeSelf != on) seats.Rect.gameObject.SetActive(on);
            seatsOn = on;
            if (!on) return;
            int key = (empty ? 1 : 0) + open * 2 + (c.Client ? 1000 : 0) + (c.Stale ? 2000 : 0);
            if (key == seatsKey) return;
            seatsKey = key;
            string name = empty ? (c.Client && c.Stale ? "WAITING FOR THE HOST'S WING" : "NO WINGMEN") : AvNum.Fixed(open, 0) + " OPEN";
            seats.Set(name, empty ? "CALL one on the WING row, or requisition on SUPPLY." : "Empty seats: requisition wingmen on SUPPLY.", "", AvState.Inert);
            SupplyButton.gameObject.SetActive(!c.Client);
        }

        /// <summary>Whether a SUPPLY button shows (open seats or no wingmen, host).</summary>
        public bool SeatsShown => seatsOn;

        public bool SeatsEmpty => last != null && last.Count == 0;

        public bool SeatsOpen => seatsOn && last != null && last.Count > 0;

        private void FillHead(Cell h, int e, WmcContext c)
        {
            int members = perElement[e];
            WingPlanner p = c.Wing != null && !c.Client && c.Wing.Roster.InUse(e) ? c.Wing.PlannerOf(e) : null;
            string name = c.Wing != null && !c.Client ? c.Wing.Roster.Name(e) : ElementRoster.Letter(e);
            int eta = p != null && p.Active ? (int)(p.Lead != null && p.Lead.Speed > 1f ? p.Leg * 100000 + Mathf.RoundToInt(Time.unscaledTime) : p.Leg) : -1;
            bool inScope = c.Scope.Kind == ScopeKind.Element && c.Scope.Element == e;
            int key = members * 1000003 + eta * 31 + (p != null && p.Active ? (int)p.Current.Kind + 1 : 0) + (name?.GetHashCode() ?? 0) + (inScope ? 1 << 28 : 0);
            if (!h.Styled || h.Selected != inScope)
            {
                h.Styled = true;
                h.Selected = inScope;
                Style(h);
            }
            if (key == h.Key) return;
            h.Key = key;
            string letter = ElementRoster.Letter(e);
            string title = (string.IsNullOrEmpty(name) || name == letter ? "ELEMENT " + letter : letter + " · " + Cut(name, 9)) + " · " + AvNum.Fixed(members, 0) + " AC";
            WmcKit.Set(h.Num, letter);
            WmcKit.Set(h.Name, title);
            string task = PreviewTask != null ? PreviewTask(e) : p == null ? (e == 0 ? "FORM · on you" : WmcText.Unknown)
                : !p.Active ? (e == 0 ? "FORM · on you" : "FORM")
                : TaskCard.Short(p.Current, p.Leg, p.Lead != null ? p.Lead.Position : Vec3.Zero, p.Lead != null ? p.Lead.Speed : 0f);
            WmcKit.Set(h.Task, task);
            h.Task.color = AvStyleHost.FuiColor("info", AvTheme.RailInfo);
        }

        private static string Cut(string s, int n) => s.Length <= n ? s : s.Substring(0, n);

        private void FillRow(Cell v, in SnapshotMember m, WmcContext c)
        {
            if (v.Id != m.Id)
            {
                v.Id = m.Id;
                v.Key = int.MinValue;
            }
            bool selected = c.Selection.Contains(m.Id) || (c.Scope.Kind == ScopeKind.Element && c.Scope.Element == m.Element);
            bool flash = selected && Time.unscaledTime < FlashUntil;
            WingMember wm = c.CanOrder ? c.MemberOf(m.Id) : null;
            Unit target = wm == null ? null : wm.AssignedTarget != null ? wm.AssignedTarget : wm.StandingTarget;
            if (target != null && target.disabled) target = null;
            int key = m.Slot * 131 + m.Duty * 17 + m.Behaviour * 7 + m.Flags + m.Fuel * 1031 + m.Ammo * 7 + m.Phase * 3 + m.Err10
                + (target != null ? (int)(target.persistentID.Id % 1000003u) * 257 : 0) + (selected ? 1 << 28 : 0) + (flash ? 1 << 29 : 0);
            if (key == v.Key) return;
            v.Key = key;
            if (!v.Styled || v.Selected != selected || flash)
            {
                v.Styled = true;
                v.Selected = selected;
            }
            v.Element = m.Element;
            WmcKit.Set(v.Num, AvNum.Fixed(m.Slot + 2, 0));
            Identity(c, m, out string callsign, out string type);
            WmcKit.Set(v.Name, callsign);
            WmcKit.Set(v.Type, type);
            string code = target == null ? null : target.definition != null && !string.IsNullOrEmpty(target.definition.code)
                ? target.definition.code : target.unitName;
            WmcKit.Set(v.Task, MemberLine.Task(m, code));
            string state = WingRows.State(m);
            v.Task.color = WmcState.Color(selected ? "live" : WmcStyle.Rail(state) == "inert" ? "info" : WmcStyle.Rail(state));
            v.Fuel = WingRows.Bar(WingRows.Fraction(m.Fuel));
            v.FuelRail = MemberLine.FuelRail(m.Flags);
            bool win = (m.Flags & (byte)SnapshotFlags.Winchester) != 0;
            WmcKit.Set(v.Ammo, win ? "WIN" : AvNum.Fixed(Mathf.RoundToInt(WingRows.Fraction(m.Ammo) * 100f), 0) + "%");
            v.Ammo.color = win ? WmcState.Color("danger") : AvStyleHost.Resolve(AvStyleHost.FuiStyle("row-sub").Color, AvTheme.Dim);
            bool dmg = (m.Flags & (byte)SnapshotFlags.Damaged) != 0;
            WmcKit.Set(v.Dmg, dmg ? "DMG" : "—");
            v.Dmg.color = dmg ? WmcState.Color("caution") : AvStyleHost.Resolve(AvStyleHost.FuiStyle("row-sub").Color, AvTheme.Dim);
            Style(v);
            // The fuel fill is placed against the row's slot; a changed fraction moves it.
            if (v.FuelTrack.rectTransform.sizeDelta.x > 0f)
            {
                Vector2 p = v.FuelTrack.rectTransform.anchoredPosition;
                AvLay.Place(v.FuelFill.rectTransform, p.x, -p.y, FuelW * v.Fuel, 5f);
            }
        }

        private void Identity(WmcContext c, in SnapshotMember m, out string callsign, out string type)
        {
            callsign = WingRows.Number(m.Slot);
            type = WmcText.Unknown;
            if (PreviewIdentity != null)
            {
                string[] id = PreviewIdentity(m.Id);
                if (id != null && id.Length >= 2)
                {
                    callsign = id[0];
                    type = id[1];
                    return;
                }
            }
            Unit u = WmcContext.UnitOf(m.Id);
            AircraftDefinition def = u is Aircraft a ? a.definition : null;
            if (def != null && !string.IsNullOrEmpty(def.code)) type = def.code;
            string cs = u is Aircraft air && !c.Client ? WingPilotRoster.Of(air)?.Callsign : null;
            if (!string.IsNullOrEmpty(cs)) callsign = Cut(cs, 11);
        }
    }
}
