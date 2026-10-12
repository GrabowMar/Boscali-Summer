using BoscaliSummer.Modules.Command.Domain;
using NOAvionics;
using TMPro;
using UnityEngine;

namespace BoscaliSummer.Modules.Command.Presentation
{
    /// <summary>Panel and map ink for a role, read from the live kit sheet so both surfaces always agree and a theme change repaints through Restyle.</summary>
    internal static class StrInk
    {
        /// <summary>Effort and Wave are the sheet's amber, Own the friendly blue, Hostile and Strike the hostile red, Offered the key cyan, Caution and Danger their state hues.</summary>
        internal static Color Of(StrInkRole role)
        {
            switch (role)
            {
                case StrInkRole.Effort:
                case StrInkRole.Wave: return AvInk.Effort;
                case StrInkRole.Own: return AvInk.Friendly;
                case StrInkRole.Hostile:
                case StrInkRole.Strike: return AvInk.Hostile;
                case StrInkRole.Offered: return AvInk.Key;
                case StrInkRole.Caution: return AvInk.State(AvState.Caution);
                case StrInkRole.Danger: return AvInk.State(AvState.Danger);
                default: return AvInk.Muted;
            }
        }

        internal static Rgba RgbaOf(StrInkRole role) => Of(role).ToRgba();

        /// <summary>The text colour that reads best on a solid fill: the sheet's ground or its ink, whichever contrasts more.</summary>
        internal static Color On(Color fill)
        {
            Color dark = AvInk.Ground, light = AvInk.Ink;
            Rgba f = fill.WithAlpha(1f).ToRgba();
            return Rgba.Contrast(dark.WithAlpha(1f).ToRgba(), f) >= Rgba.Contrast(light.WithAlpha(1f).ToRgba(), f) ? dark.WithAlpha(1f) : light;
        }
    }

    /// <summary>A small solid label ("MAIN EFFORT", "STRIKE", "O1"): one fill, one word, text colour chosen for contrast. A part element, not a flow part.</summary>
    internal sealed class StrSlab
    {
        private readonly AvFrame frame;
        private readonly TMP_Text text;

        internal RectTransform Rect { get; }

        internal StrSlab(RectTransform parent, string name, AvTextRole role = AvTextRole.Micro)
        {
            Rect = AvLay.Child(parent, name);
            frame = AvFrame.Add(Rect, "Frame", default(AvChamfer));
            AvLay.Fill(frame.rectTransform);
            text = AvText.Make(Rect, "Text", role, "", TextAlignmentOptions.Center);
            AvText.Fit(text, false);
            text.raycastTarget = false;
            AvLay.Fill(text.rectTransform, 1f);
        }

        internal string Text => text.text;

        /// <summary>A solid slab: the word on <paramref name="fill"/>.</summary>
        internal void Set(string value, Color fill)
        {
            AvText.Set(text, value);
            frame.Paint(fill, fill);
            text.color = StrInk.On(fill);
        }

        /// <summary>A quiet slab: the word in <paramref name="ink"/> inside a hairline of the same ink.</summary>
        internal void SetOutline(string value, Color ink)
        {
            AvText.Set(text, value);
            frame.Paint(ink.WithAlpha(0.10f), ink.WithAlpha(0.9f));
            text.color = ink;
        }

        /// <summary>Width the word wants, with its side padding.</summary>
        internal float WidthFor() => Mathf.Ceil(AvText.Width(text)) + 14f;

        internal void Place(float x, float y, float w, float h) => AvLay.Place(Rect, x, y, w, h);

        internal void SetActive(bool on)
        {
            if (Rect.gameObject.activeSelf != on) Rect.gameObject.SetActive(on);
        }
    }
}
