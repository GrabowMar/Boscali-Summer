using System;
using System.Collections.Generic;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Modules.Autopilot.Domain;

namespace BoscaliSummer.Modules.Autopilot.Runtime
{
    /// <summary>
    /// Boscali integrations, each through its owner's narrow contract: the camera mark and armed support
    /// (<see cref="ICameraTargetService"/>), OPS perks (<see cref="IOpsPerks"/>), SKYWELL, squad exfil, the local radio
    /// (<see cref="IRadioRemote"/>), team brevity calls (<see cref="IQuickCalls"/>), the wing (<see cref="IWingOrders"/>), the TAC BOARD options
    /// (<see cref="ITacBoardSettings"/>), the cabin effects (<see cref="IImmersionSettings"/>), the kneeboard (<see cref="IKneeboardGallery"/>), the visor
    /// terrain (<see cref="IVisorTerrainSettings"/>) and the contributed TGT preset page (<see cref="IRadialMenuPage"/>). A missing owner leaves its fields at their
    /// defaults, so the pure catalog hides the rows.
    /// </summary>
    internal static partial class MenuGame
    {
        private const int MaxCalls = 12, MaxFilters = 8;
        private const string FilterOn = " [ON]";

        private static void ReadSupport(MenuWorld w)
        {
            ICameraTargetService camera = Service<ICameraTargetService>();
            if (camera != null && camera.Available)
            {
                w.MarkAvailable = true;
                w.HasMark = camera.HasMark;
                w.MarkAgeSeconds = camera.AgeSeconds;
                w.CanCapture = camera.CanCapture;
                w.CanCallAtMark = camera.CanCallAtMark;
                w.Armed = MenuText.Upper(camera.ArmedActionName, 18);
            }
            IOpsPerks perks = Service<IOpsPerks>();
            if (perks != null)
            {
                w.PerksAvailable = true;
                int count = Math.Min(perks.PerkCount, IOpsPerks.MaxPerks);
                for (int i = 0; i < count; i++)
                {
                    string label = perks.PerkLabel(i);
                    if (string.IsNullOrEmpty(label)) continue;
                    w.Perks.Add(new WorldPerk { Label = label, Note = MenuText.Upper(perks.PerkNote(i), 30), Armed = perks.PerkArmed(i), Front = perks.PerkFront(i) ?? "", Cooldown = perks.PerkCooldown(i) });
                }
            }
            ISkywellControl kit = Service<ISkywellControl>();
            if (kit != null && kit.Carried) { w.SkywellCarried = true; w.SkywellDeployed = kit.Deployed; w.SkywellStock = kit.Stock ?? ""; }
            if (kit != null && kit.PitAvailable)
            {
                w.PitAvailable = true;
                w.PitMode = kit.PitMode;
                for (int i = 0, n = Math.Min(kit.PitStationCount, 8); i < n; i++)
                {
                    int station = kit.PitStationAt(i);
                    string label = kit.PitStationLabel(station);
                    if (station < 0 || string.IsNullOrEmpty(label)) continue;
                    string state = kit.PitStationStatus(station) ?? "";
                    w.PitStations.Add(new WorldPitStation { Index = station, Label = label, State = state, First = state.EndsWith("FIRST", StringComparison.Ordinal) });
                }
            }
            w.CanExfil = Service<IAirAssaultOrders>()?.CanRequestExfil == true;
        }

        /// <summary>One on/off option of an owner, by the slug the catalog's switch tables name it with.</summary>
        private sealed class Flag<T>
        {
            public Flag(Func<T, bool> get, Action<T, bool> set) { Get = get; Set = set; }
            public Func<T, bool> Get { get; }
            public Action<T, bool> Set { get; }
        }

