using BoscaliSummer.Features.Hud.Domain;
using NOAvionics.Ui;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
namespace BoscaliSummer.Features.Hud.Presentation
{
    // A reusable display slot, never a feed handle. Data lifetime is independent of UI lifetime.
    internal sealed class HudRow
    {
        public readonly RectTransform Rect;
        private readonly TMP_Text title, detail, glyph;
        private readonly Image progress;
        public HudRow(Transform parent, int index)
        {
            Rect = HudSurface.Rect("Feed slot " + index, parent);
            title = HudSurface.Text("Message", Rect, 15);
            detail = HudSurface.Text("Detail", Rect, 13);
            glyph = HudSurface.Text("Severity", Rect, 13);
            progress = HudSurface.Line("Progress", Rect, AvTheme.RailInfo);
        }
        public static float Height(HudMessage message, bool details) => details && !string.IsNullOrEmpty(message.Detail) ? 36 : 24;
        public void Present(HudMessage message, float width, bool details)
        {
            float height = Height(message, details);
            Color color = HudSurface.Tone(message.Tone);
            HudSurface.Place(glyph.rectTransform, 8, height - 21, 12, 20);
            HudSurface.Place(title.rectTransform, 23, height - 21, width - 33, 20);
            HudSurface.Place(detail.rectTransform, 23, 1, width - (message.Bar > .001f ? 93 : 33), 16);
            HudSurface.Place(progress.rectTransform, width - 58, 8, 48 * Mathf.Clamp01(message.Bar), 1);
            HudSurface.Write(title, message.Text, color);
            HudSurface.Write(detail, message.Detail, HudSurface.Ink.WithAlpha(.85f));
            HudSurface.Write(glyph, message.Tone == Framework.Contracts.HudTone.Warning ? "!" : message.Tone == Framework.Contracts.HudTone.Caution ? "!" : message.Notice ? "+" : "", color);
            detail.gameObject.SetActive(details && !string.IsNullOrEmpty(message.Detail));
            progress.gameObject.SetActive(details && message.Bar > .001f && message.Bar < .999f); progress.color = color;
            Rect.gameObject.SetActive(true);
        }
        public void Hide() => Rect.gameObject.SetActive(false);
    }
}
