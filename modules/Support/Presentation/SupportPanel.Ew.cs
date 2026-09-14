using BoscaliSummer.Features.Support.Runtime;
using NOAvionics;
using NOAvionics.Ui;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Features.Support.Presentation
{
    /// <summary>
    /// EW: a physical electronic-warfare presence the player builds — a mobile radar truck
    /// (this phase), later "unloadable" into a static, more powerful but more exposed
    /// encampment (a later phase) — plus the C2 DISRUPTOR/EW DIVISION half of the facility
    /// graph that used to live on CYBER. Radar Blackout, Ghost Shield and Spoof Contacts
    /// require both: the facility unlocked here, and this asset physically near the target
    /// (enforced host-side in <see cref="Actions.HackAction.Execute"/>).
    /// </summary>
    internal sealed partial class SupportPanel
    {
        /// <summary>The asset card's content is fixed-height text and two buttons, not
        /// auto-measured — this is exactly the total it needs (12 top pad + 20 title row +
        /// 4 gap + 30 detail, capped at 2 lines + 4 gap + 26 deploy button + 4 gap + 26
        /// reposition button + 12 bottom pad), so a future edit that adds a line here should
        /// grow this constant to match rather than let content silently overflow the card.</summary>
        private const float EwCardHeight = 140f;
        private const int EwDetailMaxLines = 2;

        private TMP_Text ewSummary;
        private Image ewStatusRail;
        private TMP_Text ewStatusTitle;
        private TMP_Text ewStatusDetail;
        private TMP_Text ewCostValue;
        private AvButton ewDeployButton;
        private AvButton ewRepositionButton;

        private FacilityGraphSection ewGraph;

        private void ResetEwPage()
        {
            ewSummary = null;
            ewStatusRail = null;
            ewStatusTitle = null;
            ewStatusDetail = null;
            ewCostValue = null;
            ewDeployButton = null;
            ewRepositionButton = null;
            ewGraph = null;
            ewScan = default;
        }

        private void BuildEwPage(RectTransform parent, Rect body)
        {
            ewGraph = new FacilityGraphSection { FacilityIds = EwFacilities, HackIds = EwHacks };

            AvNode page = AvBox.Column("ew").Gaps(0f)
                .Add(AvBox.Cell("header").Height(30f))
                .Add(AvBox.Cell("assetTitle").Height(18f))
                .Add(AvBox.Cell("assetCard").Height(EwCardHeight));
            AppendGraphBlock(page, ewGraph);
            page.Add(AvBox.Filler());

            page.Arrange(body);
            float graphHeight = page.At("graph").height;
            float contentHeight = 30f + 18f + EwCardHeight + 18f + graphHeight + 18f + DetailStripHeight + 20f;
            parent = AvScreen.Scroll(parent, body, contentHeight, out body);
            page.Arrange(body);

            AddScanlineOverlay(parent, body);
            ewScan = BuildScanSweep(parent, body);
            AvStyled.Spine(parent, new Rect(body.x, body.y, 3f, body.height));

            Rect header = page.At("header");
            AvKit.Label(parent, "ELECTRONIC WARFARE", new Rect(header.x + SpineInset, header.y, header.width - SpineInset, 18f),
                AvTheme.TextPrimary, AvTokens.FontLead, FontStyles.Bold, TextAlignmentOptions.MidlineLeft);
            ewSummary = AvStyled.Label(parent, new Rect(header.x + SpineInset, header.y - 16f,
                header.width - SpineInset, 14f), "", "row-sub");

            DrawBandTitle(parent, page.At("assetTitle"), "01 / EW ASSET", "DEPLOY, THEN RIGHT-CLICK MAP");

            Rect card = page.At("assetCard");
            AddCardGlow(parent, card, AvTheme.RailCaution);
            (Image _, Image rail) = AvKit.TacticalCard(parent, card, AvTheme.RailInert);
            ewStatusRail = rail;

            float x = card.x + SpineInset + 8f;
            float width = card.width - SpineInset - 16f;
            float y = card.y - 12f;

            ewStatusTitle = AvKit.Label(parent, "NO EW ASSET", new Rect(x, y, width - 110f, 20f),
                AvTheme.Dim, AvTokens.FontLead, FontStyles.Bold, TextAlignmentOptions.MidlineLeft);
            ewCostValue = AvStyled.Label(parent, new Rect(x + width - 110f, y, 110f, 20f), "", "row-value",
                align: TextAlignmentOptions.MidlineRight);
            y -= 24f;

            ewStatusDetail = AvStyled.Label(parent, new Rect(x, y, width, 30f), "", "row-sub");
            ewStatusDetail.maxVisibleLines = EwDetailMaxLines;
            y -= 34f;

            ewDeployButton = AvStyled.Button(parent, new Rect(x, y, width, 26f),
                "DEPLOY EW TRUCK", "btn", () =>
                {
                    support.ArmCommand(OpsCommand.EwDeploy, 0, 0, "DEPLOY EW TRUCK");
                    nextRefresh = 0f;
                }, AvButtonStyle.Primary)
                .WithTooltip("Spend allocation to deploy a mobile radar truck. Electronic-warfare " +
                    "operations will need it near their target.");
            y -= 30f;

            ewRepositionButton = AvStyled.Button(parent, new Rect(x, y, width, 26f),
                "REPOSITION · RIGHT-CLICK MAP", "btn", () =>
                {
                    support.ArmCommand(OpsCommand.EwReposition, 0, 0, "EW TRUCK REPOSITION");
                    nextRefresh = 0f;
                }, AvButtonStyle.Default)
                .WithTooltip("Drive the truck to a new position.");

            BuildGraphBlockWidgets(parent, page, ewGraph,
                "02 / EW INFRASTRUCTURE", "RADAR BLACKOUT/GHOST/SPOOF NEED THE ASSET NEARBY");
        }

        private void RefreshEw(bool bypass)
        {
            if (ewStatusTitle == null) return;

            EwAssetState state = support.LocalEwAssetState;
            bool truckArmed = support.CommandArmed && support.ArmedCommand == OpsCommand.EwDeploy;
            bool repositionArmed = support.CommandArmed && support.ArmedCommand == OpsCommand.EwReposition;

            if (ewSummary != null)
                ewSummary.text = "PHYSICAL PRESENCE FOR ELECTRONIC-WARFARE OPERATIONS";

            switch (state)
            {
                case EwAssetState.Truck:
                    ewStatusRail.color = AvTheme.RailCaution;
                    ewStatusTitle.text = "EW TRUCK · MOBILE";
                    ewStatusTitle.color = AvTheme.TextPrimary;
                    ewStatusDetail.text = "Mobile radar truck on station. Vulnerable; standard EW effect strength.";
                    break;
                case EwAssetState.Encampment:
                    ewStatusRail.color = AvTheme.RailReady;
                    ewStatusTitle.text = "EW ENCAMPMENT · STATIC";
                    ewStatusTitle.color = AvTheme.TextPrimary;
                    ewStatusDetail.text = "Static encampment. More exposed; stronger EW effect radius/duration.";
                    break;
                default:
                    ewStatusRail.color = AvTheme.RailInert;
                    ewStatusTitle.text = "NO EW ASSET";
                    ewStatusTitle.color = AvTheme.Dim;
                    ewStatusDetail.text = "Deploy a radar truck to enable electronic-warfare operations.";
                    break;
            }

            float cost = support.EwTruckCost();
            bool affordable = bypass || support.LocalAllocation + 0.001f >= cost;
            ewCostValue.text = state == EwAssetState.None ? cost.ToString("N0") + " ALLOC" : "—";
            ewCostValue.color = state != EwAssetState.None ? AvTheme.Dim
                : affordable ? AvTheme.TextPrimary : AvTheme.Warning;

            if (ewDeployButton != null)
            {
                bool canDeploy = state == EwAssetState.None && !support.CommandPending;
                ewDeployButton.SetLatched(truckArmed);
                ewDeployButton.SetText(truckArmed ? "AWAITING GROUND STATION" : "DEPLOY EW TRUCK");
                ewDeployButton.SetEnabled(truckArmed || (canDeploy && affordable));
                ewDeployButton.WithTooltip(state != EwAssetState.None
                    ? "Only one EW asset per faction."
                    : "Spend " + cost.ToString("N0") + " allocation to deploy a mobile radar truck.");
            }

            if (ewRepositionButton != null)
            {
                bool canReposition = state == EwAssetState.Truck && !support.CommandPending;
                ewRepositionButton.SetLatched(repositionArmed);
                ewRepositionButton.SetText(repositionArmed ? "AWAITING GROUND STATION" : "REPOSITION · RIGHT-CLICK MAP");
                ewRepositionButton.SetEnabled(repositionArmed || canReposition);
                ewRepositionButton.WithTooltip(state == EwAssetState.Truck
                    ? "Drive the truck to a new position."
                    : "Only available while the asset is a mobile truck.");
            }

            RefreshGraphSection(support.LocalInfo, bypass, ewGraph);
        }
    }
}
