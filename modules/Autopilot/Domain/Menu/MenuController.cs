using System;
using System.Collections.Generic;

namespace BoscaliSummer.Modules.Autopilot.Domain
{
    internal struct MenuToast
    {
        public string Text, Icon, Context;
        public float Until;
    }

    internal sealed class MenuHold
    {
        public MenuRow Row;
        public PressSource Source;
        public int Digit;
        public float StartedAt;
        /// <summary>0..1: how much of the 0.8 s has passed.</summary>
        public float Progress;
    }

    /// <summary>
    /// The v7 input state machine, ported rule for rule from the prototype (app.js): HOLD flick with dead zone, angular hysteresis
    /// and the category lock; tap C to latch; digits that act on ONE level; hat; wheel; RMB / Backspace back one level; Esc;
    /// hold-to-confirm danger rows; the 80 ms rest rule on release; pointer ownership after a key. Pure: one <see cref="MenuInput"/> in,
    /// state + <see cref="Commands"/> out. The view reads the public state; the host runs Execute commands.
    /// </summary>
    internal sealed class MenuController
    {
        public const float HoldSec = 0.18f, TapSec = 0.4f, DangerSec = 0.8f, FlashSec = 0.15f, ToastSec = 1.2f, DenySec = 0.18f, ArmSec = 0.08f;
        public const float NearPx = 14f, SwitchRestSec = 0.12f, LockRestSec = 0.15f, EdgeRepeatSec = 0.14f, StickDeadzone = 0.25f, StickReach = 2.2f;

        private enum Owner : byte { None, Key, Pointer }
        private enum ExecSource : byte { Key, Release, Lmb, Hold }
        private enum HitType : byte { None, Category, Row, More, Dock }

        private struct Hit { public HitType Type; public int Index; public int Dir; }

        private readonly MenuModel model;
        private readonly DockLayout dock = new DockLayout();
        private MenuLayout layout;

        private bool cDown, prevC, moved, focusFromPointer;
        private float cTime, focusTime, lastMoveTime, edgeTime, flashUntil;
        private Owner owner;
        private float ownX, ownY;
        private int hover = -1;

        public MenuController(MenuModel model, MenuLayout layout)
        {
            this.model = model; this.layout = layout;
            Commands = new List<MenuCommand>(4);
        }

        // ---- state the view reads ------------------------------------------------------------------------------------
        public List<MenuCommand> Commands { get; }
        public bool Open { get; private set; }
        public MenuMode Mode { get; private set; }
        /// <summary>C went down less than 0.18 s ago and has not moved far: the mode is still being decided.</summary>
        public bool Pending { get; private set; }
        public int Cat { get; private set; } = -1;
        public MenuRow Sub { get; private set; }
        /// <summary>-1 nothing, -2 the BACK row, else the index in <see cref="Items"/>.</summary>
        public int Focus { get; private set; } = -1;
        public bool DockFocus { get; private set; }
        public int Offset { get; private set; }
        public float PtrX { get; private set; }
        public float PtrY { get; private set; }
        public bool Locked { get; private set; }
        public MenuDevice Device { get; private set; }
        public MenuHold Hold { get; private set; }
        public string FlashId { get; private set; }
        public MenuToast Toast;
        public float DenyUntil { get; private set; }
        public int Edge { get; private set; }
        /// <summary>The main dock's geometry for the current category / level; valid while <see cref="Cat"/> >= 0.</summary>
        public DockLayout Dock => dock;
        public MenuLayout Layout => layout;
        public MenuModel Model => model;
        public float Now { get; private set; }
        public bool FocusFromPointer => focusFromPointer;
        public int HoverCategory => hover;

        public bool Showing => Toast.Text != null && Now < Toast.Until;
        public bool Denying => Now < DenyUntil;
        public MenuCategory Category => Cat >= 0 ? model.Categories[Cat] : null;
        private bool HoldLike => Mode == MenuMode.Hold && (cDown || Pending);
        private bool VirtualPointer => Mode == MenuMode.Hold || Device == MenuDevice.Pad;

        public List<MenuRow> Items
        {
            get
            {
                MenuCategory c = Category;
                if (c == null) return Empty;
                return Sub != null ? Sub.Children : c.Rows;
            }
        }

        private static readonly List<MenuRow> Empty = new List<MenuRow>(0);

