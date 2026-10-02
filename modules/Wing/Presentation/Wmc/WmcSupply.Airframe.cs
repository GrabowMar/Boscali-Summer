using NOAvionics;
using System.Collections.Generic;
using UnityEngine;

using BoscaliSummer.Modules.Wing.Domain;
using BoscaliSummer.Modules.Wing.Runtime;
using BoscaliSummer.Modules.Wing.Presentation;
using BoscaliSummer.Modules.Wing.Patches;
using BoscaliSummer.Modules.Wing.Networking;
using BoscaliSummer.Modules.Wing.Configuration;
using BoscaliSummer.Core.Math;
using BoscaliSummer.Core.Util;
using BoscaliSummer.Core.Storage;
namespace BoscaliSummer.Modules.Wing.Presentation
{
    // SUPPLY step 2 AIRFRAME (a page of tiles and the HANGAR store) and step 3 FIT & FUEL.
    internal sealed partial class WmcSupply
    {
        private WmcAirframeGrid tiles;
        private int tilePage, airChipShown = -1, hangarKey = int.MinValue;
        private AvSection airSection;
        private AvRow hangarRow;
        private AvControl store, giveBack;
        private ConfirmGate returnGate = new ConfirmGate();

        private void BuildAirframe(AvFlow f)
        {
            airSection = f.Section(AvIcon.Plane, "2 · " + SupplyWords.AirframeTitle, "");
            tiles = f.Add(new WmcAirframeGrid(f.Content, ids, BezelLayout.TileCols, BezelLayout.TileRows, BezelLayout.TileH, true, "sup.tile",
                "sup.tiles.", PickTile, TurnTiles));
            // The HANGAR: its count as a row, then STORE and a two-press RETURN.
            hangarRow = f.Add(new AvRow(f.Content));
            AvControl[] hangar = f.Buttons(new AvControl.Spec("STORE", Store, AvButtonStyle.Default, AvIcon.Database),
                new AvControl.Spec("RETURN", Return, AvButtonStyle.Default, AvIcon.ArrowBackUp)).Controls;
            store = hangar[0];
            giveBack = hangar[1];
            ids.Add("sup.store", store);
            ids.Add("sup.return", giveBack);
        }

        /// <summary>This page of listed airframes: each tile quoted with its own reason only (a wing-wide block is said once, on
        /// DISPATCH), rebound only when its quote, stock or selection changes. A selection no longer listed stays selected and
        /// DISPATCH says why (0.9 critique 15).</summary>
        private void RefreshAirframe()
        {
            List<AircraftDefinition> cat = WingRequisition.Catalogue;
            int per = tiles.PerPage;
            tilePage = Pages.Clamp(tilePage, cat.Count, per);
            tiles.SetPage(tilePage, Pages.Count(cat.Count, per));
            if (cat.Count != airChipShown)
            {
                airChipShown = cat.Count;
                StepCaption(airSection, SupplyWords.AirframeChip(cat.Count), cat.Count > 0 ? "live" : "warn");
                relayout = true;
            }
            tiles.ShowEmpty(cat.Count > 0 ? null : caller == null ? SupplyWords.NotFlyingTiles : SupplyWords.NoneOffered);
            int first = Pages.First(tilePage, per);
            for (int s = 0; s < per; s++)
            {
                int k = first + s;
                if (k >= cat.Count)
                {
                    tiles.Hide(s);
                    continue;
                }
                AircraftDefinition d = cat[k];
                ShopAirframe a = WingRequisition.For(d, hq);
                ShopQuote q = ShopRules.Quote(wing, a);
                bool sel = ReferenceEquals(d, selected);
                int key;
                unchecked
                {
                    key = d.GetHashCode() * 31 + (int)q.Tile * 7 + (int)(q.Price * 10f) * 131 + (a.FactionStock + a.Held) * 1009 + a.Held * 17
                        + (sel ? 3 : 0) + (wing.Sandbox ? 5 : 0);
                }
                if (!tiles.NeedsBind(s, key)) continue;
                bool blocked = q.Tile != TileBlock.None;
                // The foot's own colour: affordable reads live (ok), blocked by funds reads danger (bad), any other block warn.
                string footLevel = q.Tile == TileBlock.None ? "ok" : q.Tile == TileBlock.Funds ? "bad" : "warn";
                tiles.Bind(s, IconFactory.Aircraft(d), SupplyWords.Code(d.code, d.unitName), SupplyWords.Name(d.unitName, d.code),
                    ShopRules.TileFoot(q, a, wing.Sandbox), footLevel,
                    sel ? "live" : blocked ? "warn" : a.Held > 0 && !wing.Sandbox ? "info" : "inert", sel, true, TileTip(d, q, a));
            }
        }

