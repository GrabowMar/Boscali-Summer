using System;
using BoscaliSummer.Features.Progression.Domain;
using BoscaliSummer.Features.Progression.Runtime;
using BoscaliSummer.Framework.Contracts;
using NOAvionics;
using NOAvionics.Ui;
using NuclearOption.Networking;
using TMPro;
using UnityEngine;

namespace BoscaliSummer.Features.Progression.Presentation
{
    internal sealed partial class SqdMfdPanel
    {
        private const int PilotChipColumns = 3; // 3 columns: the longest grade word (SURVEILLANCE) must fit one chip

        private PilotIdentityCard pilotIdentity;
        private AvStatTile tileSortie, tileTime, tileFuel, tileDeaths;
        private AvTextBlock pilotBackground;

        private AvKeyValue pilotMode, pilotStatus, pilotDeaths, pilotGeneration;
        private AvKeyValue runAirframeValue, runTimeValue, runFlightStatusValue, runFuelValue, runSortieScoreValue;
        private AvKeyValue runRankValue, runMissionScoreValue, pilotScoreValue, aceBonusValue, runNextPerkValue;
        private AvKeyValue earnedValue, spentValue, availableValue;

        private AvTextBlock committedSkillsEmpty;
        private readonly AvChip[] committedChips = new AvChip[PerkCatalog.All.Length];

        private void ResetPilotPage()
        {
            pilotIdentity = null;
            tileSortie = tileTime = tileFuel = tileDeaths = null;
            pilotBackground = null;
            pilotMode = pilotStatus = pilotDeaths = pilotGeneration = null;
            runAirframeValue = runTimeValue = runFlightStatusValue = runFuelValue = runSortieScoreValue = null;
            runRankValue = runMissionScoreValue = pilotScoreValue = aceBonusValue = runNextPerkValue = null;
            earnedValue = spentValue = availableValue = null;
            committedSkillsEmpty = null;
            Array.Clear(committedChips, 0, committedChips.Length);
        }

        private void BuildPilotPage(AvFlow p)
        {
            p.Section(AvIcon.User, "PILOT RECORD", "LOCAL + HOST DATA");
            pilotIdentity = p.Add(new PilotIdentityCard(p.Content));

            tileSortie = new AvStatTile(p.Content, "SORTIE");
            tileTime = new AvStatTile(p.Content, "MISSION");
            tileFuel = new AvStatTile(p.Content, "FUEL");
            tileDeaths = new AvStatTile(p.Content, "DEATHS");
            p.Row(tileSortie, tileTime, tileFuel, tileDeaths);

            p.Section(AvIcon.Typography, "SERVICE NOTE", "LOCAL PROFILE");
            pilotBackground = p.Add(new AvTextBlock(p.Content, AvTextRole.Prose));
            pilotBackground.Set("No service background on file.");

            float half = AvFlowMath.ColumnWidth(p.Inner, 2, AvGridTokens.Gap);

            AvCard life = new AvCard(p.Content, console.Ticker, half, "CURRENT LIFE · SORTIE");
            pilotMode = life.Flow.Add(new AvKeyValue(life.Flow.Content, "LIFE MODE"));
            pilotStatus = life.Flow.Add(new AvKeyValue(life.Flow.Content, "STATUS"));
            pilotDeaths = life.Flow.Add(new AvKeyValue(life.Flow.Content, "DEATHS"));
            pilotGeneration = life.Flow.Add(new AvKeyValue(life.Flow.Content, "GENERATION"));
            runAirframeValue = life.Flow.Add(new AvKeyValue(life.Flow.Content, "AIRFRAME"));
            runTimeValue = life.Flow.Add(new AvKeyValue(life.Flow.Content, "ELAPSED"));
            runFlightStatusValue = life.Flow.Add(new AvKeyValue(life.Flow.Content, "CONDITION"));
            runFuelValue = life.Flow.Add(new AvKeyValue(life.Flow.Content, "FUEL"));
            runSortieScoreValue = life.Flow.Add(new AvKeyValue(life.Flow.Content, "SORTIE SCORE"));

            AvCard career = new AvCard(p.Content, console.Ticker, half, "CAREER TOTALS");
            runRankValue = career.Flow.Add(new AvKeyValue(career.Flow.Content, "RANK"));
            runMissionScoreValue = career.Flow.Add(new AvKeyValue(career.Flow.Content, "MISSION"));
            pilotScoreValue = career.Flow.Add(new AvKeyValue(career.Flow.Content, "THIS PILOT"));
            aceBonusValue = career.Flow.Add(new AvKeyValue(career.Flow.Content, "ACE BONUS"));
            runNextPerkValue = career.Flow.Add(new AvKeyValue(career.Flow.Content, "NEXT PICK"));
            earnedValue = career.Flow.Add(new AvKeyValue(career.Flow.Content, "EARNED"));
            spentValue = career.Flow.Add(new AvKeyValue(career.Flow.Content, "SPENT"));
            availableValue = career.Flow.Add(new AvKeyValue(career.Flow.Content, "UNSPENT"));

            p.Row(life, career);

            p.Section(AvIcon.Star, "COMMITTED SKILLS", null);
            committedSkillsEmpty = p.Add(new AvTextBlock(p.Content, AvTextRole.ProseSmall));
            committedSkillsEmpty.Set("No skills committed yet. Open SKILLS to choose one.");
            AvCellGrid grid = p.Grid(PilotChipColumns);
            for (int i = 0; i < committedChips.Length; i++)
            {
                committedChips[i] = new AvChip(p.Content);
                grid.Add(committedChips[i]);
            }
        }