        public MenuRow FocusedRow
        {
            get
            {
                if (!Open || Cat < 0) return null;
                if (Focus == -2) return Sub != null ? MenuRow.BackRow : null;
                List<MenuRow> items = Items;
                return Focus >= 0 && Focus < items.Count ? items[Focus] : null;
            }
        }

        public int Cap => layout.Cap(Items, Sub != null);

        public void SetLayout(MenuLayout l) { layout = l; RefreshDock(); }

        /// <summary>Close from outside: focus lost, aircraft gone, map opened.</summary>
        public void Cancel() { if (Open) CloseMenu(); }

        // ---- the frame ---------------------------------------------------------------------------------------------------
        public void Step(in MenuInput inp)
        {
            Now = inp.Time;
            Commands.Clear();
            if (Open && inp.Esc) CloseMenu();

            bool down = inp.CHeld && !prevC, up = !inp.CHeld && prevC;
            prevC = inp.CHeld;
            if (down) { Device = inp.FromPad ? MenuDevice.Pad : MenuDevice.Key; KeyC(true, inp); }
            else if (up) { if (inp.FromPad) Device = MenuDevice.Pad; KeyC(false, inp); }

            if (Open)
            {
                if (inp.Digit >= 0) { SetDevice(inp); PressNumber(inp.Digit); }
                if (inp.Left) { SetDevice(inp); Rotate(-1); }
                if (inp.Right) { SetDevice(inp); Rotate(1); }
                if (inp.Up) { SetDevice(inp); MoveFocus(-1); }
                if (inp.Down) { SetDevice(inp); MoveFocus(1); }
                if (inp.Press) { SetDevice(inp); HatPress(inp.PressSource, inp.Digit); }
                if (inp.Back) { SetDevice(inp); Back(false); }
                if (inp.Wheel != 0) Wheel(inp.Wheel);
                if (inp.Rmb) { Device = MenuDevice.Mouse; Back(true); }
                if (inp.DeltaX != 0f || inp.DeltaY != 0f || inp.PointerMoved) PointerMove(inp);
                if (inp.Lmb) Click(inp);
                if (Math.Abs(inp.StickX) > StickDeadzone || Math.Abs(inp.StickY) > StickDeadzone) Stick(inp);
                CheckHoldSource(inp);
            }
            Update(inp.Time);
        }

        private void SetDevice(in MenuInput inp) => Device = inp.FromPad ? MenuDevice.Pad : MenuDevice.Key;

        // ---- open / close ------------------------------------------------------------------------------------------------
        private void OpenMenu(MenuMode mode)
        {
            Open = true; Mode = mode; Pending = false; Cat = -1; Sub = null; Focus = -1; DockFocus = false; Offset = 0;
            Hold = null; owner = Owner.None; moved = false; hover = -1; Edge = 0; DenyUntil = 0f; Locked = false;
            Toast = default;
            Recentre();
        }

        private void CloseMenu()
        {
            bool was = Open;
            Open = false; Pending = false; cDown = false; Cat = -1; Sub = null; Focus = -1; DockFocus = false; Offset = 0;
            Hold = null; Locked = false; hover = -1; Edge = 0;
            if (was) Commands.Add(new MenuCommand(MenuCommandKind.Close));
        }

        private void Recentre()
        {
            PtrX = layout.Cx; PtrY = layout.Cy; Locked = false;
            if (owner == Owner.Key) { ownX = PtrX; ownY = PtrY; }
        }

        private void KeyOwns() { owner = Owner.Key; ownX = PtrX; ownY = PtrY; }

        private bool PointerOwns()
        {
            if (owner != Owner.Key) return true;
            float dx = PtrX - ownX, dy = PtrY - ownY;
            if (Math.Sqrt(dx * dx + dy * dy) < NearPx) return false;
            owner = Owner.Pointer;
            return true;
        }

        private void KeyC(bool isDown, in MenuInput inp)
        {
            if (isDown)
            {
                if (!Open)
                {
                    if (!inp.CanOpen) return;
                    OpenMenu(MenuMode.Hold);
                    cDown = true; Pending = true; cTime = Now;
                    return;
                }
                CloseMenu();
                return;
            }
            if (!cDown) return;
            cDown = false;
            if (!Open) return;
            if (Pending) { Pending = false; Latch(inp); return; }
            if (Mode == MenuMode.Hold)
            {
                // a tap travelled nowhere and was quick; anything that moved is a hold
                if (!moved && Cat < 0 && Now - cTime < TapSec) Latch(inp); else ReleaseExec();
            }
        }

