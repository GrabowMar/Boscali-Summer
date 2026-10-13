using System;
using BoscaliSummer.Modules.Weather.Domain;
using NOAvionics;
using UnityEngine;

namespace BoscaliSummer.Modules.Weather.Presentation
{
    // The window's layout: a hero row (sky, tape, story, next front), three columns (scenarios, stories and log; the sky gallery and inspector; fronts, outlook,
    // set-pieces, placement, rain and identity) and the weather tape with its time controls across the bottom.
    internal sealed partial class MetOfficeWindow
    {
        private static readonly string[] HeroNames = { "SKY NOW", "TAPE", "STORY", "NEXT FRONT" };
        private static readonly float[] HeroWidths = { 500f, 280f, 330f, 0f };
        private static readonly KeyCode[] HotKeys = { KeyCode.H, KeyCode.Comma, KeyCode.Period, KeyCode.S, KeyCode.P, KeyCode.F, KeyCode.R, KeyCode.Backspace };
        private static readonly MetControl[] HotControls =
        {
            MetControl.Hold, MetControl.PaceDown, MetControl.PaceUp, MetControl.StoryNext,
            MetControl.PinSelected, MetControl.Front5, MetControl.Reroll, MetControl.ScenReset,
        };

        private readonly MetHero[] hero = new MetHero[4];
        private readonly AvControl[] classButtons = new AvControl[7];
        private AvControl[] scenarioButtons, storyButtons, setButtons;
        private MetSkyGallery gallery;
        private MetInspectBody inspect;
        private MetCard inspectCard, skyCard, tapeCard;
        private MetLabeledRow pinRow, holdRow, frontRow;
        private MetStepRow paceRow;
        private MetSkyBody sky;
        private MetFrontsBody fronts;
        private MetStoryBody story;
        private EnvOutlookStrip outlook;
        private AvSection outlookSection;
        private AvSegmented anchorRow, rainRow;
        private AvNote identityHead, identitySub, identityNext, logNote, tapeNote;

        // ---- Build ---------------------------------------------------------------------------------

        private void BuildHero()
        {
            float room = MetOfficeLayout.Width - 2f * MetOfficeLayout.Pad - 3f * MetOfficeLayout.ColGap, x = MetOfficeLayout.Pad;
            float last = room - HeroWidths[0] - HeroWidths[1] - HeroWidths[2];
            for (int i = 0; i < hero.Length; i++)
            {
                float w = HeroWidths[i] > 0f ? HeroWidths[i] : last;
                hero[i] = new MetHero(window.Root, HeroNames[i]);
                hero[i].Place(new AvSlot(x, MetOfficeLayout.HeroTop, w, MetOfficeLayout.Hero));
                window.Ticker.Register(hero[i]);
                x += w + MetOfficeLayout.ColGap;
            }
        }

        private void BuildLeft()
        {
            AvFlow f = Column("LeftColumn", MetOfficeLayout.Pad, MetOfficeLayout.Left);

            MetCard scen = Card(f, "SCENARIOS");
            scen.SetCaption("▲ CUT · OVERWRITES EDITS");
            var scenSpecs = new AvControl.Spec[6];
            for (int i = 0; i < 6; i++)
            {
                string later = MetCatalog.ScenarioLater[i] != null ? " · " + MetCatalog.ScenarioLater[i] + " S5" : "";
                scenSpecs[i] = Spec(MetCatalog.From(MetControl.ScenHurricane, i), MetCatalog.ScenarioNames[i] + "\n" + MetCatalog.ScenarioRecipe[i] + later,
                    AvIcon.None, i == 5 ? AvButtonStyle.Danger : AvButtonStyle.Default);
            }
            scenarioButtons = scen.Add(new MetControlGrid(scen.Content, scenSpecs, 1, MetOfficeLayout.ScenarioRow), MetOfficeLayout.ScenarioCard - MetOfficeLayout.CardChrome).Controls;
            for (int i = 0; i < 6; i++) Reg(MetCatalog.From(MetControl.ScenHurricane, i), scenarioButtons[i]);

            MetCard st = Card(f, "STORIES");
            st.SetCaption("▲ CUT · REPEATS ALL TAPE");
            var storySpecs = new AvControl.Spec[WeatherStories.Count + 1];
            storySpecs[0] = Spec(MetControl.StoryAuto, "AUTO");
            for (int i = 0; i < WeatherStories.Count; i++) { int pick = i; storySpecs[i + 1] = new AvControl.Spec(WeatherStories.Name(i).ToUpperInvariant(), () => PressStory(pick)); }
            storyButtons = st.Add(new MetControlGrid(st.Content, storySpecs, 1, MetOfficeLayout.StoryRow), MetOfficeLayout.StoriesCard - MetOfficeLayout.CardChrome).Controls;
            Reg(MetControl.StoryAuto, storyButtons[0]);
            for (int i = 0; i < storyButtons.Length; i++)
            {
                storyButtons[i].SingleLine();
                if (i > 0) repaint.Track(MetRail.Attach(storyButtons[i].Rect, MetEffect.Cut));
            }
            clicks["story"] = i => ClickControl(storyButtons[i]);   // the harness clicks the story rows (0 = AUTO) through the shell's probes
        }

