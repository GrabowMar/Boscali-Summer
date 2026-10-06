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
    // LOADOUT's HARDPOINTS: a station map beside the station table (a station is one row everywhere, a pair counts once, only ✕ empties
    // one, a blocked station names what blocks it), and the store picker kept open below for the selected station.
    internal sealed partial class WmcLoadout
    {
        private const string EmptyStationText = "— EMPTY —";

        private WmcHeadBar hpHead;
        private WmcLines hpEmpty;
        private WmcStationBoard hpBoard;
        private WmcStorePicker picker;
        private int selStation = -1, pickerPage;
        private bool hpResetPending;
        private readonly List<WingLoadoutCatalog.StoreOption> stores = new List<WingLoadoutCatalog.StoreOption>();
        private readonly List<int> pylonCounts = new List<int>();

        private void BuildHardpoints(AvFlow f)
        {
            hpHead = f.Add(new WmcHeadBar(f.Content, AvIcon.Stack2, "HARDPOINTS"));
            hpEmpty = f.Add(new WmcLines(f.Content, 1));
            hpBoard = f.Add(new WmcStationBoard(f.Content, ids, SelectStation, ClearStation, TurnStations));
            picker = f.Add(new WmcStorePicker(f.Content, ids, PickStore, TurnStores));
        }

        /// <summary>The stations in words, the map and the picker for the selected station; the head counts stations once, as the metric does.</summary>
        private void RefreshHardpoints()
        {
            int stations = layout != null ? layout.Stations : 0;
            string note = card.Title;
            if (layout != null && summary.Blocked > 0)
                note += " · " + AvNum.Fixed(summary.Blocked, 0) + " BLOCKED";
            hpHead.SetCaption(note);
            string empty = airframe == null ? "Pick an airframe above." : layout == null ? LoadoutWords.Unreadable
                : current == null ? "NO TEMPLATE · NEW starts one for this airframe" : null;
            hpEmpty.Set(0, empty ?? "");
            int n = empty == null ? stations : 0;
            if (hpResetPending)
            {
                hpResetPending = false;
                selStation = -1;
                pickerPage = 0;
                hpBoard.SetPage(0);
            }
            pylonCounts.Clear();
            for (int st = 0; st < n; st++) pylonCounts.Add(layout.PylonsOf(st));
            hpBoard.SetStations(n, pylonCounts);
            // The selection: kept while it exists, else the first station that takes a store.
            if (selStation < 0 || selStation >= n)
            {
                selStation = -1;
                for (int st = 0; st < n && selStation < 0; st++)
                    if (CanPick(st)) selStation = st;
                if (selStation < 0 && n > 0) selStation = 0;
                pickerPage = 0;
            }
            for (int st = 0; st < n; st++) BindStation(st);
            hpBoard.SetSelected(selStation);
            hpBoard.Rebuild();
            RefreshPicker(n);
        }

        /// <summary>A fitted station always opens (to change it); an empty one another store blocks takes nothing.</summary>
        private bool CanPick(int st)
        {
            if (layout == null || st < 0 || st >= layout.Stations) return false;
            int set = layout.First(st);
            return !(set < setOptions.Length && setOptions[set].IsEmpty && layout.BlockedBy(KnownKeys(), st) >= 0);
        }

        /// <summary>A station's words: its store (or what blocks it, or EMPTY), its mass, and ✕ while a store is fitted.</summary>
        private void BindStation(int st)
        {
            int set = layout.First(st), pylons = layout.PylonsOf(st);
            WingLoadoutCatalog.StoreOption o = setOptions[set];
            string storeText, mass;
            int blockedBy = layout.BlockedBy(KnownKeys(), st);
            bool fitted = !o.IsEmpty;
            StoreVerdict verdict = StoreVerdict.Ok;
            if (fitted)
            {
                MountFacts f = WingLoadoutCatalog.FactsOf(o, pylons, hq);
                f.Blocked = set < clearedSets.Length && clearedSets[set];
                verdict = StoreRules.Check(f, mission);
                storeText = StoreWords.Row(o.Known ? o.Label : null, verdict, RankFor(o));
                mass = StoreRules.Flies(verdict) ? LoadoutWords.RowMass(o.Mass * pylons) : WmcText.Unknown;
            }
            else if (blockedBy >= 0)
            {
                storeText = StoreWords.BlockedBy(layout.Name(blockedBy));
                mass = WmcText.Unknown;
            }
            else
            {
                storeText = EmptyStationText;
                mass = WmcText.Unknown;
            }
            bool flies = fitted && StoreRules.Flies(verdict);
            bool can = fitted || blockedBy < 0;
            string tip = LoadoutWords.StationName(layout.Name(st), pylons) + " · " + (!can ? StoreWords.Why(StoreVerdict.Blocked, 0)
                : fitted && !flies ? StoreWords.Why(verdict, RankFor(o)) : "Pick the store for " + layout.Name(st) + ".");
            hpBoard.Bind(st, storeText, mass, flies ? "ready" : fitted ? "caution" : "inert", fitted, can, tip);
        }

        /// <summary>The keys with unknown stores left out (they never reach the launch, so they block nothing).</summary>
        private List<string> KnownKeys()
        {
            knownKeys.Clear();
            for (int s = 0; s < keys.Count; s++) knownKeys.Add(s < setFacts.Length && setFacts[s].Known ? keys[s] : null);
            return knownKeys;
        }

        private readonly List<string> knownKeys = new List<string>();

        private void SelectStation(int st)
        {
            if (layout == null || st < 0 || st >= layout.Stations) return;
            selStation = st;
            pickerPage = 0;
            WmcPanel.Instance?.Refresh();
        }

        private void TurnStations(int dir)
        {
            hpBoard.SetPage(hpBoard.Page + dir);
            pageKey = int.MinValue;
            WmcPanel.Instance?.Refresh();
        }

        // ---------------------------------------------------------------- the store picker

        private struct PickerEntry
        {
            public string Key, Name, Verdict, Mass, Level, Tip;
            public bool Pickable, Selected;
        }

        private readonly List<PickerEntry> entries = new List<PickerEntry>();

        /// <summary>Every store the selected station can carry: those that fly first, nuclear ones the mission holds back next (pickable, they
        /// launch empty until they clear), the rest last with their verdict word and disabled; EMPTY ends the list. A station another
        /// store blocks says so instead.</summary>
        private void RefreshPicker(int stations)
        {
            entries.Clear();
            popupClear = true;
            if (current == null || layout == null || airframe == null || selStation < 0 || selStation >= stations)
            {
                picker.SetHead("STORES", "");
                picker.SetBody(airframe == null ? "Pick an airframe above." : current == null ? "NO TEMPLATE · NEW starts one for this airframe" : "");
                picker.SetCount(0);
                picker.SetPaging(0, 0);
                return;
            }
            int st = selStation, set = layout.First(st), pylons = layout.PylonsOf(st);
            WingLoadoutCatalog.StoreOption held = setOptions[set];
            picker.SetHead(LoadoutWords.PickerHead(st, layout.Name(st)), LoadoutWords.PickerHolds(!held.IsEmpty && held.Known ? held.Label : null));
            int blockedBy = layout.BlockedBy(KnownKeys(), st);
            if (held.IsEmpty && blockedBy >= 0)
            {
                picker.SetBody(StoreWords.BlockedBy(layout.Name(blockedBy)) + " · " + StoreWords.Why(StoreVerdict.Blocked, 0));
                picker.SetCount(0);
                picker.SetPaging(0, 0);
                return;
            }
            picker.SetBody(null);
            WingLoadoutCatalog.StoresFor(airframe, set, stores);
            string key = layout.KeyOf(keys, st);
            for (int pass = 0; pass < 3; pass++)
                foreach (WingLoadoutCatalog.StoreOption o in stores)
                {
                    StoreVerdict v = StoreRules.Check(WingLoadoutCatalog.FactsOf(o, pylons, hq), mission);
                    // The game's own menu hides what the build lacks, switched off or event-only; everything else is listed.
                    if (v == StoreVerdict.Disabled || v == StoreVerdict.EventOnly || v == StoreVerdict.Missing) continue;
                    int group = v == StoreVerdict.Ok ? 0 : StoreRules.Pickable(v) ? 1 : 2;
                    if (group != pass) continue;
                    entries.Add(new PickerEntry
                    {
                        Key = o.Key, Name = WmcText.Cut(o.Label, LoadoutWords.StoreChars), Verdict = StoreWords.Detail(v, o.Kind, o.Ammo, RankFor(o)),
                        Mass = LoadoutWords.RowMass(o.Mass * pylons), Level = v == StoreVerdict.Ok ? "ok" : "warn", Pickable = StoreRules.Pickable(v),
                        Selected = o.Key == key, Tip = StoreRules.Pickable(v) ? "Fit " + o.Label + "." : StoreWords.Why(v, RankFor(o)),
                    });
                }
            entries.Add(new PickerEntry
            {
                Key = null, Name = LoadoutWords.EmptyStore, Verdict = "CLEAR THE STATION", Mass = "0 kg", Level = "", Pickable = true,
                Selected = held.IsEmpty, Tip = "Empty this station.",
            });
            pickerPage = Mathf.Clamp(pickerPage, 0, (entries.Count - 1) / WmcStorePicker.PerPage);
            int first = pickerPage * WmcStorePicker.PerPage, shown = Mathf.Min(WmcStorePicker.PerPage, entries.Count - first);
            picker.SetCount(shown);
            for (int i = 0; i < shown; i++)
            {
                PickerEntry e = entries[first + i];
                picker.Bind(i, e.Name, e.Verdict, e.Mass, e.Level, e.Selected, e.Pickable, e.Tip);
            }
            picker.SetPaging(pickerPage, entries.Count);
        }

        private void TurnStores(int dir)
        {
            pickerPage = Mathf.Max(0, pickerPage + dir);
            pageKey = int.MinValue;
            WmcPanel.Instance?.Refresh();
        }

        /// <summary>A cell of the picker (its index in the whole list): the store, or EMPTY.</summary>
        private void PickStore(int i)
        {
            if (current == null || layout == null || selStation < 0 || i < 0 || i >= entries.Count || !entries[i].Pickable) return;
            if (entries[i].Key == null)
            {
                ClearStation(selStation);
                return;
            }
            int emptied = layout.Pick(keys, selStation, entries[i].Key);
            if (emptied < 0)
            {
                WingToast.Show(StoreWords.Why(StoreVerdict.Blocked, 0));
                return;
            }
            WingLoadoutTemplates.SetMounts(current, keys);
            if (emptied > 0)
                WingToast.Show(emptied == 1 ? "1 station emptied: the new store blocks it" : AvNum.Fixed(emptied, 0) + " stations emptied: the new store blocks them");
            WmcPanel.Instance?.Refresh();
        }

        private void ClearStation(int st)
        {
            if (current == null || layout == null) return;
            if (st < 0 || st >= layout.Stations) return;
            layout.Clear(keys, st);
            WingLoadoutTemplates.SetMounts(current, keys);
            WmcPanel.Instance?.Refresh();
        }
    }
}