        private static readonly Dictionary<string, Flag<ITacBoardSettings>> BoardFlags = new Dictionary<string, Flag<ITacBoardSettings>>
        {
            ["parts/stores"] = new Flag<ITacBoardSettings>(b => b.ShowStores, (b, v) => b.ShowStores = v),
            ["parts/pages"] = new Flag<ITacBoardSettings>(b => b.ScopeMode != BoardScopeMode.Off, (b, v) => b.ScopeMode = v ? BoardScopeMode.Manual : BoardScopeMode.Off),
            ["parts/status"] = new Flag<ITacBoardSettings>(b => b.ShowLamps || b.ShowGauges, (b, v) => b.ShowLamps = b.ShowGauges = v),
            ["parts/notices"] = new Flag<ITacBoardSettings>(b => b.ShowTicker, (b, v) => b.ShowTicker = v),
            ["parts/corners"] = new Flag<ITacBoardSettings>(b => b.Corners, (b, v) => b.Corners = v),
        };

        private static readonly Dictionary<string, Flag<IImmersionSettings>> FeelFlags = new Dictionary<string, Flag<IImmersionSettings>>
        {
            ["master"] = new Flag<IImmersionSettings>(s => s.IsEnabled, (s, v) => s.IsEnabled = v),
            ["head-motion"] = new Flag<IImmersionSettings>(s => s.HeadMotionEnabled, (s, v) => s.HeadMotionEnabled = v),
            ["shake"] = new Flag<IImmersionSettings>(s => s.ExtraShakeEnabled, (s, v) => s.ExtraShakeEnabled = v),
            ["glare"] = new Flag<IImmersionSettings>(s => s.SunGlareEnabled, (s, v) => s.SunGlareEnabled = v),
            ["visor"] = new Flag<IImmersionSettings>(s => s.HelmetVisorEnabled, (s, v) => s.HelmetVisorEnabled = v),
            ["breathing"] = new Flag<IImmersionSettings>(s => s.PilotBreathingEnabled, (s, v) => s.PilotBreathingEnabled = v),
            ["wind"] = new Flag<IImmersionSettings>(s => s.WindAudioEnabled, (s, v) => s.WindAudioEnabled = v),
            ["airframe"] = new Flag<IImmersionSettings>(s => s.AirframeAudioEnabled, (s, v) => s.AirframeAudioEnabled = v),
            ["buffet"] = new Flag<IImmersionSettings>(s => s.MachBuffetEnabled, (s, v) => s.MachBuffetEnabled = v),
            ["mfd-glow"] = new Flag<IImmersionSettings>(s => s.MfdGlowEnabled, (s, v) => s.MfdGlowEnabled = v),
        };

        /// <summary>The HUD category: the TAC BOARD's options. The cycle indices are the contract enums' values, in the order of <c>MenuCatalog.BoardDeclutters</c> and <c>BoardLayouts</c>.</summary>
        private static void ReadBoard(MenuWorld w)
        {
            ITacBoardSettings board = Service<ITacBoardSettings>();
            if (board == null) return;
            w.BoardAvailable = true;
            w.BoardOn = board.Enabled;
            w.BoardDeclutter = (int)board.Declutter;
            w.BoardLayout = (int)board.Layout;
            w.BoardShade = MenuCatalog.Nearest(MenuCatalog.ShadeSteps, board.Shade);
            w.BoardSize = MenuCatalog.Nearest(MenuCatalog.SizeSteps, board.Scale);
            w.BoardPageCount = board.PageCount;
            w.BoardPage1 = board.Page1; w.BoardPage2 = board.Page2; w.BoardPage3 = board.Page3; w.BoardPage4 = board.Page4;
            w.BoardPagesOn = board.ScopeMode != BoardScopeMode.Off;
            w.BoardSway = board.Sway;
            foreach (KeyValuePair<string, Flag<ITacBoardSettings>> flag in BoardFlags) if (flag.Value.Get(board)) w.BoardSwitches.Add(flag.Key);
        }

        /// <summary>The cabin: the immersion effects, the kneeboard card, and the visor's terrain pictures (listed under VIEW).</summary>
        private static void ReadCabin(MenuWorld w)
        {
            IImmersionSettings feel = Service<IImmersionSettings>();
            if (feel != null)
            {
                w.FeelAvailable = true;
                foreach (KeyValuePair<string, Flag<IImmersionSettings>> flag in FeelFlags) if (flag.Value.Get(feel)) w.Feel.Add(flag.Key);
            }
            IKneeboardGallery cards = Service<IKneeboardGallery>();
            if (cards != null && cards.Count > 1) { w.KneeboardAvailable = true; w.KneeboardCard = cards.Name(cards.Selected) ?? ""; }
            IVisorTerrainSettings terrain = Service<IVisorTerrainSettings>();
            if (terrain != null)
            {
                w.TerrainAvailable = true;
                w.HoloMap = terrain.HoloMap;
                w.TerrainGhost = MenuCatalog.Nearest(MenuCatalog.GhostSteps, terrain.TerrainGhost);
            }
        }

