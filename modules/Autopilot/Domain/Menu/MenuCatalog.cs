using System;
using System.Collections.Generic;

namespace BoscaliSummer.Modules.Autopilot.Domain
{
    /// <summary>
    /// The v7 catalog as a pure function of a <see cref="MenuWorld"/> snapshot: ten categories at fixed
    /// angles, verb-first labels that state the outcome of a press (LOWER GEAR flips to RAISE GEAR),
    /// NOUN: VALUE state lines, hybrid availability (a category that has nothing dims in place with a reason; inside a
    /// category a row that cannot exist right now is absent, one that is only blocked for a moment stays dimmed with
    /// its reason) and a ring badge per category. <paramref name="bind"/> attaches the game call to each runnable row.
    /// Copy rules: UPPER CASE, label at most 22 characters, state at most 30, description at most 48 and never a middle dot.
    /// </summary>
    internal static class MenuCatalog
    {
        public const int MaxLabel = 22, MaxState = 30, MaxDesc = 48;
        /// <summary>The steps of the two TAC BOARD cycle rows, in the order of the <c>BoardDeclutter</c> and <c>BoardLayout</c> contract enums (a press moves to the next, wrapping).</summary>
        public static readonly string[] BoardDeclutters = { "FULL", "LEAN", "MIN" }, BoardLayouts = { "WIDE", "STACK" };
        private const string D = MenuText.Dot;
        private const string X = MenuText.Times;

        public static MenuModel Build(MenuWorld w, Func<MenuRow, Action> bind = null)
        {
            var b = new Builder(bind);
            var model = new MenuModel { AircraftName = MenuText.Upper(w.Aircraft, MaxLabel) };
            model.Categories.Add(Weapons(w, b));
            model.Categories.Add(Lights(w, b));
            model.Categories.Add(Flight(w, b));
            model.Categories.Add(Wing(w, b));
            model.Categories.Add(Comms(w, b));
            model.Categories.Add(Radio(w, b));
            model.Categories.Add(Support(w, b));
            model.Categories.Add(View(w, b));
            model.Categories.Add(Defence(w, b));
            model.Categories.Add(Targets(w, b));
            foreach (MenuCategory c in model.Categories)
                if (c.Rows.Count == 0)
                {
                    c.Available = false;
                    if (c.Reason.Length == 0) c.Reason = "NONE";
                    if (c.EmptyTitle.Length == 0) c.EmptyTitle = c.Reason;
                    if (c.EmptyBody.Length == 0) c.EmptyBody = "NOT AVAILABLE RIGHT NOW";
                    c.Badge = c.Reason; c.BadgeShow = true; c.BadgeAlert = false;
                }
            model.Summary = Summary(w);
            return model;
        }

        // ------------------------------------------------------------------ plumbing

        private sealed class Builder
        {
            private readonly Func<MenuRow, Action> bind;
            public Builder(Func<MenuRow, Action> bind) { this.bind = bind; }

            public MenuRow Reg(MenuRow row)
            {
                if (bind != null) row.Run = bind(row);
                if (row.Children != null) foreach (MenuRow child in row.Children) Reg(child);
                return row;
            }
        }

        private static MenuFace F(string label, string state, string desc, string toast = null, MenuTone tone = MenuTone.Normal) =>
            new MenuFace(label, Cut(state, MaxState), desc, toast, tone);

        private static string Cut(string text, int max) => MenuText.Upper(text, max);

        private static MenuCategory Cat(string id, string label, float angle, string key, string context) =>
            new MenuCategory(id, label, angle, key, id) { Context = context };

        private static void Badge(MenuCategory c, string text, bool show = false, bool alert = false)
        {
            c.Badge = MenuText.Upper(text, MenuModel.BadgeChars);
            c.BadgeShow = show;
            c.BadgeAlert = alert;
        }

        private static List<MenuSummary> Summary(MenuWorld w)
        {
            var list = new List<MenuSummary>(4);
            int fuel = (int)Math.Round(Math.Max(0f, Math.Min(1f, w.Fuel)) * 100f);
            list.Add(new MenuSummary("FUEL", fuel + "%", w.Fuel < 0.2f));
            if (w.Gear != GearPhase.None)
                list.Add(w.Gear == GearPhase.Extending || w.Gear == GearPhase.Retracting ? new MenuSummary("GEAR", "MOVING", true)
                    : new MenuSummary("GEAR", w.Gear == GearPhase.Down ? "DN" : "UP"));
            if (w.Targets > 0) list.Add(new MenuSummary("TGT", w.Targets.ToString()));
            if (w.Wingmen > 0) list.Add(new MenuSummary("WING", w.Wingmen.ToString()));
            return list;
        }

        // ------------------------------------------------------------------ WEAPONS

        private static string StationName(WorldStation s) => MenuText.Upper(s.Name, 14);

        private static string StationAmmo(WorldStation s) => X + MenuText.Ammo(s.Ammo);

        private static string StationTag(WorldStation s, int room)
        {
            string word = MenuText.FirstWord(s.Name, 64);
            if (word.Length <= room) return word;
            switch (s.Class)
            {
                case StationClass.Gun: return "GUN";
                case StationClass.Bomb: return "BOMB";
                case StationClass.Turret: return "TUR";
                case StationClass.Missile: return "MSL";
                case StationClass.Agm: return "AGM";
                default: return "STA";
            }
        }

        private static string StationIcon(WorldStation s)
        {
            switch (s.Class)
            {
                case StationClass.Gun: return "gun";
                case StationClass.Missile: return "aam";
                case StationClass.Agm: return "agm";
                case StationClass.Bomb: return "bomb";
                case StationClass.Turret: return "turret";
                default: return "hardpoint";
            }
        }

