using NOAvionics;
using System.Collections.Generic;
using UnityEngine;

using BoscaliSummer.Modules.Wing.Domain;
namespace BoscaliSummer.Modules.Wing.Presentation
{
    // LOADOUT's HARDPOINTS: a station map beside the station table (each side of a pair has its own row, only ✕ empties
    // one, a blocked station names what blocks it), and the store picker kept open below for the selected station.
    internal sealed partial class WmcLoadout
    {
        private const string EmptyStationText = "— EMPTY —";

        private WmcHeadBar hpHead;
        private WmcLines hpEmpty;
        private WmcStationBoard hpBoard;
        private WmcStorePicker picker;
        private int selStation = -1, pickerPage;
        private readonly List<WingLoadoutCatalog.StoreOption> stores = new List<WingLoadoutCatalog.StoreOption>();
        private readonly List<int> pylonCounts = new List<int>();
        private readonly List<bool> mirroredStations = new List<bool>();

        private void BuildHardpoints(AvFlow f)
        {
            hpHead = f.Add(new WmcHeadBar(f.Content, AvIcon.Stack2, "HARDPOINTS"));
            hpEmpty = f.Add(new WmcLines(f.Content, 1));
            hpBoard = f.Add(new WmcStationBoard(f.Content, SelectStation, ClearStation, TurnStations, ToggleLink));
            picker = f.Add(new WmcStorePicker(f.Content, PickStore, TurnStores));
        }

