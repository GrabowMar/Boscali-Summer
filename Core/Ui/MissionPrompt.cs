using System;
using System.Collections.Generic;
using BoscaliSummer.Core.Game;
using BoscaliSummer.Core.Modules;
using NOAvionics;
using TMPro;
using UnityEngine;

namespace BoscaliSummer.Core.Ui
{
    /// <summary>
    /// The mission-start prompt: which Boscali Summer systems run in this mission. It opens on
    /// the map screen once a mission is running and stays out of the cockpit. Systems on the left
    /// by category; on the right a live tally, the hovered system's card and the update notes.
    /// </summary>
    internal sealed class MissionPrompt : MonoBehaviour
    {
        // Five columns plus the side panel show every module and the start row without scrolling at 1080p.
        private const float Width = 1700f, Height = 920f, SideWidth = 420f;
        private const int Columns = 5;

        /// <summary>Module id, the name a pilot knows it by, one line for the tile, the info card text, its icon.</summary>
        private static readonly (string id, string title, string sub, string info, AvIcon icon)[] Labels =
        {
            ("command", "AVIONICS", "MFD panels and tactical map", "The MFD pages and the tactical map: relief map, threat scope, systems and the rail of screens behind the map.", AvIcon.Map2),
            ("autopilot", "AUTOPILOT", "Autopilot and the C menu", "Autopilot modes and the C interaction menu for weapons, drones, wing orders and cockpit shortcuts.", AvIcon.FlightPath),
            ("immersion", "IMMERSION", "G cues, visor, pilot body", "The pilot's body and senses: G-LOC and grey-out, visor, heartbeat, cockpit voices and night lighting.", AvIcon.Pilot),
            ("effects", "EFFECTS", "Display grade, sharpening", "A colour grade and sharpening pass over the game image.", AvIcon.AdjustmentsHorizontal),
            ("qol", "QUALITY OF LIFE", "Small cockpit conveniences", "Small cockpit conveniences that save a few clicks.", AvIcon.Coffee),
            ("cinematography", "CINEMATOGRAPHY", "Cinematic shot tools", "Cutscene cameras for scripted missions and private shot tools for trailers.", AvIcon.Camera),
            ("radio", "RADIO", "Cockpit radio stations", "Music and talk stations you tune from the cockpit.", AvIcon.Radio),
            ("chatter", "CHATTER", "Voices on the radio nets", "Voice packs on the radio nets: tower, flight, AWACS and ground calls that react to the fight.", AvIcon.Headset),
            ("comms", "COMMS", "Map pings, polls, team comms", "Map pings, polls and quick team messages; brevity calls follow the caller.", AvIcon.Message2),
            ("wing", "WING", "Wingmen on your orders", "Your own AI wingmen: they taxi, take off, hold formation and fly the orders you give from the map or the C menu.", AvIcon.Formation),
            ("strategic-command", "STR", "Theater command, war map", "Theater command: a strategic map with striped front lines, offensive arrows and plans you draw yourself.", AvIcon.Territory),
            ("support", "OPS", "Support calls and fronts", "Support calls and the SPACE, CYBER and SOF fronts: strikes, satellites and special operations paid for with power.", AvIcon.CallDown),
            ("high-command", "HIGH COMMAND", "Chain of command, VIP staff", "A chain of command above you: staff officers and VIPs who hand down orders and can be lost.", AvIcon.Crest),
            ("intel", "INTEL", "Fog-of-war threat picture", "Fog of war: you only see the threats your side has detected.", AvIcon.Eye),
            ("theater-ops", "LIVING FRONT", "AI staff steer ground forces", "AI staff steer the ground war: they push fronts and pick objectives on their own.", AvIcon.ChartArrows),
            ("progression", "PROGRESSION", "Perk board", "A perk board you climb by flying and winning.", AvIcon.ChartLine),
            ("squad", "SQUAD", "Squadron and ace hunts", "Your squadron roster and hunts for named enemy aces.", AvIcon.UsersGroup),
            ("dynamic-operations", "CONTRACTS", "Extra objectives in flight", "Extra objectives offered during the mission, paid when you complete them.", AvIcon.Clipboard),
            ("urban-combat", "URBAN COMBAT", "Garrisons, airborne troops", "Garrisons hold towns as strongpoints; airborne troops drop in to take them.", AvIcon.Building),
            ("trenches", "TRENCHES", "Front-line trench lines", "Trench lines dug along the front for ground forces to fight from.", AvIcon.Shield),
            ("fire-and-destruction", "FIRE + DESTRUCTION", "Fire spread, lasting wrecks", "Fires spread through forests and towns, and wrecks stay where they fall.", AvIcon.Flame),
            ("combat", "COMBAT", "Fog, missile failures, smoke", "Less arcade, same rules for both sides: fewer contacts on the shared picture, old contacts drift and fade, some missiles fail, armour pops smoke, ships fire decoys, SAM radars go dark.", AvIcon.Crosshair),
            ("air-survival", "AIR STATIONS", "AI air survival, stations", "AI aircraft survive smarter and hold combat air stations over the front.", AvIcon.Runway),
            ("player-spawn-priority", "SPAWN PRIORITY", "Players get hangars first", "Human players get hangar slots ahead of the AI.", AvIcon.Plane),
            ("vanguard", "VANGUARD", "Extra weapons and gliders", "Extra weapons, drones and gliders: decoys, lasers, recon pods and drone wingmen.", AvIcon.Rocket),
            ("weather", "WEATHER", "Dynamic weather and clouds", "Living weather: fronts drift across the map with rain, storms, cloud layers and fog.", AvIcon.CloudRain),
            ("events", "EVENTS", "World events, modifiers", "World events that shake up the mission with temporary modifiers, such as a supply crisis.", AvIcon.Bolt),
            ("spooktober", "SPOOKTOBER", "Seasonal hauntings", "The seasonal event: a menu of haunted bosses on MISC, the mountain-sized kaiju among them.", AvIcon.Skull),
            ("ares", "ARES", "Game master tools", "Host-only game master console: spawn units, call strikes, set weather and time. Developer access only.", AvIcon.Crown),
            ("hud", "HUD", "TAC BOARD, helmet display", "The TAC BOARD and helmet display every other screen draws on.", AvIcon.Crosshair),
            ("session", "SESSION", "Multiplayer handshake", "The multiplayer handshake that keeps host and clients on the same settings.", AvIcon.Link),
            ("scenario", "SCENARIO", "Mission scripts, campaigns", "Mission scripts, dialogue and the Boscali Summer campaign.", AvIcon.Flag),
            ("pmc", "PMC", "Extra sides for scripts", "Extra sides (PMC, Pirates) and peace for scripted missions.", AvIcon.Coins),
            ("performance", "PERFORMANCE", "Adaptive cosmetic budget", "Watches frame time and trims cosmetic effects when the game struggles.", AvIcon.Activity)
        };

