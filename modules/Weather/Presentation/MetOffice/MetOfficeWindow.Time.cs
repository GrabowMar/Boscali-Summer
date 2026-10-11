using System;
using System.Collections.Generic;
using BoscaliSummer.Modules.Weather.Domain;
using NOAvionics;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Modules.Weather.Presentation
{
    internal sealed partial class MetOfficeWindow
    {
        private AvControl[] scenarioButtons;
        private MetStepRow storyRow, paceRow;
        private MetLabeledRow holdRow, frontRow;
        private MetStoryBody story;
        private AvNote logNote;
        private MetCard storyCard, logCard;

        partial void BuildTimeColumn()
        {
            float x = MetOfficeLayout.Pad + MetOfficeLayout.Col1 + MetOfficeLayout.ColGap + MetOfficeLayout.Col23 + MetOfficeLayout.ColGap;
            AvFlow f = Column("TimeColumn", x, MetOfficeLayout.Col23);

            storyCard = Card(f, "STORY");
            MetCard st = storyCard;
            st.SetCaption("▲ CUT · WHOLE TAPE"); st.SetTag(0, "4B", MetAvail.Tape4B); st.SetTag(1, "R3", MetAvail.Live);
            storyRow = st.Add(new MetStepRow(st.Content, Spec(MetControl.StoryPrev, "", AvIcon.ChevronLeft, AvButtonStyle.Quiet), Spec(MetControl.StoryNext, "", AvIcon.ChevronRight, AvButtonStyle.Quiet))
                .WithLeft(Spec(MetControl.StoryAuto, "AUTO"), 52f), MetOfficeLayout.Row);
            Reg(MetControl.StoryPrev, storyRow.Minus); Reg(MetControl.StoryNext, storyRow.Plus); Reg(MetControl.StoryAuto, storyRow.Left);
            story = st.Add(new MetStoryBody(st.Content), MetOfficeLayout.PipsRow + 2f + MetOfficeLayout.StripArt + MetOfficeLayout.StripMeta, 3f);
            story.SetHelp("STORY STRIP: where the sky changes, ±93 minutes around now. Legacy step today; the tape (4B) draws segments and front markers here.");

            MetCard time = Card(f, "TIME");
            time.SetCaption("SMOOTH · HOLD / PACE"); time.SetTag(0, "LIVE", MetAvail.Live);
            holdRow = time.Add(new MetLabeledRow(time.Content, null, 0f, new[] { Spec(MetControl.Hold, "HOLD TAPE", AvIcon.PlayerPause) }, 128f, 0f), MetOfficeLayout.Row);
            Reg(MetControl.Hold, holdRow.Controls[0]);
            paceRow = time.Add(new MetStepRow(time.Content, Spec(MetControl.PaceDown, "", AvIcon.Minus, AvButtonStyle.Quiet), Spec(MetControl.PaceUp, "", AvIcon.Plus, AvButtonStyle.Quiet))
                .WithLabel("PACE", 44f), MetOfficeLayout.Row);
            Reg(MetControl.PaceDown, paceRow.Minus); Reg(MetControl.PaceUp, paceRow.Plus);
            var frontSpecs = new[] { Spec(MetControl.Front2, "2"), Spec(MetControl.Front5, "5"), Spec(MetControl.Front10, "10"), Spec(MetControl.Front30, "30") };
            frontRow = time.Add(new MetLabeledRow(time.Content, "FRONT IN", 60f, frontSpecs, 0f, 28f), MetOfficeLayout.Row);
            for (int i = 0; i < 4; i++) Reg(MetCatalog.From(MetControl.Front2, i), frontRow.Controls[i]);
            frontRow.SetTrail("S5", "caution");
            AvNote frontNote = time.Add(new AvNote(time.Content, AvTextRole.Micro), MetOfficeLayout.StripMeta, 3f);
            frontNote.Set("▲ FRONT IN N ARRIVES WITH TAPE MOTION (S5)");

            MetCard scen = Card(f, "SCENARIOS");
            scen.SetCaption("▲ CUT · OVERWRITES STORY PIN SETS RAIN");
            var scenSpecs = new AvControl.Spec[6];
            for (int i = 0; i < 6; i++)
            {
                string later = MetCatalog.ScenarioLater[i] != null ? " · " + MetCatalog.ScenarioLater[i] + " S5" : "";
                scenSpecs[i] = Spec(MetCatalog.From(MetControl.ScenHurricane, i), MetCatalog.ScenarioNames[i] + "\n" + MetCatalog.ScenarioRecipe[i] + later,
                    AvIcon.None, i == 5 ? AvButtonStyle.Danger : AvButtonStyle.Default);
            }
            MetControlGrid scenGrid = scen.Add(new MetControlGrid(scen.Content, scenSpecs, 2, MetOfficeLayout.ScenarioRow), 3f * MetOfficeLayout.ScenarioRow + 2f * MetOfficeLayout.CellGap);
            scenarioButtons = scenGrid.Controls;
            for (int i = 0; i < 6; i++) Reg(MetCatalog.From(MetControl.ScenHurricane, i), scenarioButtons[i]);

            logCard = Card(f, "LOG · PENDING");
            MetCard log = logCard;
            log.SetCaption("IDLE"); log.SetTag(0, "R10 S5", MetAvail.Motion5);
            logNote = log.Add(new AvNote(log.Content, AvTextRole.DataSmall), MetOfficeLayout.LogLines * MetOfficeLayout.LogLine);
            Tip(logNote.Rect.Find("Text").gameObject, MetCatalog.LogHelp);   // lines are at most MetOfficeText.LogColumns wide, so four fit the 64 px box without wrapping
            probes["pips"] = () => story.PipsPainted;                         // the check counts the painted pips through the shell's probes
        }

        partial void RefreshTimeColumn()
        {
            story.Set(model);
            logNote.Set(model.LogText.Length > 0 ? model.LogText : "NO HOST ACTIONS YET");
            logCard.SetCaption(model.Pending.Length > 0 ? model.Pending : "IDLE");
            storyCard.SetCaption(model.SkyRole.Length > 0 ? "NOW · " + model.SkyRole : "▲ CUT · WHOLE TAPE");
            storyRow.SetValue(model.HasStory ? model.StoryName.ToUpperInvariant() + " " + (model.StoryIndex + 1) + " / " + model.StoryCount : "AUTO · — / " + model.StoryCount);
            control[(int)MetControl.StoryAuto].Latched = !model.HasStory;
            control[(int)MetControl.Hold].Latched = !model.Running;
            Label(control[(int)MetControl.Hold], MetOfficeText.HoldLabel(model.Running));
            // A client's Pace setting is its own: only the host's speed travels, so a client reads the speed alone (pace 0).
            int shownPace = model.Host ? model.Pace : 0;
            holdRow.SetTrail(MetOfficeText.TapeLine(model.Running, shownPace, model.SpeedMps), model.Running ? "info" : "caution");
            paceRow.SetValue(MetOfficeText.PaceRow(shownPace, model.SpeedMps, model.WindMps));
            for (int i = 0; i < scenarioButtons.Length; i++) scenarioButtons[i].Latched = false;
        }
    }
}
