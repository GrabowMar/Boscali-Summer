using NOAvionics;
using System.Collections.Generic;
using NuclearOption.Networking;
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
    /// <summary>LOADOUT (spec WMC rebuild §LOADOUT), the 0.9 v2 page with its critique fixed, now a kit v2 flow: a build card that says
    /// it is a saved preset (SUPPLY's FIT flies it) with its NAME field, airframe tiles, a template bar (NEW · COPY · DELETE,
    /// two-press), a HARDPOINTS table where a station is one row everywhere and only CLEAR empties one, a store popup beside its
    /// row, and a LIVERY per airframe that the spawn now wears. Templates and liveries live in this machine's config, so a client
    /// edits its own.</summary>
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
            BuildTiles(flow);
            BuildTemplateBar(flow);
            BuildHardpoints(flow);
            BuildLivery(flow);
            // Last, so its list draws over the page.
            popup = new AvPopup(flow.Content, flow.Width);
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
                    + tilePage * 7 + hpList.Page * 131 + (client ? 3 : 0) + (asking ? 5 : 0) + (nameField.Dirty ? 17 : 0) + airframes.Count * 1009
                    + (WingRequisition.FitOf(airframe) != null ? WingRequisition.FitOf(airframe).GetHashCode() : 0) + mission.Rank * 13
                    + (mission.TacticalOpen ? 19 : 0) + (mission.StrategicOpen ? 23 : 0);
            }
            nameField.EditingId = current?.Id;
            if (key != pageKey)
            {
                pageKey = key;
                RefreshCard(asking);
                RefreshTiles();
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
            f.Section(AvIcon.AdjustmentsHorizontal, "BUILD", "PRESET · SUPPLY FIT");
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
            card.Set(airframe != null ? LoadoutWords.Title(code, current?.Name) : "NO AIRFRAME", airframe != null
                ? LoadoutWords.Build(summary, current != null, summary.Refused) : "Pick an airframe below", chipShown, state, asking ? "warn" : state, icon,
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

        private AvSection liverySection;
        private AvRow liveryRow;
        private AvControl liveryPrev, liveryNext;
        private readonly List<WingLoadoutTemplates.LiveryOption> liveries = new List<WingLoadoutTemplates.LiveryOption>();
        private readonly List<string> liveryTokens = new List<string>();
        private AircraftDefinition liveryFor;
        private Faction liveryFaction;

        private void BuildLivery(AvFlow f)
        {
            liverySection = f.Section(AvIcon.Sticker, "LIVERY", "");
            liveryRow = f.Add(new AvRow(f.Content));
            liveryPrev = liveryRow.AddTrailing(new AvControl.Spec("", () => StepLivery(-1), AvButtonStyle.Quiet, AvIcon.ChevronLeft));
            liveryNext = liveryRow.AddTrailing(new AvControl.Spec("", () => StepLivery(1), AvButtonStyle.Quiet, AvIcon.ChevronRight));
            ids.Add("lo.livery.prev", liveryPrev);
            ids.Add("lo.livery.next", liveryNext);
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
            string code = airframe != null ? SupplyWords.Code(airframe.code, airframe.unitName) : WmcText.Unknown;
            liverySection.SetCaption(LoadoutWords.LiveryKey(code));
            int i = LiveryChoice.IndexOf(liveryTokens, WingLoadoutTemplates.LiveryTokenOf(airframe));
            bool can = airframe != null && liveries.Count > 1;
            liveryRow.Set(liveries.Count > 0 ? LoadoutWords.Livery(liveries[i].Label) : LoadoutWords.Standard, "", "", AvState.Info);
            string tip = can ? "The livery this airframe's wingmen wear (STANDARD: the faction's own)."
                : airframe == null ? "Pick an airframe first." : "This faction offers no other livery for it.";
            foreach (AvControl b in new[] { liveryPrev, liveryNext })
            {
                b.Interactable = can;
                b.Help = tip;
            }
            liveryRow.Help = tip;
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
