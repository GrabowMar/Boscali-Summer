using System.Collections.Generic;
using System.Text;
using UnityEngine;

using BoscaliSummer.Modules.Wing.Domain;
using BoscaliSummer.Modules.Wing.Domain.Pure;
using BoscaliSummer.Modules.Wing.Runtime;
using BoscaliSummer.Modules.Wing.Networking;
using NOAvionics;

namespace BoscaliSummer.Modules.Wing.Presentation
{
    /// <summary>The Call Ladder (spec 2026-10-04 §2): while the wing key is held it replaces the HUD strip with a numbered menu.
    /// WHO (the wing, an element, a member) then DO (the command card's orders, one group of four a page) then, for an order
    /// that needs one, WHERE (the nearest threats, MY TARGET, HERE, MAP CLICK). Navigation is <see cref="LadderState"/>; this
    /// builds the lists, words the lines and sends. Host only, as every order.</summary>
    internal static class WingCallLadder
    {
        private enum WhereKind : byte { Threat, MyTarget, Here, Map }

        private struct WhereChoice
        {
            public WhereKind Kind;
            public uint Id;
            public string Label;
        }

        public const int MaxLines = 12, MaxWho = 13, MaxWhere = 8, PerDoPage = OrderGrid.Columns;

        public static readonly LadderState State = new LadderState();

        /// <summary>The ladder is engaged (Enter, or a letter chord on an order that needs a point); digits then walk it. Otherwise
        /// the held wing key shows the stance list and digits 1-6 pick a stance.</summary>
        public static bool Open { get; private set; }

        /// <summary>Bumped by every change the HUD must redraw at once.</summary>
        public static int Version { get; private set; }

        private static readonly WingScope[] whoScope = new WingScope[MaxWho];
        private static readonly string[] whoLabel = new string[MaxWho];
        private static int whoCount;
        /// <summary>WHO as picked (review fix): Send re-finds it by label, so a wingman lost meanwhile aborts instead of the order
        /// silently going to the whole wing or to the aircraft that shifted into its index.</summary>
        private static string pickedWho;
        private static readonly WhereChoice[] where = new WhereChoice[MaxWhere];
        private static int whereCount;
        private static readonly ConfirmGate dismissGate = new ConfirmGate();
        private static readonly AckLine[] chips = new AckLine[AckFeed.MaxChips];
        private static readonly StringBuilder sb = new StringBuilder(96);
        private static readonly string numberOpen = "<color=#" + ColorUtility.ToHtmlStringRGB(AvTheme.Accent) + ">";

        /// <summary>The wing key went up (or text is being typed): the ladder closes and forgets.</summary>
        public static void Close()
        {
            if (!Open && State.Step == LadderStep.Who && State.Who < 0) return;
            Open = false;
            pickedWho = null;
            State.Reset();
            Version++;
        }

        public static void Begin()
        {
            Open = true;
            State.Reset();
            Version++;
        }

        // ------------------------------------------------------------------ the digits

        public static void Press(int digit)
        {
            if (!Open) return;
            LadderStep before = State.Step;
            int total = Total(before);
            LadderKind k = State.Press(digit, total, PerPage(before, total), NeedsWhere);
            if (before == LadderStep.Who && State.Step != LadderStep.Who && State.Who >= 0 && State.Who < whoCount)
                pickedWho = whoLabel[State.Who];
            switch (k)
            {
                case LadderKind.Close:
                    Close();
                    return;
                case LadderKind.Send:
                    Send();
                    break;
                case LadderKind.Moved:
                    if (State.Step == LadderStep.Where && before != LadderStep.Where) BuildWhere(CellAt(State.Do));
                    break;
            }
            Version++;
        }

        private static bool NeedsWhere(int doIndex) => CellAt(doIndex).Input != GridInput.Now;