        /// <summary>The stations in words, the map and the picker for the selected station; the head counts stations once, as the metric does.</summary>
        private void RefreshHardpoints()
        {
            int stations = layout != null ? layout.Stations : 0;
            // The card above names the airframe and fit; the head counts what is fitted.
            string note = layout != null ? LoadoutWords.Stations(summary) + " FITTED" : "";
            if (layout != null && summary.Blocked > 0)
                note += " · " + AvNum.Fixed(summary.Blocked, 0) + " BLOCKED";
            hpHead.SetCaption(note);
            string empty = airframe == null ? LoadoutWords.NoAirframe : layout == null ? LoadoutWords.Unreadable : null;
            hpEmpty.Set(0, empty ?? "");
            int n = empty == null ? stations : 0;
            pylonCounts.Clear();
            mirroredStations.Clear();
            for (int st = 0; st < n; st++)
            {
                pylonCounts.Add(layout.PylonsAt(st));
                mirroredStations.Add(layout.MirrorsPrevious(st));
            }
            hpBoard.SetStations(n, pylonCounts, mirroredStations);
            // The selection: kept while it exists, else the first station that takes a store (the table turns to its page).
            if (selStation < 0 || selStation >= n)
            {
                selStation = -1;
                for (int st = 0; st < n && selStation < 0; st++)
                    if (CanPick(st)) selStation = st;
                if (selStation < 0 && n > 0) selStation = 0;
                pickerPage = 0;
                hpBoard.SetPage(Mathf.Max(0, selStation) / WmcStationBoard.RowsPerPage);
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
            return !(st < setOptions.Length && setOptions[st].IsEmpty && layout.BlockedBy(KnownKeys(), st) >= 0);
        }

        /// <summary>A station's words: its store (or what blocks it, or EMPTY), and ✕ while a store is fitted.</summary>
        private void BindStation(int st)
        {
            int pylons = layout.PylonsAt(st);
            WingLoadoutCatalog.StoreOption o = setOptions[st];
            string storeText;
            int blockedBy = layout.BlockedBy(KnownKeys(), st);
            bool fitted = !o.IsEmpty;
            StoreVerdict verdict = StoreVerdict.Ok;
            if (fitted)
            {
                MountFacts f = WingLoadoutCatalog.FactsOf(o, hq);
                f.Blocked = st < clearedSets.Length && clearedSets[st];
                verdict = StoreRules.Check(f, mission);
                storeText = StoreWords.Row(o.Known ? o.Label : null, verdict, RankFor(o));
            }
            else
                storeText = blockedBy >= 0 ? StoreWords.BlockedBy(layout.Name(blockedBy)) : EmptyStationText;
            bool flies = fitted && StoreRules.Flies(verdict);
            bool can = fitted || blockedBy < 0;
            string tip = LoadoutWords.StationName(layout.Name(st), pylons) + " · " + (!can ? StoreWords.Why(StoreVerdict.Blocked, 0)
                : fitted && !flies ? StoreWords.Why(verdict, RankFor(o)) : client ? LoadoutWords.ClientWhy : "Pick the store for " + layout.Name(st) + ".");
            int link = client || layout.Partner(st) < 0 ? 0 : Linked(st) ? 1 : 2;
            hpBoard.Bind(st, storeText, flies ? "ready" : fitted ? "caution" : "inert", fitted && !client, can, tip, link);
        }

        // ---------------------------------------------------------------- saving

        private readonly List<string> standard = new List<string>();

        /// <summary>Saves the edited keys. The first change starts the custom loadout (from the standard stores as edited); keys that
        /// still equal the standard stores save nothing.</summary>
        private void Commit()
        {
            if (current != null)
            {
                WingLoadoutTemplates.SetMounts(current, keys);
                return;
            }
            WingLoadoutCatalog.StandardKeys(airframe, standard);
            layout.Normalize(standard);
            bool same = standard.Count == keys.Count;
            for (int i = 0; i < keys.Count && same; i++) same = standard[i] == keys[i];
            if (!same) current = WingLoadoutTemplates.Start(airframe, keys);
        }

        // ---------------------------------------------------------------- symmetry pairs (the game's own link / split)

        /// <summary>Pairs the player split, by their lower station; cleared with the airframe. <see cref="splitVersion"/> moves with it, so
        /// the page redraws.</summary>
        private readonly HashSet<int> splitPairs = new HashSet<int>();
        private int splitVersion;

        /// <summary>A pair is linked (a pick or ✕ fits both sides) until split, and only while both sides hold the same store: a saved
        /// asymmetric fit opens split.</summary>
        private bool Linked(int st)
        {
            int p = layout != null ? layout.Partner(st) : -1;
            return p >= 0 && !splitPairs.Contains(Mathf.Min(st, p)) && layout.KeyOf(keys, st) == layout.KeyOf(keys, p);
        }

        /// <summary>Split a linked pair; link a split one, the other side taking this side's store.</summary>
        private void ToggleLink(int st)
        {
            if (!Editable || st < 0 || st >= layout.Stations) return;
            int p = layout.Partner(st);
            if (p < 0) return;
            splitVersion++;
            if (Linked(st))
            {
                splitPairs.Add(Mathf.Min(st, p));
                WmcPanel.Instance?.Refresh();
                return;
            }
            splitPairs.Remove(Mathf.Min(st, p));
            string key = layout.KeyOf(keys, st);
            if (string.IsNullOrEmpty(key)) layout.Clear(keys, p);
            else if (layout.Pick(keys, p, key, KnownKeys()) < 0) WingToast.Show(StoreWords.Why(StoreVerdict.Blocked, 0));
            Commit();
            WmcPanel.Instance?.Refresh();
        }

        /// <summary>The keys with unknown stores left out (they never reach the launch, so they block nothing).</summary>
        private List<string> KnownKeys()
        {
            knownKeys.Clear();
            for (int s = 0; s < keys.Count; s++) knownKeys.Add(s < setFacts.Length && setFacts[s].Known ? keys[s] : null);
            return knownKeys;
        }

        private readonly List<string> knownKeys = new List<string>();

        /// <summary>A click on the map or the table: the station is selected and the table turns to its page.</summary>
        private void SelectStation(int st)
        {
            if (layout == null || st < 0 || st >= layout.Stations) return;
            selStation = st;
            pickerPage = 0;
            hpBoard.SetPage(st / WmcStationBoard.RowsPerPage);
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
            public string Key, Name, Verdict, Level, Tip;
            public bool Selected;
        }

        private readonly List<PickerEntry> entries = new List<PickerEntry>();

        /// <summary>The stores the selected station can carry that fly, then nuclear ones the mission holds back (pickable, they launch
        /// empty until they clear); EMPTY ends the list. A station another store blocks says so instead; a client sees why it cannot edit.</summary>
        private void RefreshPicker(int stations)
        {
            entries.Clear();
            if (layout == null || airframe == null || selStation < 0 || selStation >= stations)
            {
                picker.SetHead("STORES", "");
                picker.SetBody(airframe == null ? LoadoutWords.NoAirframe : layout == null ? LoadoutWords.Unreadable : "");
                picker.SetCount(0);
                picker.SetPaging(0, 0);
                return;
            }
            int st = selStation, pylons = layout.PylonsAt(st);
            WingLoadoutCatalog.StoreOption held = setOptions[st];
            picker.SetHead(LoadoutWords.PickerHead(st, layout.Name(st)), LoadoutWords.PickerHolds(!held.IsEmpty && held.Known ? held.Label : null));
            int blockedBy = layout.BlockedBy(KnownKeys(), st);
            if (client || (held.IsEmpty && blockedBy >= 0))
            {
                picker.SetBody(client ? LoadoutWords.ClientWhy : StoreWords.BlockedBy(layout.Name(blockedBy)) + " · " + StoreWords.Why(StoreVerdict.Blocked, 0));
                picker.SetCount(0);
                picker.SetPaging(0, 0);
                return;
            }
            picker.SetBody(null);
            WingLoadoutCatalog.StoresFor(airframe, st, stores);
            string key = layout.KeyOf(keys, st);
            for (int pass = 0; pass < 2; pass++)
                foreach (WingLoadoutCatalog.StoreOption o in stores)
                {
                    StoreVerdict v = StoreRules.Check(WingLoadoutCatalog.FactsOf(o, hq), mission);
                    // Only what can be fitted is listed: a store the build lacks, the game switched off or the mission restricts is not.
                    if (!StoreRules.Pickable(v) || (v == StoreVerdict.Ok ? 0 : 1) != pass) continue;
                    entries.Add(new PickerEntry
                    {
                        Key = o.Key, Name = WmcText.Cut(o.Label, LoadoutWords.StoreChars), Verdict = StoreWords.Detail(v, o.Kind, o.Ammo, RankFor(o)),
                        Level = v == StoreVerdict.Ok ? "ok" : "warn", Selected = o.Key == key,
                        Tip = v == StoreVerdict.Ok ? "Fit " + o.Label + "." : StoreWords.Why(v, RankFor(o)),
                    });
                }
            entries.Add(new PickerEntry { Key = null, Name = LoadoutWords.EmptyStore, Verdict = "CLEAR THE STATION", Level = "", Selected = held.IsEmpty, Tip = "Empty this station." });
            pickerPage = Mathf.Clamp(pickerPage, 0, (entries.Count - 1) / WmcStorePicker.PerPage);
            int first = pickerPage * WmcStorePicker.PerPage, shown = Mathf.Min(WmcStorePicker.PerPage, entries.Count - first);
            picker.SetCount(shown);
            for (int i = 0; i < shown; i++)
            {
                PickerEntry e = entries[first + i];
                picker.Bind(i, e.Name, e.Verdict, e.Level, e.Selected, e.Tip);
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
            if (!Editable || selStation < 0 || i < 0 || i >= entries.Count) return;
            if (entries[i].Key == null)
            {
                ClearStation(selStation);
                return;
            }
            bool linked = Linked(selStation);
            int emptied = layout.Pick(keys, selStation, entries[i].Key, KnownKeys());
            if (emptied < 0)
            {
                WingToast.Show(StoreWords.Why(StoreVerdict.Blocked, 0));
                return;
            }
            // A blocked other side stays as it was; the pair then reads split.
            if (linked) emptied += Mathf.Max(0, layout.Pick(keys, layout.Partner(selStation), entries[i].Key, KnownKeys()));
            Commit();
            if (emptied > 0)
                WingToast.Show(emptied == 1 ? "1 station emptied: the new store blocks it" : AvNum.Fixed(emptied, 0) + " stations emptied: the new store blocks them");
            WmcPanel.Instance?.Refresh();
        }

        private void ClearStation(int st)
        {
            if (!Editable || st < 0 || st >= layout.Stations) return;
            if (Linked(st)) layout.Clear(keys, layout.Partner(st));
            layout.Clear(keys, st);
            Commit();
            WmcPanel.Instance?.Refresh();
        }
    }
}
