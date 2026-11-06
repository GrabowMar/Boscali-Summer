using NOAvionics;
using System.Collections.Generic;
using NuclearOption.Networking;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

using BoscaliSummer.Modules.Wing.Domain;
using BoscaliSummer.Modules.Wing.Runtime;
namespace BoscaliSummer.Modules.Wing.Presentation
{
    /// <summary>LOADOUT (2026-11-05 remake: an AIRFRAME grid of every airframe on top, FLY IT ON SUPPLY at the foot; was spec 2026-10-04 "One Sheet"): a build card that says it is the airframe's saved custom loadout (SUPPLY's
    /// FIT flies it as CUSTOM), AIRFRAME ◂ ▸ over the airframes SUPPLY offers (starting on SUPPLY's pick), HARDPOINTS as a small station map beside
    /// the station table with the store picker kept open below, and a LIVERY row. The airframe's standard stores show until the first
    /// change, which saves them as its one custom loadout. Loadouts and liveries live in this machine's config and only the host's launch
    /// the wing, so a client looks.</summary>
    internal sealed partial class WmcLoadout : IWmcPage
    {
        private AvFlow flow;
        private bool relayout;
        private bool client;

        // What is being edited: SUPPLY's selected airframe when the tab opens, then whatever the arrows browse to (browsing never
        // changes what SUPPLY or the CALL hotkey launches), and its custom loadout (null until the first change).
        private AircraftDefinition airframe;
        private StationLayout layout;
        private LoadoutTemplateRecord current;
        private readonly List<string> keys = new List<string>();
        private FactionHQ hq;
        private MissionFacts mission;
        private FitSummary summary;
        private StoreFacts[] setFacts = new StoreFacts[0];

        private int pageKey = int.MinValue;
        private string hint, alert;

        public string Hint => hint;

        public string Alert => alert;

        public void Build(AvFlow pageFlow, AvTicker pageTicker, int index)
        {
            flow = pageFlow;
            BuildAirframeRow(flow);
            BuildCard(flow);
            BuildHardpoints(flow);
            BuildLivery(flow);
            BuildFly(flow);
        }

        /// <summary>LOADOUT came on screen: the airframes SUPPLY offers and the faction's liveries are read again.</summary>
        public void Shown(WmcContext c)
        {
            client = c.Client;
            ReadFaction();
            Aircraft caller = WingRequisition.CallerOf(c.Wing, client);
            WingRequisition.RefreshLists(caller, force: true);
            WingRequisition.SelectDefault(caller);
            airframe = WingRequisition.Selected;
            liveryFor = null;
            pageKey = int.MinValue;
        }

        /// <summary>The player's faction and rank: the liveries and the mission rules the table reads.</summary>
        private void ReadFaction()
        {
            GameManager.GetLocalPlayer(out Player player);
            hq = player != null && player.HQ != null ? player.HQ
                : WingService.Instance != null && WingService.Instance.Leader != null ? WingService.Instance.Leader.NetworkHQ : null;
            mission = WingLoadoutCatalog.Mission(player != null ? player.PlayerRank : 0);
        }

        /// <summary>Editing is the host's (a client's loadouts are never used) and needs readable hardpoints.</summary>
        private bool Editable => !client && layout != null && airframe != null && !string.IsNullOrEmpty(airframe.jsonKey);

