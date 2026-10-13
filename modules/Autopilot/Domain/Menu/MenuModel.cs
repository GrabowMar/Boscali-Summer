using System;
using System.Collections.Generic;

namespace BoscaliSummer.Modules.Autopilot.Domain
{
    internal enum MenuKind : byte { Action, Toggle, Cycle, Submenu, Picker, Danger }

    /// <summary>State-line tone. Dim = unavailable but not a hazard; Caution/Danger are the only warm colours.</summary>
    internal enum MenuTone : byte { Normal, Active, Caution, Danger, Dim }

    /// <summary>
    /// The words of a row in one of its states: a toggle owns two faces, every other kind one. The label
    /// states the outcome of a press (LOWER GEAR, not GEAR), the state line is NOUN: VALUE.
    /// </summary>
    internal readonly struct MenuFace
    {
        public MenuFace(string label, string state, string desc, string toast = null, MenuTone tone = MenuTone.Normal)
        {
            Label = label; State = state; Desc = desc; Toast = toast; Tone = tone;
        }

        public string Label { get; }
        public string State { get; }
        public string Desc { get; }
        /// <summary>What the toast says when this face is pressed.</summary>
        public string Toast { get; }
        public MenuTone Tone { get; }
    }

    /// <summary>
    /// One row of a dock list. Id and Kind never change; every other field is live state that
    /// <see cref="MenuModel.Reconcile"/> rewrites from a fresh build, so a rebuild can never reorder, add or
    /// remove a row under the pointer. Build rows with the factories, then set the optional fields.
    /// </summary>
    internal sealed class MenuRow
    {
        public const string GoneText = "UNAVAILABLE";

        private MenuRow(string id, MenuKind kind) { Id = id; Kind = kind; }

        /// <summary>Stable across rebuilds, slash-joined from the category: "flight/gear", "weapons/select/st2".</summary>
        public string Id { get; }
        public MenuKind Kind { get; }
        /// <summary>Sub-header this row sits under (COMMS: TACTICAL / REPLY / STATUS). Part of the order, never rewritten.</summary>
        public string Group = null;
        /// <summary>Position in the provider list the Run closure was bound to (station, call, perk, filter); -1 when unused.</summary>
        public int Index = -1;

        // ---- live state, copied by CopyLiveFrom -------------------------------------------------------------
        /// <summary>Action / Picker / Submenu / Danger: the only face. Toggle: the face while OFF (what a press does now).</summary>
        public MenuFace Face;
        /// <summary>Toggle only: the face while ON.</summary>
        public MenuFace OnFace;
        public bool IsOn;
        public string Icon;
        /// <summary>Toggle: glyph while ON when it differs (autoland becomes autoland-cancel).</summary>
        public string IconOn;
        /// <summary>Verb the hub and hint put before the label (SEND COPY).</summary>
        public string Verb;
        /// <summary>Map-ping pictograph drawn at the right edge (comms calls that drop a ping).</summary>
        public string Ping;
        /// <summary>Square checkbox at the right edge (steady boolean whose label is an ON/OFF flip).</summary>
        public bool Checkbox;
        /// <summary>Square armed pip instead of a checkbox (OPS perks).</summary>
        public bool ArmedPip;
        /// <summary>Non-null: a press must be HELD 0.8 s; the text is the bar label (HOLD TO EJECT).</summary>
        public string HoldLabel;
        /// <summary>Danger rows swap their icon slot for this amber marker.</summary>
        public string WarnIcon;
        /// <summary>Non-null: temporarily unavailable ("GEAR: EXTENDING"); keeps its place and number, dimmed, unselectable.</summary>
        public string Blocked;
        /// <summary>Set by Reconcile when a rebuild no longer offers the row: it stays, dimmed, until the menu closes.</summary>
        public bool Gone;
        // cycle
        public string[] Values;
        public int ValueIndex;
        public string Lead;
        public string StatePrefix;
        public string ToastPrefix;
        // picker
        public bool Selected;
        public string StateSelected;
        /// <summary>Picker pip drawn hatched: empty station.</summary>
        public bool Hatched;
        // submenu
        public string Context;
        private string tailFor, tail;
        public string HintNoun;
        public bool Iconless;
        public List<MenuRow> Children;
        /// <summary>The game call. Null for a submenu. Rebuilt with every refresh so it always holds current references.</summary>
        public Action Run;