        private static MenuCategory Weapons(MenuWorld w, Builder b)
        {
            int n = w.Stations.Count;
            var c = Cat("weapons", "WEAPONS", 18f, "1", "LOADOUT" + D + MenuText.Plural(n, "STATION", "STATIONS"));
            if (n == 0) return c;
            int active = Math.Max(0, Math.Min(n - 1, w.ActiveStation));
            WorldStation now = w.Stations[active];
            if (n > 1)
            {
                int next = (active + 1) % n, prev = (active + n - 1) % n;
                c.Rows.Add(b.Reg(Stepper("weapons/next", "station-next", "NEXT STATION", "NEXT: STA " + (next + 1) + D + StationName(w.Stations[next]) + " " + StationAmmo(w.Stations[next]),
                    "STEP FORWARD THROUGH THE LOADOUT", "STA " + (next + 1) + D + StationName(w.Stations[next]))));
                c.Rows.Add(b.Reg(Stepper("weapons/prev", "station-prev", "PREV STATION", "PREV: STA " + (prev + 1) + D + StationName(w.Stations[prev]) + " " + StationAmmo(w.Stations[prev]),
                    "STEP BACK THROUGH THE LOADOUT", "STA " + (prev + 1) + D + StationName(w.Stations[prev]))));
            }
            var picks = new List<MenuRow>(n);
            for (int i = 0; i < n; i++)
            {
                WorldStation s = w.Stations[i];
                bool empty = s.Ammo <= 0;
                MenuRow pick = MenuRow.Pick("weapons/select/st" + (i + 1), StationIcon(s), "STA " + (i + 1) + D + StationName(s),
                    empty ? "AMMO: EMPTY" : "AMMO: " + StationAmmo(s), empty ? "ACTIVE" + D + "EMPTY" : "ACTIVE" + D + StationAmmo(s),
                    i == active, empty ? "STATION EMPTY, NOTHING TO FIRE" : "MAKE THIS THE ACTIVE WEAPON",
                    "STA " + (i + 1) + D + StationName(s), empty ? MenuTone.Dim : MenuTone.Normal, hatched: empty);
                pick.Index = i;
                picks.Add(pick);
            }
            c.Rows.Add(b.Reg(MenuRow.Submenu("weapons/select", "station-select", "SELECT STATION",
                "ACTIVE: " + StationName(now) + " " + StationAmmo(now), "CHOOSE A HARDPOINT", Cut(w.Aircraft, 14) + D + MenuText.Plural(n, "STATION", "STATIONS"), "STATIONS", picks)));
            if (w.HasMultipleGuns)
                c.Rows.Add(b.Reg(MenuRow.Toggle("weapons/link", "link-guns", w.GunsLinked,
                    F("LINK GUNS", "GUNS: SEPARATE", "FIRE ALL GUNS TOGETHER", "GUNS LINKED"),
                    F("UNLINK GUNS", "GUNS: LINKED", "FIRE THE ACTIVE GUN ONLY", "GUNS UNLINKED"))));
            if (w.HasTurrets)
                c.Rows.Add(b.Reg(MenuRow.Toggle("weapons/turret", "turret-auto", w.TurretAuto,
                    F("ENABLE TURRET AUTO", "TURRET: MANUAL", "TURRET TRACKS SELECTED TARGETS", "TURRET AUTO ON"),
                    F("DISABLE TURRET AUTO", "TURRET: AUTO", "YOU AIM THE TURRET", "TURRET AUTO OFF"))));

            string tail = X + MenuText.Ammo(now.Ammo);
            Badge(c, StationTag(now, MenuModel.BadgeChars - 1 - tail.Length) + " " + tail, show: now.Ammo <= 0, alert: now.Ammo <= 0);
            return c;
        }

        private static MenuRow Stepper(string id, string icon, string label, string state, string desc, string toast)
        {
            MenuRow row = MenuRow.Action(id, icon, label, state, desc, toast);
            row.StayOpen = true;
            return row;
        }

        // ------------------------------------------------------------------ LIGHTS

        private static MenuCategory Lights(MenuWorld w, Builder b)
        {
            var c = Cat("lights", "LIGHTS", 54f, "2", "EXTERIOR" + D + "VISION");
            if (w.HasNav)
                c.Rows.Add(b.Reg(MenuRow.Toggle("lights/nav", "nav-lights", w.NavOn,
                    F("TURN NAV LIGHTS ON", "NAV LIGHTS: OFF", "SHOW POSITION LIGHTS TO FRIENDLIES", "NAV LIGHTS ON"),
                    F("TURN NAV LIGHTS OFF", "NAV LIGHTS: ON", "GO DARK, HARDER TO SPOT AT NIGHT", "NAV LIGHTS OFF"))));
            if (w.HasLandingLight)
                c.Rows.Add(b.Reg(MenuRow.Cycle("lights/light", "landing-light", "SET LANDING LIGHT", "LANDING LIGHT: ",
                    new[] { "OFF", "ON", "AUTO" }, Math.Max(0, Math.Min(2, w.LandingLight)), "LANDING LIGHT: ")));
            if (w.HasNvg)
                c.Rows.Add(b.Reg(MenuRow.Toggle("lights/nvg", "night-vision", w.NvgOn,
                    F("ENABLE NIGHT VISION", "NIGHT VISION: OFF", "AMPLIFY LOW-LIGHT IMAGERY", "NIGHT VISION ON"),
                    F("DISABLE NIGHT VISION", "NIGHT VISION: ON", "RETURN TO NORMAL VISION", "NIGHT VISION OFF"))));
            if (w.HasNav && !w.NavOn) Badge(c, "NAV OFF", show: true);
            else if (w.HasNvg && w.NvgOn) Badge(c, "NVG ON", show: true);
            else if (w.HasLandingLight && w.LandingLight == 1) Badge(c, "LDG ON", show: true);
            else Badge(c, w.HasNav ? "NAV ON" : "");
            return c;
        }