        /// <summary>Categories in reading order. Each is all cockpit or all battlefield, so its caption says which.</summary>
        private static readonly (string title, AvIcon icon, string[] ids)[] Categories =
        {
            ("FLIGHT DECK", AvIcon.Gauge, new[] { "command", "autopilot", "immersion", "effects", "qol" }),
            ("RADIO AND WING", AvIcon.Broadcast, new[] { "radio", "chatter", "comms", "wing" }),
            ("WAR ROOM", AvIcon.Radar2, new[] { "strategic-command", "support", "high-command", "intel", "theater-ops" }),
            ("PILOT CAREER", AvIcon.Star, new[] { "progression", "squad", "dynamic-operations", "player-spawn-priority" }),
            ("BATTLESPACE", AvIcon.Swords, new[] { "urban-combat", "trenches", "fire-and-destruction", "combat", "air-survival", "vanguard" }),
            ("SKY AND WORLD", AvIcon.Cloud, new[] { "weather", "events" }),
            ("SPECIALS", AvIcon.Burst, new[] { "spooktober", "ares", "cinematography" }),
            ("CORE", AvIcon.ShieldLock, new[] { "hud", "session", "scenario", "pmc", "performance" })
        };

        /// <summary>The current release's notes, short. Refresh with each release.</summary>
        private static readonly (string title, string body)[] Notes =
        {
            ("WORLD EFFECTS", "Contrails at altitude, dust wakes on low passes, a crack on near misses, canopy-muffled sound."),
            ("NIGHT", "Darker moonlit nights, cockpit night lighting, goggles that flip down and warm up."),
            ("VANGUARD", "TORCH, SUNSPEAR, STORMCELL, WATCHTOWER and MANTA stores; send drones where you look."),
            ("STR MAP", "Striped fronts with teeth, offensive arrows, and offensives you draw yourself."),
            ("COCKPIT SOUND", "Softer RWR tones and missile alarm, gentler switch clicks."),
            ("PERFORMANCE", "Lighter building damage and smoother distant clouds.")
        };