        private static void ReadRadio(MenuWorld w)
        {
            IRadioRemote radio = Service<IRadioRemote>();
            if (radio == null) return;
            w.DeckAvailable = radio.DeckAvailable;
            w.DeckPlaying = radio.DeckPlaying;
            w.DeckTitle = radio.DeckTitle ?? "";
            w.ReceiverAvailable = radio.ReceiverAvailable;
            w.ReceiverOn = radio.ReceiverOn;
            w.Station = radio.ReceiverStation ?? "";
            w.CanStopAll = radio.DeckPlaying || radio.ReceiverOn;
        }

        private static void ReadComms(MenuWorld w)
        {
            IQuickCalls calls = Service<IQuickCalls>();
            if (calls == null) return;
            w.CallsAvailable = true;
            w.CanCall = calls.CanCall;
            int count = Math.Min(calls.CallCount, MaxCalls);
            for (int i = 0; i < count; i++) w.CallLabels.Add(calls.CallLabel(i) ?? "");
        }

        private static void ReadWing(MenuWorld w)
        {
            IWingOrders wing = Service<IWingOrders>();
            if (wing == null) return;
            w.Wingmen = wing.Members;
            w.Shape = MenuText.Upper(wing.Shape, 40);
            for (int i = 0, n = wing.ShapeCount; i < n; i++)
                w.Shapes.Add(new WorldShape { Name = wing.ShapeLabel(i), Family = wing.ShapeFamily(i), Active = wing.ShapeActive(i) });
        }

        /// <summary>The TGT quick slots (Command's page), listed under TARGETS as TARGET FILTERS.</summary>
        private static void ReadFilters(MenuWorld w)
        {
            IRadialMenuPage page = Service<IRadialMenuPage>();
            if (page == null) return;
            int count = Math.Min(page.EntryCount, MaxFilters);
            for (int i = 0; i < count; i++)
            {
                string label = page.EntryLabel(i);
                if (string.IsNullOrEmpty(label)) continue;
                w.Filters.Add(new WorldFilter { Name = FilterName(label), Active = label.EndsWith(FilterOn, StringComparison.Ordinal), Allowed = page.EntryAllowed(i), Slot = i + 1 });
            }
        }

        private static string FilterName(string label) => label.EndsWith(FilterOn, StringComparison.Ordinal) ? label.Substring(0, label.Length - FilterOn.Length) : label;

        /// <summary>
        /// Binds a row to an owner list entry by the id slug of its label, looked up when the row is PRESSED: the owner's list shifts as perks come off cooldown or
        /// formations stop suiting the wing, and a closure bound to a build-time index would press a neighbour (the wrong, costly perk).
        /// </summary>
        private static Action BySlug<T>(string id, string prefix, Func<T, int> count, Func<T, int, string> label, Action<T, int> press) where T : class
        {
            string slug = id.Substring(prefix.Length);
            return () =>
            {
                T owner = Service<T>();
                if (owner == null) return;
                for (int i = 0, n = count(owner); i < n; i++)
                    if (MenuText.Slug(label(owner, i) ?? "") == slug) { press(owner, i); return; }
            };
        }