        // ---- factories --------------------------------------------------------------------------------------
        public static MenuRow Action(string id, string icon, string label, string state, string desc, string toast = null, MenuTone tone = MenuTone.Normal) =>
            new MenuRow(id, MenuKind.Action) { Icon = icon, Face = new MenuFace(label, state, desc, toast, tone) };

        public static MenuRow Toggle(string id, string icon, bool on, MenuFace whenOff, MenuFace whenOn, bool checkbox = true) =>
            new MenuRow(id, MenuKind.Toggle) { Icon = icon, IsOn = on, Face = whenOff, OnFace = whenOn, Checkbox = checkbox };

        public static MenuRow Cycle(string id, string icon, string lead, string statePrefix, string[] values, int index, string toastPrefix = null) =>
            new MenuRow(id, MenuKind.Cycle) { Icon = icon, Lead = lead, StatePrefix = statePrefix, Values = values, ValueIndex = index, ToastPrefix = toastPrefix };

        public static MenuRow Submenu(string id, string icon, string label, string state, string desc, string context, string hintNoun,
            List<MenuRow> children, bool iconless = false) =>
            new MenuRow(id, MenuKind.Submenu)
            {
                Icon = icon, Face = new MenuFace(label, state, desc), Context = context, HintNoun = hintNoun, Children = children, Iconless = iconless,
            };

        public static MenuRow Pick(string id, string icon, string label, string state, string stateSelected, bool selected, string desc,
            string toast, MenuTone tone = MenuTone.Normal, bool hatched = false) =>
            new MenuRow(id, MenuKind.Picker)
            {
                Icon = icon, Face = new MenuFace(label, state, desc, toast, tone), StateSelected = stateSelected, Selected = selected, Hatched = hatched,
            };

        public static MenuRow Danger(string id, string icon, string warnIcon, string label, string state, string desc, string holdLabel, string toast) =>
            new MenuRow(id, MenuKind.Danger)
            {
                Icon = icon, WarnIcon = warnIcon, Face = new MenuFace(label, state, desc, toast, MenuTone.Caution), HoldLabel = holdLabel,
            };

        // ---- what the menu shows now ------------------------------------------------------------------------
        private MenuFace Current => Kind == MenuKind.Toggle && IsOn ? OnFace : Face;

        public string Value => Values != null && Values.Length > 0 ? Values[ValueIndex % Values.Length] : "";
        public string NextValue => Values != null && Values.Length > 0 ? Values[(ValueIndex + 1) % Values.Length] : "";

        // a cycle row's three strings are built once per value change, not on every read (the painter reads them every frame)
        private string[] cycValues;
        private int cycIndex;
        private string cycLead, cycPrefix, cycLabel, cycState, cycDesc;

        private void Cycled()
        {
            if (cycLabel != null && cycValues == Values && cycIndex == ValueIndex && (object)cycLead == Lead && (object)cycPrefix == StatePrefix) return;
            cycValues = Values; cycIndex = ValueIndex; cycLead = Lead; cycPrefix = StatePrefix;
            cycLabel = Lead + " " + NextValue; cycState = StatePrefix + Value; cycDesc = Value + " → " + NextValue;
        }

        public string Label
        {
            get
            {
                if (Kind != MenuKind.Cycle) return Current.Label;
                Cycled();
                return cycLabel;
            }
        }

        public string State
        {
            get
            {
                if (IsBlocked) return BlockedText;
                if (Kind == MenuKind.Cycle) { Cycled(); return cycState; }
                if (Kind == MenuKind.Picker && Selected && StateSelected != null) return StateSelected;
                return Current.State;
            }
        }