        private readonly HashSet<string> off = new HashSet<string>(StringComparer.Ordinal);
        private readonly List<AvCell> cells = new List<AvCell>();
        private readonly AvSection[] sections = new AvSection[Categories.Length];
        private AvWindow window;
        private string mission;
        private AvChip modeChip;
        private AvReadout tally;
        private AvHazardBar cockpitLoad, battlefieldLoad;
        private InfoHead infoHead;
        private AvNote infoBody, infoEffect;

        /// <summary>Every module the player may switch, in roster order.</summary>
        internal ModuleMetadata[] Offered = Array.Empty<ModuleMetadata>();

        private void Update()
        {
            string pending = BoscaliMission.PendingMission;
            bool wanted = pending != null && MissionManager.IsRunning && Cursor.lockState != CursorLockMode.Locked;
            if (window != null && (!wanted || pending != mission)) Close();
            if (wanted && window == null) Open(pending);
        }

        private void OnDisable() => Close();

        private void Open(string name)
        {
            mission = name;
            off.Clear();
            cells.Clear();
            foreach (ModuleMetadata module in Offered)
                if (!MissionProfile.AllowsModule(module.Id)) off.Add(module.Id);

            string title = string.IsNullOrEmpty(name) ? "UNTITLED" : name.ToUpperInvariant();
            window = AvWindow.Build(transform, "mission-systems", "MISSION SYSTEMS / " + title, Width, Height, 30010);
            // It opens over the faction screen, whose text would read through glass.
            window.Opaque = true;
            window.CloseControl.Help = "Start with the systems shown.";
            window.Closed += () => Done(false, false);
            var split = window.Body.Add(new Split(window.Body.Content, window.Ticker,
                Width - 2f * AvGridTokens.Pad - AvGridTokens.Gutter, SideWidth));
            BuildSystems(split.Left);
            BuildSide(split.Right, title);
            window.Footer.Set("A battlefield system switched on here joins a mission already under way. F1 > Mission brings this prompt back.");
            RefreshCells();
            window.Show();
        }