        private void Latch(in MenuInput inp)
        {
            Mode = MenuMode.Latched; Locked = false;
            LatchCursor(inp);
            LatchedHover();
            RefreshDock();
        }

        private void LatchCursor(in MenuInput inp)
        {
            if (inp.PointerX >= 0f && inp.PointerY >= 0f && inp.PointerX <= layout.W && inp.PointerY <= layout.H) { PtrX = inp.PointerX; PtrY = inp.PointerY; }
            else { PtrX = layout.Cx; PtrY = layout.Cy; }
        }

        // ---- focus, category, scroll -------------------------------------------------------------------------------------
        private void SetFocus(int i, bool fromPointer)
        {
            if (i != Focus) focusTime = Now;
            Focus = i; focusFromPointer = fromPointer;
        }

        private void SetCat(int i)
        {
            if (i == Cat) return;
            Cat = i; Sub = null; Focus = -1; DockFocus = false; Offset = 0; Hold = null; Edge = 0; hover = -1;
            RefreshDock();
        }

        private void RefreshDock()
        {
            if (!Open || Cat < 0) return;
            MenuCategory c = Category;
            bool empty = !c.Available;
            bool footer = (!empty && Device != MenuDevice.Mouse) || Sub == null;
            layout.FillDock(dock, Items, Sub != null, Offset, footer, MenuLayout.DockSide(c), empty);
            Offset = dock.Offset;
        }

        private void ClampOffset()
        {
            int n = Items.Count, cap = Cap;
            Offset = Math.Max(0, Math.Min(Offset, Math.Max(0, n - cap)));
        }

        private void EnsureVisible()
        {
            ClampOffset();
            int cap = Cap;
            if (Focus >= 0)
            {
                if (Focus < Offset) Offset = Focus;
                else if (Focus >= Offset + cap) Offset = Focus - cap + 1;
            }
            ClampOffset();
            RefreshDock();
        }

        private bool CanScroll(int d) => d < 0 ? Offset > 0 : Offset + Cap < Items.Count;

        private bool ScrollBy(int d)
        {
            int n = Items.Count, cap = Cap;
            if (n <= cap) return false;
            int o = Math.Max(0, Math.Min(Offset + d, n - cap));
            if (o != Offset)
            {
                Offset = o;
                if (Focus >= 0)
                {
                    if (Focus < o) SetFocus(o, focusFromPointer);
                    else if (Focus >= o + cap) SetFocus(o + cap - 1, focusFromPointer);
                }
                RefreshDock();
            }
            return true;
        }

        // ---- back / drill ------------------------------------------------------------------------------------------------
        private void Back(bool fromPointer)
        {
            if (!fromPointer) KeyOwns();
            Hold = null;
            if (Sub != null)
            {
                MenuRow parent = Sub;
                Sub = null;
                SetFocus(Math.Max(0, Category.Rows.IndexOf(parent)), false);
                DockFocus = true; Offset = 0;
                RefreshDock(); EnsureVisible();
                Commands.Add(new MenuCommand(MenuCommandKind.Back));
            }
            else if (Cat >= 0)
            {
                Cat = -1; Focus = -1; DockFocus = false; Offset = 0; Edge = 0; hover = -1;
                if (Mode == MenuMode.Hold) Recentre();
                Commands.Add(new MenuCommand(MenuCommandKind.Back));
            }
            else CloseMenu();
        }

        private void Drill(MenuRow row)
        {
            if (row.Children == null) return;
            Hold = null; Sub = row; DockFocus = true; Offset = 0; Edge = 0;
            int sel = row.Children.FindIndex(c => c.Selected);
            SetFocus(sel >= 0 ? sel : 0, false);
            RefreshDock(); EnsureVisible();
        }