        // ------------------------------------------------------------------ FLIGHT

        private static MenuCategory Flight(MenuWorld w, Builder b)
        {
            var c = Cat("flight", "FLIGHT", 90f, "3", "OWN AIRCRAFT");
            bool moving = w.Gear == GearPhase.Extending || w.Gear == GearPhase.Retracting;
            if (w.Airborne && w.Gear != GearPhase.None)
            {
                MenuRow gear = MenuRow.Toggle("flight/gear", "gear", w.Gear == GearPhase.Down || w.Gear == GearPhase.Extending,
                    F("LOWER GEAR", "CURRENT: UP", "DEPLOY LANDING GEAR", "GEAR EXTENDING"),
                    F("RAISE GEAR", "CURRENT: DOWN", "RETRACT LANDING GEAR", "GEAR RETRACTING"), checkbox: false);
                if (moving) gear.Blocked = w.Gear == GearPhase.Extending ? "GEAR: EXTENDING" : "GEAR: RETRACTING";
                c.Rows.Add(b.Reg(gear));
            }
            if (w.HasAssist)
                c.Rows.Add(b.Reg(MenuRow.Toggle("flight/assist", "flight-assist", w.Assist,
                    F("ENABLE ASSIST", "FLIGHT ASSIST: OFF", "LIMIT PITCH TO PREVENT STALLS", "FLIGHT ASSIST ON"),
                    F("DISABLE ASSIST", "FLIGHT ASSIST: ON", "REMOVE THE PITCH LIMITER", "FLIGHT ASSIST OFF"))));
            if (w.HasHover)
                c.Rows.Add(b.Reg(MenuRow.Toggle("flight/hover", "auto-hover", w.Hover,
                    F("ENABLE AUTO-HOVER", "AUTO-HOVER: OFF", "HOLD POSITION AND ALTITUDE", "AUTO-HOVER ON"),
                    F("DISABLE AUTO-HOVER", "AUTO-HOVER: ON", "RETURN VTOL CONTROL TO THE STICK", "AUTO-HOVER OFF"))));
            if (w.Airborne || w.AutolandEngaged)
            {
                MenuRow land = MenuRow.Toggle("flight/autoland", "autoland", w.AutolandEngaged,
                    F("AUTOLAND", "AUTOLAND: READY", "FLY THE APPROACH TO THE NEAREST STRIP", "AUTOLAND ENGAGED"),
                    F("CANCEL AUTOLAND", "AUTOLAND: ENGAGED", "ABORT THE APPROACH, TAKE THE STICK", "AUTOLAND CANCELLED", MenuTone.Active), checkbox: false);
                land.IconOn = "autoland-cancel";
                if (!w.AutolandEngaged) land.Blocked = w.AutolandBlock;
                c.Rows.Add(b.Reg(land));
            }
            MenuRow engine = MenuRow.Toggle("flight/engine", "engine", w.Ignition,
                F("START ENGINE", "ENGINE: SHUT DOWN", "RELIGHT THE ENGINE", "ENGINE STARTING", MenuTone.Caution),
                F("STOP ENGINE", "ENGINE: RUNNING", "CUTS FUEL IN FLIGHT", "ENGINE STOPPED"), checkbox: false);
            if (w.Ignition && w.Airborne) engine.HoldLabel = "HOLD TO STOP";
            c.Rows.Add(b.Reg(engine));
            if (w.CanEject)
                c.Rows.Add(b.Reg(MenuRow.Danger("flight/eject", "eject", "warning", "EJECT", "CONFIRM REQUIRED", "EJECT NOW, NO UNDO", "HOLD TO EJECT", "EJECTING")));

            if (!w.Airborne) Badge(c, "GROUND", show: true);
            else if (w.HasHover && w.Hover) Badge(c, "HOVER ON", show: true);
            else if (w.HasAssist) Badge(c, w.Assist ? "ASSIST" : "FA OFF", show: !w.Assist, alert: !w.Assist);
            else Badge(c, w.HasHover ? "HOVER OFF" : "");
            return c;
        }

        // ------------------------------------------------------------------ WING

