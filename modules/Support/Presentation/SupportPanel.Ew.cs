using BoscaliSummer.Features.Support.Domain;
using BoscaliSummer.Features.Support.Runtime;
using NOAvionics;
using NOAvionics.Ui;
using TMPro;
using UnityEngine;

namespace BoscaliSummer.Features.Support.Presentation
{
    /// <summary>
    /// EW — the faction's mobile EW station and its posture, grouped by doctrine: ES
    /// listens, EA attacks, EP protects. The posture decides which INFO attack operations
    /// the station backs; the host owns the value and the page shows only what it sent.
    /// </summary>
    internal sealed partial class SupportPanel
    {
        private OpsRow stationRow;
        private readonly OpsRow[] postureRows = new OpsRow[3];
        private TMP_Text stationNote;
        private TMP_Text postureNote;

        private void ResetEwPage()
        {
            stationRow = null;
            for (int i = 0; i < postureRows.Length; i++) postureRows[i] = null;
            stationNote = postureNote = null;
        }

        private void BuildEwPage()
        {
            int protection = CountActions(TabEw);
            float height = HeaderHeight + RowHeight + SectionGap +
                           HeaderHeight + postureRows.Length * RowHeight + SectionGap +
                           (protection > 0 ? HeaderHeight + protection * RowHeight : 0f) + SectionGap;

            RectTransform parent = BeginPage(TabEw, "EwPage", height, out float x, out float y, out float width);

            stationNote = Header(parent, x, ref y, width, "01 / MOBILE EW STATION", "");
            stationRow = Row(parent, x, y, width, false, "EWS", "MOBILE EW STATION",
                             "Drives from the nearest owned airbase to the mark. One per faction.",
                             "DEPLOY", OnStationCommand);
            y -= RowHeight + SectionGap;

            postureNote = Header(parent, x, ref y, width, "02 / POSTURE", "ES · EA");
            for (int i = 0; i < postureRows.Length; i++)
            {
                EwPostureInfo info = EwPostures.All[i];
                EwPosture posture = info.Posture;
                postureRows[i] = Row(parent, x, y, width, false, info.Discipline, info.Name, info.Summary,
                                     "SET", () => OnRetune(posture), primaryStyle: AvButtonStyle.Default);
                y -= RowHeight;
            }
            y -= SectionGap;

            if (protection > 0)
            {
                Header(parent, x, ref y, width, "03 / ELECTRONIC PROTECTION", "EP");
                BuildActionRows(parent, TabEw, x, y, width, "CALL IN");
            }
        }

        private void OnStationCommand()
        {
            if (StationArmed())
            {
                support.Disarm();
            }
            else if (support.LocalEwAssetState == EwAssetState.None)
            {
                support.ArmCommand(OpsCommand.EwDeploy, 0, 0, "DEPLOY EW STATION");
            }
            else
            {
                support.ArmCommand(OpsCommand.EwReposition, 0, 0, "EW STATION REPOSITION");
            }
            nextRefresh = 0f;
        }

        private void OnRetune(EwPosture posture)
        {
            if (support.CommandPending || support.LocalEwAssetState == EwAssetState.None) return;
            if (support.LocalEwPosture == posture) return;
            support.RequestEwRetune(posture);
            nextRefresh = 0f;
        }

        private bool StationArmed() =>
            support.CommandArmed &&
            (support.ArmedCommand == OpsCommand.EwDeploy || support.ArmedCommand == OpsCommand.EwReposition);

        private void RefreshEw(bool bypass)
        {
            if (stationRow == null) return;

            bool enabled = support.Settings == null || support.Settings.EwEnabled.Value;
            bool deployed = support.LocalEwAssetState != EwAssetState.None;
            bool armed = StationArmed();
            float cost = support.EwTruckCost();
            EwPosture active = support.LocalEwPosture;

            stationNote.text = deployed ? "1/1 DEPLOYED" : "0/1 DEPLOYED";
            postureNote.text = deployed ? "ACTIVE: " + EwPostures.Info(active).Name : "ES · EA";
            stationRow.Value.text = deployed ? "1/1" : cost > 0f ? Figure(cost) : "—";

            Tone tone;
            string status;
            bool actionable = false;
            if (!enabled)
            {
                tone = Tone.Locked;
                status = "EW DISABLED IN HOST CONFIG";
            }
            else if (armed)
            {
                tone = Tone.Armed;
                status = "ARMED · RIGHT-CLICK MAP FOR DESTINATION";
                actionable = true;
            }
            else if (support.CommandPending)
            {
                tone = Tone.Pending;
                status = "COMMAND PENDING · AWAITING HOST";
            }
            else if (deployed)
            {
                tone = Tone.Ready;
                float x = support.OpsState.EwX, z = support.OpsState.EwZ;
                status = "DEPLOYED · GRID " + TheaterGrid.Kilometres(x, z);
                actionable = true;
            }
            else if (cost <= 0f)
            {
                tone = Tone.Locked;
                status = "NO EW VEHICLE ON THIS MAP";
            }
            else if (!bypass && support.LocalAllocation + 0.001f < cost)
            {
                tone = Tone.Danger;
                status = "NOT DEPLOYED · INSUFFICIENT ALLOCATION";
            }
            else
            {
                tone = Tone.Ready;
                status = "NOT DEPLOYED · READY TO DEPLOY";
                actionable = true;
            }

            stationRow.Primary.SetEnabled(actionable);
            stationRow.Primary.SetLatched(armed);
            stationRow.Primary.SetText(armed ? "ABORT" : deployed ? "MOVE" : "DEPLOY");
            if (Paint(stationRow, tone, status))
            {
                stationRow.Primary.WithTooltip(deployed
                    ? "Order the EW station to a new position. Attack operations need it near their target. " + status + "."
                    : "Deploy the EW station for " + Figure(cost) + " alloc. It drives from the nearest owned airbase. " + status + ".");
            }

            for (int i = 0; i < postureRows.Length; i++)
            {
                OpsRow row = postureRows[i];
                EwPostureInfo info = EwPostures.All[i];
                bool current = deployed && active == info.Posture;
                row.Value.text = info.Emissions;

                string postureStatus;
                Tone postureTone;
                if (!deployed)
                {
                    postureTone = Tone.Locked;
                    postureStatus = "NO STATION DEPLOYED";
                }
                else if (current)
                {
                    postureTone = Tone.Ready;
                    postureStatus = "ACTIVE · EMISSIONS " + info.Emissions;
                }
                else if (support.CommandPending)
                {
                    postureTone = Tone.Pending;
                    postureStatus = "COMMAND PENDING · AWAITING HOST";
                }
                else
                {
                    postureTone = Tone.Locked;
                    postureStatus = "STANDBY · SET TO RETUNE";
                }

                row.Primary.SetEnabled(deployed && !support.CommandPending);
                row.Primary.SetLatched(current);
                row.Primary.SetText(current ? "ACTIVE" : "SET");
                if (Paint(row, postureTone, postureStatus))
                {
                    row.Primary.WithTooltip(info.Discipline + " · " + info.Name + " — " + info.Summary +
                        " Emissions " + info.Emissions + ". " + postureStatus + ".");
                }
            }
        }
    }
}
