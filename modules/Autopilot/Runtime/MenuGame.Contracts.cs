using System;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Modules.Autopilot.Domain;

namespace BoscaliSummer.Modules.Autopilot.Runtime
{
    /// <summary>
    /// Boscali integrations, each through its owner's narrow contract: the camera mark and armed support
    /// (<see cref="ICameraTargetService"/>), OPS perks (<see cref="IOpsPerks"/>), SKYWELL, squad exfil, the local radio
    /// (<see cref="IRadioRemote"/>), team brevity calls (<see cref="IQuickCalls"/>), the wing (<see cref="IWingOrders"/>) and the contributed
    /// TGT preset page (<see cref="IRadialMenuPage"/>). A missing owner leaves its fields at their defaults, so the pure catalog hides the rows.
    /// </summary>
    internal static partial class MenuGame
    {
        private const int MaxCalls = 12;
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
                    w.Perks.Add(new WorldPerk { Label = label, Note = MenuText.Upper(perks.PerkNote(i), 30), Armed = perks.PerkArmed(i) });
                }
            }
            ISkywellControl kit = Service<ISkywellControl>();
            if (kit != null && kit.Carried) { w.SkywellCarried = true; w.SkywellDeployed = kit.Deployed; w.SkywellStock = kit.Stock ?? ""; }
            w.CanExfil = Service<IAirAssaultOrders>()?.CanRequestExfil == true;
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
            w.Shape = MenuText.Upper(wing.Shape, 18);
        }

        /// <summary>The TGT quick slots (Command's page), listed under TARGETS as TARGET FILTERS.</summary>
        private static void ReadFilters(MenuWorld w)
        {
            IRadialMenuPage page = Service<IRadialMenuPage>();
            if (page == null) return;
            int count = Math.Min(page.EntryCount, 8);
            for (int i = 0; i < count; i++)
            {
                string label = page.EntryLabel(i);
                if (string.IsNullOrEmpty(label)) continue;
                bool active = label.EndsWith(FilterOn, StringComparison.Ordinal);
                w.Filters.Add(new WorldFilter { Name = active ? label.Substring(0, label.Length - FilterOn.Length) : label, Active = active, Allowed = page.EntryAllowed(i) });
            }
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
                case "wing/shape": return () => Service<IWingOrders>()?.NextShape();
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
                case "support/exfil": return () => Service<IAirAssaultOrders>()?.RequestExfil();
                case "view/mslview": return () => Service<IMissileView>()?.Enter();
                case "view/mslnext": return () => Service<IMissileView>()?.Next();
                case "view/mslprev": return () => Service<IMissileView>()?.Prev();
                case "view/mslexit": return () => Service<IMissileView>()?.Exit();
                case "defence/drones": return StrikeOrScreen;
            }
            if (row.Id.StartsWith("comms/c", StringComparison.Ordinal)) return () => Service<IQuickCalls>()?.Call(index);
            if (row.Id.StartsWith("support/perks/", StringComparison.Ordinal)) return () => Service<IOpsPerks>()?.PerkPress(index);
            if (row.Id.StartsWith("targets/filters/", StringComparison.Ordinal)) return () => Service<IRadialMenuPage>()?.InvokeEntry(index);
            return null;
        }

        private static void StrikeOrScreen()
        {
            IDroneCommand drones = Service<IDroneCommand>();
            if (drones == null) return;
            if (drones.Striking) drones.Screen(); else drones.Strike();
        }
    }
}
