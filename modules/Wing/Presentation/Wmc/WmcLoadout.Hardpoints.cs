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
    // LOADOUT's HARDPOINTS table: one row per station (a pair counts once and says ×n), the store and its mass, CLEAR only when a store
    // is fitted, a blocked station naming what blocks it, and the store popup beside its row.
    internal sealed partial class WmcLoadout
    {
        private sealed class StationRow
        {
            public int Item;
            public AvRow Row;
            public AvControl Clear;
        }

        private const string EmptyStationText = "— EMPTY —";
        private const int RowsPerPage = BezelLayout.HpRowsMax;

        private readonly Dictionary<AvRow, StationRow> stationRows = new Dictionary<AvRow, StationRow>(RowsPerPage);
        private readonly StationRow[] slotRows = new StationRow[RowsPerPage];
        private int popupStation = -1;
        private bool hpResetPending;
        private AvSection hpSection;
        private AvList hpList;
        private WmcLines hpEmpty;
        private readonly List<WingLoadoutCatalog.StoreOption> stores = new List<WingLoadoutCatalog.StoreOption>();
        private readonly List<string> storeKeys = new List<string>();
        private readonly List<AvPopupEntry> storeEntries = new List<AvPopupEntry>();

        private void BuildHardpoints(AvFlow f)
        {
            hpSection = f.Section(AvIcon.Stack2, "HARDPOINTS", "");
            hpEmpty = f.Add(new WmcLines(f.Content, 1));
            hpList = f.Add(new AvList(f.Content, ticker, RowsPerPage, BindStation));
            hpList.RowClicked = OpenStores;
            WmcListPaging.Register(ids, "lo.hp.", hpList, RowsPerPage);
        }

        /// <summary>This page of stations in words; the head counts stations once, as the metric does.</summary>
        private void RefreshHardpoints()
        {
            int stations = layout != null ? layout.Stations : 0;
            string note = card.Title;
            if (layout != null && summary.Blocked > 0)
                note += " · " + AvNum.Fixed(summary.Blocked, 0) + " BLOCKED";
            hpSection.SetCaption(note);
            string empty = airframe == null ? "Pick an airframe above." : layout == null ? LoadoutWords.Unreadable
                : current == null ? "NO TEMPLATE · NEW starts one for this airframe" : null;
            hpEmpty.Set(0, empty ?? "");
            hpList.SetCount(empty == null ? stations : 0);
            if (hpResetPending)
            {
                hpResetPending = false;
                hpList.Reveal(0);
            }
        }

        /// <summary>A station's row: the station, its store (or what blocks it, or EMPTY), its mass, and CLEAR while a store is fitted.</summary>
        private void BindStation(int item, AvRow row)
        {
            if (!stationRows.TryGetValue(row, out StationRow r))
            {
                r = new StationRow { Row = row };
                StationRow made = r;
                made.Clear = row.AddTrailing(new AvControl.Spec("CLEAR", () => ClearStation(made.Item), AvButtonStyle.Default, AvIcon.Eraser));
                made.Clear.Help = "Empty this station.";
                stationRows[row] = made;
            }
            r.Item = item;
            slotRows[item % RowsPerPage] = r;
            ids.Add("lo.hp" + item % RowsPerPage, row);
            ids.Add("lo.hp" + item % RowsPerPage + ".clear", r.Clear);
            if (layout == null || item >= layout.Stations) return;
            int st = item;
            int set = layout.First(st), pylons = layout.PylonsOf(st);
            WingLoadoutCatalog.StoreOption o = setOptions[set];
            string name = LoadoutWords.StationName(layout.Name(st), pylons), storeText, mass;
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
            AvState state = flies ? AvState.Ready : fitted ? AvState.Caution : AvState.Inert;
            row.Set(name, AvStates.Glyph(state) + storeText, mass, state);
            // An empty station another store blocks takes nothing; a fitted one always opens (to change it) and says why it will not fly.
            bool can = fitted || blockedBy < 0;
            row.Interactable = can;
            row.Help = !can ? StoreWords.Why(StoreVerdict.Blocked, 0) : fitted && !flies ? StoreWords.Why(verdict, RankFor(o))
                : "Pick the store for " + layout.Name(st) + ".";
            r.Clear.gameObject.SetActive(fitted);
        }

        /// <summary>The keys with unknown stores left out (they never reach the launch, so they block nothing).</summary>
        private List<string> KnownKeys()
        {
            knownKeys.Clear();
            for (int s = 0; s < keys.Count; s++) knownKeys.Add(s < setFacts.Length && setFacts[s].Known ? keys[s] : null);
            return knownKeys;
        }

        private readonly List<string> knownKeys = new List<string>();

        // ---------------------------------------------------------------- the store popup

        /// <summary>The stores station <paramref name="st"/> can carry: those that fly first, nuclear ones the mission holds back next
        /// (pickable, they launch empty until they clear), restricted ones last and disabled; the popup beside its row.</summary>
        private void OpenStores(int st)
        {
            if (current == null || layout == null) return;
            if (st < 0 || st >= layout.Stations) return;
            int set = layout.First(st), pylons = layout.PylonsOf(st);
            // A station another store blocks takes nothing: its row does not open.
            if (set < setOptions.Length && setOptions[set].IsEmpty && layout.BlockedBy(KnownKeys(), st) >= 0) return;
            WingLoadoutCatalog.StoresFor(airframe, set, stores);
            storeEntries.Clear();
            storeKeys.Clear();
            string key = layout.KeyOf(keys, st);
            for (int pass = 0; pass < 3; pass++)
                foreach (WingLoadoutCatalog.StoreOption o in stores)
                {
                    StoreVerdict v = StoreRules.Check(WingLoadoutCatalog.FactsOf(o, pylons, hq), mission);
                    if (!StoreRules.Offered(v)) continue;
                    int group = v == StoreVerdict.Ok ? 0 : StoreRules.Pickable(v) ? 1 : 2;
                    if (group != pass) continue;
                    storeEntries.Add(new AvPopupEntry(WmcText.Cut(o.Label, LoadoutWords.StoreChars),
                        StoreWords.Detail(v, o.Kind, o.Ammo, RankFor(o)), o.Key == key, StoreRules.Pickable(v)));
                    storeKeys.Add(o.Key);
                }
            if (storeEntries.Count == 0)
            {
                WingToast.Show("No store for " + layout.Name(st) + " is allowed here");
                return;
            }
            popupStation = st;
            StationRow slot = slotRows[st % RowsPerPage];
            RectTransform rowRect = slot != null ? slot.Row.Rect : hpList.Rect;
            Rect area = WmcPopup.Area(flow.Content, rowRect, storeEntries.Count);
            NotePopup(area, rowRect);
            popup.Show(area, storeEntries, PickStore);
        }

        private void PickStore(int i)
        {
            if (current == null || layout == null || popupStation < 0 || i < 0 || i >= storeKeys.Count) return;
            int emptied = layout.Pick(keys, popupStation, storeKeys[i]);
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