        // ---- PILOT refresh ---------------------------------------------------------------

        private void RefreshPilotPage(bool bypass, int pilotScore, int bonus)
        {
            if (pilotIdentity == null) return;
            PilotView pilot = squad != null ? squad.Pilot : default;
            bool profile = hasLocalProfile && !string.IsNullOrEmpty(localProfile.Callsign);
            string callsign = profile ? localProfile.Callsign : pilot.Callsign;
            string name = profile ? localProfile.Name : pilot.Name;
            string background = profile && !string.IsNullOrEmpty(localProfile.Background)
                ? localProfile.Background : pilot.Background;

            pilotBackground.Set(string.IsNullOrEmpty(background) ? "No service background on file." : background);
            string pilotTitle = PilotTitleCatalog.TitleFor(pilotScore, Progress.ScorePerPoint, Progress.MaximumPoints);
            bool kia = pilot.Status != null && pilot.Status.IndexOf("KIA", StringComparison.OrdinalIgnoreCase) >= 0;
            pilotIdentity.Set(
                profile ? "LOCAL PROFILE" : "PILOT DOSSIER",
                string.IsNullOrEmpty(callsign) ? "PILOT RECORD PENDING" : callsign,
                string.IsNullOrEmpty(name) ? "—" : name,
                "RANK " + AvNum.Thousands(Progress.Rank) + "   ·   GEN " + AvNum.Thousands(pilot.Generation) +
                    "   ·   " + pilotTitle,
                pilot.Status,
                PlayerPortrait(name, callsign), emblemSprite,
                string.IsNullOrEmpty(squadronName) ? "NO SQUADRON NAME" : squadronName,
                bypass ? AvState.Caution : kia ? AvState.Danger : AvState.Ready,
                bypass ? "DEBUG" : kia ? "KIA" : "ACTIVE");

            IProgressionView view = Progress;
            Player localPlayer;
            Aircraft playerAircraft = null;
            if (GameManager.GetLocalPlayer<Player>(out localPlayer) && localPlayer != null)
                playerAircraft = localPlayer.Aircraft;

            if (playerAircraft != null)
            {
                string airframe = playerAircraft.definition != null
                    ? playerAircraft.definition.unitName : playerAircraft.unitName;
                runAirframeValue.Set(string.IsNullOrEmpty(airframe) ? "AIRCRAFT" : airframe.ToUpperInvariant());
                runSortieScoreValue.Set(AvNum.Thousands(playerAircraft.sortieScore));
                bool disabled = playerAircraft.disabled;
                runFlightStatusValue.Set(disabled ? "DISABLED" : playerAircraft.IsLanded() ? "LANDED" : "AIRBORNE",
                    disabled ? AvState.Danger : AvState.Ready);
                string fuelText = AvNum.Percent(playerAircraft.fuelLevel);
                runFuelValue.Set(fuelText);
                tileFuel.Set(fuelText);
                tileSortie.Set(AvNum.Thousands(playerAircraft.sortieScore));
            }
            else
            {
                runAirframeValue.Set("NO AIRCRAFT");
                runSortieScoreValue.Set(null);
                runFlightStatusValue.Set("GROUND", AvState.Inert);
                runFuelValue.Set(null);
                tileFuel.Set(null);
                tileSortie.Set(null);
            }

            int minutes = Mathf.FloorToInt(Time.timeSinceLevelLoad / 60f);
            int seconds = Mathf.FloorToInt(Time.timeSinceLevelLoad % 60f);
            string elapsed = AvNum.Clock(minutes * 60 + seconds);
            runTimeValue.Set(elapsed);
            tileTime.Set(elapsed);

            pilotMode.Set(pilot.Respawns ? "RESPAWNING" : "ONE LIFE", pilot.Respawns ? AvState.Info : AvState.Caution);
            pilotStatus.Set(pilot.Status);
            pilotDeaths.Set(AvNum.Thousands(pilot.Deaths));
            pilotGeneration.Set(AvNum.Thousands(pilot.Generation));
            tileDeaths.Set(AvNum.Thousands(pilot.Deaths));

            runRankValue.Set(AvNum.Thousands(view.Rank));
            runMissionScoreValue.Set(AvNum.Thousands(view.Score));
            pilotScoreValue.Set(AvNum.Thousands(pilotScore));
            aceBonusValue.Set("+" + AvNum.Thousands(bonus) + "P");

            int perPoint = Math.Max(1, view.ScorePerPoint);
            int toNext = perPoint - (pilotScore % perPoint);
            runNextPerkValue.Set(bypass ? "BYPASS"
                : view.EarnedPoints >= view.MaximumPoints ? "COMPLETE" : AvNum.Thousands(toNext));

            int earned = view.EarnedPoints;
            int available = view.AvailablePoints;
            int spent = Math.Max(0, earned - available);
            int ceiling = Math.Max(1, view.MaximumPoints);

            earnedValue.Set(bypass ? "BYPASS" : earned + "/" + ceiling);
            spentValue.Set(bypass ? null : AvNum.Thousands(spent));
            availableValue.Set(bypass ? "UNLIMITED" : AvNum.Thousands(available),
                !bypass && available > 0 ? AvState.Ready : AvState.Inert);

            RefreshCommittedSkills(view.GetPerks());
        }