        private static int Total(LadderStep step)
        {
            switch (step)
            {
                case LadderStep.Who:
                    BuildWho();
                    return whoCount;
                case LadderStep.Do: return OrderGrid.Rows * OrderGrid.Columns;
                default: return whereCount;
            }
        }

        private static int PerPage(LadderStep step, int total) => step == LadderStep.Do ? PerDoPage : LadderState.AutoPerPage(total);

        // -------------------------------------------------------------- the letter chords

        /// <summary>Wing key + a command-card letter (<see cref="ChordResolver.OrderKeys"/> order, the grid's cell order): an order
        /// that needs nothing goes to the whole wing at once; one that needs a point or an enemy opens the ladder at its WHERE.</summary>
        public static void RunOrderKey(int cellIndex)
        {
            if (cellIndex < 0 || cellIndex >= OrderGrid.Rows * OrderGrid.Columns) return;
            GridCell cell = CellAt(cellIndex);
            if (cell.Input != GridInput.Now)
            {
                if (!Ready(out string why))
                {
                    WingToast.Show(why);
                    return;
                }
                Open = true;
                pickedWho = null;
                State.OpenAt(cellIndex, PerDoPage);
                BuildWhere(cell);
                Version++;
                return;
            }
            RunNow(cell, WingScope.Wing);
        }

        /// <summary>Wing key + 1-6: the stance slot for the whole wing (host only).</summary>
        public static void RunStance(int slot)
        {
            if (!Ready(out string why))
            {
                WingToast.Show(why);
                return;
            }
            Stance s = WmcStanceActions.Book.Slot(slot);
            if (s == null)
            {
                WingToast.Show("Stance slot " + (slot + 1) + " is empty");
                return;
            }
            WmcStanceActions.Run(WingScope.Wing, s);
        }

        // ------------------------------------------------------------------- the lists

        private static bool Helos()
        {
            WingService w = WingService.Instance;
            if (w == null) return false;
            foreach (WingMember m in w.Members)
                if (!m.Released && (object)m.Aircraft != null && m.Profile != null && m.Profile.Class != AirframeClass.FixedWing) return true;
            return false;
        }

        private static GridCell CellAt(int index) =>
            OrderGrid.At(index / OrderGrid.Columns, index % OrderGrid.Columns, Helos());

        /// <summary>WING, then each element in use (when there are two or more), then every member by number.</summary>
        private static void BuildWho()
        {
            whoCount = 0;
            whoScope[whoCount] = WingScope.Wing;
            whoLabel[whoCount++] = "WING";
            WingService w = WingService.Instance;
            if (w == null) return;
            int elements = 0;
            for (int e = 0; e < WingScope.MaxElements; e++)
                if (CountIn(w, e) > 0) elements++;
            if (elements > 1)
                for (int e = 0; e < WingScope.MaxElements && whoCount < MaxWho; e++)
                    if (CountIn(w, e) > 0)
                    {
                        whoScope[whoCount] = WingScope.OfElement(e);
                        whoLabel[whoCount++] = "ELEMENT " + ElementRoster.Letter(e);
                    }
            foreach (WingMember m in w.Members)
            {
                if (whoCount >= MaxWho) break;
                if (m.Released || !m.Alive || (object)m.Aircraft == null) continue;
                whoScope[whoCount] = WingScope.OfMembers(m.Aircraft.persistentID.Id);
                whoLabel[whoCount++] = "#" + m.Number;
            }
        }

        private static int CountIn(WingService w, int element)
        {
            int n = 0;
            foreach (WingMember m in w.Members)
                if (!m.Released && m.Alive && (object)m.Aircraft != null && w.ElementOf(m) == element) n++;
            return n;
        }