        /// <summary>The hub's one-line description of what a press does.</summary>
        public string Desc
        {
            get
            {
                if (Kind != MenuKind.Cycle) return Current.Desc;
                Cycled();
                return cycDesc;
            }
        }

        public string IconNow => Kind == MenuKind.Toggle && IsOn && IconOn != null ? IconOn : Icon;

        public MenuTone ToneNow => IsBlocked ? MenuTone.Dim : Current.Tone;

        /// <summary>What the toast says right after this row runs (computed before the press, from the face that was pressed).</summary>
        public string ToastText() =>
            Kind == MenuKind.Cycle ? (ToastPrefix ?? StatePrefix) + NextValue : Current.Toast ?? Current.Label;

        /// <summary>The BACK row of a depth-3 list. Not part of any model; the controller focuses it as index -2.</summary>
        public static readonly MenuRow BackRow = Action("back", "back", "BACK", "", "RETURN TO THE PREVIOUS LEVEL");

        /// <summary>The part of <see cref="Context"/> after the last middle dot: the dock header of a depth-3 list shows only that. Built once per context text.</summary>
        public string ContextTail
        {
            get
            {
                if (Context == null) return null;
                if (!ReferenceEquals(tailFor, Context))
                {
                    tailFor = Context;
                    int dot = Context.LastIndexOf(" · ", StringComparison.Ordinal);
                    tail = dot >= 0 ? Context.Substring(dot + 3) : Context;
                }
                return tail;
            }
        }

        public bool IsBlocked => Blocked != null || Gone;
        public string BlockedText => Blocked ?? GoneText;
        public bool NeedsHold => HoldLabel != null;

        /// <summary>Copies every live field from a freshly built twin (same Id) and nothing else.</summary>
        public void CopyLiveFrom(MenuRow fresh)
        {
            Face = fresh.Face; OnFace = fresh.OnFace; IsOn = fresh.IsOn; Icon = fresh.Icon; IconOn = fresh.IconOn;
            Verb = fresh.Verb; Ping = fresh.Ping; Checkbox = fresh.Checkbox; ArmedPip = fresh.ArmedPip; HoldLabel = fresh.HoldLabel;
            WarnIcon = fresh.WarnIcon; Blocked = fresh.Blocked; Gone = false;
            Values = fresh.Values; ValueIndex = fresh.ValueIndex; Lead = fresh.Lead; StatePrefix = fresh.StatePrefix; ToastPrefix = fresh.ToastPrefix;
            Selected = fresh.Selected; StateSelected = fresh.StateSelected; Hatched = fresh.Hatched;
            Context = fresh.Context; HintNoun = fresh.HintNoun; Iconless = fresh.Iconless; Run = fresh.Run; Index = fresh.Index;
            if (Children != null && fresh.Children != null) ReconcileList(Children, fresh.Children);
        }

        internal static void ReconcileList(List<MenuRow> rows, List<MenuRow> freshRows)
        {
            for (int i = 0; i < rows.Count; i++)
            {
                MenuRow twin = Find(freshRows, rows[i].Id);
                if (twin != null) rows[i].CopyLiveFrom(twin);
                else rows[i].Gone = true;
            }
        }

        internal static MenuRow Find(List<MenuRow> rows, string id)
        {
            for (int i = 0; i < rows.Count; i++) if (rows[i].Id == id) return rows[i];
            return null;
        }
    }

    /// <summary>One entry of a switch list (a scope page, a board part, a cabin effect): its id slug and its words.</summary>
    internal readonly struct MenuSwitch
    {
        public MenuSwitch(string slug, string name, string desc, bool enable = false) { Slug = slug; Name = name; Desc = desc; Enable = enable; }
        public string Slug { get; }
        public string Name { get; }
        public string Desc { get; }
        /// <summary>The row reads ENABLE / DISABLE (a behaviour) instead of SHOW / HIDE (something drawn).</summary>
        public bool Enable { get; }
    }

