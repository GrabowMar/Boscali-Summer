using BoscaliSummer.Framework.Contracts;
using NOAvionics;
using NOAvionics.Ui;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Features.Events.Presentation
{
    /// <summary>
    /// The kit v2 parts the DISPATCH and DESK pages share.
    ///
    /// <para><see cref="EventPlateArt"/> hosts the event poster (data/art, <see cref="EventArtCache"/> and
    /// <see cref="EventPlate"/>): the player's drawn poster when one exists, else a generated stripe plate
    /// over a Tabler category glyph — never a blank rectangle. It is not itself an <see cref="AvPart"/>;
    /// it is a small widget the cards below place and resize, the way <c>AvCard</c> hosts a nested
    /// <c>AvFlow</c>.</para>
    ///
    /// <para><see cref="EventActiveCardPart"/> is the live dispatch: poster, title, scope, countdown,
    /// effect, plain-words consequence, flavour copy, the scripted beat rows and the time-remaining bar.
    /// Every state it can show (ENDING, a scripted beat firing, a cancelled beat) carries a word as well
    /// as a rail colour (R1). <see cref="EventCaseFilePart"/> is the same poster-plus-copy shape, sized
    /// for the DESK page's current-or-last case file.</para>
    /// </summary>
    internal sealed partial class EventsMfdPanel
    {
        /// <summary>The category mark shown when no poster has been drawn for an event.</summary>
        internal static AvIcon CategoryIcon(string category) =>
            category == "POLITICAL" ? AvIcon.Scale
            : category == "HAZARD" ? AvIcon.AlertTriangle
            : AvIcon.Coins;

        /// <summary>One 16:9 poster plate: the drawn art, or a generated stripe film over a category glyph.</summary>
        internal sealed class EventPlateArt
        {
            private readonly Image back, stripes, art;
            private readonly TMP_Text mark;
            private readonly float markSize;

            public RectTransform Root { get; }

            public EventPlateArt(RectTransform parent, float markSizePx)
            {
                markSize = markSizePx;
                Root = AvLay.Child(parent, "Plate");
                Root.gameObject.AddComponent<RectMask2D>();
                back = AvLay.Solid(Root, "Back", Color.clear);
                stripes = AvLay.Solid(Root, "Stripes", Color.white);
                stripes.sprite = EventPlate.Stripes();
                stripes.type = Image.Type.Tiled;
                art = AvLay.Solid(Root, "Art", Color.white);
                art.preserveAspect = false;
                mark = AvIcons.Make(Root, AvIcon.Coins, markSize, Color.white);
                Restyle();
            }

            public void Layout(float w, float h)
            {
                AvLay.Fill(back.rectTransform);
                AvLay.Fill(stripes.rectTransform);
                AvLay.Fill(art.rectTransform);
                float size = Mathf.Min(markSize, Mathf.Min(w, h) * 0.6f);
                AvLay.Place(mark.rectTransform, (w - size) * 0.5f, (h - size) * 0.5f, size, size);
            }

            public void Bind(Sprite poster, AvIcon categoryGlyph, Color ink)
            {
                bool hasArt = poster != null;
                art.sprite = poster;
                art.enabled = hasArt;
                stripes.gameObject.SetActive(!hasArt);
                stripes.color = ink.WithAlpha(0.10f);
                mark.gameObject.SetActive(!hasArt);
                if (hasArt) return;
                AvIcons.Set(mark, categoryGlyph, markSize);
                mark.color = ink.WithAlpha(0.85f);
            }

            public void Restyle() => back.color = AvStyleHost.FuiColor("surface-inert", AvTheme.SurfaceInert);
        }

        /// <summary>
        /// The active dispatch card: poster, headline, scope, countdown, effect, consequence, flavour and
        /// (for a scripted superevent) the beat log and the time-remaining bar. Content and scripted-step
        /// count are re-measured on every relayout, so a long flavour text grows the card instead of clipping.
        /// </summary>
        private sealed class EventActiveCardPart : AvPart
        {
            private const float PlateHeight = 118f;
            private const float StepPitch = 15f;

            private readonly AvFrame frame;
            private readonly Image rail;
            private readonly EventPlateArt plate;
            private readonly TMP_Text title, scope, countdown, effect, consequence, flavor;
            private readonly TMP_Text[] steps;
            private readonly AvGaugeGraphic progress;

            private AvState tierState = AvState.Inert;
            private AvState effectState = AvState.Inert;
            private AvState countdownState = AvState.Inert;
            private AvState progressState = AvState.Inert;
            private bool scripted;
            private int stepCount;

            public EventActiveCardPart(RectTransform parent)
            {
                Rect = AvLay.Child(parent, "ActiveEvent");
                frame = AvFrame.Add(Rect, "Frame", AvChamfer.Diagonal(8f));
                AvLay.Fill(frame.rectTransform);
                rail = AvLay.Solid(Rect, "Rail", Color.clear);
                plate = new EventPlateArt(Rect, 30f);
                title = AvText.Make(Rect, "Title", AvTextRole.Title, "", TextAlignmentOptions.TopLeft, true);
                scope = AvText.Make(Rect, "Scope", AvTextRole.Micro, "", TextAlignmentOptions.TopLeft, true);
                countdown = AvText.Make(Rect, "Countdown", AvTextRole.DataStrong, "", TextAlignmentOptions.TopLeft, true);
                effect = AvText.Make(Rect, "Effect", AvTextRole.Head, "", TextAlignmentOptions.TopLeft, true);
                consequence = AvText.Make(Rect, "Consequence", AvTextRole.Prose, "", TextAlignmentOptions.TopLeft, true);
                flavor = AvText.Make(Rect, "Flavor", AvTextRole.ProseSmall, "", TextAlignmentOptions.TopLeft, true);
                steps = new TMP_Text[MaximumEventSteps];
                for (int i = 0; i < steps.Length; i++)
                    steps[i] = AvText.Make(Rect, "Step " + i, AvTextRole.DataSmall, "", TextAlignmentOptions.TopLeft, true);
                var go = new GameObject("Progress", typeof(RectTransform), typeof(CanvasRenderer));
                go.transform.SetParent(Rect, false);
                progress = go.AddComponent<AvGaugeGraphic>();
                progress.Shape = AvGaugeShape.Bar;
                progress.raycastTarget = false;
                Restyle();
            }

            /// <summary>Binds a live dispatch. Countdown/progress/script are refreshed separately, at refresh cadence.</summary>
            public void BindActive(ActiveEventView view, AvState tier, string effectText, AvState state, string consequenceText)
            {
                tierState = tier;
                effectState = state;
                plate.Bind(EventArtCache.Get(view.IconKey, view.IsSuper ? "tier_super" : "tier_medium"),
                    CategoryIcon(view.Category), RailColor(tierState));
                title.text = view.Title.ToUpperInvariant();
                scope.text = view.Category + " · " + view.Target;
                effect.text = effectText ?? "";
                consequence.text = consequenceText ?? "";
                flavor.text = view.FlavorText ?? "";
                Restyle();
            }

            public void BindCalm(string note)
            {
                tierState = AvState.Inert;
                effectState = AvState.Inert;
                countdownState = AvState.Inert;
                plate.Bind(null, AvIcon.Radar2, RailColor(AvState.Inert));
                title.text = "THE THEATER IS QUIET";
                scope.text = "";
                countdown.text = "";
                effect.text = "";
                consequence.text = note ?? "";
                flavor.text = "";
                scripted = false;
                stepCount = 0;
                SetProgress(0f, AvState.Inert);
                Restyle();
            }

            /// <summary>"ENDS m:ss", switching to danger/caution as the clock runs low (R1: the word always says so too).</summary>
            public void SetClock(string text, bool ending, bool critical)
            {
                countdown.text = ending ? "ENDING · " + text : text;
                countdownState = critical ? AvState.Danger : ending ? AvState.Caution : tierState;
                Restyle();
            }

            public void SetProgress(float fraction01, AvState state)
            {
                progress.Value = Mathf.Clamp01(fraction01);
                progressState = state;
                Restyle();
            }

            /// <summary>The scripted beat log: "T+m:ss [DONE]/[NEXT] label", or cancelled when the target didn't resolve.</summary>
            public void SetScript(ActiveEventView view, float start, float now)
            {
                int count = view != null ? view.Steps.Count : 0;
                scripted = view != null && view.IsSuper && count > 0;
                stepCount = Mathf.Clamp(count, 0, steps.Length);
                for (int i = 0; i < steps.Length; i++)
                {
                    if (i >= stepCount) { steps[i].text = ""; continue; }
                    ActiveEventStep step = view.Steps[i];
                    if (!view.TargetResolved)
                    {
                        steps[i].text = "T+" + AvNum.Clock(step.AtSeconds) + "  [CANCELLED] " + step.Label;
                        continue;
                    }
                    bool fired = now >= start + step.AtSeconds;
                    steps[i].text = "T+" + AvNum.Clock(step.AtSeconds) + "  " + (fired ? "[DONE] " : "[NEXT] ") + step.Label;
                }
                Restyle();
            }

            public override float Measure(float width)
            {
                float w = width - 24f;
                float h = 10f + PlateHeight + 8f;
                h += AvText.Height(title, w) + 4f;
                if (scope.text.Length > 0) h += AvText.Height(scope, w) + 4f;
                if (countdown.text.Length > 0) h += AvText.Height(countdown, w) + 6f;
                if (effect.text.Length > 0) h += AvText.Height(effect, w) + 4f;
                if (consequence.text.Length > 0) h += AvText.Height(consequence, w) + 4f;
                if (flavor.text.Length > 0) h += AvText.Height(flavor, w) + 6f;
                if (scripted) h += stepCount * StepPitch + 4f;
                h += 10f; // progress bar row
                return h;
            }

            public override void Place(AvSlot s)
            {
                base.Place(s);
                float x = 12f, w = s.W - 24f, y = 10f;
                AvLay.Place(rail.rectTransform, 0f, 0f, 3f, s.H);
                AvLay.Place(plate.Root, x, y, w, PlateHeight);
                plate.Layout(w, PlateHeight);
                y += PlateHeight + 8f;

                float titleH = AvText.Height(title, w);
                AvLay.Place(title.rectTransform, x, y, w, titleH);
                y += titleH + 4f;

                if (scope.text.Length > 0)
                {
                    float sh = AvText.Height(scope, w);
                    AvLay.Place(scope.rectTransform, x, y, w, sh);
                    y += sh + 4f;
                }
                scope.gameObject.SetActive(scope.text.Length > 0);

                if (countdown.text.Length > 0)
                {
                    float ch = AvText.Height(countdown, w);
                    AvLay.Place(countdown.rectTransform, x, y, w, ch);
                    y += ch + 6f;
                }
                countdown.gameObject.SetActive(countdown.text.Length > 0);

                if (effect.text.Length > 0)
                {
                    float eh = AvText.Height(effect, w);
                    AvLay.Place(effect.rectTransform, x, y, w, eh);
                    y += eh + 4f;
                }
                effect.gameObject.SetActive(effect.text.Length > 0);

                if (consequence.text.Length > 0)
                {
                    float qh = AvText.Height(consequence, w);
                    AvLay.Place(consequence.rectTransform, x, y, w, qh);
                    y += qh + 4f;
                }
                consequence.gameObject.SetActive(consequence.text.Length > 0);

                if (flavor.text.Length > 0)
                {
                    float fh = AvText.Height(flavor, w);
                    AvLay.Place(flavor.rectTransform, x, y, w, fh);
                    y += fh + 6f;
                }
                flavor.gameObject.SetActive(flavor.text.Length > 0);

                for (int i = 0; i < steps.Length; i++)
                {
                    bool active = scripted && i < stepCount;
                    steps[i].gameObject.SetActive(active);
                    if (!active) continue;
                    float sh = AvText.Height(steps[i], w);
                    AvLay.Place(steps[i].rectTransform, x, y, w, sh);
                    y += StepPitch;
                }
                if (scripted) y += 4f;

                AvLay.Place((RectTransform)progress.transform, x, s.H - 8f, w, 3f);
            }

            public override void Restyle()
            {
                AvStyle c = AvStyleHost.FuiStyle("card");
                frame.Paint(AvStyleHost.Resolve(c.Background, AvTheme.Surface), AvStyleHost.Resolve(c.Border, AvTheme.Hairline));
                rail.color = RailColor(tierState);
                title.color = AvStyleHost.FuiColor("ink", AvTheme.TextPrimary);
                scope.color = AvStyleHost.FuiColor("ink-dim", AvTheme.Dim);
                countdown.color = RailColor(countdownState);
                effect.color = RailColor(effectState);
                consequence.color = AvStyleHost.FuiColor("ink", AvTheme.TextPrimary);
                flavor.color = AvStyleHost.FuiColor("ink-dim", AvTheme.Dim);
                foreach (TMP_Text step in steps) step.color = AvStyleHost.FuiColor("ink-dim", AvTheme.Dim);
                progress.Track = AvStyleHost.FuiColor("hairline", AvTheme.Hairline);
                progress.FillColor = progress.FillEnd = RailColor(progressState);
                progress.SetVerticesDirty();
                plate.Restyle();
            }

            private static Color RailColor(AvState state) =>
                AvStyleHost.FuiColor(AvStates.Class(state), AvTheme.RailInfo);
        }

        /// <summary>The DESK page's current-or-last case file: a small poster plus title, meta and body copy.</summary>
        private sealed class EventCaseFilePart : AvPart
        {
            private const float PlateSize = 64f;
            private readonly AvFrame frame;
            private readonly EventPlateArt plate;
            private readonly TMP_Text title, meta, body;
            private AvState tierState = AvState.Inert;

            public EventCaseFilePart(RectTransform parent)
            {
                Rect = AvLay.Child(parent, "CaseFile");
                frame = AvFrame.Add(Rect, "Frame", AvChamfer.Diagonal(6f));
                AvLay.Fill(frame.rectTransform);
                plate = new EventPlateArt(Rect, 20f);
                title = AvText.Make(Rect, "Title", AvTextRole.Head, "", TextAlignmentOptions.TopLeft, true);
                meta = AvText.Make(Rect, "Meta", AvTextRole.Micro, "", TextAlignmentOptions.TopLeft, true);
                body = AvText.Make(Rect, "Body", AvTextRole.Prose, "", TextAlignmentOptions.TopLeft, true);
                Restyle();
            }

            public void Bind(string titleText, string metaText, string bodyText, AvIcon icon, Sprite poster, AvState tier)
            {
                tierState = tier;
                title.text = titleText ?? "";
                meta.text = metaText ?? "";
                body.text = bodyText ?? "";
                plate.Bind(poster, icon, AvStyleHost.FuiColor(AvStates.Class(tierState), AvTheme.Dim));
                Restyle();
            }

            public override float Measure(float width)
            {
                float textW = width - PlateSize - 12f - 24f;
                float h = 10f + AvText.Height(title, textW) + 4f + AvText.Height(meta, textW) + 6f;
                float bodyH = AvText.Height(body, width - 24f);
                return Mathf.Max(10f + PlateSize + 10f, h) + bodyH + 10f;
            }

            public override void Place(AvSlot s)
            {
                base.Place(s);
                float x = 12f, textX = x + PlateSize + 12f, textW = s.W - PlateSize - 12f - 24f;
                AvLay.Place(plate.Root, x, 10f, PlateSize, PlateSize);
                plate.Layout(PlateSize, PlateSize);
                float th = AvText.Height(title, textW);
                AvLay.Place(title.rectTransform, textX, 10f, textW, th);
                AvLay.Place(meta.rectTransform, textX, 14f + th, textW, AvText.Height(meta, textW));
                float bodyY = 10f + PlateSize + 10f;
                AvLay.Place(body.rectTransform, x, bodyY, s.W - 24f, AvText.Height(body, s.W - 24f));
            }

            public override void Restyle()
            {
                AvStyle c = AvStyleHost.FuiStyle("card");
                frame.Paint(AvStyleHost.Resolve(c.Background, AvTheme.Surface), AvStyleHost.Resolve(c.Border, AvTheme.Hairline));
                title.color = AvStyleHost.FuiColor("ink", AvTheme.TextPrimary);
                meta.color = AvStyleHost.FuiColor(AvStates.Class(tierState), AvTheme.Dim);
                body.color = AvStyleHost.FuiColor("ink-dim", AvTheme.Dim);
                plate.Restyle();
            }
        }
    }
}