        private string TileTip(AircraftDefinition d, in ShopQuote q, in ShopAirframe a) =>
            d.unitName + " · " + ShopRules.TileFoot(q, a, wing.Sandbox)
            + (a.Held > 0 && !wing.Sandbox ? " · " + AvNum.Fixed(a.Held, 0) + " IN THE HANGAR" : "");

        private void PickTile(int slot)
        {
            int k = Pages.First(tilePage, tiles.PerPage) + slot;
            if (k < 0 || k >= WingRequisition.Catalogue.Count) return;
            WingRequisition.Selected = WingRequisition.Catalogue[k];
            // A RETURN asked about another airframe asks again (review focus 3).
            returnGate = new ConfirmGate();
            WmcPanel.Instance?.Refresh();
        }

        private void TurnTiles(int dir)
        {
            tilePage = Pages.Clamp(tilePage + dir, WingRequisition.Catalogue.Count, tiles.PerPage);
            WmcPanel.Instance?.Refresh();
        }

        // ---------------------------------------------------------------- the HANGAR store

        /// <summary>The HANGAR's count; STORE and RETURN act on the selected airframe, each disabled with its reason.</summary>
        private void RefreshHangar()
        {
            int count = WingSupplyReserve.Count, cap = WingSupplyReserve.Capacity;
            bool host = WingSupplyReserve.IsHost && !client, faction = WingSupplyReserve.HasFaction, offline = client || !faction;
            int stock = selected != null && faction ? WingSupplyReserve.FactionStockOf(selected) : 0;
            int stored = selected != null ? WingSupplyReserve.CountOf(selected) : 0;
            bool asking = selected != null && returnGate.IsArmed(selected.jsonKey, Time.unscaledTime);
            int key;
            unchecked
            {
                key = count * 7 + cap * 31 + (offline ? 3 : 0) + stock * 101 + stored * 1009 + (asking ? 5 : 0) + (host ? 11 : 0) + (wing.Sandbox ? 13 : 0)
                    + (selected != null ? selected.GetHashCode() : 0);
            }
            if (key == hangarKey) return;
            hangarKey = key;
            relayout = true;
            bool full = HangarWords.Full(count, cap) && !offline;
            hangarRow.Set(HangarWords.Label(count, cap, offline), offline ? "HOST ONLY" : full ? AvStates.Glyph(AvState.Caution) + "FULL" : "STORED",
                "", offline ? AvState.Inert : full ? AvState.Caution : AvState.Info);
            string why = client ? ClientWhy : selected == null ? "Pick an airframe first." : ShopRules.StoreBlock(host, faction, wing.Sandbox, count, cap, stock);
            store.Interactable = why == null;
            store.Help = why ?? "Store one " + selected.unitName + " in the HANGAR: the faction's AI cannot take it, and your next " +
                "requisition of it launches from there.";
            why = client ? ClientWhy : selected == null ? "Pick an airframe first." : ShopRules.ReturnBlock(host, faction, stored);
            giveBack.Interactable = why == null;
            giveBack.Help = why ?? "Return one " + selected.unitName + " to the faction's stock (press twice).";
            giveBack.Label = HangarWords.ReturnLabel(asking);
            giveBack.Latched = asking;
        }

        private void Store()
        {
            if (client || selected == null) return;
            AircraftDefinition d = selected;
            WingToast.Show(WingSupplyReserve.Hold(d, out string why)
                ? HangarWords.Stored(d.unitName, WingSupplyReserve.Count, WingSupplyReserve.Capacity) : why);
            WmcPanel.Instance?.Refresh();
        }

        private void Return()
        {
            if (client || selected == null) return;
            AircraftDefinition d = selected;
            if (!returnGate.Press(d.jsonKey, Time.unscaledTime))
                WingToast.Show(HangarWords.Ask(d.unitName));
            else
                WingToast.Show(WingSupplyReserve.Release(d, out string why)
                    ? HangarWords.Returned(d.unitName, WingSupplyReserve.Count, WingSupplyReserve.Capacity) : why);
            WmcPanel.Instance?.Refresh();
        }

