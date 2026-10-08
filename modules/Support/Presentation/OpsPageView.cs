using System;
using System.Collections.Generic;
using BoscaliSummer.Modules.Support.Domain.C2;
using BoscaliSummer.Modules.Support.Domain.Calls;
using BoscaliSummer.Modules.Support.Domain.Fronts;
using BoscaliSummer.Modules.Support.Presentation.Fronts;
using BoscaliSummer.Modules.Support.Runtime;
using NOAvionics;

namespace BoscaliSummer.Modules.Support.Presentation
{
    /// <summary>One front card of the OPS page, already worded.</summary>
    internal struct FrontCardView
    {
        public Front Front;
        public bool Known;
        public int Readiness, Superiority;
        public string Word, Directive, Building, BuildNote, Alert, Queue;
        public int Unlocked, ReadyPerks, FundingPct;
        public ushort Budget;
        public AvState Tone, AlertTone;
        public bool HasBuild;
        public float BuildProgress;
    }

    /// <summary>One perk row of the PERKS tab.</summary>
    internal struct PerkRowView
    {
        public SupportActionId Id;
        public Front Front;
        public int Rung;
        public string Name, RungWord, Price, State, Button;
        public AvState Tone;
        public bool Enabled, Armed;
    }

    /// <summary>Everything the OPS page paints. Built each refresh by <see cref="OpsPageViews"/> from the manager, the controller and the front mirror.</summary>
    internal sealed class OpsPageView
    {
        public const int FavouriteSlots = 4;

        public int Allocation, PerksReady, PerksTotal, FrontsAhead, Alerts;
        public readonly FrontCardView[] Cards = new FrontCardView[FrontRules.FrontCount];
        public readonly List<PerkRowView> Perks = new List<PerkRowView>(16);
        public readonly CallTile?[] Favourites = new CallTile?[FavouriteSlots];
        public string ArmedName = "", AimLine = "", OrderRight = "", Words = "";
        public AvState WordsTone = AvState.Ready;
        public bool Armed, Pending;
        public string Alert = "", FrontWords = "";
        public AvState FrontWordsTone = AvState.Inert;
    }

    /// <summary>The pure mapping from the mirror and the tiles onto <see cref="OpsPageView"/>.</summary>
    internal static class OpsPageViews
    {
        public static void Cards(OpsPageView v, FrontStateData d, bool known, float now)
        {
            int ahead = 0, alerts = 0;
            for (int i = 0; i < FrontRules.FrontCount; i++)
            {
                var f = (Front)i;
                FrontCardView c = default;
                c.Front = f; c.Known = known;
                FrontRow row = known && d != null ? d.Fronts[i] : null;
                if (row == null)
                {
                    c.Readiness = 1; c.Word = "NO LINK"; c.Tone = AvState.Inert; c.Directive = FrontRules.Name(FrontRules.DefaultDirective(f));
                    c.Building = "WAITING FOR THE HOST"; c.Queue = ""; c.Alert = "FRONT STATE NOT RECEIVED YET"; c.AlertTone = AvState.Inert;
                    v.Cards[i] = c;
                    continue;
                }
                c.Readiness = row.Readiness;
                c.Superiority = row.Superiority;
                c.FundingPct = row.PriorityPct; c.Budget = row.Budget;
                foreach (CallRow r in CallSheet.Rows) if (r.Front == f && r.Rung <= row.Readiness) c.Unlocked++;
                c.Word = FrontSuperiority.Word(row.Superiority);
                c.Tone = row.Superiority >= 20 ? AvState.Ready : row.Superiority > -20 ? AvState.Info : row.Superiority > -60 ? AvState.Caution : AvState.Danger;
                c.Directive = FrontRules.Name(row.Directive) + (row.DirectiveLockUntil > now ? " · LOCKED " + FrontRules.Clock(row.DirectiveLockUntil - now) : "");
                if (row.Superiority >= 20) ahead++;
                if (row.Superiority <= -20) alerts++;

                // The programme being built, else the first one filling, else nothing.
                int pick = -1;
                for (int q = 0; q < row.Queue.Count; q++) if (row.Queue[q].Percent >= 100) { pick = q; break; }
                if (pick < 0 && row.Queue.Count > 0) pick = 0;
                if (pick >= 0)
                {
                    FrontQueueRow q = row.Queue[pick];
                    bool building = q.Percent >= 100;
                    float stamp = d.Now;
                    int left = (int)Math.Max(0f, Math.Ceiling(q.BuildLeft - (building ? now - stamp : 0f)));
                    float build = Math.Max(1f, FrontRules.BuildSeconds(q.Id, row.Readiness));
                    c.HasBuild = true;
                    c.Building = (q.Id == ProgrammeId.Readiness ? "READINESS " + row.Readiness + " → " + Math.Min(FrontRules.MaxReadiness, row.Readiness + 1) : FrontRules.Info(q.Id).Name);
                    c.BuildProgress = building ? 1f - left / build : q.Percent / 100f;
                    c.BuildNote = building ? "BUILD " + FrontRules.Clock(left) : "FUNDED " + q.Percent + " %";
                }
                else { c.Building = "NOTHING QUEUED"; c.BuildNote = ""; }

                // The alert line: the newest log row (the log is oldest first).
                if (row.Log.Count > 0)
                {
                    FrontLogRow l = row.Log[row.Log.Count - 1];
                    string text = FrontWords.Line(f, l, row.DirectiveBy);
                    c.Alert = text.Length > 0 ? text : "NO NEWS";
                    var code = (FrontLogCode)l.Code;
                    c.AlertTone = code == FrontLogCode.Rebuild ? AvState.Danger : code == FrontLogCode.Counter && l.A != 0 ? AvState.Caution : code == FrontLogCode.Done || code == FrontLogCode.Effect ? AvState.Ready : AvState.Info;
                    if (code == FrontLogCode.Rebuild) alerts++;
                }
                else { c.Alert = "NO NEWS FROM THIS FRONT YET"; c.AlertTone = AvState.Inert; }
                // What waits behind the shown programme.
                string behind = "";
                for (int q = 0; q < row.Queue.Count; q++)
                {
                    if (q == pick) continue;
                    behind += (behind.Length > 0 ? " · " : "") + (row.Queue[q].Id == ProgrammeId.Readiness ? "READINESS" : FrontRules.Info(row.Queue[q].Id).Name);
                }
                c.Queue = behind;
                v.Cards[i] = c;
            }
            v.FrontsAhead = ahead;
            v.Alerts = alerts;
            foreach (PerkRowView p in v.Perks) if (p.Enabled) v.Cards[(int)p.Front].ReadyPerks++;
        }