        private static Action BindContracts(MenuRow row)
        {
            int index = row.Index;
            switch (row.Id)
            {
                case "wing/formup": return () => Service<IWingOrders>()?.FormUp();
                case "wing/engage": return () => Service<IWingOrders>()?.Engage();
                case "wing/breakoff": return () => Service<IWingOrders>()?.BreakOff();
                case "wing/six": return () => Service<IWingOrders>()?.ClearMySix();
                case "wing/dope": return () => Service<IWingOrders>()?.BogeyDope();
                case "wing/escort": return () => Service<IWingOrders>()?.EscortMe();
                case "wing/rtb": return () => Service<IWingOrders>()?.ReturnToBase();
                case "cockpit/music": return () => Service<IRadioRemote>()?.DeckTogglePlayback();
                case "cockpit/trknext": return () => Service<IRadioRemote>()?.DeckNext();
                case "cockpit/trkprev": return () => Service<IRadioRemote>()?.DeckPrevious();
                case "cockpit/receiver": return () => Service<IRadioRemote>()?.ReceiverToggle();
                case "cockpit/seek": return () => Service<IRadioRemote>()?.ReceiverSeek(1);
                case "cockpit/stopall": return () => Service<IRadioRemote>()?.StopAll();
                case "cockpit/kneeboard": return () => { IKneeboardGallery cards = Service<IKneeboardGallery>(); if (cards != null) cards.Selected = cards.Selected + 1; };
                case "view/holo": return () => { IVisorTerrainSettings t = Service<IVisorTerrainSettings>(); if (t != null) t.HoloMap = !t.HoloMap; };
                case "view/ghost": return () => { IVisorTerrainSettings t = Service<IVisorTerrainSettings>(); if (t != null) t.TerrainGhost = Next(MenuCatalog.GhostSteps, t.TerrainGhost); };
                case "support/mark": return () => Service<ICameraTargetService>()?.Capture();
                case "support/callmark": return () => Service<ICameraTargetService>()?.CallAtMark();
                case "support/clearmark": return () => Service<ICameraTargetService>()?.Clear();
                case "support/skywell": return () => Service<ISkywellControl>()?.Toggle();
                case "support/pit/both": return () => Service<ISkywellControl>()?.SetPitMode(0);
                case "support/pit/fuel": return () => Service<ISkywellControl>()?.SetPitMode(1);
                case "support/pit/mun": return () => Service<ISkywellControl>()?.SetPitMode(2);
                case "support/pit/auto": return () => Service<ISkywellControl>()?.PrioritiseStation(-1);
                case "support/exfil": return () => Service<IAirAssaultOrders>()?.RequestExfil();
                case "view/mslview": return () => Service<IMissileView>()?.Enter();
                case "view/mslnext": return () => Service<IMissileView>()?.Next();
                case "view/mslprev": return () => Service<IMissileView>()?.Prev();
                case "view/mslexit": return () => Service<IMissileView>()?.Exit();
                case "hud/board": return OnBoard(board => board.Enabled = !board.Enabled);
                case "hud/declutter": return OnBoard(board => board.Declutter = (BoardDeclutter)(((int)board.Declutter + 1) % MenuCatalog.BoardDeclutters.Length));
                case "hud/layout": return OnBoard(board => board.Layout = (BoardLayout)(((int)board.Layout + 1) % MenuCatalog.BoardLayouts.Length));
                case "hud/shade": return OnBoard(board => board.Shade = Next(MenuCatalog.ShadeSteps, board.Shade));
                case "hud/size": return OnBoard(board => board.Scale = Next(MenuCatalog.SizeSteps, board.Scale));
                case "hud/count": return OnBoard(board => board.PageCount = board.PageCount % MenuCatalog.BoardPageCounts.Length + 1);
                case "hud/sway": return OnBoard(board => board.Sway = !board.Sway);
                case "hud/reset": return OnBoard(board => board.ResetToDefaults());
                case "defence/drones": return StrikeOrScreen;
                case "weapons/drones/hand": return () => Service<IDroneCommand>()?.ToggleHand();
                case "weapons/drones/nose": return () => Service<IDroneCommand>()?.MatchNose();
                case "weapons/drones/left": return () => Service<IDroneCommand>()?.Turn(-45f);
                case "weapons/drones/right": return () => Service<IDroneCommand>()?.Turn(45f);
                case "weapons/drones/next": return () => Service<IDroneCommand>()?.Cycle(1);
                case "weapons/drones/prev": return () => Service<IDroneCommand>()?.Cycle(-1);
                case "weapons/drones/feed": return () => Service<IDroneCommand>()?.ToggleFeed();
                case "weapons/drones/terminal": return () => Service<IDroneCommand>()?.OpenTerminal();
            }
            if (row.Id.StartsWith("comms/c", StringComparison.Ordinal)) return () => Service<IQuickCalls>()?.Call(index);
            if (row.Id.StartsWith("weapons/drones/pick/", StringComparison.Ordinal)) return () => Service<IDroneCommand>()?.Link(index);
            if (row.Id.StartsWith("support/pit/station/", StringComparison.Ordinal)) return () => Service<ISkywellControl>()?.PrioritiseStation(index);
            const string Page1 = "hud/page1/", Page2 = "hud/page2/", Page3 = "hud/page3/", Page4 = "hud/page4/", Parts = "hud/parts/", Feel = "cockpit/feel/";
            int slot = row.Id.StartsWith(Page1, StringComparison.Ordinal) ? 1 : row.Id.StartsWith(Page2, StringComparison.Ordinal) ? 2 :
                row.Id.StartsWith(Page3, StringComparison.Ordinal) ? 3 : row.Id.StartsWith(Page4, StringComparison.Ordinal) ? 4 : 0;
            if (slot != 0) return OnBoard(board => SelectBoardPage(board, slot, (BoardPage)index));
            if (row.Id.StartsWith(Parts, StringComparison.Ordinal) && BoardFlags.TryGetValue(row.Id.Substring(4), out Flag<ITacBoardSettings> part))
                return OnBoard(board => part.Set(board, !part.Get(board)));
            if (row.Id.StartsWith(Feel, StringComparison.Ordinal) && FeelFlags.TryGetValue(row.Id.Substring(Feel.Length), out Flag<IImmersionSettings> effect))
                return () => { IImmersionSettings feel = Service<IImmersionSettings>(); if (feel != null) effect.Set(feel, !effect.Get(feel)); };
            const string Perks = "support/perks/", Filters = "targets/filters/", Shapes = "wing/shape/";
            if (row.Id.StartsWith(Perks, StringComparison.Ordinal)) return BySlug<IOpsPerks>(row.Id, Perks, p => p.PerkCount, (p, i) => p.PerkLabel(i), (p, i) => p.PerkPress(i));
            if (row.Id.StartsWith(Filters, StringComparison.Ordinal))
                return BySlug<IRadialMenuPage>(row.Id, Filters, p => Math.Min(p.EntryCount, MaxFilters), (p, i) => FilterName(p.EntryLabel(i) ?? ""), (p, i) => p.InvokeEntry(i));
            if (row.Id.StartsWith(Shapes, StringComparison.Ordinal)) return BySlug<IWingOrders>(row.Id, Shapes, o => o.ShapeCount, (o, i) => o.ShapeLabel(i), (o, i) => o.SelectShape(i));
            return null;
        }