        private void BuildSystems(AvFlow body)
        {
            body.Add(new AvNote(body.Content,
                "Choose what Boscali Summer runs in this mission. Cockpit systems leave it as its author made it; battlefield systems change it."));
            body.Buttons(
                new AvControl.Spec("ALL ON / FULL EXPERIENCE", () => Preset(false), icon: AvIcon.CircleCheck),
                new AvControl.Spec("BATTLEFIELD OFF / IMMERSION ONLY", () => Preset(true), icon: AvIcon.Pilot));
            for (int c = 0; c < Categories.Length; c++)
            {
                sections[c] = body.Section(Categories[c].icon, Categories[c].title);
                AvCellGrid grid = body.Grid(Columns);
                foreach (string id in Categories[c].ids)
                {
                    int l = Array.FindIndex(Labels, label => label.id == id);
                    AddCell(grid, id, Labels[l].title, Labels[l].sub, Labels[l].info, Labels[l].icon);
                }
                // Anything new and not yet sorted falls in with the specials.
                if (Categories[c].title == "SPECIALS")
                    foreach (ModuleMetadata module in Offered)
                        if (Array.FindIndex(Labels, label => label.id == module.Id) < 0)
                            AddCell(grid, module.Id, module.Id.Replace('-', ' ').ToUpperInvariant(), module.DisplayName, module.DisplayName, AvIcon.Stack2);
            }
            body.Space(6f);
            body.Buttons(
                new AvControl.Spec("START", () => Done(false, false), AvButtonStyle.Primary, AvIcon.PlayerPlay),
                new AvControl.Spec("START / KEEP FOR THIS MISSION", () => Done(true, false), icon: AvIcon.Bookmark),
                new AvControl.Spec("START / NEVER ASK AGAIN", () => Done(false, true), icon: AvIcon.Lock));
        }

        private void BuildSide(AvFlow side, string title)
        {
            modeChip = new AvChip(side.Content);
            var version = new AvChip(side.Content);
            version.Set("BOSCALI SUMMER " + Plugin.PluginVersion, AvState.Info);
            side.Row(modeChip, version);
            tally = side.Add(new AvReadout(side.Content));
            cockpitLoad = side.Add(new AvHazardBar(side.Content, "COCKPIT SIDE"));
            cockpitLoad.Help = "Cockpit systems running. They leave the mission as its author made it.";
            battlefieldLoad = side.Add(new AvHazardBar(side.Content, "BATTLEFIELD SIDE"));
            battlefieldLoad.Help = "Battlefield systems running. Each one changes the mission.";

            var info = side.Add(new AvCard(side.Content, side.Ticker, side.Inner, "SYSTEM CARD"));
            infoHead = info.Flow.Add(new InfoHead(info.Flow.Content));
            infoBody = info.Flow.Add(new AvNote(info.Flow.Content));
            infoEffect = info.Flow.Add(new AvNote(info.Flow.Content, AvTextRole.DataSmall));
            infoHead.Set(AvIcon.InfoCircle, title, "HOVER A SYSTEM", false);
            infoBody.Set("Hover any system on the left to read what it does and whether it changes the mission.");
            infoEffect.Set("");

            var notes = side.Add(new AvCard(side.Content, side.Ticker, side.Inner, "WHAT'S NEW"));
            foreach (var note in Notes)
            {
                notes.Flow.Add(new AvNote(notes.Flow.Content, AvTextRole.Label, true)).Set(note.title);
                notes.Flow.Add(new AvNote(notes.Flow.Content)).Set(note.body);
            }
        }

        private void AddCell(AvCellGrid grid, string id, string title, string sub, string info, AvIcon icon)
        {
            AvCell cell;
            if (MissionProfile.IsCore(id))
            {
                // Running and locked: a click only shows its card.
                cell = grid.Toggle(title, sub, () => true, _ => ShowInfo(id, title, info, icon), icon, "CORE");
                cell.Help = "Always on: other systems lean on it.";
            }
            else if (Array.FindIndex(Offered, module => module.Id == id) < 0)
            {
                cell = grid.Toggle(title, sub, () => false, null, icon, offWord: "F1 OFF");
                cell.Interactable = false;
                cell.Help = "Switched off in F1 > Modules; switch it on there to offer it here.";
                return;
            }
            else cells.Add(cell = grid.Toggle(title, sub, () => !off.Contains(id), on => { Set(id, on); RefreshCells(); ShowInfo(id, title, info, icon); }, icon));
            cell.Hovered = h => { if (h) ShowInfo(id, title, info, icon); };
        }

