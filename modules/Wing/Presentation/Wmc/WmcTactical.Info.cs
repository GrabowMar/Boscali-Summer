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
    /// <summary>TACTICAL › ORDERS' two blocks (spec FUI §TACTICAL; 0.9's situation panes) on kit v2: THREATS — the side's tracked
    /// hostiles within 60 km, nearest first, hot ones in the danger state, each an <see cref="AvList"/> row with [ATK] for the scope and
    /// a click that centres the map — and FLIGHT POOL — what the scope still carries (one aircraft: its stations) and what the wing is
    /// doing now.</summary>
    internal sealed partial class WmcTactical
    {
        private const int PaneRows = 5;
        private const string AtkTip = "Send the scope (who orders go to) to attack it.";
        private const string NoThreats = "NONE TRACKED WITHIN 60 KM";

        private readonly AvControl[] threatAtk = new AvControl[PaneRows];
        private readonly ThreatRow[] threatCache = new ThreatRow[PaneRows];
        private readonly uint[] threatIds = new uint[PaneRows];
        private readonly List<string> storeLines = new List<string>(DetailLines.MaxStores);
        private AvSection threatSection, poolSection;
        private AvList threatList;
        private WmcLines poolLines;
        private MemberDetail storeScratch;
        private int threatsKey = int.MinValue, storesContent = int.MinValue, doingKey = int.MinValue;

        private void BuildPanes(AvFlow f)
        {
            threatSection = f.Section(AvIcon.Radar2, "THREATS", NoThreats);
            threatList = f.Add(new AvList(f.Content, ticker, PaneRows, BindThreat));
            threatList.RowClicked = CentreThreat;
            poolSection = f.Section(AvIcon.Stack2, "FLIGHT POOL");
            poolLines = f.Add(new WmcLines(f.Content, PaneRows));
            storeScratch.Stores = new StoreLine[DetailLines.MaxStores];
        }

        private void RefreshPanes(WmcContext c)
        {
            RefreshThreats(c);
            RefreshStores(c);
        }

        private void RefreshThreats(WmcContext c)
        {
            ThreatRow[] rows = null;
            int n = c.Wing != null ? c.Wing.Threats(out rows) : 0;
            if (n > PaneRows) n = PaneRows;
            int hot = 0, key = n;
            for (int i = 0; i < n; i++)
            {
                if (rows[i].Hot) hot++;
                key = key * 31 + (int)(rows[i].Id % 1000003u) * 31 + (int)(rows[i].RangeM / 500f) * 7 + (int)(rows[i].BearingDeg / 5f) + (rows[i].Hot ? 1 : 0);
            }
            if (key != threatsKey)
            {
                threatsKey = key;
                for (int i = 0; i < n; i++)
                {
                    threatCache[i] = rows[i];
                    threatIds[i] = rows[i].Id;
                }
                for (int i = n; i < PaneRows; i++) threatIds[i] = 0u;
                threatSection.SetCaption(n == 0 ? NoThreats : hot > 0 ? AvNum.Fixed(hot, 0) + " HOT · " + AvNum.Fixed(n, 0) : AvNum.Fixed(n, 0) + " TRACKED");
                threatList.SetCount(n);
                relayout = true;
            }
            for (int i = 0; i < n; i++)
            {
                if (threatAtk[i] == null) continue;
                threatAtk[i].Interactable = c.CanOrder;
                string tip = c.CanOrder ? AtkTip : "The host gives orders";
                if (!ReferenceEquals(threatAtk[i].Help, tip)) threatAtk[i].Help = tip;
            }
        }

        private void BindThreat(int i, AvRow row)
        {
            ThreatRow r = threatCache[i];
            AvState state = r.Hot ? AvState.Danger : WmcState.Of(WingThreatList.Rail(r));
            row.Set(AvStates.Glyph(state) + WingThreatList.Text(r), "", "", state);
            row.Help = "Centre the map on it.";
            ids.Add("tac.threat" + i, row);
            if (threatAtk[i] != null) return;
            int k = i;
            AvControl atk = row.AddTrailing(new AvControl.Spec("ATK", () => AttackThreat(k)));
            atk.Help = AtkTip;
            threatAtk[i] = atk;
            ids.Add("tac.threat" + i + ".atk", atk);
        }

        private void CentreThreat(int i)
        {
            if (i < 0 || i >= PaneRows) return;
            Unit u = WmcContext.UnitOf(threatIds[i]);
            if (u != null) WmcMap.Center(u);
        }

        /// <summary>The same Attack a right-click on the unit sends while ATTACK is armed (review focus 4: a unit gone since the
        /// sample does nothing).</summary>
        private void AttackThreat(int i)
        {
            Unit u = WmcContext.UnitOf(threatIds[i]);
            if (last == null || u == null || u.disabled) return;
            WmcMotion.Punch(threatAtk[i]);
            WmcUi.Order(last, () =>
            {
                WingOrders.Run(new WingOrder { Kind = OrderKind.Attack, Units = new[] { u.persistentID.Id }, Scope = last.Scope });
                FlashScope();
            });
        }

        /// <summary>The scope's pool (every scoped member's stations summed), or one aircraft's stations; the host's only. The last
        /// line is what the wing is doing now.</summary>
        private void RefreshStores(WmcContext c)
        {
            int key, members = 0;
            if (!c.CanOrder)
            {
                key = c.Client ? 1 : 2;
                if (key != storesContent)
                {
                    storeLines.Clear();
                    storeLines.Add(c.Client ? "The host's stores are not shared" : "NO WING");
                }
            }
            else
            {
                var totals = new PoolTotals();
                WingMember only = null;
                foreach (WingMember m in c.Wing.Members)
                {
                    if (m.Released || !m.Alive || (object)m.Aircraft == null) continue;
                    int i = WingRows.IndexOf(c.Rows, c.Count, m.Aircraft.persistentID.Id);
                    if (i < 0 || !InScope(c, c.Rows[i])) continue;
                    WmcDetail.Stores(m, ref storeScratch);
                    for (int st = 0; st < storeScratch.StoreCount; st++) FlightPool.Add(ref totals, storeScratch.Stores[st]);
                    only = m;
                    members++;
                }
                if (members == 1) WmcDetail.Stores(only, ref storeScratch);
                key = members == 1 ? StationsKey(storeScratch)
                    : (totals.AirMissiles * 7919 + totals.Agm * 131 + totals.Bombs * 31 + totals.GunRounds * 3 + totals.Ecm) * 11 + members;
                if (key != storesContent)
                {
                    if (members == 1) FlightPool.StationLines(storeScratch, storeLines);
                    else FlightPool.Lines(totals, storeLines);
                }
            }
            bool changed = false;
            if (key != storesContent)
            {
                storesContent = key;
                for (int i = 0; i < PaneRows - 1; i++) changed |= poolLines.Set(i, i < storeLines.Count ? storeLines[i] : "");
                poolSection.SetCaption(members == 1 ? "ONE AIRCRAFT" : members > 1 ? AvNum.Fixed(members, 0) + " AC" : "");
            }
            int doing = 0;
            for (int i = 0; i < c.Count; i++) doing = doing * 7 + c.Rows[i].Duty + 1;
            if (doing != doingKey)
            {
                doingKey = doing;
                changed |= poolLines.Set(PaneRows - 1, WmcWords.Posture(c.Rows, c.Count));
            }
            if (changed) relayout = true;
        }

        private static int StationsKey(in MemberDetail d)
        {
            int k = d.StoreCount;
            for (int i = 0; i < d.StoreCount; i++) k = k * 31 + d.Stores[i].Ammo;
            return k;
        }
    }
}
