using NOAvionics;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Modules.Support.Presentation.C2
{
    /// <summary>Footer strip, 24 px: a state slab (READY / NEG / INT ...) and one words line in the NEGATIVE: reason - fix voice.</summary>
    internal sealed class C2Footer : AvPart
    {
        public const float Height = 24f;
        private const float SlabW = 46f;
        private readonly Image back, rule, slab;
        private readonly TMP_Text slabText, words;
        private AvState tone = AvState.Inert;
        private string slabRaw = "", wordsRaw = "";
        private float width = AvTokens.PanelWidth;

        public C2Footer(RectTransform parent)
        {
            Rect = AvLay.Child(parent, "C2Footer");
            back = AvLay.Solid(Rect, "Back", Color.clear);
            rule = AvLay.Solid(Rect, "Rule", Color.clear);
            slab = AvLay.Solid(Rect, "Slab", Color.clear);
            slabText = C2Kit.Mono(Rect, "SlabText", 10f, TextAlignmentOptions.Center, true, 3f);
            words = C2Kit.Cond(Rect, "Words", AvTextRole.ProseSmall, 12f, TextAlignmentOptions.MidlineLeft);
            Restyle();
            Layout();
        }

        public void Set(string slabWord, AvState state, string text)
        {
            slabRaw = slabWord ?? "";
            wordsRaw = text ?? "";
            tone = state;
            Restyle();
            Layout();
        }

        public override float Measure(float w) => Height;

        public override void Place(AvSlot s)
        {
            base.Place(s);
            width = s.W;
            Layout();
        }

        private void Layout()
        {
            AvLay.Place(back.rectTransform, 0f, 0f, width, Height);
            AvLay.Place(rule.rectTransform, 0f, 0f, width, 1f);
            AvLay.Place(slab.rectTransform, 4f, 4f, SlabW, 16f);
            C2Kit.Place(slabText, 4f, 4f, SlabW, 16f);
            OpsText.Set(slabText, C2Kit.FitTo(slabText, slabRaw, SlabW - 4f));
            float x = 4f + SlabW + 8f, w = width - x - 8f;
            OpsText.Set(words, C2Kit.FitTo(words, wordsRaw, w));
            C2Kit.Place(words, x, 0f, w, Height);
        }

        public override void Restyle()
        {
            AvStyle f = AvStyleHost.FuiStyle("footer " + AvStates.Class(tone));
            back.color = AvStyleHost.Resolve(f.Background, AvTheme.SurfaceInert);
            rule.color = OpsInk.Hairline;
            slab.color = C2Kit.SlabFill(tone == AvState.Inert ? AvState.Ready : tone);
            slabText.color = C2Kit.SlabInk;
            words.color = tone == AvState.Inert || tone == AvState.Ready || tone == AvState.Info ? OpsInk.Dim : OpsInk.Word(tone);
        }
    }
}