        private static MenuCategory Wing(MenuWorld w, Builder b)
        {
            var c = Cat("wing", "WING", 126f, "4", "WING" + D + MenuText.Plural(w.Wingmen, "SHIP", "SHIPS"));
            c.Reason = "NO WING"; c.EmptyTitle = "NO WINGMEN"; c.EmptyBody = "ORDERS APPEAR WHEN AI WINGMEN JOIN YOUR FLIGHT";
            if (w.Wingmen <= 0) return c;
            string to = "TO: WING" + D + MenuText.Plural(w.Wingmen, "SHIP", "SHIPS");
            c.Rows.Add(b.Reg(MenuRow.Action("wing/formup", "form-up", "FORM UP ON ME", to, "ALL WINGMEN JOIN FORMATION ON YOU", "ORDER: FORM UP")));
            c.Rows.Add(b.Reg(MenuRow.Action("wing/engage", "engage", "ENGAGE TARGETS", w.Targets > 0 ? to + D + w.Targets + " TGT" : to, "WING ATTACKS YOUR SELECTED TARGETS", "ORDER: ENGAGE")));
            c.Rows.Add(b.Reg(MenuRow.Action("wing/breakoff", "break-off", "BREAK OFF ATTACK", to, "WING DISENGAGES AND REJOINS YOU", "ORDER: BREAK OFF")));
            c.Rows.Add(b.Reg(MenuRow.Action("wing/six", "clear-six", "CLEAR MY SIX", "TO: WINGMAN", "WINGMAN CHASES BOGEYS OFF YOUR TAIL", "ORDER: CLEAR SIX")));
            c.Rows.Add(b.Reg(MenuRow.Action("wing/dope", "bogey-dope", "ASK FOR BOGEY DOPE", "ANSWERS: WINGMAN", "WINGMAN CALLS BRA OF NEAREST AIR THREAT", "ASKED: BOGEY DOPE")));
            c.Rows.Add(b.Reg(MenuRow.Action("wing/escort", "escort", "ESCORT ME", to, "WING GUARDS YOU WHILE YOU STRIKE", "ORDER: ESCORT")));
            if (w.Shapes.Count > 0) c.Rows.Add(b.Reg(ShapeMenu(w)));
            c.Rows.Add(b.Reg(MenuRow.Action("wing/rtb", "rtb", "ORDER WING RTB", to, "WING RETURNS TO BASE AND LANDS", "ORDER: RTB")));
            Badge(c, w.Shape.Length > 0 ? ShortShape(w.Shape) : "WING " + w.Wingmen);
            return c;
        }

        /// <summary>FORMATION: the formations that suit the wing, scrolling, one dock group per family (families run together, game order inside).</summary>
        private static MenuRow ShapeMenu(MenuWorld w)
        {
            var families = new List<string>(4);
            foreach (WorldShape s in w.Shapes) if (!families.Contains(s.Family)) families.Add(s.Family);
            var kids = new List<MenuRow>(w.Shapes.Count);
            foreach (string family in families)
                foreach (WorldShape s in w.Shapes)
                {
                    if (s.Family != family) continue;
                    // the game's own name keeps its qualifier: FINGER FOUR (STRONG RIGHT) is a row FINGER FOUR with the state STRONG RIGHT
                    int paren = s.Name.EndsWith(")", StringComparison.Ordinal) ? s.Name.IndexOf(" (", StringComparison.Ordinal) : -1;
                    string label = paren > 0 ? s.Name.Substring(0, paren) : s.Name, note = paren > 0 ? s.Name.Substring(paren + 2, s.Name.Length - paren - 3) : family;
                    MenuRow pick = MenuRow.Pick("wing/shape/" + MenuText.Slug(s.Name), "shape", Cut(label, MaxLabel), Cut(note, MaxState), "ACTIVE" + D + Cut(note, MaxState - 9),
                        s.Active, "FLY THE WING IN THIS FORMATION", "FORMATION: " + s.Name);
                    pick.Group = family;
                    kids.Add(pick);
                }
            string current = "FORMATION: " + (w.Shape.Length > 0 ? w.Shape : "NONE");
            if (current.Length > MaxState) current = "FORMATION: " + ShortShape(w.Shape);
            return MenuRow.Submenu("wing/shape", "shape", "FORMATION", current, "CHOOSE THE WING FORMATION", "WING", "FORMATIONS", kids, iconless: true);
        }

        /// <summary>A formation as a ring badge, at most <see cref="MenuModel.BadgeChars"/>: no qualifier, FOUR as 4, then the longest words cut to fit.</summary>
        internal static string ShortShape(string name)
        {
            string text = MenuText.Upper(name, 64);
            int paren = text.IndexOf(" (", StringComparison.Ordinal);
            if (paren > 0) text = text.Substring(0, paren);
            string[] words = text.Replace(" FOUR", " 4").Split(' ');
            while (string.Join(" ", words).Length > MenuModel.BadgeChars)
            {
                int longest = 0;
                for (int i = 0; i < words.Length; i++) if (words[i].Length >= words[longest].Length) longest = i;
                int over = string.Join(" ", words).Length - MenuModel.BadgeChars;
                if (words[longest].Length <= 3) break;
                words[longest] = words[longest].Substring(0, Math.Max(3, words[longest].Length - over));
            }
            return string.Join(" ", words);
        }

        // ------------------------------------------------------------------ COMMS

        private struct CallSpec
        {
            public string Label, Group, Icon, Ping, PingName, Desc;
            public int Order;
        }

        private static readonly CallSpec[] Calls =
        {
            Call("NEED SUPPORT", "TACTICAL", 0, "call-tactical", "ping-help", "HELP", "REQUEST HELP AT MY POSITION"),
            Call("ENGAGING", "TACTICAL", 1, "call-tactical", "ping-attack", "ATTACK", "ENGAGING TARGETS NEAR MY POSITION"),
            Call("SPIKE", "TACTICAL", 2, "call-tactical", "ping-spike", "SPIKE", "ENEMY RADAR IS TRACKING ME"),
            Call("SPLASH ONE", "TACTICAL", 3, "call-tactical", null, null, "TARGET DESTROYED"),
            Call("ON MY WAY", "REPLY", 4, "call-chatter", "ping-rally", "RALLY", "MOVING TO SUPPORT"),
            Call("COPY", "REPLY", 5, "call-chatter", null, null, "UNDERSTOOD"),
            Call("NEGATIVE", "REPLY", 6, "call-chatter", null, null, "NO / UNABLE"),
            Call("THANKS", "REPLY", 7, "call-chatter", null, null, "THANKS FOR THE HELP"),
            Call("BINGO FUEL", "STATUS", 8, "call-logistics", null, null, "FUEL LOW, HEADING HOME SOON"),
            Call("WINCHESTER", "STATUS", 9, "call-logistics", null, null, "OUT OF WEAPONS"),
            Call("RTB", "STATUS", 10, "call-logistics", null, null, "RETURNING TO BASE"),
            Call("GG", "STATUS", 11, "call-logistics", null, null, "GOOD GAME"),
        };

