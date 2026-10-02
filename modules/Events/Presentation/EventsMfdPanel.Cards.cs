using BoscaliSummer.Core.Contracts;
using NOAvionics;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Modules.Events.Presentation
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
    /// <para><see cref="EventHeroPart"/> is the live dispatch: the poster with its tier tag, the event name, one
    /// effect line, the plain-words consequence and the scripted beats (the countdown is the LEFT metric tile).
    /// It has a calm standby banner for a quiet theater that grows into spare page height. Every state it can show (ENDING, a scripted beat
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

        /// <summary>Solid slab colours (same tokens as <see cref="AvSlab"/>): filled tag, dark ink.</summary>
        internal static Color SlabBack(AvState state) =>
            AvStyleHost.Resolve(AvStyleHost.FuiStyle("slab " + AvStates.Class(state)).Background,
                AvStyleHost.FuiColor(AvStates.Class(state), AvTheme.RailInfo));

        internal static Color SlabInk() => AvStyleHost.Resolve(AvStyleHost.FuiStyle("slab").Color, Color.black);

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
            private AvIcon glyphIcon = AvIcon.Coins;
            private float shownMark = -1f;

            /// <summary>How much larger than its base size the category mark is drawn (a big standby banner scales it up).</summary>
            public float MarkScale = 1f;

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
                float want = markSize * Mathf.Max(0.1f, MarkScale);
                float size = Mathf.Min(want, Mathf.Min(w, h) * 0.6f);
                if (poster == null && Mathf.Abs(want - shownMark) > 0.5f) { shownMark = want; AvIcons.Set(mark, glyphIcon, want); }
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
                glyphIcon = categoryGlyph;
                shownMark = markSize * Mathf.Max(0.1f, MarkScale);
                AvIcons.Set(mark, categoryGlyph, shownMark);
                mark.color = ink.WithAlpha(0.85f);
            }

            public void Restyle() => back.color = AvStyleHost.FuiColor("surface-inert", AvTheme.SurfaceInert);
        }

        /// <summary>
        /// The active dispatch, the page's hero. Active: the poster on the left with the tier tag on it, and
        /// beside it the category and target, the event name and one effect line; below, the consequence flag and
        /// (for a scripted superevent) the beat log. The countdown and its bar live in the LEFT metric tile, and
        /// the price numbers in COST / RESET, so nothing is said twice. Calm: a designed standby banner (stripe
        /// plate, large radar mark) that takes the page's spare height, never an empty box.
        /// </summary>
        private sealed class EventHeroPart : AvPart
        {
            private const float PlateW = 132f, PlateMin = 96f;
            private const float CalmPlate = 52f;

            private readonly AvFrame frame;
            private readonly Image rail, tagBack;
            private readonly EventPlateArt plate;
            private readonly TMP_Text tag, kicker, title, effectLine, consequence;
            private readonly TMP_Text[] steps;

            private AvState tierState = AvState.Inert;
            private AvState effectState = AvState.Inert;
            private bool calm = true;
            private bool scripted;
            private int stepCount;

            public EventHeroPart(RectTransform parent)
            {
                Rect = AvLay.Child(parent, "EventHero");
                frame = AvFrame.Add(Rect, "Frame", AvChamfer.Diagonal(8f));
                AvLay.Fill(frame.rectTransform);
                frame.raycastTarget = true;   // the whole card carries the dispatch's plain-words help
                rail = AvLay.Solid(Rect, "Rail", Color.clear);
                plate = new EventPlateArt(Rect, 30f);
                tagBack = AvLay.Solid(plate.Root, "TagBack", Color.clear);
                tag = AvText.Make(tagBack.rectTransform, "Tag", AvTextRole.Micro, "", TextAlignmentOptions.MidlineLeft);
                kicker = AvText.Make(Rect, "Kicker", AvTextRole.Micro, "", TextAlignmentOptions.TopLeft, true);
                title = AvText.Make(Rect, "Title", AvTextRole.Title, "", TextAlignmentOptions.TopLeft, true);
                effectLine = AvText.Make(Rect, "Effect", AvTextRole.DataSmall, "", TextAlignmentOptions.TopLeft, true);
                consequence = AvText.Make(Rect, "Consequence", AvTextRole.Prose, "", TextAlignmentOptions.TopLeft, true);
                steps = new TMP_Text[MaximumEventSteps];
                for (int i = 0; i < steps.Length; i++)
                    steps[i] = AvText.Make(Rect, "Step " + i, AvTextRole.DataSmall, "", TextAlignmentOptions.TopLeft, true);
                ApplyMode(true);
                Restyle();
            }

            /// <summary>Show the calm standby banner or the full active card. Only the calm banner grows.</summary>
            private void ApplyMode(bool isCalm)
            {
                calm = isCalm;
                Grow = isCalm ? 1f : 0f;
                tagBack.gameObject.SetActive(!isCalm);
                kicker.gameObject.SetActive(!isCalm);
                effectLine.gameObject.SetActive(!isCalm);
            }

            /// <summary>Binds a live dispatch. The effect line and the script refresh separately, at refresh cadence.</summary>
            public void BindActive(ActiveEventView view, AvState tier, string consequenceText, string helpText)
            {
                ApplyMode(false);
                tierState = tier;
                plate.Bind(EventArtCache.Get(view.IconKey, view.IsSuper ? "tier_super" : "tier_medium"),
                    CategoryIcon(view.Category), TextColor(tierState));
                tag.text = AvStates.Glyph(tierState) + view.Tier;
                kicker.text = view.Category + " · " + view.Target;
                title.text = view.Title.ToUpperInvariant();
                consequence.text = consequenceText ?? "";
                AvHelpTip.Attach(frame.gameObject, (helpText + " " + view.FlavorText).Trim());
                Restyle();
                Changed();
            }

            public void BindCalm(string note)
            {
                ApplyMode(true);
                tierState = AvState.Inert;
                effectState = AvState.Inert;
                scripted = false;
                stepCount = 0;
                foreach (TMP_Text step in steps) step.text = "";
                effectLine.text = "";
                plate.Bind(null, AvIcon.Radar2, TextColor(AvState.Inert));
                title.text = "THEATER QUIET";
                consequence.text = note ?? "";
                AvHelpTip.Attach(frame.gameObject,
                    "THEATER QUIET: no world event is running. The director rolls the next one from the theater's ground balance; the rings below show how close it is to escalating.");
                Restyle();
                Changed();
            }

            /// <summary>The one effect line (price, tempo, side). It carries a word as well as a colour (R1).</summary>
            public void SetEffect(string text, AvState state)
            {
                bool grew = effectLine.text != (text ?? "");
                effectLine.text = text ?? "";
                if (state != effectState) { effectState = state; effectLine.color = TextColor(effectState); }
                if (grew) Changed();
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

            public override float Measure(float width) => Arrange(width, false, 0f);

            public override void Place(AvSlot s)
            {
                base.Place(s);
                Arrange(s.W, true, s.H);
            }

            private float Arrange(float width, bool place, float slotHeight)
            {
                float x = 12f, w = width - 24f, y = 12f;
                if (calm)
                {
                    float textX = x + CalmPlate + 12f, textW = w - CalmPlate - 12f;
                    float th = AvText.Height(title, textW);
                    float ch = consequence.text.Length > 0 ? AvText.Height(consequence, textW) : 0f;
                    float block = th + (ch > 0f ? 4f + ch : 0f);
                    float total = 12f + Mathf.Max(CalmPlate, block) + 12f;
                    if (place && slotHeight > total + 40f)
                    {
                        // Spare height: a full-width standby banner with the words underneath it.
                        float fth = AvText.Height(title, w);
                        float fch = consequence.text.Length > 0 ? AvText.Height(consequence, w) : 0f;
                        float below = fth + (fch > 0f ? 4f + fch : 0f);
                        float plateH = Mathf.Max(CalmPlate, slotHeight - 24f - below - 10f);
                        AvLay.Place(rail.rectTransform, 0f, 0f, 3f, slotHeight);
                        AvLay.Place(plate.Root, x, y, w, plateH);
                        plate.MarkScale = Mathf.Clamp(plateH / 90f, 1f, 3f);
                        plate.Layout(w, plateH);
                        float by = y + plateH + 10f;
                        AvLay.Place(title.rectTransform, x, by, w, fth);
                        AvLay.Place(consequence.rectTransform, x, by + fth + 4f, w, fch);
                        return total;
                    }
                    if (place)
                    {
                        AvLay.Place(rail.rectTransform, 0f, 0f, 3f, total);
                        AvLay.Place(plate.Root, x, y, CalmPlate, CalmPlate);
                        plate.MarkScale = 1f;
                        plate.Layout(CalmPlate, CalmPlate);
                        AvLay.Place(title.rectTransform, textX, y, textW, th);
                        AvLay.Place(consequence.rectTransform, textX, y + th + 4f, textW, ch);
                    }
                    return total;
                }

                float tx = x + PlateW + 12f, tw = width - tx - 12f;
                float kh = AvText.Height(kicker, tw);
                float titleH = AvText.Height(title, tw);
                float eh = effectLine.text.Length > 0 ? AvText.Height(effectLine, tw) : 0f;
                float text = kh + 2f + titleH + (eh > 0f ? 6f + eh : 0f);
                float headH = Mathf.Max(PlateMin, text);
                if (place)
                {
                    AvLay.Place(plate.Root, x, y, PlateW, headH);
                    plate.MarkScale = 1f;
                    plate.Layout(PlateW, headH);
                    float tagW = Mathf.Ceil(AvText.Width(tag)) + 16f;
                    AvLay.Place(tagBack.rectTransform, 6f, 6f, Mathf.Min(tagW, PlateW - 12f), 20f);
                    AvLay.Place(tag.rectTransform, 8f, 0f, Mathf.Min(tagW, PlateW - 12f) - 10f, 20f);
                    AvLay.Place(kicker.rectTransform, tx, y, tw, kh);
                    AvLay.Place(title.rectTransform, tx, y + kh + 2f, tw, titleH);
                    AvLay.Place(effectLine.rectTransform, tx, y + kh + 2f + titleH + 6f, tw, eh);
                }
                y += headH;

                if (consequence.text.Length > 0)
                {
                    float qh = AvText.Height(consequence, w);
                    if (place) AvLay.Place(consequence.rectTransform, x, y + 8f, w, qh);
                    y += 8f + qh;
                }
                for (int i = 0; i < steps.Length; i++)
                {
                    bool active = scripted && i < stepCount && steps[i].text.Length > 0;
                    if (place) steps[i].gameObject.SetActive(active);
                    if (!active) continue;
                    float sh = AvText.Height(steps[i], w);
                    if (place) AvLay.Place(steps[i].rectTransform, x, y + 6f, w, sh);
                    y += 6f + sh;
                }
                float total2 = y + 12f;
                if (place) AvLay.Place(rail.rectTransform, 0f, 0f, 3f, total2);
                return total2;
            }

            public override void Restyle()
            {
                AvStyle c = AvStyleHost.FuiStyle("card");
                frame.Paint(AvStyleHost.Resolve(c.Background, AvTheme.Surface), AvStyleHost.Resolve(c.Border, AvTheme.Hairline));
                rail.color = AvStyleHost.FuiColor(AvStates.Class(tierState), AvTheme.RailInfo);
                tagBack.color = SlabBack(tierState);
                tag.color = SlabInk();
                kicker.color = AvStyleHost.FuiColor("ink-dim", AvTheme.Dim);
                title.color = AvStyleHost.FuiColor("ink", AvTheme.TextPrimary);
                effectLine.color = TextColor(effectState);
                consequence.color = AvStyleHost.FuiColor("ink", AvTheme.TextPrimary);
                foreach (TMP_Text step in steps) step.color = AvStyleHost.FuiColor("ink-dim", AvTheme.Dim);
                plate.Restyle();
            }
        }

        /// <summary>
        /// The DESK page's current-or-last case file. Natural size: a small poster beside title and meta, the body
        /// underneath. Given spare height it becomes a poster card: the poster on top grows and the words sit
        /// under it, centred in whatever is left.
        /// </summary>
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
                frame.raycastTarget = true;
                rail = AvLay.Solid(Rect, "Rail", Color.clear);
                plate = new EventPlateArt(Rect, 20f);
                title = AvText.Make(Rect, "Title", AvTextRole.Head, "", TextAlignmentOptions.TopLeft, true);
                meta = AvText.Make(Rect, "Meta", AvTextRole.Micro, "", TextAlignmentOptions.TopLeft, true);
                body = AvText.Make(Rect, "Body", AvTextRole.Prose, "", TextAlignmentOptions.TopLeft, true);
                Restyle();
            }

            /// <summary>Hover help shown in the console footer.</summary>
            public string Help { set => AvHelpTip.Attach(frame.gameObject, value); }

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

            public override float Measure(float width) => Arrange(width, false, 0f);

            public override void Place(AvSlot s)
            {
                base.Place(s);
                Arrange(s.W, true, s.H);
            }

            private float Arrange(float width, bool place, float slotHeight)
            {
                float x = 12f, textX = x + PlateSize + 12f, textW = width - textX - 12f;
                float th = AvText.Height(title, textW);
                float mh = meta.text.Length > 0 ? AvText.Height(meta, textW) : 0f;
                float head = Mathf.Max(PlateSize, th + 4f + mh);
                float bodyW = width - 24f;
                float bh = body.text.Length > 0 ? AvText.Height(body, bodyW) : 0f;
                float total = 12f + head + (bh > 0f ? 8f + bh : 0f) + 12f;
                if (!place) return total;

                if (slotHeight > total + 60f)
                {
                    // Poster card: full-width poster (never squarer than about 1.15:1, so the art is not sliced),
                    // then title, meta and body, the whole stack centred in the card.
                    float fth = AvText.Height(title, bodyW);
                    float fmh = meta.text.Length > 0 ? AvText.Height(meta, bodyW) : 0f;
                    float words = fth + (fmh > 0f ? 4f + fmh : 0f) + (bh > 0f ? 8f + bh : 0f);
                    float plateH = Mathf.Clamp(slotHeight - 24f - words - 10f, PlateSize, bodyW / 1.15f);
                    float stack = plateH + 10f + words;
                    float top = Mathf.Max(12f, (slotHeight - stack) * 0.5f);
                    AvLay.Place(rail.rectTransform, 0f, 0f, 3f, slotHeight);
                    AvLay.Place(plate.Root, x, top, bodyW, plateH);
                    plate.MarkScale = Mathf.Clamp(plateH / 60f, 1f, 3f);
                    plate.Layout(bodyW, plateH);
                    float y = top + plateH + 10f;
                    AvLay.Place(title.rectTransform, x, y, bodyW, fth);
                    y += fth + 4f;
                    AvLay.Place(meta.rectTransform, x, y, bodyW, fmh);
                    y += fmh + (fmh > 0f ? 4f : 0f) + 4f;
                    AvLay.Place(body.rectTransform, x, y, bodyW, bh);
                    return total;
                }

                AvLay.Place(rail.rectTransform, 0f, 0f, 3f, total);
                AvLay.Place(plate.Root, x, 12f, PlateSize, PlateSize);
                plate.MarkScale = 1f;
                plate.Layout(PlateSize, PlateSize);
                AvLay.Place(title.rectTransform, textX, 12f, textW, th);
                AvLay.Place(meta.rectTransform, textX, 12f + th + 4f, textW, mh);
                AvLay.Place(body.rectTransform, x, 12f + head + 8f, bodyW, bh);
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