        private void RefreshCommittedSkills(PerkView[] perks)
        {
            int slot = 0;
            for (int i = 0; i < PerkCatalog.All.Length; i++)
            {
                PerkDefinition definition = PerkCatalog.All[i];
                bool unlocked = false;
                for (int j = 0; j < perks.Length; j++)
                {
                    if (perks[j].Id != definition.Id) continue;
                    unlocked = perks[j].Unlocked;
                    break;
                }
                if (!unlocked || slot >= committedChips.Length) continue;

                committedChips[slot].Rect.gameObject.SetActive(true);
                string word = definition.Capability == null
                    ? definition.Name.Split(' ')[0].ToUpperInvariant()
                    : PerkCatalog.CodeOf(definition);
                committedChips[slot].Set(word, definition.Capability == null ? AvState.Ready : AvState.Info);
                slot++;
            }

            for (int i = slot; i < committedChips.Length; i++)
                committedChips[i].Rect.gameObject.SetActive(false);

            if (committedSkillsEmpty != null)
                committedSkillsEmpty.Rect.gameObject.SetActive(slot == 0);
        }

        /// <summary>
        /// The pilot identity strip: portrait, callsign/name/rank/status, a state chip and the
        /// squadron emblem. Absolute-positioned (not a vertical <see cref="AvFlow"/>) because its
        /// three columns (photo / text / emblem) have unrelated natural widths.
        /// </summary>
        private sealed class PilotIdentityCard : AvPart
        {
            private readonly AvFrame frame;
            private readonly AvPortrait portrait;
            private readonly TMP_Text profileTag, callsignText, nameLine, rankLine, statusLine;
            private readonly AvPortrait emblem;
            private readonly TMP_Text squadronText;
            private readonly AvChip stateChip;

