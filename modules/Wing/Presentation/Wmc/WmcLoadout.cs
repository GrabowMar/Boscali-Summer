using NOAvionics;
using System.Collections.Generic;
using NuclearOption.Networking;
using UnityEngine;

using BoscaliSummer.Modules.Wing.Domain;
using BoscaliSummer.Modules.Wing.Runtime;
namespace BoscaliSummer.Modules.Wing.Presentation
{
    /// <summary>LOADOUT (spec 2026-10-04 §SUPPLY × LOADOUT, "One Sheet"): a build card that says it is a saved preset (SUPPLY's FIT
    /// flies it), NAME, AIRFRAME ◂ ▸, TEMPLATE chips with NEW · COPY · DELETE (two-press), HARDPOINTS as a small station map beside the
    /// station table with the store picker kept open below (every store with its verdict word and mass), and a LIVERY row. Templates and
    /// liveries live in this machine's config, so a client edits its own.</summary>
    internal sealed partial class WmcLoadout : IWmcPage
    {
        private readonly WmcControls ids;
        private AvFlow flow;
        private AvTicker ticker;
        private bool relayout;
        private bool client;

        // What is being edited.
        private AircraftDefinition airframe;
        private StationLayout layout;
        private LoadoutTemplateRecord current;
        private readonly Dictionary<string, string> editing = new Dictionary<string, string>();
        private readonly List<string> keys = new List<string>();
        private FactionHQ hq;
        private MissionFacts mission;
        private FitSummary summary;
        private StoreFacts[] setFacts = new StoreFacts[0];
        private readonly List<AircraftDefinition> airframes = new List<AircraftDefinition>();

        private int pageKey = int.MinValue;
        private string hint, alert;

        public WmcLoadout(WmcControls controls) => ids = controls;

        public string Hint => hint;

        public string Alert => alert;

        public void Build(AvFlow pageFlow, AvTicker pageTicker, int index)
        {
            flow = pageFlow;
            ticker = pageTicker;
            BuildCard(flow);
            BuildAirframeRow(flow);
            BuildTemplateBar(flow);
            BuildHardpoints(flow);
            BuildLivery(flow);
        }

        /// <summary>SUPPLY's EDIT ›: LOADOUT edits <paramref name="definition"/> and, when given, the template <paramref name="templateId"/>.</summary>
        public void Edit(AircraftDefinition definition, string templateId)
        {
            if (definition == null) return;
            airframe = definition;
            if (templateId != null && definition.jsonKey != null) editing[definition.jsonKey] = templateId;
            hpResetPending = true;
            deleteGate = new ConfirmGate();
            liveryFor = null;
            pageKey = int.MinValue;
        }

        /// <summary>LOADOUT came into view: the airframe list and the faction's liveries are read again.</summary>
        private void OnShown()
        {
            FillAirframes();
            if (airframe == null || !airframes.Contains(airframe))
                airframe = WingRequisition.Selected != null && airframes.Contains(WingRequisition.Selected) ? WingRequisition.Selected
                    : airframes.Count > 0 ? airframes[0] : null;
            liveryFor = null;
            pageKey = int.MinValue;
        }

        /// <summary>Airframes with readable hardpoints that are neither VTOL nor placeholders; offered or stored ones first.</summary>
        private void FillAirframes()
        {
            airframes.Clear();
            Encyclopedia enc = Encyclopedia.i;
            if (enc == null || enc.aircraft == null) return;
            int offered = 0;
            foreach (AircraftDefinition d in enc.aircraft)
            {
                if (d == null) continue;
                ShopAirframe a = WingRequisition.For(d, hq);
                if (a.Vtol || a.Placeholder || WingLoadoutCatalog.Layout(d) == null) continue;
                if (WingRequisition.Catalogue.Contains(d) || a.Held > 0) airframes.Insert(offered++, d);
                else airframes.Add(d);
            }
        }

        /// <summary>LOADOUT came on screen: the airframe list and the faction's liveries are read again.</summary>
        public void Shown(WmcContext c)
        {
            client = c.Client;
            ReadFaction();
            OnShown();
        }

