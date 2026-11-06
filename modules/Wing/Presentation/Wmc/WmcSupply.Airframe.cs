using NOAvionics;
using System.Collections.Generic;

using BoscaliSummer.Modules.Wing.Domain;
namespace BoscaliSummer.Modules.Wing.Presentation
{
    // SUPPLY step 1 AIRFRAME (every listed airframe as a tile, no paging) and step 3 FIT & FUEL (two labelled rows and EDIT LOADOUT ›).
    internal sealed partial class WmcSupply
    {
        private WmcAirGrid tiles;
        private WmcHeadBar airHead;
        private int airCountShown = -1;
        /// <summary>The airframe each tile shows, bound at refresh: a click acts on what was shown, not on a list that refilled since.</summary>
        private readonly AircraftDefinition[] tileDef = new AircraftDefinition[WmcAirGrid.Capacity];

        private void BuildAirframe(AvFlow f)
        {
            airHead = f.Add(new WmcHeadBar(f.Content, AvIcon.Plane, "1 · " + SupplyWords.AirframeTitle));
            tiles = f.Add(new WmcAirGrid(f.Content, PickTile));
        }

        /// <summary>Every listed airframe: each tile quoted with its own reason, else the wing-wide block that stops it going (said in full
        /// on DISPATCH), rebound only when its quote, stock or selection changes. A selection no longer listed stays selected and DISPATCH
        /// says why.</summary>
        private void RefreshAirframe()
        {
            List<AircraftDefinition> cat = WingRequisition.Catalogue;
            if (cat.Count != airCountShown)
            {
                airCountShown = cat.Count;
                airHead.SetCaption(StepCaption(cat.Count > 0 ? cat.Count + " LISTED · TAP TO PICK" : "NONE LISTED", cat.Count > 0 ? "live" : "warn"));
                relayout = true;
            }
            tiles.ShowEmpty(cat.Count > 0 ? null : caller == null ? SupplyWords.NotFlyingTiles : SupplyWords.NoneOffered);
            for (int s = 0; s < WmcAirGrid.Capacity; s++)
            {
                if (s >= cat.Count)
                {
                    tileDef[s] = null;
                    tiles.Hide(s);
                    continue;
                }
                AircraftDefinition d = cat[s];
                tileDef[s] = d;
                ShopAirframe a = WingRequisition.For(d, hq);
                ShopQuote q = ShopRules.Quote(wing, a);
                bool sel = ReferenceEquals(d, selected);
                int key;
                unchecked
                {
                    key = d.GetHashCode() * 31 + (int)q.Tile * 7 + (int)(q.Price * 10f) * 131 + a.FactionStock * 1009 + (int)q.Wing * 19
                        + (sel ? 3 : 0) + (wing.Sandbox ? 5 : 0) + (q.OverLimit ? 11 : 0);
                }
                if (!tiles.NeedsBind(s, key)) continue;
                bool blocked = q.Tile != TileBlock.None;
                // Its own block first; the wing's block is the card's to say (once, above), so the tiles go quiet with a dash.
                string reason = blocked ? AirReason(q, a) : q.Wing != WingBlock.None ? "—" : q.OverLimit ? "OVER LIMIT" : "READY";
                string level = q.Allowed ? q.OverLimit ? "warn" : "ok" : q.Tile == TileBlock.Funds ? "bad" : blocked ? "warn" : "";
                tiles.Bind(s, IconFactory.Aircraft(d), SupplyWords.Code(d.code, d.unitName), SupplyWords.Name(d.unitName, d.code),
                    Credits.Price(q.Price), reason, level, sel ? "live" : blocked ? "warn" : "inert", sel, true, TileTip(d, q, a));
                relayout = true;
            }
        }

        /// <summary>The tile's reason word: its own block, funds said short (the price is on the tile).</summary>
        private static string AirReason(in ShopQuote q, in ShopAirframe a) =>
            q.Tile == TileBlock.Funds ? "FUNDS" : ShopRules.TileFoot(q, a, false);

        private string TileTip(AircraftDefinition d, in ShopQuote q, in ShopAirframe a) =>
            d.unitName + " · " + (q.Tile == TileBlock.None && q.Wing != WingBlock.None ? ShopRules.WingReason(q.Wing, wing) : ShopRules.TileFoot(q, a, wing.Sandbox));

