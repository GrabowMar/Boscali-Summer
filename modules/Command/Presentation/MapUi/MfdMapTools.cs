using System;
using System.Collections.Generic;
using NOAvionics;
using UnityEngine;

namespace BoscaliSummer.Modules.Command.Presentation.MapUi
{
    /// <summary>The drawer's LAYERS and PLOT pages. Game/module services remain the state and action authorities.</summary>
    internal static class MfdMapTools
    {
        private static Page layers, plot;
        private static Action<int> refresh;
        private static int lastShown = -1;

        internal static void Build(RectTransform layersRoot, RectTransform plotRoot, AvTicker ticker, AvFooter footer)
        {
            Reset();
            layers = new Page(layersRoot, ticker);
            plot = new Page(plotRoot, ticker);
            refresh = VanillaMfdRebuild.BuildMapDrawer(layers, plot, footer);
        }

        internal static void Layout(float width, float height)
        {
            layers?.Layout(width, height);
            plot?.Layout(width, height);
        }

        // The owner also calls this with -1 while folded: an armed plot tool must keep accepting map input.
        internal static void Refresh(int selectedTab)
        {
            refresh?.Invoke(selectedTab);
            if (lastShown == selectedTab) return;
            lastShown = selectedTab;
            if (selectedTab == 1) layers?.SettleVisible();
            else if (selectedTab == 2) plot?.SettleVisible();
        }

        internal static void Reset()
        {
            refresh = null;
            lastShown = -1;
            layers?.Dispose(); plot?.Dispose();
            layers = plot = null;
        }

        internal sealed class Page
        {
            private readonly RectTransform host;
            private readonly AvScrollView scroll;
            private readonly AvFlow flow;
            private readonly Cards cards;
            private float width = -1f, height = -1f;
            private bool shown;

            internal Page(RectTransform parent, AvTicker ticker)
            {
                host = AvLay.Child(parent, "MapToolsScroll");
                scroll = new AvScrollView(host, true);
                flow = new AvFlow(scroll.Content, ticker, 1000f, 0f);
                cards = flow.Add(new Cards(flow.Content, ticker));
                ticker?.Register(new AvRestyleHook(scroll.Restyle));
            }

            internal AvFlow Card(string title) => cards.Add(title);

            internal void Layout(float w, float h)
            {
                w = Mathf.Max(1f, w); h = Mathf.Max(1f, h);
                if (Mathf.Abs(width - w) < .5f && Mathf.Abs(height - h) < .5f) return;
                bool first = width < 0f;
                width = w; height = h;
                AvLay.Place(host, 0f, 0f, w, h);
                scroll.Place(w, h);
                cards.Columns = w >= 960f ? 3 : w >= 700f ? 2 : 1;
                flow.EnsureLayout(w);
                if (first) scroll.Content.anchoredPosition = Vector2.zero;
            }

            internal void SettleVisible()
            {
                if (width <= 0f || !host.gameObject.activeInHierarchy) return;
                Vector2 offset = shown ? scroll.Content.anchoredPosition : Vector2.zero;
                // TMP built below an inactive page can report a cold size. Re-measure after activation,
                // before the first paint; later fast ticks handle text changes without repeating this pass.
                flow.RequestRelayout();
                flow.EnsureLayout(width);
                scroll.Scroll.StopMovement();
                offset.x = 0f;
                offset.y = Mathf.Clamp(offset.y, 0f, Mathf.Max(0f, flow.ContentHeight - height));
                scroll.Content.anchoredPosition = offset;
                shown = true;
            }

            internal void Dispose() { if (host != null) UnityEngine.Object.Destroy(host.gameObject); }
        }

        /// <summary>A small responsive card grid; each card keeps its flow and callbacks through resize.</summary>
        private sealed class Cards : AvPart
        {
            private readonly List<AvCard> cards = new List<AvCard>(6);
            private readonly AvTicker ticker;
            private readonly List<float> rows = new List<float>(6);
            private int columns = 3;

            internal Cards(RectTransform parent, AvTicker ticker)
            {
                Rect = AvLay.Child(parent, "Cards");
                this.ticker = ticker;
            }

            internal int Columns
            {
                set { if (columns == value) return; columns = value; Changed(); }
            }

            internal AvFlow Add(string title)
            {
                var card = new AvCard(Rect, ticker, 320f, title, false) { Parent = this };
                cards.Add(card); ticker?.Register(card);
                Changed();
                return card.Flow;
            }

            public override float Measure(float width)
            {
                rows.Clear();
                float cell = Mathf.Max(1f, (width - (columns - 1) * AvGridTokens.Gap) / columns);
                for (int i = 0; i < cards.Count; i++)
                {
                    if (i % columns == 0) rows.Add(0f);
                    int row = rows.Count - 1;
                    rows[row] = Mathf.Max(rows[row], cards[i].Measure(cell));
                }
                float h = Mathf.Max(0, rows.Count - 1) * AvGridTokens.Gap;
                foreach (float row in rows) h += row;
                return h;
            }

            public override void Place(AvSlot slot)
            {
                base.Place(slot);
                Measure(slot.W);
                float cell = Mathf.Max(1f, (slot.W - (columns - 1) * AvGridTokens.Gap) / columns), y = 0f;
                for (int i = 0; i < cards.Count; i++)
                {
                    int row = i / columns;
                    cards[i].Place(new AvSlot((i % columns) * (cell + AvGridTokens.Gap), y, cell, rows[row]));
                    if (i % columns == columns - 1) y += rows[row] + AvGridTokens.Gap;
                }
            }
        }
    }
}
