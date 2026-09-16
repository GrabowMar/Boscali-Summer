using System.Globalization;
using BoscaliSummer.Features.Support.Runtime;
using NOAvionics.Ui;
using TMPro;
using UnityEngine;

namespace BoscaliSummer.Features.Support.Presentation
{
    /// <summary>
    /// INFO — cyber operations against hostile C2 and tracks, and the infrastructure that
    /// unlocks and scales them. Operation rows are the shared support-action rows; the
    /// infrastructure rows send host-validated upgrade commands.
    /// </summary>
    internal sealed partial class SupportPanel
    {
        private readonly OpsRow[] facilityRows = new OpsRow[InfoNetwork.Facilities.Length];
        private TMP_Text operationsNote;
        private TMP_Text infrastructureNote;

        private void ResetInfoPage()
        {
            for (int i = 0; i < facilityRows.Length; i++) facilityRows[i] = null;
            operationsNote = infrastructureNote = null;
        }

        private void BuildInfoPage()
        {
            int operations = CountActions(TabInfo);
            float height = (operations > 0 ? HeaderHeight + operations * RowHeight + SectionGap : 0f) +
                           HeaderHeight + facilityRows.Length * RowHeight + SectionGap;

            RectTransform parent = BeginPage(TabInfo, "InfoPage", height, out float x, out float y, out float width);

            if (operations > 0)
            {
                operationsNote = Header(parent, x, ref y, width, "01 / CYBER OPERATIONS", "");
                y = BuildActionRows(parent, TabInfo, x, y, width, "EXECUTE") - SectionGap;
            }

            infrastructureNote = Header(parent, x, ref y, width,
                                        operations > 0 ? "02 / INFRASTRUCTURE" : "01 / INFRASTRUCTURE", "");
            for (int i = 0; i < facilityRows.Length; i++)
            {
                FacilityInfo facility = InfoNetwork.Facilities[i];
                FacilityId id = facility.Id;
                facilityRows[i] = Row(parent, x, y, width, false, facility.Code, facility.Name, facility.Summary,
                                      "BUILD", () => { support.RequestUpgrade(id); nextRefresh = 0f; });
                y -= RowHeight;
            }
        }

        private void RefreshInfo(bool bypass)
        {
            if (infrastructureNote == null) return;

            InfoNetwork info = support.LocalInfo;
            InfoPowers powers = info != null ? info.Powers : default;
            if (operationsNote != null)
            {
                int discount = Mathf.RoundToInt((1f - (info != null ? powers.CostScale : 1f)) * 100f);
                operationsNote.text = discount > 0 ? "CRYPTO -" + discount + "% COST" : "RIGHT-CLICK MAP TO TARGET";
            }
            infrastructureNote.text = "NETWORK TIER " + powers.Tier.ToString(CultureInfo.InvariantCulture) +
                                      "/" + InfoNetwork.MaxLevel * InfoNetwork.Facilities.Length;

            float allocation = support.LocalAllocation;
            for (int i = 0; i < facilityRows.Length; i++)
            {
                OpsRow row = facilityRows[i];
                if (row == null) continue;
                FacilityInfo facility = InfoNetwork.Facilities[i];
                int level = info != null ? info.Level(facility.Id) : 0;
                float cost = support.FacilityCost(facility.Id);
                string requirement = info != null ? info.Requirement(facility.Id) : "AWAITING THEATER DATA";
                string grade = "LV" + level + "/" + InfoNetwork.MaxLevel;
                row.Value.text = cost > 0f ? Figure(cost) : "—";
                row.Detail.text = level < InfoNetwork.MaxLevel
                    ? "NEXT: " + facility.Levels[level]
                    : facility.Summary;

                Tone tone;
                string status;
                bool enabled = false;
                if (info != null && level >= InfoNetwork.MaxLevel)
                {
                    tone = Tone.Ready;
                    status = grade + " · MAXIMUM LEVEL";
                }
                else if (requirement != null)
                {
                    tone = Tone.Locked;
                    status = grade + " · " + requirement;
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
                    status = grade + (level == 0 ? " · READY TO BUILD" : " · UPGRADE AVAILABLE");
                    enabled = true;
                }

                row.Primary.SetEnabled(enabled);
                row.Primary.SetText(level >= InfoNetwork.MaxLevel ? "MAX" : level == 0 ? "BUILD" : "UPGRADE");
                if (Paint(row, tone, status))
                {
                    row.Primary.WithTooltip(facility.Name + " — " + facility.Summary + " " +
                        (cost > 0f ? Figure(cost) + " alloc. " : "") + status + ".");
                }
            }
        }
    }
}