        private void PickTile(int slot)
        {
            AircraftDefinition d = slot >= 0 && slot < tileDef.Length ? tileDef[slot] : null;
            if (d == null) return;
            WingRequisition.Selected = d;
            WmcPanel.Instance?.Refresh();
        }

        // ---------------------------------------------------------------- step 3 FIT & FUEL

        private static readonly string[] FitTips =
        {
            "AUTO: the game arms it for the role it is called for.",
            "YOUR LOADOUT: the stores your own aircraft carries now, where this airframe can take them.",
            "CUSTOM: this airframe's loadout built on LOADOUT.",
        };

        private WmcHeadBar fitHead;
        private WmcTuneRow fitRow, fuelRow;
        private AvControl editButton;
        private AircraftDefinition fitFor;
        private string fitShown;
        private int fuelShown = -1, fitRevision = -1;
        private bool fitClient, fitSet;
        private readonly string[] fitIds = new string[3];

        private void BuildFit(AvFlow f)
        {
            fitHead = f.Add(new WmcHeadBar(f.Content, AvIcon.Settings, "3 · " + SupplyWords.FitTitle));
            fitRow = f.Add(new WmcTuneRow(f.Content, "STORES", new[] { "AUTO", "YOUR LOADOUT", "CUSTOM" }, PickFit));
            var fuel = new string[SupplyWords.FuelSteps.Length];
            for (int i = 0; i < fuel.Length; i++) fuel[i] = SupplyWords.FuelSteps[i] + " %";
            fuelRow = f.Add(new WmcTuneRow(f.Content, "FUEL", fuel, SetFuel));
            editButton = f.Buttons(new AvControl.Spec("EDIT LOADOUT ›", EditFit, AvButtonStyle.Default, AvIcon.Stack2)).Controls[0];
        }

        /// <summary>The selected airframe's fit (AUTO, YOUR LOADOUT, CUSTOM once LOADOUT has built one — shown greyed with how to get
        /// one until then) and the fuel at launch, rebuilt only when either changes.</summary>
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
            LoadoutTemplateRecord custom = selected != null ? WingLoadoutTemplates.CustomFor(selected) : null;
            fitIds[0] = null;
            fitIds[1] = CallSpec.YourLoadout;
            fitIds[2] = custom?.Id;
            int at = fit == null ? 0 : fit == CallSpec.YourLoadout ? 1 : custom != null && fit == custom.Id ? 2 : 0;
            fitRow.Show(selected != null ? at : -1, -1, false);
            fitHead.SetCaption(selected == null ? "" : SupplyWords.Fit(fit) + " · " + fuel + " % FUEL");
            string why = client ? ClientWhy : selected == null ? "Pick an airframe first." : null;
            for (int i = 0; i < fitIds.Length; i++)
            {
                AvControl c = fitRow.Options[i];
                bool on = why == null && (i < 2 || custom != null);
                c.Interactable = on;
                c.Help = why ?? (on ? FitTips[i] : "No custom loadout for this airframe yet: build one with EDIT LOADOUT ›.");
            }
            editButton.Interactable = why == null;
            editButton.Help = why ?? "Open LOADOUT on this airframe.";
            fuelRow.Show(SupplyWords.FuelIndex(fuel), -1, false);
            foreach (AvControl c in fuelRow.Options)
            {
                c.Interactable = !client;
                c.Help = client ? ClientWhy : "Fuel at launch. Less is lighter and needs less runway, but reaches bingo sooner; a refit refuels to full.";
            }
        }

        private void PickFit(int i)
        {
            if (client || selected == null || i < 0 || i >= fitIds.Length || (i == 2 && fitIds[2] == null)) return;
            WingRequisition.SetFit(selected, fitIds[i]);
            WmcPanel.Instance?.Refresh();
        }

        /// <summary>EDIT LOADOUT ›: LOADOUT opens on the selected airframe.</summary>
        private void EditFit()
        {
            if (client || selected == null) return;
            WmcPanel panel = WmcPanel.Instance;
            if (panel == null) return;
            panel.Show(WmcTabs.Loadout);
            panel.Refresh();
        }

        private void SetFuel(int step)
        {
            if (client || step < 0 || step >= SupplyWords.FuelSteps.Length) return;
            WingRequisition.FuelPercent = SupplyWords.FuelSteps[step];
            WmcPanel.Instance?.Refresh();
        }
    }
}