            public PilotIdentityCard(RectTransform parent)
            {
                Rect = AvLay.Child(parent, "PilotIdentity");
                frame = AvFrame.Add(Rect, "Frame", AvChamfer.Diagonal(6f));
                AvLay.Fill(frame.rectTransform);
                frame.Bracket = 6f;

                portrait = new AvPortrait(Rect, "Pilot");
                profileTag = AvText.Make(Rect, "ProfileTag", AvTextRole.Micro);
                callsignText = AvText.Make(Rect, "Callsign", AvTextRole.Title);
                AvText.Fit(callsignText, false);
                nameLine = AvText.Make(Rect, "Name", AvTextRole.Prose);
                rankLine = AvText.Make(Rect, "Rank", AvTextRole.ProseSmall);
                AvText.Fit(rankLine, false);
                statusLine = AvText.Make(Rect, "Status", AvTextRole.ProseSmall);

                emblem = new AvPortrait(Rect, "Emblem", "NO ART");
                squadronText = AvText.Make(Rect, "Squadron", AvTextRole.Micro, "", TextAlignmentOptions.Center, true);
                stateChip = new AvChip(Rect);

                Restyle();
            }

            public void Set(string profileTagText, string callsign, string name, string rank, string status,
                Sprite photo, Sprite emblemSprite, string squadron, AvState state, string stateWord)
            {
                profileTag.text = profileTagText ?? "";
                callsignText.text = callsign ?? "";
                nameLine.text = name ?? "";
                rankLine.text = rank ?? "";
                statusLine.text = status ?? "";
                portrait.Set(photo);
                emblem.Set(emblemSprite);
                squadronText.text = squadron ?? "";
                stateChip.Set(stateWord, state);
            }

            public override float Measure(float width) => 128f;

            public override void Place(AvSlot s)
            {
                base.Place(s);
                const float portraitW = 76f, emblemW = 56f;
                portrait.Place(new AvSlot(6f, 6f, portraitW, s.H - 12f));
                float textX = portraitW + 16f;
                float textW = s.W - textX - emblemW - 16f;
                AvLay.Place(profileTag.rectTransform, textX, 6f, textW, 14f);
                AvLay.Place(callsignText.rectTransform, textX, 20f, textW, 22f);
                AvLay.Place(nameLine.rectTransform, textX, 44f, textW, 16f);
                AvLay.Place(rankLine.rectTransform, textX, 62f, textW, 15f);
                AvLay.Place(statusLine.rectTransform, textX, 80f, textW, 15f);
                stateChip.Place(new AvSlot(textX, s.H - 26f, 120f, AvGridTokens.ChipStrip));

                float emblemX = s.W - emblemW - 6f;
                emblem.Place(new AvSlot(emblemX, 6f, emblemW, emblemW));
                AvLay.Place(squadronText.rectTransform, emblemX - 4f, emblemW + 10f, emblemW + 8f,
                    Mathf.Max(0f, s.H - emblemW - 16f));
            }

            public override void Restyle()
            {
                AvStyle c = AvStyleHost.FuiStyle("card raised");
                frame.Paint(AvStyleHost.Resolve(c.Background, AvTheme.SurfaceRaised),
                    AvStyleHost.Resolve(c.Border, AvTheme.Frame));
                frame.BracketColor = AvStyleHost.Resolve(AvStyleHost.FuiStyle("card-bracket").Background, AvTheme.Frame);
                profileTag.color = AvStyleHost.Resolve(AvStyleHost.FuiStyle("section-caption").Color, AvTheme.Dim);
                callsignText.color = AvStyleHost.Resolve(AvStyleHost.FuiStyle("title").Color, AvTheme.TextPrimary);
                nameLine.color = AvStyleHost.Resolve(AvStyleHost.FuiStyle("row-name").Color, AvTheme.TextPrimary);
                rankLine.color = statusLine.color = AvStyleHost.Resolve(AvStyleHost.FuiStyle("row-sub").Color, AvTheme.Dim);
                squadronText.color = AvStyleHost.Resolve(AvStyleHost.FuiStyle("section-caption").Color, AvTheme.Dim);
                portrait.Restyle();
                emblem.Restyle();
                stateChip.Restyle();
            }
        }
    }
}
