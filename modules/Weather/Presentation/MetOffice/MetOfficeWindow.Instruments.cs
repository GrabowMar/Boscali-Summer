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
        private readonly AvControl[] classButtons = new AvControl[7];
        private AvControl[] setButtons;
        private AvSegmented anchorRow, rainRow;
        private MetSkyBody sky;
        private MetCard skyCard;
        private MetStepRow pinRow;
        private EnvOutlookStrip outlook;
        private AvSection outlookSection;

        partial void BuildInstrumentColumn()
        {
            AvFlow f = Column("InstrumentColumn", MetOfficeLayout.Pad + MetOfficeLayout.Col1 + MetOfficeLayout.ColGap, MetOfficeLayout.Col23);

            skyCard = Card(f, "SKY NOW");
            skyCard.SetTag(0, "R1b 4B", MetAvail.Tape4B); skyCard.SetTag(1, "R1", MetAvail.Live);
            sky = skyCard.Add(new MetSkyBody(skyCard.Content), MetOfficeLayout.SkyCard - MetOfficeLayout.CardChrome);
            sky.SetHelp("SKY NOW: the class and cover over you, the sky's name, a METAR line from the layers above you and the three layers with their bases.");

            MetCard pin = Card(f, "PIN SKY");
            pin.SetCaption("▲ CUT · WHOLE TAPE = ONE SKY"); pin.SetTag(0, "LIVE", MetAvail.Live);
            var classSpecs = new AvControl.Spec[7];
            for (int i = 0; i < 7; i++) { int cls = i; classSpecs[i] = new AvControl.Spec(MetCatalog.ClassCodes[i], () => PressClass(cls)); }
            MetControlGrid classGrid = pin.Add(new MetControlGrid(pin.Content, classSpecs, 7, MetOfficeLayout.Row), MetOfficeLayout.Row);
            for (int i = 0; i < 7; i++)
            {
                classButtons[i] = classGrid.Controls[i];
                classButtons[i].SingleLine();
                repaint.Track(MetRail.Attach(classButtons[i].Rect, MetEffect.Cut));
            }
            pinRow = pin.Add(new MetStepRow(pin.Content, Spec(MetControl.PinPrev, "", AvIcon.ChevronLeft, AvButtonStyle.Quiet), Spec(MetControl.PinNext, "", AvIcon.ChevronRight, AvButtonStyle.Quiet))
                .WithLeft(Spec(MetControl.PinAuto, "AUTO"), 54f)
                .WithExtra(Spec(MetControl.PinSelected, "PIN", AvIcon.Target, AvButtonStyle.Primary), 96f), MetOfficeLayout.Row, 4f);
            Reg(MetControl.PinPrev, pinRow.Minus); Reg(MetControl.PinNext, pinRow.Plus); Reg(MetControl.PinAuto, pinRow.Left); Reg(MetControl.PinSelected, pinRow.Extra);

            outlookSection = f.Section(AvIcon.Clock, "OUTLOOK 60 MIN", "AT AIRCRAFT · TAPE");
            outlook = f.Add(new EnvOutlookStrip(f.Content, MetOfficeModel.OutlookCount));   // the offsets come from the model (MetOutlookCell.Minutes), never from WeatherForecast
            outlook.CautionMark = MetSkyBody.CautionMark;   // the amber triangle means CUT in this window: a broken or overcast sky wears a dot
            outlook.SetHelp("OUTLOOK: the class now and at +5, +10, +15, +30 and +60 minutes at your position, with the rain bar. HELD while the tape is held.");

            f.Section(AvIcon.Bolt, "SET-PIECES", "TOGGLE · FADE 20 S");
            var setSpecs = new AvControl.Spec[MetOfficeModel.SetCount];
            for (int i = 0; i < setSpecs.Length; i++) setSpecs[i] = Spec(MetCatalog.From(MetControl.SetSquall, i), MetCatalog.SetNames[i]);
            MetControlGrid setGrid = f.Add(new MetControlGrid(f.Content, setSpecs, 2, MetOfficeLayout.SetCellRow));
            setButtons = setGrid.Controls;
            for (int i = 0; i < setButtons.Length; i++) Reg(MetCatalog.From(MetControl.SetSquall, i), setButtons[i]);
            anchorRow = f.Add(new AvSegmented(f.Content, "ANCHOR", new[] { "HERE", "30 KM", "DEFAULT" }, () => MetOfficeText.AnchorChoice(model),
                i => Press(MetCatalog.From(MetControl.AnchorHere, i))));
            for (int i = 0; i < 3; i++) Reg(MetCatalog.From(MetControl.AnchorHere, i), anchorRow.Options[i]);
            rainRow = f.Add(new AvSegmented(f.Content, "RAIN", MetCatalog.RainNames, () => { for (int i = 0; i < MetCatalog.RainValues.Length; i++) if (RainMatches(i)) return i; return 0; },
                i => Press(MetCatalog.From(MetControl.RainAuto, i))));
            for (int i = 0; i < 4; i++) Reg(MetCatalog.From(MetControl.RainAuto, i), rainRow.Options[i]);
            clicks["class"] = cls => ClickControl(classButtons[cls]);   // the harness clicks the seven class chips through the shell's probes
        }

        partial void RefreshInstrumentColumn()
        {
            sky.Set(model);
            skyCard.SetRail(sky.RailState);
            RefreshOutlook();
            for (int i = 0; i < classButtons.Length; i++)
            {
                classButtons[i].Interactable = MetOfficeRules.State(MetControl.PinClass, model).Enabled;
                classButtons[i].Help = MetOfficeRules.Tip(MetControl.PinClass, model);
                classButtons[i].Latched = model.PinnedSky >= 0 && (int)picker.Class == i;
            }
            control[(int)MetControl.PinAuto].Latched = model.PinnedSky < 0;
            control[(int)MetControl.PinSelected].Latched = model.PinnedSky == picker.Selected;
            Label(control[(int)MetControl.PinSelected], MetOfficeText.PinLabel(picker.Selected + 1));
            pinRow.SetValue(MetOfficeText.StepperValue(picker.Selected + 1, MetCatalog.ClassCodes[(int)picker.Class], picker.IndexInClass, picker.CountInClass));
            for (int i = 0; i < MetOfficeModel.SetCount; i++)
            {
                MetSetPiece sp = model.Sets[i];
                // A placed formation says where it stands and how strong it is (or how far its 20 s fade has got, from slice 5). A forced fog bank is Present but has no site:
                // it keeps the "where" line of a piece nothing placed (LOW LAYER · 300 M) instead of a distance of 0 km and a strength of 0%.
                string strength = sp.FadeProgress > 0f && sp.FadeProgress < 1f ? "FADE " + MetOfficeText.Cover(sp.FadeProgress) : MetOfficeText.Cover(sp.Strength);
                string sub = sp.Present && sp.Placed ? MetOfficeText.SetPieceWhere(sp.X, sp.Z, model.CamX, model.CamZ) + " · " + strength
                    : MetCatalog.SetWhere[i];
                Label(setButtons[i], MetCatalog.SetNames[i] + "  " + (sp.Forced ? "ON" : "OFF") + "\n" + (sp.Placed && !sp.Forced ? "AUTO · " : "") + sub);
                setButtons[i].Latched = sp.Forced;
            }
            anchorRow.Refresh(); rainRow.Refresh();
        }

        private void RefreshOutlook()
        {
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
        }
    }
}
