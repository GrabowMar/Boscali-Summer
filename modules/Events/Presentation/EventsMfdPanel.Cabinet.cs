using System.Text;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Core.Services;
using BoscaliSummer.Core.Util;
using NOAvionics;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Modules.Events.Presentation
{
    // EVN DISPATCH's politics: FOR THE CABINET (the open card, its picture and its answers; any pilot answers, else the staff at 0:00) and
    // LATEST DECISIONS (one line per side; the full record is in the map log). Fed by CabinetDispatch and INationView; hidden while Nation is off.
    internal sealed partial class EventsMfdPanel
    {
        private const int CabinetOptions = 3;

        /// <summary>The cabinet card Nation hands over (set by the module).</summary>
        internal CabinetDispatch Cabinet;

        private AvSection cabinetSection, decisionsSection;
        private CabinetCardPart cabinetCard;
        private readonly AvRow[] cabinetOptions = new AvRow[CabinetOptions];
        private readonly AvControl[] cabinetButtons = new AvControl[CabinetOptions];
        private readonly AvRow[] latest = new AvRow[2];
        private readonly StringBuilder chips = new StringBuilder(64);
        private int boundCard = -1;

        private void BuildCabinet(AvFlow p)
        {
            cabinetSection = p.Section(AvIcon.Flag, "FOR THE CABINET", "ANY PILOT ANSWERS · ELSE THE STAFF");
            HelpOn(cabinetSection, "A cabinet card: something just happened and the country wants an answer. Any pilot of your side may choose; the first answer counts. Nobody answering, the staff takes the marked answer when the clock runs out.");
            cabinetCard = p.Add(new CabinetCardPart(p.Content));
            for (int i = 0; i < CabinetOptions; i++)
            {
                int option = i;
                cabinetOptions[i] = p.Add(new AvRow(p.Content));
                cabinetButtons[i] = cabinetOptions[i].AddTrailing(new AvControl.Spec("CHOOSE", () => Cabinet?.Choose(option), AvButtonStyle.Primary));
            }
            decisionsSection = p.Section(AvIcon.ListDetails, "LATEST DECISIONS", "ONE PER SIDE");
            for (int i = 0; i < latest.Length; i++)
            {
                latest[i] = p.Add(new AvRow(p.Content));
                latest[i].BadgeSlot = true;
                latest[i].Help = "The newest decision of this side, who took it and when. Decisions are taken on FAC POLITICS.";
            }
            ShowCabinet(false);
            decisionsSection.SetShown(false);
            foreach (AvRow row in latest) row.SetShown(false);
        }

        private void ShowCabinet(bool on)
        {
            cabinetSection.SetShown(on);
            cabinetCard.SetShown(on);
            if (!on) foreach (AvRow row in cabinetOptions) row.SetShown(false);
        }

        /// <summary>Binds the card and the decisions; returns the footer's words while a card waits ("" otherwise).</summary>
        private string RefreshCabinet()
        {
            if (cabinetSection == null) return "";
            NationView v = ModuleServices.TryGet(out INationView nation) && nation.Active ? nation.Local : null;
            CabinetCard card = Cabinet != null && v != null ? Cabinet.Card : null;
            ShowCabinet(card != null);
            string footer = "";
            if (card != null)
            {
                if (card.Serial != boundCard)
                {
                    boundCard = card.Serial;
                    cabinetCard.Bind(card);
                }
                cabinetCard.SetClock(card);
                bool waiting = card.Chosen < 0;
                for (int i = 0; i < cabinetOptions.Length; i++)
                {
                    bool on = i < card.Options.Count;
                    cabinetOptions[i].SetShown(on);
                    if (!on) continue;
                    CabinetOption o = card.Options[i];
                    chips.Clear();
                    for (int c = 0; c < o.Chips.Count; c++) chips.Append(c > 0 ? " · " : "").Append(o.Chips[c].Text);
                    if (waiting && i == card.StaffChoice) chips.Append(" · THE STAFF PICKS THIS AT 0:00");
                    string value = !waiting && i == card.Chosen ? "CHOSEN" : waiting && i == card.StaffChoice ? "STAFF" : "";
                    cabinetOptions[i].Set(o.Label, chips.ToString(), value,
                        !waiting ? (i == card.Chosen ? AvState.Ready : AvState.Inert) : i == card.StaffChoice ? AvState.Caution : AvState.Info);
                    cabinetButtons[i].Label = waiting ? "CHOOSE" : i == card.Chosen ? "BY " + card.ChosenBy : "—";
                    cabinetButtons[i].Interactable = waiting;
                }
                cabinetSection.SetCaption(waiting ? "ANY PILOT ANSWERS · ELSE THE STAFF AT 0:00" : "ANSWERED BY " + card.ChosenBy);
                if (waiting) footer = "1 TO ANSWER · " + AvNum.Clock(Mathf.Ceil(card.SecondsLeft)) + " · NO ANSWER: THE STAFF PICKS";
            }
            else boundCard = -1;

            bool decided = v != null && (v.Decided >= 0 || v.EnemyDecided >= 0);
            decisionsSection.SetShown(decided);
            BindDecision(latest[0], decided && v.Decided >= 0, v, false);
            BindDecision(latest[1], decided && v.EnemyDecided >= 0, v, true);
            return footer;
        }

        private static void BindDecision(AvRow row, bool on, NationView v, bool enemy)
        {
            row.SetShown(on);
            if (!on) return;
            Doctrine doctrine = enemy ? v.EnemyDoctrine : v.Doctrine;
            Sprite emblem = NationArt.Get(Doctrines.ArtKey(doctrine));
            row.SetBadge(emblem != null ? emblem.texture : null);
            int id = enemy ? v.EnemyDecided : v.Decided;
            string who = enemy ? v.EnemyName : "WE";
            float age = enemy ? v.EnemyDecidedAge : v.DecidedAge;
            row.Set(who + " DECIDE" + (enemy ? "S " : " ") + Decisions.Name(id), Decisions.Effect(id) + (enemy ? "" : " · " + v.DecidedBy),
                age < 60f ? "NOW" : Mathf.FloorToInt(age / 60f) + " MIN", enemy ? AvState.Danger : AvState.Ready);
        }

        /// <summary>The cabinet card: the picture with its kicker and clock, the title and the text. The answers are rows under it.</summary>
        private sealed class CabinetCardPart : AvPart
        {
            private const float Plate = 150f, Pad = 10f;
            private readonly AvFrame frame;
            private readonly EventPlateArt plate;
            private readonly Image kickBack, clockBack, emblem;
            private readonly TMP_Text kick, clock, title, text;

            public CabinetCardPart(RectTransform parent)
            {
                Rect = AvLay.Child(parent, "CabinetCard");
                frame = AvFrame.Add(Rect, "Frame", AvChamfer.Diagonal(8f));
                AvLay.Fill(frame.rectTransform);
                plate = new EventPlateArt(Rect, 34f);
                kickBack = AvLay.Solid(Rect, "KickBack", Color.clear);
                kick = AvText.Make(Rect, "Kicker", AvTextRole.Micro, "", TextAlignmentOptions.MidlineLeft);
                clockBack = AvLay.Solid(Rect, "ClockBack", Color.clear);
                clock = AvText.Make(Rect, "Clock", AvTextRole.DataSmall, "", TextAlignmentOptions.Center);
                emblem = AvLay.Solid(Rect, "Emblem", Color.white);
                emblem.preserveAspect = true;
                emblem.enabled = false;
                title = AvText.Make(Rect, "Title", AvTextRole.Title, "", TextAlignmentOptions.TopLeft, true);
                text = AvText.Make(Rect, "Text", AvTextRole.ProseSmall, "", TextAlignmentOptions.TopLeft, true);
                AvText.Fit(kick, false); AvText.Fit(clock, false);
                Restyle();
            }

            private float TextW(float width) => Mathf.Max(40f, width - 2f * Pad);

            public override float Measure(float width) =>
                Plate + 8f + AvText.Height(title, TextW(width)) + 4f + AvText.Height(text, TextW(width)) + Pad;

            public override void Place(AvSlot s)
            {
                base.Place(s);
                float w = TextW(s.W);
                AvLay.Place(plate.Root, 0f, 0f, s.W, Plate);
                plate.Layout(s.W, Plate);
                float kw = Mathf.Min(s.W * 0.6f, 12f + kick.preferredWidth);
                AvLay.Place(kickBack.rectTransform, 8f, 8f, kw, 18f);
                AvLay.Place(kick, 14f, 8f, kw - 10f, 18f);
                AvLay.Place(clockBack.rectTransform, s.W - 62f, 8f, 54f, 20f);
                AvLay.Place(clock, s.W - 62f, 8f, 54f, 20f);
                AvLay.Place(emblem.rectTransform, s.W - 70f, Plate - 70f, 62f, 62f);
                float th = AvText.Height(title, w);
                AvLay.Place(title, Pad, Plate + 8f, w, th);
                AvLay.Place(text, Pad, Plate + 12f + th, w, AvText.Height(text, w));
            }

            public void Bind(CabinetCard card)
            {
                plate.Bind(EventArtCache.Get(card.ArtKey, "tier_medium"), AvIcon.Flag, AvTheme.RailCaution);
                AvText.Set(kick, card.Kicker);
                AvText.Set(title, card.Title);
                AvText.Set(text, card.Text);
                Changed();
            }

            public void SetClock(CabinetCard card)
            {
                AvText.Set(clock, card.Chosen >= 0 ? "DONE" : AvNum.Clock(Mathf.Ceil(card.SecondsLeft)));
                Sprite art = string.IsNullOrEmpty(card.EmblemKey) ? null : NationArt.Get(card.EmblemKey);
                if ((art != null) != emblem.enabled || art != emblem.sprite) { emblem.sprite = art; emblem.enabled = art != null; }
            }

            public override void Restyle()
            {
                frame.Paint(AvTheme.Surface, AvTheme.RailCaution);
                plate.Restyle();
                kickBack.color = SlabBack(AvState.Caution);
                kick.color = SlabInk();
                clockBack.color = AvStyleHost.FuiColor("ground", AvTheme.Ground).WithAlpha(0.85f);
                clock.color = AvTheme.RailCaution;
                title.color = AvTheme.RailCaution;
                text.color = AvInk.Dim;
            }
        }
    }
}
