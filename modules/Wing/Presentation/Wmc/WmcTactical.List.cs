using NOAvionics;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

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
    /// <summary>TACTICAL's FLIGHT (spec FUI §TACTICAL; the 0.9 rows) on kit v2: a section header with the element counts, then per
    /// element an <see cref="AvRow"/> head (name, task, aircraft count; a click sends orders to the element) and one line per wingman —
    /// slot, airframe, type, callsign, fuel and ammo tapes (joker tick), [RTB] (press twice), [RDR], [EJ] and [›] (INSPECT), the task
    /// in its state colour under them. Aircraft in the scope carry the accent rail and flash once when an order goes to them; the rest
    /// are dimmed while a sub-scope is picked. The console body scrolls, so the list never pages. Views are built once and moved into
    /// place; text is set only when it changes. (Kit gap: <see cref="AvRow"/> takes three trailing controls at most and a wingman
    /// carries four commands and two tapes, so the wingman line is a local <see cref="AvPart"/> built from kit primitives.)</summary>
    internal sealed partial class WmcTactical
    {
        private const int MaxRows = WcSnapshot.MaxMembers, MaxLines = MaxRows + ElementRoster.MaxElements;
        private const float FlashSeconds = 0.35f, NoPager = 1e9f;

        private readonly int[] order = new int[MaxRows];
        private readonly FlightLine[] lines = new FlightLine[MaxLines];
        private readonly ConfirmGate rtbGate = new ConfirmGate();
        // Its own gate (eject.md): RTB then EJ on one row must not confirm the ejection on the first EJ press.
        private readonly ConfirmGate ejGate = new ConfirmGate();
        private readonly int[] elementCounts = new int[ElementRoster.MaxElements];
        private AvSection flightSection;
        private FlightView flightView;
        private int flightKey = int.MinValue;
        private float flashUntil;

        private void BuildFlight(AvFlow f)
        {
            flightSection = f.Section(AvIcon.Plane, "FLIGHT");
            flightView = f.Add(new FlightView(f.Content, this));
        }

        /// <summary>The wingman line: rail, slot box, aircraft icon, type, callsign, the fuel and ammo tapes, four commands, and the
        /// task under them (it wraps, so nothing is cut).</summary>
        private sealed class FlightRow : AvPart
        {
            private const float PadX = 10f, PadY = 5f, Line = 24f, SlotBox = 18f, TapeW = 28f;
            private const float BtnRtb = 42f, BtnRdr = 52f, BtnEj = 32f, BtnGo = 30f, BtnGap = 3f;
            private readonly WmcTactical owner;
            private readonly int index;
            private readonly AvFrame frame;
            private readonly Image rail, slotBox, icon, fuelTrack, fuelFill, ammoTrack, ammoFill, jokerMark;
            private readonly TMP_Text slot, type, callsign, task;
            private readonly CanvasGroup group;
            private bool hover, placed;
            private AvState state = AvState.Info;

            public readonly AvControl Rtb, Rdr, Ej, Inspect;
            public int Index => index;
            public uint Id;
            public string IdText;
            public int Key = int.MinValue, TapeKey = int.MinValue;
            public bool Selected, Styled, RtbAsking, EjAsking, Dim;
            public string RdrText;
            public string FuelRail = "info", AmmoRail = "info";
            public float FuelFraction, AmmoFraction;

            public Transform HitTarget => frame.transform;

            public FlightRow(RectTransform parent, WmcTactical tactical, int rowIndex)
            {
                owner = tactical;
                index = rowIndex;
                Rect = AvLay.Child(parent, "FlightRow" + rowIndex);
                group = Rect.gameObject.AddComponent<CanvasGroup>();
                frame = AvFrame.Add(Rect, "Frame", default(AvChamfer));
                AvLay.Fill(frame.rectTransform);
                AvHit hit = AvHit.On(frame);
                hit.Hover = h => { hover = h; Restyle(); };
                hit.Click = e => owner.ClickRow(index);
                AvHelpTip.Attach(frame.gameObject, "Click: orders go to this wingman. Shift-click: add or remove it.");
                rail = AvLay.Solid(Rect, "Rail", Color.clear);
                slotBox = AvLay.Solid(Rect, "SlotBox", Color.clear);
                slot = AvText.Make(Rect, "Slot", AvTextRole.Label, "", TextAlignmentOptions.Center);
                icon = AvLay.Solid(Rect, "Icon", Color.white);
                icon.preserveAspect = true;
                icon.raycastTarget = false;
                type = AvText.Make(Rect, "Type", AvTextRole.DataSmall);
                AvText.Fit(type, false);
                callsign = AvText.Make(Rect, "Callsign", AvTextRole.Label);
                AvText.Fit(callsign, false);
                fuelTrack = AvLay.Solid(Rect, "FuelTrack", Color.clear);
                fuelFill = AvLay.Solid(Rect, "FuelFill", Color.clear);
                jokerMark = AvLay.Solid(Rect, "Joker", Color.clear);
                ammoTrack = AvLay.Solid(Rect, "AmmoTrack", Color.clear);
                ammoFill = AvLay.Solid(Rect, "AmmoFill", Color.clear);
                task = AvText.Make(Rect, "Task", AvTextRole.ProseSmall, "", TextAlignmentOptions.TopLeft, true);
                Rtb = AvControl.Make(Rect, new AvControl.Spec("RTB", () => owner.AskRtb(index)));
                Rdr = AvControl.Make(Rect, new AvControl.Spec("RDR", () => owner.ToggleRadar(index)));
                Ej = AvControl.Make(Rect, new AvControl.Spec("EJ", () => owner.AskEject(index), AvButtonStyle.Danger));
                Inspect = AvControl.Make(Rect, new AvControl.Spec("", () => owner.Inspect(index), AvButtonStyle.Quiet, AvIcon.ChevronRight));
                Rtb.Help = "Send this wingman home to the reserve (press twice).";
                Rdr.Help = "This aircraft's radar: RDR on, EMCON silent or off. Press to switch it (the rest of the wing keeps its setting).";
                Ej.Help = "Eject this pilot (press twice): the aircraft is lost, search and rescue picks the pilot up.";
                Inspect.Help = "This aircraft on WING › INSPECT: stores, fuel, hull, its task and what it has been doing.";
                Restyle();
            }

            public string TaskText => task.text;

            public string CallsignText => callsign.text;

            public void SetIdentity(string slotText, Sprite sprite, string typeText, string callsignText)
            {
                WmcKit.Set(slot, slotText);
                icon.sprite = sprite;
                icon.enabled = sprite != null;
                WmcKit.Set(type, typeText);
                WmcKit.Set(callsign, callsignText);
            }

            /// <summary>The task line and the row's state class (returns true when the height may have changed).</summary>
            public bool SetTask(string text, string railClass)
            {
                AvState s = WmcState.Of(railClass);
                bool wrapped = task.text != text;
                WmcKit.Set(task, text);
                if (s != state)
                {
                    state = s;
                    Restyle();
                }
                return wrapped;
            }

            public void SetSelected(bool selected)
            {
                Selected = selected;
                Restyle();
            }

            public void SetDim(bool dim)
            {
                Dim = dim;
                group.alpha = dim ? 0.55f : 1f;
            }

            public void SetTapes(float fuel, string fuelRail, float ammo, string ammoRail)
            {
                FuelFraction = WingRows.Bar(fuel);
                AmmoFraction = WingRows.Bar(ammo);
                FuelRail = fuelRail;
                AmmoRail = ammoRail;
                PaintTapes();
            }

            public override float Measure(float width)
            {
                float taskH = task.text.Length > 0 ? 2f + AvText.Height(task, width - 2f * PadX - 4f) : 0f;
                return Mathf.Max(AvGridTokens.Row, PadY + Line + taskH + PadY);
            }

            public override void Place(AvSlot s)
            {
                base.Place(s);
                placed = true;
                float y = PadY, w = s.W;
                AvLay.Place(rail.rectTransform, 0f, 0f, 3f, s.H);
                AvLay.Place(slotBox.rectTransform, PadX, y + 3f, SlotBox, SlotBox);
                AvLay.Place(slot.rectTransform, PadX, y + 3f, SlotBox, SlotBox);
                AvLay.Place(icon.rectTransform, PadX + SlotBox + 4f, y + 5f, 14f, 14f);
                AvLay.Place(type.rectTransform, PadX + SlotBox + 22f, y, 38f, Line);
                AvLay.Place(callsign.rectTransform, PadX + SlotBox + 62f, y, 74f, Line);
                float buttons = BtnRtb + BtnRdr + BtnEj + BtnGo + 3f * BtnGap, bx = w - PadX + 2f - buttons;
                float tapes = bx - 2f * (TapeW + 4f) - 4f;
                AvLay.Place(fuelTrack.rectTransform, tapes, y + 9f, TapeW, 6f);
                AvLay.Place(ammoTrack.rectTransform, tapes + TapeW + 4f, y + 9f, TapeW, 6f);
                AvLay.Place(jokerMark.rectTransform, tapes + TapeW * 0.35f, y + 6f, 1f, 12f);
                PaintTapes();
                AvLay.Place(Rtb.Rect, bx, y, BtnRtb, Line);
                bx += BtnRtb + BtnGap;
                AvLay.Place(Rdr.Rect, bx, y, BtnRdr, Line);
                bx += BtnRdr + BtnGap;
                AvLay.Place(Ej.Rect, bx, y, BtnEj, Line);
                bx += BtnEj + BtnGap;
                AvLay.Place(Inspect.Rect, bx, y, BtnGo, Line);
                float taskY = y + Line + 2f;
                AvLay.Place(task.rectTransform, PadX + 4f, taskY, w - 2f * PadX - 4f, Mathf.Max(0f, s.H - taskY - PadY));
            }

            private void PaintTapes()
            {
                if (!placed) return;
                float trackW = fuelTrack.rectTransform.sizeDelta.x;
                Vector2 fp = fuelTrack.rectTransform.anchoredPosition, ap = ammoTrack.rectTransform.anchoredPosition;
                AvLay.Place(fuelFill.rectTransform, fp.x + 1f, -fp.y + 1f, (trackW - 2f) * FuelFraction, 4f);
                AvLay.Place(ammoFill.rectTransform, ap.x + 1f, -ap.y + 1f, (trackW - 2f) * AmmoFraction, 4f);
                fuelFill.color = WmcState.Color(FuelRail);
                ammoFill.color = WmcState.Color(AmmoRail);
            }

            public override void Restyle()
            {
                string st = Selected ? "armed" : hover ? "hover" : null;
                AvStyle r = AvStyleHost.FuiStyle("row " + AvStates.Class(state), st);
                frame.Paint(AvStyleHost.Resolve(r.Background, AvTheme.SurfaceInert),
                    r.Border.HasValue ? AvStyleHost.Resolve(r.Border, Color.clear) : Color.clear);
                rail.color = AvStyleHost.Resolve(r.Rail, AvTheme.RailInfo);
                slotBox.color = AvStyleHost.FuiColor(Selected ? "select" : "surface-inert", Selected ? AvTheme.Accent : AvTheme.SurfaceInert);
                slot.color = AvStyleHost.FuiColor(Selected ? "ground" : "ink", Selected ? AvTheme.TextInk : AvTheme.TextPrimary);
                type.color = AvStyleHost.Resolve(AvStyleHost.FuiStyle("row-sub").Color, AvTheme.Dim);
                callsign.color = AvStyleHost.Resolve(AvStyleHost.FuiStyle("row-name").Color, AvTheme.TextPrimary);
                task.color = WmcState.Color(AvStates.Class(state == AvState.Ready ? AvState.Info : state));
                Color track = AvStyleHost.FuiColor("surface-inert", AvTheme.SurfaceInert);
                fuelTrack.color = ammoTrack.color = track;
                jokerMark.color = AvStyleHost.FuiColor("caution", AvTheme.Warning);
                PaintTapes();
                Rtb.Restyle();
                Rdr.Restyle();
                Ej.Restyle();
                Inspect.Restyle();
            }
        }

        /// <summary>The flight list as one flow line: element heads, wingman lines and (when there are seats free or no wingmen) a seats
        /// line, stacked top to bottom in the order <see cref="FlightList"/> gives.</summary>
        private sealed class FlightView : AvPart
        {
            private const float Gap = 2f;
            private const int KindHead = 0, KindRow = 1, KindSeats = 2;
            private readonly FlightRow[] rows = new FlightRow[MaxRows];
            private readonly AvRow[] heads = new AvRow[ElementRoster.MaxElements];
            private readonly int[] headKeys = new int[ElementRoster.MaxElements];
            private readonly AvRow seats;
            private readonly int[] kind = new int[MaxLines + 1], which = new int[MaxLines + 1];
            private readonly bool[] headOn = new bool[ElementRoster.MaxElements], rowOn = new bool[MaxRows];
            private int count;

            public FlightRow this[int i] => rows[i];

            public AvRow Seats => seats;

            public FlightView(RectTransform parent, WmcTactical owner)
            {
                Rect = AvLay.Child(parent, "FlightView");
                for (int e = 0; e < heads.Length; e++)
                {
                    int k = e;
                    heads[e] = new AvRow(Rect, () => owner.PickElement(k));
                    heads[e].Help = "Orders go to this whole element.";
                    heads[e].Rect.gameObject.SetActive(false);
                    headKeys[e] = int.MinValue;
                    owner.ids.Add("tac.list.el" + e, heads[e]);
                }
                for (int i = 0; i < rows.Length; i++)
                {
                    FlightRow row = rows[i] = new FlightRow(Rect, owner, i);
                    row.Rect.gameObject.SetActive(false);
                    string id = "tac.list.row" + i;
                    owner.ids.Add(id, row.HitTarget);
                    owner.ids.Add(id + ".rtb", row.Rtb);
                    owner.ids.Add(id + ".rdr", row.Rdr);
                    owner.ids.Add(id + ".ej", row.Ej);
                    owner.ids.Add(id + ".inspect", row.Inspect);
                }
                seats = new AvRow(Rect);
                AvControl toSupply = seats.AddTrailing(new AvControl.Spec("SUPPLY", owner.OpenSupply));
                toSupply.Help = "Requisition a wingman: pilot, airframe, fit and base.";
                owner.ids.Add("tac.list.open", owner.OpenSupply, () => seats.Rect.gameObject.activeInHierarchy && owner.seatsOpen);
                owner.ids.Add("tac.list.supply", owner.OpenSupply, () => seats.Rect.gameObject.activeInHierarchy && owner.seatsEmpty);
                seats.Rect.gameObject.SetActive(false);
            }

            public AvRow Head(int e) => heads[e];

            public int HeadKey(int e) => headKeys[e];

            public void SetHeadKey(int e, int key) => headKeys[e] = key;

            public void Begin() => count = 0;

            public void AddHead(int e)
            {
                if (count >= kind.Length) return;
                kind[count] = KindHead;
                which[count++] = e;
            }

            public void AddRow(int r)
            {
                if (count >= kind.Length) return;
                kind[count] = KindRow;
                which[count++] = r;
            }

            public void AddSeats()
            {
                if (count >= kind.Length) return;
                kind[count] = KindSeats;
                which[count++] = 0;
            }

            /// <summary>Shows exactly the parts named since <see cref="Begin"/> (the rest hide); the caller relayouts.</summary>
            public void End()
            {
                System.Array.Clear(headOn, 0, headOn.Length);
                System.Array.Clear(rowOn, 0, rowOn.Length);
                bool seatsOn = false;
                for (int i = 0; i < count; i++)
                {
                    if (kind[i] == KindHead) headOn[which[i]] = true;
                    else if (kind[i] == KindRow) rowOn[which[i]] = true;
                    else seatsOn = true;
                }
                for (int e = 0; e < heads.Length; e++)
                    if (heads[e].Rect.gameObject.activeSelf != headOn[e]) heads[e].Rect.gameObject.SetActive(headOn[e]);
                for (int i = 0; i < rows.Length; i++)
                {
                    if (rows[i].Rect.gameObject.activeSelf != rowOn[i]) rows[i].Rect.gameObject.SetActive(rowOn[i]);
                    if (!rowOn[i]) rows[i].Id = 0u;
                }
                if (seats.Rect.gameObject.activeSelf != seatsOn) seats.Rect.gameObject.SetActive(seatsOn);
            }

            private float HeightOf(int i) =>
                kind[i] == KindHead ? heads[which[i]].Measure(measured)
                : kind[i] == KindRow ? rows[which[i]].Measure(measured) : seats.Measure(measured);

            private float measured;

            public override float Measure(float width)
            {
                measured = width;
                float h = 0f;
                for (int i = 0; i < count; i++) h += HeightOf(i) + Gap;
                return Mathf.Max(0f, h - Gap);
            }

            public override void Place(AvSlot s)
            {
                base.Place(s);
                measured = s.W;
                float y = 0f;
                for (int i = 0; i < count; i++)
                {
                    float h = HeightOf(i);
                    var slot = new AvSlot(0f, y, s.W, h);
                    if (kind[i] == KindHead) heads[which[i]].Place(slot);
                    else if (kind[i] == KindRow) rows[which[i]].Place(slot);
                    else seats.Place(slot);
                    y += h + Gap;
                }
            }

            public override void Restyle()
            {
                foreach (AvRow h in heads) h.Restyle();
                foreach (FlightRow r in rows) r.Restyle();
                seats.Restyle();
            }
        }

        private void ClickRow(int index)
        {
            if (last == null || flightView[index].Id == 0u) return;
            uint id = flightView[index].Id;
            bool shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            if (shift) last.Selection.Toggle(id);
            else last.Selection.SelectOnly(id);
            // Review P3 I3: an order pressed right after the click goes to what the click chose.
            last.Rescope();
        }

        private void AskRtb(int index)
        {
            FlightRow v = flightView[index];
            if (last == null || v.Id == 0u) return;
            WmcUi.Order(last, () =>
            {
                if (!rtbGate.Press(v.IdText, Time.unscaledTime))
                {
                    WingToast.Show("Send " + v.CallsignText + " home? Press RTB? again");
                    return;
                }
                WmcMotion.Punch(v.Rtb);
                WingOrders.Run(WingOrder.Of(OrderKind.Rtb, WingScope.OfMembers(v.Id)));
            });
        }

        private void ToggleRadar(int index)
        {
            FlightRow v = flightView[index];
            WingMember m = last?.MemberOf(v.Id);
            if (m == null) return;
            bool on = last.Wing.DoctrineFor(m).Radar == RadarPolicy.On;
            WmcMotion.Punch(v.Rdr);
            WmcUi.Order(last, () => WingOrders.Run(new WingOrder
            {
                Kind = OrderKind.SetOverride, Number = (int)DoctrineAxis.Radar, Text = on ? "Off" : "On", Scope = WingScope.OfMembers(v.Id),
            }));
        }

        private void AskEject(int index)
        {
            FlightRow v = flightView[index];
            if (last == null || v.Id == 0u) return;
            WmcUi.Order(last, () =>
            {
                if (!ejGate.Press(v.IdText, Time.unscaledTime))
                {
                    WingToast.Show("Eject " + v.CallsignText + "? The aircraft is lost. Press EJ? again");
                    return;
                }
                WingOrders.Run(new WingOrder { Kind = OrderKind.Eject, Scope = WingScope.OfMembers(v.Id), Flag = true });
            });
        }

        private void Inspect(int index)
        {
            if (last == null || flightView[index].Id == 0u) return;
            WmcPanel.Instance?.Inspect(flightView[index].Id);
        }

        private void FocusAircraft(uint id)
        {
            WmcMap.Center(WmcContext.UnitOf(id));
            last.Selection.SelectOnly(id);
            last.Rescope();
        }

        /// <summary>The aircraft in scope flash once (an order went to them).</summary>
        private void FlashScope() => flashUntil = WmcMotion.Reduced ? 0f : Time.unscaledTime + FlashSeconds;

        private void RefreshFlightHead(WmcContext c)
        {
            int[] per = elementCounts;
            for (int e = 0; e < per.Length; e++) per[e] = 0;
            for (int i = 0; i < c.Count; i++)
                if (c.Rows[i].Element < per.Length) per[c.Rows[i].Element]++;
            int open = c.Client ? 0 : Mathf.Max(0, WingService.MaxMembers - c.Count - Pending(c));
            int key = per[0] + per[1] * 10 + per[2] * 100 + per[3] * 1000 + open * 10000 + (c.Client ? 1 : 0) * 100000;
            if (key == flightKey) return;
            flightKey = key;
            string note = "";
            for (int e = 0; e < per.Length; e++)
                if (per[e] > 0) note += (note.Length > 0 ? " · " : "") + ElementRoster.Letter(e) + " " + AvNum.Fixed(per[e], 0);
            flightSection.SetCaption(note.Length > 0 ? note : c.Client ? "HOST'S WING" : "EMPTY");
            relayout = true;
        }

        private void RefreshList(WmcContext c)
        {
            int n = FlightList.Page(c.Rows, c.Count, order, NoPager, 0, lines, out int _, pagerOutside: true);
            int open = c.Client ? 0 : Mathf.Max(0, WingService.MaxMembers - c.Count - Pending(c));

            flightView.Begin();
            int r = 0;
            for (int k = 0; k < n; k++)
            {
                FlightLine line = lines[k];
                if (line.Header)
                {
                    flightView.AddHead(line.Element);
                    FillHeader(line.Element, c);
                }
                else if (r < MaxRows)
                {
                    flightView.AddRow(r);
                    FillRow(flightView[r], c.Rows[line.Row], c);
                    r++;
                }
            }
            bool empty = c.Count == 0;
            if (empty || open > 0)
            {
                flightView.AddSeats();
                FillSeats(c, empty, open);
            }
            flightView.End();
            int shape = n * 31 + (empty ? 1 : 0) + open * 7;
            if (shape != listShape)
            {
                listShape = shape;
                relayout = true;
            }
        }

        private int listShape = int.MinValue, seatsKey = int.MinValue;
        private bool seatsEmpty, seatsOpen;

        private void OpenSupply() => WmcPanel.Instance?.Show(WmcTabs.Supply);

        private static int Pending(WmcContext c) => !c.Client && SpawnService.Instance != null ? SpawnService.Instance.PendingTotal : 0;

        private void FillSeats(WmcContext c, bool empty, int open)
        {
            int key = (empty ? 1 : 0) + open * 2 + (c.Client ? 1000 : 0) + (c.Stale ? 2000 : 0);
            if (key == seatsKey) return;
            seatsKey = key;
            seatsEmpty = empty;
            seatsOpen = open > 0;
            AvRow seats = flightView.Seats;
            string name = empty ? (c.Client && c.Stale ? "WAITING FOR THE HOST'S WING" : "NO WINGMEN") : AvNum.Fixed(open, 0) + " OPEN";
            seats.Set(name, empty ? "CALL one on the WING row, or requisition on SUPPLY." : "Empty seats in the wing: requisition wingmen on SUPPLY.",
                "", AvState.Inert);
            relayout = true;
        }

        private void FillHeader(int e, WmcContext c)
        {
            int count = 0;
            for (int i = 0; i < c.Count; i++)
                if (c.Rows[i].Element == e) count++;
            WingPlanner p = c.Wing != null && !c.Client && c.Wing.Roster.InUse(e) ? c.Wing.PlannerOf(e) : null;
            string name = c.Wing != null && !c.Client ? c.Wing.Roster.Name(e) : ElementRoster.Letter(e);
            // The ETA ticks by the second; everything else changes rarely.
            int eta = p != null && p.Active ? (int)(p.Lead != null && p.Lead.Speed > 1f ? p.Leg * 100000 + Mathf.RoundToInt(Time.unscaledTime) : p.Leg) : -1;
            int key = count * 1000003 + eta * 31 + (p != null && p.Active ? (int)p.Current.Kind + 1 : 0) + (name?.GetHashCode() ?? 0);
            if (key == flightView.HeadKey(e)) return;
            flightView.SetHeadKey(e, key);
            string letter = ElementRoster.Letter(e);
            string title = string.IsNullOrEmpty(name) || name == letter ? letter : letter + " · " + name;
            string task = p == null ? (e == 0 ? "FORM · on you" : WmcText.Unknown)
                : !p.Active ? (e == 0 ? "FORM · on you" : "FORM")
                : TaskCard.Short(p.Current, p.Leg, p.Lead != null ? p.Lead.Position : Vec3.Zero, p.Lead != null ? p.Lead.Speed : 0f);
            flightView.Head(e).Set(title, task, AvNum.Fixed(count, 0) + " AC", AvState.Info);
            relayout = true;
        }

        private void FillRow(FlightRow v, in SnapshotMember m, WmcContext c)
        {
            if (v.Id != m.Id)
            {
                v.Id = m.Id;
                v.IdText = AvNum.Fixed(m.Id, 0);
                v.Key = int.MinValue;
                v.TapeKey = int.MinValue;
            }
            bool selected = c.Selection.Contains(m.Id);
            if (!v.Styled || v.Selected != selected)
            {
                v.Styled = true;
                v.SetSelected(selected);
            }
            // Out of a picked sub-scope: dimmed, so who gets the next order reads at a glance.
            bool inScope = c.InScope(m), dim = c.Scope.Kind != ScopeKind.Wing && !inScope;
            if (dim != v.Dim) v.SetDim(dim);
            bool flash = inScope && Time.unscaledTime < flashUntil;
            string state = WingRows.State(m);
            WingMember wm = c.CanOrder ? c.MemberOf(m.Id) : null;
            Unit target = wm == null ? null : wm.AssignedTarget != null ? wm.AssignedTarget : wm.StandingTarget;
            if (target != null && target.disabled) target = null;
            int key = m.Slot * 131 + m.Duty * 17 + m.Behaviour * 7 + m.Flags + state.GetHashCode()
                + (target != null ? (int)(target.persistentID.Id % 1000003u) * 257 : 0) + (flash ? 1 << 29 : 0) + (inScope ? 1 << 28 : 0);
            if (key != v.Key)
            {
                v.Key = key;
                Unit u = WmcContext.UnitOf(m.Id);
                AircraftDefinition def = u is Aircraft a ? a.definition : null;
                string callsign = u is Aircraft air && !c.Client ? WingPilotRoster.Of(air)?.Callsign : null;
                Sprite icon = def != null ? IconFactory.Aircraft(def) : null;
                v.SetIdentity(AvNum.Fixed(m.Slot + 2, 0), icon, def != null && !string.IsNullOrEmpty(def.code) ? def.code : WmcText.Unknown,
                    string.IsNullOrEmpty(callsign) ? WingRows.Number(m.Slot) : callsign);
                string code = target == null ? null : target.definition != null && !string.IsNullOrEmpty(target.definition.code)
                    ? target.definition.code : target.unitName;
                string rail = WmcStyle.Rail(state);
                // The scope's accent rail; bright for one refresh after an order (game-feel tier 1).
                string shown = flash ? "live" : inScope && c.Scope.Kind != ScopeKind.Wing ? "live" : rail;
                if (v.SetTask(MemberLine.Task(m, code), shown)) relayout = true;
            }
            int tapes = m.Fuel * 1031 + m.Ammo * 7 + m.Flags;
            if (tapes != v.TapeKey)
            {
                v.TapeKey = tapes;
                v.SetTapes(WingRows.Fraction(m.Fuel), MemberLine.FuelRail(m.Flags), WingRows.Fraction(m.Ammo), MemberLine.AmmoRail(m.Flags));
            }
            bool asking = rtbGate.IsArmed(v.IdText, Time.unscaledTime);
            if (asking != v.RtbAsking)
            {
                v.RtbAsking = asking;
                v.Rtb.Label = asking ? "RTB?" : "RTB";
            }
            v.Rtb.Interactable = c.CanOrder;
            asking = ejGate.IsArmed(v.IdText, Time.unscaledTime);
            if (asking != v.EjAsking)
            {
                v.EjAsking = asking;
                v.Ej.Label = asking ? "EJ?" : "EJ";
            }
            v.Ej.Interactable = c.CanOrder;
            bool hasRadar = wm != null && wm.Aircraft != null && wm.Aircraft.radar is Radar;
            string rdr = wm == null ? "RDR" : !hasRadar ? "RDR —" : c.Wing.DoctrineFor(wm).Radar == RadarPolicy.On ? "RDR" : "EMCON";
            if (!ReferenceEquals(rdr, v.RdrText))
            {
                v.RdrText = rdr;
                v.Rdr.Label = rdr;
            }
            v.Rdr.Interactable = hasRadar;
        }
    }
}