        /// <summary>The player's faction and rank: the liveries and the mission rules the table reads.</summary>
        private void ReadFaction()
        {
            GameManager.GetLocalPlayer(out Player player);
            hq = player != null && player.HQ != null ? player.HQ
                : WingService.Instance != null && WingService.Instance.Leader != null ? WingService.Instance.Leader.NetworkHQ : null;
            mission = WingLoadoutCatalog.Mission(player != null ? player.PlayerRank : 0);
        }

        /// <summary>Which template is edited: the one last edited on this airframe, else SUPPLY's fit for it, else its first; then its
        /// keys, each set's store and the summary — only when the template, its revision or the airframe moved.</summary>
        private void Resolve()
        {
            layout = airframe != null ? WingLoadoutCatalog.Layout(airframe) : null;
            LoadoutTemplateRecord t = null;
            if (airframe != null)
            {
                string key = airframe.jsonKey;
                if (key != null && editing.TryGetValue(key, out string id)) t = Mine(WingLoadoutTemplates.ById(id));
                if (t == null) t = Mine(WingLoadoutTemplates.ById(WingRequisition.FitOf(airframe)));
                if (t == null)
                {
                    IReadOnlyList<LoadoutTemplateRecord> list = WingLoadoutTemplates.For(airframe);
                    t = list.Count > 0 ? list[0] : null;
                }
            }
            int rev = WingLoadoutTemplates.Revision;
            if (ReferenceEquals(t, current) && rev == resolvedRevision && ReferenceEquals(airframe, resolvedAirframe) && mission.Rank == resolvedRank
                && mission.TacticalOpen == resolvedTactical && mission.StrategicOpen == resolvedStrategic) return;
            current = t;
            resolvedRevision = rev;
            resolvedAirframe = airframe;
            resolvedRank = mission.Rank;
            resolvedTactical = mission.TacticalOpen;
            resolvedStrategic = mission.StrategicOpen;
            keys.Clear();
            if (current != null) keys.AddRange(current.MountKeys);
            if (layout != null) layout.Normalize(keys);
            int sets = layout != null ? layout.Sets : 0;
            if (setFacts.Length != sets) setFacts = new StoreFacts[sets];
            if (setOptions.Length != sets) setOptions = new WingLoadoutCatalog.StoreOption[sets];
            for (int s = 0; s < sets; s++)
            {
                WingLoadoutCatalog.StoreOption o = WingLoadoutCatalog.StoreOn(airframe, s, keys[s]);
                setOptions[s] = o;
                StoreFacts f = o.Facts;
                if (f.Known) f.Refused = !StoreRules.Flies(StoreRules.Check(WingLoadoutCatalog.FactsOf(o, layout.PylonsAt(s), hq), mission));
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

        private LoadoutTemplateRecord Mine(LoadoutTemplateRecord t) => t != null && airframe != null && t.AirframeKey == airframe.jsonKey ? t : null;

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
            // Escalation and rank move mid-mission: the rules are read every refresh (a struct, nothing allocated).
            ReadFaction();
            Resolve();
            bool asking = current != null && deleteGate.IsArmed(current.Id, Time.unscaledTime);
            int key;
            unchecked
            {
                key = (current != null ? current.Id.GetHashCode() : 1) + WingLoadoutTemplates.Revision * 31 + (airframe != null ? airframe.GetHashCode() : 0)
                    + selStation * 7 + pickerPage * 131 + hpBoard.Page * 197 + (client ? 3 : 0) + (asking ? 5 : 0) + (nameField.Dirty ? 17 : 0)
                    + airframes.Count * 1009 + (WingRequisition.FitOf(airframe) != null ? WingRequisition.FitOf(airframe).GetHashCode() : 0)
                    + mission.Rank * 13 + (mission.TacticalOpen ? 19 : 0) + (mission.StrategicOpen ? 23 : 0);
            }
            nameField.EditingId = current?.Id;
            if (key != pageKey)
            {
                pageKey = key;
                RefreshCard(asking);
                RefreshAirframeRow();
                RefreshTemplateBar(asking);
                RefreshHardpoints();
                RefreshLivery();
                string code = airframe != null ? SupplyWords.Code(airframe.code, airframe.unitName) : null;
                hint = LoadoutWords.Hint(client, airframe != null, current != null, code);
                alert = asking ? LoadoutWords.DeleteAsk(current.Name) : current != null ? LoadoutWords.EmptyHereAlert(summary.Refused + summary.Blocked) : null;
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
        private string chipShown;
        private WmcNameField nameField;

        private void BuildCard(AvFlow f)
        {
            card = f.Add(new WmcBuildCard(f.Content));
            nameField = f.Add(new WmcNameField(f.Content, "NAME", TemplateNames.MaxChars, CommitName,
                "The template's name as SUPPLY's FIT lists it: Enter saves it (16 characters at most)."));
        }

        private void RefreshCard(bool asking)
        {
            string code = airframe != null ? SupplyWords.Code(airframe.code, airframe.unitName) : WmcText.Unknown;
            bool supplyFit = current != null && WingRequisition.FitOf(airframe) == current.Id;
            string state;
            chipShown = LoadoutWords.Chip(current != null, nameField.Dirty, supplyFit, out state);
            Sprite icon = airframe != null ? IconFactory.Aircraft(airframe) : null;
            string chain = airframe != null ? LoadoutWords.Build(summary, current != null, summary.Refused) : "Pick an airframe below";
            if (airframe != null && current != null) chain += " · " + LoadoutWords.UsedBy(supplyFit ? 1 : 0);
            card.Set(airframe != null ? LoadoutWords.Title(code, current?.Name) : "NO AIRFRAME", chain, chipShown, state, asking ? "warn" : state, icon,
                summary.Stations > 0 ? (float)summary.Fitted / summary.Stations : 0f);
            nameField.SetText(current?.Name ?? "");
            nameField.SetInteractable(current != null);
        }

        private void CommitName(string id, string typed)
        {
            LoadoutTemplateRecord t = WingLoadoutTemplates.ById(id);
            if (t != null && WingLoadoutTemplates.Rename(t, typed)) WingToast.Show("Template renamed " + t.Name);
            WmcPanel.Instance?.Refresh();
        }

        // ---------------------------------------------------------------- the LIVERY row

        private WmcPickRow liveryRow;
        private readonly List<WingLoadoutTemplates.LiveryOption> liveries = new List<WingLoadoutTemplates.LiveryOption>();
        private readonly List<string> liveryTokens = new List<string>();
        private AircraftDefinition liveryFor;
        private Faction liveryFaction;

        private void BuildLivery(AvFlow f)
        {
            liveryRow = f.Add(new WmcPickRow(f.Content, "LIVERY", false, StepLivery, null));
            ids.Add("lo.livery.prev", liveryRow.Prev);
            ids.Add("lo.livery.next", liveryRow.Next);
        }

        /// <summary>A livery's swatch: STANDARD is the console's neutral, any other label gets a hue from its name (a livery has no
        /// colour the page can read).</summary>
        private static Color Swatch(string label, bool standard)
        {
            if (standard) return new Color(0.29f, 0.36f, 0.40f, 1f);
            int h = 17;
            foreach (char ch in label ?? "") h = unchecked(h * 31 + ch);
            return Color.HSVToRGB((h & 0xFFFF) / 65535f, 0.42f, 0.62f);
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
            bool can = airframe != null && liveries.Count > 1;
            string label = liveries.Count > 0 ? liveries[i].Label : LoadoutWords.Standard;
            liveryRow.Set(LoadoutWords.Livery(label), liveries.Count > 0 ? "· " + (i + 1) + " / " + liveries.Count : "", null,
                Swatch(label, liveries.Count == 0 || i == 0));
            string tip = can ? "The livery this airframe's wingmen wear (STANDARD: the faction's own)."
                : airframe == null ? "Pick an airframe first." : "This faction offers no other livery for it.";
            liveryRow.SetEnabled(can, tip);
        }

        private void StepLivery(int dir)
        {
            if (airframe == null || liveries.Count <= 1) return;
            int i = LiveryChoice.IndexOf(liveryTokens, WingLoadoutTemplates.LiveryTokenOf(airframe));
            WingLoadoutTemplates.SetLivery(airframe, liveryTokens[LiveryChoice.Step(i, liveries.Count, dir)]);
            WmcPanel.Instance?.Refresh();
        }
    }
}