        private static CallSpec Call(string label, string group, int order, string icon, string ping, string pingName, string desc) =>
            new CallSpec { Label = label, Group = group, Order = order, Icon = icon, Ping = ping, PingName = pingName, Desc = desc };

        private static MenuCategory Comms(MenuWorld w, Builder b)
        {
            var c = Cat("comms", "COMMS", 162f, "5", "TEAM CALLS");
            c.Iconless = true;
            if (!w.CallsAvailable || w.CallLabels.Count == 0) return c;
            var rows = new List<KeyValuePair<int, MenuRow>>(w.CallLabels.Count);
            for (int i = 0; i < w.CallLabels.Count; i++)
            {
                string label = MenuText.Upper(w.CallLabels[i], MaxLabel);
                CallSpec spec = Array.Find(Calls, s => s.Label == label);
                bool known = spec.Label != null;
                MenuRow row = MenuRow.Action("comms/c" + MenuText.Slug(label), known ? spec.Icon : "call-chatter", label,
                    "TEAM" + D + (known && spec.Ping != null ? spec.PingName + " PING" : "NO PING"),
                    known ? spec.Desc : "SEND THIS BREVITY CALL", "SENT: " + label);
                row.Verb = "SEND";
                row.Group = known ? spec.Group : "MORE";
                row.Ping = known ? spec.Ping : null;
                row.Index = i;
                if (!w.CanCall) row.Blocked = "COMMS OFF";
                rows.Add(new KeyValuePair<int, MenuRow>(known ? spec.Order : 100 + i, row));
            }
            rows.Sort((x, y) => x.Key.CompareTo(y.Key));
            foreach (var pair in rows) c.Rows.Add(b.Reg(pair.Value));
            Badge(c, w.CanCall ? "NET OPEN" : "NET OFF");
            return c;
        }

        // ------------------------------------------------------------------ RADIO

        private static MenuCategory Radio(MenuWorld w, Builder b)
        {
            var c = Cat("radio", "RADIO", 198f, "6", "COCKPIT RADIO");
            string title = Cut(w.DeckTitle, 22), station = Cut(w.Station, 22);
            if (w.DeckAvailable)
            {
                c.Rows.Add(b.Reg(MenuRow.Toggle("radio/music", "music", w.DeckPlaying,
                    F("PLAY MUSIC", title.Length > 0 ? "PAUSED: " + title : "MUSIC: PAUSED", "RESUME THE MUSIC DECK", "MUSIC PLAYING"),
                    F("PAUSE MUSIC", title.Length > 0 ? "CURRENT: " + title : "MUSIC: PLAYING", "PAUSE THE MUSIC DECK", "MUSIC PAUSED", MenuTone.Active), checkbox: false)));
                c.Rows.Add(b.Reg(Stepper("radio/trknext", "track-next", "PLAY NEXT TRACK", title.Length > 0 ? "CURRENT: " + title : "MUSIC DECK", "SKIP TO THE NEXT TRACK", "NEXT TRACK")));
                c.Rows.Add(b.Reg(Stepper("radio/trkprev", "track-prev", "PLAY PREV TRACK", title.Length > 0 ? "CURRENT: " + title : "MUSIC DECK", "BACK TO THE LAST TRACK", "PREVIOUS TRACK")));
            }
            if (w.ReceiverAvailable)
            {
                c.Rows.Add(b.Reg(MenuRow.Toggle("radio/receiver", "receiver", w.ReceiverOn,
                    F("TURN RECEIVER ON", "RECEIVER: OFF", "LISTEN TO THE LAST STATION", "RECEIVER ON"),
                    F("TURN RECEIVER OFF", w.ReceiverOn && station.Length > 0 ? "TUNED: " + station : "RECEIVER: ON", "SILENCE THE BROADCAST RECEIVER", "RECEIVER OFF", MenuTone.Active))));
                c.Rows.Add(b.Reg(Stepper("radio/seek", "seek", "SEEK NEXT STATION", w.ReceiverOn && station.Length > 0 ? "TUNED: " + station : "RECEIVER: OFF",
                    "SCAN UP THE DIAL TO THE NEXT STATION", "STATION CHANGED")));
            }
            if (w.DeckAvailable || w.ReceiverAvailable)
            {
                MenuRow stop = MenuRow.Action("radio/stopall", "stop-audio", "STOP ALL AUDIO",
                    "RECEIVER: " + (w.ReceiverOn ? "ON" : "OFF") + D + "MUSIC: " + (w.DeckPlaying ? "ON" : "OFF"), "SILENCE ALL RADIO AND MUSIC", "AUDIO STOPPED");
                if (!w.CanStopAll) stop.Blocked = "NOTHING PLAYING";
                c.Rows.Add(b.Reg(stop));
            }
            if (w.DeckPlaying) Badge(c, "MUSIC", show: true);
            else if (w.ReceiverAvailable && !w.ReceiverOn) Badge(c, "RX OFF", show: true);
            else Badge(c, w.ReceiverOn ? MenuText.FirstWord(w.Station, MenuModel.BadgeChars) : "RADIO");
            return c;
        }

        // ------------------------------------------------------------------ SUPPORT

