using System.Globalization;
using BoscaliSummer.Features.Support.Domain;
using NOAvionics.Ui;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Features.Support.Presentation
{
    /// <summary>
    /// SPEC OPS and INTEL — the same page twice over a different reserve, except SPEC OPS
    /// opens on the base of operations: the detachment's standing improvements, bought with
    /// the SOF tokens the task groups earn. A doctrine rank raises what the ground forces do
    /// — how much of a zone one fortification order secures, how many encampments one
    /// fast-rope insertion leaves behind — and the row says so in plain words.
    /// </summary>
    internal sealed partial class SupportPanel
    {
        private const float ReserveHeight = 72f;
        private const float StampWidth = 128f;

        private sealed class ProgramPage
        {
            public OpsReserve Reserve;
            public TMP_Text ReserveNote;
            public TMP_Text ProgramsNote;
            public TMP_Text Count;
            public TMP_Text Eta;
            public TMP_Text Yield;
            public Image Fill;
            public OpsRow[] Rows;
            public OpsProgramId[] Ids;
            public OpsRow[] DoctrineRows;
            public GarrisonUpgradeId[] DoctrineIds;
        }

        private readonly ProgramPage[] programPages = new ProgramPage[OpsProgramLedger.ReserveCount];

        private void ResetProgramPages()
        {
            for (int i = 0; i < programPages.Length; i++) programPages[i] = null;
        }

        private void BuildProgramPage(int tab, OpsReserve reserve)
        {
            bool specOps = reserve == OpsReserve.SpecOps;
            int programCount = 0;
            for (int i = 0; i < OpsProgramLedger.Programs.Length; i++)
                if (OpsProgramLedger.Programs[i].Reserve == reserve) programCount++;
            int doctrineCount = specOps ? OpsGarrison.UpgradeCount : 0;
            int actions = CountActions(tab);

            float height = HeaderHeight + ReserveHeight + SectionGap +
                           (doctrineCount > 0 ? HeaderHeight + doctrineCount * RowHeight + SectionGap : 0f) +
                           HeaderHeight + programCount * RowHeight + SectionGap +
                           (actions > 0 ? HeaderHeight + actions * RowHeight : 0f) + SectionGap;

            RectTransform parent = BeginPage(tab, specOps ? "SpecOpsPage" : "IntelPage",
                                             height, out float x, out float y, out float width);
            var page = new ProgramPage
            {
                Reserve = reserve,
                Rows = new OpsRow[programCount],
                Ids = new OpsProgramId[programCount],
                DoctrineRows = doctrineCount > 0 ? new OpsRow[doctrineCount] : null,
                DoctrineIds = doctrineCount > 0 ? new GarrisonUpgradeId[doctrineCount] : null
            };
            programPages[(int)reserve] = page;

            // 01 — the detachment and its readiness
            page.ReserveNote = Header(parent, x, ref y, width,
                                      specOps ? "01 / BASE OF OPERATIONS" : "01 / " + OpsProgramLedger.ReserveName(reserve),
                                      specOps ? "SOF DETACHMENT · READINESS" : "HELD FOR THEATER EVENTS");
            AvStyled.Label(parent, new Rect(x, y, width * 0.4f, 11f), "RESERVE", "metric-key");
            page.Count = AvStyled.Label(parent, new Rect(x, y - 13f, width * 0.4f, 28f), "—", "metric-value");
            float column = x + width * 0.45f;
            float columnWidth = width * 0.55f;
            AvStyled.Label(parent, new Rect(column, y - 2f, columnWidth * 0.5f, 14f), "NEXT TOKEN", "kv-key");
            page.Eta = AvStyled.Label(parent, new Rect(column + columnWidth * 0.5f, y - 2f, columnWidth * 0.5f, 14f),
                                      "—", "kv-value", align: TextAlignmentOptions.MidlineRight);
            AvStyled.Label(parent, new Rect(column, y - 20f, columnWidth * 0.5f, 14f), "YIELD", "kv-key");
            page.Yield = AvStyled.Label(parent, new Rect(column + columnWidth * 0.5f, y - 20f, columnWidth * 0.5f, 14f),
                                        "—", "kv-value", align: TextAlignmentOptions.MidlineRight);
            page.Fill = AvKit.ProgressBar(parent, new Rect(x, y - 48f, width, 6f), 0f, AvTheme.RailInfo);
            AvStyled.Label(parent, new Rect(x, y - 58f, specOps ? width - StampWidth - 8f : width, 13f),
                           specOps
                               ? "The base of operations is bought with these tokens."
                               : "No theater event draws on this reserve yet.",
                           "row-sub").color = AvTheme.Disabled;
            if (specOps)
            {
                var stamp = new Rect(x + width - StampWidth, y - 60f, StampWidth, 16f);
                AvStyled.Box(parent, stamp, "stamp");
                AvStyled.Label(parent, stamp, "AIRBORNE // SOF", "stamp",
                               align: TextAlignmentOptions.Center);
            }
            y -= ReserveHeight + SectionGap;

            // 02 — base-of-operations doctrine (SPEC OPS only)
            if (doctrineCount > 0)
            {
                Header(parent, x, ref y, width, "02 / DOCTRINE", "SPENDS SOF TOKENS");
                for (int i = 0; i < OpsGarrison.UpgradeCount; i++)
                {
                    GarrisonUpgradeInfo info = OpsGarrison.Upgrades[i];
                    page.DoctrineIds[i] = info.Id;
                    page.DoctrineRows[i] = Row(parent, x, y, width, false, info.Code, info.Name, info.Summary,
                                               "IMPROVE", () => { support.RequestGarrisonUpgrade(info.Id); nextRefresh = 0f; });
                    page.DoctrineRows[i].Pips = RankPips(parent, x, y);
                    y -= RowHeight;
                }
                y -= SectionGap;
            }

            // 03 — task groups (SPEC OPS) or networks (INTEL)
            page.ProgramsNote = Header(parent, x, ref y, width,
                                       specOps ? "03 / TASK GROUPS" : "02 / NETWORKS",
                                       specOps ? "ASSIGNED ELEMENTS" : "");
            int row = 0;
            for (int i = 0; i < OpsProgramLedger.Programs.Length; i++)
            {
                OpsProgramInfo info = OpsProgramLedger.Programs[i];
                if (info.Reserve != reserve) continue;
                OpsProgramId id = info.Id;
                page.Ids[row] = id;
                page.Rows[row] = Row(parent, x, y, width, false, info.Code, info.Name, info.Summary,
                                     "FUND", () => { support.RequestInvest(id); nextRefresh = 0f; });
                row++;
                y -= RowHeight;
            }
            y -= SectionGap;

            // 04 — direct action (SPEC OPS only)
            if (actions > 0)
            {
                Header(parent, x, ref y, width, specOps ? "04 / DIRECT ACTION" : "03 / DIRECT ACTION",
                       "GROUND FORCES");
                BuildActionRows(parent, tab, x, y, width, "TASK");
            }
        }

        private void RefreshProgramPage(int tab, bool bypass)
        {
            ProgramPage page = programPages[tab == TabSpecOps ? (int)OpsReserve.SpecOps : (int)OpsReserve.Intel];
            if (page == null) return;

            OpsProgramLedger ledger = support.LocalPrograms;
            OpsGarrison garrison = support.LocalGarrison;
            OpsReserve reserve = page.Reserve;
            string tag = OpsProgramLedger.ReserveTag(reserve);

            if (ledger == null)
            {
                page.Count.text = "—";
                page.Eta.text = "—";
                page.Yield.text = "—";
                page.Fill.fillAmount = 0f;
                page.ProgramsNote.text = "AWAITING THEATER DATA";
            }
            else
            {
                int tokens = ledger.Tokens(reserve);
                float eta = ledger.SecondsToNextToken(reserve);
                float yield = ledger.YieldPerMinute(reserve);
                page.Count.text = tokens + "/" + OpsProgramLedger.ReserveCap;
                page.Eta.text = ledger.Full(reserve) ? "RESERVE FULL" : eta < 0f ? "UNFUNDED" : "T-" + Clock(eta);
                page.Eta.color = ledger.Full(reserve) ? AvTheme.RailReady : eta < 0f ? AvTheme.Dim : AvTheme.TextPrimary;
                page.Yield.text = yield.ToString("0.00", CultureInfo.InvariantCulture) + " " + tag + "/MIN";
                page.Fill.fillAmount = ledger.Full(reserve) ? 1f : ledger.Progress(reserve);
                page.Fill.color = ledger.Full(reserve) ? AvTheme.RailReady : AvTheme.RailInfo;
                page.ProgramsNote.text = "FUNDED " + ledger.FundedTiers(reserve) + "/" +
                                         OpsProgramLedger.MaxTier * page.Rows.Length;
            }

            if (page.DoctrineRows != null) RefreshDoctrine(page, ledger, garrison, bypass);

            float allocation = support.LocalAllocation;
            for (int i = 0; i < page.Rows.Length; i++)
            {
                OpsRow row = page.Rows[i];
                OpsProgramInfo info = OpsProgramLedger.Info(page.Ids[i]);
                int tier = ledger != null ? ledger.Tier(info.Id) : 0;
                float cost = support.ProgramCost(info.Id);
                string grade = "TIER " + tier + "/" + OpsProgramLedger.MaxTier;
                string rate = "+" + info.YieldPerMinute[tier].ToString("0.00", CultureInfo.InvariantCulture) + "/MIN";
                row.Value.text = cost > 0f ? Figure(cost) : "—";
                row.Detail.text = tier < OpsProgramLedger.MaxTier ? "NEXT: " + info.Tiers[tier] : info.Summary;

                Tone tone;
                string status;
                bool enabled = false;
                if (ledger == null)
                {
                    tone = Tone.Locked;
                    status = "AWAITING THEATER DATA";
                }
                else if (tier >= OpsProgramLedger.MaxTier)
                {
                    tone = Tone.Ready;
                    status = grade + " · " + rate + " · FULLY FUNDED";
                }
                else if (support.CommandPending)
                {
                    tone = Tone.Pending;
                    status = grade + " · COMMAND PENDING";
                }
                else if (!bypass && allocation + 0.001f < cost)
                {
                    tone = Tone.Danger;
                    status = grade + " · INSUFFICIENT ALLOCATION";
                }
                else
                {
                    tone = Tone.Ready;
                    status = tier == 0 ? "UNFUNDED · FUND TO START ACCRUAL" : grade + " · " + rate;
                    enabled = true;
                }

                row.Primary.SetEnabled(enabled);
                row.Primary.SetText(tier >= OpsProgramLedger.MaxTier ? "MAX" : "FUND");
                if (Paint(row, tone, status))
                {
                    string next = tier < OpsProgramLedger.MaxTier
                        ? " Tier " + (tier + 1) + " for " + Figure(cost) + " alloc: " + info.Tiers[tier] +
                          " Yields +" + info.YieldPerMinute[tier + 1].ToString("0.00", CultureInfo.InvariantCulture) +
                          " " + tag + "/min."
                        : "";
                    row.Primary.WithTooltip(info.Name + " — " + info.Summary + next + " " + status + ".");
                }
            }
        }

        /// <summary>Three-segment rank strip in the row's left gutter; colour repeats the rank word.</summary>
        private static Image[] RankPips(RectTransform parent, float x, float y)
        {
            var pips = new Image[OpsGarrison.MaxRank];
            for (int i = 0; i < pips.Length; i++)
                pips[i] = AvKit.Rule(parent, new Rect(x + 10f + i * 12f, y - 20f, 10f, 3f), AvTheme.RailInert);
            return pips;
        }

        /// <summary>
        /// The two standing improvements. Rank and effect are written as words on the row;
        /// the cost is taken from the same table the host charges.
        /// </summary>
        private void RefreshDoctrine(ProgramPage page, OpsProgramLedger ledger, OpsGarrison garrison, bool bypass)
        {
            int tokens = ledger != null ? ledger.Tokens(OpsReserve.SpecOps) : 0;
            page.ReserveNote.text = "SOF DETACHMENT · " + DetachmentGrade(garrison);

            for (int i = 0; i < page.DoctrineRows.Length; i++)
            {
                OpsRow row = page.DoctrineRows[i];
                GarrisonUpgradeId id = page.DoctrineIds[i];
                GarrisonUpgradeInfo info = OpsGarrison.Info(id);
                int rank = garrison != null ? garrison.Rank(id) : 0;
                int cost = garrison != null ? garrison.NextCost(id) : 0;
                bool max = garrison != null && !garrison.CanUpgrade(id);

                row.Value.text = max ? "—" : cost + " SOF";
                row.Detail.text = info.Summary;
                row.Primary.SetText(max ? "MAX" : "IMPROVE");
                row.Primary.SetEnabled(false);
                if (row.Pips != null)
                    for (int pip = 0; pip < row.Pips.Length; pip++)
                        row.Pips[pip].color = pip < rank ? AvTheme.RailReady : AvTheme.RailInert;

                Tone tone;
                string status;
                bool enabled = false;
                if (garrison == null)
                {
                    tone = Tone.Locked;
                    status = "AWAITING THEATER DATA";
                }
                else if (max)
                {
                    tone = Tone.Ready;
                    status = OpsGarrison.RankLabel(rank) + " · MAX · " + OpsGarrison.EffectLabel(id, rank);
                }
                else if (support.CommandPending)
                {
                    tone = Tone.Pending;
                    status = OpsGarrison.RankLabel(rank) + " · AWAITING HOST";
                }
                else if (!bypass && tokens < cost)
                {
                    tone = Tone.Danger;
                    status = OpsGarrison.RankLabel(rank) + " · NEED " + cost + " SOF";
                }
                else
                {
                    tone = Tone.Ready;
                    status = OpsGarrison.RankLabel(rank) + " · NEXT: " + OpsGarrison.EffectLabel(id, rank + 1);
                    enabled = true;
                }

                row.Primary.SetEnabled(enabled);
                if (Paint(row, tone, status))
                {
                    string next = !max && garrison != null
                        ? " Next rank: " + info.Ranks[rank] + " Costs " + cost + " SOF tokens."
                        : "";
                    row.Primary.WithTooltip(info.Name + " — " + info.Summary + next + " " + status + ".");
                }
            }
        }

        /// <summary>The detachment's overall standing, read from the ranks already bought.</summary>
        private static string DetachmentGrade(OpsGarrison garrison)
        {
            if (garrison == null) return "AWAITING THEATER DATA";
            int ranks = 0;
            for (int i = 0; i < OpsGarrison.UpgradeCount; i++) ranks += garrison.Rank((GarrisonUpgradeId)i);
            if (ranks >= 2 * OpsGarrison.MaxRank) return "TIER ONE";
            if (ranks >= 4) return "VETERAN";
            if (ranks >= 2) return "QUALIFIED";
            return "ESTABLISHED";
        }
    }
}
