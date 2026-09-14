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
        private TMP_Text pilotPortraitFallback;
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
        private readonly SqdGlyph[] committedIcons = new SqdGlyph[PerkCatalog.MaximumPerks];
        private readonly TMP_Text[] committedLabels = new TMP_Text[PerkCatalog.MaximumPerks];

        private void ResetPilotPage()
        {
            pilotPortraitImage = null;
            pilotPortraitFallback = null;
            pilotCallsign = pilotProfileTag = pilotName = pilotRankLine = pilotStatusLine = pilotBackground = null;
            pilotEmblemImage = null;
            pilotEmblemFallback = pilotSquadron = null;
            pilotProgressFill = null;
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

        private void BuildPilotPage(RectTransform parent, Rect body)
        {
            parent = AvScreen.Scroll(parent, body, 880f, out body);
            AvStyled.Spine(parent, new Rect(body.x, body.y, 3f, body.height));
            float x = body.x + SpineInset;
            float width = body.width - SpineInset;
            float y = body.y;

            // ---- Dossier card ------------------------------------------------------------
            const float cardHeight = 150f;
            AvStyled.Box(parent, new Rect(x, y, width, cardHeight), "section band");
            AvStyled.SpineTick(parent, x - SpineInset + 3f, y - 12f);

            Rect portraitFrame = new Rect(x + 10f, y - 8f, 92f, 138f);
            AvKit.Panel(parent, portraitFrame, new Color32(18, 22, 26, 255));
            AvKit.Outline(parent, portraitFrame, AvTheme.Frame);
            pilotPortraitFallback = PlainLabel(parent,
                new Rect(portraitFrame.x + 4f, portraitFrame.y - 56f, portraitFrame.width - 8f, 40f),
                "NO\nVISUAL", "row-sub");
            pilotPortraitFallback.alignment = TextAlignmentOptions.Center;
            pilotPortraitImage = AvKit.Panel(parent,
                new Rect(portraitFrame.x + 3f, portraitFrame.y - 3f, portraitFrame.width - 6f, portraitFrame.height - 6f),
                Color.white);
            pilotPortraitImage.type = Image.Type.Simple;
            pilotPortraitImage.preserveAspect = true;
            pilotPortraitImage.raycastTarget = false;

            float textX = portraitFrame.x + portraitFrame.width + 12f;
            float emblemWidth = 74f;
            float textWidth = width - (textX - x) - emblemWidth - 12f;
            pilotProfileTag = PlainLabel(parent, new Rect(textX, y - 8f, textWidth, 16f), "", "section-title-note");
            pilotProfileTag.alignment = TextAlignmentOptions.MidlineRight;
            pilotCallsign = PlainLabel(parent, new Rect(textX, y - 10f, textWidth, 26f), "PILOT RECORD PENDING", "section-title");
            pilotName = PlainLabel(parent, new Rect(textX, y - 38f, textWidth, 16f), "", "kv-value");
            pilotRankLine = PlainLabel(parent, new Rect(textX, y - 57f, textWidth, 15f), "", "row-sub");
            pilotStatusLine = PlainLabel(parent, new Rect(textX, y - 76f, textWidth, 15f), "", "row-sub");
            pilotProgressFill = AvKit.ProgressBar(parent,
                new Rect(textX, y - 100f, textWidth, 5f), 0f, AvTheme.RailReady);

            float emblemX = x + width - emblemWidth + 6f;
            pilotEmblemFallback = PlainLabel(parent, new Rect(emblemX, y - 42f, 62f, 30f), "NO\nART", "row-sub");
            pilotEmblemFallback.alignment = TextAlignmentOptions.Center;
            pilotEmblemImage = AvKit.Panel(parent, new Rect(emblemX, y - 8f, 62f, 62f), Color.white);
            pilotEmblemImage.type = Image.Type.Simple;
            pilotEmblemImage.preserveAspect = true;
            pilotEmblemImage.raycastTarget = false;
            pilotSquadron = PlainLabel(parent, new Rect(emblemX - 8f, y - 74f, 78f, 14f), "", "row-sub");
            pilotSquadron.alignment = TextAlignmentOptions.Center;
            y -= cardHeight + 10f;

            // ---- Service tiles -----------------------------------------------------------
            float tileWidth = (width - AvTokens.Space2 * 3f) / 4f;
            tileSortie = StatTile(parent, x, y, tileWidth, "SORTIE", "0");
            tileTime = StatTile(parent, x + (tileWidth + AvTokens.Space2), y, tileWidth, "MISSION", "00:00");
            tileFuel = StatTile(parent, x + (tileWidth + AvTokens.Space2) * 2f, y, tileWidth, "FUEL", "—");
            tileDeaths = StatTile(parent, x + (tileWidth + AvTokens.Space2) * 3f, y, tileWidth, "DEATHS", "0");
            y -= 56f;

            y = DrawSectionTitle(parent, x, y, width, "SERVICE BACKGROUND", "PILOT LORE", band: false);
            pilotBackground = AvStyled.Label(parent, new Rect(x, y, width, 42f),
                "No service background on file.", "row-sub");
            y -= 52f;

            y = DrawSectionTitle(parent, x, y, width, "PILOT LIFE", "HOST-AUTHORITATIVE", band: false);
            pilotMode = KeyValue(parent, x, y, width, "PILOT LIFE MODE (F1)");
            y -= 19f;
            pilotStatus = KeyValue(parent, x, y, width, "PILOT STATUS");
            y -= 19f;
            pilotDeaths = KeyValue(parent, x, y, width, "PILOT DEATHS");
            y -= 19f;
            pilotGeneration = KeyValue(parent, x, y, width, "PILOT GENERATION");
            y -= 27f;

            y = DrawSectionTitle(parent, x, y, width, "SORTIE PERFORMANCE", "CURRENT AIRCRAFT", band: false);
            runAirframeValue = KeyValue(parent, x, y, width, "ACTIVE AIRFRAME");
            y -= 19f;
            runTimeValue = KeyValue(parent, x, y, width, "MISSION ELAPSED");
            y -= 19f;
            runFlightStatusValue = KeyValue(parent, x, y, width, "FLIGHT CONDITION");
            y -= 19f;
            runFuelValue = KeyValue(parent, x, y, width, "FUEL QUANTITY");
            y -= 19f;
            runSortieScoreValue = KeyValue(parent, x, y, width, "SORTIE SCORE");
            y -= 27f;

            y = DrawSectionTitle(parent, x, y, width, "CAREER STANDING", "MISSION TOTALS", band: true);
            runRankValue = KeyValue(parent, x, y, width, "PILOT RANK");
            y -= 19f;
            runMissionScoreValue = KeyValue(parent, x, y, width, "MISSION SCORE");
            y -= 19f;
            pilotScoreValue = KeyValue(parent, x, y, width, "CURRENT PILOT SCORE");
            y -= 19f;
            aceBonusValue = KeyValue(parent, x, y, width, "ACE BONUS POINTS");
            y -= 19f;
            runNextPerkValue = KeyValue(parent, x, y, width, "SCORE TO NEXT SKILL POINT");
            y -= 19f;
            earnedValue = KeyValue(parent, x, y, width, "POINTS EARNED");
            y -= 19f;
            spentValue = KeyValue(parent, x, y, width, "POINTS COMMITTED");
            y -= 19f;
            availableValue = KeyValue(parent, x, y, width, "POINTS UNSPENT");
            y -= 24f;

            budgetPips = new Image[MaximumBudgetPips];
            budgetPipSlots = new GameObject[MaximumBudgetPips];
            for (int i = 0; i < MaximumBudgetPips; i++)
            {
                var slot = new GameObject("BudgetPoint_" + i, typeof(RectTransform));
                var rect = (RectTransform)slot.transform;
                rect.SetParent(parent, false);
                AvKit.Place(rect, new Rect(x + i * 15f, y, 12f, 12f));
                AvKit.Outline(rect, new Rect(0f, 0f, 12f, 12f), AvTheme.Hairline);
                budgetPips[i] = AvKit.Panel(rect, new Rect(2f, -2f, 8f, 8f), Color.clear);
                budgetPipSlots[i] = slot;
            }
            y -= 26f;

            y = DrawSectionTitle(parent, x, y, width, "COMMITTED SKILLS", null, band: false);
            committedSkillsEmpty = AvStyled.Label(parent, new Rect(x, y, width, 18f),
                "No skills committed yet. Open SKILLS to choose one.", "row-sub");
            float chipWidth = (width - AvTokens.Space2 * 5f) / 6f;
            for (int i = 0; i < committedIcons.Length; i++)
            {
                float chipX = x + (chipWidth + AvTokens.Space2) * (i % 6);
                float chipY = y - 20f - (i / 6) * 44f;
                committedIcons[i] = SqdGlyph.Create(parent, new Rect(chipX, chipY, 18f, 18f), SqdMarks.FromKey("combat"));
                committedLabels[i] = PlainLabel(parent,
                    new Rect(chipX - 2f, chipY - 16f, chipWidth + 4f, 16f), "", "row-sub");
                committedLabels[i].alignment = TextAlignmentOptions.Center;
                committedIcons[i].gameObject.SetActive(false);
                committedLabels[i].gameObject.SetActive(false);
            }
        }

        private static TMP_Text StatTile(
            RectTransform parent, float x, float y, float width, string caption, string value)
        {
            AvKit.Panel(parent, new Rect(x, y, width, 44f), AvTheme.SurfaceInert);
            AvKit.Rule(parent, new Rect(x, y, width, 2f), AvTheme.RailInfo.WithAlpha(0.6f));
            PlainLabel(parent, new Rect(x + 6f, y - 3f, width - 12f, 12f), caption, "section-title-note");
            TMP_Text label = PlainLabel(parent, new Rect(x + 6f, y - 18f, width - 12f, 22f), value, "row-main");
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
            pilotStatusLine.color = pilot.Status != null && pilot.Status.IndexOf("KIA", StringComparison.OrdinalIgnoreCase) >= 0
                ? AvTheme.Alert : AvTheme.RailReady;
            pilotBackground.text = string.IsNullOrEmpty(background) ? "No service background on file." : background;

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
                runSortieScoreValue.text = "0";
                runFlightStatusValue.text = "GROUND";
                runFlightStatusValue.color = AvTheme.Dim;
                runFuelValue.text = "—";
                if (tileFuel != null) tileFuel.text = "—";
                if (tileSortie != null) tileSortie.text = "0";
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
                    budgetPips[i].color = bypass || i < spent ? AvTheme.Accent
                                        : i < earned ? AvTheme.RailReady
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