        /// <summary>The airframe's custom loadout (or, before the first change, its standard stores), its keys, each set's store and the
        /// summary — only when the loadout, its revision or the airframe moved. A different airframe starts the hardpoint view over.</summary>
        private void Resolve()
        {
            layout = airframe != null ? WingLoadoutCatalog.Layout(airframe) : null;
            LoadoutTemplateRecord t = airframe != null ? WingLoadoutTemplates.CustomFor(airframe) : null;
            int rev = WingLoadoutTemplates.Revision;
            if (ReferenceEquals(t, current) && rev == resolvedRevision && ReferenceEquals(airframe, resolvedAirframe) && mission.Rank == resolvedRank
                && mission.TacticalOpen == resolvedTactical && mission.StrategicOpen == resolvedStrategic) return;
            if (!ReferenceEquals(airframe, resolvedAirframe))
            {
                selStation = -1;
                pickerPage = 0;
                splitPairs.Clear();
                hpBoard.SetPage(0);
            }
            current = t;
            resolvedRevision = rev;
            resolvedAirframe = airframe;
            resolvedRank = mission.Rank;
            resolvedTactical = mission.TacticalOpen;
            resolvedStrategic = mission.StrategicOpen;
            keys.Clear();
            if (current != null) keys.AddRange(current.MountKeys);
            else if (airframe != null) WingLoadoutCatalog.StandardKeys(airframe, keys);
            if (layout != null) layout.Normalize(keys);
            int sets = layout != null ? layout.Sets : 0;
            if (setFacts.Length != sets) setFacts = new StoreFacts[sets];
            if (setOptions.Length != sets) setOptions = new WingLoadoutCatalog.StoreOption[sets];
            for (int s = 0; s < sets; s++)
            {
                WingLoadoutCatalog.StoreOption o = WingLoadoutCatalog.StoreOn(airframe, s, keys[s]);
                setOptions[s] = o;
                StoreFacts f = o.Facts;
                if (f.Known) f.Refused = !StoreRules.Flies(StoreRules.Check(WingLoadoutCatalog.FactsOf(o, hq), mission));
                setFacts[s] = f;
            }
            summary = layout != null ? LoadoutSummary.Of(layout, setFacts) : default;
            // What the launch's own check will empty (a store another fitted store blocks), once per change.
            if (fittedSets.Length != sets)
            {
                fittedSets = new bool[sets];
                clearedSets = new bool[sets];
            }
            for (int s = 0; s < sets; s++) fittedSets[s] = setFacts[s].Known && !setFacts[s].Refused;
            if (layout != null) layout.WillClear(fittedSets, clearedSets);
            pageKey = int.MinValue;
        }

        private WingLoadoutCatalog.StoreOption[] setOptions = new WingLoadoutCatalog.StoreOption[0];
        private bool[] fittedSets = new bool[0], clearedSets = new bool[0];

        /// <summary>The rank a nuclear store needs here (strategic or tactical).</summary>
        private int RankFor(in WingLoadoutCatalog.StoreOption o) => (int)(o.Strategic ? mission.StrategicMinRank : mission.TacticalMinRank);
        private int resolvedRevision = -1, resolvedRank = -1;
        private bool resolvedTactical, resolvedStrategic;
        private AircraftDefinition resolvedAirframe;

        public void Refresh(WmcContext c)
        {
            client = c.Client;
            Aircraft caller = WingRequisition.CallerOf(c.Wing, client);
            WingRequisition.RefreshLists(caller);
            WingRequisition.SelectDefault(caller);
            // SUPPLY's pick can appear after the tab opened (before the first spawn there is none): take it up while none is held.
            if (airframe == null) airframe = WingRequisition.Selected;
            // Escalation and rank move mid-mission: the rules are read every refresh (a struct, nothing allocated).
            ReadFaction();
            Resolve();
            string fit = WingRequisition.FitOf(airframe);
            int key;
            unchecked
            {
                key = (current != null ? current.Id.GetHashCode() : 1) + WingLoadoutTemplates.Revision * 31 + (airframe != null ? airframe.GetHashCode() : 0)
                    + selStation * 7 + pickerPage * 131 + hpBoard.Page * 197 + (client ? 3 : 0) + splitVersion * 211
                    + WingRequisition.Catalogue.Count * 1009 + WingRequisition.Catalogue.IndexOf(airframe) * 29
                    + (WingRequisition.Selected != null ? WingRequisition.Selected.GetHashCode() * 37 : 0) + (fit != null ? fit.GetHashCode() : 0)
                    + mission.Rank * 13 + (mission.TacticalOpen ? 19 : 0) + (mission.StrategicOpen ? 23 : 0);
            }
            if (key != pageKey)
            {
                pageKey = key;
                RefreshCard(fit);
                RefreshAirframeRow();
                RefreshHardpoints();
                RefreshLivery();
                RefreshFly();
                hint = LoadoutWords.Hint(client, airframe != null, current != null);
                alert = current != null ? LoadoutWords.EmptyHereAlert(summary.Refused + summary.Blocked) : null;
                relayout = true;
            }
            if (relayout)
            {
                relayout = false;
                flow.RequestRelayout();
            }
        }