        private void ShowInfo(string id, string title, string info, AvIcon icon)
        {
            bool core = MissionProfile.IsCore(id), on = core || !off.Contains(id);
            infoHead.Set(icon, title, core ? "CORE / ALWAYS ON" : on ? "ON IN THIS MISSION" : "OFF IN THIS MISSION", on);
            bool battlefield = MissionProfile.IsBattlefield(id);
            infoBody.Set(info + (core ? " Other systems lean on it, so it cannot be switched off."
                : battlefield ? " Switched on mid-mission, it joins the fight under way." : ""));
            infoEffect.Set(core ? "ALWAYS ON" : battlefield ? "CHANGES THE MISSION" : "LEAVES THE MISSION ALONE",
                core ? AvState.Info : battlefield ? AvState.Caution : AvState.Ready);
        }

        /// <summary>Switching a module on brings what it needs; switching one off takes what needs it.</summary>
        private void Set(string id, bool on)
        {
            if (on ? !off.Remove(id) : !off.Add(id)) return;
            foreach (ModuleMetadata module in Offered)
            {
                if (on && module.Id == id)
                    foreach (string dependency in module.Dependencies) Set(dependency, true);
                if (!on && Array.IndexOf(module.Dependencies, id) >= 0) Set(module.Id, false);
            }
        }

        private void Preset(bool immersionOnly)
        {
            off.Clear();
            if (immersionOnly)
                foreach (ModuleMetadata module in Offered)
                    if (MissionProfile.IsBattlefield(module.Id)) off.Add(module.Id);
            RefreshCells();
        }

        private bool Offers(string id) => Array.FindIndex(Offered, module => module.Id == id) >= 0;

        private void RefreshCells()
        {
            foreach (AvCell cell in cells) cell.Refresh();
            int allOn = 0, allTotal = 0, battlefieldOff = 0, battlefieldTotal = 0;
            for (int c = 0; c < Categories.Length; c++)
            {
                string[] ids = Categories[c].ids;
                if (MissionProfile.IsCore(ids[0]))
                {
                    sections[c]?.SetCaption(ids.Length + " ALWAYS ON / EVERYTHING ELSE LEANS ON THEM");
                    continue;
                }
                int total = 0, running = 0;
                foreach (string id in ids)
                {
                    if (!Offers(id)) continue;
                    total++;
                    if (!off.Contains(id)) running++;
                }
                sections[c]?.SetCaption(running + " OF " + total + " ON / " +
                    (MissionProfile.IsBattlefield(ids[0]) ? "CHANGES THE MISSION" : "LEAVES THE MISSION ALONE"));
            }
            foreach (ModuleMetadata module in Offered)
            {
                allTotal++;
                if (!off.Contains(module.Id)) allOn++;
                if (!MissionProfile.IsBattlefield(module.Id)) continue;
                battlefieldTotal++;
                if (off.Contains(module.Id)) battlefieldOff++;
            }
            bool full = off.Count == 0, immersion = !full && battlefieldOff == battlefieldTotal && off.Count == battlefieldOff;
            modeChip?.Set(full ? "FULL EXPERIENCE" : immersion ? "IMMERSION ONLY" : "CUSTOM MIX",
                full ? AvState.Ready : immersion ? AvState.Info : AvState.Caution);
            tally?.Set(allOn + " / " + allTotal, "SYSTEMS ON",
                off.Count == 0 ? "NOTHING SWITCHED OFF" : off.Count + " SWITCHED OFF FOR THIS MISSION");
            int cockpitOn = allOn - (battlefieldTotal - battlefieldOff), cockpitTotal = allTotal - battlefieldTotal;
            cockpitLoad?.Set(cockpitOn / (float)Mathf.Max(1, cockpitTotal), cockpitOn + " / " + cockpitTotal,
                cockpitOn == cockpitTotal ? AvState.Ready : AvState.Caution);
            int battlefieldOn = battlefieldTotal - battlefieldOff;
            battlefieldLoad?.Set(battlefieldOn / (float)Mathf.Max(1, battlefieldTotal), battlefieldOn + " / " + battlefieldTotal,
                battlefieldOn == battlefieldTotal ? AvState.Ready : battlefieldOn == 0 ? AvState.Info : AvState.Caution);
        }

