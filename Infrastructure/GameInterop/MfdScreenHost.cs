using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Runtime
{
    /// <summary>
    /// Hosts a mod screen on the vanilla MFD as an extra slot, for screens that do not fit
    /// the six free vanilla bezel buttons.
    ///
    /// <para>Boscali has seven screens and Wing Command contributes WMC, but the stock
    /// layout only leaves six free buttons. A screen that loses that race is not shown at
    /// all; a screen hosted here instead gets a button of its own appended to the vanilla
    /// column lists, which vanilla then treats exactly like any other slot:
    /// <c>SetupButtons</c> titles it, <c>PressLeftButton</c>/<c>PressRightButton</c> toggle
    /// it, <c>ToggleAllButtons</c> shows and hides it with the map, and
    /// <c>HideAllLeftScreens</c>/<c>HideAllRightScreens</c> close it. The rail adopts it
    /// like any borrowed button, so it is branded and latched with the rest.</para>
    ///
    /// <para>Wing Command's WMC installer scans the live lists for a free screen, so an
    /// appended slot is already occupied as far as it is concerned and the six vanilla
    /// slots stay available for WMC and the five claimed Boscali screens. Vanilla indexes
    /// its screens list by button index, so the button and the screen are always added and
    /// removed together, keeping the two lists the same length.</para>
    /// </summary>
    internal static class MfdScreenHost
    {
        private sealed class Hosted
        {
            public Button Button;
            public List<Button> Buttons;
            public List<MFDScreen> Screens;
        }

        private static readonly Dictionary<string, Hosted> hosted = new Dictionary<string, Hosted>();

        /// <summary>
        /// Append a host button to the preferred column and return the slot it occupies.
        /// The caller builds its screen against that button and then binds it through
        /// <see cref="MfdBezel.Bind"/>, exactly like a claimed vanilla slot.
        /// </summary>
        public static bool TryHost(
            string id, bool preferLeft, VirtualMFD mfd,
            out List<Button> buttons, out List<MFDScreen> screens, out int slot, out bool left)
        {
            buttons = null;
            screens = null;
            slot = -1;
            left = preferLeft;
            if (string.IsNullOrEmpty(id) || mfd == null || !GameAccess.MfdAvailable) return false;
            if (hosted.ContainsKey(id)) return false;

            List<Button> column = preferLeft
                ? GameAccess.GetLeftMfdButtons(mfd)
                : GameAccess.GetRightMfdButtons(mfd);
            List<MFDScreen> columnScreens = preferLeft
                ? GameAccess.GetLeftMfdScreens(mfd)
                : GameAccess.GetRightMfdScreens(mfd);
            if (column == null || columnScreens == null) return false;

            Button button = CreateButton(id, column);
            column.Add(button);
            columnScreens.Add(null); // filled by Bind, alongside its button

            buttons = column;
            screens = columnScreens;
            slot = column.Count - 1;
            hosted[id] = new Hosted { Button = button, Buttons = column, Screens = columnScreens };
            return true;
        }

        /// <summary>Remove a hosted slot and its button; safe to call when nothing is hosted.</summary>
        public static void Release(string id)
        {
            if (string.IsNullOrEmpty(id) || !hosted.TryGetValue(id, out Hosted entry)) return;
            hosted.Remove(id);

            if (entry.Buttons != null)
            {
                int index = entry.Button == null ? -1 : entry.Buttons.IndexOf(entry.Button);
                if (index >= 0)
                {
                    entry.Buttons.RemoveAt(index);
                    if (entry.Screens != null && index < entry.Screens.Count)
                        entry.Screens.RemoveAt(index);
                }
            }

            if (entry.Button != null) Object.Destroy(entry.Button.gameObject);
        }

        /// <summary>
        /// A button that reads as vanilla before the rail restyles it: the first sibling's
        /// fill, typography and size, with the label and highlight children every panel's
        /// Build resolves from its bezel button.
        /// </summary>
        private static Button CreateButton(string id, List<Button> column)
        {
            Button sibling = column.Count > 0 ? column[0] : null;
            var go = new GameObject("BoscaliHost_" + id, typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(sibling != null ? sibling.transform.parent : null, worldPositionStays: false);

            var rect = go.GetComponent<RectTransform>();
            var siblingRect = sibling != null ? sibling.transform as RectTransform : null;
            if (siblingRect != null)
            {
                rect.anchorMin = siblingRect.anchorMin;
                rect.anchorMax = siblingRect.anchorMax;
                rect.pivot = siblingRect.pivot;
                rect.sizeDelta = siblingRect.sizeDelta;
            }

            Image background = go.GetComponent<Image>();
            Image siblingImage = sibling != null ? sibling.GetComponent<Image>() : null;
            if (siblingImage != null)
            {
                background.sprite = siblingImage.sprite;
                background.type = siblingImage.type;
                background.color = siblingImage.color;
            }
            background.raycastTarget = true;

            var labelGo = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI));
            labelGo.transform.SetParent(go.transform, false);
            Stretch(labelGo.transform as RectTransform);
            TextMeshProUGUI label = labelGo.GetComponent<TextMeshProUGUI>();
            TMP_Text siblingLabel = sibling != null ? sibling.GetComponentInChildren<TMP_Text>(true) : null;
            if (siblingLabel != null)
            {
                label.font = siblingLabel.font;
                label.fontSize = siblingLabel.fontSize;
                label.fontSizeMin = siblingLabel.fontSizeMin;
                label.fontSizeMax = siblingLabel.fontSizeMax;
                label.enableAutoSizing = siblingLabel.enableAutoSizing;
                label.fontStyle = siblingLabel.fontStyle;
                label.color = siblingLabel.color;
            }
            label.text = id;
            label.alignment = TextAlignmentOptions.Center;
            label.richText = true;
            label.raycastTarget = false;

            var highlightGo = new GameObject("Highlight", typeof(RectTransform), typeof(Image));
            highlightGo.transform.SetParent(go.transform, false);
            Stretch(highlightGo.transform as RectTransform);
            Image highlight = highlightGo.GetComponent<Image>();
            highlight.color = Color.clear;
            highlight.raycastTarget = false;

            Button button = go.GetComponent<Button>();
            button.targetGraphic = background;
            button.enabled = false;
            go.SetActive(false);
            return button;
        }

        private static void Stretch(RectTransform rect)
        {
            if (rect == null) return;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            rect.localScale = Vector3.one;
        }
    }
}
