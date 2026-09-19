using System;
using BoscaliSummer.Features.Progression.Runtime;
using BoscaliSummer.Framework.Contracts;
using NOAvionics;
using NOAvionics.Ui;
using NuclearOption.Networking;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Features.Progression.Presentation
{
    internal sealed partial class SqdMfdPanel
    {
        private Image pilotPortraitImage;
        private GameObject pilotPortraitFallback;
        private TMP_Text pilotCallsign;
        private TMP_Text pilotProfileTag;
        private TMP_Text pilotName;
        private TMP_Text pilotRankLine;
        private TMP_Text pilotStatusLine;
        private TMP_Text pilotBackground;
        private Image pilotEmblemImage;
        private TMP_Text pilotEmblemFallback;
        private TMP_Text pilotSquadron;
        private Image pilotProgressFill;
        private Image pilotStateFill;
        private TMP_Text pilotStateBadge;
        private TMP_Text pilotPhotoCaption;

        private TMP_Text tileSortie;
        private TMP_Text tileTime;
        private TMP_Text tileFuel;
        private TMP_Text tileDeaths;

        private TMP_Text pilotMode;
        private TMP_Text pilotStatus;
        private TMP_Text pilotDeaths;
        private TMP_Text pilotGeneration;
        private TMP_Text runAirframeValue;
        private TMP_Text runTimeValue;
        private TMP_Text runFlightStatusValue;
        private TMP_Text runFuelValue;
        private TMP_Text runSortieScoreValue;
        private TMP_Text runRankValue;
        private TMP_Text runMissionScoreValue;
        private TMP_Text pilotScoreValue;
        private TMP_Text aceBonusValue;
        private TMP_Text runNextPerkValue;
        private TMP_Text earnedValue;
        private TMP_Text spentValue;
        private TMP_Text availableValue;
        private Image[] budgetPips;
        private GameObject[] budgetPipSlots;
        private TMP_Text committedSkillsEmpty;
        private readonly SqdGlyph[] committedIcons = new SqdGlyph[PerkCatalog.All.Length];
        private readonly TMP_Text[] committedLabels = new TMP_Text[PerkCatalog.All.Length];

        private void ResetPilotPage()
        {
            pilotPortraitImage = null;
            pilotPortraitFallback = null;
            pilotCallsign = pilotProfileTag = pilotName = pilotRankLine = pilotStatusLine = pilotBackground = null;
            pilotEmblemImage = null;
            pilotEmblemFallback = pilotSquadron = null;
            pilotProgressFill = null;
            pilotStateFill = null;
            pilotStateBadge = pilotPhotoCaption = null;
            tileSortie = tileTime = tileFuel = tileDeaths = null;
            pilotMode = pilotStatus = pilotDeaths = pilotGeneration = null;
            runAirframeValue = runTimeValue = runFlightStatusValue = runFuelValue = runSortieScoreValue = null;
            runRankValue = runMissionScoreValue = pilotScoreValue = aceBonusValue = runNextPerkValue = null;
            earnedValue = spentValue = availableValue = null;
            budgetPips = null;
            budgetPipSlots = null;
            committedSkillsEmpty = null;
            Array.Clear(committedIcons, 0, committedIcons.Length);
            Array.Clear(committedLabels, 0, committedLabels.Length);
        }

        private const float PilotCardHeight = 150f;
        private const float PilotTileHeight = 46f;
        private const float PilotParaHeight = 42f;
        private const float PilotChipPitch = 36f;
        private const float PilotPipsHeight = 42f;

        /// <summary>
        /// The record body is two columns: the pilot's own service on the left, the career
        /// ledger on the right. They are cut to one shared height, and each column spreads
        /// its rows over it, so the lower half of the page can never draw a short column
        /// beside a tall one or pool blank glass under the last field.
        /// </summary>
        private const float PilotRecordRegion = 200f;

        private const float PilotServiceBlock = SheetHeadingHeight + PilotParaHeight;
        /// <summary>Chips per row in the committed-skills block.</summary>
        private const int PilotChipColumns = 6;
        /// <summary>
        /// Heading, the whole chip pool (icon over its label) and the 8px lead-in. The debug
        /// bypass can light every grade, so the block reserves all four rows; a normal career
        /// only ever fills two.
        /// </summary>
        private static readonly int PilotChipRows =
            (PerkCatalog.All.Length + PilotChipColumns - 1) / PilotChipColumns;
        private static readonly float PilotCommittedBlock =
            SheetHeadingHeight + PilotChipRows * PilotChipPitch + 6f;

        private void BuildPilotPage(RectTransform page, Rect body)
        {
            float content = PilotMinContentHeight();
            RectTransform parent = AvScreen.Scroll(page, body, content, out body);
            PageRail(parent, new Rect(body.x, body.y, 3f, body.height));
            float x = body.x + SpineInset;
            float width = body.width - SpineInset;
            float y = body.y;

            y = DrawPageHeader(parent, x, y, width, "PILOT  /  01 OF 04", "PILOT STATUS",
                "LOCAL + HOST DATA");

            // The spare glass is shared on the page's one rhythm first, then into the
            // identity card — the block that can grow without opening a dead band.
            float slack = Mathf.Max(0f, body.height - content);
            float gap = SheetGap(body.height, content, 4);
            float cardHeight = PilotCardHeight + Mathf.Max(0f, Mathf.Min(slack - (gap - SheetBaseGap) * 4f, 24f));

            y = BuildPilotIdentity(parent, x, y, width, cardHeight);
            y -= gap;

            float tileWidth = (width - AvTokens.Space2 * 3f) / 4f;
            tileSortie = StatTile(parent, x, y, tileWidth, "SORTIE", "—");
            tileTime = StatTile(parent, x + (tileWidth + AvTokens.Space2), y, tileWidth, "MISSION", "00:00");
            tileFuel = StatTile(parent, x + (tileWidth + AvTokens.Space2) * 2f, y, tileWidth, "FUEL", "—");
            tileDeaths = StatTile(parent, x + (tileWidth + AvTokens.Space2) * 3f, y, tileWidth, "DEATHS", "0");
            y -= PilotTileHeight + gap;

            y = DrawSectionTitle(parent, x, y, width, "SERVICE NOTE", "LOCAL PROFILE", band: false);
            pilotBackground = AvStyled.Label(parent, new Rect(x, y, width, PilotParaHeight),
                "No service background on file.", "row-sub");
            // DrawSectionTitle already consumed the heading height.
            y -= PilotParaHeight + gap;

            // ---- Record body: service on the left, career ledger on the right -----------
            float gutter = AvTokens.Space3;
            float leftWidth = Mathf.Floor((width - gutter) * 0.52f);
            float rightX = x + leftWidth + gutter;
            float rightWidth = width - leftWidth - gutter;
            float leftPitch = (PilotRecordRegion - 2f * SheetHeadingHeight - AvTokens.Space2) / 9f;
            float rightPitch = (PilotRecordRegion - SheetHeadingHeight - PilotPipsHeight) / 8f;

            float leftY = y;
            leftY = DrawSectionTitle(parent, x, leftY, leftWidth, "CURRENT LIFE", null, band: false);
            pilotMode = Field(parent, leftPitch, x, ref leftY, leftWidth, "LIFE MODE");
            pilotStatus = Field(parent, leftPitch, x, ref leftY, leftWidth, "STATUS");
            pilotDeaths = Field(parent, leftPitch, x, ref leftY, leftWidth, "DEATHS");
            pilotGeneration = Field(parent, leftPitch, x, ref leftY, leftWidth, "GENERATION");
            leftY -= AvTokens.Space2;
            // Column-width headings: "SORTIE PERFORMANCE" and "CAREER STANDING" at the
            // this width would ellipsise in a half-width column, so the short names
            // carry the same meaning and the fields underneath say the rest.
            leftY = DrawSectionTitle(parent, x, leftY, leftWidth, "SORTIE", null, band: false);
            runAirframeValue = Field(parent, leftPitch, x, ref leftY, leftWidth, "AIRFRAME");
            runTimeValue = Field(parent, leftPitch, x, ref leftY, leftWidth, "ELAPSED");
            runFlightStatusValue = Field(parent, leftPitch, x, ref leftY, leftWidth, "CONDITION");
            runFuelValue = Field(parent, leftPitch, x, ref leftY, leftWidth, "FUEL");
            runSortieScoreValue = Field(parent, leftPitch, x, ref leftY, leftWidth, "SORTIE SCORE");

            float rightY = DrawSectionTitle(parent, rightX, y, rightWidth, "CAREER TOTALS", null, band: true);
            runRankValue = Field(parent, rightPitch, rightX, ref rightY, rightWidth, "RANK");
            runMissionScoreValue = Field(parent, rightPitch, rightX, ref rightY, rightWidth, "MISSION");
            pilotScoreValue = Field(parent, rightPitch, rightX, ref rightY, rightWidth, "THIS PILOT");
            aceBonusValue = Field(parent, rightPitch, rightX, ref rightY, rightWidth, "ACE BONUS");
            runNextPerkValue = Field(parent, rightPitch, rightX, ref rightY, rightWidth, "NEXT PICK");
            earnedValue = Field(parent, rightPitch, rightX, ref rightY, rightWidth, "EARNED");
            spentValue = Field(parent, rightPitch, rightX, ref rightY, rightWidth, "SPENT");
            availableValue = Field(parent, rightPitch, rightX, ref rightY, rightWidth, "UNSPENT");

            budgetPips = new Image[MaximumBudgetPips];
            budgetPipSlots = new GameObject[MaximumBudgetPips];
            for (int i = 0; i < MaximumBudgetPips; i++)
            {
                var slot = new GameObject("BudgetPoint_" + i, typeof(RectTransform));
                var rect = (RectTransform)slot.transform;
                rect.SetParent(parent, false);
                AvKit.Place(rect, new Rect(rightX + (i % 10) * 18f, rightY - 2f - (i / 10) * 14f, 11f, 11f));
                AvKit.Outline(rect, new Rect(0f, 0f, 11f, 11f), AvTheme.Hairline);
                budgetPips[i] = AvKit.Panel(rect, new Rect(2f, -2f, 7f, 7f), Color.clear, AvSprites.Led);
                budgetPipSlots[i] = slot;
            }
            // Three pip states, three captions: two greens a shade apart are not a key.
            BudgetKey(parent, rightX, rightY - 30f, AvTheme.Dim, "SPENT");
            BudgetKey(parent, rightX + 64f, rightY - 30f, AvTheme.Accent, "UNSPENT");
            BudgetKey(parent, rightX + 132f, rightY - 30f, AvTheme.Hairline, "OPEN");
            y -= PilotRecordRegion + gap;

            y = DrawSectionTitle(parent, x, y, width, "COMMITTED SKILLS", null, band: false);
            committedSkillsEmpty = AvStyled.Label(parent, new Rect(x, y, width, 28f),
                "No skills committed yet. Open SKILLS to choose one.", "row-sub");
            float chipWidth = (width - AvTokens.Space2 * (PilotChipColumns - 1)) / PilotChipColumns;
            for (int i = 0; i < committedIcons.Length; i++)
            {
                float chipX = x + (chipWidth + AvTokens.Space2) * (i % PilotChipColumns);
                float chipY = y - 8f - (i / PilotChipColumns) * PilotChipPitch;
                committedIcons[i] = SqdGlyph.Create(parent, new Rect(chipX, chipY, 18f, 18f), SqdMarks.FromKey("combat"));
                committedLabels[i] = Fitted(PlainLabel(parent,
                    new Rect(chipX - 2f, chipY - 18f, chipWidth + 4f, 16f), "", "row-sub"));
                // This width would ellipsise a word like SURVEILLANCE in a chip
                // this narrow; the tile is the mark, so the caption only has to name it.
                committedLabels[i].characterSpacing = 0f;
                committedLabels[i].alignment = TextAlignmentOptions.Center;
                committedIcons[i].gameObject.SetActive(false);
                committedLabels[i].gameObject.SetActive(false);
            }
        }

        /// <summary>One budget-pip key: a swatch and its state word, so the pips are a key and not a shade.</summary>
        private static void BudgetKey(RectTransform parent, float x, float y, Color colour, string word)
        {
            AvKit.Panel(parent, new Rect(x, y + 1f, 9f, 9f), colour, AvSprites.Led);
            TMP_Text label = PlainLabel(parent, new Rect(x + 12f, y, 64f, 12f), word, "section-title-note");
            label.enableWordWrapping = false;
        }

        /// <summary>
        /// The content floor the pilot page declares: identity, tiles, service block, the
        /// record body and the full committed-skill pool. The pool is reserved whole because
        /// the debug bypass lights every grade, so no committed chip is ever drawn outside
        /// the height the page scrolled to.
        /// </summary>
        private static float PilotMinContentHeight() =>
            SheetHeaderHeight + PilotCardHeight + PilotTileHeight + PilotServiceBlock +
            PilotRecordRegion + PilotCommittedBlock + SheetBaseGap * 4f;

        /// <summary>The pilot identity card: portrait, identity, readiness and squadron mark.</summary>
        private float BuildPilotIdentity(RectTransform parent, float x, float y, float width, float cardHeight)
        {
            AvKit.Panel(parent, new Rect(x, y, width, cardHeight), AvTheme.SurfaceInert);
            AvKit.Outline(parent, new Rect(x, y, width, cardHeight), AvTheme.Frame.WithAlpha(0.7f));
            AvKit.Rule(parent, new Rect(x, y, 3f, cardHeight), AvTheme.RailInfo);

            Rect portraitFrame = new Rect(x + 10f, y - 8f, 92f, cardHeight - 16f);
            AvKit.Panel(parent, portraitFrame, AvTheme.SurfaceInert);
            AvKit.Outline(parent, portraitFrame, AvTheme.Frame);
            pilotPortraitFallback = VisualPlaceholder(parent,
                new Rect(portraitFrame.x + 12f, portraitFrame.y - 30f, portraitFrame.width - 24f, 46f));
            pilotPortraitImage = AvKit.Panel(parent,
                new Rect(portraitFrame.x + 3f, portraitFrame.y - 3f, portraitFrame.width - 6f, portraitFrame.height - 6f),
                Color.white);
            pilotPortraitImage.type = Image.Type.Simple;
            pilotPortraitImage.preserveAspect = true;
            pilotPortraitImage.raycastTarget = false;
            pilotPortraitImage.enabled = false;
            AvKit.Panel(parent, new Rect(portraitFrame.x + 3f, portraitFrame.y - portraitFrame.height + 19f,
                portraitFrame.width - 6f, 16f), AvTheme.Ground.WithAlpha(0.88f));
            pilotPhotoCaption = PlainLabel(parent,
                new Rect(portraitFrame.x + 4f, portraitFrame.y - portraitFrame.height + 17f,
                         portraitFrame.width - 8f, 14f),
                "NO PHOTO", "photo-cap");
            pilotPhotoCaption.alignment = TextAlignmentOptions.Center;

            float textX = portraitFrame.x + portraitFrame.width + 12f;
            const float emblemWidth = 74f;
            float textWidth = width - (textX - x) - emblemWidth - 12f;
            pilotProfileTag = PlainLabel(parent, new Rect(textX, y - 8f, textWidth, 16f), "", "section-title-note");
            pilotProfileTag.alignment = TextAlignmentOptions.MidlineRight;
            pilotCallsign = PlainLabel(parent, new Rect(textX, y - 10f, textWidth, 26f), "PILOT RECORD PENDING", "section-title");
            pilotName = PlainLabel(parent, new Rect(textX, y - 40f, textWidth, 16f), "", "kv-value");
            pilotRankLine = PlainLabel(parent, new Rect(textX, y - 58f, textWidth, 15f), "", "row-sub");
            pilotStatusLine = PlainLabel(parent, new Rect(textX, y - 76f, textWidth, 15f), "", "row-sub");
            pilotProgressFill = AvKit.ProgressBar(parent,
                new Rect(textX, y - cardHeight + 36f, textWidth, 5f), 0f, AvTheme.RailReady);

            float emblemX = x + width - emblemWidth + 6f;
            pilotEmblemFallback = PlainLabel(parent, new Rect(emblemX, y - 42f, 62f, 30f), "NO\nART", "row-sub");
            pilotEmblemFallback.alignment = TextAlignmentOptions.Center;
            pilotEmblemImage = AvKit.Panel(parent, new Rect(emblemX, y - 8f, 62f, 62f), Color.white);
            pilotEmblemImage.type = Image.Type.Simple;
            pilotEmblemImage.preserveAspect = true;
            pilotEmblemImage.raycastTarget = false;
            pilotEmblemImage.enabled = false;
            // Two lines: a 24-character squadron name does not fit one 78px line, and the
            // name is the reader's own, so it shrinks and wraps rather than being cut.
            pilotSquadron = PlainLabel(parent, new Rect(emblemX - 8f, y - 74f, 78f, 26f), "", "row-sub");
            pilotSquadron.alignment = TextAlignmentOptions.Center;
            pilotSquadron.enableAutoSizing = true;
            pilotSquadron.fontSizeMin = AvTokens.FontMicro;
            pilotSquadron.fontSizeMax = pilotSquadron.fontSize;

            (_, pilotStateFill, pilotStateBadge) = StatusBadge(
                parent, new Rect(x + width - 88f, y - cardHeight + 8f, 84f, 18f), "ACTIVE", "ok");
            return y - cardHeight;
        }

        private static TMP_Text StatTile(
            RectTransform parent, float x, float y, float width, string caption, string value)
        {
            AvKit.Panel(parent, new Rect(x, y, width, PilotTileHeight), AvTheme.SurfaceInert);
            AvKit.Rule(parent, new Rect(x, y, width, 2f), AvTheme.RailInfo.WithAlpha(0.6f));
            PlainLabel(parent, new Rect(x + 6f, y - 3f, width - 12f, 12f), caption, "section-title-note");
            TMP_Text label = PlainLabel(parent, new Rect(x + 6f, y - 19f, width - 12f, 24f), value, "row-main");
            label.enableAutoSizing = true;
            label.fontSizeMin = AvTokens.FontMicro;
            label.fontSizeMax = label.fontSize;
            label.overflowMode = TextOverflowModes.Overflow;
            return label;
        }

        // ---- PILOT refresh ---------------------------------------------------------------

        private void RefreshPilotPage(bool bypass, int pilotScore, int bonus)
        {
            if (runAirframeValue == null) return;
            PilotView pilot = squad != null ? squad.Pilot : default;
            bool profile = hasLocalProfile && !string.IsNullOrEmpty(localProfile.Callsign);
            string callsign = profile ? localProfile.Callsign : pilot.Callsign;
            string name = profile ? localProfile.Name : pilot.Name;
            string background = profile && !string.IsNullOrEmpty(localProfile.Background)
                ? localProfile.Background : pilot.Background;

            pilotCallsign.text = string.IsNullOrEmpty(callsign) ? "PILOT RECORD PENDING" : callsign;
            pilotProfileTag.text = profile ? "LOCAL PROFILE" : "SQUADRON RECORD";
            pilotProfileTag.color = profile ? AvTheme.Accent : AvTheme.Dim;
            pilotName.text = string.IsNullOrEmpty(name) ? "—" : name;
            pilotRankLine.text = "RANK " + Progress.Rank + "   ·   GENERATION " + pilot.Generation;
            pilotStatusLine.text = pilot.Status;
            bool kia = pilot.Status != null && pilot.Status.IndexOf("KIA", StringComparison.OrdinalIgnoreCase) >= 0;
            pilotStatusLine.color = kia ? AvTheme.Alert : AvTheme.RailReady;
            pilotBackground.text = string.IsNullOrEmpty(background) ? "No service background on file." : background;

            if (pilotPhotoCaption != null)
            {
                pilotPhotoCaption.text = string.IsNullOrEmpty(callsign)
                    ? "NO PHOTO" : callsign.ToUpperInvariant();
            }
            if (pilotStateBadge != null)
            {
                PaintStatusBadge(pilotStateFill, pilotStateBadge,
                    bypass ? "warn" : kia ? "bad" : "ok",
                    bypass ? "DEBUG" : kia ? "KIA" : "ACTIVE");
            }

            SetPortrait(pilotPortraitImage, pilotPortraitFallback, PlayerPortrait(name, callsign));

            if (pilotEmblemImage != null)
            {
                pilotEmblemImage.sprite = emblemSprite;
                pilotEmblemImage.enabled = emblemSprite != null;
                pilotEmblemFallback.gameObject.SetActive(emblemSprite == null);
                pilotSquadron.text = string.IsNullOrEmpty(squadronName) ? "NO SQUADRON NAME" : squadronName;
            }

            IProgressionView view = Progress;
            Aircraft playerAircraft = null;
            Player localPlayer = null;
            if (GameManager.GetLocalPlayer<Player>(out localPlayer) && localPlayer != null)
                playerAircraft = localPlayer.Aircraft;

            if (playerAircraft != null)
            {
                string airframe = playerAircraft.definition != null
                    ? playerAircraft.definition.unitName : playerAircraft.unitName;
                runAirframeValue.text = string.IsNullOrEmpty(airframe) ? "AIRCRAFT" : airframe.ToUpperInvariant();
                runSortieScoreValue.text = playerAircraft.sortieScore.ToString("N0");
                runFlightStatusValue.text = playerAircraft.disabled ? "DISABLED"
                    : playerAircraft.IsLanded() ? "LANDED" : "AIRBORNE";
                runFlightStatusValue.color = playerAircraft.disabled ? AvTheme.Alert : AvTheme.RailReady;
                runFuelValue.text = $"{playerAircraft.fuelLevel * 100f:0}%";
                if (tileFuel != null) tileFuel.text = $"{playerAircraft.fuelLevel * 100f:0}%";
                if (tileSortie != null) tileSortie.text = playerAircraft.sortieScore.ToString("N0");
            }
            else
            {
                runAirframeValue.text = "NO AIRCRAFT";
                runSortieScoreValue.text = "—";
                runFlightStatusValue.text = "GROUND";
                runFlightStatusValue.color = AvTheme.Dim;
                runFuelValue.text = "—";
                if (tileFuel != null) tileFuel.text = "—";
                if (tileSortie != null) tileSortie.text = "—";
            }

            int minutes = Mathf.FloorToInt(Time.timeSinceLevelLoad / 60f);
            int seconds = Mathf.FloorToInt(Time.timeSinceLevelLoad % 60f);
            runTimeValue.text = $"{minutes:00}:{seconds:00}";
            if (tileTime != null) tileTime.text = runTimeValue.text;

            pilotMode.text = pilot.Respawns ? "RESPAWNING" : "ONE LIFE";
            pilotMode.color = pilot.Respawns ? AvTheme.RailInfo : AvTheme.RailCaution;
            pilotStatus.text = pilot.Status;
            pilotDeaths.text = pilot.Deaths.ToString();
            pilotGeneration.text = pilot.Generation.ToString();
            if (tileDeaths != null) tileDeaths.text = pilot.Deaths.ToString();

            runRankValue.text = view.Rank.ToString();
            runMissionScoreValue.text = view.Score.ToString("N0");
            pilotScoreValue.text = pilotScore.ToString("N0");
            aceBonusValue.text = "+" + bonus + "P";

            int perPoint = Math.Max(1, view.ScorePerPoint);
            int toNext = perPoint - (pilotScore % perPoint);
            runNextPerkValue.text = bypass ? "BYPASS ACTIVE"
                : view.EarnedPoints >= view.MaximumPoints ? "BUDGET COMPLETE" : toNext.ToString("N0");
            if (pilotProgressFill != null)
                pilotProgressFill.fillAmount = bypass || view.EarnedPoints >= view.MaximumPoints
                    ? 1f : (pilotScore % perPoint) / (float)perPoint;

            int earned = view.EarnedPoints;
            int available = view.AvailablePoints;
            int spent = Math.Max(0, earned - available);
            int ceiling = Math.Max(1, view.MaximumPoints);

            earnedValue.text = bypass ? "BYPASS" : $"{earned} / {ceiling}";
            spentValue.text = bypass ? "—" : spent.ToString();
            availableValue.text = bypass ? "UNLIMITED" : available.ToString();
            availableValue.color = !bypass && available > 0 ? AvTheme.RailReady : AvTheme.TextPrimary;

            if (budgetPips != null)
            {
                for (int i = 0; i < budgetPips.Length; i++)
                {
                    budgetPipSlots[i].SetActive(i < ceiling);
                    budgetPips[i].color = bypass ? AvTheme.Accent
                                        : i < spent ? AvTheme.Dim
                                        : i < earned ? AvTheme.Accent
                                        : Color.clear;
                }
            }

            RefreshCommittedSkills(view.GetPerks());
        }

        private void RefreshCommittedSkills(PerkView[] perks)
        {
            int shown = 0;
            for (int i = 0; i < PerkCatalog.All.Length && i < committedIcons.Length; i++)
            {
                PerkDefinition definition = PerkCatalog.All[i];
                bool unlocked = false;
                for (int j = 0; j < perks.Length; j++)
                {
                    if (perks[j].Id != definition.Id) continue;
                    unlocked = perks[j].Unlocked;
                    break;
                }
                committedIcons[i].gameObject.SetActive(unlocked);
                committedLabels[i].gameObject.SetActive(unlocked);
                if (!unlocked) continue;
                committedIcons[i].Mark = SqdMarks.FromKey(definition.Icon);
                committedIcons[i].SetVerticesDirty();
                committedIcons[i].color = definition.Capability == null ? AvTheme.RailReady : AvTheme.RailInfo;
                committedLabels[i].text = definition.Capability == null
                    ? definition.Name.Split(' ')[0].ToUpperInvariant()
                    : PerkCatalog.CodeOf(definition);
                shown++;
            }
            if (committedSkillsEmpty != null)
                committedSkillsEmpty.gameObject.SetActive(shown == 0);
        }
    }
}
