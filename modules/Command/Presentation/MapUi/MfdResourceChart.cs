using System;
using NOAvionics;
using NOAvionics.Ui;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Features.Command.Presentation.MapUi
{
    internal sealed class MfdResourceChart
    {
        private readonly Image[] segments = new Image[MfdResourceHistory.Capacity - 1];
        private readonly Image[] glow = new Image[MfdResourceHistory.Capacity - 1];
        private readonly TMP_Text upper, lower, window, summary, empty;
        private readonly Image zero;
        private readonly Image marker;
        private readonly float left, top, width, height;

        internal MfdResourceChart(RectTransform parent, float y, float panelWidth, float availableHeight)
        {
            left = AvTokens.Space3 + 76f;
            top = y - 28f;
            width = panelWidth - left - 8f;
            height = Mathf.Max(64f, availableHeight - 82f);
            summary = AvStyled.Label(parent, new Rect(AvTokens.Space3, y, panelWidth - AvTokens.Space3, 18f),
                "", "row-main");
            upper = AvStyled.Label(parent, new Rect(AvTokens.Space3, top, 70f, 16f), "—", "row-sub");
            lower = AvStyled.Label(parent, new Rect(AvTokens.Space3, top - height + 16f, 70f, 16f), "—", "row-sub");
            AvKit.Panel(parent, new Rect(left, top, width, height), AvTheme.SurfaceRaised);
            for (int i = 0; i < 3; i++)
                AvKit.Rule(parent, new Rect(left, top - height * i / 2f, width, 1f), AvTheme.Hairline);
            zero = AvKit.Rule(parent, new Rect(left, top - height, width, 1f), AvTheme.RailInfo);
            for (int i = 0; i < glow.Length; i++)
            {
                glow[i] = AvKit.Rule(parent, new Rect(left, top, 0f, 5f), AvTheme.Accent.WithAlpha(0.16f));
                glow[i].rectTransform.pivot = new Vector2(0f, .5f);
                glow[i].raycastTarget = false;
            }
            for (int i = 0; i < segments.Length; i++)
            {
                segments[i] = AvKit.Rule(parent, new Rect(left, top, 0f, 2f), AvTheme.Accent);
                segments[i].rectTransform.pivot = new Vector2(0f, .5f);
                segments[i].raycastTarget = false;
            }
            marker = AvKit.Rule(parent, new Rect(left, top, 6f, 6f), AvTheme.TextPrimary);
            marker.rectTransform.pivot = new Vector2(.5f, .5f);
            marker.raycastTarget = false;
            empty = AvStyled.Label(parent, new Rect(left + 8f, top - height * .5f + 9f, width - 16f, 18f),
                "COLLECTING SAMPLES…", "row-sub", align: TextAlignmentOptions.Center);
            window = AvStyled.Label(parent, new Rect(AvTokens.Space3, top - height - 10f,
                panelWidth - AvTokens.Space3, 18f), "", "row-sub");
        }

        internal void Set(MfdResourceHistory history, int series, string name, Func<float, string> format)
        {
            history.Range(series, out float min, out float max);
            upper.text = format(max);
            lower.text = format(min);
            float duration = history.Count > 1 ? history.Time(history.Count - 1) - history.Time(0) : 0f;
            int drawn = 0;
            for (int i = 0; i < segments.Length; i++)
            {
                bool valid = i + 1 < history.Count && duration > 0f &&
                    MfdResourceHistory.Finite(history.Value(series, i)) &&
                    MfdResourceHistory.Finite(history.Value(series, i + 1));
                segments[i].enabled = valid;
                glow[i].enabled = valid;
                if (!valid) continue;
                Vector2 a = Point(history, series, i, min, max, duration);
                Vector2 b = Point(history, series, i + 1, min, max, duration);
                SetSegment(segments[i], a, b, 2f);
                SetSegment(glow[i], a, b, 5f);
                drawn++;
            }
            zero.rectTransform.anchoredPosition = new Vector2(left, top - height + height * (0f - min) / (max - min));
            bool known = history.Count > 0 && MfdResourceHistory.Finite(history.Value(series, history.Count - 1));
            marker.enabled = drawn > 0 && known;
            if (marker.enabled)
            {
                marker.rectTransform.anchoredPosition =
                    Point(history, series, history.Count - 1, min, max, duration);
            }
            empty.enabled = drawn == 0;
            empty.text = known ? "COLLECTING SAMPLES…" : "RESOURCE DATA UNAVAILABLE";
            float change = known ? history.Value(series, history.Count - 1) - history.Value(series, 0) : float.NaN;
            summary.text = name + "  •  CHANGE " + (MfdResourceHistory.Finite(change)
                ? (change > 0f ? "+" : "") + format(change) : "—");
            window.text = duration > 0f ? "LAST " + duration.ToString("0") + "s   →   NOW  |  5s SAMPLES"
                : "RECORDING SINCE MISSION START  |  5s SAMPLES";
            if (!known) upper.text = lower.text = "—";
        }

        private static void SetSegment(Image image, Vector2 a, Vector2 b, float thickness)
        {
            RectTransform rect = image.rectTransform;
            rect.anchoredPosition = a;
            rect.sizeDelta = new Vector2((b - a).magnitude, thickness);
            rect.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(b.y - a.y, b.x - a.x) * Mathf.Rad2Deg);
        }

        private Vector2 Point(MfdResourceHistory history, int series, int index, float min, float max, float duration) =>
            new Vector2(left + width * (history.Time(index) - history.Time(0)) / duration,
                top - height + height * (history.Value(series, index) - min) / (max - min));
    }
}