        private static MenuCategory Support(MenuWorld w, Builder b)
        {
            var c = Cat("support", "SUPPORT", 234f, "7", "OPS");
            string markState = w.HasMark ? "MARK: SET" + D + (int)w.MarkAgeSeconds + " S AGO" : "MARK: NONE";
            if (w.MarkAvailable)
            {
                MenuRow mark = MenuRow.Action("support/mark", "camera-mark", "MARK CAMERA POINT", markState, "MARK WHERE THE CAMERA IS LOOKING", "MARK SET");
                mark.StayOpen = true;
                if (!w.CanCapture) mark.Blocked = "NO SURFACE IN VIEW";
                c.Rows.Add(b.Reg(mark));
                MenuRow call = MenuRow.Action("support/callmark", "call-at-mark", "CALL AT MARK",
                    w.Armed.Length > 0 ? "ARMED: " + w.Armed : "NOTHING ARMED", "SEND THE ARMED SUPPORT TO THE MARK", "SUPPORT CALLED AT MARK",
                    w.Armed.Length > 0 ? MenuTone.Normal : MenuTone.Caution);
                if (!w.CanCallAtMark) call.Blocked = !w.HasMark ? "MARK: NONE" : w.Armed.Length == 0 ? "NOTHING ARMED" : "NOT AVAILABLE";
                c.Rows.Add(b.Reg(call));
                MenuRow clear = MenuRow.Action("support/clearmark", "mark-clear", "CLEAR MARK", markState, "FORGET THE MARKED POINT", "MARK CLEARED");
                clear.StayOpen = true;
                if (!w.HasMark) clear.Blocked = "MARK: NONE";
                c.Rows.Add(b.Reg(clear));
            }
            int armed = 0, ready = 0;
            if (w.PerksAvailable && w.Perks.Count > 0)
            {
                var kids = new List<MenuRow>(w.Perks.Count);
                for (int i = 0; i < w.Perks.Count; i++)
                {
                    WorldPerk p = w.Perks[i];
                    if (p.Armed) armed++; else if (p.Cooldown <= 0) ready++;
                    string name = MenuText.Upper(p.Label, 14), front = MenuText.Upper(p.Front, 5);
                    MenuRow perk = MenuRow.Toggle("support/perks/" + MenuText.Slug(p.Label), front.Length > 0 ? "front-" + front.ToLowerInvariant() : "perk", p.Armed,
                        F("ARM " + name, p.Note, "ARM, THEN PRESS AGAIN TO FIRE", name + " ARMED"),
                        F("EXECUTE " + name, "ARMED: PRESS AGAIN", "PRESS AGAIN TO FIRE AT YOUR AIM", name + " FIRED", MenuTone.Active), checkbox: false);
                    perk.ArmedPip = true;
                    perk.Index = i;
                    if (front.Length > 0) perk.Group = front;
                    if (p.Cooldown > 0 && !p.Armed) perk.Blocked = "COOLDOWN: " + p.Cooldown + " S";
                    kids.Add(perk);
                }
                c.Rows.Add(b.Reg(MenuRow.Submenu("support/perks", "perk", "OPS PERKS", ready + " READY" + D + armed + " ARMED",
                    "ARM A PERK, PRESS AGAIN TO FIRE", "OPS PERKS", "PERKS", kids)));
            }
            if (w.SkywellCarried)
                c.Rows.Add(b.Reg(MenuRow.Toggle("support/skywell", "skywell", w.SkywellDeployed,
                    F("DEPLOY SKYWELL", Cut(w.SkywellStock, MaxState), "OPEN THE REFUEL AND REARM KIT", "SKYWELL DEPLOYED"),
                    F("STOW SKYWELL", Cut(w.SkywellStock, MaxState), "CLOSE THE REFUEL AND REARM KIT", "SKYWELL STOWED", MenuTone.Active), checkbox: false)));
            if (w.PitAvailable)
            {
                string[] ids = { "both", "fuel", "mun" }, labels = { "BOTH", "FUEL ONLY", "MUNITIONS ONLY" };
                var kids = new List<MenuRow>();
                for (int i = 0; i < labels.Length; i++)
                    kids.Add(MenuRow.Pick("support/pit/" + ids[i], i == 1 ? "engine" : "skywell", labels[i], "ORDER: " + labels[w.PitMode <= 2 ? w.PitMode : 0],
                        "ACTIVE", w.PitMode == i, "SET THE STANDING SERVICE ORDER", "ORDER: " + labels[i]));
                kids.Add(MenuRow.Action("support/pit/auto", "station-select", "AUTO ORDER", "EMPTIEST STATION FIRST", "CLEAR THE STATION PRIORITY", "AUTO ORDER"));
                for (int i = 0, n = Math.Min(w.PitStations.Count, 8); i < n; i++)
                {
                    WorldPitStation station = w.PitStations[i];
                    MenuRow row = MenuRow.Pick("support/pit/station/" + station.Index, "station-select", Cut(station.Label, 14), Cut(station.State, MaxState),
                        "FIRST", station.First, "HAND THIS STATION ITS ROUNDS FIRST", "HANDOFF FIRST");
                    row.Index = station.Index;
                    kids.Add(row);
                }
                c.Rows.Add(b.Reg(MenuRow.Submenu("support/pit", "skywell", "PIT STOP", labels[w.PitMode <= 2 ? w.PitMode : 0],
                    "SET THE ORDER AND FIRST STATION", "PIT STOP", "SERVICE", kids)));
            }
            if (w.CanExfil)
                c.Rows.Add(b.Reg(MenuRow.Action("support/exfil", "exfil", "REQUEST SQUAD EXFIL", "SQUAD: IN ROPE RANGE", "LIFT THE SQUAD OUT ON THE ROPES", "EXFIL REQUESTED")));
            if (armed > 0) Badge(c, armed + " ARMED", show: true, alert: true);
            else Badge(c, "OPS");
            return c;
        }

        // ------------------------------------------------------------------ VIEW

