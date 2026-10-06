using NOAvionics;
using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

using BoscaliSummer.Modules.Wing.Domain;
namespace BoscaliSummer.Modules.Wing.Presentation
{
    /// <summary>A bezel tab (kit v2, phase B): built once into its console page flow, refreshed at the panel's rate while it shows.
    /// The panel hands each tab its <see cref="AvConsole.Page"/> flow, the console's <see cref="AvTicker"/> (register per-frame or
    /// slow work with <c>ticker.Add(pageIndex, rate, action)</c>) and its page index. A tab that changes the height of anything in its
    /// flow calls <see cref="AvFlow.RequestRelayout"/> (cheap; the flow relayouts on the next fast tick).</summary>
    internal interface IWmcPage
    {
        void Build(AvFlow flow, AvTicker ticker, int pageIndex);

        void Refresh(WmcContext c);

        /// <summary>The footer's ambient line while the page shows.</summary>
        string Hint { get; }

        /// <summary>The footer's alert line, or null.</summary>
        string Alert { get; }

        /// <summary>The page came on screen (its tab was picked, or the panel opened on it): per-show work, before the first
        /// refresh.</summary>
        void Shown(WmcContext c);
    }

    /// <summary>The WMC's state classes (live, warn, armed, danger, info, inert, locked, ready) as kit v2 states and colours.</summary>
    internal static class WmcState
    {
        public static AvState Of(string cls)
        {
            switch (cls)
            {
                case "danger": return AvState.Danger;
                case "warn":
                case "armed":
                case "caution": return AvState.Caution;
                case "live":
                case "ready": return AvState.Ready;
                case "info": return AvState.Info;
                default: return AvState.Inert;
            }
        }

        public static Color Color(string cls) => WmcDraw.RailColor(cls);
    }

    /// <summary>The WMC's hand-drawn <see cref="Glyph"/>s as the nearest kit v2 <see cref="AvIcon"/> (the only icons that exist).</summary>
    internal static class WmcIcons
    {
        public static AvIcon Of(Glyph g)
        {
            switch (g)
            {
                case Glyph.Attack: return AvIcon.Target;
                case Glyph.MyTarget: return AvIcon.Focus2;
                case Glyph.Splash: return AvIcon.Flame;
                case Glyph.Engage: return AvIcon.Bolt;
                case Glyph.Disengage: return AvIcon.ArrowBackUp;
                case Glyph.ClearSix: return AvIcon.Eye;
                case Glyph.Ecm: return AvIcon.Antenna;
                case Glyph.FormUp: return AvIcon.UsersGroup;
                case Glyph.Move: return AvIcon.ArrowUpRight;
                case Glyph.Orbit: return AvIcon.CurrentLocation;
                case Glyph.Hold: return AvIcon.PlayerPause;
                case Glyph.Patrol: return AvIcon.ChartArrows;
                case Glyph.Cap: return AvIcon.Shield;
                case Glyph.Sweep: return AvIcon.Radar2;
                case Glyph.Scout: return AvIcon.Map2;
                case Glyph.Escort: return AvIcon.ShieldLock;
                case Glyph.Rtb: return AvIcon.ArrowLeft;
                case Glyph.Refit: return AvIcon.Gauge;
                case Glyph.Land: return AvIcon.ArrowDown;
                case Glyph.Cargo: return AvIcon.Stack2;
                case Glyph.TakeOff: return AvIcon.ArrowUp;
                case Glyph.Rescue: return AvIcon.Heart;
                case Glyph.Detach: return AvIcon.Unlink;
                case Glyph.Call: return AvIcon.Plus;
                case Glyph.Bogey: return AvIcon.Radio;
                case Glyph.Dismiss: return AvIcon.X;
                case Glyph.React: return AvIcon.Bolt;
                case Glyph.Missile: return AvIcon.AlertTriangle;
                case Glyph.Warn: return AvIcon.AlertCircle;
                case Glyph.Fuel: return AvIcon.Gauge;
                case Glyph.Ammo: return AvIcon.Database;
                case Glyph.Lost: return AvIcon.Skull;
                case Glyph.Behind: return AvIcon.Eye;
                case Glyph.Plane: return AvIcon.Plane;
                case Glyph.Threat: return AvIcon.Radar2;
                case Glyph.Pool: return AvIcon.Stack2;
                case Glyph.Formation: return AvIcon.LayersSubtract;
                case Glyph.Route: return AvIcon.MapPin;
                case Glyph.Pilot: return AvIcon.User;
                case Glyph.Gear: return AvIcon.Settings;
                case Glyph.Base: return AvIcon.BuildingBank;
                case Glyph.Pylon: return AvIcon.Stack2;
                case Glyph.Check: return AvIcon.CircleCheck;
                case Glyph.Info: return AvIcon.InfoCircle;
                case Glyph.Record: return AvIcon.Circle;
                case Glyph.Plan: return AvIcon.ListDetails;
                case Glyph.Tuning: return AvIcon.AdjustmentsHorizontal;
                case Glyph.Posture: return AvIcon.Shield;
                case Glyph.Clear: return AvIcon.CircleCheck;
                default: return AvIcon.None;
            }
        }
    }

