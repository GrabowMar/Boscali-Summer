using NOAvionics;
using System.Collections.Generic;
using NuclearOption.Networking;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Core.Services;
using BoscaliSummer.Modules.Wing.Domain;
using BoscaliSummer.Modules.Wing.Runtime;
namespace BoscaliSummer.Modules.Wing.Presentation
{
    /// <summary>SUPPLY's loadout editor (was the LOADOUT tab; folded into SUPPLY 2026-11-15 so the airframe grid is built once): for the
    /// airframe SUPPLY has picked, a build card that says it is the airframe's saved custom loadout (SUPPLY's FIT flies it as CUSTOM),
    /// HARDPOINTS as a small station map beside the station table with the store picker kept open below, and a LIVERY row. The parts
    /// are folded away until SUPPLY's EDIT LOADOUT is on. The airframe's standard stores show until the first change, which saves them
    /// as its one custom loadout. Loadouts and liveries live in this machine's config and only the host's launch the wing, so a client
    /// looks. <see cref="BuildMine"/> adds MY AIRCRAFT (was PILOT's PLANE tab), which Progression builds through <see cref="IMyAircraft"/>.</summary>
    internal sealed partial class WmcLoadout
    {
        private AvFlow flow;
        private bool relayout;
        private bool client;
        private bool open;

        /// <summary>The editor is on show (SUPPLY's EDIT LOADOUT is latched).</summary>
        public bool IsOpen => open;
        private readonly List<WmcCollapsible> folds = new List<WmcCollapsible>(8);

        // What is edited: SUPPLY's selected airframe, and its custom loadout (null until the first change).
        private AircraftDefinition airframe;
        private StationLayout layout;
        private LoadoutTemplateRecord current;
        private readonly List<string> keys = new List<string>();
        private FactionHQ hq;
        private MissionFacts mission;
        private FitSummary summary;
        private StoreFacts[] setFacts = new StoreFacts[0];

        private int pageKey = int.MinValue;
        private string alert;

        /// <summary>The footer's alert while the editor is open: what the launch will empty.</summary>
        public string Alert => open ? alert : null;

        /// <summary>Adds a part to the flow inside a fold: hidden (no room) until the editor is open.</summary>
        private T Fold<T>(AvFlow f, T part) where T : AvPart
        {
            var fold = f.Add(new WmcCollapsible(part));
            fold.Set(false);
            folds.Add(fold);
            return part;
        }

        public void BuildEditor(AvFlow pageFlow)
        {
            flow = pageFlow;
            BuildCard(flow);
            BuildHardpoints(flow);
            BuildLivery(flow);
        }

        /// <summary>MY AIRCRAFT, built by Progression into SUPPLY's flow; it refreshes on the flow's ticker while SUPPLY shows.</summary>
        public static void BuildMine(AvFlow f, int pageIndex)
        {
            if (ModuleServices.TryGet(out IMyAircraft mine)) mine.Build(pageIndex, f);
        }

        /// <summary>Opens or folds the editor; true when that changed it (the caller relayouts and refreshes).</summary>
        public bool SetOpen(bool on)
        {
            if (on == open) return false;
            open = on;
            foreach (WmcCollapsible fold in folds) fold.Set(on);
            if (on)
            {
                liveryFor = null;
                pageKey = int.MinValue;
            }
            flow.RequestRelayout();
            return true;
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


        /// <summary>One refresh while the editor is open, for SUPPLY's selected airframe (lists and the pick are SUPPLY's).</summary>
        public void Refresh(WmcContext c, AircraftDefinition selected)
        {
            if (!open) return;
            client = c.Client;
            airframe = selected;
            // Escalation and rank move mid-mission: the rules are read every refresh (a struct, nothing allocated).
            ReadFaction();
            Resolve();
            string fit = WingRequisition.FitOf(airframe);
            int key;
            unchecked
            {
                key = (current != null ? current.Id.GetHashCode() : 1) + WingLoadoutTemplates.Revision * 31 + (airframe != null ? airframe.GetHashCode() : 0)
                    + selStation * 7 + pickerPage * 131 + hpBoard.Page * 197 + (client ? 3 : 0) + splitVersion * 211
                    + (fit != null ? fit.GetHashCode() : 0)
                    + mission.Rank * 13 + (mission.TacticalOpen ? 19 : 0) + (mission.StrategicOpen ? 23 : 0);
            }
            if (key != pageKey)
            {
                pageKey = key;
                RefreshCard(fit);
                RefreshHardpoints();
                RefreshLivery();
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

        private void BuildCard(AvFlow f) => card = Fold(f, new WmcBuildCard(f.Content));

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

        // ---------------------------------------------------------------- the LIVERY row

        private WmcPickRow liveryRow;
        private readonly List<WingLoadoutTemplates.LiveryOption> liveries = new List<WingLoadoutTemplates.LiveryOption>();
        private readonly List<string> liveryTokens = new List<string>();
        private AircraftDefinition liveryFor;
        private Faction liveryFaction;

        private void BuildLivery(AvFlow f)
        {
            liveryRow = Fold(f, new WmcPickRow(f.Content, "LIVERY", false, StepLivery));
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
