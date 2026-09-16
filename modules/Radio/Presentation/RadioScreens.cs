using System;
using NOAvionics;
using NOAvionics.Ui;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Features.Radio.Presentation
{
    /// <summary>One built Boscali radio screen: root, chrome and the body it lays pages into.</summary>
    internal sealed class RadioScreen
    {
        public GameObject Root;
        public MFDScreen Screen;
        public AvScreen Shell;
        public Rect Body;
    }

    /// <summary>
    /// The scaffolding the radio panel needs: clone the stock panel's bay, build the shared
    /// chrome with its tab bar, and hand back the body rectangle. The receiver page and the
    /// deck page differ in every row they draw and in nothing about how they are mounted.
    /// </summary>
    internal static class RadioScreens
    {
        private const float Width = AvTokens.PanelWidth;

        public static bool TryBuild(
            MFDScreen template, Button bezel, string id, string rootName,
            string[] tabs, Action onPageChanged, out RadioScreen built)
        {
            built = null;
            if (template == null) return false;

            var root = new GameObject(rootName, typeof(RectTransform), typeof(Image));
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
            float height = AvScreen.ResolveHeight(
                templateRect.parent as RectTransform, AvTokens.PanelHeight, AvTokens.PanelHeightMax);
            rootRect.sizeDelta = new Vector2(Width, height);
            AvKit.ClampIntoCanvas(rootRect);

            Image background = root.GetComponent<Image>();
            background.sprite = AvSprites.Panel;
            background.type = Image.Type.Sliced;
            background.color = Color.white;
            background.raycastTarget = true;

            var contentObject = new GameObject("Content", typeof(RectTransform));
            RectTransform content = contentObject.GetComponent<RectTransform>();
            content.SetParent(rootRect, false);
            AvKit.Stretch(content);

            AvScreen shell = AvScreen.Build(
                content, id, tabs, null, 3,
                Width, height, _ => onPageChanged?.Invoke());

            MFDScreen screen = root.AddComponent<MFDScreen>();
            screen.shortName = id;
            screen.displayPanel = contentObject;
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
                Screen = screen,
                Shell = shell,
                Body = shell.Body
            };
            return true;
        }

        /// <summary>Wrap one page in a clipped scroll viewport when it is taller than the body.</summary>
        public static RectTransform ScrollPage(
            AvScreen shell, int index, string name, float contentHeight, out Rect area)
        {
            var page = (RectTransform)shell.CreatePage(index, name).transform;
            RectTransform target = AvScreen.Scroll(page, shell.Body, contentHeight, out area);
            page.gameObject.SetActive(true);
            return target;
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

    /// <summary>A downward layout cursor over a page rectangle, in panel coordinates.</summary>
    internal struct RadioCursor
    {
        private readonly Rect area;

        public RadioCursor(Rect area)
        {
            this.area = area;
            Y = area.y;
        }

        public float Y { get; private set; }
        public float X => area.x;
        public float Width => area.width;

        public Rect Take(float height)
        {
            var rect = new Rect(area.x, Y, area.width, height);
            Y -= height + AvTokens.Space2;
            return rect;
        }

        public Rect Take(float height, float gap)
        {
            var rect = new Rect(area.x, Y, area.width, height);
            Y -= height + gap;
            return rect;
        }

        public void Skip(float height) => Y -= height;
    }
}