    /// <summary>Several sub-pages that share one flow line (TACTICAL's ORDERS · FORMATION · ROUTE·AP): each has a nested flow that
    /// lays out exactly as a page's; only the current one takes space, so the hidden ones cost no height.</summary>
    internal sealed class WmcSubPages : AvPart
    {
        private readonly AvFlow outer;
        private readonly AvFlow[] flows;
        private readonly RectTransform[] rects;
        private int current = -1;
        private float seen = -1f;

        public WmcSubPages(AvFlow outerFlow, AvTicker ticker, int pageIndex, int count)
        {
            outer = outerFlow;
            Rect = AvLay.Child(outerFlow.Content, "SubPages");
            flows = new AvFlow[count];
            rects = new RectTransform[count];
            for (int i = 0; i < count; i++)
            {
                rects[i] = AvLay.Child(Rect, "Sub" + i);
                flows[i] = new AvFlow(rects[i], ticker, outerFlow.Width);
                rects[i].gameObject.SetActive(false);
            }
            ticker?.Add(pageIndex, AvTickRate.Fast, Watch);
        }

        public int Current => current;

        public AvFlow Flow(int i) => flows[i];

        public void Show(int i)
        {
            if (i < 0 || i >= flows.Length) return;
            current = i;
            for (int k = 0; k < rects.Length; k++) rects[k].gameObject.SetActive(k == i);
            seen = -1f;
            outer.RequestRelayout();
        }

        public override float Measure(float width)
        {
            if (current < 0) return 0f;
            flows[current].Relayout();
            return Mathf.Max(0f, flows[current].ContentHeight - 2f * AvGridTokens.Pad);
        }

        public override void Place(AvSlot slot)
        {
            float h = slot.H + 2f * AvGridTokens.Pad;
            AvLay.Place(Rect, 0f, slot.Y - AvGridTokens.Pad, outer.Width, h);
            for (int i = 0; i < rects.Length; i++) AvLay.Place(rects[i], 0f, 0f, outer.Width, h);
        }

        /// <summary>The current sub-page grew or shrank on its own (a list changed): the page's flow follows.</summary>
        private void Watch()
        {
            if (current < 0) return;
            float h = flows[current].ContentHeight;
            if (Mathf.Abs(h - seen) < 0.5f) return;
            seen = h;
            outer.RequestRelayout();
        }
    }

    /// <summary>A grid of buttons of which only the first <see cref="Shown"/> take space (FORMATION's families and shapes: their
    /// number follows the wing). Labels, latches and help are set on <see cref="this[int]"/>.</summary>
    internal sealed class WmcButtonGrid : AvPart
    {
        private readonly AvControl[] cells;
        private readonly int columns;
        private int shown;