        /// <summary>The state column word of a perk row.</summary>
        public static string StateWord(in CallTile t)
        {
            switch (t.State)
            {
                case CallState.Ready: return "READY";
                case CallState.Armed: return "ARMED";
                case CallState.Pending: return "PENDING";
                case CallState.Offline: return "NO UPLINK";
                case CallState.LowCredit: return "LOW ALLOC · " + t.StateWord;
                case CallState.Cooldown: return C2Cap.StateWord(t).Replace("COOL ", "COOLDOWN ");
                default: return t.StateWord; // LOCKED carries its reason (NEEDS READINESS 3), LOW ALLOC its price
            }
        }

        public static void Perks(OpsPageView v, IReadOnlyList<CallTile> tiles)
        {
            v.Perks.Clear();
            int ready = 0;
            foreach (CallTile t in tiles)
            {
                CallRow row; CallSheet.TryGet(t.Id, out row);
                if (t.State == CallState.Ready || t.State == CallState.Armed || t.State == CallState.Pending) ready++;
                v.Perks.Add(new PerkRowView
                {
                    Id = t.Id, Front = row.Front, Rung = row.Rung, Name = t.Label, RungWord = t.RungWord, Price = t.CostText, State = StateWord(t), Tone = Ink(t.State),
                    Enabled = t.Enabled, Armed = t.State == CallState.Armed, Button = t.State == CallState.Armed ? "FIRE" : t.State == CallState.Pending ? "WAIT" : "ARM"
                });
            }
            v.PerksReady = ready;
            v.PerksTotal = tiles.Count;
        }

        public static AvState Ink(CallState s) =>
            s == CallState.Ready ? AvState.Ready : s == CallState.Armed ? AvState.Caution : s == CallState.Pending ? AvState.Info
            : s == CallState.Offline || s == CallState.LowCredit ? AvState.Danger : AvState.Inert;

        /// <summary>The ORDER row: what is armed, where it would land, or the standing-by line.</summary>
        public static void Order(OpsPageView v, IReadOnlyList<CallTile> tiles, AimSource aim, string aimGrid, bool pending, string words, int allocation)
        {
            CallTile armed = default;
            v.Armed = false;
            foreach (CallTile t in tiles) if (t.State == CallState.Armed) { armed = t; v.Armed = true; break; }
            v.Pending = pending;
            string where = aim == AimSource.Pod ? "LOCKED TARGET" : aim == AimSource.Map ? "MAP CLICK" : aim == AimSource.Team ? "SOF TEAM LASER" : "NO AIM YET";
            if (v.Armed)
            {
                v.ArmedName = "ARMED · " + armed.Label;
                v.AimLine = "AIM " + where + (aimGrid.Length > 0 ? " · " + aimGrid : "") + " · FIRE, or right-click the map";
                v.OrderRight = armed.CostText;
            }
            else if (pending) { v.ArmedName = "ORDER SENT"; v.AimLine = "Waiting for the host to answer."; v.OrderRight = "WAIT"; }
            else { v.ArmedName = "STANDING BY"; v.AimLine = "ARM a perk below, then aim: lock a target or right-click the map."; v.OrderRight = allocation + " ALLOC"; }
            v.Words = C2Cap.FooterWords(words ?? "");
            v.WordsTone = v.Armed ? AvState.Caution : pending ? AvState.Info : C2Cap.StartsNegative(words ?? "") ? AvState.Danger : AvState.Ready;
        }
    }
}