        private void BuildCentre()
        {
            AvFlow f = Column("CentreColumn", MetOfficeLayout.Pad + MetOfficeLayout.Left + MetOfficeLayout.ColGap, MetOfficeLayout.Centre);

            var classSpecs = new AvControl.Spec[7];
            for (int i = 0; i < 7; i++) { int cls = i; classSpecs[i] = new AvControl.Spec(MetCatalog.ClassWords[i], () => BrowseClass(cls)); }
            MetControlGrid classGrid = f.Add(new MetControlGrid(f.Content, classSpecs, 7, MetOfficeLayout.Row));
            for (int i = 0; i < 7; i++)
            {
                classButtons[i] = classGrid.Controls[i];
                classButtons[i].SingleLine();
                classButtons[i].Help = MetCatalog.ClassHelp;
            }

            gallery = f.Add(new MetSkyGallery(f.Content, SelectSky, Spec(MetControl.PinAuto, "AUTO\nSKY BACK TO THE STORY", AvIcon.Refresh)));
            Reg(MetControl.PinAuto, gallery.Auto);

            inspectCard = Card(f, "INSPECT");
            inspect = inspectCard.Add(new MetInspectBody(inspectCard.Content), 3f * MetOfficeLayout.InspectRow);
            inspect.SetHelp("INSPECT: the selected sky's three layers with base and cover, before you pin it. PIN makes the whole tape this sky.");
            pinRow = inspectCard.Add(new MetLabeledRow(inspectCard.Content, null, 0f, new[] { Spec(MetControl.PinSelected, "PIN", AvIcon.Target, AvButtonStyle.Primary) }, 150f, 0f), MetOfficeLayout.Row);
            Reg(MetControl.PinSelected, pinRow.Controls[0]);

            skyCard = Card(f, "SKY OVERHEAD");
            sky = skyCard.Add(new MetSkyBody(skyCard.Content), MetOfficeLayout.SkyCard - MetOfficeLayout.CardChrome);
            sky.SetHelp("SKY OVERHEAD: the class and cover over you, the sky's name, a METAR line from the layers above you and the three layers with their bases.");
            clicks["class"] = cls => ClickControl(classButtons[cls]);
            clicks["tile"] = slot => ClickControl(gallery.Tiles[slot]);
            probes["tiles"] = () => gallery.TilesShown;
            probes["selected"] = () => picker.Selected;
        }

