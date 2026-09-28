using System;
using NOAvionics;
using NOAvionics.Ui;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Features.Radio.Presentation
{
    /// <summary>One built Boscali radio screen: root, the vanilla show/hide anchor, and the kit v2 console.</summary>
    internal sealed class RadioScreen
    {
        public GameObject Root;
        public GameObject DisplayPanel;
        public MFDScreen Screen;
        public AvConsole Console;
    }

    /// <summary>
    /// The scaffolding the radio panel needs: clone the stock panel's bay position, mount one
    /// kit v2 <see cref="AvConsole"/> inside it, and wire the vanilla bezel binding
    /// (<c>MFDScreen.displayPanel</c> / <c>label</c> / <c>highlight</c>). The receiver and deck
    /// pages differ in every part they add to their <see cref="AvFlow"/>; nothing about how the
    /// screen is mounted.
    /// </summary>
    internal static class RadioScreens
    {
        public static bool TryBuild(
            MFDScreen template, Button bezel, string id, string title,
            (AvIcon icon, string label)[] tabs, out RadioScreen built)
        {
            built = null;
            if (template == null) return false;

            var root = new GameObject("BoscaliRadio.Screen", typeof(RectTransform));
            var rootRect = (RectTransform)root.transform;
            rootRect.SetParent(template.transform.parent, false);

            RectTransform templateRect = (RectTransform)template.transform;
            rootRect.anchorMin = templateRect.anchorMin;
            rootRect.anchorMax = templateRect.anchorMax;
            rootRect.pivot = templateRect.pivot;
            rootRect.localScale = templateRect.localScale;
            // Position is deliberately not copied. VirtualMFD.showPos is Vector3.zero and
            // MFDScreen.ShowScreen assigns it straight to localPosition, so a screen has no
            // remembered home — it is placed by its parent and anchors, and an
            // anchoredPosition written here is overwritten whenever the panel is opened.
            float height = AvLay.ResolveHeight(
                templateRect.parent as RectTransform, AvTokens.PanelHeight, AvTokens.PanelHeightMax);
            rootRect.sizeDelta = new Vector2(AvTokens.PanelWidth, height);
            AvLay.ClampIntoCanvas(rootRect);

            // The vanilla MFDScreen toggles displayPanel's GameObject when the bezel button
            // cycles to a different screen on this slot; the whole kit v2 console mounts inside
            // it so one SetActive hides frame, chrome and every page together.
            var displayObject = new GameObject("Content", typeof(RectTransform));
            RectTransform display = displayObject.GetComponent<RectTransform>();
            display.SetParent(rootRect, false);
            display.anchorMin = Vector2.zero;
            display.anchorMax = Vector2.one;
            display.pivot = new Vector2(0.5f, 0.5f);
            display.offsetMin = Vector2.zero;
            display.offsetMax = Vector2.zero;

            AvConsole console = AvConsole.Build(display, id, title, tabs.Length, AvTokens.PanelWidth, height);
            console.Tabs(tabs);

            MFDScreen screen = root.AddComponent<MFDScreen>();
            screen.shortName = id;
            screen.displayPanel = displayObject;
            screen.aircraftOnly = false;
            screen.label = bezel == null ? null : bezel.GetComponentInChildren<TextMeshProUGUI>(true);
            screen.highlight = FindHighlight(bezel);
            if (screen.label == null || screen.highlight == null)
            {
                UnityEngine.Object.Destroy(root);
                return false;
            }

            built = new RadioScreen
            {
                Root = root,
                DisplayPanel = displayObject,
                Screen = screen,
                Console = console
            };
            return true;
        }

        public static Image FindHighlight(Button button)
        {
            if (button == null) return null;
            Image[] images = button.GetComponentsInChildren<Image>(true);
            for (int i = 0; i < images.Length; i++)
                if (images[i].gameObject != button.gameObject) return images[i];
            return button.GetComponent<Image>();
        }
    }
}
