using System;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Modules.Autopilot.Domain;

namespace BoscaliSummer.Modules.Autopilot.Runtime
{
    /// <summary>
    /// Boscali integrations, each through its owner's narrow contract: the camera mark and armed support
    /// (<see cref="ICameraTargetService"/>), OPS perks (<see cref="IOpsPerks"/>), SKYWELL, squad exfil, the local radio
    /// (<see cref="IRadioRemote"/>), team brevity calls (<see cref="IQuickCalls"/>), the wing (<see cref="IWingOrders"/>), the TAC BOARD options
    /// (<see cref="ITacBoardSettings"/>) and the contributed TGT preset page (<see cref="IRadialMenuPage"/>). A missing owner leaves its fields at their
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

        /// <summary>The TAC BOARD's two VIEW rows. The cycle index is the contract enum's value, in the order of <c>MenuCatalog.BoardDeclutters</c>.</summary>
        private static void ReadBoard(MenuWorld w)
        {
            ITacBoardSettings board = Service<ITacBoardSettings>();
            if (board == null) return;
            w.BoardAvailable = true;
            w.BoardOn = board.Enabled;
            w.BoardDeclutter = (int)board.Declutter;
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
                case "radio/music": return () => Service<IRadioRemote>()?.DeckTogglePlayback();
                case "radio/trknext": return () => Service<IRadioRemote>()?.DeckNext();
                case "radio/trkprev": return () => Service<IRadioRemote>()?.DeckPrevious();
                case "radio/receiver": return () => Service<IRadioRemote>()?.ReceiverToggle();
                case "radio/seek": return () => Service<IRadioRemote>()?.ReceiverSeek(1);
                case "radio/stopall": return () => Service<IRadioRemote>()?.StopAll();
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
                case "view/board": return OnBoard(board => board.Enabled = !board.Enabled);
                case "view/declutter": return OnBoard(board => board.Declutter = (BoardDeclutter)(((int)board.Declutter + 1) % MenuCatalog.BoardDeclutters.Length));
                case "defence/drones": return StrikeOrScreen;
            }
            if (row.Id.StartsWith("comms/c", StringComparison.Ordinal)) return () => Service<IQuickCalls>()?.Call(index);
            if (row.Id.StartsWith("support/pit/station/", StringComparison.Ordinal)) return () => Service<ISkywellControl>()?.PrioritiseStation(index);
            const string Perks = "support/perks/", Filters = "targets/filters/", Shapes = "wing/shape/";
            if (row.Id.StartsWith(Perks, StringComparison.Ordinal)) return BySlug<IOpsPerks>(row.Id, Perks, p => p.PerkCount, (p, i) => p.PerkLabel(i), (p, i) => p.PerkPress(i));
            if (row.Id.StartsWith(Filters, StringComparison.Ordinal))
                return BySlug<IRadialMenuPage>(row.Id, Filters, p => Math.Min(p.EntryCount, MaxFilters), (p, i) => FilterName(p.EntryLabel(i) ?? ""), (p, i) => p.InvokeEntry(i));
            if (row.Id.StartsWith(Shapes, StringComparison.Ordinal)) return BySlug<IWingOrders>(row.Id, Shapes, o => o.ShapeCount, (o, i) => o.ShapeLabel(i), (o, i) => o.SelectShape(i));
            return null;
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