        // ---- executing ---------------------------------------------------------------------------------------------------
        private void Exec(MenuRow row, ExecSource src)
        {
            if (row == MenuRow.BackRow)
            {
                Back(src == ExecSource.Release || src == ExecSource.Lmb);
                if (Mode == MenuMode.Hold && src == ExecSource.Release && Open) { Mode = MenuMode.Latched; cDown = false; Latch(default(MenuInput).WithPointer(PtrX, PtrY)); }
                return;
            }
            if (row.Kind == MenuKind.Submenu)
            {
                Drill(row);
                if (Mode == MenuMode.Hold && src == ExecSource.Release) { Mode = MenuMode.Latched; cDown = false; Latch(default(MenuInput).WithPointer(PtrX, PtrY)); }
                return;
            }
            if (row.IsBlocked) { Deny(); return; }
            string text = row.ToastText();
            string icon = row.IconNow ?? row.Icon;
            string context = Category != null ? Category.Label : "";
            Commands.Add(new MenuCommand(MenuCommandKind.Execute, row));
            if (Mode == MenuMode.Latched && row.Stays)
            {
                FlashId = row.Id; flashUntil = Now + FlashSec;
                return;
            }
            CloseMenu();
            Toast = new MenuToast { Text = text, Icon = icon, Context = context, Until = Now + ToastSec };
        }

        private void StartSelect(MenuRow row, PressSource source, int digit)
        {
            if (row.IsBlocked) { Deny(); return; }
            if (row.NeedsHold) { Hold = new MenuHold { Row = row, Source = source, Digit = digit, StartedAt = Now }; return; }
            Exec(row, source == PressSource.Lmb ? ExecSource.Lmb : ExecSource.Key);
        }

        private void Deny() { DenyUntil = Now + DenySec; Commands.Add(new MenuCommand(MenuCommandKind.Pulse)); }

        /// <summary>Release C runs the focused row only if the pointer put it there and it has rested there for 80 ms.</summary>
        private void ReleaseExec()
        {
            MenuRow row = FocusedRow;
            if (row == null || row.Kind == MenuKind.Danger || row.NeedsHold || row.IsBlocked || !focusFromPointer || Now - focusTime < ArmSec) { CloseMenu(); return; }
            Exec(row, ExecSource.Release);
        }

        // ---- keys: ring, list, digits --------------------------------------------------------------------------------------
        private void Rotate(int dir)
        {
            KeyOwns(); Hold = null;
            int n = model.Categories.Count, i = Cat < 0 ? (dir > 0 ? -1 : 0) : Cat;
            for (int k = 0; k < n; k++)
            {
                i = (i + dir + n) % n;
                if (model.Categories[i].Available) break;
            }
            if (i == Cat) { DockFocus = false; Focus = -1; }
            SetCat(i);
        }

        private void MoveFocus(int dir)
        {
            if (Cat < 0) return;
            KeyOwns();
            int n = Items.Count;
            if (n == 0) return;
            if (!DockFocus)
            {
                if (dir < 0) return;
                SetFocus(Offset, false); DockFocus = true; EnsureVisible();
                return;
            }
            int f = Focus;
            if (dir > 0) f = f == -2 ? 0 : (f < 0 ? Offset : Math.Min(n - 1, f + 1));
            else f = f == 0 ? (Sub != null ? -2 : 0) : (f > 0 ? f - 1 : f);
            SetFocus(f, false); EnsureVisible();
        }

        private void HatPress(PressSource source, int digit)
        {
            if (Cat < 0) return;
            KeyOwns();
            if (!DockFocus)
            {
                if (Items.Count > 0) { SetFocus(Offset, false); DockFocus = true; EnsureVisible(); }
                return;
            }
            MenuRow row = FocusedRow;
            if (row != null) StartSelect(row, source, digit);
        }

        private void PressNumber(int n)
        {
            KeyOwns(); Hold = null;
            MenuCategory c = Category;
            bool dockUp = Open && c != null && c.Available && Items.Count > 0;
            if (dockUp)
            {
                if (n == 0) { Back(false); return; }
                List<MenuRow> list = Items;
                ClampOffset();
                int i = Offset + n - 1;
                if (n > Cap || i >= list.Count) { Deny(); return; }
                DockFocus = true; SetFocus(i, false); RefreshDock();
                StartSelect(list[i], PressSource.Digit, n);
            }
            else SetCat(n == 0 ? 9 : n - 1);
        }

        private void Wheel(int d)
        {
            if (ScrollBy(d))
            {
                if (owner != Owner.Key && Cat >= 0 && layout.PointerDock(dock).Contains(PtrX, PtrY)) FocusFromPtr(PtrX, PtrY);
            }
            else { Device = MenuDevice.Key; MoveFocus(d); }
        }