        // ---------------------------------------------------------------- the build card

        private WmcBuildCard card;

        private void BuildCard(AvFlow f) => card = f.Add(new WmcBuildCard(f.Content));

        private void RefreshCard(string fit)
        {
            string code = airframe != null ? SupplyWords.Code(airframe.code, airframe.unitName) : WmcText.Unknown;
            string state = "warn", word;
            if (layout != null) word = LoadoutWords.Chip(current != null, current != null && fit == current.Id, out state);
            else word = airframe == null ? "NONE" : "UNREADABLE";
            Sprite icon = airframe != null ? IconFactory.Aircraft(airframe) : null;
            string chain = airframe == null ? LoadoutWords.NoAirframe : layout == null ? LoadoutWords.Unreadable : LoadoutWords.Build(summary, summary.Refused);
            card.Set(airframe != null ? LoadoutWords.Title(code, current != null) : "NO AIRFRAME", chain, word, state, state, icon,
                summary.Stations > 0 ? (float)summary.Fitted / summary.Stations : 0f);
        }

        // ---------------------------------------------------------------- the AIRFRAME grid

        private WmcHeadBar airHead;
        private WmcAirGrid airGrid;
        /// <summary>The airframe each tile shows, bound at refresh: a click acts on what was shown.</summary>
        private readonly AircraftDefinition[] tileDef = new AircraftDefinition[WmcAirGrid.Capacity];

        private void BuildAirframeRow(AvFlow f)
        {
            airHead = f.Add(new WmcHeadBar(f.Content, AvIcon.Plane, "AIRFRAME"));
            airGrid = f.Add(new WmcAirGrid(f.Content, PickAirframe));
        }

        /// <summary>Every airframe SUPPLY offers as a tile: CUSTOM when it has a loadout built here, ON SUPPLY on SUPPLY's pick; the one
        /// being edited is lit. Picking one here never changes what SUPPLY launches (FLY IT ON SUPPLY does).</summary>
        private void RefreshAirframeRow()
        {
            List<AircraftDefinition> cat = WingRequisition.Catalogue;
            airHead.SetCaption(cat.Count > 0 ? cat.Count + " LISTED · TAP TO EDIT" : "NONE LISTED");
            airGrid.ShowEmpty(cat.Count > 0 ? null : "No airframes listed yet: they list once you are flying.");
            AircraftDefinition onSupply = WingRequisition.Selected;
            for (int s = 0; s < WmcAirGrid.Capacity; s++)
            {
                if (s >= cat.Count)
                {
                    tileDef[s] = null;
                    airGrid.Hide(s);
                    continue;
                }
                AircraftDefinition d = cat[s];
                tileDef[s] = d;
                bool custom = WingLoadoutTemplates.CustomFor(d) != null, sel = ReferenceEquals(d, airframe), supply = ReferenceEquals(d, onSupply);
                int key;
                unchecked
                {
                    key = d.GetHashCode() * 31 + (custom ? 3 : 0) + (sel ? 5 : 0) + (supply ? 7 : 0) + (client ? 11 : 0);
                }
                if (!airGrid.NeedsBind(s, key)) continue;
                string code = SupplyWords.Code(d.code, d.unitName);
                airGrid.Bind(s, IconFactory.Aircraft(d), code, SupplyWords.Name(d.unitName, d.code), supply ? "SUPPLY" : "",
                    custom ? "CUSTOM" : "STANDARD", custom ? "ok" : "", sel ? "live" : custom ? "ready" : "inert", sel, true,
                    d.unitName + (custom ? " · its custom loadout is built" : " · flies its standard stores") + (supply ? " · SUPPLY's next requisition" : "")
                    + ". Tap to edit it.");
            }
        }

