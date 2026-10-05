using System;
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
        private const float SlabW = 46f, AbortW = 58f;
        private readonly Image back, rule, slab;
        private readonly TMP_Text slabText, words;
        private readonly AvControl abort;
        private bool abortShown;
        private AvState tone = AvState.Inert;
        private string slabRaw = "", wordsRaw = "", hint;
        private float width = AvTokens.PanelWidth;

        /// <param name="onAbort">When given, the footer carries a small ABORT button (shown by <see cref="ShowAbort"/>) that calls it.</param>
        public C2Footer(RectTransform parent, Action onAbort = null)
        {
            Rect = AvLay.Child(parent, "C2Footer");
            back = AvLay.Solid(Rect, "Back", Color.clear);
            rule = AvLay.Solid(Rect, "Rule", Color.clear);
            slab = AvLay.Solid(Rect, "Slab", Color.clear);
            slabText = C2Kit.Mono(Rect, "SlabText", 10f, TextAlignmentOptions.Center, true, 3f);
            words = C2Kit.Cond(Rect, "Words", AvTextRole.ProseSmall, 12f, TextAlignmentOptions.MidlineLeft);
            if (onAbort != null)
            {
                abort = AvControl.Make(Rect, new AvControl.Spec("ABORT", onAbort, AvButtonStyle.Danger));
                abort.SingleLine();
                abort.Help = "Disarm the armed CALL. Nothing is spent.";
                abort.Rect.gameObject.SetActive(false);
            }
            Restyle();
            Layout();
        }

        /// <summary>Shows or hides the ABORT button (only while a call is armed on a page that has no abort of its own).</summary>
        public void ShowAbort(bool show)
        {
            show &= abort != null;
            if (show == abortShown) return;
            abortShown = show;
            abort.Rect.gameObject.SetActive(show);
            Layout();
        }

        public void Set(string slabWord, AvState state, string text)
        {
            string slabNew = slabWord ?? "", wordsNew = text ?? "";
            // The slab already says READY: the words do not say it twice.
            if (slabNew == "READY" && wordsNew.StartsWith("READY \u00B7 ", StringComparison.Ordinal)) wordsNew = wordsNew.Substring(8);
            if (slabNew == slabRaw && wordsNew == wordsRaw && state == tone) return;
            slabRaw = slabNew;
            wordsRaw = wordsNew;
            bool toneChanged = state != tone;
            tone = state;
            if (toneChanged) Restyle();
            Layout();
        }

        /// <summary>Hover help: the TIP slab and the hint replace the status words while the pointer is over a control; null or empty restores them.</summary>
        public void SetHint(string text)
        {
            string h = string.IsNullOrEmpty(text) ? null : text;
            if (h == hint) return;
            hint = h;
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
            OpsText.Set(slabText, C2Kit.FitTo(slabText, hint != null ? "TIP" : slabRaw, SlabW - 4f));
            float x = 4f + SlabW + 8f, w = width - x - 8f - (abortShown ? AbortW + 4f : 0f);
            if (abort != null) AvLay.Place(abort.Rect, width - 4f - AbortW, 3f, AbortW, 18f);
            OpsText.Set(words, C2Kit.FitTo(words, hint ?? wordsRaw, w));
            C2Kit.Place(words, x, 0f, w, Height);
        }

        public override void Restyle()
        {
            AvState shown = hint != null ? AvState.Inert : tone;
            AvStyle f = AvStyleHost.FuiStyle("footer " + AvStates.Class(shown));
            back.color = AvStyleHost.Resolve(f.Background, AvTheme.SurfaceInert);
            rule.color = OpsInk.Hairline;
            slab.color = C2Kit.SlabFill(shown == AvState.Inert ? AvState.Ready : shown);
            slabText.color = C2Kit.SlabInk;
            abort?.Restyle();
            words.color = hint != null ? OpsInk.Ink
                : tone == AvState.Inert || tone == AvState.Ready || tone == AvState.Info ? OpsInk.Dim : OpsInk.Word(tone);
        }
    }
}
