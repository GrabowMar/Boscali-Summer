using System.Globalization;
using BoscaliSummer.Features.Support.Domain;
using BoscaliSummer.Features.Support.Domain.Cyber;
using BoscaliSummer.Features.Support.Runtime;
using NOAvionics;
using NOAvionics.Ui;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Features.Support.Presentation
{
    /// <summary>
    /// CYBER › ARCHITECT — extend the network. Cyber Command and the gateways come up by
    /// themselves on owned airbases; the catalogue adds field sites with a right-click on the map
    /// (a real vehicle leaves the nearest owned vehicle depot); the inspector moves, retunes and
    /// scraps the selected field site and explains an airbase node; the roster lists all sixteen
    /// slots; DOCTRINE carries the four facility tracks that unlock and scale the operations.
    /// </summary>
    internal sealed partial class SupportPanel
    {
        private const float SiteInspectorHeight = 180f;
        private const int RosterPageSize = 4;
        private const float ScrapConfirmSeconds = 3f;

        private readonly OpsRow[] catalogueRows = new OpsRow[CyberSites.Field.Length];
        private readonly OpsRow[] rosterRows = new OpsRow[RosterPageSize];
        private int rosterPage;
        private AvButton rosterPrevious, rosterNext;
        private readonly OpsRow[] facilityRows = new OpsRow[InfoNetwork.Facilities.Length];
        private readonly AvButton[] modeButtons = new AvButton[3];
        private TMP_Text catalogueNote, rosterNote, doctrineNote;
        private Image inspectorRail;
        private TMP_Text inspectorName, inspectorState, inspectorDetail, inspectorMore;
        private AvButton inspectorMove, inspectorScrap;
        private float scrapArmedUntil;
        private int scrapArmedSlot = -1;

        private void ResetArchitectPage()
        {
            for (int i = 0; i < catalogueRows.Length; i++) catalogueRows[i] = null;
            for (int i = 0; i < rosterRows.Length; i++) rosterRows[i] = null;
            for (int i = 0; i < facilityRows.Length; i++) facilityRows[i] = null;
            for (int i = 0; i < modeButtons.Length; i++) modeButtons[i] = null;
            catalogueNote = rosterNote = doctrineNote = null;
            inspectorRail = null;
            inspectorName = inspectorState = inspectorDetail = inspectorMore = null;
            inspectorMove = inspectorScrap = null;
            scrapArmedUntil = 0f;
            scrapArmedSlot = -1;
            rosterPage = 0;
            rosterPrevious = rosterNext = null;
        }

        private void BuildArchitectPage(RectTransform root, Rect body)
        {
            float height = HeaderHeight + catalogueRows.Length * RowHeight + SectionGap +
                           HeaderHeight + SiteInspectorHeight + SectionGap +
                           HeaderHeight + rosterRows.Length * CompactRowHeight + 36f + SectionGap +
                           HeaderHeight + facilityRows.Length * RowHeight + SectionGap;
            RectTransform parent = BeginSub(root, body, height, out float x, out float y, out float width);

            catalogueNote = Header(parent, x, ref y, width, "FIELD SITES", "");
            for (int i = 0; i < catalogueRows.Length; i++)
            {
                CyberSiteInfo info = CyberSites.Info(CyberSites.Field[i]);
                CyberSiteKind kind = info.Kind;
                catalogueRows[i] = Row(parent, x, y, width, false, info.Code, info.Name, info.Summary, "DEPLOY",
                    () => OnDeploy(kind));
                y -= RowHeight;
            }
            y -= SectionGap;

            Header(parent, x, ref y, width, "SELECTED SITE", "SELECT A ROSTER ENTRY OR A NODE ON THE NETWORK MAP");
            BuildSiteInspector(parent, x, y, width);
            y -= SiteInspectorHeight + SectionGap;

            rosterNote = Header(parent, x, ref y, width, "SITE ROSTER", "");
            for (int i = 0; i < rosterRows.Length; i++)
            {
                int slot = i;
                rosterRows[i] = Row(parent, x, y, width, true, "--", "OPEN SLOT", null, "SELECT",
                    () => SelectSite(rosterPage * RosterPageSize + slot),
                    onSelect: () => SelectSite(rosterPage * RosterPageSize + slot),
                    selectTooltip: "Select this slot for the inspector above.");
                y -= CompactRowHeight;
            }
            rosterPrevious = AvStyled.Button(parent, new Rect(x, y - 4f, 104f, 28f), "PREVIOUS", "btn",
                () => { rosterPage = Mathf.Max(0, rosterPage - 1); nextRefresh = 0f; }, AvButtonStyle.Quiet);
            rosterNext = AvStyled.Button(parent, new Rect(x + width - 104f, y - 4f, 104f, 28f), "NEXT", "btn",
                () => { rosterPage = Mathf.Min((CyberNetwork.SlotCount - 1) / RosterPageSize, rosterPage + 1); nextRefresh = 0f; }, AvButtonStyle.Quiet);
            y -= 36f;
            y -= SectionGap;

            doctrineNote = Header(parent, x, ref y, width, "FACILITY DOCTRINE", "");
            for (int i = 0; i < facilityRows.Length; i++)
            {
                FacilityInfo facility = InfoNetwork.Facilities[i];
                FacilityId id = facility.Id;
                facilityRows[i] = Row(parent, x, y, width, false, facility.Code, facility.Name, facility.Summary,
                                      "BUILD", () => { support.RequestUpgrade(id); nextRefresh = 0f; });
                y -= RowHeight;
            }
        }

        private void BuildSiteInspector(RectTransform parent, float x, float y, float width)
        {
            var area = new Rect(x, y, width, SiteInspectorHeight);
            inspectorRail = AvKit.TacticalCard(parent, area, AvTheme.RailInert).Rail;
            AvStyled.Button(parent, new Rect(x + 10f, y - 8f, 24f, 20f), "<", "btn", () => CycleSite(-1),
                AvButtonStyle.Quiet).WithTooltip("Previous site.");
            AvStyled.Button(parent, new Rect(x + 38f, y - 8f, 24f, 20f), ">", "btn", () => CycleSite(1),
                AvButtonStyle.Quiet).WithTooltip("Next site.");
            inspectorName = SingleLine(AvKit.Label(parent, "", new Rect(x + 70f, y - 8f, width - 82f, 20f),
                AvTheme.TextPrimary, AvTokens.FontLead, FontStyles.Bold));
            inspectorState = SingleLine(AvKit.Label(parent, "", new Rect(x + 12f, y - 34f, width - 24f, 18f),
                AvTheme.Dim, AvTokens.FontSmall, FontStyles.Bold));
            inspectorDetail = Wrapped(AvStyled.Label(parent, new Rect(x + 12f, y - 60f, width - 24f, 30f), "", "row-sub"));
            inspectorMore = Wrapped(AvStyled.Label(parent, new Rect(x + 12f, y - 96f, width - 24f, 30f), "", "row-sub"));
            inspectorMore.color = AvTheme.Dim;

            float buttonY = y - 140f;
            float modeWidth = (width - 24f - 16f) / 5f;
            for (int i = 0; i < modeButtons.Length; i++)
            {
                var mode = (EwPosture)ModeOrder(i);
                modeButtons[i] = AvStyled.Button(parent, new Rect(x + 12f + i * (modeWidth + 4f), buttonY, modeWidth, 28f),
                    CyberWords.Mode(mode), "btn", () => OnMode(mode), AvButtonStyle.Default)
                    .WithTooltip(EwPostures.Info(mode).Summary);
            }
            inspectorMove = AvStyled.Button(parent,
                new Rect(x + 12f + 3f * (modeWidth + 4f), buttonY, modeWidth, 28f), "MOVE", "btn", OnMove,
                AvButtonStyle.Primary)
                .WithTooltip("Drive the site to a new mark. It drops off the net until it arrives.");
            inspectorScrap = AvStyled.Button(parent,
                new Rect(x + 12f + 4f * (modeWidth + 4f), buttonY, modeWidth, 28f), "SCRAP", "btn", OnScrap,
                AvButtonStyle.Danger);
        }

        /// <summary>Button order NOISE, DECEPTION, EMCON: the loud mode first.</summary>
        private static byte ModeOrder(int index) =>
            index == 0 ? (byte)EwPosture.NoiseJamming : index == 1 ? (byte)EwPosture.GhostSpoofing : (byte)EwPosture.SigintPassive;

        // ---- Actions -------------------------------------------------------------------------------

        private void OnDeploy(CyberSiteKind kind)
        {
            if (support.CommandArmed && support.ArmedCommand == OpsCommand.CyberBuild && support.ArmedCommandArg == (byte)kind)
            {
                support.Disarm();
            }
            else
            {
                string name = CyberSites.Info(kind).Name;
                support.ArmCommand(OpsCommand.CyberBuild, (byte)kind, 0, "DEPLOY " + name);
                CyberLog("ARCHITECT · " + name + " · MARK THE MAP");
            }
            nextRefresh = 0f;
        }

        private void SelectSite(int slot)
        {
            selectedSite = slot;
            rosterPage = Mathf.Clamp(slot / RosterPageSize, 0, (CyberNetwork.SlotCount - 1) / RosterPageSize);
            scrapArmedSlot = -1;
            nextRefresh = 0f;
        }

        private void CycleSite(int step)
        {
            CyberNetwork network = support.LocalCyber;
            if (network == null) return;
            int start = selectedSite < 0 ? (step > 0 ? -1 : 0) : selectedSite;
            for (int i = 1; i <= CyberNetwork.SlotCount; i++)
            {
                int slot = ((start + step * i) % CyberNetwork.SlotCount + CyberNetwork.SlotCount) % CyberNetwork.SlotCount;
                if (!network.Exists(slot)) continue;
                SelectSite(slot);
                return;
            }
        }

        private void OnMode(EwPosture mode)
        {
            CyberNetwork network = support.LocalCyber;
            if (network == null || !network.Online(selectedSite) || support.CommandPending) return;
            if (network.Site(selectedSite).Mode == mode) return;
            support.RequestCyberMode(selectedSite, mode);
            CyberLog(CyberWords.Callsign(network, selectedSite) + " RETUNING TO " + CyberWords.Mode(mode));
            nextRefresh = 0f;
        }

        private void OnMove()
        {
            CyberNetwork network = support.LocalCyber;
            if (network == null || !network.Online(selectedSite)) return;
            if (support.CommandArmed && support.ArmedCommand == OpsCommand.CyberMove)
            {
                support.Disarm();
                return;
            }
            support.ArmCommand(OpsCommand.CyberMove, (byte)selectedSite, 0,
                "RELOCATE " + CyberWords.Callsign(network, selectedSite));
            nextRefresh = 0f;
        }

        private void OnScrap()
        {
            CyberNetwork network = support.LocalCyber;
            if (network == null || !network.Exists(selectedSite) || support.CommandPending) return;
            if (scrapArmedSlot != selectedSite || Time.unscaledTime > scrapArmedUntil)
            {
                scrapArmedSlot = selectedSite;
                scrapArmedUntil = Time.unscaledTime + ScrapConfirmSeconds;
                nextRefresh = 0f;
                return;
            }
            scrapArmedSlot = -1;
            CyberLog("ARCHITECT · SCRAPPING " + CyberWords.Callsign(network, selectedSite));
            support.RequestCyberScrap(selectedSite);
            nextRefresh = 0f;
        }

        // ---- Refresh -------------------------------------------------------------------------------

        private void RefreshArchitectPage(bool bypass, CyberNetwork network, double now)
        {
            if (catalogueNote == null) return;
            if (network != null && !network.Exists(selectedSite)) selectedSite = -1;
            float allocation = support.LocalAllocation;
            CyberStats stats = network != null ? network.Stats() : default;
            int fielded = network != null ? network.FieldCount : 0;
            catalogueNote.text = fielded + "/" + support.CyberSiteLimit + " TRUCKS · ROLL FROM NEAREST DEPOT";

            for (int i = 0; i < catalogueRows.Length; i++)
            {
                OpsRow row = catalogueRows[i];
                CyberSiteInfo info = CyberSites.Info(CyberSites.Field[i]);
                float cost = support.CyberSiteCost(info.Kind);
                int count = network != null ? network.Count(info.Kind) : 0;
                bool armed = support.CommandArmed && support.ArmedCommand == OpsCommand.CyberBuild &&
                             support.ArmedCommandArg == (byte)info.Kind;
                SitePlacement placement = network != null
                    ? network.CheckPlacement(info.Kind, support.CyberSiteLimit)
                    : SitePlacement.NeedsCommand;
                row.Value.text = cost > 0f ? Figure(cost) : "—";
                string tally = count + "/" + info.CopyLimit + " · " + CyberSites.Emissions(info.Emission) + " · ";

                Tone tone;
                string status;
                bool enabled = false;
                if (!support.CyberEnabled)
                {
                    tone = Tone.Locked;
                    status = "DISABLED IN HOST CONFIG";
                }
                else if (armed)
                {
                    tone = Tone.Armed;
                    status = "ARMED · RIGHT-CLICK THE MAP";
                    enabled = true;
                }
                else if (network == null)
                {
                    tone = Tone.Locked;
                    status = "AWAITING THEATER DATA";
                }
                else if (placement != SitePlacement.None)
                {
                    tone = Tone.Locked;
                    status = tally + CyberWords.Placement(placement);
                }
                else if (support.CommandPending)
                {
                    tone = Tone.Pending;
                    status = tally + "COMMAND PENDING";
                }
                else if (!bypass && allocation + 0.001f < cost)
                {
                    tone = Tone.Danger;
                    status = tally + "INSUFFICIENT ALLOCATION";
                }
                else
                {
                    tone = Tone.Ready;
                    status = tally + "READY · DEPLOY, THEN RIGHT-CLICK";
                    enabled = true;
                }
                row.Primary.SetEnabled(enabled);
                row.Primary.SetLatched(armed);
                row.Primary.SetText(armed ? "ABORT" : "DEPLOY");
                if (Paint(row, tone, status))
                    row.Primary.WithTooltip(info.Name + " — " + info.Summary + " Link " +
                        Km(info.LinkRange) + ", cover " + Km(info.EffectRadius) + ". " + status + ".");
            }

            RefreshSiteInspector(network, now);
            RefreshRoster(network, now, stats);
            RefreshDoctrine(bypass, allocation);
        }

        private void RefreshSiteInspector(CyberNetwork network, double now)
        {
            bool has = network != null && network.Exists(selectedSite);
            for (int i = 0; i < modeButtons.Length; i++) modeButtons[i].gameObject.SetActive(false);
            inspectorMove.SetEnabled(false);
            inspectorScrap.SetEnabled(false);
            if (!has)
            {
                inspectorRail.color = AvTheme.RailInert;
                inspectorName.text = "NO SITE SELECTED";
                inspectorState.text = "";
                inspectorDetail.text = network != null && network.SiteCount > 0
                    ? "Pick a site in the roster, on the mesh, or with < >."
                    : "Cyber Command comes up by itself once your faction holds an airbase.";
                inspectorMore.text = "";
                inspectorMove.WithTooltip("Select a site first.");
                inspectorScrap.SetText("SCRAP");
                inspectorScrap.WithTooltip("Select a site first.");
                return;
            }

            CyberSite site = network.Site(selectedSite);
            CyberSiteInfo info = CyberSites.Info(site.Kind);
            string state = CyberWords.SiteState(network, selectedSite, now);
            Tone tone = SiteTone(network, selectedSite, now);
            inspectorRail.color = RailColor(tone);
            inspectorName.text = CyberWords.Callsign(network, selectedSite) + " · " + info.Name;
            inspectorState.text = state;
            inspectorState.color = StatusColor(tone);
            int hops = network.Hops(selectedSite);
            inspectorDetail.text = TheaterGrid.Kilometres(site.X, site.Z) + " · " +
                                   (hops >= 0 ? hops + " HOP" + (hops == 1 ? "" : "S") + " TO C2" : "NO PATH TO C2") +
                                   " · " + CyberSites.Emissions(network.EmissionOf(selectedSite));
            float scale = network.EffectScale(selectedSite, now);
            inspectorMore.text = info.EffectRadius > 0f
                ? "COVER " + Km(network.EffectRadius(selectedSite, now)) + " · STRENGTH " + Mathf.RoundToInt(scale * 100f) + "%" +
                  (site.Kind == CyberSiteKind.Jammer
                      ? " · UMBRELLA " + Mathf.RoundToInt(EwPostures.Umbrella(site.Mode) * scale * 100f) + "%"
                      : "")
                : "LINK " + Km(info.LinkRange) + " · BANDWIDTH " + (info.Bandwidth >= 0f ? "+" : "") +
                  info.Bandwidth.ToString("0", CultureInfo.InvariantCulture) + "/S";

            bool online = network.Online(selectedSite);
            bool idle = !support.CommandPending;
            if (site.Static)
            {
                inspectorMore.text = (site.Down ? "ANCHOR BUILDING DESTROYED · BACK WHEN REPAIRED · " : "BACKBONE · ") +
                                     "RADIO REACH " + Km(info.LinkRange) + " · +" +
                                     info.Bandwidth.ToString("0", CultureInfo.InvariantCulture) + " MB/S";
                inspectorMove.SetText("MOVE");
                inspectorMove.SetLatched(false);
                inspectorMove.WithTooltip("Airbase nodes stay with their base. Capture or hold bases to shape the backbone.");
                inspectorScrap.SetText("SCRAP");
                inspectorScrap.SetLatched(false);
                inspectorScrap.WithTooltip(site.Kind == CyberSiteKind.Command
                    ? "Cyber Command lives on your central airbase. It moves by itself if that base falls."
                    : "A gateway lives on its airbase and leaves only if the base is captured. Bomb an enemy tower to cut theirs.");
                return;
            }
            if (site.Kind == CyberSiteKind.Jammer)
            {
                for (int i = 0; i < modeButtons.Length; i++)
                {
                    modeButtons[i].gameObject.SetActive(true);
                    bool active = (byte)site.Mode == ModeOrder(i);
                    modeButtons[i].SetLatched(active);
                    modeButtons[i].SetEnabled(online && idle && !active);
                }
            }
            bool moving = support.CommandArmed && support.ArmedCommand == OpsCommand.CyberMove;
            inspectorMove.SetEnabled((online && idle) || moving);
            inspectorMove.SetLatched(moving);
            inspectorMove.SetText(moving ? "ABORT" : "MOVE");

            bool confirming = scrapArmedSlot == selectedSite && Time.unscaledTime <= scrapArmedUntil;
            inspectorScrap.SetEnabled(!site.Lost && idle);
            inspectorScrap.SetLatched(confirming);
            inspectorScrap.SetText(confirming ? "CONFIRM" : "SCRAP");
            inspectorScrap.WithTooltip("Scrap the site: its vehicle is withdrawn and " +
                  Mathf.RoundToInt(support.CyberScrapRefund * 100f) + "% of what was paid comes back. Click twice.");
        }

        private void RefreshRoster(CyberNetwork network, double now, in CyberStats stats)
        {
            rosterNote.text = stats.Sites + "/" + CyberNetwork.SlotCount + " SITES · " +
                              stats.OnNet + " ON NET · PAGE " + (rosterPage + 1) + "/" + ((CyberNetwork.SlotCount - 1) / RosterPageSize + 1);
            rosterPrevious.SetEnabled(rosterPage > 0);
            rosterNext.SetEnabled((rosterPage + 1) * RosterPageSize < CyberNetwork.SlotCount);
            for (int i = 0; i < rosterRows.Length; i++)
            {
                OpsRow row = rosterRows[i];
                int slot = rosterPage * RosterPageSize + i;
                bool exists = network != null && network.Exists(slot);
                bool selected = exists && slot == selectedSite;
                SetSelected(row, selected);
                row.Primary.SetEnabled(exists);
                if (!exists)
                {
                    row.Code.text = "--";
                    row.Name.text = "OPEN SLOT";
                    row.Value.text = "";
                    Paint(row, Tone.Locked, "SLOT " + (slot + 1) + " FREE");
                    continue;
                }
                CyberSite site = network.Site(slot);
                row.Code.text = CyberSites.Code(site.Kind);
                row.Name.text = CyberWords.Callsign(network, slot);
                row.Value.text = site.Kind == CyberSiteKind.Jammer ? CyberWords.Mode(site.Mode)
                    : site.Static ? "AIRBASE" : "";
                string state = CyberWords.SiteState(network, slot, now);
                Paint(row, SiteTone(network, slot, now), state + " · " + TheaterGrid.Kilometres(site.X, site.Z));
                // Paint repaints the row wash from its tone; a selected site keeps its
                // highlight by going last.
                if (selected) SetSelected(row, true);
            }
        }

        private void RefreshDoctrine(bool bypass, float allocation)
        {
            InfoNetwork info = support.LocalInfo;
            InfoPowers powers = info != null ? info.Powers : default;
            doctrineNote.text = "TIER " + powers.Tier.ToString(Invariant) + "/" +
                                InfoNetwork.MaxLevel * InfoNetwork.Facilities.Length;
            for (int i = 0; i < facilityRows.Length; i++)
            {
                OpsRow row = facilityRows[i];
                FacilityInfo facility = InfoNetwork.Facilities[i];
                int level = info != null ? info.Level(facility.Id) : 0;
                float cost = support.FacilityCost(facility.Id);
                string requirement = info != null ? info.Requirement(facility.Id) : "AWAITING THEATER DATA";
                string grade = "LV" + level + "/" + InfoNetwork.MaxLevel;
                row.Value.text = cost > 0f ? Figure(cost) : "—";
                row.Detail.text = level < InfoNetwork.MaxLevel ? "NEXT: " + facility.Levels[level] : facility.Summary;

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
                    row.Primary.WithTooltip(facility.Name + " — " + facility.Summary + " " +
                        (cost > 0f ? Figure(cost) + " alloc. " : "") + status + ".");
            }
        }

        private static Tone SiteTone(CyberNetwork network, int slot, double now)
        {
            CyberSite site = network.Site(slot);
            if (site.Lost || site.Compromised || site.Down) return Tone.Danger;
            if (site.Deploying) return Tone.Pending;
            if (site.Isolated) return Tone.Locked;
            if (!network.OnNet(slot) || network.Jammed(slot, now)) return Tone.Armed;
            return Tone.Ready;
        }

        private static string Km(float metres) =>
            (metres / 1000f).ToString("0.#", CultureInfo.InvariantCulture) + " KM";
    }
}
