using NOAvionics;
using System;
using BoscaliSummer.Modules.Progression.Domain;
using BoscaliSummer.Modules.Progression.Runtime;
using BoscaliSummer.Core.Contracts;
using NuclearOption.Networking;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Modules.Progression.Presentation
{
    internal sealed partial class SqdMfdPanel
    {
        private PilotIdCard pilotIdentity;
        private AvStatTile tileSortie, tileTime, tileFuel, tileDeaths;
        private SqdQuote pilotBackground;
        private SqdStatGrid sortieGrid;
        private int sortieAirframe, sortieCondition, sortieLife, sortieMission;
        private const int AceBonusCeiling = 20; // matches the host's ace bonus point ceiling
        private AvGauge ringScore, ringBonus, ringPicks, ringFree;
        private SkillLaneStrip committedLanes;
        private string[] committedLaneNames;
        private readonly bool[][] committedHeld = new bool[8][];

        private void ResetPilotPage()
        {
            pilotIdentity = null;
            tileSortie = tileTime = tileFuel = tileDeaths = null;
            pilotBackground = null;
            sortieGrid = null;
            ringScore = ringBonus = ringPicks = ringFree = null;
            committedLanes = null;
            committedLaneNames = null;
        }

        private void BuildPilotPage(AvFlow p)
        {
            pilotIdentity = p.Add(new PilotIdCard(p.Content));

            tileSortie = new AvStatTile(p.Content, "SORTIE");
            tileTime = new AvStatTile(p.Content, "MISSION");
            tileFuel = new AvStatTile(p.Content, "FUEL");
            tileDeaths = new AvStatTile(p.Content, "DEATHS");
            p.Row(tileSortie, tileTime, tileFuel, tileDeaths);

            pilotBackground = p.Add(new SqdQuote(p.Content));
            pilotBackground.Set("No service background on file.");

            sortieGrid = p.Add(new SqdStatGrid(p.Content));
            sortieAirframe = sortieGrid.Add("AIRFRAME");
            sortieCondition = sortieGrid.Add("CONDITION");
            sortieLife = sortieGrid.Add("LIFE MODE");
            sortieMission = sortieGrid.Add("MISSION SCORE");
            sortieGrid.Help = "This sortie: the airframe you fly, its condition, whether you respawn or have one life, and the mission score the pick ladder is built from.";

            ringScore = new AvGauge(p.Content, "NEXT");
            ringBonus = new AvGauge(p.Content, "ACE");
            ringPicks = new AvGauge(p.Content, "EARNED");
            ringFree = new AvGauge(p.Content, "FREE");
            ringScore.Help = "NEXT: mission score still needed to earn the next skill pick. The ladder fills as you approach it.";
            ringBonus.Help = "ACE: bonus score from ace kills and streaks, added to your career score (ceiling " + AceBonusCeiling + ").";
            ringPicks.Help = "EARNED: skill picks earned so far out of the maximum.";
            ringFree.Help = "FREE: earned picks not yet spent. Spend them on the SKILLS tab; a pick cannot be undone.";
            p.Row(ringScore, ringBonus, ringPicks, ringFree);

            // The lanes come from the catalogue, in table order: one row per qualification, one pip per grade.
            var names = new System.Collections.Generic.List<string>(4);
            for (int i = 0; i < PerkCatalog.All.Length; i++)
                if (!names.Contains(PerkCatalog.All[i].Lane)) names.Add(PerkCatalog.All[i].Lane);
            committedLaneNames = names.ToArray();
            for (int i = 0; i < committedHeld.Length; i++) committedHeld[i] = new bool[PerkCatalog.MaximumDepth];
            committedLanes = p.Add(new SkillLaneStrip(p.Content, committedLaneNames, PerkCatalog.MaximumDepth) { Grow = 1f });
            committedLanes.Help = "HELD: every qualification lane and its six grades. A lit pip is a grade you hold; a dim lane is closed because the career already carries two support tools.";
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

            IProgressionView view = Progress;
            Player localPlayer;
            Aircraft playerAircraft = null;
            if (GameManager.GetLocalPlayer<Player>(out localPlayer) && localPlayer != null)
                playerAircraft = localPlayer.Aircraft;

            pilotBackground.Set(string.IsNullOrEmpty(background) ? "No service background on file." : background);
            string pilotTitle = PilotTitleCatalog.TitleFor(pilotScore, Progress.ScorePerPoint, Progress.MaximumPoints);
            bool kia = pilot.Status != null && pilot.Status.IndexOf("KIA", StringComparison.OrdinalIgnoreCase) >= 0;
            pilotIdentity.Set(
                profile ? "LOCAL PROFILE" : "PILOT DOSSIER",
                string.IsNullOrEmpty(callsign) ? "RECORD PENDING" : callsign,
                string.IsNullOrEmpty(name) ? "—" : name,
                AvNum.Thousands(Progress.Rank), pilotTitle,
                "GEN " + AvNum.Thousands(pilot.Generation),
                string.IsNullOrEmpty(pilot.Status) ? "NO STATUS ON FILE" : pilot.Status,
                kia ? AvState.Danger : playerAircraft != null ? AvState.Ready : AvState.Info,
                PlayerPortrait(name, callsign), emblemSprite,
                string.IsNullOrEmpty(squadronName) ? "NO SQUADRON NAME" : squadronName,
                bypass ? AvState.Caution : kia ? AvState.Danger : AvState.Ready,
                bypass ? "DEBUG" : kia ? "KIA" : "ACTIVE");

            if (playerAircraft != null)
            {
                string airframe = playerAircraft.definition != null
                    ? playerAircraft.definition.unitName : playerAircraft.unitName;
                sortieGrid.Set(sortieAirframe, string.IsNullOrEmpty(airframe) ? "AIRCRAFT" : airframe.ToUpperInvariant());
                bool disabled = playerAircraft.disabled;
                sortieGrid.Set(sortieCondition, disabled ? "DISABLED" : playerAircraft.IsLanded() ? "LANDED" : "AIRBORNE",
                    disabled ? AvState.Danger : AvState.Ready);
                float fuel = Mathf.Clamp01(playerAircraft.fuelLevel);
                tileFuel.Set(AvNum.Percent(fuel), fuel <= .15f ? AvState.Caution : AvState.Inert);
                tileSortie.Set(AvNum.Thousands(playerAircraft.sortieScore));
            }
            else
            {
                sortieGrid.Set(sortieAirframe, "NO AIRCRAFT");
                sortieGrid.Set(sortieCondition, "GROUND", AvState.Inert);
                tileFuel.Set(null);
                tileSortie.Set(null);
            }

            int minutes = Mathf.FloorToInt(Time.timeSinceLevelLoad / 60f);
            int seconds = Mathf.FloorToInt(Time.timeSinceLevelLoad % 60f);
            tileTime.Set(AvNum.Clock(minutes * 60 + seconds));
            tileDeaths.Set(AvNum.Thousands(pilot.Deaths), pilot.Deaths > 0 ? AvState.Caution : AvState.Inert);

            sortieGrid.Set(sortieLife, pilot.Respawns ? "RESPAWNING" : "ONE LIFE",
                pilot.Respawns ? AvState.Info : AvState.Caution);
            sortieGrid.Set(sortieMission, AvNum.Thousands(view.Score));

            int perPoint = Math.Max(1, view.ScorePerPoint);
            int toNext = perPoint - (pilotScore % perPoint);
            int earned = view.EarnedPoints;
            int available = view.AvailablePoints;
            int ceiling = Math.Max(1, view.MaximumPoints);
            bool complete = earned >= view.MaximumPoints;

            ringScore.Set(bypass || complete ? 1f : 1f - toNext / (float)perPoint,
                bypass ? "DBG" : complete ? "MAX" : AvNum.Thousands(toNext), bypass ? AvState.Caution : AvState.Info);
            ringBonus.Set(Mathf.Clamp01(bonus / (float)AceBonusCeiling), "+" + AvNum.Thousands(bonus),
                bonus > 0 ? AvState.Ready : AvState.Inert);
            ringPicks.Set(bypass ? 1f : Mathf.Clamp01(earned / (float)ceiling), bypass ? "ALL" : earned + "/" + ceiling,
                AvState.Info);
            ringFree.Set(bypass ? 1f : Mathf.Clamp01(available / (float)ceiling), bypass ? "ALL" : AvNum.Thousands(available),
                bypass || available > 0 ? AvState.Ready : AvState.Inert);

            RefreshCommittedSkills(view.GetPerks());
        }

        private void RefreshCommittedSkills(PerkView[] perks)
        {
            if (committedLanes == null || committedLaneNames == null) return;
            for (int l = 0; l < committedLaneNames.Length && l < committedHeld.Length; l++)
            {
                Array.Clear(committedHeld[l], 0, committedHeld[l].Length);
                bool closed = false;
                for (int i = 0; i < PerkCatalog.All.Length; i++)
                {
                    PerkDefinition definition = PerkCatalog.All[i];
                    if (!string.Equals(definition.Lane, committedLaneNames[l], StringComparison.Ordinal)) continue;
                    if (!TryFind(perks, definition.Id, out PerkView view)) continue;
                    int grade = definition.Grade - 1;
                    if (grade >= 0 && grade < committedHeld[l].Length) committedHeld[l][grade] = view.Unlocked;
                    if (definition.IsTool && !view.Unlocked && view.Block == PerkView.BlockCap) closed = true;
                }
                committedLanes.SetLane(l, committedHeld[l], closed);
            }
        }

        /// <summary>
        /// The pilot's ID card: portrait at left, callsign in display type, name, rank insignia with
        /// its title, generation, a status line with its glyph, and the squadron emblem at right.
        /// Absolute-positioned (not a vertical <see cref="AvFlow"/>) because its columns (photo / text /
        /// emblem) have unrelated natural widths; every text has a fixed slot so none can run into
        /// another.
        /// </summary>
        private sealed class PilotIdCard : AvPart
        {
            private static Sprite equipmentArt;
            private const float CardH = 156f, StripH = 26f, PortraitW = 92f, EmblemW = 64f, Pad = 10f;
            private readonly AvFrame frame, rankFrame;
            private readonly Image strip;
            private readonly AvPortrait portrait, emblem;
            private readonly TMP_Text profileTag, callsignText, nameLine, rankCaption, rankNumber, rankTitle,
                generationText, statusText, squadronText;
            private TMP_Text statusIcon;
            private readonly AvChip stateChip;
            private AvState status = AvState.Info;
            private AvIcon statusGlyph = AvIcon.None;

            public PilotIdCard(RectTransform parent)
            {
                Rect = AvLay.Child(parent, "PilotIdentity");
                frame = AvFrame.Add(Rect, "Frame", AvChamfer.Diagonal(6f));
                AvLay.Fill(frame.rectTransform);
                frame.Bracket = 6f;
                AvBundle.Engraving(Rect, 0.75f);
                strip = AvLay.Solid(Rect, "Rule", Color.clear);

                profileTag = AvText.Make(Rect, "ProfileTag", AvTextRole.Micro);
                AvText.Fit(profileTag, false);
                stateChip = new AvChip(Rect);

                portrait = new AvPortrait(Rect, "Pilot");
                callsignText = AvText.Make(Rect, "Callsign", AvTextRole.Headline);
                AvText.Fit(callsignText, false);
                nameLine = AvText.Make(Rect, "Name", AvTextRole.Prose);
                AvText.Fit(nameLine, false);

                rankFrame = AvFrame.Add(Rect, "RankFrame", AvChamfer.Diagonal(5f));
                rankCaption = AvText.Make(Rect, "RankCaption", AvTextRole.Micro, "RANK", TextAlignmentOptions.Center);
                AvText.Fit(rankCaption, false);
                rankNumber = AvText.Make(Rect, "RankNumber", AvTextRole.Display, "", TextAlignmentOptions.Center);
                AvText.Fit(rankNumber, false);
                rankTitle = AvText.Make(Rect, "RankTitle", AvTextRole.Head);
                AvText.Fit(rankTitle, false);
                generationText = AvText.Make(Rect, "Generation", AvTextRole.DataSmall);
                AvText.Fit(generationText, false);

                statusIcon = AvIcons.Make(Rect, AvIcon.Circle, AvGridTokens.IconInline, Color.white);
                statusText = AvText.Make(Rect, "Status", AvTextRole.ProseSmall);
                AvText.Fit(statusText, false);

                emblem = new AvPortrait(Rect, "Emblem", "NO ART");
                squadronText = AvText.Make(Rect, "Squadron", AvTextRole.Micro, "", TextAlignmentOptions.Top, true);
                AvText.Fit(squadronText, true);

                Restyle();
            }

            public void Set(string profileTagText, string callsign, string name, string rank, string title,
                string generation, string statusLine, AvState statusState, Sprite photo, Sprite emblemSprite,
                string squadron, AvState state, string stateWord)
            {
                profileTag.text = profileTagText ?? "";
                callsignText.text = callsign ?? "";
                nameLine.text = name ?? "";
                rankNumber.text = rank ?? "";
                rankTitle.text = title ?? "";
                generationText.text = generation ?? "";
                statusText.text = statusLine ?? "";
                if (statusState != status || statusGlyph == AvIcon.None)
                {
                    status = statusState;
                    statusGlyph = SqdTone.Glyph(statusState);
                    AvIcons.Set(statusIcon, statusGlyph, AvGridTokens.IconInline);
                    Restyle();
                }
                if (photo == null && equipmentArt == null)
                {
                    Texture2D texture = AvBundle.Illustration("pilot-helmet");
                    if (texture != null) equipmentArt = Sprite.Create(texture,
                        new Rect(0f, 0f, texture.width, texture.height), new Vector2(0.5f, 0.5f));
                }
                portrait.Set(photo != null ? photo : equipmentArt);
                portrait.Help = photo != null ? "Pilot portrait from the local profile." : "Generic flight equipment illustration. The callsign and service record beside it identify your pilot.";
                emblem.Set(emblemSprite);
                squadronText.text = squadron ?? "";
                stateChip.Set(stateWord, state);
            }

            public override float Measure(float width) => CardH;

            public override void Place(AvSlot s)
            {
                base.Place(s);
                AvLay.Place(profileTag.rectTransform, 12f, 6f, s.W - 130f, 14f);
                stateChip.Place(new AvSlot(s.W - Pad - 96f, 2f, 96f, AvGridTokens.ChipStrip));
                AvLay.Place(strip.rectTransform, Pad, StripH, s.W - 2f * Pad, 1f);

                float top = StripH + 8f;
                portrait.Place(new AvSlot(Pad, top, PortraitW, s.H - top - Pad));

                float emblemX = s.W - Pad - EmblemW;
                emblem.Place(new AvSlot(emblemX, top, EmblemW, EmblemW));
                AvLay.Place(squadronText.rectTransform, emblemX - 8f, top + EmblemW + 4f, EmblemW + 16f,
                    Mathf.Max(0f, s.H - top - EmblemW - Pad - 4f));

                float x = Pad + PortraitW + 12f;
                float w = emblemX - 12f - x;
                AvLay.Place(callsignText.rectTransform, x, top - 2f, w, 30f);
                AvLay.Place(nameLine.rectTransform, x, top + 30f, w, 18f);

                float rankY = Mathf.Max(top + 54f, s.H - Pad - 48f);
                AvLay.Place(rankFrame.rectTransform, x, rankY, 50f, 48f);
                AvLay.Place(rankCaption.rectTransform, x, rankY + 3f, 50f, 13f);
                AvLay.Place(rankNumber.rectTransform, x, rankY + 15f, 50f, 30f);
                float tx = x + 60f, tw = Mathf.Max(0f, w - 60f);
                AvLay.Place(rankTitle.rectTransform, tx, rankY, tw, 18f);
                AvLay.Place(generationText.rectTransform, tx, rankY + 17f, tw, 16f);
                AvLay.Place(statusIcon.rectTransform, tx, rankY + 33f, 14f, 14f);
                AvLay.Place(statusText.rectTransform, tx + 18f, rankY + 32f, Mathf.Max(0f, tw - 18f), 16f);
            }

            public override void Restyle()
            {
                AvStyle c = AvStyleHost.FuiStyle("card raised");
                frame.Paint(AvStyleHost.Resolve(c.Background, AvTheme.SurfaceRaised),
                    AvStyleHost.Resolve(c.Border, AvTheme.Frame));
                frame.BracketColor = AvStyleHost.FuiFill("card-bracket", AvTheme.Frame);
                AvStyle inert = AvStyleHost.FuiStyle("card inert");
                rankFrame.Paint(AvStyleHost.Resolve(inert.Background, AvTheme.SurfaceInert), AvStyleHost.Resolve(inert.Border, AvTheme.Hairline));
                strip.color = AvTheme.Hairline;
                profileTag.color = AvInk.Muted;
                callsignText.color = AvStyleHost.FuiInk("title", AvTheme.TextPrimary);
                nameLine.color = AvInk.Ink;
                rankCaption.color = AvInk.Muted;
                rankNumber.color = AvStyleHost.FuiInk("title", AvTheme.TextPrimary);
                rankTitle.color = AvInk.Key;
                generationText.color = AvInk.Dim;
                statusText.color = SqdTone.Text(status);
                if (statusIcon != null) statusIcon.color = SqdTone.Rail(status);
                squadronText.color = AvInk.Muted;
                portrait.Restyle();
                emblem.Restyle();
                stateChip.Restyle();
            }
        }
    }
}