        public WmcButtonGrid(RectTransform parent, int columnCount, int count, Func<int, AvControl.Spec> spec)
        {
            Rect = AvLay.Child(parent, "ButtonGrid");
            columns = Mathf.Max(1, columnCount);
            cells = new AvControl[count];
            for (int i = 0; i < count; i++)
            {
                cells[i] = AvControl.Make(Rect, spec(i));
                cells[i].gameObject.SetActive(false);
            }
        }

        public AvControl this[int i] => cells[i];

        public int Count => cells.Length;

        /// <summary>The first <paramref name="n"/> cells show. Returns true when that changed (the caller relayouts).</summary>
        public bool SetShownCount(int n)
        {
            n = Mathf.Clamp(n, 0, cells.Length);
            if (n == shown) return false;
            shown = n;
            for (int i = 0; i < cells.Length; i++)
                if (cells[i].gameObject.activeSelf != (i < n)) cells[i].gameObject.SetActive(i < n);
            return true;
        }

        public override float Measure(float width)
        {
            if (shown == 0) return 0f;
            float w = AvFlowMath.ColumnWidth(width, columns, AvGridTokens.Gap), lineH = AvGridTokens.Row;
            for (int i = 0; i < shown; i++) lineH = Mathf.Max(lineH, cells[i].PreferredHeight(w));
            int rows = (shown + columns - 1) / columns;
            return rows * Mathf.Ceil(lineH) + (rows - 1) * AvGridTokens.Gap;
        }

        public override void Place(AvSlot slot)
        {
            base.Place(slot);
            if (shown == 0) return;
            float w = AvFlowMath.ColumnWidth(slot.W, columns, AvGridTokens.Gap);
            int rows = (shown + columns - 1) / columns;
            float lineH = (slot.H - (rows - 1) * AvGridTokens.Gap) / rows;
            for (int i = 0; i < shown; i++)
                AvLay.Place(cells[i].Rect, i % columns * (w + AvGridTokens.Gap), i / columns * (lineH + AvGridTokens.Gap), w, lineH);
        }

        public override void Restyle()
        {
            foreach (AvControl c in cells) c.Restyle();
        }
    }

    /// <summary>A key with a state rail on the left and a row of buttons (TACTICAL's order grid: OFFENSE · DEFENSE · … each with its
    /// four orders). <see cref="AvButtons"/> has no key column; this is the same equal-width row with one.</summary>
    internal sealed class WmcLabeledButtons : AvPart
    {
        private const float KeyW = 76f;
        private readonly TMP_Text key;
        private readonly Image rail;
        private readonly AvControl[] controls;
        private AvState state;

        public WmcLabeledButtons(RectTransform parent, string keyText, AvState railState, AvControl.Spec[] specs)
        {
            Rect = AvLay.Child(parent, "Buttons " + keyText);
            state = railState;
            rail = AvLay.Solid(Rect, "Rail", Color.clear);
            key = AvText.Make(Rect, "Key", AvTextRole.Label, keyText, TextAlignmentOptions.MidlineLeft);
            AvText.Fit(key, false);
            controls = new AvControl[specs.Length];
            for (int i = 0; i < specs.Length; i++) controls[i] = AvControl.Make(Rect, specs[i]);
            Restyle();
        }

        public AvControl[] Controls => controls;

        public override float Measure(float width)
        {
            float w = AvFlowMath.ColumnWidth(width - KeyW, controls.Length, AvGridTokens.Gap / 2f), h = AvGridTokens.Row;
            foreach (AvControl c in controls) h = Mathf.Max(h, c.PreferredHeight(w));
            return Mathf.Ceil(h);
        }