        private void BuildRight()
        {
            AvFlow f = Column("RightColumn", MetOfficeLayout.Width - MetOfficeLayout.Pad - MetOfficeLayout.Right, MetOfficeLayout.Right);

            MetCard next = Card(f, "NEXT FRONTS");
            next.SetTag(0, "ETA S5", MetAvail.Motion5);
            fronts = next.Add(new MetFrontsBody(next.Content), MetOfficeLayout.FrontsCard - MetOfficeLayout.CardChrome);
            fronts.SetHelp("NEXT FRONTS: the two nearest fronts with distance and bearing. ETA reads STATIC while the tape is held; it counts down once the tape moves them (S5).");

            outlookSection = f.Section(AvIcon.Clock, "OUTLOOK 60 MIN", "AT AIRCRAFT · TAPE");
            outlook = f.Add(new EnvOutlookStrip(f.Content, MetOfficeModel.OutlookCount));
            outlook.CautionMark = MetSkyBody.CautionMark;   // the amber triangle means CUT in this window: a broken or overcast sky wears a dot
            outlook.SetHelp("OUTLOOK: the class now and at +5, +10, +15, +30 and +60 minutes at your position, with the rain bar. HELD while the tape is held.");

            f.Section(AvIcon.Bolt, "SET-PIECES", "TOGGLE · FADE 20 S");
            var setSpecs = new AvControl.Spec[MetOfficeModel.SetCount];
            for (int i = 0; i < setSpecs.Length; i++) setSpecs[i] = Spec(MetCatalog.From(MetControl.SetSquall, i), MetCatalog.SetNames[i]);
            setButtons = f.Add(new MetControlGrid(f.Content, setSpecs, 2, MetOfficeLayout.SetCellRow)).Controls;
            for (int i = 0; i < setButtons.Length; i++) Reg(MetCatalog.From(MetControl.SetSquall, i), setButtons[i]);
            anchorRow = f.Add(new AvSegmented(f.Content, "ANCHOR", new[] { "HERE", "30 KM", "DEFAULT" }, () => MetOfficeText.AnchorChoice(model),
                i => Press(MetCatalog.From(MetControl.AnchorHere, i))));
            for (int i = 0; i < 3; i++) Reg(MetCatalog.From(MetControl.AnchorHere, i), anchorRow.Options[i]);
            rainRow = f.Add(new AvSegmented(f.Content, "RAIN", MetCatalog.RainNames, () => { for (int i = 0; i < MetCatalog.RainValues.Length; i++) if (RainMatches(i)) return i; return 0; },
                i => Press(MetCatalog.From(MetControl.RainAuto, i))));
            for (int i = 0; i < 4; i++) Reg(MetCatalog.From(MetControl.RainAuto, i), rainRow.Options[i]);

            MetCard id = Card(f, "IDENTITY");
            Reg(MetControl.Reroll, id.SetHeaderControl(Spec(MetControl.Reroll, "RE-ROLL", AvIcon.Refresh), 96f));
            identityHead = id.Add(new AvNote(id.Content, AvTextRole.DataSmall, true), 16f, 0f);
            identitySub = id.Add(new AvNote(id.Content, AvTextRole.DataSmall), 16f, 0f);
            identityNext = id.Add(new AvNote(id.Content, AvTextRole.DataSmall), 16f, 0f);
            MetCard log = Card(f, "HOST LOG");
            log.SetCaption("NEWEST FIRST");
            logNote = log.Add(new AvNote(log.Content, AvTextRole.DataSmall), MetOfficeLayout.LogLines * MetOfficeLayout.LogLine);
            Tip(logNote.Rect.Find("Text").gameObject, MetCatalog.LogHelp);
            foreach (AvNote note in new[] { identityHead, identitySub, identityNext }) Tip(note.Rect.Find("Text").gameObject, MetCatalog.IdentityHelp);
        }