        // ---- pointer ----------------------------------------------------------------------------------------------------------
        private void PointerMove(in MenuInput inp)
        {
            if (HoldLike)
            {
                PtrX = Math.Max(6f, Math.Min(layout.W - 6f, PtrX + inp.DeltaX));
                PtrY = Math.Max(6f, Math.Min(layout.H - 6f, PtrY + inp.DeltaY));
            }
            else { PtrX = inp.PointerX; PtrY = inp.PointerY; }
            if (!PointerOwns()) return;
            lastMoveTime = Now; Device = MenuDevice.Mouse;
            if (HoldLike) HoldPointerLogic(); else LatchedHover();
        }

        private void Stick(in MenuInput inp)
        {
            Device = MenuDevice.Pad; owner = Owner.Pointer; lastMoveTime = Now; moved = true;
            PtrX = Math.Max(6f, Math.Min(layout.W - 6f, layout.Cx + inp.StickX * StickReach * layout.R));
            PtrY = Math.Max(6f, Math.Min(layout.H - 6f, layout.Cy + inp.StickY * StickReach * layout.R));
            HoldPointerLogic();
        }

        private void HoldPointerLogic()
        {
            float dx = PtrX - layout.Cx, dy = PtrY - layout.Cy, r = (float)Math.Sqrt(dx * dx + dy * dy);
            bool inDock = Cat >= 0 && layout.PointerDock(dock).Contains(PtrX, PtrY);
            if (r > MenuLayout.DeadPx * layout.Rho) moved = true;
            if (inDock) { hover = -1; FocusFromPtr(PtrX, PtrY); return; }
            Edge = 0;
            if (DockFocus) { DockFocus = false; SetFocus(-1, true); }
            if (r < MenuLayout.DeadPx * layout.Rho)
            {
                Locked = false;
                if (Cat >= 0) { Cat = -1; Sub = null; Focus = -1; Offset = 0; Hold = null; }
                return;
            }
            if (Locked)
            {
                if (r > MenuLayout.LockOut * layout.R) return;
                Locked = false;
            }
            int i = layout.SectorPick(MenuLayout.AngleOf(dx, dy), Cat);
            if (i != Cat) SetCat(i);
            if (r >= MenuLayout.LockIn * layout.R) Locked = true;
        }

        private Hit HitAt(float x, float y)
        {
            if (Open && Cat >= 0 && dock.Overflow)
            {
                if (dock.ChipUp.Contains(x, y)) return new Hit { Type = HitType.More, Dir = -1 };
                if (dock.ChipDown.Contains(x, y)) return new Hit { Type = HitType.More, Dir = 1 };
            }
            if (Open && Cat >= 0 && layout.PointerDock(dock).Contains(x, y))
            {
                foreach (DockRow row in dock.Rows)
                    if (y >= row.Rect.T && y < row.Rect.B) return new Hit { Type = HitType.Row, Index = row.Index };
                return new Hit { Type = HitType.Dock };
            }
            int cat = layout.CategoryAt(x, y);
            return cat >= 0 ? new Hit { Type = HitType.Category, Index = cat } : default;
        }

        /// <summary>Focus from the pointer inside the dock: rows by Y; header, footer, rule and group headings clear it unless the focused row is within 10 px.</summary>
        private void FocusFromPtr(float x, float y)
        {
            Edge = 0;
            if (dock.Overflow)
            {
                int dir = dock.ChipUp.Contains(x, y) ? -1 : dock.ChipDown.Contains(x, y) ? 1 : 0;
                if (dir != 0) { Edge = dir; DockFocus = true; SetFocus(-1, true); return; }
            }
            float band = 14f * layout.Lg;
            foreach (DockRow r in dock.Rows)
            {
                if (y < r.Rect.T || y >= r.Rect.B) continue;
                DockFocus = true;
                if (r.Index == Offset && Offset > 0 && y < r.Rect.T + band) { Edge = -1; SetFocus(-1, true); return; }
                if (r.Index >= 0 && r.Index == Offset + dock.Cap - 1 && Offset + dock.Cap < Items.Count && y >= r.Rect.B - band) { Edge = 1; SetFocus(-1, true); return; }
                SetFocus(r.Index, true);
                return;
            }
            DockFocus = true;
            bool kept = false;
            foreach (DockRow r in dock.Rows)
                if (r.Index == Focus) { kept = y >= r.Rect.T - 10f && y < r.Rect.B + 10f; break; }
            if (!kept) SetFocus(-1, true);
        }

