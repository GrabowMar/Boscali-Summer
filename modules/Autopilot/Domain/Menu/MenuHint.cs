using System;

namespace BoscaliSummer.Modules.Autopilot.Domain
{
    /// <summary>The hint bar: mode | live text | back + close. Live text names what the next input does.</summary>
    internal readonly struct MenuHintParts
    {
        public MenuHintParts(string mode, string live, string back, bool warn) { Mode = mode; Live = live; Back = back; Warn = warn; }
        public string Mode { get; }
        public string Live { get; }
        public string Back { get; }
        /// <summary>The live segment is amber: a danger hold is armed or running.</summary>
        public bool Warn { get; }
    }

    /// <summary>The hub readout: the focused action and what it does, shown under the hub while a row is focused.</summary>
    internal readonly struct MenuReadout
    {
        public MenuReadout(string title, string desc) { Title = title; Desc = desc; }
        public string Title { get; }
        public string Desc { get; }
    }

    /// <summary>All the words the controller state turns into, as pure functions (the view only paints them).</summary>
    internal static class MenuHint
    {
        // A controller has the one Radial Menu button and its stick or hat (whatever the player bound), so its legends name those, never A / B.
        public static string BackGlyph(MenuDevice d) => d == MenuDevice.Mouse ? "RMB" : d == MenuDevice.Pad ? "STICK" : "BKSP";

        public static string Press(MenuDevice d) => d == MenuDevice.Pad ? "PRESS" : d == MenuDevice.Mouse ? "CLICK" : "ENTER";

        public static string RowLabel(MenuRow r) => (r.Verb != null ? r.Verb + " " : "") + r.Label;

        public static MenuHintParts Parts(MenuController c)
        {
            bool hold = c.Mode == MenuMode.Hold, pad = c.Device == MenuDevice.Pad;
            MenuRow row = c.FocusedRow;
            // the extra open key is configurable: a long name (LEFTSHIFT) would push the row label out of the 396 px segment, so only short names are
            // spelled out; the Radial Menu binding has no name here at all
            string release = pad || c.OpenKeyName.Length == 0 || c.OpenKeyName.Length > 4 ? "RELEASE" : "RELEASE " + c.OpenKeyName;
            string key = hold ? release : Press(c.Device);
            string enter = c.PadSteps ? "HAT UP" : "HOLD STICK", turn = c.PadSteps ? "HAT LEFT / RIGHT" : "STICK";
            int aimed = pad && c.Wheel && c.WheelAim >= 0 ? c.WheelSlot(c.WheelAim) : MenuController.SlotEmpty;
            string act;
            bool warn = false;
            if (c.Hold != null)
            {
                act = "HOLDING " + c.Hold.Row.Label + " · RELEASE TO ABORT";
                if (act.Length > 34) act = act.Replace("HOLDING", "HOLD");
                warn = true;
            }
            else if (aimed == MenuController.SlotBack) act = enter + " · BACK";
            else if (aimed == MenuController.SlotMore) act = enter + " · NEXT PAGE";
            else if (row != null && row != MenuRow.BackRow && row.IsBlocked) act = row.BlockedText;
            else if (row == MenuRow.BackRow) act = key + " · BACK";
            else if (row != null && row.NeedsHold)
            {
                act = "HOLD " + (pad ? "BUTTON" : c.Device == MenuDevice.Key ? "ENTER" : "LMB") + " 0.8 S · " + row.Label;
                warn = true;
            }
            else if (row != null && row.Kind == MenuKind.Picker) act = key + " · SELECT " + row.Label.Split('·')[0].Trim();
            else if (row != null && row.Kind == MenuKind.Submenu) act = (pad ? enter : key) + " · OPEN " + (row.HintNoun ?? row.Label);
            else if (row != null) act = key + " · " + RowLabel(row);
            else if (c.Cat < 0 && pad) act = turn + " · AIM A CATEGORY";
            else if (c.Cat < 0 && c.Recalled != null) act = (hold ? "POINT OR 1–0 · " : "CLICK OR 1–0 · ") + "SPACE FOR " + c.Recalled.Label;
            else if (c.Cat < 0) act = hold ? "POINT OR 1–0 · PICK A CATEGORY" : "CLICK OR 1–0 · PICK A CATEGORY";
            else
            {
                // the digits that act now: one per visible row
                int n = Math.Min(c.Items.Count, c.Cap);
                string keys = n > 1 ? " OR 1–" + n : n == 1 ? " OR 1" : "";
                if (!c.Category.Available) act = c.Category.Reason.Length > 0 ? c.Category.Reason : "NOTHING AVAILABLE";
                else if (pad) act = c.Wheel ? turn + " · AIM A ROW" : enter + " · OPEN " + c.Category.Label;
                else act = hold ? "AIM" + keys + (c.Locked ? " · POINT AT ANOTHER TO RE-AIM" : " · PICK A ROW") : "CLICK" + keys + " · PICK A ROW";
            }
            // a refused key says why in words for as long as a toast: the amber pulse alone is 180 ms of colour
            if (c.Refusal != null && c.Hold == null) { act = c.Refusal; warn = true; }
            string back = pad ? (c.Wheel ? (c.PadSteps ? "HAT DOWN · BACK" : hold ? "AIM BACK · " + enter : "CENTRE + PRESS · CLOSE") : hold ? "RELEASE · CLOSE" : "PRESS · CLOSE")
                : BackGlyph(c.Device) + " BACK · " + (hold ? "ESC CANCEL" : "ESC CLOSE");
            return new MenuHintParts(hold ? "HOLD MODE" : "LATCHED", act, back, warn);
        }

        public static string FooterLeft(MenuController c) =>
            c.Category != null && c.Category.Available && c.Device == MenuDevice.Key ? "↑↓ ROW · ←→ RING" : "";

        public static string FooterRight(MenuController c) =>
            c.Sub != null ? "" : c.Device == MenuDevice.Mouse ? "RMB · BACK" : c.Device == MenuDevice.Pad ? "" : "BKSP/0 · BACK";

        /// <summary>Null when nothing is focused: the hub then shows nothing.</summary>
        public static MenuReadout? Readout(MenuController c)
        {
            MenuRow row = c.FocusedRow;
            if (row == null) return null;
            if (row == MenuRow.BackRow) return new MenuReadout("BACK", "BACK TO " + (c.Category != null ? c.Category.Label : ""));
            return new MenuReadout(RowLabel(row), row.IsBlocked ? row.BlockedText : row.Desc);
        }

        /// <summary>Dock header: category label, or "CATEGORY › LIST" at depth 3; context on the right.</summary>
        public static string Crumb(MenuController c) => c.Sub != null ? c.Category.Crumb : "";

        public static string Title(MenuController c) => c.Sub != null ? c.Sub.Label : c.Category.Label;

        public static string Context(MenuController c)
        {
            MenuCategory cat = c.Category;
            if (cat == null || !cat.Available) return "";
            if (c.Sub == null) return cat.Context;
            if (c.Sub.Context != null) return c.Sub.ContextTail;
            int dot = cat.Context.LastIndexOf(" · ", StringComparison.Ordinal);
            return dot >= 0 ? cat.Context.Substring(dot + 3) : cat.Context;
        }
    }
}