        private void Done(bool keepForMission, bool neverAsk)
        {
            if (window == null) return;
            BoscaliMission.Confirm(mission, off, keepForMission, neverAsk);
            Close();
        }

        private void Close()
        {
            if (window == null) return;
            AvWindow closing = window;
            window = null;
            closing.Hide();
            Destroy(closing.Root.gameObject);
        }

        /// <summary>Two flows side by side: the systems list and a fixed-width side panel.</summary>
        private sealed class Split : AvPart
        {
            private readonly Column left, right;
            private readonly float rightWidth;

            public Split(RectTransform parent, AvTicker ticker, float width, float rightWidth)
            {
                Rect = AvLay.Child(parent, "Split");
                this.rightWidth = rightWidth;
                left = new Column(Rect, ticker, width - rightWidth - AvGridTokens.Gap) { Parent = this };
                right = new Column(Rect, ticker, rightWidth) { Parent = this };
            }

            public AvFlow Left => left.Flow;
            public AvFlow Right => right.Flow;

            public override float Measure(float width) =>
                Mathf.Max(left.Measure(width - rightWidth - AvGridTokens.Gap), right.Measure(rightWidth));

            public override void Place(AvSlot s)
            {
                base.Place(s);
                float leftWidth = s.W - rightWidth - AvGridTokens.Gap;
                left.Place(new AvSlot(0f, 0f, leftWidth, s.H));
                right.Place(new AvSlot(leftWidth + AvGridTokens.Gap, 0f, rightWidth, s.H));
            }
        }

        /// <summary>A frameless nested flow.</summary>
        private sealed class Column : AvPart
        {
            public Column(RectTransform parent, AvTicker ticker, float width)
            {
                Rect = AvLay.Child(parent, "Column");
                Flow = new AvFlow(Rect, ticker, width, 0f) { Host = this };
            }

            public AvFlow Flow { get; }

            public override float Measure(float width)
            {
                Flow.Relayout();
                return Flow.ContentHeight;
            }
        }

        /// <summary>The system card's head: a large icon lit with the system, its name and its state.</summary>
        private sealed class InfoHead : AvPart
        {
            private const float Glyph = 40f;
            private readonly TMP_Text glyph, title, line;
            private bool lit;

            public InfoHead(RectTransform parent)
            {
                Rect = AvLay.Child(parent, "InfoHead");
                glyph = AvIcons.Make(Rect, AvIcon.InfoCircle, Glyph, Color.clear);
                title = AvText.Make(Rect, "Title", AvTextRole.Title, "");
                AvText.Fit(title, false);
                line = AvText.Make(Rect, "Line", AvTextRole.DataSmall, "");
                Restyle();
            }

            public void Set(AvIcon icon, string titleText, string lineText, bool on)
            {
                AvIcons.Set(glyph, icon, Glyph);
                AvText.Set(title, titleText);
                AvText.Set(line, lineText);
                lit = on;
                Restyle();
            }

            public override float Measure(float width) => Glyph + 8f;

            public override void Place(AvSlot s)
            {
                base.Place(s);
                float x = Glyph + 12f;
                AvLay.Place(glyph.rectTransform, 0f, 2f, Glyph, Glyph);
                AvLay.Place(title.rectTransform, x, 2f, s.W - x, 24f);
                AvLay.Place(line.rectTransform, x, 28f, s.W - x, 16f);
            }

            public override void Restyle()
            {
                Color on = AvStyleHost.FuiFill("cell-led", AvTheme.RailInert, "on");
                glyph.color = lit ? on : AvStyleHost.FuiInk("cell-sub", AvTheme.Dim);
                title.color = AvStyleHost.FuiInk("title", AvTheme.TextPrimary);
                line.color = lit ? on : AvStyleHost.FuiInk("cell-state", AvTheme.Disabled);
            }
        }
    }
}