        private static void BuildWhere(GridCell cell)
        {
            whereCount = 0;
            WingService w = WingService.Instance;
            if (cell.Order == GridOrder.Attack && w != null)
            {
                int n = w.Threats(out ThreatRow[] rows);
                for (int i = 0; i < n && whereCount < 5; i++)
                    where[whereCount++] = new WhereChoice { Kind = WhereKind.Threat, Id = rows[i].Id, Label = WingThreatList.Text(rows[i]) };
                where[whereCount++] = new WhereChoice { Kind = WhereKind.MyTarget, Label = "MY TARGET" };
            }
            else if (OrderGrid.HasHere(cell.Order))
                where[whereCount++] = new WhereChoice { Kind = WhereKind.Here, Label = "HERE" };
            where[whereCount++] = new WhereChoice { Kind = WhereKind.Map, Label = "MAP CLICK" };
        }

        // --------------------------------------------------------------------- sending

        private static bool Ready(out string why)
        {
            why = null;
            if (WingNet.ClientOnly) why = "Orders are host only for now";
            else if (WingService.Instance?.Selection == null) why = "Wing Command is not ready";
            return why == null;
        }

        private static void Send()
        {
            BuildWho();
            int who = 0;
            if (pickedWho != null)
            {
                who = -1;
                for (int i = 0; i < whoCount; i++)
                    if (whoLabel[i] == pickedWho) { who = i; break; }
                if (who < 0)
                {
                    WingToast.Show(pickedWho + " is no longer in the wing: order not sent");
                    State.Reset();
                    pickedWho = null;
                    return;
                }
            }
            WingScope scope = whoScope[who];
            GridCell cell = CellAt(State.Do);
            if (!Ready(out string why))
            {
                WingToast.Show(why);
                return;
            }
            WingService w = WingService.Instance;
            string refusal = OrderGrid.Why(cell, true, w.Members.Count, scope.Kind != ScopeKind.Wing);
            if (refusal != null)
            {
                WingToast.Show(refusal);
                State.Reset();
                return;
            }
            if (cell.Input == GridInput.Now) RunNow(cell, scope);
            else if (State.Where >= 0 && State.Where < whereCount) RunWhere(cell, scope, where[State.Where]);
            State.Reset();
            pickedWho = null;
        }

        private static void RunWhere(GridCell cell, WingScope scope, WhereChoice at)
        {
            switch (at.Kind)
            {
                case WhereKind.Threat:
                    WingOrders.Run(new WingOrder { Kind = OrderKind.Attack, Units = new[] { at.Id }, Scope = scope });
                    break;
                case WhereKind.MyTarget:
                    WingCommands.AttackTarget(scope);
                    break;
                case WhereKind.Here:
                    if (cell.Order == GridOrder.Hold) WingCommands.HoldHere(scope);
                    else WingCommands.OrbitHere(scope);
                    break;
                default:
                    ArmMap(cell, scope);
                    break;
            }
        }

        /// <summary>MAP CLICK: opens the map when it is not, makes the ladder's WHO the WMC's selection and arms the order's map
        /// mode, so the next right-click places it as TACTICAL's grid would.</summary>
        private static void ArmMap(GridCell cell, WingScope scope)
        {
            WmcPanel panel = WmcPanel.Instance;
            if (panel == null || cell.Map == MapMode.Off)
            {
                WingToast.Show("Wing Command is not ready");
                return;
            }
            if (!DynamicMap.mapMaximized) panel.Open();
            panel.FillContext();
            WmcContext c = panel.Context;
            WingService w = WingService.Instance;
            switch (scope.Kind)
            {
                case ScopeKind.Element:
                    var ids = new List<uint>();
                    foreach (WingMember m in w.Members)
                        if (!m.Released && m.Alive && (object)m.Aircraft != null && w.ElementOf(m) == scope.Element) ids.Add(m.Aircraft.persistentID.Id);
                    c.Selection.SelectElement(scope.Element, ids);
                    break;
                case ScopeKind.Members:
                    c.Selection.SelectOnly(scope.Members[0]);
                    break;
                default:
                    c.Selection.Clear();
                    break;
            }
            c.Rescope();
            c.Map.Arm(c, cell.Map);
        }