        private void PickAirframe(int slot)
        {
            AircraftDefinition d = slot >= 0 && slot < tileDef.Length ? tileDef[slot] : null;
            if (d == null || ReferenceEquals(d, airframe)) return;
            airframe = d;
            WmcPanel.Instance?.Refresh();
        }

        // ---------------------------------------------------------------- FLY IT ON SUPPLY

        private AvControl flyButton;

        private void BuildFly(AvFlow f)
        {
            flyButton = f.Buttons(new AvControl.Spec("FLY IT ON SUPPLY ›", FlyOnSupply, AvButtonStyle.Primary, AvIcon.PlayerPlay)).Controls[0];
        }

        private void RefreshFly()
        {
            bool on = !client && airframe != null;
            flyButton.Interactable = on;
            flyButton.Help = client ? LoadoutWords.ClientWhy : airframe == null ? "Pick an airframe first."
                : "SUPPLY's next requisition: this airframe" + (current != null ? " with this custom loadout." : " with its standard stores (AUTO).");
        }

        /// <summary>SUPPLY takes this airframe (and this custom loadout, once one is built) and comes on screen.</summary>
        private void FlyOnSupply()
        {
            if (client || airframe == null) return;
            WingRequisition.Selected = airframe;
            if (current != null) WingRequisition.SetFit(airframe, current.Id);
            WmcPanel panel = WmcPanel.Instance;
            if (panel == null) return;
            panel.Show(WmcTabs.Supply);
            panel.Refresh();
        }

        // ---------------------------------------------------------------- the LIVERY row

        private WmcPickRow liveryRow;
        private readonly List<WingLoadoutTemplates.LiveryOption> liveries = new List<WingLoadoutTemplates.LiveryOption>();
        private readonly List<string> liveryTokens = new List<string>();
        private AircraftDefinition liveryFor;
        private Faction liveryFaction;

        private void BuildLivery(AvFlow f)
        {
            liveryRow = f.Add(new WmcPickRow(f.Content, "LIVERY", false, StepLivery));
        }

        private void RefreshLivery()
        {
            Faction faction = hq != null ? hq.faction : null;
            if (!ReferenceEquals(liveryFor, airframe) || !ReferenceEquals(liveryFaction, faction))
            {
                liveryFor = airframe;
                liveryFaction = faction;
                WingLoadoutTemplates.Liveries(airframe, faction, liveries);
                liveryTokens.Clear();
                foreach (WingLoadoutTemplates.LiveryOption o in liveries) liveryTokens.Add(o.Token);
            }
            int i = LiveryChoice.IndexOf(liveryTokens, WingLoadoutTemplates.LiveryTokenOf(airframe));
            bool offered = airframe != null && liveries.Count > 1;
            string label = liveries.Count > 0 ? liveries[i].Label : LoadoutWords.Standard;
            liveryRow.Set(LoadoutWords.Livery(label), liveries.Count > 0 ? "· " + (i + 1) + " / " + liveries.Count : "", null);
            string tip = client ? LoadoutWords.ClientWhy : offered ? "The livery this airframe's wingmen wear (STANDARD: the faction's own)."
                : airframe == null ? "Pick an airframe first." : "This faction offers no other livery for it.";
            liveryRow.SetEnabled(offered && !client, tip);
        }

        private void StepLivery(int dir)
        {
            if (client || airframe == null || liveries.Count <= 1) return;
            int i = LiveryChoice.IndexOf(liveryTokens, WingLoadoutTemplates.LiveryTokenOf(airframe));
            WingLoadoutTemplates.SetLivery(airframe, liveryTokens[LiveryChoice.Step(i, liveries.Count, dir)]);
            WmcPanel.Instance?.Refresh();
        }
    }

    /// <summary>LOADOUT's build card: the airframe's icon, the loadout it shows and its state chip, the build summary, and a tape of
    /// how many stations are fitted. Not a click target.</summary>
    internal sealed class WmcBuildCard : AvPart
    {
        private const float Pad = 8f, ChipW = 96f, TopH = 22f, TapeH = 6f;
        private readonly AvFrame frame;
        private readonly Image rail, icon, track, fill;
        private readonly AvChip chip;
        private readonly TMP_Text title, chain;
        private AvState state = AvState.Inert;
        private string tapeState = "inert", railState = "inert";
        private float tape;