        /// <summary>The value of the step after the one nearest <paramref name="value"/>, wrapping: what a press of a cycle row over a float option sets.</summary>
        private static float Next(float[] steps, float value) => steps[(MenuCatalog.Nearest(steps, value) + 1) % steps.Length];

        /// <summary>Keep all saved slots distinct, including hidden slots that a later count change may reveal.</summary>
        private static void SelectBoardPage(ITacBoardSettings board, int slot, BoardPage page)
        {
            if (slot > board.PageCount) return;
            BoardPage[] pages = { board.Page1, board.Page2, board.Page3, board.Page4 };
            for (int i = 0; i < pages.Length; i++)
                if (i != slot - 1 && pages[i] == page) { pages[i] = pages[slot - 1]; break; }
            pages[slot - 1] = page;
            board.Page1 = pages[0]; board.Page2 = pages[1]; board.Page3 = pages[2]; board.Page4 = pages[3];
        }

        /// <summary>Statement over the TAC BOARD options, looked up again when the row is pressed.</summary>
        private static Action OnBoard(Action<ITacBoardSettings> run) =>
            () => { ITacBoardSettings board = Service<ITacBoardSettings>(); if (board != null) run(board); };

        private static void StrikeOrScreen()
        {
            IDroneCommand drones = Service<IDroneCommand>();
            if (drones == null) return;
            if (drones.Striking) drones.Screen(); else drones.Strike();
        }
    }
}