        // ---------------------------------------------------------------- step 3 FIT & FUEL

        private AvControl fitButton, fuelButton;
        private AvSection fitSection;
        private WmcLines fitDetail;
        private AvPopup fitPopup;
        private readonly List<AvPopupEntry> fitEntries = new List<AvPopupEntry>();
        private readonly List<string> fitIds = new List<string>();
        private AircraftDefinition fitFor;
        private string fitShown;
        private int fuelShown = -1;
        private bool fitClient, fitSet;

        private void BuildFit(AvFlow f)
        {
            fitSection = f.Section(AvIcon.Settings, "3 · " + SupplyWords.FitTitle, "");
            AvControl[] row = f.Buttons(new AvControl.Spec(SupplyWords.FitButton("AUTO"), OpenFit, AvButtonStyle.Default, AvIcon.Settings),
                new AvControl.Spec(SupplyWords.Fuel(100), CycleFuel, AvButtonStyle.Default, AvIcon.Gauge)).Controls;
            fitButton = row[0];
            fuelButton = row[1];
            ids.Add("sup.fit", fitButton);
            ids.Add("sup.fuel", fuelButton);
            fitDetail = f.Add(new WmcLines(f.Content, 1));
        }

        /// <summary>The selected airframe's fit (AUTO, the default: the game's own pick) and the fuel at launch, rebuilt only when
        /// either changes.</summary>
        private void RefreshFit()
        {
            string fit = WingRequisition.FitOf(selected);
            int fuel = WingRequisition.FuelPercent;
            if (fitSet && ReferenceEquals(selected, fitFor) && ReferenceEquals(fit, fitShown) && fuel == fuelShown && client == fitClient) return;
            fitSet = true;
            fitFor = selected;
            fitShown = fit;
            fuelShown = fuel;
            fitClient = client;
            relayout = true;
            fitButton.Label = SupplyWords.FitButton(SupplyWords.Fit(fit,
                fit != null && fit != CallSpec.YourLoadout ? WingLoadoutTemplates.NameOf(fit) : null));
            fuelButton.Label = SupplyWords.Fuel(fuel);
            fitDetail.Set(0, SupplyWords.FitDetail(fit, selected != null && WingLoadoutTemplates.CountFor(selected) > 0));
            StepCaption(fitSection, SupplyWords.Fuel(fuel), fuel < 50 ? "warn" : "live");
            string why = client ? ClientWhy : selected == null ? "Pick an airframe first." : null;
            fitButton.Interactable = why == null;
            fitButton.Help = why ?? "What it carries: AUTO (the game's pick for the mission), YOUR LOADOUT, or a template saved on LOADOUT.";
            fuelButton.Interactable = !client;
            fuelButton.Help = client ? ClientWhy
                : "Fuel at launch: 25, 50, 75 or 100%. Less is lighter but reaches bingo sooner, and a refit refuels to the same level.";
        }

        private void OpenFit()
        {
            if (client || selected == null) return;
            AircraftDefinition d = selected;
            string current = WingRequisition.FitOf(d);
            fitEntries.Clear();
            fitIds.Clear();
            AddFit("AUTO", "the game's pick for the mission", null, current);
            AddFit("YOUR LOADOUT", "as you would fly it", CallSpec.YourLoadout, current);
            foreach (LoadoutTemplateRecord t in WingLoadoutTemplates.For(d)) AddFit(SupplyWords.Fit(t.Id, t.Name), "saved on LOADOUT", t.Id, current);
            fitPopup.Show(WmcPopup.Area(flow.Content, fitButton.Rect, fitEntries.Count), fitEntries, PickFit);
        }

        private void AddFit(string text, string detail, string id, string current)
        {
            fitEntries.Add(new AvPopupEntry(text, detail, id == current));
            fitIds.Add(id);
        }

        private void PickFit(int i)
        {
            if (client || selected == null || i < 0 || i >= fitIds.Count) return;
            WingRequisition.SetFit(selected, fitIds[i]);
            WmcPanel.Instance?.Refresh();
        }

        private void CycleFuel()
        {
            if (client) return;
            WingRequisition.FuelPercent = ShopRules.NextFuel(WingRequisition.FuelPercent);
            WmcPanel.Instance?.Refresh();
        }
    }
}