        public override void Place(AvSlot slot)
        {
            base.Place(slot);
            float gap = AvGridTokens.Gap / 2f, w = AvFlowMath.ColumnWidth(slot.W - KeyW, controls.Length, gap);
            AvLay.Place(rail.rectTransform, 0f, 0f, 2f, slot.H);
            AvLay.Place(key.rectTransform, 8f, 0f, KeyW - 10f, slot.H);
            for (int i = 0; i < controls.Length; i++) AvLay.Place(controls[i].Rect, KeyW + i * (w + gap), 0f, w, slot.H);
        }

        public override void Restyle()
        {
            rail.color = WmcState.Color(AvStates.Class(state));
            key.color = AvStyleHost.Resolve(AvStyleHost.FuiStyle("row-sub").Color, AvTheme.Dim);
            foreach (AvControl c in controls) c.Restyle();
        }
    }

    /// <summary>A few wrapped lines of text (FLIGHT POOL's stations, a note under a control): kit v2 type roles, no ellipsis; an
    /// empty line takes no room.</summary>
    internal sealed class WmcLines : AvPart
    {
        private readonly TMP_Text[] lines;
        private readonly AvTextRole role;
        private readonly string colorClass;

        public WmcLines(RectTransform parent, int count, AvTextRole textRole = AvTextRole.ProseSmall, string cssClass = "row-sub")
        {
            Rect = AvLay.Child(parent, "Lines");
            role = textRole;
            colorClass = cssClass;
            lines = new TMP_Text[count];
            for (int i = 0; i < count; i++) lines[i] = AvText.Make(Rect, "Line" + i, role, "", TextAlignmentOptions.TopLeft, true);
            Restyle();
        }

        public int Count => lines.Length;

        /// <summary>Sets line <paramref name="i"/>; true when the text changed (the caller relayouts).</summary>
        public bool Set(int i, string text)
        {
            if (i < 0 || i >= lines.Length) return false;
            text = text ?? "";
            if (lines[i].text == text) return false;
            lines[i].text = text;
            return true;
        }

        public override float Measure(float width)
        {
            float h = 0f;
            foreach (TMP_Text t in lines)
                if (t.text.Length > 0) h += AvText.Height(t, width) + 2f;
            return Mathf.Max(0f, h - 2f);
        }

        public override void Place(AvSlot slot)
        {
            base.Place(slot);
            float y = 0f;
            foreach (TMP_Text t in lines)
            {
                if (t.text.Length == 0) continue;
                float h = AvText.Height(t, slot.W);
                AvLay.Place(t.rectTransform, 0f, y, slot.W, h);
                y += h + 2f;
            }
        }

        public override void Restyle()
        {
            Color c = AvStyleHost.Resolve(AvStyleHost.FuiStyle(colorClass).Color, AvTheme.Dim);
            foreach (TMP_Text t in lines) t.color = c;
        }
    }

    /// <summary>Where a popup for a control goes inside a tab's page (the popup is a child of the page content, which scrolls in the
    /// console body): below the control when it fits the visible body, else above, else pinned inside the visible body.</summary>
    internal static class WmcPopup
    {
        /// <summary>The area in <see cref="AvPopup"/> terms (top-left, negative y down) inside <paramref name="content"/>;
        /// <paramref name="width"/> 0 keeps the control's width.</summary>
        public static Rect Area(RectTransform content, RectTransform anchor, int entries, float width = 0f)
        {
            Rect r = WmcKit.RectIn(content, anchor);
            float popupH = BezelLayout.PopupHeight(entries);
            var viewport = content.parent as RectTransform;
            float view = viewport != null ? viewport.rect.height : content.rect.height;
            float scrolled = Mathf.Max(0f, content.anchoredPosition.y);
            float depth = BezelLayout.PopupPlace(-r.y - scrolled, r.height, popupH, view, 0f, out float _) + scrolled;
            return new Rect(r.x, -depth, width > 0f ? width : r.width, popupH);
        }
    }
}
