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

    /// <summary>The hub readout: the focused action and what it does, or null title for the status summary.</summary>
    internal readonly struct MenuReadout
    {
        public MenuReadout(string title, string desc) { Title = title; Desc = desc; }
        public string Title { get; }
        public string Desc { get; }
    }

    /// <summary>All the words the controller state turns into, as pure functions (the view only paints them).</summary>
    internal static class MenuHint
    {
        // The pad's own Back button and d-pad are not wired (the host reads the stick and the one A button), so a pad user is told the keys that do work.
        public static string BackGlyph(MenuDevice d) => d == MenuDevice.Mouse ? "RMB" : "BKSP";

        /// <summary>The dock footer's back legend.</summary>
        public static string BackFooter(MenuDevice d) => d == MenuDevice.Mouse ? "RMB" : "BKSP/0";

        public static string Press(MenuDevice d) => d == MenuDevice.Pad ? "A" : d == MenuDevice.Mouse ? "CLICK" : "ENTER";

        public static string RowLabel(MenuRow r) => (r.Verb != null ? r.Verb + " " : "") + r.Label;

        public static MenuHintParts Parts(MenuController c)
        {
            bool hold = c.Mode == MenuMode.Hold;
            MenuRow row = c.FocusedRow;
            // the open key is configurable: a long name (LEFTSHIFT) would push the row label out of the 396 px segment, so only short names are spelled out
            string release = c.Device == MenuDevice.Pad || c.OpenKeyName.Length > 4 ? "RELEASE" : "RELEASE " + c.OpenKeyName;
            string key = hold ? (c.FocusFromPointer ? release : (c.Device == MenuDevice.Pad ? "A" : "ENTER")) : Press(c.Device);
            string act;
            bool warn = false;
            if (c.Hold != null)
            {
                act = "HOLDING " + c.Hold.Row.Label + " · RELEASE TO ABORT";
                if (act.Length > 34) act = act.Replace("HOLDING", "HOLD");
                warn = true;
            }
            else if (row != null && row != MenuRow.BackRow && row.IsBlocked) act = row.BlockedText;
            else if (row == MenuRow.BackRow) act = key + " · BACK";
            else if (row != null && row.NeedsHold)
            {
                act = "HOLD " + (c.Device == MenuDevice.Pad ? "A" : c.Device == MenuDevice.Key ? "ENTER" : "LMB") + " 0.8 S · " + row.Label;
                warn = true;
            }
            else if (row != null && row.Kind == MenuKind.Picker) act = key + " · SELECT " + row.Label.Split('·')[0].Trim();
            else if (row != null && row.Kind == MenuKind.Submenu) act = key + " · OPEN " + (row.HintNoun ?? row.Label);
            else if (row != null) act = key + " · " + RowLabel(row);
            else if (c.Cat < 0) act = hold ? "FLICK OR 1–0 · PICK A CATEGORY" : "CLICK OR 1–0 · PICK A CATEGORY";
            else act = hold ? (c.Locked ? "AIM OR 1–8 · PULL BACK TO RE-AIM" : "AIM OR 1–8 · PICK A ROW") : "CLICK OR 1–8 · PICK A ROW";
            return new MenuHintParts(hold ? "HOLD MODE" : "LATCHED", act, BackGlyph(c.Device) + " BACK · " + (hold ? "ESC CANCEL" : "ESC CLOSE"), warn);
        }

        public static string FooterLeft(MenuController c) =>
            c.Category != null && c.Category.Available && c.Device == MenuDevice.Key ? "↑↓ ROW · ←→ RING" : "";

        public static string FooterRight(MenuController c) => c.Sub != null ? "" : c.Device == MenuDevice.Mouse ? "RMB · BACK" : "BKSP/0 · BACK";

        /// <summary>Null when nothing is focused: the hub then shows the status summary.</summary>
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