    /// <summary>One ring category. Its angle and key never change; an unavailable one dims in place with its reason.</summary>
    internal sealed class MenuCategory
    {
        public MenuCategory(string id, string label, float angleDeg, string key, string icon)
        {
            Id = id; Label = label; Angle = angleDeg; Key = key; Icon = icon;
        }

        public string Id { get; }
        public string Label { get; }
        /// <summary>The dock header's lead at depth 3: "WING › ".</summary>
        public string Crumb => crumb ??= Label + " › ";

        /// <summary>Clockwise from 12 o'clock.</summary>
        public float Angle { get; }
        /// <summary>Ring key-cap: 1..9 then 0.</summary>
        public string Key { get; }
        public string Icon { get; }

        public string Context = "";
        private string crumb;
        public bool Available = true;
        /// <summary>Unavailable only: replaces the badge (NO WING).</summary>
        public string Reason = "";
        public string EmptyTitle = "";
        public string EmptyBody = "";
        /// <summary>Live ring readout, at most <see cref="MenuModel.BadgeChars"/> characters.</summary>
        public string Badge = "";
        /// <summary>Worth a second plate line while the ring is idle (armed, caution, changed).</summary>
        public bool BadgeShow;
        public bool BadgeAlert;
        /// <summary>The dock list has no icon column (COMMS).</summary>
        public bool Iconless;
        public List<MenuRow> Rows = new List<MenuRow>();

        internal void CopyLiveFrom(MenuCategory fresh)
        {
            Context = fresh.Context; EmptyTitle = fresh.EmptyTitle; EmptyBody = fresh.EmptyBody; Reason = fresh.Reason;
            Badge = fresh.Badge; BadgeShow = fresh.BadgeShow; BadgeAlert = fresh.BadgeAlert; Iconless = fresh.Iconless;
            // A category that had no rows at build time stays unavailable until the next open: rows are never added mid-session.
            Available = fresh.Available && Rows.Count > 0;
            MenuRow.ReconcileList(Rows, fresh.Rows);
        }
    }

    /// <summary>Everything the menu shows: ten categories at fixed angles.</summary>
    internal sealed class MenuModel
    {
        public const int BadgeChars = 8;

        public string AircraftName = "";
        public readonly List<MenuCategory> Categories = new List<MenuCategory>(10);

        public MenuCategory Category(string id)
        {
            for (int i = 0; i < Categories.Count; i++) if (Categories[i].Id == id) return Categories[i];
            return null;
        }

        public int IndexOf(string id)
        {
            for (int i = 0; i < Categories.Count; i++) if (Categories[i].Id == id) return i;
            return -1;
        }

        public MenuRow Row(string id)
        {
            for (int c = 0; c < Categories.Count; c++)
            {
                MenuRow hit = FindIn(Categories[c].Rows, id);
                if (hit != null) return hit;
            }
            return null;
        }

        private static MenuRow FindIn(List<MenuRow> rows, string id)
        {
            for (int i = 0; i < rows.Count; i++)
            {
                if (rows[i].Id == id) return rows[i];
                if (rows[i].Children != null)
                {
                    MenuRow hit = FindIn(rows[i].Children, id);
                    if (hit != null) return hit;
                }
            }
            return null;
        }

        /// <summary>
        /// Takes the live state of a freshly built model while keeping this model's shape: categories, rows and
        /// children keep their order and index; a row the rebuild no longer offers is kept as a dimmed
        /// UNAVAILABLE row; a row it newly offers waits for the next open. The hub text follows the rebuild.
        /// </summary>
        public void Reconcile(MenuModel fresh)
        {
            AircraftName = fresh.AircraftName;
            for (int i = 0; i < Categories.Count; i++)
            {
                MenuCategory twin = fresh.Category(Categories[i].Id);
                if (twin != null) Categories[i].CopyLiveFrom(twin);
            }
        }
    }
}
