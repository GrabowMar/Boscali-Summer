using BoscaliSummer.Framework.Contracts;
using NOAvionics;
using NOAvionics.Ui;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Features.Events.Presentation
{
    /// <summary>
    /// The two card shapes the feed uses.
    ///
    /// <para>The active event is a media card read top to bottom in one order: the 16:9 plate
    /// (the event's poster when the player has drawn one, a generated stripe plate over the
    /// category mark when not — never a blank rectangle), the title, the category and scope,
    /// the countdown, the live effect, its plain-words consequence, the flavor copy and the
    /// scripted beats. The title is the headline and the effect is the figure; the countdown
    /// is quiet row text on a state rail, so it never competes with either. Every state the
    /// card can hold, such as ENDING, is carried by words and a rail, never by colour alone.</para>
    ///
    /// <para>Bars are placed by width, not by <c>fillAmount</c>: Unity's filled image type
    /// draws a full quad without a sprite, which is the module's oldest layout lesson.</para>
    /// </summary>
    internal sealed partial class EventsMfdPanel
    {
        /// <summary>One 16:9 image plate: poster or generated stripe plate, plus the category mark.</summary>
        private sealed class PlateUi
        {
            private readonly RectTransform root;
            private readonly Image stripes;
            private readonly Image art;
            private readonly EventGlyph mark;

            private PlateUi(RectTransform root, Image stripes, Image art, EventGlyph mark)
            {
                this.root = root;
                this.stripes = stripes;
                this.art = art;
                this.mark = mark;
            }

            /// <summary>The plate frame, so a row can move it without touching its children.</summary>
            public RectTransform Root => root;

            public static PlateUi Build(RectTransform parent, float x, float y, float width, float height,
                float markSize)
            {
                var rootObject = new GameObject("Plate", typeof(RectTransform), typeof(Image));
                var root = (RectTransform)rootObject.transform;
                root.SetParent(parent, false);
                Image back = rootObject.GetComponent<Image>();
                back.color = AvTheme.SurfaceInert;
                back.raycastTarget = false;
                rootObject.AddComponent<RectMask2D>();

                var stripeObject = new GameObject("Stripes", typeof(RectTransform), typeof(Image));
                Image stripes = stripeObject.GetComponent<Image>();
                stripes.transform.SetParent(root, false);
                stripes.sprite = EventPlate.Stripes();
                stripes.type = Image.Type.Tiled;
                stripes.raycastTarget = false;

                var artObject = new GameObject("Art", typeof(RectTransform), typeof(Image));
                Image art = artObject.GetComponent<Image>();
                art.transform.SetParent(root, false);
                art.preserveAspect = false;
                art.raycastTarget = false;

                var markObject = new GameObject("Mark", typeof(RectTransform), typeof(CanvasRenderer), typeof(EventGlyph));
                EventGlyph mark = markObject.GetComponent<EventGlyph>();
                markObject.transform.SetParent(root, false);
                mark.raycastTarget = false;

                AvKit.Place(root, new Rect(x, y, width, height));
                AvKit.Place(stripes.rectTransform, new Rect(0f, 0f, width, height));
                float imageWidth = Mathf.Max(width, height * 16f / 9f);
                float imageHeight = imageWidth * 9f / 16f;
                AvKit.Place(art.rectTransform, new Rect((width - imageWidth) * 0.5f,
                    (imageHeight - height) * 0.5f, imageWidth, imageHeight));
                AvKit.Place(mark.rectTransform, new Rect(width * 0.5f - markSize * 0.5f,
                    -height * 0.5f + markSize * 0.5f, markSize, markSize));

                AvKit.Outline(parent, new Rect(x, y, width, height), AvTheme.Frame.WithAlpha(0.6f));
                return new PlateUi(root, stripes, art, mark);
            }

            public void Bind(Sprite poster, int category, Color ink)
            {
                bool hasArt = poster != null;
                art.sprite = poster;
                art.enabled = hasArt;
                stripes.gameObject.SetActive(!hasArt);
                stripes.color = ink.WithAlpha(0.10f);
                mark.gameObject.SetActive(!hasArt);
                if (hasArt) return;
                mark.SetKind(GlyphKind(category));
                mark.color = ink.WithAlpha(0.85f);
            }
        }

        private sealed class ActiveEventCard
        {
            public const float PlainHeight = 366f;
            private const float CalmHeight = 236f;

            /// <summary>
            /// The height the page reserves for a scripted card at the beat cap, so the scroll
            /// viewport exists whenever a superevent can grow the page. The card's actual height
            /// follows the entry's own beat count and flavour copy.
            /// </summary>
            public const float ScriptedHeight = ScriptedBaseHeight + MaximumEventSteps * StepPitch;

            private const float PlateHeight = 132f;
            private const float TextX = 12f;
            private const float TitleY = -153f;
            private const float ScopeY = -184f;
            private const float CountdownY = -201f;
            private const float EffectY = -229f;
            private const float ConsequenceY = -260f;
            private const float FlavorY = -294f;
            private const float FlavorMinHeight = 42f;
            private const float FlavorMaxHeight = 66f;
            private const float StepTop = 347f;
            private const float StepPitch = 16f;
            private const float ScriptedBaseHeight = 368f;
            private const int MaximumSteps = EventsMfdPanel.MaximumEventSteps;

            private readonly float width;
            private readonly float originX;
            private readonly float originY;
            private readonly GameObject root;
            private readonly RectTransform rootRect;
            private readonly Image box;
            private readonly Image rail;
            private readonly PlateUi plate;
            private readonly TMP_Text posterTag;
            private readonly TMP_Text title;
            private readonly TMP_Text scope;
            private readonly Image countdownRail;
            private readonly TMP_Text countdown;
            private readonly TMP_Text effect;
            private readonly TMP_Text consequence;
            private readonly TMP_Text flavor;
            private readonly Image[] stepDot;
            private readonly TMP_Text[] stepClock;
            private readonly TMP_Text[] stepLabel;
            private readonly Image track;
            private readonly Image fill;
            private readonly Image[] timeTicks;

            private Color tierInk = AvTheme.Dim;
            private Color tierRail = AvTheme.RailInert;
            private float urgency;
            private bool ending;
            private bool critical;
            private bool scripted;
            private bool shapeSet;
            private bool calm;
            private int scriptedSteps;
            private float flavorExtra;

            public float Height { get; private set; } = PlainHeight;

            public ActiveEventCard(RectTransform parent, float x, float y, float cardWidth)
            {
                width = cardWidth;
                originX = x;
                originY = y;

                root = new GameObject("ActiveEventCard", typeof(RectTransform));
                rootRect = (RectTransform)root.transform;
                rootRect.SetParent(parent, false);
                box = AvStyled.Box(rootRect, new Rect(0f, 0f, width, PlainHeight), "card");
                rail = AvStyled.Rail(rootRect, new Rect(0f, 0f, 3f, PlainHeight), "locked");

                plate = PlateUi.Build(rootRect, TextX, -8f, width - TextX * 2f, PlateHeight, 30f);
                AvKit.Panel(rootRect, new Rect(TextX + 8f, -17f, 148f, 20f),
                    AvTheme.Ground.WithAlpha(.86f)).raycastTarget = false;
                posterTag = AvStyled.Label(rootRect, new Rect(TextX + 16f, -19f, 136f, 16f),
                    "THEATER / LIVE", "section-title-note");

                title = AvStyled.Label(rootRect,
                    new Rect(TextX, TitleY, width - TextX * 2f, 27f), "", "page-title");
                title.enableAutoSizing = true;
                title.fontSizeMin = AvTokens.FontLead;
                scope = AvStyled.Label(rootRect, new Rect(TextX, ScopeY, width - TextX * 2f, 13f), "",
                    "section-title-note");
                countdownRail = AvKit.Panel(rootRect, new Rect(TextX, CountdownY - 1f, 3f, 12f),
                    AvTheme.RailInert);
                countdownRail.raycastTarget = false;
                countdown = AvStyled.Label(rootRect,
                    new Rect(TextX + 10f, CountdownY, width - TextX * 2f - 10f, 16f),
                    "", "row-value", align: TextAlignmentOptions.MidlineLeft);

                effect = AvStyled.Label(rootRect, new Rect(TextX, EffectY, width - TextX * 2f, 22f),
                    "", "metric-value");
                effect.fontSize = AvTokens.FontTitle;
                consequence = AvStyled.Label(rootRect, new Rect(TextX, ConsequenceY, width - TextX * 2f, 28f),
                    "", "row-main");
                flavor = AvStyled.Label(rootRect, new Rect(TextX, FlavorY, width - TextX * 2f, 42f),
                    "", "row-sub");

                stepDot = new Image[MaximumSteps];
                stepClock = new TMP_Text[MaximumSteps];
                stepLabel = new TMP_Text[MaximumSteps];
                for (int i = 0; i < MaximumSteps; i++)
                {
                    stepDot[i] = AvKit.Panel(rootRect, new Rect(0f, 0f, 6f, 6f), AvTheme.Dim);
                    stepDot[i].raycastTarget = false;
                    stepClock[i] = AvStyled.Label(rootRect, new Rect(0f, 0f, 46f, 14f), "", "section-title-note");
                    stepLabel[i] = AvStyled.Label(rootRect, new Rect(0f, 0f, width - TextX - 78f, 14f),
                        "", "row-sub");
                    stepDot[i].gameObject.SetActive(false);
                    stepClock[i].gameObject.SetActive(false);
                    stepLabel[i].gameObject.SetActive(false);
                }

                var barArea = new Rect(8f, -(PlainHeight - 10f), width - 16f, 3f);
                track = AvKit.Panel(rootRect, barArea, AvTheme.SurfaceInert);
                track.raycastTarget = false;
                fill = AvKit.Panel(rootRect, new Rect(barArea.x, barArea.y, 0f, barArea.height), AvTheme.Dim);
                fill.raycastTarget = false;
                timeTicks = new Image[3];
                for (int i = 0; i < timeTicks.Length; i++)
                    timeTicks[i] = AvKit.Rule(rootRect,
                        new Rect(barArea.x + barArea.width * (i + 1f) / 4f,
                            barArea.y - 1f, 1f, 5f), AvTheme.Frame);

                ApplyShape(false, 0, force: true);
            }

            public void Bind(ActiveEventView view, string effectText, Color effectColor, string consequenceText)
            {
                if (!root.activeSelf) root.SetActive(true);
                calm = false;
                ApplyShape(view.IsSuper, view.Steps.Count, force: true);
                tierInk = TierInk(view.Tier);
                tierRail = TierRail(view.Tier);
                urgency = 0f;
                ending = false;
                critical = false;

                plate.Bind(EventArtCache.Get(view.IconKey,
                    view.IsSuper ? "tier_super" : "tier_medium"), CategoryOf(view.Category), tierInk);
                posterTag.text = "THEATER / " + TierShort(view.Tier);

                scope.gameObject.SetActive(true);
                countdown.gameObject.SetActive(true);
                countdownRail.gameObject.SetActive(true);
                effect.gameObject.SetActive(true);
                flavor.gameObject.SetActive(true);
                AvKit.Place(consequence.rectTransform,
                    new Rect(TextX, ConsequenceY, width - TextX * 2f, 28f));
                title.text = view.Title.ToUpperInvariant();
                scope.text = view.Category + "  ·  " + view.Target;
                scope.color = AvTheme.Dim;
                effect.text = effectText;
                effect.color = effectColor;
                consequence.text = consequenceText;
                SetFlavor(view.FlavorText);
                rail.color = tierRail;
                countdownRail.color = AvTheme.RailInert;
                TickPulse();
            }

            public void BindPlaceholder(string noteText)
            {
                if (!root.activeSelf) root.SetActive(true);
                calm = true;
                flavorExtra = 0f;
                ApplyShape(false, 0, force: true);
                tierInk = AvTheme.Dim;
                tierRail = AvTheme.RailInert;
                urgency = 0f;
                ending = false;
                critical = false;

                plate.Bind(null, 0, AvTheme.Dim);
                posterTag.text = "THEATER / STANDBY";
                title.text = "THE THEATER IS QUIET";
                scope.gameObject.SetActive(false);
                effect.gameObject.SetActive(false);
                flavor.gameObject.SetActive(false);
                countdown.gameObject.SetActive(false);
                countdownRail.gameObject.SetActive(false);
                consequence.text = noteText;
                AvKit.Place(consequence.rectTransform,
                    new Rect(TextX, -184f, width - TextX * 2f, 38f));
                countdown.text = "";
                countdownRail.color = AvTheme.RailInert;
                rail.color = AvTheme.RailInert;

                for (int i = 0; i < MaximumSteps; i++) SetStep(i, false, "", "", AvTheme.Dim);
                SetProgress(0f, AvTheme.RailInert);
            }

            /// <summary>
            /// A scripted event needs the beat rows, an ordinary one does not; the card's height
            /// and every placement that depends on it follow from the entry's own beat count and
            /// the flavour copy's measured height.
            /// </summary>
            private void ApplyShape(bool isScripted, int stepCount, bool force)
            {
                int steps = Mathf.Clamp(stepCount, 0, MaximumSteps);
                if (!force && shapeSet && scripted == isScripted && scriptedSteps == steps) return;
                shapeSet = true;
                scripted = isScripted;
                scriptedSteps = steps;
                float shift = flavorExtra;
                Height = calm ? CalmHeight :
                    isScripted ? ScriptedBaseHeight + steps * StepPitch + shift
                    : PlainHeight + shift;
                float h = Height;

                AvKit.Place(rootRect, new Rect(originX, originY, width, h));
                AvKit.Place(box.rectTransform, new Rect(0f, 0f, width, h));
                AvKit.Place(rail.rectTransform, new Rect(0f, 0f, 3f, h));

                for (int i = 0; i < MaximumSteps; i++)
                {
                    if (!isScripted) continue;
                    float rowY = -StepTop - shift - i * StepPitch;
                    AvKit.Place(stepDot[i].rectTransform, new Rect(TextX, rowY + 5f, 6f, 6f));
                    AvKit.Place(stepClock[i].rectTransform, new Rect(TextX + 14f, rowY, 46f, 14f));
                    AvKit.Place(stepLabel[i].rectTransform, new Rect(TextX + 66f, rowY, width - TextX - 78f, 14f));
                }

                var barArea = new Rect(8f, -(h - 10f), width - 16f, 3f);
                AvKit.Place(track.rectTransform, barArea);
                AvKit.Place(fill.rectTransform, new Rect(barArea.x, barArea.y, 0f, barArea.height));
                for (int i = 0; i < timeTicks.Length; i++)
                    AvKit.Place(timeTicks[i].rectTransform,
                        new Rect(barArea.x + barArea.width * (i + 1f) / 4f,
                            barArea.y - 1f, 1f, 5f));
            }

            /// <summary>
            /// Writes the copy and gives the block the height it measured, growing the card and
            /// shifting the beats down with it. The copy is never ellipsised.
            /// </summary>
            private void SetFlavor(string copy)
            {
                flavor.text = copy ?? string.Empty;
                float measured = flavor.GetPreferredValues(flavor.text, width - TextX * 2f, 0f).y;
                float height = Mathf.Clamp(Mathf.Ceil(measured), FlavorMinHeight, FlavorMaxHeight);
                AvKit.Place(flavor.rectTransform, new Rect(TextX, FlavorY, width - TextX * 2f, height));
                float extra = height - FlavorMinHeight;
                if (Mathf.Abs(extra - flavorExtra) < 0.5f) return;
                flavorExtra = extra;
                ApplyShape(scripted, scriptedSteps, force: true);
            }

            public void SetScript(ActiveEventView view, float start, float now, Color railColor)
            {
                int count = view != null ? view.Steps.Count : 0;
                for (int i = 0; i < MaximumSteps; i++)
                {
                    if (i >= count)
                    {
                        SetStep(i, false, "", "", AvTheme.Dim);
                        continue;
                    }

                    ActiveEventStep step = view.Steps[i];
                    if (!view.TargetResolved)
                    {
                        SetStep(i, true, "T+" + StepClock(step.AtSeconds),
                            "[CANCELLED] " + step.Label, AvTheme.Dim);
                        continue;
                    }
                    bool fired = now >= start + step.AtSeconds;
                    SetStep(i, true,
                        "T+" + StepClock(step.AtSeconds),
                        (fired ? "[DONE] " : "[NEXT] ") + step.Label,
                        fired ? AvTheme.Dim : railColor);
                }
            }

            private void SetStep(int index, bool active, string time, string label, Color color)
            {
                stepDot[index].gameObject.SetActive(active);
                stepClock[index].gameObject.SetActive(active);
                stepLabel[index].gameObject.SetActive(active);
                if (!active) return;
                stepDot[index].color = color;
                stepClock[index].text = time;
                stepClock[index].color = color;
                stepLabel[index].text = label;
                stepLabel[index].color = color;
            }

            /// <summary>
            /// The clock is text plus a rail: "ENDING · ENDS 0:52" and the rail colour that
            /// goes with it, so the alarm never depends on the colour being seen.
            /// </summary>
            public void SetClock(string text, bool isEnding, bool isCritical)
            {
                ending = isEnding;
                critical = isCritical;
                countdown.text = isEnding ? "ENDING · " + text : text;
            }

            /// <summary>0 well before the end, 1 at the end: the countdown starts to breathe.</summary>
            public void SetUrgency(float value) => urgency = Mathf.Clamp01(value);

            /// <summary>Called every visible frame; the only per-frame work on this screen.</summary>
            public void TickPulse()
            {
                Color baseColor = critical ? AvTheme.RailDanger
                    : ending ? AvTheme.RailCaution
                    : urgency > 0.85f ? AvTheme.RailDanger
                    : urgency > 0.5f ? AvTheme.RailCaution
                    : tierInk;
                baseColor.a = 1f;
                countdown.color = baseColor;
                countdownRail.color = critical ? AvTheme.RailDanger
                    : ending ? AvTheme.RailCaution
                    : urgency > 0.5f ? AvTheme.RailCaution
                    : AvTheme.RailInert;
            }

            /// <summary>Width-driven fill; see the class note about Unity's filled images.</summary>
            public void SetProgress(float fraction, Color color)
            {
                fill.color = color;
                fill.rectTransform.sizeDelta = new Vector2(
                    Mathf.Clamp01(fraction) * (width - 16f), fill.rectTransform.sizeDelta.y);
            }

            private static string StepClock(int seconds)
            {
                int total = Mathf.Max(0, seconds);
                return (total / 60) + ":" + (total % 60).ToString("00");
            }
        }

        private sealed class HistoryCard
        {
            private const float NormalHeight = 64f;
            private const float TallThreshold = 80f;
            private const float ThumbWidth = 40f;
            private const float ThumbHeight = 22f;

            private readonly GameObject root;
            private readonly RectTransform rootRect;
            private readonly Image box;
            private readonly Image rail;
            private readonly PlateUi plate;
            private readonly Image chipFill;
            private readonly TMP_Text chip;
            private readonly TMP_Text title;
            private readonly TMP_Text meta;
            private readonly TMP_Text help;
            private readonly TMP_Text clock;
            private readonly Image effectFill;
            private readonly TMP_Text effect;

            public HistoryCard(RectTransform parent)
            {
                root = new GameObject("HistoryCard", typeof(RectTransform));
                rootRect = (RectTransform)root.transform;
                rootRect.SetParent(parent, false);
                box = AvStyled.Box(rootRect, new Rect(0f, 0f, 438f, NormalHeight), "row");
                rail = AvStyled.Rail(rootRect, new Rect(0f, -6f, 3f, NormalHeight - 12f), "locked");

                plate = PlateUi.Build(rootRect, 10f, -8f, ThumbWidth, ThumbHeight, 12f);

                chipFill = AvStyled.Box(rootRect, new Rect(58f, -9f, 56f, 16f), "chip", "inert");
                chip = AvStyled.Label(rootRect, new Rect(58f, -9f, 56f, 16f), "", "chip", "inert",
                    align: TextAlignmentOptions.Center);

                title = AvStyled.Label(rootRect, new Rect(122f, -8f, 212f, 18f), "", "row-name");
                clock = AvStyled.Label(rootRect, new Rect(338f, -8f, 88f, 16f), "", "row-value-unit",
                    align: TextAlignmentOptions.MidlineRight);
                meta = AvStyled.Label(rootRect, new Rect(58f, -29f, 190f, 13f), "", "row-sub");

                effectFill = AvStyled.Box(rootRect, new Rect(270f, -28f, 156f, 17f), "chip", "inert");
                effect = AvStyled.Label(rootRect, new Rect(270f, -28f, 156f, 17f), "", "chip", "inert",
                    align: TextAlignmentOptions.Center);

                help = AvStyled.Label(rootRect, new Rect(60f, -48f, 318f, 30f), "", "row-sub");
                help.gameObject.SetActive(false);
            }

            /// <summary>
            /// Width follows the page column, so the row is re-placed on every layout pass.
            /// A tall card is the empty feed's fill: the section stretches to the bottom of
            /// the body and its message centres, so the lower half is never dead space.
            /// </summary>
            public void Place(float x, float y, float width, float height)
            {
                AvKit.Place(rootRect, new Rect(x, y, width, height));
                AvKit.Place(box.rectTransform, new Rect(0f, 0f, width, height));
                float inset = height > TallThreshold ? 14f : 6f;
                AvKit.Place(rail.rectTransform, new Rect(0f, -inset, 3f, height - inset * 2f));

                float top = height > TallThreshold ? -(height * 0.5f) + 40f : -8f;
                AvKit.Place(plate.Root, new Rect(10f, top, ThumbWidth, ThumbHeight));
                PlaceChip(new Rect(58f, top - 1f, 56f, 16f), chipFill, chip);
                AvKit.Place(title.rectTransform, new Rect(122f, top, width - 222f, 18f));
                AvKit.Place(clock.rectTransform, new Rect(width - 96f, top, 88f, 16f));
                AvKit.Place(meta.rectTransform, new Rect(58f, top - 21f, width - 230f, 13f));
                PlaceChip(new Rect(width - 168f, top - 20f, 156f, 17f), effectFill, effect);
                AvKit.Place(help.rectTransform, new Rect(12f, top - 48f, width - 24f, 30f));
            }

            private static void PlaceChip(Rect area, Image fill, TMP_Text label)
            {
                AvKit.Place(fill.rectTransform, area);
                AvKit.Place(label.rectTransform, area);
            }

            public void Bind(ActiveEventView view, Color ink, Color railColor, Color effectColor)
            {
                if (!root.activeSelf) root.SetActive(true);
                if (help.gameObject.activeSelf) help.gameObject.SetActive(false);

                plate.Bind(EventArtCache.Get(view.IconKey,
                    view.IsSuper ? "tier_super" : "tier_medium"), CategoryOf(view.Category), ink);

                chip.text = TierShort(view.Tier);
                chip.color = ink;
                chipFill.color = ink.WithAlpha(0.12f);

                title.text = view.Title.ToUpperInvariant();
                meta.text = view.Category + "  ·  " + ShortTarget(view.Target);
                effect.text = IsNeutral(view.EffectSummary) && !string.IsNullOrEmpty(view.TempoSummary)
                    ? view.TempoSummary : view.EffectSummary;
                effect.color = effectColor;
                effectFill.color = effectColor.WithAlpha(0.10f);
                rail.color = railColor;
            }

            /// <summary>The empty feed keeps its helpful copy and chip, in a full-height card.</summary>
            public void BindEmpty()
            {
                if (!root.activeSelf) root.SetActive(true);
                if (!help.gameObject.activeSelf) help.gameObject.SetActive(true);

                plate.Bind(null, 0, AvTheme.Dim);
                chip.text = "STANDBY";
                chip.color = AvTheme.Dim;
                chipFill.color = AvTheme.Dim.WithAlpha(0.08f);

                title.text = "THE FEED IS EMPTY";
                meta.text = "COMPLETED EVENTS APPEAR HERE";
                help.text = "NO COMPLETED EVENTS YET — THE FEED ROLLS ONE EVENT AT A TIME.";
                effect.text = "AWAITING FIRST EVENT";
                effect.color = AvTheme.Dim;
                effectFill.color = AvTheme.Dim.WithAlpha(0.08f);
                rail.color = AvTheme.RailInert;
                clock.text = "";
            }

            public void SetClock(string text)
            {
                clock.text = text;
                clock.color = AvTheme.Dim;
            }

            public void Hide() => root.SetActive(false);
        }
    }
}
