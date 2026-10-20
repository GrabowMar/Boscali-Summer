using BoscaliSummer.Core.Game;
using BoscaliSummer.Core.Ui;
using NOAvionics;
using TMPro;
using UnityEngine;

namespace BoscaliSummer.Garrisons
{
    /// <summary>One input-transparent LZ fix: ground brackets, a parachute and a labelled bearing.</summary>
    internal sealed class LandingZoneMarker
    {
        internal readonly RectTransform Root;
        private readonly AvVector brackets, pointer;
        private readonly TMP_Text parachute, label;

        internal LandingZoneMarker(RectTransform parent, string name, bool hud)
        {
            Root = AvLay.Child(parent, name);
            Root.anchorMin = Root.anchorMax = Root.pivot = new Vector2(.5f, .5f);
            Root.sizeDelta = Vector2.zero;
            brackets = AvVector.Create(Root, "Ground brackets", 16);
            AvStrokes.Bracket(brackets.Buffer, -13f, -13f, 26f, 26f, 6f, 1.5f, new Rgba(1f, 1f, 1f, 1f));
            brackets.Commit();
            parachute = AvIcons.Make(Root, AvIcon.Parachute, hud ? 18f : 20f, Color.white);
            parachute.rectTransform.anchorMin = parachute.rectTransform.anchorMax = parachute.rectTransform.pivot = new Vector2(.5f, .5f);
            parachute.rectTransform.sizeDelta = new Vector2(26f, 26f);
            parachute.raycastTarget = false;
            pointer = AvVector.Create(Root, "Offscreen bearing", 4);
            AvStrokes.Line(pointer.Buffer, 16f, 6f, 24f, 0f, 1.8f, new Rgba(1f, 1f, 1f, 1f));
            AvStrokes.Line(pointer.Buffer, 24f, 0f, 16f, -6f, 1.8f, new Rgba(1f, 1f, 1f, 1f));
            pointer.Commit();
            if (hud) ImageShade(Root);
            label = AvText.Make(Root, "Landing zone label", AvTextRole.DataSmall, "LZ", TextAlignmentOptions.Center);
            label.rectTransform.anchorMin = label.rectTransform.anchorMax = new Vector2(.5f, .5f);
            label.rectTransform.pivot = new Vector2(.5f, 1f);
            label.rectTransform.sizeDelta = new Vector2(hud ? 240f : 80f, 25f);
            label.rectTransform.anchoredPosition = new Vector2(0f, -19f);
            label.enableWordWrapping = false;
            label.richText = false;
            label.raycastTarget = false;
            label.fontSize = hud ? 16f : 13f;
            if (hud && VanillaHudStyle.TryCockpit(out VanillaHudStyle.CockpitStyle style))
            {
                label.font = style.Font;
                if (style.FontMaterial != null) label.fontSharedMaterial = style.FontMaterial;
            }
            SetVisible(false);
        }

        private static void ImageShade(RectTransform parent)
        {
            var shade = HudSprites.Child(parent, "Landing zone label shade", HudSprites.Feather, sliced: true);
            shade.rectTransform.SetAsFirstSibling();
            shade.rectTransform.anchorMin = shade.rectTransform.anchorMax = shade.rectTransform.pivot = new Vector2(.5f, .5f);
            shade.rectTransform.sizeDelta = new Vector2(276f, 58f);
            shade.rectTransform.anchoredPosition = new Vector2(0f, -32f);
            shade.color = HudSprites.ShadeTint.WithAlpha(.3f);
        }

        internal void Show(Vector2 at, string caption, Color ink, bool pinned = false, float bearing = 0f)
        {
            SetVisible(true);
            Root.localPosition = new Vector3(at.x, at.y, 0f);
            brackets.color = pointer.color = ink;
            parachute.color = label.color = ink;
            brackets.enabled = !pinned;
            pointer.gameObject.SetActive(pinned);
            pointer.rectTransform.localRotation = Quaternion.Euler(0f, 0f, bearing);
            if (label.text != caption) label.text = caption;
        }

        internal void SetVisible(bool visible)
        {
            if (Root != null && Root.gameObject.activeSelf != visible) Root.gameObject.SetActive(visible);
        }
    }
}