        private void BuildTape()
        {
            tapeCard = new MetCard(window.Root, window.Ticker, "WEATHER TAPE");
            window.Ticker.Register(tapeCard);
            story = tapeCard.Add(new MetStoryBody(tapeCard.Content), MetOfficeLayout.StoryBody);
            story.SetHelp("WEATHER TAPE: segments and front markers, ±93 minutes around you. HELD while the tape does not move; minutes count at the tape speed.");

            var row = new MetHRow(tapeCard.Content);
            holdRow = row.Add(new MetLabeledRow(row.Rect, null, 0f, new[] { Spec(MetControl.Hold, "HOLD TAPE", AvIcon.PlayerPause) }, 128f, 0f), 330f);
            Reg(MetControl.Hold, holdRow.Controls[0]);
            paceRow = row.Add(new MetStepRow(row.Rect, Spec(MetControl.PaceDown, "", AvIcon.Minus, AvButtonStyle.Quiet), Spec(MetControl.PaceUp, "", AvIcon.Plus, AvButtonStyle.Quiet))
                .WithLabel("PACE", 44f), 320f);
            Reg(MetControl.PaceDown, paceRow.Minus); Reg(MetControl.PaceUp, paceRow.Plus);
            var frontSpecs = new[] { Spec(MetControl.Front2, "2"), Spec(MetControl.Front5, "5"), Spec(MetControl.Front10, "10"), Spec(MetControl.Front30, "30") };
            frontRow = row.Add(new MetLabeledRow(row.Rect, "FRONT IN", 64f, frontSpecs, 0f, 64f), 330f);
            for (int i = 0; i < 4; i++) Reg(MetCatalog.From(MetControl.Front2, i), frontRow.Controls[i]);
            frontRow.SetTrail("MIN · S5", "caution");
            tapeNote = row.Add(new AvNote(row.Rect, AvTextRole.DataSmall), 0f);
            tapeCard.Add(row, MetOfficeLayout.Row, MetOfficeLayout.Gap);
            tapeCard.Place(new AvSlot(MetOfficeLayout.Pad, MetOfficeLayout.TapeTop, MetOfficeLayout.Width - 2f * MetOfficeLayout.Pad, MetOfficeLayout.TapeCard));
            probes["pips"] = () => story.PipsPainted;
        }

        // ---- Browsing: nothing here reaches the world ------------------------------------------------

        private void BrowseClass(int cls) { picker.SelectClass((WeatherRegimeType)cls); RefreshCentre(); }

        private void SelectSky(int skyIndex) { picker.Select(skyIndex); RefreshCentre(); }

        /// <summary>A story row: one request, refused with the stepper's reason while the stories cannot be steered.</summary>
        private void PressStory(int pick)
        {
            source.Fill(model);
            if (!MetOfficeRules.State(MetControl.StoryNext, model).Enabled)
            {
                window.Footer.SetHint(MetOfficeRules.Tip(MetControl.StoryNext, model));
                refusedUntil = Time.unscaledTime + 2.5f;
                return;
            }
            commands.SetStory(pick);
            nextRefresh = 0f;
        }

