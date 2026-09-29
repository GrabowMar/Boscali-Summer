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
    /// <c>AvFlow</c>. The poster is centre-cropped to the plate, never stretched.</para>
    ///
    /// <para><see cref="EventHeroPart"/> is the live dispatch: a banner, the tier tag, the event name, effect
    /// pills, a countdown with its time-remaining bar, the plain-words consequence and the scripted beats.
    /// It has a compact calm state for a quiet theater. Every state it can show (ENDING, a scripted beat
    /// firing, a cancelled beat) carries a word as well as a colour (R1). <see cref="EventCaseFilePart"/>
    /// is the same poster-plus-copy shape, sized for the DESK page's current-or-last case file.</para>
    ///
    /// <para>Every part here measures and places through one arrangement routine and calls
    /// <see cref="AvPart.Changed"/> whenever a setter changes text that can change its height.</para>
    /// </summary>
    internal sealed partial class EventsMfdPanel
    {
        /// <summary>The category mark shown when no poster has been drawn for an event.</summary>
        internal static AvIcon CategoryIcon(string category) =>
            category == "POLITICAL" ? AvIcon.Scale
            : category == "HAZARD" ? AvIcon.AlertTriangle
            : AvIcon.Coins;

        /// <summary>A state colour that is legible as text: inert reads as the dim ink, not the rail grey.</summary>
        internal static Color TextColor(AvState state) =>
            state == AvState.Inert
                ? AvStyleHost.FuiColor("ink-dim", AvTheme.Dim)
                : AvStyleHost.FuiColor(AvStates.Class(state), AvTheme.RailInfo);

        /// <summary>The rect of <paramref name="source"/> (in uv space) that fills a w x h plate without stretching.</summary>
        internal static Rect CropUv(Rect source, float w, float h)
        {
            if (w <= 0f || h <= 0f || source.width <= 0f || source.height <= 0f) return source;
            float srcAspect = source.width / source.height, dstAspect = w / h;
            if (dstAspect > srcAspect)
            {
                float keep = source.height * srcAspect / dstAspect;
                return new Rect(source.x, source.y + (source.height - keep) * 0.5f, source.width, keep);
            }
            float keepW = source.width * dstAspect / srcAspect;
            return new Rect(source.x + (source.width - keepW) * 0.5f, source.y, keepW, source.height);
        }

        /// <summary>The uv rect of a sprite inside its texture.</summary>
        internal static Rect SpriteUv(Sprite sprite)
        {
            Texture2D texture = sprite != null ? sprite.texture : null;
            if (texture == null || texture.width == 0 || texture.height == 0) return new Rect(0f, 0f, 1f, 1f);
            Rect r = sprite.textureRect;
            return new Rect(r.x / texture.width, r.y / texture.height, r.width / texture.width, r.height / texture.height);
        }

        /// <summary>One plate: the drawn art, or a generated stripe film over a category glyph.</summary>
        internal sealed class EventPlateArt
        {
            private readonly Image back, stripes;
            private readonly RawImage art;
            private readonly TMP_Text mark;
            private readonly float markSize;
            private Sprite poster;

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
                var artGo = new GameObject("Art", typeof(RectTransform), typeof(CanvasRenderer));
                artGo.transform.SetParent(Root, false);
                art = artGo.AddComponent<RawImage>();
                art.raycastTarget = false;
                mark = AvIcons.Make(Root, AvIcon.Coins, markSize, Color.white);
                Restyle();
            }

            public void Layout(float w, float h)
            {
                AvLay.Fill(back.rectTransform);
                AvLay.Fill(stripes.rectTransform);
                AvLay.Fill(art.rectTransform);
                if (poster != null) art.uvRect = CropUv(SpriteUv(poster), w, h);
                float size = Mathf.Min(markSize, Mathf.Min(w, h) * 0.6f);
                AvLay.Place(mark.rectTransform, (w - size) * 0.5f, (h - size) * 0.5f, size, size);
            }

            public void Bind(Sprite art, AvIcon categoryGlyph, Color ink)
            {
                poster = art;
                bool hasArt = poster != null;
                this.art.texture = hasArt ? poster.texture : null;
                this.art.enabled = hasArt;
                stripes.gameObject.SetActive(!hasArt);
                stripes.color = ink.WithAlpha(0.10f);
                mark.gameObject.SetActive(!hasArt);
                if (hasArt) return;
                AvIcons.Set(mark, categoryGlyph, markSize);
                mark.color = ink.WithAlpha(0.85f);
            }

            public void Restyle() => back.color = AvStyleHost.FuiColor("surface-inert", AvTheme.SurfaceInert);
        }

        /// <summary>A small effect pill: state rail plus the effect word, sized to its text and wrapped by its owner.</summary>
        internal sealed class Pill
        {
            private const float Height = 22f;
            private readonly Image back, rail;
            private readonly TMP_Text text;
            private AvState state = AvState.Inert;

            public RectTransform Root { get; }
            public static float PillHeight => Height;
            public bool Visible => text.text.Length > 0;
            public float Width => Visible ? Mathf.Ceil(AvText.Width(text)) + 20f : 0f;

            public Pill(RectTransform parent)
            {
                Root = AvLay.Child(parent, "Pill");
                back = AvLay.Solid(Root, "Back", Color.clear);
                rail = AvLay.Solid(Root, "Rail", Color.clear);
                text = AvText.Make(Root, "Text", AvTextRole.DataSmall, "", TextAlignmentOptions.MidlineLeft);
                Root.gameObject.SetActive(false);
                Restyle();
            }

            /// <summary>True when the pill's text or visibility changed (its owner must re-measure).</summary>
            public bool Set(string value, AvState st)
            {
                string composed = string.IsNullOrEmpty(value) ? "" : AvStates.Glyph(st) + value;
                if (composed == text.text && st == state) return false;
                bool visibilityOrWidth = composed != text.text;
                text.text = composed;
                state = st;
                Root.gameObject.SetActive(composed.Length > 0);
                Restyle();
                return visibilityOrWidth;
            }

            public void Place(float x, float y, float w)
            {
                AvLay.Place(Root, x, y, w, Height);
                AvLay.Fill(back.rectTransform);
                AvLay.Place(rail.rectTransform, 0f, 0f, 2f, Height);
                AvLay.Place(text.rectTransform, 10f, 0f, w - 12f, Height);
            }

            public void Restyle()
            {
                AvStyle st = AvStyleHost.FuiStyle("chip " + AvStates.Class(state));
                back.color = AvStyleHost.Resolve(st.Background, AvTheme.SurfaceInert);
                rail.color = AvStyleHost.Resolve(st.Rail, AvTheme.RailInert);
                text.color = state == AvState.Inert
                    ? AvStyleHost.FuiColor("ink", AvTheme.TextPrimary)
                    : TextColor(state);
            }
        }

        /// <summary>
        /// The active dispatch, the page's hero. Calm: one compact card. Active: banner + tier tag, category and
        /// target, the event name, effect pills, a big countdown over its time-remaining bar, the consequence in
        /// plain words, flavour copy and (for a scripted superevent) the beat log.
        /// </summary>
        private sealed class EventHeroPart : AvPart
        {
            private const float BannerHeight = 100f;
            private const float CalmPlate = 52f;

            private readonly AvFrame frame;
            private readonly Image rail, tagBack;
            private readonly EventPlateArt plate;
            private readonly TMP_Text tag, kicker, title, clock, clockNote, clockTag, consequence, flavor;
            private readonly Pill[] pills = new Pill[3];
            private readonly TMP_Text[] steps;
            private readonly AvGaugeGraphic progress;

            private AvState tierState = AvState.Inert;
            private AvState clockState = AvState.Inert;
            private AvState progressState = AvState.Inert;
            private bool calm = true;
            private bool scripted;
            private int stepCount;

            public EventHeroPart(RectTransform parent)
            {
                Rect = AvLay.Child(parent, "EventHero");
                frame = AvFrame.Add(Rect, "Frame", AvChamfer.Diagonal(8f));
                AvLay.Fill(frame.rectTransform);
                rail = AvLay.Solid(Rect, "Rail", Color.clear);
                plate = new EventPlateArt(Rect, 30f);
                tagBack = AvLay.Solid(plate.Root, "TagBack", Color.clear);
                tag = AvText.Make(tagBack.rectTransform, "Tag", AvTextRole.Micro, "", TextAlignmentOptions.MidlineLeft);
                kicker = AvText.Make(Rect, "Kicker", AvTextRole.Micro, "", TextAlignmentOptions.TopLeft, true);
                title = AvText.Make(Rect, "Title", AvTextRole.Title, "", TextAlignmentOptions.TopLeft, true);
                for (int i = 0; i < pills.Length; i++) pills[i] = new Pill(Rect);
                clock = AvText.Make(Rect, "Clock", AvTextRole.Display, "", TextAlignmentOptions.MidlineLeft);
                clockNote = AvText.Make(Rect, "ClockNote", AvTextRole.Micro, "", TextAlignmentOptions.MidlineLeft);
                clockTag = AvText.Make(Rect, "ClockTag", AvTextRole.Micro, "", TextAlignmentOptions.MidlineRight);
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
                ApplyMode(true);
                Restyle();
            }

            /// <summary>Show the calm compact card or the full active one.</summary>
            private void ApplyMode(bool isCalm)
            {
                calm = isCalm;
                tagBack.gameObject.SetActive(!isCalm);
                kicker.gameObject.SetActive(!isCalm);
                clock.gameObject.SetActive(!isCalm);
                clockNote.gameObject.SetActive(!isCalm);
                clockTag.gameObject.SetActive(!isCalm);
                progress.gameObject.SetActive(!isCalm);
                flavor.gameObject.SetActive(!isCalm);
                if (isCalm)
                    foreach (Pill pill in pills) pill.Set("", AvState.Inert);
            }

            /// <summary>Binds a live dispatch. Clock, bar and script refresh separately, at refresh cadence.</summary>
            public void BindActive(ActiveEventView view, AvState tier, string consequenceText)
            {
                ApplyMode(false);
                tierState = tier;
                plate.Bind(EventArtCache.Get(view.IconKey, view.IsSuper ? "tier_super" : "tier_medium"),
                    CategoryIcon(view.Category), TextColor(tierState));
                tag.text = AvStates.Glyph(tierState) + view.Tier;
                kicker.text = view.Category + " · " + view.Target;
                title.text = view.Title.ToUpperInvariant();
                consequence.text = consequenceText ?? "";
                flavor.text = view.FlavorText ?? "";
                Restyle();
                Changed();
            }

            public void BindCalm(string note)
            {
                ApplyMode(true);
                tierState = AvState.Inert;
                clockState = AvState.Inert;
                scripted = false;
                stepCount = 0;
                foreach (TMP_Text step in steps) step.text = "";
                plate.Bind(null, AvIcon.Radar2, TextColor(AvState.Inert));
                title.text = "THE THEATER IS QUIET";
                consequence.text = note ?? "";
                SetProgress(0f, AvState.Inert);
                Restyle();
                Changed();
            }

            /// <summary>The three effect pills (cost, tempo, side). Empty text hides a pill.</summary>
            public void SetPills(string first, AvState firstState, string second, AvState secondState,
                string third, AvState thirdState)
            {
                bool grew = pills[0].Set(first, firstState);
                grew |= pills[1].Set(second, secondState);
                grew |= pills[2].Set(third, thirdState);
                if (grew) Changed();
            }

            /// <summary>The clock is one mono number; ENDING / CRITICAL is a word beside it (R1).</summary>
            public void SetClock(string clockText, bool ending, bool critical)
            {
                clock.text = clockText ?? "";
                clockNote.text = "REMAINING";
                clockState = critical ? AvState.Danger : ending ? AvState.Caution : AvState.Inert;
                clockTag.text = critical ? AvStates.Glyph(AvState.Danger) + "CRITICAL"
                    : ending ? AvStates.Glyph(AvState.Caution) + "ENDING" : "";
                clock.color = clockState == AvState.Inert ? AvStyleHost.FuiColor("ink", AvTheme.TextPrimary) : TextColor(clockState);
                clockTag.color = TextColor(clockState);
            }

            public void SetProgress(float fraction01, AvState state)
            {
                progress.Value = Mathf.Clamp01(fraction01);
                if (state != progressState)
                {
                    progressState = state;
                    Restyle();
                }
            }

            /// <summary>The scripted beat log: "T+m:ss [DONE]/[NEXT] label", or cancelled when the target didn't resolve.</summary>
            public void SetScript(ActiveEventView view, float start, float now)
            {
                int count = view != null ? view.Steps.Count : 0;
                bool wasScripted = scripted;
                int wasCount = stepCount;
                scripted = view != null && view.IsSuper && count > 0;
                stepCount = Mathf.Clamp(count, 0, steps.Length);
                bool textChanged = wasScripted != scripted || wasCount != stepCount;
                for (int i = 0; i < steps.Length; i++)
                {
                    string line = "";
                    if (scripted && i < stepCount)
                    {
                        ActiveEventStep step = view.Steps[i];
                        if (!view.TargetResolved)
                            line = "T+" + AvNum.Clock(step.AtSeconds) + "  [CANCELLED] " + step.Label;
                        else
                        {
                            bool fired = now >= start + step.AtSeconds;
                            line = "T+" + AvNum.Clock(step.AtSeconds) + "  " + (fired ? "[DONE] " : "[NEXT] ") + step.Label;
                        }
                    }
                    if (steps[i].text != line)
                    {
                        // [NEXT] -> [DONE] keeps the line length; only a line appearing or going changes height.
                        textChanged |= steps[i].text.Length == 0 || line.Length == 0;
                        steps[i].text = line;
                    }
                }
                if (textChanged) Changed();
            }

            public override float Measure(float width) => Arrange(width, false);

            public override void Place(AvSlot s)
            {
                base.Place(s);
                Arrange(s.W, true);
            }

            private float Arrange(float width, bool place)
            {
                float x = 12f, w = width - 24f, y = 12f;
                if (calm)
                {
                    float textX = x + CalmPlate + 12f, textW = w - CalmPlate - 12f;
                    float th = AvText.Height(title, textW);
                    float ch = consequence.text.Length > 0 ? AvText.Height(consequence, textW) : 0f;
                    float block = th + (ch > 0f ? 4f + ch : 0f);
                    float total = 12f + Mathf.Max(CalmPlate, block) + 12f;
                    if (place)
                    {
                        AvLay.Place(rail.rectTransform, 0f, 0f, 3f, total);
                        AvLay.Place(plate.Root, x, y, CalmPlate, CalmPlate);
                        plate.Layout(CalmPlate, CalmPlate);
                        AvLay.Place(title.rectTransform, textX, y, textW, th);
                        AvLay.Place(consequence.rectTransform, textX, y + th + 4f, textW, ch);
                    }
                    return total;
                }

                if (place)
                {
                    AvLay.Place(plate.Root, x, y, w, BannerHeight);
                    plate.Layout(w, BannerHeight);
                    float tagW = Mathf.Ceil(AvText.Width(tag)) + 16f;
                    AvLay.Place(tagBack.rectTransform, 8f, 8f, tagW, 20f);
                    AvLay.Place(tag.rectTransform, 8f, 0f, tagW - 10f, 20f);
                }
                y += BannerHeight + 10f;

                float kh = AvText.Height(kicker, w);
                if (place) AvLay.Place(kicker.rectTransform, x, y, w, kh);
                y += kh + 2f;
                float titleH = AvText.Height(title, w);
                if (place) AvLay.Place(title.rectTransform, x, y, w, titleH);
                y += titleH + 8f;

                float cx = 0f, rowY = y;
                bool anyPill = false;
                foreach (Pill pill in pills)
                {
                    if (!pill.Visible) continue;
                    float pw = Mathf.Min(w, pill.Width);
                    if (cx > 0f && cx + pw > w) { cx = 0f; rowY += Pill.PillHeight + 6f; }
                    if (place) pill.Place(x + cx, rowY, pw);
                    cx += pw + 6f;
                    anyPill = true;
                }
                if (anyPill) y = rowY + Pill.PillHeight + 10f;

                float clockW = Mathf.Ceil(AvText.Width(clock)) + 4f;
                if (place)
                {
                    AvLay.Place(clock.rectTransform, x, y, clockW, 32f);
                    AvLay.Place(clockNote.rectTransform, x + clockW + 8f, y + 12f, Mathf.Max(40f, w - clockW - 8f), 15f);
                    AvLay.Place(clockTag.rectTransform, x, y + 12f, w, 15f);
                }
                y += 32f + 4f;
                if (place) AvLay.Place((RectTransform)progress.transform, x, y, w, 4f);
                y += 4f + 10f;

                if (consequence.text.Length > 0)
                {
                    float qh = AvText.Height(consequence, w);
                    if (place) AvLay.Place(consequence.rectTransform, x, y, w, qh);
                    y += qh + 4f;
                }
                if (flavor.text.Length > 0)
                {
                    float fh = AvText.Height(flavor, w);
                    if (place) AvLay.Place(flavor.rectTransform, x, y, w, fh);
                    y += fh + 6f;
                }
                for (int i = 0; i < steps.Length; i++)
                {
                    bool active = scripted && i < stepCount && steps[i].text.Length > 0;
                    if (place) steps[i].gameObject.SetActive(active);
                    if (!active) continue;
                    float sh = AvText.Height(steps[i], w);
                    if (place) AvLay.Place(steps[i].rectTransform, x, y, w, sh);
                    y += sh + 2f;
                }
                float total2 = y + 8f;
                if (place) AvLay.Place(rail.rectTransform, 0f, 0f, 3f, total2);
                return total2;
            }

            public override void Restyle()
            {
                AvStyle c = AvStyleHost.FuiStyle("card");
                frame.Paint(AvStyleHost.Resolve(c.Background, AvTheme.Surface), AvStyleHost.Resolve(c.Border, AvTheme.Hairline));
                rail.color = AvStyleHost.FuiColor(AvStates.Class(tierState), AvTheme.RailInfo);
                tagBack.color = AvStyleHost.FuiColor("ground", AvTheme.Ground).WithAlpha(0.88f);
                tag.color = TextColor(tierState);
                kicker.color = AvStyleHost.FuiColor("ink-dim", AvTheme.Dim);
                title.color = AvStyleHost.FuiColor("ink", AvTheme.TextPrimary);
                clock.color = clockState == AvState.Inert ? AvStyleHost.FuiColor("ink", AvTheme.TextPrimary) : TextColor(clockState);
                clockNote.color = AvStyleHost.FuiColor("ink-dim", AvTheme.Dim);
                clockTag.color = TextColor(clockState);
                consequence.color = AvStyleHost.FuiColor("ink", AvTheme.TextPrimary);
                flavor.color = AvStyleHost.FuiColor("ink-dim", AvTheme.Dim);
                foreach (TMP_Text step in steps) step.color = AvStyleHost.FuiColor("ink-dim", AvTheme.Dim);
                foreach (Pill pill in pills) pill.Restyle();
                progress.Track = AvStyleHost.FuiColor("hairline", AvTheme.Hairline);
                progress.FillColor = progress.FillEnd = AvStyleHost.FuiColor(
                    AvStates.Class(progressState == AvState.Inert ? AvState.Info : progressState), AvTheme.RailInfo);
                progress.SetVerticesDirty();
                plate.Restyle();
            }
        }

        /// <summary>The DESK page's current-or-last case file: a poster plus title, meta and body copy.</summary>
        private sealed class EventCaseFilePart : AvPart
        {
            private const float PlateSize = 64f;
            private readonly AvFrame frame;
            private readonly Image rail;
            private readonly EventPlateArt plate;
            private readonly TMP_Text title, meta, body;
            private AvState tierState = AvState.Inert;

            public EventCaseFilePart(RectTransform parent)
            {
                Rect = AvLay.Child(parent, "CaseFile");
                frame = AvFrame.Add(Rect, "Frame", AvChamfer.Diagonal(6f));
                AvLay.Fill(frame.rectTransform);
                rail = AvLay.Solid(Rect, "Rail", Color.clear);
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
                plate.Bind(poster, icon, TextColor(tierState));
                Restyle();
                Changed();
            }

            public override float Measure(float width) => Arrange(width, false);

            public override void Place(AvSlot s)
            {
                base.Place(s);
                Arrange(s.W, true);
            }

            private float Arrange(float width, bool place)
            {
                float x = 12f, textX = x + PlateSize + 12f, textW = width - textX - 12f;
                float th = AvText.Height(title, textW);
                float mh = meta.text.Length > 0 ? AvText.Height(meta, textW) : 0f;
                float head = Mathf.Max(PlateSize, th + 4f + mh);
                float bodyW = width - 24f;
                float bh = body.text.Length > 0 ? AvText.Height(body, bodyW) : 0f;
                float total = 12f + head + (bh > 0f ? 8f + bh : 0f) + 12f;
                if (place)
                {
                    AvLay.Place(rail.rectTransform, 0f, 0f, 3f, total);
                    AvLay.Place(plate.Root, x, 12f, PlateSize, PlateSize);
                    plate.Layout(PlateSize, PlateSize);
                    AvLay.Place(title.rectTransform, textX, 12f, textW, th);
                    AvLay.Place(meta.rectTransform, textX, 12f + th + 4f, textW, mh);
                    AvLay.Place(body.rectTransform, x, 12f + head + 8f, bodyW, bh);
                }
                return total;
            }

            public override void Restyle()
            {
                AvStyle c = AvStyleHost.FuiStyle("card");
                frame.Paint(AvStyleHost.Resolve(c.Background, AvTheme.Surface), AvStyleHost.Resolve(c.Border, AvTheme.Hairline));
                rail.color = AvStyleHost.FuiColor(AvStates.Class(tierState), AvTheme.RailInfo);
                title.color = AvStyleHost.FuiColor("ink", AvTheme.TextPrimary);
                meta.color = TextColor(tierState);
                body.color = AvStyleHost.FuiColor("ink-dim", AvTheme.Dim);
                plate.Restyle();
            }
        }
    }
}