        private static MenuCategory View(MenuWorld w, Builder b)
        {
            var c = Cat("view", "VIEW", 270f, "8", "CAMERA");
            if (w.MissileViewActive)
            {
                string cam = "CAMERA: " + (w.MissileCurrent.Length > 0 ? w.MissileCurrent : "MISSILE"), next = cam, prev = cam;
                string[] now = w.MissileCurrent.Split(' ');
                string[] slot = now.Length >= 2 && now[0] == "MSL" ? now[1].Split('/') : null;
                if (slot != null && slot.Length == 2 && int.TryParse(slot[0], out int k) && int.TryParse(slot[1], out int n) && k >= 1 && n >= 1)
                {
                    next = "NEXT: MSL " + (k % n + 1) + "/" + n;
                    prev = "PREV: MSL " + ((k + n - 2) % n + 1) + "/" + n;
                }
                c.Rows.Add(b.Reg(Stepper("view/mslnext", "missile-next", "FOLLOW NEXT MISSILE", next, "SWITCH CAMERA TO THE NEXT MISSILE", "MISSILE CHANGED")));
                c.Rows.Add(b.Reg(Stepper("view/mslprev", "missile-prev", "FOLLOW PREV MISSILE", prev, "SWITCH CAMERA TO THE PREVIOUS ONE", "MISSILE CHANGED")));
                c.Rows.Add(b.Reg(MenuRow.Action("view/mslexit", "missile-exit", "EXIT MISSILE VIEW", cam, "LEAVE MISSILE VIEW, RESUME CAMERA", "MISSILE VIEW OFF")));
                if (w.MapAvailable) c.Rows.Add(b.Reg(MenuRow.Action("view/map", "map", "OPEN MAP", "MAP: CLOSED", "FULL-SCREEN TACTICAL MAP", "MAP OPEN")));
                string[] words = w.MissileCurrent.Split(' ');
                Badge(c, words.Length >= 2 ? words[0] + " " + words[1] : "MISSILE", show: true);
                return c;
            }
            if (w.CameraFollowing)
            {
                MenuRow cam = MenuRow.Toggle("view/cam", "external-view", w.Camera == ViewKind.Orbit,
                    F("SWITCH TO EXTERNAL", "CURRENT: " + (w.Camera == ViewKind.Cockpit ? "COCKPIT" : "OTHER VIEW"), "ORBIT THE CAMERA AROUND YOUR JET", "EXTERNAL VIEW"),
                    F("SWITCH TO COCKPIT", "CURRENT: EXTERNAL", "RETURN TO THE PILOT SEAT", "COCKPIT VIEW"), checkbox: false);
                cam.IconOn = "cockpit-view";
                if (w.Camera == ViewKind.Orbit && !w.HasCockpitPoint) cam.Blocked = "NO COCKPIT VIEW";
                c.Rows.Add(b.Reg(cam));
            }
            if (w.MapAvailable) c.Rows.Add(b.Reg(MenuRow.Action("view/map", "map", "OPEN MAP", "MAP: CLOSED", "FULL-SCREEN TACTICAL MAP", "MAP OPEN")));
            if (w.MissileViewAvailable)
            {
                // no missile in flight is transient: the row keeps its place and number, dimmed
                MenuRow view = MenuRow.Action("view/mslview", "missile-view", "ENTER MISSILE VIEW", Cut(w.MissileStatus, MaxState), "FOLLOW YOUR MISSILES IN FLIGHT", "MISSILE VIEW");
                if (!w.CanEnterMissileView) view.Blocked = "NO MISSILE IN FLIGHT";
                c.Rows.Add(b.Reg(view));
            }
            if (w.BoardAvailable)
            {
                c.Rows.Add(b.Reg(MenuRow.Toggle("view/board", "hardpoint", w.BoardOn,
                    F("SHOW TAC BOARD", "CURRENT: OFF", "RESTORE THE TAC BOARD", "TAC BOARD ON"),
                    F("HIDE TAC BOARD", "CURRENT: ON", "GO BACK TO STOCK PANELS", "TAC BOARD OFF"), checkbox: false)));
                // the two look rows keep their place while the board is off, dimmed with the reason: a row is never added mid-session
                MenuRow declutter = MenuRow.Cycle("view/declutter", "target-filter", "SET HUD DECLUTTER", "HUD DECLUTTER: ",
                    BoardDeclutters, Math.Max(0, Math.Min(BoardDeclutters.Length - 1, w.BoardDeclutter)), "HUD DECLUTTER: ");
                MenuRow layout = MenuRow.Cycle("view/boardlayout", "shape", "SET BOARD LAYOUT", "BOARD LAYOUT: ",
                    BoardLayouts, Math.Max(0, Math.Min(BoardLayouts.Length - 1, w.BoardLayout)), "BOARD LAYOUT: ");
                if (!w.BoardOn) declutter.Blocked = layout.Blocked = "TAC BOARD: OFF";
                c.Rows.Add(b.Reg(declutter));
                c.Rows.Add(b.Reg(layout));
            }
            if (w.Camera == ViewKind.Orbit) Badge(c, "EXTERNAL", show: true);
            else if (w.Missiles > 0) Badge(c, w.Missiles + " MSL", show: true);
            else Badge(c, w.CameraFollowing ? "COCKPIT" : "");
            return c;
        }

        // ------------------------------------------------------------------ DEFENCE