        private void Keys()
        {
            if (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl) || Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt)) return;
            for (int i = 0; i < HotKeys.Length; i++)
                if (Input.GetKeyDown(HotKeys[i])) Press(HotControls[i]);
            if (Input.GetKeyDown(KeyCode.W)) CycleRain();
        }

        // ---- Refresh -------------------------------------------------------------------------------

        private void RefreshDesk()
        {
            RefreshHero();
            RefreshLeft();
            RefreshCentre();
            RefreshRight();
            RefreshTape();
        }

        private void RefreshHero()
        {
            bool ready = model.Captured;
            int cls = (int)model.Class;
            // A client's Pace setting is its own: only the host's speed travels, so a client reads the speed alone (pace 0).
            int shownPace = model.Host ? model.Pace : 0;
            hero[0].Set(ready ? MetOfficeText.Cover(model.Cover) + " COVER" : "", ready ? MetCatalog.ClassCodes[cls] + "  " + MetCatalog.ClassWords[cls] : "NO SKY YET",
                ready ? MetOfficeText.SkyHead(model.SkyNumber, model.SkyName) : MetOfficeRules.Waiting, ready ? model.Metar : "",
                ready ? WeatherEnvView.RegimeState(model.Class) : AvState.Inert);
            hero[1].Set(!ready ? "" : model.Running ? "RUNNING" : "HELD", ready ? MetOfficeText.PaceValue(shownPace, model.SpeedMps) : "NO TAPE YET",
                ready ? MetOfficeText.WindStrip(model.WindFromDeg, model.WindMps) : "", ready ? MetOfficeText.MissionClock(model.MissionSeconds) : "",
                !ready ? AvState.Inert : model.Running ? AvState.Info : AvState.Caution);
            bool pinned = model.PinnedSky >= 0;
            hero[2].Set(!ready ? "" : pinned ? "PINNED" : model.StoryPick < 0 ? "AUTO" : "PICKED", model.HasStory ? model.StoryName.ToUpperInvariant() : "NO STORY",
                model.SkyRole.Length > 0 ? "NOW · " + model.SkyRole : "", pinned ? "SKY #" + (model.PinnedSky + 1) + " PINNED · STORY SUSPENDED" : ready ? "S STEPS THE STORY · ▲ CUT" : "",
                !ready ? AvState.Inert : pinned ? AvState.Caution : AvState.Info);

            int ahead = -1;
            for (int i = 0; i < model.MarkCount; i++)
                if (!model.Marks[i].Step && model.Marks[i].AtMin > 0f && (ahead < 0 || model.Marks[i].AtMin < model.Marks[ahead].AtMin)) ahead = i;
            if (ahead < 0) hero[3].Set("", "NO FRONT AHEAD", ready ? "NOTHING ON THE TAPE WITHIN 93 MIN" : "", "", AvState.Inert);
            else
            {
                MetMark mark = model.Marks[ahead];
                bool hard = mark.Kind == MetFrontKind.Cold || mark.Kind == MetFrontKind.Squall;
                hero[3].Set(model.Running ? MetOfficeText.Eta(mark.AtMin) : "STATIC", MetOfficeText.FrontWord(mark.Kind),
                    AvNum.Fixed(mark.AtMin * 60f * model.SpeedMps / 1000f, 0) + " KM UPWIND · " + AvNum.Fixed(mark.AtMin, 0) + " MIN OF TAPE",
                    model.Running ? "FRONT IN N ARRIVES WITH TAPE MOTION (S5)" : "TAPE HELD · THE FRONT STANDS STILL", hard ? AvState.Caution : AvState.Info);
            }
        }

        private void RefreshLeft()
        {
            MetState steer = MetOfficeRules.State(MetControl.StoryNext, model);
            string tip = MetOfficeRules.Tip(MetControl.StoryNext, model);
            for (int i = 0; i < storyButtons.Length; i++)
            {
                storyButtons[i].Latched = model.StoryPick == i - 1;
                if (i == 0) continue;   // AUTO is a registered control: the shell sets its state
                storyButtons[i].Interactable = steer.Enabled;
                storyButtons[i].Help = tip;
            }
            for (int i = 0; i < scenarioButtons.Length; i++) scenarioButtons[i].Latched = false;
            logNote.Set(model.Host ? (model.LogText.Length > 0 ? model.LogText : "NO HOST ACTIONS YET\nEVERY CHANGE YOU MAKE IS LOGGED HERE") : "HOST ACTIONS ARE LOGGED ON THE HOST\nTHIS SEAT ONLY READS: THE HOST STEERS");
        }

        private void RefreshCentre()
        {
            int selected = picker.Selected;
            SkyDef d = SkyLibrary.Get(selected);
            for (int i = 0; i < classButtons.Length; i++) classButtons[i].Latched = (int)picker.Class == i;
            gallery.Show(picker.Class, selected, model.PinnedSky, model.Captured ? model.SkyNumber - 1 : -1);
            inspect.Set(selected);
            inspectCard.SetCaption("#" + d.Number + " " + d.Name.ToUpperInvariant() + " · " + MetSkyFacts.Code(d));
            pinRow.SetTrail(model.PinnedSky == selected ? "PINNED · AUTO HANDS THE SKY BACK" : "▲ CUT · THE WHOLE TAPE BECOMES THIS SKY", model.PinnedSky == selected ? "info" : "caution");
            if (control[(int)MetControl.PinSelected] != null)
            {
                control[(int)MetControl.PinSelected].Latched = model.PinnedSky == selected;
                Label(control[(int)MetControl.PinSelected], MetOfficeText.PinLabel(selected + 1));
                control[(int)MetControl.PinAuto].Latched = model.PinnedSky < 0;
            }
            sky.Set(model);
            skyCard.SetRail(sky.RailState);
        }

        private void RefreshRight()
        {
            fronts.Set(model);
            outlookSection.SetCaption(model.OutlookHeld ? "HELD · NO FORECAST" : "AT AIRCRAFT · TAPE");
            for (int i = 0; i < MetOfficeModel.OutlookCount; i++)
            {
                MetOutlookCell cell = model.Outlook[i];
                // Before the world is captured there is nothing to forecast: the time labels stay, the cells go dim and empty (never six clear skies).
                if (!model.HasOutlook && !model.OutlookHeld)
                {
                    outlook.Set(i, MetOfficeText.OutlookLabel(cell.Minutes > 0 ? cell.Minutes : WeatherForecast.DefaultOffsetsMinutes[i]), i == 0, model.Class, "--", 0f, AvState.Inert, false);
                    continue;
                }
                if (model.OutlookHeld) { outlook.Set(i, MetOfficeText.OutlookLabel(cell.Minutes), i == 0, model.Class, "HELD", 0f, AvState.Inert); continue; }
                AvState st = cell.Class == WeatherRegimeType.Storm ? AvState.Danger : cell.Rain > 0.2f ? AvState.Caution : WeatherEnvView.RegimeState(cell.Class);
                outlook.Set(i, MetOfficeText.OutlookLabel(cell.Minutes), i == 0, cell.Class, cell.Code, cell.Rain, st);
            }
            for (int i = 0; i < MetOfficeModel.SetCount; i++)
            {
                MetSetPiece sp = model.Sets[i];
                // A placed formation says where it stands and how strong it is. A forced fog bank is Present but has no site: it keeps its "where" line (LOW LAYER · 300 M).
                string strength = sp.FadeProgress > 0f && sp.FadeProgress < 1f ? "FADE " + MetOfficeText.Cover(sp.FadeProgress) : MetOfficeText.Cover(sp.Strength);
                string sub = sp.Present && sp.Placed ? MetOfficeText.SetPieceWhere(sp.X, sp.Z, model.CamX, model.CamZ) + " · " + strength : MetCatalog.SetWhere[i];
                Label(setButtons[i], MetCatalog.SetNames[i] + "  " + (sp.Forced ? "ON" : "OFF") + "\n" + (sp.Placed && !sp.Forced ? "AUTO · " : "") + sub);
                setButtons[i].Latched = sp.Forced;
            }
            anchorRow.Refresh(); rainRow.Refresh();
            // A client's own SeedMode setting says nothing about the host's mission: only the seed travels in the key.
            identityHead.Set(model.SeedHex.Length > 0 ? MetOfficeText.IdentityHead(model.Host ? model.SeedMode : "", model.SeedHex) : "NO TAPE YET");
            identitySub.Set(model.SeedHex.Length > 0 ? MetOfficeText.IdentitySub(model.LayoutSalt, model.TapeHex) : "SEED + LAYOUT SHOW ONCE CAPTURED");
            identityNext.Set(model.SeedHex.Length > 0 ? MetOfficeText.RerollLine(model.LayoutSalt) : "");
        }

        private void RefreshTape()
        {
            story.Set(model);
            tapeCard.SetCaption(model.SkyRole.Length > 0 ? "NOW · " + model.SkyRole : "±93 MIN AROUND YOU");
            control[(int)MetControl.Hold].Latched = !model.Running;
            Label(control[(int)MetControl.Hold], MetOfficeText.HoldLabel(model.Running));
            int shownPace = model.Host ? model.Pace : 0;
            holdRow.SetTrail(MetOfficeText.TapeLine(model.Running, shownPace, model.SpeedMps), model.Running ? "info" : "caution");
            paceRow.SetValue(MetOfficeText.PaceRow(shownPace, model.SpeedMps, model.WindMps));
            int line = model.LogText.IndexOf('\n');
            tapeNote.Set(model.Pending.Length > 0 ? model.Pending : model.LogText.Length > 0 ? "LAST · " + (line < 0 ? model.LogText : model.LogText.Substring(0, line)) : "");
        }
    }
}
