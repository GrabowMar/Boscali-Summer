using System;
using System.Collections.Generic;
using BoscaliSummer.Modules.Autopilot.Domain;

namespace BoscaliSummer.Tests.Features.Autopilot
{
    /// <summary>
    /// Drives a <see cref="MenuController"/> like the input source would, frame by frame, with a fake clock, and logs every Execute.
    /// Pure (Domain types only): the offline Unity check copies this file to put the menu in each gallery state.
    /// </summary>
    internal sealed class MenuRig
    {
        public readonly MenuController C;
        public readonly MenuModel Model;
        public MenuLayout L { get; private set; }
        public readonly List<string> Log = new List<string>();
        public readonly List<MenuCommandKind> Kinds = new List<MenuCommandKind>();
        public float Time = 5f;
        public float Mx, My;
        public bool CHeld, EnterHeld, SpaceHeld, LmbHeld, PadAHeld;
        public int DigitHeld = -1;
        public bool CanOpen = true;

        public MenuRig(MenuWorld world, int w = 1920, int h = 1080, IMenuMetrics metrics = null)
        {
            Model = MenuCatalog.Build(world);
            L = MenuLayout.Compute(w, h, Model.Categories, metrics ?? new MenuTestMetrics());
            C = new MenuController(Model, L);
            Mx = 6f; My = 6f;
        }

        public MenuRig(int w, int h) : this(MenuWorlds.Default(), w, h) { }

        private MenuInput Base()
        {
            MenuInput i = MenuInput.Idle(Time);
            i.CHeld = CHeld; i.CanOpen = CanOpen; i.PointerX = Mx; i.PointerY = My;
            i.EnterHeld = EnterHeld; i.SpaceHeld = SpaceHeld; i.LmbHeld = LmbHeld; i.PadAHeld = PadAHeld; i.DigitHeld = DigitHeld;
            return i;
        }

        public void Send(MenuInput i)
        {
            C.Step(i);
            foreach (MenuCommand cmd in C.Commands)
            {
                Kinds.Add(cmd.Kind);
                if (cmd.Kind == MenuCommandKind.Execute) Log.Add(cmd.Row.Id);
            }
        }

        public bool Ran(string id) => Log.Contains(id);

        public void Adv(float sec) { Time += sec; Send(Base()); }

        public void Mouse(float x, float y)
        {
            MenuInput i = Base();
            i.DeltaX = x - Mx; i.DeltaY = y - My; i.PointerX = x; i.PointerY = y; i.PointerMoved = true;
            Mx = x; My = y;
            Send(i);
        }

        public void CDown() { CHeld = true; Send(Base()); }
        public void CUp() { CHeld = false; Send(Base()); }

        /// <summary>Hold C at the hub for 200 ms: HOLD mode, pointer at the centre.</summary>
        public void HoldOpen() { Mouse(L.Cx, L.Cy); CDown(); Adv(0.2f); }

        /// <summary>Tap C.</summary>
        public void LatchOpen() { CDown(); Adv(0.1f); CUp(); }

        public void Digit(int n) { MenuInput i = Base(); i.Digit = n; i.DigitHeld = n; DigitHeld = n; Send(i); }
        public void DigitUp() { DigitHeld = -1; Send(Base()); }
        public void PressDigit(int n) { Digit(n); DigitUp(); }
        public void Step(int dx, int dy) { MenuInput i = Base(); i.Left = dx < 0; i.Right = dx > 0; i.Up = dy < 0; i.Down = dy > 0; Send(i); }
        public void Left() => Step(-1, 0);
        public void Right() => Step(1, 0);
        public void Up() => Step(0, -1);
        public void Down() => Step(0, 1);

        public void EnterDown() { EnterHeld = true; MenuInput i = Base(); i.Press = true; i.PressSource = PressSource.Enter; Send(i); }
        public void EnterUp() { EnterHeld = false; Send(Base()); }
        public void Enter() { EnterDown(); EnterUp(); }
        public void Backspace() { MenuInput i = Base(); i.Back = true; Send(i); }
        public void Wheel(int d) { MenuInput i = Base(); i.Wheel = d; Send(i); }
        public void Rmb() { MenuInput i = Base(); i.Rmb = true; Send(i); }
        public void Esc() { MenuInput i = Base(); i.Esc = true; Send(i); }
        public void LmbDown() { LmbHeld = true; MenuInput i = Base(); i.Lmb = true; i.PointerMoved = true; Send(i); }
        public void LmbUp() { LmbHeld = false; Send(Base()); }
        public void Click() { LmbDown(); LmbUp(); }
        public void Stick(float x, float y) { MenuInput i = Base(); i.StickX = x; i.StickY = y; i.FromPad = true; Send(i); }
        public void PadY(bool down) { CHeld = down; MenuInput i = Base(); i.FromPad = true; Send(i); }

        // ---- queries --------------------------------------------------------------------------------------------------
        public string Cat => C.Cat >= 0 ? Model.Categories[C.Cat].Id : "";
        public string Focused => C.FocusedRow == null ? "" : C.FocusedRow.Id;

        public MenuRect RowRect(string id)
        {
            foreach (DockRow r in C.Dock.Rows)
                if (r.Index >= 0 && r.Index < C.Items.Count && C.Items[r.Index].Id == id) return r.Rect;
            throw new InvalidOperationException("row " + id + " is not in the open dock");
        }

        public bool InDock(string id)
        {
            foreach (DockRow r in C.Dock.Rows) if (r.Index >= 0 && r.Index < C.Items.Count && C.Items[r.Index].Id == id) return true;
            return false;
        }

        public float[] At(string id, float fx = 0.7f)
        {
            MenuRect r = RowRect(id);
            return new[] { r.L + (r.R - r.L) * fx, (r.T + r.B) / 2f };
        }

        /// <summary>Scrolls the open list until the row is visible, then moves the pointer onto it.</summary>
        public void ToRow(string id, float fx = 0.7f)
        {
            for (int guard = 0; guard < 24 && !InDock(id); guard++) Wheel(1);
            float[] p = At(id, fx);
            Mouse(p[0], p[1]);
        }

        public float[] Polar(float angleDeg, float r) =>
            new[] { L.Cx + r * (float)Math.Sin(angleDeg * Math.PI / 180.0), L.Cy - r * (float)Math.Cos(angleDeg * Math.PI / 180.0) };

        public float[] DiscOf(int i) => new[] { L.DiscX[i], L.DiscY[i] };

        /// <summary>A hand path sampled every <paramref name="step"/> pixels, one frame (16 ms) per sample.</summary>
        public void Walk(IList<float[]> pts, float step)
        {
            float[] cur = pts[0];
            for (int k = 1; k < pts.Count; k++)
            {
                float[] to = pts[k];
                float dx = to[0] - cur[0], dy = to[1] - cur[1], d = (float)Math.Sqrt(dx * dx + dy * dy);
                int m = Math.Max(1, (int)Math.Ceiling(d / step));
                for (int j = 1; j <= m; j++) { Mouse(cur[0] + dx * j / m, cur[1] + dy * j / m); Adv(0.016f); }
                cur = to;
            }
        }
    }
}
