using BoscaliSummer.Modules.Weather.Domain;
using NOAvionics;

namespace BoscaliSummer.Modules.Weather.Presentation
{
    // The window's layout: the sky picker (class tabs over the sky gallery) on the left, the sky overhead (with RE-ROLL) and the wind and fronts on the right,
    // and the weather along the wind across the bottom.
    internal sealed partial class MetOfficeWindow
    {
        private readonly AvControl[] classButtons = new AvControl[7];
        private MetSkyGallery gallery;
        private MetCard skyCard, stripCard;
        private MetSkyBody sky;
        private AvNote windNote;
        private MetFrontsBody fronts;
        private MetStripBody strip;

        private void BuildPicker()
        {
            AvFlow f = Column("Picker", MetOfficeLayout.Pad, MetOfficeLayout.Left);
            var classSpecs = new AvControl.Spec[7];
            for (int i = 0; i < 7; i++) { var cls = (WeatherRegimeType)i; classSpecs[i] = new AvControl.Spec(MetCatalog.ClassWords[i], () => picker.Browse(cls)); }
            MetControlGrid classGrid = f.Add(new MetControlGrid(f.Content, classSpecs, 7, MetOfficeLayout.Row));
            for (int i = 0; i < 7; i++)
            {
                classButtons[i] = classGrid.Controls[i];
                classButtons[i].SingleLine();
                classButtons[i].Help = MetCatalog.ClassHelp;
            }
            gallery = f.Add(new MetSkyGallery(f.Content, s => Press(MetControl.Pin, s), Spec(MetControl.Auto, "AUTO\nMISSION WEATHER", AvIcon.Refresh)));
            Reg(MetControl.Auto, gallery.Auto);
        }

        private void BuildRight()
        {
            AvFlow f = Column("Sky", MetOfficeLayout.Width - MetOfficeLayout.Pad - MetOfficeLayout.Right, MetOfficeLayout.Right);
            skyCard = Card(f, "SKY OVERHEAD");
            Reg(MetControl.Reroll, skyCard.SetHeaderControl(Spec(MetControl.Reroll, "RE-ROLL", AvIcon.Refresh), 96f));
            sky = skyCard.Add(new MetSkyBody(skyCard.Content), MetOfficeLayout.SkyCard - MetOfficeLayout.CardChrome);
            MetCard around = Card(f, "WIND & FRONTS");
            windNote = around.Add(new AvNote(around.Content, AvTextRole.DataStrong, true), MetOfficeLayout.WindLine, 0f);
            fronts = around.Add(new MetFrontsBody(around.Content), MetOfficeLayout.FrontsBody);
        }

        private void BuildStrip()
        {
            stripCard = new MetCard(window.Root, window.Ticker, "ALONG THE WIND");
            window.Ticker.Register(stripCard);
            strip = stripCard.Add(new MetStripBody(stripCard.Content), MetOfficeLayout.StripBody);
            stripCard.Place(new AvSlot(MetOfficeLayout.Pad, MetOfficeLayout.TapeTop, MetOfficeLayout.Width - 2f * MetOfficeLayout.Pad, MetOfficeLayout.TapeCard));
        }

        private void RefreshDesk()
        {
            for (int i = 0; i < classButtons.Length; i++) classButtons[i].Latched = (int)picker.Class == i;
            MetState pin = MetOfficeRules.State(MetControl.Pin, model);
            gallery.Show(picker.Class, model.PinnedSky, model.Captured ? model.SkyNumber - 1 : -1, pin.Enabled, MetOfficeRules.Tip(MetControl.Pin, model));
            control[(int)MetControl.Auto].Latched = model.Captured && model.PinnedSky < 0 && !model.Forced;
            sky.Set(model);
            skyCard.SetRail(sky.RailState);
            windNote.Set(model.Captured ? MetOfficeText.WindStrip(model.WindFromDeg, model.WindMps) : "");
            fronts.Set(model);
            strip.Set(model);
            stripCard.SetCaption(model.SkyRole.Length > 0 ? "OVERHEAD · " + model.SkyRole : "");
        }
    }
}
