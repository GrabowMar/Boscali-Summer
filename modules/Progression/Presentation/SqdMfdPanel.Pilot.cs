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
        private const string NoBackground = "No service background on file. One can be written in the Wing Command pilot studio.";
        private AvStatTile tileNext, tileAce, tilePicks;

        // Last values painted, so a 5 Hz tick with nothing new builds no strings and touches no text.
        private int idSig;
        private bool idSigSet, ledgerSigSet;
        private int ledgerSig;
        private string lastBackground;
        private int lastGen = int.MinValue, lastClock = -1, lastDeaths = int.MinValue, lastSortie = int.MinValue, lastMissionScore = int.MinValue;
        private string lastAirframe, airframeText, fuelText;
        private int lastFuel = -1;
        private string genText, clockText, deathsText, sortieText, missionScoreText;

        private void ResetPilotPage()
        {
            pilotIdentity = null;
            tileSortie = tileTime = tileFuel = tileDeaths = null;
            pilotBackground = null;
            sortieGrid = null;
            tileNext = tileAce = tilePicks = null;
            idSigSet = ledgerSigSet = false;
            lastBackground = null;
            lastAirframe = airframeText = fuelText = null;
            lastFuel = -1;
            genText = clockText = deathsText = sortieText = missionScoreText = null;
            lastGen = lastDeaths = lastSortie = lastMissionScore = int.MinValue;
            lastClock = -1;
        }

        /// <summary>The cached text of an int: rebuilt only when the number changes.</summary>
        private static string Num(int value, ref int last, ref string text)
        {
            if (text == null || value != last) { last = value; text = AvNum.Thousands(value); }
            return text;
        }

        private static int Mix(int h, int v) => unchecked(h * 31 + v);
        private static int Mix(int h, string s) => unchecked(h * 31 + (s == null ? 0 : s.GetHashCode()));

        private void BuildPilotPage(AvFlow p)
        {
            pilotIdentity = p.Add(new PilotIdCard(p.Content));

            pilotBackground = p.Add(new SqdQuote(p.Content, "SERVICE BACKGROUND"));
            pilotBackground.Set(NoBackground);

            p.Section(AvIcon.ListDetails, "FLIGHT LOG", "THIS SORTIE");
            tileSortie = new AvStatTile(p.Content, "SORTIE");
            tileTime = new AvStatTile(p.Content, "MISSION");
            tileFuel = new AvStatTile(p.Content, "FUEL");
            tileDeaths = new AvStatTile(p.Content, "DEATHS");
            p.Row(tileSortie, tileTime, tileFuel, tileDeaths);

            sortieGrid = p.Add(new SqdStatGrid(p.Content));
            sortieAirframe = sortieGrid.Add("AIRFRAME");
            sortieCondition = sortieGrid.Add("CONDITION");
            sortieLife = sortieGrid.Add("LIFE MODE");
            sortieMission = sortieGrid.Add("MISSION SCORE");
            sortieGrid.Help = "This sortie: the airframe you fly, its condition, whether you respawn or have one life, and the mission score the pick ladder is built from.";

            // One ladder, one row: score to the next pick, the ace bonus, and the picks in hand. They are spent on SKILLS.
            p.Section(AvIcon.Star, "PICK LEDGER", "MISSION SCORE TO PICKS");
            tileNext = new AvStatTile(p.Content, "NEXT PICK");
            tileAce = new AvStatTile(p.Content, "ACE BONUS");
            tilePicks = new AvStatTile(p.Content, "FREE / EARNED");
            p.Row(tileNext, tileAce, tilePicks);

            BuildAceWatch(p);
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

            if (!string.Equals(lastBackground, background, StringComparison.Ordinal))
            {
                lastBackground = background;
                pilotBackground.Set(string.IsNullOrEmpty(background) ? NoBackground : background);
            }

            Sprite portraitSprite = PlayerPortrait(name, callsign);
            int rank = Progress.Rank;
            bool kia = pilot.Status != null && pilot.Status.IndexOf("KIA", StringComparison.OrdinalIgnoreCase) >= 0;
            int sig = 17;
            sig = Mix(sig, callsign); sig = Mix(sig, name); sig = Mix(sig, pilot.Status); sig = Mix(sig, squadronName);
            sig = Mix(sig, rank); sig = Mix(sig, pilotScore); sig = Mix(sig, pilot.Generation);
            sig = Mix(sig, (profile ? 1 : 0) | (bypass ? 2 : 0) | (kia ? 4 : 0) | (playerAircraft != null ? 8 : 0));
            sig = Mix(sig, portraitSprite == null ? 0 : portraitSprite.GetInstanceID());
            sig = Mix(sig, emblemSprite == null ? 0 : emblemSprite.GetInstanceID());
            if (!idSigSet || sig != idSig)
            {
                idSigSet = true;
                idSig = sig;
                string pilotTitle = PilotTitleCatalog.TitleFor(pilotScore, Progress.ScorePerPoint, Progress.MaximumPoints);
                if (genText == null || pilot.Generation != lastGen) { lastGen = pilot.Generation; genText = "GEN " + AvNum.Thousands(lastGen); }
                pilotIdentity.Set(
                    profile ? "PERSONNEL JACKET · LOCAL PROFILE" : "PERSONNEL JACKET",
                    string.IsNullOrEmpty(callsign) ? "RECORD PENDING" : callsign,
                    string.IsNullOrEmpty(name) ? "—" : name,
                    AvNum.Thousands(rank), pilotTitle,
                    genText,
                    string.IsNullOrEmpty(pilot.Status) ? "NO STATUS ON FILE" : pilot.Status,
                    kia ? AvState.Danger : playerAircraft != null ? AvState.Ready : AvState.Info,
                    portraitSprite, emblemSprite,
                    string.IsNullOrEmpty(squadronName) ? "NO SQUADRON NAME" : squadronName,
                    bypass ? AvState.Caution : kia ? AvState.Danger : AvState.Ready,
                    bypass ? "DEBUG" : kia ? "KIA" : "ACTIVE");
            }

            if (playerAircraft != null)
            {
                string airframe = playerAircraft.definition != null
                    ? playerAircraft.definition.unitName : playerAircraft.unitName;
                if (!ReferenceEquals(airframe, lastAirframe) || airframeText == null)
                {
                    lastAirframe = airframe;
                    airframeText = string.IsNullOrEmpty(airframe) ? "AIRCRAFT" : airframe.ToUpperInvariant();
                }
                sortieGrid.Set(sortieAirframe, airframeText);
                bool disabled = playerAircraft.disabled;
                sortieGrid.Set(sortieCondition, disabled ? "DISABLED" : playerAircraft.IsLanded() ? "LANDED" : "AIRBORNE",
                    disabled ? AvState.Danger : AvState.Ready);
                float fuel = BoscaliSummer.Core.Game.AircraftReadout.Fuel(playerAircraft);
                int fuelPct = Mathf.RoundToInt(fuel * 100f);
                if (fuelPct != lastFuel || fuelText == null) { lastFuel = fuelPct; fuelText = AvNum.Percent(fuel); }
                tileFuel.Set(fuelText, fuel <= .15f ? AvState.Caution : AvState.Inert);
                tileSortie.Set(Num((int)playerAircraft.sortieScore, ref lastSortie, ref sortieText));
            }
            else
            {
                sortieGrid.Set(sortieAirframe, "NO AIRCRAFT");
                sortieGrid.Set(sortieCondition, "GROUND", AvState.Inert);
                tileFuel.Set(null);
                tileSortie.Set(null);
            }

            int clock = Mathf.FloorToInt(Time.timeSinceLevelLoad);
            if (clock != lastClock || clockText == null) { lastClock = clock; clockText = AvNum.Clock(clock); }
            tileTime.Set(clockText);
            tileDeaths.Set(Num(pilot.Deaths, ref lastDeaths, ref deathsText), pilot.Deaths > 0 ? AvState.Caution : AvState.Inert);

            sortieGrid.Set(sortieLife, pilot.Respawns ? "RESPAWNING" : "ONE LIFE",
                pilot.Respawns ? AvState.Info : AvState.Caution);
            sortieGrid.Set(sortieMission, Num(view.Score, ref lastMissionScore, ref missionScoreText));

            // The host's ramp (grade n costs n × the interval), never a score modulo; -1 once the ladder is spent.
            int toNext = PerkPoints.RemainingToNext(pilotScore, view.ScorePerPoint);
            int earned = view.EarnedPoints;
            int available = view.AvailablePoints;
            bool complete = earned >= view.MaximumPoints || toNext < 0;

            int ledger = Mix(Mix(Mix(Mix(Mix(17, toNext), bonus), available), earned), (bypass ? 1 : 0) | (complete ? 2 : 0));
            if (ledger != ledgerSig || !ledgerSigSet)
            {
                ledgerSig = ledger;
                ledgerSigSet = true;
                tileNext.Set(bypass ? "DEBUG" : complete ? "MAX" : AvNum.Thousands(toNext), bypass ? AvState.Caution : AvState.Info);
                tileAce.Set("+" + AvNum.Thousands(bonus), bonus > 0 ? AvState.Ready : AvState.Inert);
                tilePicks.Set(bypass ? "ALL" : AvNum.Thousands(available) + " / " + AvNum.Thousands(earned),
                    bypass || available > 0 ? AvState.Ready : AvState.Inert);
            }

            RefreshAceWatch();
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
            private readonly TMP_Text statusIcon;
            private readonly AvChip stateChip;
            private AvState status = AvState.Info;
            private AvIcon statusGlyph = AvIcon.None;

            public PilotIdCard(RectTransform parent)
            {
                Rect = AvLay.Child(parent, "PilotIdentity");
                frame = AvFrame.Add(Rect, "Frame", default);
                AvLay.Fill(frame.rectTransform);
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
                AvText.Set(profileTag, profileTagText);
                AvText.Set(callsignText, callsign);
                AvText.Set(nameLine, name);
                AvText.Set(rankNumber, rank);
                AvText.Set(rankTitle, title);
                AvText.Set(generationText, generation);
                AvText.Set(statusText, statusLine);
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
                AvText.Set(squadronText, squadron ?? "");
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
                frame.PaintCard("card raised", AvTheme.SurfaceRaised, AvTheme.Frame);
                rankFrame.PaintCard("card inert", AvTheme.SurfaceInert, AvTheme.Hairline);
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
