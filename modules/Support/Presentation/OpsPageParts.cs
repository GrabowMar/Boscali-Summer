using System;
using BoscaliSummer.Modules.Support.Domain.Fronts;
using BoscaliSummer.Modules.Support.Presentation.Fronts;
using BoscaliSummer.Modules.Support.Runtime;
using NOAvionics;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Modules.Support.Presentation
{
    /// <summary>
    /// One front on the OPS page: icon and name, readiness pips, the OPEN button, the superiority bar with its word, perk count, posture and
    /// funding, the programme being built with its bar, the queue behind it and one alert line. Fixed height, absolute layout, no policy.
    /// </summary>
    internal sealed class FrontCardPart : AvPart
    {
        public const float Height = 206f;
        private const float LabelW = 92f;

        private readonly FrontSkin skin = new FrontSkin();
        private readonly AvFrame back;
        private readonly Image rail;
        private readonly TMP_Text icon, name, readinessLabel, readiness, supLabel, supWord, supValue, perkLabel, perkValue, dirLabel, dirValue, fundValue, buildLabel, buildName, buildNote, queueLine, alertLine;
        private readonly Image supWell, supFill, supNeedle;
        private readonly Image[] pips = new Image[FrontRules.MaxReadiness];
        private readonly FrontBar buildBar;
        private readonly AvControl open;
        private float width = 458f;
        private FrontCardView shown;
        private bool painted;

        public FrontCardPart(RectTransform parent, Front front, Action<Front> onOpen)
        {
            Rect = AvLay.Child(parent, "Front " + front);
            back = FrontKit.Panel(Rect, "Back", 0f, 0f, width, Height, 6f, 7f);
            rail = FrontKit.Solid(Rect, "Rail", 0f, 0f, 3f, Height, Color.clear);
            icon = AvIcons.Make(Rect, front == Front.Space ? AvIcon.Satellite : front == Front.Cyber ? AvIcon.Antenna : AvIcon.UsersGroup, 16f, AvInk.Ink);
            name = FrontKit.Cond(Rect, "Name", 34f, 8f, 150f, 26f, 13f, TextAlignmentOptions.MidlineLeft);
            name.characterSpacing = 2f;
            name.text = FrontRules.Name(front) + " · " + FrontWords.Callsign(front);
            readinessLabel = FrontKit.Mono(Rect, "ReadinessLabel", 190f, 8f, 76f, 26f, 10f, TextAlignmentOptions.MidlineLeft, false, 1f);
            readinessLabel.text = "READINESS";
            readiness = FrontKit.Mono(Rect, "Readiness", 348f, 8f, 26f, 26f, 12f, TextAlignmentOptions.MidlineLeft, true);
            for (int i = 0; i < pips.Length; i++) pips[i] = FrontKit.Solid(Rect, "Pip" + i, 0f, 0f, 13f, 12f, Color.clear);
            open = AvControl.Make(Rect, new AvControl.Spec("OPEN", () => onOpen?.Invoke(front), AvButtonStyle.Primary));
            open.SingleLine();
            open.Help = "Open the " + FrontRules.Name(front) + " front window: map, rail, programmes and perks.";

            supLabel = FrontKit.Mono(Rect, "SupLabel", 12f, 44f, LabelW, 18f, 10f, TextAlignmentOptions.MidlineLeft, false, 1f); supLabel.text = "SUPERIORITY";
            supWell = FrontKit.Solid(Rect, "SupWell", 0f, 46f, 0f, 14f, Color.clear);
            supFill = FrontKit.Solid(Rect, "SupFill", 0f, 47f, 0f, 12f, Color.clear);
            supNeedle = FrontKit.Solid(Rect, "SupNeedle", 0f, 43f, 3f, 20f, Color.clear);
            supWord = FrontKit.Mono(Rect, "SupWord", 0f, 44f, 100f, 18f, 11.5f, TextAlignmentOptions.MidlineLeft, true, 1f);
            supValue = FrontKit.Mono(Rect, "SupValue", 0f, 44f, 60f, 18f, 11f, TextAlignmentOptions.MidlineRight, true);

            perkLabel = FrontKit.Mono(Rect, "PerkLabel", 12f, 72f, LabelW, 16f, 10f, TextAlignmentOptions.MidlineLeft, false, 1f); perkLabel.text = "PERKS";
            perkValue = FrontKit.Mono(Rect, "PerkValue", 12f + LabelW, 72f, 400f, 16f, 11f, TextAlignmentOptions.MidlineLeft);
            dirLabel = FrontKit.Mono(Rect, "DirLabel", 12f, 96f, LabelW, 16f, 10f, TextAlignmentOptions.MidlineLeft, false, 1f); dirLabel.text = "DIRECTIVE";
            dirValue = FrontKit.Mono(Rect, "DirValue", 12f + LabelW, 96f, 300f, 16f, 11f, TextAlignmentOptions.MidlineLeft, true);
            fundValue = FrontKit.Mono(Rect, "FundValue", 0f, 96f, 200f, 16f, 10.5f, TextAlignmentOptions.MidlineRight);

            buildLabel = FrontKit.Mono(Rect, "BuildLabel", 12f, 122f, LabelW, 20f, 10f, TextAlignmentOptions.MidlineLeft, false, 1f); buildLabel.text = "BUILDING";
            buildName = FrontKit.Mono(Rect, "BuildName", 12f + LabelW, 122f, 200f, 20f, 11.5f, TextAlignmentOptions.MidlineLeft, true);
            buildBar = new FrontBar(Rect, skin, 12f + LabelW + 140f, 127f, 100f, 10f);
            buildNote = FrontKit.Mono(Rect, "BuildNote", 0f, 122f, 100f, 20f, 10.5f, TextAlignmentOptions.MidlineRight);
            queueLine = FrontKit.Mono(Rect, "QueueLine", 12f, 148f, 556f, 16f, 10.5f, TextAlignmentOptions.MidlineLeft);
            alertLine = FrontKit.Mono(Rect, "Alert", 12f, 176f, 556f, 20f, 11f, TextAlignmentOptions.MidlineLeft, true);
            skin.Add(StyleStatic);
            LayoutFor(width);
        }

        public override float Measure(float w) => Height;

        public override void Place(AvSlot s)
        {
            base.Place(s);
            if (Mathf.Abs(s.W - width) > 0.5f) LayoutFor(s.W);
        }

        private void LayoutFor(float w)
        {
            width = w;
            AvLay.Place(back.rectTransform, 0f, 0f, w, Height);
            AvLay.Place(icon.rectTransform, 12f, 12f, 18f, 18f);
            AvLay.Place(open.Rect, w - 82f, 7f, 72f, 26f);
            for (int i = 0; i < pips.Length; i++) AvLay.Place(pips[i].rectTransform, 268f + i * 15f, 15f, 12f, 12f);
            float barX = 12f + LabelW, barW = Mathf.Max(100f, w - barX - 190f);
            AvLay.Place(supWell.rectTransform, barX, 46f, barW, 14f);
            AvLay.Place(supWord.rectTransform, barX + barW + 10f, 44f, 90f, 18f);
            AvLay.Place(supValue.rectTransform, w - 62f, 44f, 50f, 18f);
            AvLay.Place(fundValue.rectTransform, w - 212f, 96f, 200f, 16f);
            AvLay.Place(buildName.rectTransform, 12f + LabelW, 122f, 132f, 20f);
            AvLay.Place(buildNote.rectTransform, w - 104f, 122f, 96f, 20f);
            AvLay.Place(queueLine.rectTransform, 12f, 148f, w - 24f, 16f);
            AvLay.Place(alertLine.rectTransform, 12f, 176f, w - 24f, 20f);
            AvLay.Place(rail.rectTransform, 0f, 0f, 3f, Height);
            if (painted) Paint(shown); // the superiority bar depends on the width
        }

        private void StyleStatic()
        {
            back.Paint(AvInk.Surface.WithAlpha(0.62f), AvInk.Hairline);
            back.BracketColor = AvInk.Frame.WithAlpha(0.8f);
            supWell.color = AvInk.Ground.WithAlpha(0.92f);
            supNeedle.color = AvInk.Ink;
            Color dim = AvInk.Dim;
            foreach (TMP_Text t in new[] { readinessLabel, supLabel, perkLabel, dirLabel, buildLabel }) t.color = dim;
            name.color = AvInk.Ink;
            icon.color = AvInk.Dim;
        }

        public override void Restyle()
        {
            skin.Apply();
            if (painted) Paint(shown);
        }

        public void Paint(in FrontCardView c)
        {
            shown = c;
            painted = true;
            Color tone = FrontKit.Tone(c.Tone);
            rail.color = c.Known ? AvInk.State(c.Tone) : AvInk.Frame;
            FrontKit.Set(readiness, "R" + c.Readiness, AvInk.Ink);
            for (int i = 0; i < pips.Length; i++) pips[i].color = i < c.Readiness ? AvInk.Select : AvInk.Frame.WithAlpha(0.45f);

            float barX = 12f + LabelW, barW = supWell.rectTransform.rect.width, half = barW * 0.5f, len = Mathf.Abs(c.Superiority) / 100f * half;
            supFill.color = AvInk.State(c.Tone).WithAlpha(c.Known ? 0.85f : 0f);
            AvLay.Place(supFill.rectTransform, barX + half + (c.Superiority >= 0 ? 0f : -len), 47f, len, 12f);
            AvLay.Place(supNeedle.rectTransform, barX + half + c.Superiority / 100f * half - 1.5f, 43f, 3f, 20f);
            supNeedle.gameObject.SetActive(c.Known);
            FrontKit.Set(supWord, c.Word, tone);
            FrontKit.Set(supValue, c.Known ? FrontKit.Signed(c.Superiority) : "—", tone);

            FrontKit.Set(perkValue, c.Known ? c.Unlocked + " UNLOCKED · " + c.ReadyPerks + " READY TO FIRE" : "—", AvInk.Ink);
            FrontKit.Set(dirValue, c.Directive, AvInk.Ink);
            FrontKit.Set(fundValue, c.Known ? "FUNDING " + c.FundingPct + " % · ¤ " + c.Budget : "", AvInk.Dim);

            FrontKit.Set(buildName, c.Building, c.HasBuild ? AvInk.Ink : AvInk.Dim);
            buildBar.Show(c.HasBuild);
            buildBar.Set(c.BuildProgress, c.BuildProgress >= 0.995f ? AvInk.State(AvState.Ready) : AvInk.Select);
            FrontKit.Set(buildNote, c.BuildNote ?? "", AvInk.Dim);
            FrontKit.Set(queueLine, !string.IsNullOrEmpty(c.Queue) ? "NEXT · " + c.Queue : "", AvInk.Dim);
            FrontKit.Set(alertLine, c.Alert ?? "", FrontKit.Tone(c.AlertTone == AvState.Inert ? AvState.Info : c.AlertTone));
        }
    }

    /// <summary>One perk of the PERKS tab: name, front and rung, price, state word and ARM / FIRE. Fixed height; the press goes through the controller.</summary>
    internal sealed class PerkRowPart : AvPart
    {
        public const float Height = 28f;

        private readonly AvFrame back;
        private readonly Image rail;
        private readonly TMP_Text name, rung, price, state;
        private readonly AvControl button;
        private PerkRowView shown;
        private bool has;
        private float width = 458f;

        public PerkRowPart(RectTransform parent, Action<SupportActionId> press, SupportActionId id)
        {
            Rect = AvLay.Child(parent, "Perk " + id);
            back = FrontKit.Panel(Rect, "Back", 0f, 0f, width, Height);
            rail = FrontKit.Solid(Rect, "Rail", 0f, 0f, 3f, Height, Color.clear);
            name = FrontKit.Mono(Rect, "Name", 12f, 0f, 128f, Height, 12f, TextAlignmentOptions.MidlineLeft, true);
            rung = FrontKit.Mono(Rect, "Rung", 142f, 0f, 66f, Height, 10.5f, TextAlignmentOptions.MidlineLeft);
            price = FrontKit.Mono(Rect, "Price", 210f, 0f, 60f, Height, 10.5f, TextAlignmentOptions.MidlineLeft);
            state = FrontKit.Mono(Rect, "State", 272f, 0f, 120f, Height, 10.5f, TextAlignmentOptions.MidlineLeft, true);
            button = AvControl.Make(Rect, new AvControl.Spec("ARM", () => press?.Invoke(id), AvButtonStyle.Primary));
            button.SingleLine();
            Layout(width);
            Restyle();
        }

        public override float Measure(float w) => Height;

        public override void Place(AvSlot s)
        {
            base.Place(s);
            if (Mathf.Abs(s.W - width) > 0.5f) Layout(s.W);
        }

        private void Layout(float w)
        {
            width = w;
            AvLay.Place(back.rectTransform, 0f, 0f, w, Height);
            AvLay.Place(state.rectTransform, 272f, 0f, Mathf.Max(90f, w - 272f - 66f), Height);
            AvLay.Place(button.Rect, w - 62f, 2f, 54f, Height - 4f);
        }

        public override void Restyle()
        {
            back.Paint(AvInk.Surface.WithAlpha(0.55f), AvInk.Hairline);
            rung.color = AvInk.Dim;
            price.color = AvInk.Ink;
            if (has) Paint(shown);
            else { name.color = AvInk.Ink; rail.color = AvInk.Frame; }
        }

        public void Paint(in PerkRowView r)
        {
            has = true; shown = r;
            FrontKit.Set(name, r.Name, r.Enabled || r.Armed ? AvInk.Ink : AvInk.Dim);
            FrontKit.Set(rung, r.RungWord, AvInk.Dim);
            FrontKit.Set(price, r.Price, AvInk.Ink);
            FrontKit.Set(state, r.State, FrontKit.Tone(r.Tone));
            rail.color = AvInk.State(r.Tone == AvState.Inert ? AvState.Info : r.Tone).WithAlpha(r.Tone == AvState.Inert ? 0.5f : 1f);
            button.Label = r.Button;
            button.SetStyle(r.Armed ? AvButtonStyle.Danger : AvButtonStyle.Primary);
            if (button.Interactable != r.Enabled) button.Interactable = r.Enabled;
            button.Armed = r.Armed;
        }
    }
}
