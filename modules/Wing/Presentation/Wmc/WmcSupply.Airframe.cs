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
    // SUPPLY step ② AIRFRAME (compact rows, a pager in the header, and the HANGAR strip) and step ③ FIT & FUEL (FIT chips, FUEL steps).
    internal sealed partial class WmcSupply
    {
        private const int AirPerPage = 6;
        private WmcAirRows tiles;
        private WmcHeadBar airHead;
        private int tilePage, airChipShown = -1, hangarKey = int.MinValue;
        private WmcStripBlock hangar;
        private AvControl store, giveBack;
        private ConfirmGate returnGate = new ConfirmGate();
        private readonly List<string> storedCodes = new List<string>();

        private void BuildAirframe(AvFlow f)
        {
            airHead = f.Add(new WmcHeadBar(f.Content, AvIcon.Plane, "2 · " + SupplyWords.AirframeTitle,
                new AvControl.Spec("", () => TurnTiles(-1), AvButtonStyle.Quiet, AvIcon.ChevronLeft),
                new AvControl.Spec("", () => TurnTiles(1), AvButtonStyle.Quiet, AvIcon.ChevronRight)));
            airHead.Control(0).Help = "Previous page";
            airHead.Control(1).Help = "Next page";
            ids.Add("sup.tiles.prev", airHead.Control(0));
            ids.Add("sup.tiles.next", airHead.Control(1));
            tiles = f.Add(new WmcAirRows(f.Content, ids, AirPerPage, "sup.tile", PickTile));
            // The HANGAR: its count, what is stored, then STORE and a two-press RETURN.
            hangar = f.Add(new WmcStripBlock(f.Content, "Hangar", 1, null, (r, k) => { if (k == 0) Store(); else Return(); },
                new AvControl.Spec("STORE", null, AvButtonStyle.Default), new AvControl.Spec("RETURN", null, AvButtonStyle.Default)));
            store = hangar.Button(0, 0);
            giveBack = hangar.Button(0, 1);
            ids.Add("sup.store", store);
            ids.Add("sup.return", giveBack);
        }

        /// <summary>This page of listed airframes: each row quoted with its own reason only (a wing-wide block is said once, on
        /// DISPATCH), rebound only when its quote, stock or selection changes. A selection no longer listed stays selected and
        /// DISPATCH says why (0.9 critique 15).</summary>
        private void RefreshAirframe()
        {
            List<AircraftDefinition> cat = WingRequisition.Catalogue;
            int per = tiles.PerPage, pages = Pages.Count(cat.Count, per);
            tilePage = Pages.Clamp(tilePage, cat.Count, per);
            airHead.Control(0).Interactable = tilePage > 0;
            airHead.Control(1).Interactable = tilePage < pages - 1;
            int chip = (cat.Count * 31 + tilePage) * 7 + pages;
            if (chip != airChipShown)
            {
                airChipShown = chip;
                airHead.SetCaption(StepCaption(SupplyWords.AirframeCaption(cat.Count, tilePage, pages), cat.Count > 0 ? "live" : "warn"));
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
                        + (sel ? 3 : 0) + (wing.Sandbox ? 5 : 0) + (q.OverLimit ? 11 : 0);
                }
                if (!tiles.NeedsBind(s, key)) continue;
                bool blocked = q.Tile != TileBlock.None;
                string reason = blocked ? AirReason(q, a) : q.OverLimit ? "OVER LIMIT" : "READY";
                // The reason's own colour: ready reads live (ok), blocked by funds reads danger (bad), any other block warn.
                string level = q.Tile == TileBlock.None ? q.OverLimit ? "warn" : "ok" : q.Tile == TileBlock.Funds ? "bad" : "warn";
                tiles.Bind(s, IconFactory.Aircraft(d), SupplyWords.Code(d.code, d.unitName), SupplyWords.Name(d.unitName, d.code),
                    Credits.Price(q.Price), reason, level, sel ? "live" : blocked ? "warn" : a.Held > 0 && !wing.Sandbox ? "info" : "inert", sel, true,
                    TileTip(d, q, a));
                relayout = true;
            }
        }

        /// <summary>The row's reason word: the tile's own block, funds said short (the price is in the row).</summary>
        private static string AirReason(in ShopQuote q, in ShopAirframe a) =>
            q.Tile == TileBlock.Funds ? "FUNDS" : ShopRules.TileFoot(q, a, false);

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

        /// <summary>The HANGAR's count and what is stored; STORE and RETURN act on the selected airframe, each disabled with its reason.</summary>
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
                foreach (AircraftDefinition d in WingSupplyReserve.Stored) key = key * 17 + (d != null ? d.GetHashCode() : 0);
            }
            if (key == hangarKey) return;
            hangarKey = key;
            relayout = true;
            bool full = HangarWords.Full(count, cap) && !offline;
            storedCodes.Clear();
            foreach (AircraftDefinition d in WingSupplyReserve.Stored)
                if (d != null) storedCodes.Add(SupplyWords.Code(d.code, d.unitName));
            hangar.Set(0, HangarWords.Label(count, cap, offline), offline ? AvState.Inert : full ? AvState.Caution : AvState.Info, "",
                HangarWords.Caption(storedCodes, host, faction), -1f, full ? AvStates.Glyph(AvState.Caution) + "FULL" : "", AvState.Caution, AvState.Info);
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

        // ---------------------------------------------------------------- step ③ FIT & FUEL

        private WmcHeadBar fitHead;
        private WmcChipRow fitRow;
        private AircraftDefinition fitFor;
        private string fitShown;
        private int fuelShown = -1, fitRevision = -1;
        private bool fitClient, fitSet;
        private readonly List<string> fitIds = new List<string>();
        private readonly List<string> fitLabels = new List<string>();

        private void BuildFit(AvFlow f)
        {
            var fuel = new AvControl.Spec[SupplyWords.FuelSteps.Length];
            for (int i = 0; i < fuel.Length; i++)
            {
                int step = i;
                fuel[i] = new AvControl.Spec(SupplyWords.FuelChip(SupplyWords.FuelSteps[i]), () => SetFuel(step), AvButtonStyle.Default);
            }
            fitHead = f.Add(new WmcHeadBar(f.Content, AvIcon.Settings, "3 · " + SupplyWords.FitTitle, fuel));
            fitHead.SetCaption("FUEL");
            for (int i = 0; i < fuel.Length; i++) ids.Add("sup.fuel" + SupplyWords.FuelSteps[i], fitHead.Control(i));
            ids.Add("sup.fuel", CycleFuel);
            fitRow = f.Add(new WmcChipRow(f.Content, null, 2 + TemplateNames.PerAirframe, PickFit,
                new AvControl.Spec(SupplyWords.EditLabel, EditFit, AvButtonStyle.Default)));
            ids.Add("sup.fit", CycleFit);
            ids.Add("sup.fit.auto", fitRow.Choice(0));
            ids.Add("sup.fit.yours", fitRow.Choice(1));
            for (int i = 0; i < TemplateNames.PerAirframe; i++) ids.Add("sup.fit.t" + i, fitRow.Choice(2 + i));
            ids.Add("sup.fit.edit", fitRow.Action(0));
        }

        /// <summary>The selected airframe's fit (AUTO, the default: the game's own pick) and the fuel at launch, rebuilt only when
        /// either changes.</summary>
        private void RefreshFit()
        {
            string fit = WingRequisition.FitOf(selected);
            int fuel = WingRequisition.FuelPercent, revision = WingLoadoutTemplates.Revision;
            if (fitSet && ReferenceEquals(selected, fitFor) && ReferenceEquals(fit, fitShown) && fuel == fuelShown && client == fitClient
                && revision == fitRevision) return;
            fitSet = true;
            fitFor = selected;
            fitShown = fit;
            fuelShown = fuel;
            fitClient = client;
            fitRevision = revision;
            relayout = true;
            fitIds.Clear();
            fitLabels.Clear();
            fitIds.Add(null);
            fitLabels.Add("AUTO");
            fitIds.Add(CallSpec.YourLoadout);
            fitLabels.Add("YOUR LOADOUT");
            if (selected != null)
                foreach (LoadoutTemplateRecord t in WingLoadoutTemplates.For(selected))
                {
                    if (fitIds.Count >= fitRow.MaxChoices) break;
                    fitIds.Add(t.Id);
                    fitLabels.Add(SupplyWords.Fit(t.Id, t.Name));
                }
            int at = fitIds.IndexOf(fit);
            fitRow.SetChoices(fitLabels, at < 0 ? 0 : at);
            string why = client ? ClientWhy : selected == null ? "Pick an airframe first." : null;
            for (int i = 0; i < fitLabels.Count; i++)
            {
                AvControl c = fitRow.Choice(i);
                c.Interactable = why == null;
                c.Help = why ?? FitTip(fitIds[i]);
            }
            fitRow.Action(0).Interactable = why == null;
            fitRow.Action(0).Help = why ?? "Open LOADOUT on this airframe" + (fit != null && fit != CallSpec.YourLoadout ? " and this template." : ".");
            int step = SupplyWords.FuelIndex(fuel);
            for (int i = 0; i < SupplyWords.FuelSteps.Length; i++)
            {
                AvControl c = fitHead.Control(i);
                c.Latched = i == step;
                c.Interactable = !client;
                c.Help = client ? ClientWhy
                    : "Fuel at launch: 25, 50, 75 or 100%. Less is lighter but reaches bingo sooner, and a refit refuels to the same level.";
            }
        }

        private static string FitTip(string id) =>
            id == null ? SupplyWords.FitDetail(null, true) : id == CallSpec.YourLoadout ? SupplyWords.FitDetail(id, true) : SupplyWords.FitDetail(id, true);

        private void PickFit(int i)
        {
            if (client || selected == null || i < 0 || i >= fitIds.Count) return;
            WingRequisition.SetFit(selected, fitIds[i]);
            WmcPanel.Instance?.Refresh();
        }

        /// <summary>The next fit in the chip row (AUTO, YOUR LOADOUT, then the templates), for automation.</summary>
        private void CycleFit()
        {
            if (client || selected == null || fitIds.Count == 0) return;
            int at = fitIds.IndexOf(WingRequisition.FitOf(selected));
            PickFit((at + 1) % fitIds.Count);
        }

        /// <summary>EDIT ›: LOADOUT opens on the selected airframe and the template its fit names (AUTO and YOUR LOADOUT open the airframe).</summary>
        private void EditFit()
        {
            if (client || selected == null) return;
            WmcPanel panel = WmcPanel.Instance;
            if (panel == null || panel.Loadout == null) return;
            string fit = WingRequisition.FitOf(selected);
            panel.Loadout.Edit(selected, fit != null && fit != CallSpec.YourLoadout ? fit : null);
            panel.Show(WmcTabs.Loadout);
            panel.Refresh();
        }

        private void SetFuel(int step)
        {
            if (client || step < 0 || step >= SupplyWords.FuelSteps.Length) return;
            WingRequisition.FuelPercent = SupplyWords.FuelSteps[step];
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