        public WmcBuildCard(RectTransform parent)
        {
            Rect = AvLay.Child(parent, "BuildCard");
            frame = WmcDraw.FillFrame(Rect, AvChamfer.Diagonal(6f));
            rail = AvLay.Solid(Rect, "Rail", Color.clear);
            icon = AvLay.Solid(Rect, "Icon", Color.white);
            icon.preserveAspect = true;
            icon.enabled = false;
            chip = new AvChip(Rect);
            title = WmcSheet.Text(Rect, "Title", AvTextRole.Label);
            chain = AvText.Make(Rect, "Chain", AvTextRole.ProseSmall, "", TextAlignmentOptions.TopLeft, true);
            track = AvLay.Solid(Rect, "TapeTrack", Color.clear);
            fill = AvLay.Solid(Rect, "TapeFill", Color.clear);
            Restyle();
        }

        /// <summary>The title and chain as shown (automation reads them).</summary>
        public string Title => title.text;

        /// <summary><paramref name="stateClass"/> colours the chip and the tape, <paramref name="railClass"/> the rail.</summary>
        public void Set(string titleText, string chainText, string word, string stateClass, string railClass, Sprite sprite, float tapeFraction)
        {
            if (title.text != (titleText ?? "")) title.text = titleText ?? "";
            if (chain.text != (chainText ?? "")) chain.text = chainText ?? "";
            chip.Set(word, WmcState.Of(stateClass));
            icon.sprite = sprite;
            icon.enabled = sprite != null;
            tape = Mathf.Clamp01(tapeFraction);
            state = WmcState.Of(stateClass);
            tapeState = stateClass;
            railState = railClass;
            Restyle();
            PlaceTape();
        }

        // The text starts at the pad when there is no airframe icon to stand beside.
        private float TextX => icon.enabled ? Pad + 44f : Pad + 4f;

        private float ChainW(float width) => Mathf.Max(1f, width - TextX - Pad);

        public override float Measure(float width) => Pad + TopH + 2f + Mathf.Max(AvText.Height(chain, ChainW(width)), 15f) + 6f + TapeH + Pad;

        public override void Place(AvSlot s)
        {
            base.Place(s);
            AvLay.Place(rail.rectTransform, 0f, 0f, 3f, s.H);
            AvLay.Place(icon.rectTransform, Pad + 2f, Pad, 36f, 26f);
            chip.Place(new AvSlot(s.W - Pad - ChipW, Pad, ChipW, AvGridTokens.ChipStrip));
            AvLay.Place(title.rectTransform, TextX, Pad, Mathf.Max(0f, s.W - TextX - ChipW - 2f * Pad), TopH);
            float ch = Mathf.Max(AvText.Height(chain, ChainW(s.W)), 15f);
            AvLay.Place(chain.rectTransform, TextX, Pad + TopH + 2f, ChainW(s.W), ch);
            AvLay.Place(track.rectTransform, TextX, Pad + TopH + 2f + ch + 6f, ChainW(s.W), TapeH);
            PlaceTape();
        }

        private void PlaceTape()
        {
            Vector2 p = track.rectTransform.anchoredPosition, sz = track.rectTransform.sizeDelta;
            AvLay.Place(fill.rectTransform, p.x, -p.y, sz.x * tape, sz.y);
        }

        public override void Restyle()
        {
            WmcDraw.PaintCard(frame, "card " + AvStates.Class(state), AvTheme.SurfaceInert);
            rail.color = WmcState.Color(railState);
            title.color = AvStyleHost.FuiInk("row-name", AvTheme.TextPrimary);
            chain.color = AvStyleHost.FuiInk("row-sub", AvTheme.Dim);
            track.color = AvStyleHost.FuiFill("metric-track", AvTheme.Hairline);
            fill.color = WmcState.Color(tapeState);
            icon.color = AvTheme.Friendly;
            chip.Restyle();
        }
    }
}