        /// <summary>LATCHED hover: the first peek and clicks are instant; once a category is open, resting 120 ms on another one switches (passing over it does not).</summary>
        private void LatchedHover()
        {
            Hit h = HitAt(PtrX, PtrY);
            Edge = 0; hover = -1;
            if (h.Type == HitType.Category)
            {
                if (DockFocus) { DockFocus = false; SetFocus(-1, true); }
                if (Cat < 0) SetCat(h.Index); else if (h.Index != Cat) hover = h.Index;
            }
            else if (h.Type == HitType.Row || h.Type == HitType.More || h.Type == HitType.Dock) FocusFromPtr(PtrX, PtrY);
            else if (DockFocus) { DockFocus = false; SetFocus(-1, true); }
        }

        private void Click(in MenuInput inp)
        {
            Device = MenuDevice.Mouse; owner = Owner.None;
            if (!HoldLike) { PtrX = inp.PointerX; PtrY = inp.PointerY; }
            if (HoldLike)
            {
                MenuRow focused = FocusedRow;
                if (focused != null) StartSelect(focused, PressSource.Lmb, -1);
                return;
            }
            Hit h = HitAt(PtrX, PtrY);
            if (h.Type == HitType.Row)
            {
                DockFocus = true; SetFocus(h.Index, true);
                MenuRow row = h.Index == -2 ? MenuRow.BackRow : (h.Index >= 0 && h.Index < Items.Count ? Items[h.Index] : null);
                if (row != null) StartSelect(row, PressSource.Lmb, -1);
            }
            else if (h.Type == HitType.Category) SetCat(h.Index);
            else if (h.Type == HitType.More) ScrollBy(h.Dir * (Cap - 1));
        }

        // ---- time ---------------------------------------------------------------------------------------------------------------
        private void CheckHoldSource(in MenuInput inp)
        {
            if (Hold == null) return;
            bool held;
            switch (Hold.Source)
            {
                case PressSource.Lmb: held = inp.LmbHeld; break;
                case PressSource.Enter: held = inp.EnterHeld; break;
                case PressSource.Space: held = inp.SpaceHeld; break;
                case PressSource.Pad: held = inp.PadAHeld; break;
                case PressSource.Digit: held = inp.DigitHeld == Hold.Digit; break;
                default: held = true; break;
            }
            // a hold started this very frame may not have its level reported yet
            if (!held && Now > Hold.StartedAt) Hold = null;
        }

        private void Update(float t)
        {
            if (Open)
            {
                // the danger hold dies the moment focus leaves its row (arrows, hat, wheel, drag, rotate, back) or the menu closes
                if (Hold != null && FocusedRow != Hold.Row) Hold = null;
                if (Pending && cDown && t - cTime >= HoldSec) Pending = false;
                if (Hold != null)
                {
                    float p = (t - Hold.StartedAt) / DangerSec;
                    Hold.Progress = Math.Max(0f, Math.Min(1f, p));
                    if (p >= 1f) { MenuRow row = Hold.Row; Hold = null; Exec(row, ExecSource.Hold); }
                }
                // HOLD: resting on another disc while committed to a category side-steps to it
                if (Open && Mode == MenuMode.Hold && Locked && !DockFocus && t - lastMoveTime >= LockRestSec)
                {
                    int j = layout.DiscAt(PtrX, PtrY, 4f);
                    if (j >= 0 && j != Cat) SetCat(j);
                }
                // LATCHED: resting 120 ms on another category switches to it
                if (Open && hover >= 0 && t - lastMoveTime >= SwitchRestSec)
                {
                    int hv = hover; hover = -1;
                    if (hv != Cat) SetCat(hv);
                }
                // resting on a chip or on the first / last row's edge band keeps scrolling
                if (Open && Edge != 0 && t - edgeTime >= EdgeRepeatSec)
                {
                    edgeTime = t;
                    if (CanScroll(Edge)) ScrollBy(Edge); else Edge = 0;
                }
            }
            if (FlashId != null && t >= flashUntil) FlashId = null;
            if (Toast.Text != null && t >= Toast.Until) Toast = default;
        }
    }

    internal static class MenuInputExtensions
    {
        internal static MenuInput WithPointer(this MenuInput i, float x, float y) { i.PointerX = x; i.PointerY = y; return i; }
    }
}