        /// <summary>An order that needs nothing more, to <paramref name="scope"/> (the same calls as TACTICAL's grid).</summary>
        private static void RunNow(GridCell cell, WingScope scope)
        {
            if (!Ready(out string why))
            {
                WingToast.Show(why);
                return;
            }
            string refusal = OrderGrid.Why(cell, true, WingService.Instance.Members.Count, scope.Kind != ScopeKind.Wing);
            if (refusal != null)
            {
                WingToast.Show(refusal);
                return;
            }
            switch (cell.Order)
            {
                case GridOrder.Detach: WingToast.Show("Select the wingmen to detach"); break;
                case GridOrder.Dismiss:
                    if (!dismissGate.Press("dismiss", Time.unscaledTime))
                    {
                        WingToast.Show("Release every wingman to the game's AI? Do it again");
                        return;
                    }
                    WingCommands.Dismiss();
                    break;
                default: WingCommands.RunGrid(cell.Order, scope); break;
            }
        }

        // --------------------------------------------------------------------- the lines

        /// <summary>The ladder's text for the HUD: a header, the numbered choices, the back line and the readback (the newest ack
        /// chip). Returns how many of <paramref name="into"/> are used.</summary>
        public static int Lines(string[] into, float now)
        {
            int n = 0;
            if (!Open)
            {
                into[n++] = "WING KEY";
                for (int i = 0; i < StanceBook.Slots && n < into.Length - 3; i++)
                {
                    Stance s = WmcStanceActions.Book.Slot(i);
                    into[n++] = Key(i + 1) + (s != null ? s.Name : "-");
                }
                into[n++] = "KEY  ORDER     " + numberOpen + "ENTER</color>  LADDER";
                return Readback(into, n, now);
            }
            LadderStep step = State.Step;
            int total = Total(step);
            int per = PerPage(step, total);
            int page = State.Page, pages = LadderState.PageCount(total, per);
            into[n++] = Header(step, page, pages);
            int count = LadderState.OnPage(total, per, page);
            for (int i = 0; i < count && n < into.Length - 3; i++)
                into[n++] = Key(i + 1) + Choice(step, page * per + i);
            int more = LadderState.MoreDigit(total, per);
            if (more > 0) into[n++] = Key(more) + "MORE  " + (page + 1) + "/" + pages;
            into[n++] = Key(0) + (step == LadderStep.Who ? "CLOSE" : "BACK");
            return Readback(into, n, now);
        }

        private static int Readback(string[] into, int n, float now)
        {
            if (WingAcks.Feed.Chips(now, chips) > 0 && n < into.Length) into[n++] = chips[0].Chip();
            return n;
        }

        private static string Key(int digit) => numberOpen + digit + "</color>  ";

        private static string Header(LadderStep step, int page, int pages)
        {
            sb.Length = 0;
            sb.Append("WING");
            if (step == LadderStep.Who) return sb.Append(" > WHO").ToString();
            BuildWho();
            string who = State.Who >= 0 && State.Who < whoCount ? whoLabel[State.Who] : "WING";
            sb.Length = 0;
            sb.Append(who);
            if (step == LadderStep.Do) sb.Append(" > ").Append(OrderGrid.RowLabels[page]).Append(pages > 1 ? "  " + (page + 1) + "/" + pages : "");
            else sb.Append(" > ").Append(CellAt(State.Do).Label).Append(" > WHERE");
            return sb.ToString();
        }

        private static string Choice(LadderStep step, int index)
        {
            switch (step)
            {
                case LadderStep.Who: return index < whoCount ? whoLabel[index] : "";
                case LadderStep.Do:
                    GridCell cell = CellAt(index);
                    return cell.Label + (cell.Input != GridInput.Now ? " >" : "");
                default: return index < whereCount ? where[index].Label : "";
            }
        }
    }
}
