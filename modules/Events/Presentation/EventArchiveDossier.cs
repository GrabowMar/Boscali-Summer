using NOAvionics;
using System;
using BoscaliSummer.Modules.Events.Domain;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Modules.Events.Presentation
{
    /// <summary>
    /// An authored event's dossier. The hero is its poster inside plate brackets with the tier slab on it;
    /// beside it (or under it in a narrow pane) the three effect figures, each with a bar scaled against the
    /// widest swing or longest window in the catalog, and the tier / target / story boxes. Then the timed
    /// orders as a timeline, the desk note, the same story line's other dossiers and the file line.
    /// </summary>
    internal sealed class EventDossierPart : AvPart
    {
        private const int Steps = EventsMfdPanel.MaximumEventSteps;
        private const float EffectGap = 8f, StepX = 96f;

        private sealed class Effect
        {
            public TMP_Text Key, Value, Note, Low, High;
            public AvState State;
            public float From, To, X, Y, W;
            public bool Centred;
        }

        private sealed class Tag
        {
            public AvFrame Frame;
            public TMP_Text Key, Value;
        }

        private static float widestSwing, longestWindow;

        private readonly RecordHead head;
        private readonly EventsMfdPanel.EventPlateArt poster;
        private readonly AvQuadGraphic posterMarks, effectBars;
        private readonly Image tierBack, ordersLine, noteRule, fileRule;
        private readonly TMP_Text tier, ordersKey, ordersHint, ordersNone, noteKey, note, footnote, fileLine;
        private readonly Effect[] effects = new Effect[3];
        private readonly Tag[] tags = new Tag[3];
        private readonly Image[] nodes = new Image[Steps];
        private readonly TMP_Text[] stepTimes = new TMP_Text[Steps], stepLabels = new TMP_Text[Steps];
        private readonly RefStrip refs;
        private AvState tierState = AvState.Inert;
        private int stepCount;
        private bool placed;
        private AvSlot last;

        /// <summary>The record a cross-reference chip asks for.</summary>
        public Action<int> OpenRecord { set => refs.Picked = value; }

        public EventDossierPart(RectTransform parent)
        {
            Rect = AvLay.Child(parent, "EventDossier");
            head = new RecordHead(Rect);
            poster = new EventsMfdPanel.EventPlateArt(Rect, 34f);
            posterMarks = ArchiveUi.Quads(Rect, "PosterMarks");
            tierBack = AvLay.Solid(Rect, "TierBack", Color.clear);
            tier = AvText.Make(tierBack.rectTransform, "Tier", AvTextRole.Micro, "");
            effectBars = ArchiveUi.Quads(Rect, "EffectBars");
            for (int i = 0; i < effects.Length; i++)
                effects[i] = new Effect
                {
                    Key = AvText.Make(Rect, "EffectKey " + i, AvTextRole.Micro, ""),
                    Value = AvText.Make(Rect, "EffectValue " + i, AvTextRole.Display, ""),
                    Note = AvText.Make(Rect, "EffectNote " + i, AvTextRole.Micro, ""),
                    Low = AvText.Make(Rect, "EffectLow " + i, AvTextRole.Micro, ""),
                    High = AvText.Make(Rect, "EffectHigh " + i, AvTextRole.Micro, "", TextAlignmentOptions.MidlineRight),
                };
            for (int i = 0; i < tags.Length; i++)
            {
                var tag = new Tag { Frame = AvFrame.Add(Rect, "TagFrame " + i, AvChamfer.Diagonal(4f)) };
                tag.Key = AvText.Make(Rect, "TagKey " + i, AvTextRole.Micro, "");
                tag.Value = AvText.Make(Rect, "TagValue " + i, AvTextRole.Head, "", TextAlignmentOptions.TopLeft, true);
                tags[i] = tag;
            }
            ordersKey = AvText.Make(Rect, "OrdersKey", AvTextRole.Micro, "TIMED ORDERS");
            ordersHint = AvText.Make(Rect, "OrdersHint", AvTextRole.Micro, "MISSION CLOCK FROM THE START OF THE DISPATCH",
                TextAlignmentOptions.MidlineRight);
            ordersNone = AvText.Make(Rect, "OrdersNone", AvTextRole.ProseSmall,
                "No timed orders are attached. The dispatch takes effect at once and holds for its window.",
                TextAlignmentOptions.TopLeft, true);
            ordersLine = AvLay.Solid(Rect, "OrdersLine", Color.clear);
            for (int i = 0; i < Steps; i++)
            {
                nodes[i] = AvLay.Solid(Rect, "Node " + i, Color.clear);
                stepTimes[i] = AvText.Make(Rect, "StepTime " + i, AvTextRole.DataSmall, "");
                stepLabels[i] = AvText.Make(Rect, "StepLabel " + i, AvTextRole.Label, "", TextAlignmentOptions.TopLeft, true);
            }
            noteKey = AvText.Make(Rect, "NoteKey", AvTextRole.Micro, "DESK NOTE");
            noteRule = AvLay.Solid(Rect, "NoteRule", Color.clear);
            note = AvText.Make(Rect, "Note", AvTextRole.Prose, "", TextAlignmentOptions.TopLeft, true);
            footnote = AvText.Make(Rect, "Footnote", AvTextRole.ProseSmall,
                "Authored effects at standard strength; the live dispatch applies the host's effect setting.",
                TextAlignmentOptions.TopLeft, true);
            refs = new RefStrip(Rect);
            fileRule = AvLay.Solid(Rect, "FileRule", Color.clear);
            fileLine = AvText.Make(Rect, "FileLine", AvTextRole.Micro, "", TextAlignmentOptions.TopLeft, true);
            Restyle();
        }

        /// <summary>The catalog's widest price or reset swing and its longest window: what the effect bars are scaled against.</summary>
        private static void Scales()
        {
            if (longestWindow > 0f) return;
            foreach (EventDefinition e in EventCatalog.All)
            {
                widestSwing = Mathf.Max(widestSwing, Mathf.Abs(e.SupportCostMultiplier - 1f));
                widestSwing = Mathf.Max(widestSwing, Mathf.Abs(e.SupportCooldownMultiplier - 1f));
                longestWindow = Mathf.Max(longestWindow, e.DurationMaxSeconds);
            }
        }

        public void Bind(EventDefinition d, int item)
        {
            Scales();
            EventDefinition[] all = EventCatalog.All;
            string tierLabel = EventCatalog.TierLabel(d.Tier), category = EventCatalog.CategoryLabel(d.Category),
                target = EventCatalog.TargetLabel(d.Target);
            string place = ArchiveUi.Two(item + 1) + " OF " + ArchiveUi.Two(all.Length);
            tierState = EventsMfdPanel.TierState(tierLabel);
            head.Set("EV-" + ArchiveUi.Two(item + 1), d.Title.ToUpperInvariant(),
                "EVENT CATALOG  ·  " + category + " STORY LINE  ·  AIMED AT " + target,
                "EVENT DOSSIER", "DOSSIER " + place, tierState);
            poster.Bind(EventArtCache.Get(d.IconKey, d.IsSuper ? "tier_super" : "tier_medium"),
                EventsMfdPanel.CategoryIcon(category), AvInk.Dim);
            tier.text = AvStates.Glyph(tierState) + tierLabel;

            Multiplier(effects[0], "SUPPORT PRICE", d.SupportCostMultiplier, "COST DOWN", "COST UP");
            Multiplier(effects[1], "RESET CLOCK", d.SupportCooldownMultiplier, "FASTER", "SLOWER");
            Effect window = effects[2];
            window.Key.text = "WINDOW";
            window.Value.text = AvNum.Fixed(d.DurationMinSeconds / 60, 0) + "–" + AvNum.Fixed(d.DurationMaxSeconds / 60, 0) + " MIN";
            window.Note.text = "";
            window.State = AvState.Info;
            window.Centred = false;
            window.From = longestWindow > 0f ? d.DurationMinSeconds / longestWindow : 0f;
            window.To = longestWindow > 0f ? d.DurationMaxSeconds / longestWindow : 0f;
            window.Low.text = "0";
            window.High.text = AvNum.Fixed(longestWindow / 60f, 0) + " MIN";

            SetTag(0, "TIER", tierLabel);
            SetTag(1, "TARGET", target);
            SetTag(2, "STORY LINE", category);

            stepCount = Mathf.Min(d.Script.Length, Steps);
            for (int i = 0; i < Steps; i++)
            {
                bool on = i < stepCount;
                nodes[i].gameObject.SetActive(on);
                stepTimes[i].gameObject.SetActive(on);
                stepLabels[i].gameObject.SetActive(on);
                if (!on) continue;
                stepTimes[i].text = "T+" + AvNum.Clock(d.Script[i].AtSeconds);
                stepLabels[i].text = d.Script[i].Label;
            }
            ordersLine.gameObject.SetActive(stepCount > 1);
            ordersNone.gameObject.SetActive(stepCount == 0);
            note.text = d.FlavorText;

            refs.Begin("SAME STORY LINE  ·  " + category);
            for (int i = 0; i < all.Length; i++)
                if (i != item && all[i].Category == d.Category) refs.Add(i, "EV-" + ArchiveUi.Two(i + 1), all[i].Title.ToUpperInvariant());
            fileLine.text = "FILE EVENTS/EV-" + ArchiveUi.Two(item + 1) + "  ·  CATALOG ID " + d.Id +
                "  ·  SOURCE: DIRECTORATE EVENT CATALOG  ·  DOSSIER " + place;
            Restyle();
            if (placed) Place(last);
            Changed();
        }

        private static void Multiplier(Effect effect, string key, float multiplier, string below, string above)
        {
            effect.Key.text = key;
            effect.Value.text = "x" + AvNum.Fixed(multiplier, 2);
            effect.State = multiplier < 0.999f ? AvState.Ready : multiplier > 1.001f ? AvState.Caution : AvState.Inert;
            effect.Note.text = effect.State == AvState.Ready ? below : effect.State == AvState.Caution ? AvStates.Glyph(AvState.Caution) + above : "UNCHANGED";
            effect.Centred = true;
            effect.From = 0.5f;
            effect.To = 0.5f + (widestSwing > 0f ? Mathf.Clamp((multiplier - 1f) / widestSwing, -1f, 1f) * 0.5f : 0f);
            // The scale's ends are the widest swing any dossier in the catalog authors, either way.
            effect.Low.text = "x" + AvNum.Fixed(1f - widestSwing, 2);
            effect.High.text = "x" + AvNum.Fixed(1f + widestSwing, 2);
        }

        private void SetTag(int i, string key, string value)
        {
            tags[i].Key.text = key;
            tags[i].Value.text = value;
        }

        public override float Measure(float width) => Arrange(width, false, 0f);

        public override void Place(AvSlot s)
        {
            base.Place(s);
            last = s;
            placed = true;
            Arrange(s.W, true, s.H);
        }

        /// <summary>Key, value (the state word beside or under it), the bar and the two ends of its scale.</summary>
        private static float EffectHeight(Effect e, float w) => BarOffset(e, w) + 5f + 2f + 15f;

        private static float BarOffset(Effect e, float w) => 15f + 32f + (Beside(e, w) ? 0f : 15f) + 5f;

        private static bool Beside(Effect e, float w) =>
            e.Note.text.Length == 0 || AvText.Width(e.Value) + 12f + AvText.Width(e.Note) <= w;

        private float Arrange(float w, bool place, float slotHeight)
        {
            float y = head.Arrange(0f, w, place) + 10f;
            bool wide = w >= 700f;
            float posterW = wide ? Mathf.Floor(w * 0.58f) : w, posterMin = Mathf.Round(posterW * 9f / 16f);
            float sideX = wide ? posterW + 16f : 0f, sideW = w - sideX;
            float tagW = (sideW - 2f * EffectGap) / 3f, tagH = 0f;
            foreach (Tag tag in tags) tagH = Mathf.Max(tagH, 6f + 15f + AvText.Height(tag.Value, tagW - 16f) + 6f);
            float effectW = wide ? sideW : (w - 2f * 12f) / 3f, effectMin = 0f;
            foreach (Effect effect in effects) effectMin = Mathf.Max(effectMin, EffectHeight(effect, effectW));
            float heroMin = wide ? Mathf.Max(posterMin, 3f * effectMin + 2f * EffectGap + 10f + tagH)
                : posterMin + 12f + effectMin + 10f + tagH;

            float ordersH = 21f;
            if (stepCount == 0) ordersH += AvText.Height(ordersNone, w);
            else
                for (int i = 0; i < stepCount; i++)
                    ordersH += Mathf.Max(16f, AvText.Height(stepLabels[i], w - StepX)) + (i < stepCount - 1 ? 6f : 0f);
            float noteTextH = AvText.Height(note, w - 14f), footH = AvText.Height(footnote, w - 14f);
            float noteH = 21f + noteTextH + 6f + footH;
            int allRows = refs.RowsFor(w);
            float fileH = 8f + AvText.Height(fileLine, w);
            float natural = y + heroMin + 14f + ordersH + 12f + noteH + 12f + (allRows > 0 ? RefStrip.Height(1) + 12f : 0f) + fileH;
            if (!place) return natural;

            // Spare height: whole cross-reference rows first, the poster takes what is left.
            float extra = Mathf.Max(0f, slotHeight - natural);
            int rows = allRows > 0 ? 1 : 0;
            while (rows < allRows && extra >= RefStrip.RowPitch) { rows++; extra -= RefStrip.RowPitch; }
            float heroH = heroMin + extra, posterH = wide ? heroH : posterMin + extra;

            AvLay.Place(poster.Root, 0f, y, posterW, posterH);
            poster.MarkScale = Mathf.Clamp(posterH / 90f, 1f, 3f);
            poster.Layout(posterW, posterH);
            AvLay.Place(posterMarks.rectTransform, 0f, y, posterW, posterH);
            posterMarks.Begin();
            ArchiveUi.Brackets(posterMarks, posterW, posterH, AvInk.Ink);
            posterMarks.End();
            float tierW = AvText.Width(tier) + 18f;
            AvLay.Place(tierBack.rectTransform, 12f, y + 12f, tierW, 20f);
            AvLay.Place(tier.rectTransform, 9f, 0f, tierW - 12f, 20f);

            AvLay.Place(effectBars.rectTransform, 0f, y, w, heroH);
            float cellH = wide ? (heroH - tagH - 10f - 2f * EffectGap) / 3f : effectMin;
            float tagY = wide ? y + heroH - tagH : y + posterH + 12f + effectMin + 10f;
            for (int i = 0; i < effects.Length; i++)
            {
                Effect e = effects[i];
                float ex = wide ? sideX : i * (effectW + 12f), ey = wide ? y + i * (cellH + EffectGap) : y + posterH + 12f;
                float valueW = AvText.Width(e.Value) + 2f, noteW = AvText.Width(e.Note) + 2f;
                AvLay.Place(e.Key.rectTransform, ex, ey, effectW, 15f);
                AvLay.Place(e.Value.rectTransform, ex, ey + 14f, valueW, 32f);
                if (Beside(e, effectW)) AvLay.Place(e.Note.rectTransform, ex + valueW + 10f, ey + 26f, noteW, 15f);
                else AvLay.Place(e.Note.rectTransform, ex, ey + 46f, effectW, 15f);
                float bar = BarOffset(e, effectW);
                AvLay.Place(e.Low.rectTransform, ex, ey + bar + 7f, effectW * 0.5f, 15f);
                AvLay.Place(e.High.rectTransform, ex + effectW * 0.5f, ey + bar + 7f, effectW * 0.5f, 15f);
                e.X = ex;
                e.Y = ey - y + bar;
                e.W = effectW;
            }
            for (int i = 0; i < tags.Length; i++)
            {
                float tx = sideX + i * (tagW + EffectGap);
                AvLay.Place(tags[i].Frame.rectTransform, tx, tagY, tagW, tagH);
                AvLay.Place(tags[i].Key.rectTransform, tx + 8f, tagY + 6f, tagW - 16f, 15f);
                AvLay.Place(tags[i].Value.rectTransform, tx + 8f, tagY + 21f, tagW - 16f, tagH - 27f);
            }
            PaintBars();
            y += heroH + 14f;

            float keyW = AvText.Width(ordersKey) + 2f, hintW = AvText.Width(ordersHint) + 2f;
            AvLay.Place(ordersKey.rectTransform, 0f, y, keyW, 16f);
            ordersHint.gameObject.SetActive(stepCount > 0 && keyW + 16f + hintW <= w);
            AvLay.Place(ordersHint.rectTransform, w - hintW, y, hintW, 16f);
            float row = y + 21f, firstNode = row + 8.5f, lastNode = firstNode;
            if (stepCount == 0) AvLay.Place(ordersNone.rectTransform, 0f, row, w, ordersH - 21f);
            for (int i = 0; i < stepCount; i++)
            {
                float labelH = Mathf.Max(16f, AvText.Height(stepLabels[i], w - StepX));
                AvLay.Place(nodes[i].rectTransform, 3f, row + 5f, 7f, 7f);
                AvLay.Place(stepTimes[i].rectTransform, 22f, row, StepX - 28f, 16f);
                AvLay.Place(stepLabels[i].rectTransform, StepX, row, w - StepX, labelH);
                lastNode = row + 8.5f;
                row += labelH + 6f;
            }
            AvLay.Place(ordersLine.rectTransform, 6f, firstNode, 1f, Mathf.Max(1f, lastNode - firstNode));
            y += ordersH + 12f;

            AvLay.Place(noteKey.rectTransform, 0f, y, w, 16f);
            AvLay.Place(noteRule.rectTransform, 0f, y + 21f, 2f, noteTextH);
            AvLay.Place(note.rectTransform, 14f, y + 21f, w - 14f, noteTextH);
            AvLay.Place(footnote.rectTransform, 14f, y + 21f + noteTextH + 6f, w - 14f, footH);
            y += noteH + 12f;

            refs.Place(y, w, rows);
            if (rows > 0) y += RefStrip.Height(rows) + 12f;
            AvLay.Place(fileRule.rectTransform, 0f, y, w, 1f);
            AvLay.Place(fileLine.rectTransform, 0f, y + 8f, w, fileH - 8f);
            return natural;
        }

        /// <summary>Price and reset bars run from a centre mark (left is cheaper or faster); the window bar runs from its shortest to its longest.</summary>
        private void PaintBars()
        {
            if (!placed) return;
            effectBars.Begin();
            foreach (Effect e in effects)
            {
                effectBars.Add(e.X, e.Y, e.W, 5f, AvInk.Hairline);
                float from = Mathf.Min(e.From, e.To), to = Mathf.Max(e.From, e.To);
                if (to - from > 0.001f)
                    effectBars.Add(e.X + e.W * from, e.Y, Mathf.Max(2f, e.W * (to - from)), 5f, AvInk.State(e.State));
                if (e.Centred) effectBars.Add(e.X + e.W * 0.5f - 0.75f, e.Y - 3f, 1.5f, 11f, AvInk.Ink);
            }
            effectBars.End();
        }

        public override void Restyle()
        {
            head.Restyle();
            poster.Restyle();
            tierBack.color = EventsMfdPanel.SlabBack(tierState);
            tier.color = EventsMfdPanel.SlabInk();
            foreach (Effect e in effects)
            {
                e.Key.color = AvInk.Dim;
                e.Value.color = AvInk.Ink;
                e.Note.color = EventsMfdPanel.TextColor(e.State);
                e.Low.color = e.High.color = AvInk.Dim;
            }
            AvStyle card = AvStyleHost.FuiStyle("card inert");
            for (int i = 0; i < tags.Length; i++)
            {
                tags[i].Frame.Paint(AvStyleHost.Resolve(card.Background, AvTheme.SurfaceInert), AvStyleHost.Resolve(card.Border, AvTheme.Hairline));
                tags[i].Key.color = AvInk.Dim;
                tags[i].Value.color = i == 0 && tierState != AvState.Inert ? EventsMfdPanel.TextColor(tierState) : AvInk.Ink;
            }
            ordersKey.color = AvInk.Key;
            ordersHint.color = AvInk.Dim;
            ordersNone.color = AvInk.Dim;
            ordersLine.color = AvInk.Hairline;
            for (int i = 0; i < Steps; i++)
            {
                nodes[i].color = AvInk.Key;
                stepTimes[i].color = AvInk.Key;
                stepLabels[i].color = AvInk.Ink;
            }
            noteKey.color = AvInk.Key;
            noteRule.color = AvInk.Key;
            note.color = AvInk.Ink;
            footnote.color = AvInk.Dim;
            refs.Restyle();
            fileRule.color = AvInk.Hairline;
            fileLine.color = AvInk.Dim;
            PaintBars();
        }
    }

    /// <summary>
    /// A local volume (WORLD or MANUAL) typeset as one continuous field-manual page: every entry in order, each
    /// with its document code, page reference, margin numeral, title and a body set to a readable measure
    /// behind a margin rule. The entry picked in the index carries the accent rule and is scrolled to the top.
    /// The page closes with the reading room's orders and the volume's file line. An empty shelf uses the same
    /// page for its one notice.
    /// </summary>
    internal sealed class VolumePart : AvPart
    {
        private const int MaxSections = 8;
        private const float Margin = 60f, MaxMeasure = 760f;

        private const string RoomOrders =
            "01  UP and DOWN step the index. LEFT and RIGHT change drawer. ESC closes the room.\n" +
            "02  Cards are read off the game's own encyclopedia when the room opens and put back when it closes.\n" +
            "03  A dash means the definition does not establish the figure. It is never a zero.\n" +
            "04  Nothing read or opened here is sent to the host or to another station.";

        private sealed class Section
        {
            public TMP_Text Code, Page, Number, Title, Body;
            public Image Rule, Divider;

            public void SetActive(bool on)
            {
                Code.gameObject.SetActive(on);
                Page.gameObject.SetActive(on);
                Number.gameObject.SetActive(on);
                Title.gameObject.SetActive(on);
                Body.gameObject.SetActive(on);
                Rule.gameObject.SetActive(on);
                Divider.gameObject.SetActive(on);
            }
        }

        private readonly Section[] sections = new Section[MaxSections];
        private readonly float[] tops = new float[MaxSections];
        private readonly TMP_Text ordersKey, orders, fileLine;
        private readonly Image fileRule;
        private int count, selected;
        private bool placed;
        private AvSlot last;

        public VolumePart(RectTransform parent)
        {
            Rect = AvLay.Child(parent, "Volume");
            for (int i = 0; i < MaxSections; i++)
            {
                var s = new Section
                {
                    Code = AvText.Make(Rect, "Code " + i, AvTextRole.Micro, ""),
                    Page = AvText.Make(Rect, "Page " + i, AvTextRole.DataSmall, "", TextAlignmentOptions.MidlineRight),
                    Number = AvText.Make(Rect, "Number " + i, AvTextRole.Display, "", TextAlignmentOptions.TopRight),
                    Title = AvText.Make(Rect, "Title " + i, AvTextRole.Title, "", TextAlignmentOptions.TopLeft, true),
                    Rule = AvLay.Solid(Rect, "Rule " + i, Color.clear),
                    Body = AvText.Make(Rect, "Body " + i, AvTextRole.Prose, "", TextAlignmentOptions.TopLeft, true),
                    Divider = AvLay.Solid(Rect, "Divider " + i, Color.clear),
                };
                s.SetActive(false);
                sections[i] = s;
            }
            ordersKey = AvText.Make(Rect, "RoomOrdersKey", AvTextRole.Micro, "READING ROOM ORDERS");
            orders = AvText.Make(Rect, "RoomOrders", AvTextRole.ProseSmall, RoomOrders, TextAlignmentOptions.TopLeft, true);
            fileRule = AvLay.Solid(Rect, "FileRule", Color.clear);
            fileLine = AvText.Make(Rect, "FileLine", AvTextRole.Micro, "", TextAlignmentOptions.TopLeft, true);
            Restyle();
        }

        /// <summary>Where entry <paramref name="index"/> starts inside the part, for scrolling it to the top of the pane.</summary>
        public float SectionTop(int index) => index >= 0 && index < count ? tops[index] : 0f;

        public void Bind(string volume, EventDocEntry[] entries, int open)
        {
            count = Mathf.Min(entries.Length, MaxSections);
            selected = Mathf.Clamp(open, 0, Mathf.Max(0, count - 1));
            for (int i = 0; i < MaxSections; i++)
            {
                Section s = sections[i];
                s.SetActive(i < count);
                if (i >= count) continue;
                s.Code.text = entries[i].Code;
                s.Page.text = "PAGE " + AvNum.Fixed(i + 1, 0) + " OF " + AvNum.Fixed(count, 0);
                s.Number.text = ArchiveUi.Two(i + 1);
                s.Title.text = entries[i].Title.ToUpperInvariant();
                s.Body.text = entries[i].Body;
            }
            fileLine.text = "VOLUME " + volume + "  ·  " + AvNum.Fixed(count, 0) +
                " PAGES  ·  SOURCE: DIRECTORATE FIELD DESK  ·  LOCAL COPY HELD AT THIS STATION";
            Rebind();
        }

        /// <summary>A drawer with nothing in it: one in-world notice in place of the entries.</summary>
        public void BindNotice(string code, string title, string body)
        {
            count = 1;
            selected = 0;
            for (int i = 0; i < MaxSections; i++) sections[i].SetActive(i == 0);
            sections[0].Code.text = code;
            sections[0].Page.text = "";
            sections[0].Number.text = "—";
            sections[0].Title.text = title;
            sections[0].Body.text = body;
            fileLine.text = code + "  ·  NO RECORDS HELD  ·  LOCAL COPY HELD AT THIS STATION";
            Rebind();
        }

        private void Rebind()
        {
            Restyle();
            if (placed) Place(last);
            Changed();
        }

        public override float Measure(float width) => Arrange(width, false, 0f);

        public override void Place(AvSlot s)
        {
            base.Place(s);
            last = s;
            placed = true;
            Arrange(s.W, true, s.H);
        }

        private float Arrange(float w, bool place, float slotHeight)
        {
            float measure = Mathf.Min(w - Margin, MaxMeasure), y = 0f;
            for (int i = 0; i < count; i++)
            {
                Section s = sections[i];
                tops[i] = y;
                float pageW = AvText.Width(s.Page) + 2f;
                float titleH = AvText.Height(s.Title, measure), bodyH = AvText.Height(s.Body, measure - 16f);
                if (place)
                {
                    AvLay.Place(s.Code.rectTransform, Margin, y, w - Margin - pageW - 10f, 16f);
                    AvLay.Place(s.Page.rectTransform, w - pageW, y, pageW, 16f);
                    AvLay.Place(s.Number.rectTransform, 0f, y + 14f, Margin - 16f, 34f);
                    AvLay.Place(s.Title.rectTransform, Margin, y + 18f, measure, titleH);
                    AvLay.Place(s.Rule.rectTransform, Margin, y + 18f + titleH + 6f, i == selected ? 3f : 2f, bodyH);
                    AvLay.Place(s.Body.rectTransform, Margin + 16f, y + 18f + titleH + 6f, measure - 16f, bodyH);
                }
                y += 18f + titleH + 6f + bodyH + 12f;
                if (place) AvLay.Place(s.Divider.rectTransform, 0f, y, w, 1f);
                y += 1f + 10f;
            }
            float ordersH = AvText.Height(orders, measure), fileH = 8f + AvText.Height(fileLine, w);
            float natural = y + 21f + ordersH + 12f + fileH;
            if (!place) return natural;

            // A page shorter than the pane seats its closing block on the bottom edge, as a printed page does.
            y += Mathf.Max(0f, slotHeight - natural);
            AvLay.Place(ordersKey.rectTransform, Margin, y, measure, 16f);
            AvLay.Place(orders.rectTransform, Margin, y + 21f, measure, ordersH);
            y += 21f + ordersH + 12f;
            AvLay.Place(fileRule.rectTransform, 0f, y, w, 1f);
            AvLay.Place(fileLine.rectTransform, 0f, y + 8f, w, fileH - 8f);
            return natural;
        }

        public override void Restyle()
        {
            for (int i = 0; i < MaxSections; i++)
            {
                Section s = sections[i];
                bool open = i == selected;
                s.Code.color = AvInk.Key;
                s.Page.color = AvInk.Dim;
                s.Number.color = open ? AvInk.Key : AvInk.Dim;
                s.Title.color = AvInk.Ink;
                s.Rule.color = open ? AvInk.Select : AvInk.Hairline;
                s.Body.color = open ? AvInk.Ink : AvInk.Dim;
                s.Divider.color = AvInk.Hairline;
            }
            ordersKey.color = AvInk.Key;
            orders.color = AvInk.Dim;
            fileRule.color = AvInk.Hairline;
            fileLine.color = AvInk.Dim;
        }
    }
}
