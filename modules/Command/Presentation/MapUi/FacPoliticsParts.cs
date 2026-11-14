using System.Collections.Generic;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Core.Util;
using BoscaliSummer.Modules.Command.Domain;
using NOAvionics;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Modules.Command.Presentation.MapUi
{
    /// <summary>
    /// FAC POLITICS while the nation layer runs: WAR SUPPORT (the track with its 25 and 75 marks, and why it moved), DOCTRINE (the emblem
    /// and the rank track that runs itself), DECISIONS (cost in war support, the first press wins) and the cabinet MINUTES with the other
    /// cabinet. Every number is the host's; a press is a request. The parts join the page's nation list so the page hides them when off.
    /// </summary>
    internal sealed class FacPolitics
    {
        private const int MoveRows = 4;
        private readonly SupportTrackPart track;
        private readonly AvRow[] moves = new AvRow[MoveRows];
        private readonly DoctrineCardPart doctrine;
        private readonly AvSection decisionSection;
        private readonly AvRow[] decisions = new AvRow[Decisions.PerSide];
        private readonly AvControl[] buttons = new AvControl[Decisions.PerSide];
        private readonly int[] ids = new int[Decisions.PerSide];
        private readonly AvNote minutes;
        private INationView nation;
        private int shown;

        public FacPolitics(AvFlow page, List<AvPart> parts)
        {
            parts.Add(page.Section(AvIcon.Flag, "WAR SUPPORT", "THE COUNTRY BEHIND YOUR SORTIES · WHY IT MOVED"));
            track = page.Add(new SupportTrackPart(page.Content));
            parts.Add(track);
            // The rows show and hide with their content (Set), so they stay off the page's on/off list; they follow the track off instead.
            for (int i = 0; i < moves.Length; i++)
            {
                moves[i] = page.Add(new AvRow(page.Content));
                moves[i].Help = "Why war support moved: captures and losses of airbases, kills, decisions and the cabinet's answers.";
            }
            parts.Add(page.Section(AvIcon.Shield, "DOCTRINE", "CHOSEN ONCE · RANKS COME BY THEMSELVES"));
            doctrine = page.Add(new DoctrineCardPart(page.Content));
            parts.Add(doctrine);
            decisionSection = page.Section(AvIcon.ListDetails, "DECISIONS", "COST IN WAR SUPPORT · FIRST PRESS WINS");
            parts.Add(decisionSection);
            for (int i = 0; i < decisions.Length; i++)
            {
                int slot = i;
                decisions[i] = page.Add(new AvRow(page.Content));
                buttons[i] = decisions[i].AddTrailing(new AvControl.Spec("DECIDE", () => Press(slot), AvButtonStyle.Primary));
            }
            parts.Add(page.Section(AvIcon.Bookmark, "MINUTES", "THE CABINET ROOM"));
            minutes = page.Add(new AvNote(page.Content, AvTextRole.ProseSmall));
            parts.Add(minutes);
            page.Ticker.Add(-1, AvTickRate.Slow, Follow);
            Follow();
        }

        /// <summary>The page hid the politics (the layer is off, or OPPOSITION is shown): the rows go with it.</summary>
        private void Follow()
        {
            if (track.Shown) return;
            foreach (AvRow row in moves) row.SetShown(false);
            foreach (AvRow row in decisions) row.SetShown(false);
        }

        private void Press(int slot)
        {
            if (nation != null && slot < shown) nation.Request(NationVerb.Decide, ids[slot]);
        }

        public void Set(INationView source)
        {
            nation = source;
            NationView v = source?.Local;
            if (v == null) return;
            track.Set(v.WarSupport);
            for (int i = 0; i < moves.Length; i++)
            {
                bool on = i < v.Moves.Count || i == 0;
                moves[i].SetShown(on);
                if (!on) continue;
                if (i >= v.Moves.Count) { moves[i].Set("NOTHING YET", "SORTIES, CAPTURES AND KILLS EARN IT; LOSSES DRAIN IT", "", AvState.Inert); continue; }
                SupportMove m = v.Moves[i];
                moves[i].Set(m.Text, PoliticsText.Ago(m.Age), PoliticsText.Signed(m.Delta), m.Delta > 0f ? AvState.Ready : AvState.Danger);
            }
            doctrine.Set(v);
            shown = Decisions.Shown(v.Doctrine, ids);
            for (int i = 0; i < decisions.Length; i++)
            {
                bool on = i < shown;
                decisions[i].SetShown(on);
                if (!on) continue;
                int id = ids[i];
                string word = PoliticsText.Button(v, id, out bool open);
                decisions[i].Set(Decisions.Name(id), Decisions.Effect(id), Decisions.Cost(id).ToString(System.Globalization.CultureInfo.InvariantCulture),
                    v.InForce[id] > 0f ? AvState.Caution : open ? AvState.Ready : AvState.Inert);
                buttons[i].Label = word;
                buttons[i].Interactable = open;
            }
            bool fresh = source.LastOutcome != NationOutcome.Ok && Time.unscaledTime - source.LastOutcomeAt < 4f;
            decisionSection.SetCaption(fresh ? NationWords.Outcome(source.LastOutcome)
                : v.Doctrine == Doctrine.None ? "TWO OPEN · THE DOCTRINE ADDS THREE" : "COST IN WAR SUPPORT · FIRST PRESS WINS");
            minutes.Set(PoliticsText.Minutes(v));
        }
    }

    /// <summary>The war support track: the fill, the 25 and 75 marks with what they do, and the number.</summary>
    internal sealed class SupportTrackPart : AvPart
    {
        private const float H = 34f, BarH = 12f, ValueW = 52f;
        private readonly Image back, fill, low, high;
        private readonly TMP_Text lowWord, highWord, value;
        private float shown = -1f, barW;

        public SupportTrackPart(RectTransform parent)
        {
            Rect = AvLay.Child(parent, "SupportTrack");
            back = AvLay.Solid(Rect, "Back", AvTheme.SurfaceInert);
            fill = AvLay.Solid(Rect, "Fill", AvTheme.RailDanger);
            low = AvLay.Solid(Rect, "Mark25", AvInk.Ink);
            high = AvLay.Solid(Rect, "Mark75", AvInk.Ink);
            lowWord = AvText.Make(Rect, "Word25", AvTextRole.Micro, "UNDER 25: NO BOLD, -2 JETS", TextAlignmentOptions.TopLeft);
            highWord = AvText.Make(Rect, "Word75", AvTextRole.Micro, "OVER 75: +2 JETS", TextAlignmentOptions.TopLeft);
            value = AvText.Make(Rect, "Value", AvTextRole.DataStrong, "—", TextAlignmentOptions.MidlineRight);
            AvText.Fit(lowWord, false); AvText.Fit(highWord, false); AvText.Fit(value, false);
            AvHelpTip.Attach(back.gameObject, "War support 0-100. Under 25 the staff may not go BOLD and the AI fields two fewer jets; over 75 two more. The staff spends what is above 60.");
            Restyle();
        }

        public override float Measure(float width) => H;

        public override void Place(AvSlot s)
        {
            base.Place(s);
            barW = Mathf.Max(40f, s.W - ValueW - 8f);
            AvLay.Place(back.rectTransform, 0f, 3f, barW, BarH);
            AvLay.Place(low.rectTransform, barW * 0.25f - 1f, 0f, 2f, BarH + 6f);
            AvLay.Place(high.rectTransform, barW * 0.75f - 1f, 0f, 2f, BarH + 6f);
            // Each word starts at its own mark and stops short of the next one, so the two never meet.
            AvLay.Place(lowWord, barW * 0.25f + 4f, BarH + 7f, barW * 0.5f - 10f, 12f);
            AvLay.Place(highWord, barW * 0.75f + 4f, BarH + 7f, s.W - barW * 0.75f - 4f, 12f);
            AvLay.Place(value, s.W - ValueW, 0f, ValueW, BarH + 8f);
            PlaceFill();
        }

        private void PlaceFill() => AvLay.Place(fill.rectTransform, 0f, 3f, barW * Mathf.Clamp01(shown / 100f), BarH);

        public void Set(float warSupport)
        {
            if (Mathf.Abs(warSupport - shown) < 0.05f) return;
            shown = warSupport;
            AvText.Set(value, PoliticsText.Round(warSupport).ToString(System.Globalization.CultureInfo.InvariantCulture));
            fill.color = warSupport < 25f ? AvTheme.RailDanger : warSupport > 75f ? AvTheme.RailReady : AvTheme.RailCaution;
            PlaceFill();
        }

        public override void Restyle()
        {
            back.color = AvTheme.SurfaceInert;
            low.color = high.color = AvInk.Ink;
            lowWord.color = highWord.color = AvInk.Dim;
            value.color = AvInk.Ink;
        }
    }

    /// <summary>The doctrine card: emblem, name, who chose it and when, and the three ranks with the decision each opens.</summary>
    internal sealed class DoctrineCardPart : AvPart
    {
        private const float H = 106f, Pad = 8f, Emblem = 72f;
        private readonly AvFrame frame;
        private readonly Image emblem;
        private readonly TMP_Text title, sub;
        private readonly AvFrame[] cells = new AvFrame[3];
        private readonly TMP_Text[] ranks = new TMP_Text[3];
        private readonly bool[] reached = new bool[3];
        private Doctrine bound = (Doctrine)255;
        private AvSlot slot;
        private bool placed;

        public DoctrineCardPart(RectTransform parent)
        {
            Rect = AvLay.Child(parent, "DoctrineCard");
            frame = AvFrame.Add(Rect, "Frame", AvChamfer.Diagonal(6f));
            AvLay.Fill(frame.rectTransform);
            emblem = AvLay.Solid(Rect, "Emblem", Color.white);
            emblem.preserveAspect = true;
            title = AvText.Make(Rect, "Title", AvTextRole.Head);
            sub = AvText.Make(Rect, "Sub", AvTextRole.Micro);
            AvText.Fit(title, false); AvText.Fit(sub, false);
            for (int i = 0; i < 3; i++)
            {
                cells[i] = AvFrame.Add(Rect, "Rank" + i, default(AvChamfer));
                ranks[i] = AvText.Make(Rect, "RankText" + i, AvTextRole.Micro, "", TextAlignmentOptions.TopLeft, true);
            }
            Restyle();
        }

        public override float Measure(float width) => H;

        public override void Place(AvSlot s)
        {
            base.Place(s);
            slot = s;
            placed = true;
            bool art = emblem.enabled;
            float x = art ? Pad + Emblem + 10f : Pad;
            float w = s.W - x - Pad;
            AvLay.Place(emblem.rectTransform, Pad, (H - Emblem) * 0.5f, Emblem, Emblem);
            AvLay.Place(title, x, Pad, w, 20f);
            AvLay.Place(sub, x, Pad + 22f, w, 14f);
            float cw = (w - 8f) / 3f;
            for (int i = 0; i < 3; i++)
            {
                AvLay.Place(cells[i].rectTransform, x + i * (cw + 4f), Pad + 42f, cw, H - Pad * 2f - 42f);
                AvLay.Place(ranks[i], x + i * (cw + 4f) + 5f, Pad + 45f, cw - 10f, H - Pad * 2f - 48f);
            }
        }

        public void Set(NationView v)
        {
            bool force = v.Doctrine != bound;
            if (force)
            {
                bound = v.Doctrine;
                Sprite art = NationArt.Get(Doctrines.ArtKey(v.Doctrine));
                emblem.sprite = art;
                emblem.enabled = art != null;
                AvText.Set(title, v.Doctrine == Doctrine.None ? "NO DOCTRINE YET" : Doctrines.Name(v.Doctrine));
                if (placed) Place(slot);
            }
            AvText.Set(sub, v.Doctrine == Doctrine.None ? "THE WAR CABINET SITS AT 5:00 · ITS CARD IS ON EVN DISPATCH"
                : "CHOSEN " + PoliticsText.Ago(v.DoctrineAge) + " BY " + v.DoctrineBy + " · RANK " + v.Rank);
            bool changed = false;
            for (int i = 0; i < 3; i++)
            {
                bool on = v.Doctrine != Doctrine.None && v.Rank > i;
                if (!force && on == reached[i]) continue;
                reached[i] = on;
                changed = true;
                AvText.Set(ranks[i], v.Doctrine == Doctrine.None ? Doctrines.Name((Doctrine)(i + 1)) + " · " + Decisions.Name(Decisions.Of((Doctrine)(i + 1), 1)) + " FIRST"
                    : PoliticsText.RankLine(v.Doctrine, i + 1, on));
            }
            if (changed) Restyle();
        }

        public override void Restyle()
        {
            frame.Paint(AvTheme.Surface, AvTheme.RailCaution);
            title.color = AvTheme.RailCaution;
            sub.color = AvInk.Dim;
            for (int i = 0; i < 3; i++)
            {
                cells[i].Paint(reached[i] ? AvTheme.RailReady.WithAlpha(.16f) : AvTheme.SurfaceInert, reached[i] ? AvTheme.RailReady : AvTheme.Hairline);
                ranks[i].color = reached[i] ? AvInk.Ink : AvInk.Dim;
            }
        }
    }
}
