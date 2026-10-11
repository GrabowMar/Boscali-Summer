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
        private MetSynopticChart chart;
        private MetFrontsBody fronts;
        private AvNote identityHead, identitySub, identityNext;

        partial void BuildChartColumn()
        {
            AvFlow f = Column("ChartColumn", MetOfficeLayout.Pad, MetOfficeLayout.Col1);
            f.Section(AvIcon.Map2, "SYNOPTIC CHART", "±150 KM");
            chart = f.Add(new MetSynopticChart(f.Content));
            chart.SetHelp("SYNOPTIC CHART: north up, ±150 km around the theater centre. Fronts, storm cells, set-pieces, the anchor, you and the wind.");

            MetCard next = Card(f, "NEXT FRONTS");
            next.SetTag(0, "4B", MetAvail.Tape4B); next.SetTag(1, "R4", MetAvail.Live);
            fronts = next.Add(new MetFrontsBody(next.Content), MetOfficeLayout.FrontsCard - MetOfficeLayout.CardChrome);
            fronts.SetHelp("NEXT FRONTS: the two nearest fronts with distance and bearing. ETA reads STATIC until the tape moves them (4B / S5).");

            MetCard id = Card(f, "IDENTITY");
            id.SetTag(0, "R8", MetAvail.Live);
            Reg(MetControl.Reroll, id.SetHeaderControl(Spec(MetControl.Reroll, "RE-ROLL", AvIcon.Refresh), 96f));
            identityHead = id.Add(new AvNote(id.Content, AvTextRole.DataSmall, true), 16f, 0f);
            identitySub = id.Add(new AvNote(id.Content, AvTextRole.DataSmall), 16f, 0f);
            identityNext = id.Add(new AvNote(id.Content, AvTextRole.DataSmall), 16f, 0f);   // the mockup's "▲ 7→8" on the button, in words: CUT never travels as colour alone
            foreach (AvNote note in new[] { identityHead, identitySub, identityNext }) Tip(note.Rect.Find("Text").gameObject, MetCatalog.IdentityHelp);
        }

        partial void RefreshChartColumn()
        {
            chart.Set(model);
            fronts.Set(model);
            // A client's own SeedMode setting says nothing about the host's mission: only the seed travels in the key.
            identityHead.Set(model.SeedHex.Length > 0 ? MetOfficeText.IdentityHead(model.Host ? model.SeedMode : "—", model.SeedHex) : "NO TAPE YET");
            identitySub.Set(model.SeedHex.Length > 0 ? MetOfficeText.IdentitySub(model.LayoutSalt, model.TapeHex) : "");
            identityNext.Set(model.SeedHex.Length > 0 ? MetOfficeText.RerollLine(model.LayoutSalt) : "");
        }
    }
}