        private static MenuCategory Defence(MenuWorld w, Builder b)
        {
            var c = Cat("defence", "DEFENCE", 306f, "9", "COUNTERMEASURES");
            if (w.HasCountermeasures && w.HasFlares && w.Airborne)
            {
                string state = w.FlaresMax > 0 ? "FLARES: " + w.Flares + " / " + w.FlaresMax : "FLARES: " + w.Flares;
                MenuRow flares = MenuRow.Action("defence/flares", "flare", "POP FLARES", state, "DROP A FLARE SALVO, BREAKS IR LOCKS", "FLARES AWAY");
                if (w.Flares <= 0) flares.Blocked = "FLARES: EMPTY";
                c.Rows.Add(b.Reg(flares));
            }
            if (w.HasCountermeasures && w.Countermeasure.Length > 0 && w.NextCountermeasure.Length > 0)
                // the label names where a press leads: SWITCH TO RADAR JAMMER, and the hub says IR FLARES → RADAR JAMMER
                c.Rows.Add(b.Reg(MenuRow.Cycle("defence/cm", "countermeasure-cycle", "SWITCH TO", "ACTIVE: ", new[] { Cut(w.Countermeasure, 12), Cut(w.NextCountermeasure, 12) }, 0, "COUNTERMEASURE: ")));
            if (w.HasRadar)
                c.Rows.Add(b.Reg(MenuRow.Toggle("defence/radar", "radar", w.RadarOn,
                    F("RADIATE", "RADAR: SILENT", "TURN THE RADAR ON, FIND TARGETS", "RADAR EMITTING"),
                    F("GO SILENT", "RADAR: EMITTING", "STOP EMITTING, HARDER TO DETECT", "RADAR SILENT"), checkbox: false)));
            if (w.HasDrones)
            {
                MenuRow drones = MenuRow.Toggle("defence/drones", "drone-strike", w.DronesStriking,
                    F("SEND DRONES TO STRIKE", w.Drones > 0 ? "DRONES: " + w.Drones + " READY" : "DRONES: NONE READY", "SEND ESCORT DRONES AT THE TARGET", "DRONES STRIKING"),
                    F("SCREEN WITH DRONES", "DRONES: STRIKING", "DRONES RETURN TO SCREEN YOU", "DRONES SCREENING", MenuTone.Caution), checkbox: false);
                drones.IconOn = "drone-screen";
                if (!w.DronesStriking && !w.DronesCanStrike) drones.Blocked = "NO TARGET SELECTED";
                c.Rows.Add(b.Reg(drones));
            }
            if (w.HasFlares && w.Airborne && w.Flares <= 0) Badge(c, "FLR 0", show: true, alert: true);
            else if (w.CountermeasureIndex > 0 && w.Countermeasure.Length > 0) Badge(c, w.Countermeasure.Substring(w.Countermeasure.LastIndexOf(' ') + 1), show: true); // JAMMER
            else if (w.HasRadar && !w.RadarOn) Badge(c, "SILENT", show: true);
            else if (w.HasFlares && w.FlaresMax > 0) Badge(c, "FLR " + w.Flares);
            else Badge(c, w.HasCountermeasures ? "READY" : "");
            return c;
        }

        // ------------------------------------------------------------------ TARGETS

        private static MenuCategory Targets(MenuWorld w, Builder b)
        {
            var c = Cat("targets", "TARGETS", 342f, "0", "SELECTED TARGETS");
            if (!w.HudReady) return c;
            string selected = "SELECTED: " + w.Targets;
            MenuRow keep = MenuRow.Action("targets/keep", "target-keep", "KEEP NEAREST TARGET",
                w.Targets > 0 && w.NearestKm >= 0f ? selected + D + "NEAREST " + MenuText.Distance(w.NearestKm) : selected, "DROP ALL BUT THE NEAREST TARGET", "KEPT NEAREST TARGET");
            if (w.Targets < 2) keep.Blocked = w.Targets == 0 ? "NO TARGETS" : "ONLY ONE TARGET";
            c.Rows.Add(b.Reg(keep));
            MenuRow drop = MenuRow.Action("targets/droplast", "target-drop", "DROP LAST TARGET", selected, "REMOVE THE NEWEST SELECTION", "LAST TARGET DROPPED");
            if (w.Targets == 0) drop.Blocked = "NO TARGETS";
            c.Rows.Add(b.Reg(drop));
            MenuRow clear = MenuRow.Action("targets/clear", "target-clear", "CLEAR ALL TARGETS", selected, "DESELECT EVERY TARGET", "TARGETS CLEARED");
            if (w.Targets == 0) clear.Blocked = "NO TARGETS";
            c.Rows.Add(b.Reg(clear));
            if (w.Filters.Count > 0)
            {
                string active = "NONE";
                var kids = new List<MenuRow>(w.Filters.Count);
                for (int i = 0; i < w.Filters.Count; i++)
                {
                    WorldFilter f = w.Filters[i];
                    string name = MenuText.Upper(f.Name, 14);
                    if (f.Active) active = name;
                    string slot = "SLOT " + (f.Slot > 0 ? f.Slot : i + 1);
                    MenuRow pick = MenuRow.Pick("targets/filters/" + MenuText.Slug(f.Name), "target-filter", name, slot, "ACTIVE" + D + slot, f.Active,
                        "APPLY THIS FILTER TO THE TARGET LIST", "FILTER: " + name);
                    pick.Index = i;
                    if (!f.Allowed) pick.Blocked = "NOT READY";
                    kids.Add(pick);
                }
                c.Rows.Add(b.Reg(MenuRow.Submenu("targets/filters", "target-filter", "TARGET FILTERS", "ACTIVE: " + active, "APPLY A SAVED TARGET FILTER",
                    "TGT QUICK SLOTS", "FILTERS", kids, iconless: true)));
            }
            Badge(c, w.Targets > 0 ? w.Targets + " SEL" : "NO SEL");
            return c;
        }
    }
}
