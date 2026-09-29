using System;
using NOAvionics;
using NOAvionics.Ui;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Features.Events.Presentation
{
    /// <summary>
    /// The DESK tile and the EVENT LOG timeline. Both are module-local kit parts: they measure from their
    /// real content through one arrangement routine and call <see cref="AvPart.Changed"/> when text that
    /// can move their height changes.
    /// </summary>
    internal sealed partial class EventsMfdPanel
    {
        /// <summary>A clickable archive entry point: glyph, name, record count and one line about what is inside.</summary>
        private sealed class ArchiveTilePart : AvPart
        {
            private readonly AvFrame frame;
            private readonly Image rail;
            private readonly TMP_Text glyph, arrow, title, count, blurb;
            private bool hover;

            public ArchiveTilePart(RectTransform parent, AvIcon icon, string titleText, string blurbText, Action onClick)
            {
                Rect = AvLay.Child(parent, "Tile " + titleText);
                frame = AvFrame.Add(Rect, "Frame", AvChamfer.Diagonal(6f));
                AvLay.Fill(frame.rectTransform);
                rail = AvLay.Solid(Rect, "Rail", Color.clear);
                glyph = AvIcons.Make(Rect, icon, AvGridTokens.IconTool, Color.white);
                arrow = AvIcons.Make(Rect, AvIcon.ChevronRight, AvGridTokens.IconInline, Color.white);
                title = AvText.Make(Rect, "Title", AvTextRole.Head, titleText, TextAlignmentOptions.MidlineLeft);
                AvText.Fit(title, false);
                count = AvText.Make(Rect, "Count", AvTextRole.Micro, "", TextAlignmentOptions.TopLeft, true);
                blurb = AvText.Make(Rect, "Blurb", AvTextRole.ProseSmall, blurbText, TextAlignmentOptions.TopLeft, true);
                AvHit hit = AvHit.On(frame);
                hit.Hover = h => { hover = h; Restyle(); };
                hit.Click = e => onClick?.Invoke();
                Restyle();
            }

            public string Help { set => AvHelpTip.Attach(frame.gameObject, value); }

            public void SetCount(string text)
            {
                if (count.text == text) return;
                count.text = text ?? "";
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
                float w = width - 24f;
                float ch = count.text.Length > 0 ? AvText.Height(count, w) : 0f;
                float bh = AvText.Height(blurb, w);
                float total = 12f + 22f + 4f + (ch > 0f ? ch + 2f : 0f) + bh + 12f;
                if (place)
                {
                    AvLay.Place(rail.rectTransform, 0f, 0f, 2f, total);
                    AvLay.Place(glyph.rectTransform, 12f, 12f, 22f, 22f);
                    AvLay.Place(title.rectTransform, 40f, 12f, width - 40f - 34f, 22f);
                    AvLay.Place(arrow.rectTransform, width - 28f, 16f, 16f, 16f);
                    float y = 12f + 22f + 4f;
                    AvLay.Place(count.rectTransform, 12f, y, w, ch);
                    if (ch > 0f) y += ch + 2f;
                    AvLay.Place(blurb.rectTransform, 12f, y, w, bh);
                }
                return total;
            }

            public override void Restyle()
            {
                AvStyle c = AvStyleHost.FuiStyle(hover ? "card raised" : "card");
                frame.Paint(AvStyleHost.Resolve(c.Background, AvTheme.Surface), AvStyleHost.Resolve(c.Border, AvTheme.Hairline));
                rail.color = AvStyleHost.FuiColor(hover ? "select" : "info", AvTheme.RailInfo);
                glyph.color = AvStyleHost.FuiColor("key", AvTheme.RailInfo);
                arrow.color = AvStyleHost.FuiColor(hover ? "ink" : "ink-dim", AvTheme.Dim);
                title.color = AvStyleHost.FuiColor("ink", AvTheme.TextPrimary);
                count.color = AvStyleHost.FuiColor("key", AvTheme.RailInfo);
                blurb.color = AvStyleHost.FuiColor("ink-dim", AvTheme.Dim);
            }
        }

        /// <summary>
        /// The event log as a timeline: a vertical rail with one square node per finished event, newest first.
        /// Each entry is the event name, its effect in mono on the right and a tier / target / age line
        /// underneath. An empty log is one compact note. The pool is capped at <see cref="Capacity"/>.
        /// </summary>
        private sealed class EventTimelinePart : AvPart
        {
            internal const int Capacity = 16;
            private const float NodeX = 3f, TextX = 24f;

            private readonly Image line;
            private readonly Image[] nodes = new Image[Capacity];
            private readonly TMP_Text[] names = new TMP_Text[Capacity];
            private readonly TMP_Text[] effects = new TMP_Text[Capacity];
            private readonly TMP_Text[] metas = new TMP_Text[Capacity];
            private readonly AvState[] tiers = new AvState[Capacity];
            private readonly AvState[] effectStates = new AvState[Capacity];
            private readonly AvFrame noteFrame;
            private readonly TMP_Text noteText;
            private int count;

            public EventTimelinePart(RectTransform parent)
            {
                Rect = AvLay.Child(parent, "Timeline");
                line = AvLay.Solid(Rect, "Line", Color.clear);
                for (int i = 0; i < Capacity; i++)
                {
                    nodes[i] = AvLay.Solid(Rect, "Node " + i, Color.clear);
                    names[i] = AvText.Make(Rect, "Name " + i, AvTextRole.Label, "", TextAlignmentOptions.TopLeft, true);
                    effects[i] = AvText.Make(Rect, "Effect " + i, AvTextRole.DataSmall, "", TextAlignmentOptions.TopRight);
                    metas[i] = AvText.Make(Rect, "Meta " + i, AvTextRole.Micro, "", TextAlignmentOptions.TopLeft, true);
                    nodes[i].gameObject.SetActive(false);
                    names[i].gameObject.SetActive(false);
                    effects[i].gameObject.SetActive(false);
                    metas[i].gameObject.SetActive(false);
                }
                noteFrame = AvFrame.Add(Rect, "NoteFrame", AvChamfer.Diagonal(6f));
                noteText = AvText.Make(Rect, "Note", AvTextRole.ProseSmall, "", TextAlignmentOptions.TopLeft, true);
                Restyle();
            }

            /// <summary>Empty-state copy shown while there are no entries.</summary>
            public void SetNote(string text)
            {
                if (noteText.text == text) return;
                noteText.text = text ?? "";
                Changed();
            }

            public void SetCount(int n)
            {
                n = Mathf.Clamp(n, 0, Capacity);
                if (n == count) return;
                count = n;
                for (int i = 0; i < Capacity; i++)
                {
                    bool on = i < count;
                    nodes[i].gameObject.SetActive(on);
                    names[i].gameObject.SetActive(on);
                    effects[i].gameObject.SetActive(on);
                    metas[i].gameObject.SetActive(on);
                }
                noteFrame.gameObject.SetActive(count == 0);
                noteText.gameObject.SetActive(count == 0);
                Changed();
            }

            public void Set(int i, string nameText, string metaText, string effectText, AvState tier, AvState effectState)
            {
                if (i < 0 || i >= Capacity) return;
                bool grew = names[i].text != nameText || metas[i].text != metaText || effects[i].text != effectText;
                if (!grew && tiers[i] == tier && effectStates[i] == effectState) return;
                names[i].text = nameText;
                metas[i].text = metaText;
                effects[i].text = effectText;
                tiers[i] = tier;
                effectStates[i] = effectState;
                RestyleEntry(i);
                if (grew) Changed();
            }

            public override float Measure(float width) => Arrange(width, false);

            public override void Place(AvSlot s)
            {
                base.Place(s);
                Arrange(s.W, true);
            }

            private float Arrange(float width, bool place)
            {
                if (count == 0)
                {
                    float nh = 12f + AvText.Height(noteText, width - 24f) + 12f;
                    if (place)
                    {
                        AvLay.Fill(noteFrame.rectTransform);
                        AvLay.Place(noteText.rectTransform, 12f, 12f, width - 24f, nh - 24f);
                        AvLay.Place(line.rectTransform, 0f, 0f, 0f, 0f);
                    }
                    return nh;
                }

                float y = 0f, firstNode = 0f, lastNode = 0f;
                for (int i = 0; i < count; i++)
                {
                    float effectW = effects[i].text.Length > 0 ? Mathf.Ceil(AvText.Width(effects[i])) + 6f : 0f;
                    float nameW = width - TextX - effectW;
                    float nh = Mathf.Max(16f, AvText.Height(names[i], nameW));
                    float mh = metas[i].text.Length > 0 ? AvText.Height(metas[i], width - TextX) : 0f;
                    float h = nh + (mh > 0f ? 1f + mh : 0f) + 10f;
                    if (place)
                    {
                        AvLay.Place(nodes[i].rectTransform, NodeX, y + 4f, 7f, 7f);
                        AvLay.Place(names[i].rectTransform, TextX, y, nameW, nh);
                        AvLay.Place(effects[i].rectTransform, width - effectW, y, effectW, 16f);
                        AvLay.Place(metas[i].rectTransform, TextX, y + nh + 1f, width - TextX, mh);
                    }
                    if (i == 0) firstNode = y + 7.5f;
                    lastNode = y + 7.5f;
                    y += h;
                }
                if (place) AvLay.Place(line.rectTransform, NodeX + 3f, firstNode, 1f, Mathf.Max(1f, lastNode - firstNode));
                return y - 6f;
            }

            private void RestyleEntry(int i)
            {
                nodes[i].color = AvStyleHost.FuiColor(AvStates.Class(tiers[i]) == "inert" ? "frame" : AvStates.Class(tiers[i]), AvTheme.RailInfo);
                names[i].color = AvStyleHost.FuiColor("ink", AvTheme.TextPrimary);
                effects[i].color = effectStates[i] == AvState.Inert
                    ? AvStyleHost.FuiColor("ink-dim", AvTheme.Dim) : TextColor(effectStates[i]);
                metas[i].color = AvStyleHost.FuiColor("ink-dim", AvTheme.Dim);
            }

            public override void Restyle()
            {
                line.color = AvStyleHost.FuiColor("hairline", AvTheme.Hairline);
                AvStyle c = AvStyleHost.FuiStyle("card inert");
                noteFrame.Paint(AvStyleHost.Resolve(c.Background, AvTheme.SurfaceInert), AvStyleHost.Resolve(c.Border, AvTheme.Hairline));
                noteText.color = AvStyleHost.FuiColor("ink-dim", AvTheme.Dim);
                for (int i = 0; i < Capacity; i++) RestyleEntry(i);
            }
        }
    }
}
