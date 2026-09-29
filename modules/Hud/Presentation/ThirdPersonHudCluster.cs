using System.Reflection;
using BoscaliSummer.Features.Hud.Domain;
using HarmonyLib;
using NOAvionics;
using NOAvionics.Ui;
using NuclearOption.UIStyleSystem;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Features.Hud.Presentation
{
    /// <summary>
    /// The screen-fixed flight cluster and the relocated target-camera card
    /// (docs/superpowers/specs/2026-09-28-wingview-third-person-design.md, "Third-person HUD").
    /// Our own objects, parented directly under the native HUDCanvas root (found the same way
    /// <see cref="StatusPanel"/> finds its own dock), active only while the caller says third
    /// person is showing. All geometry comes from <see cref="ThirdPersonHudLayout"/>; this class
    /// only builds/positions widgets and writes text on change. Numbers are formatted through
    /// <see cref="AvNumFormat"/>/<see cref="AvUnitTable"/> into a reused buffer -- a string is
    /// allocated only the tick a displayed value actually changes, never every frame.
    /// </summary>
    internal sealed class ThirdPersonHudCluster
    {
        private static readonly FieldInfo WeaponStatusField = AccessTools.Field(typeof(CombatHUD), "weaponStatus");
        private static readonly FieldInfo NameTextField = AccessTools.Field(typeof(WeaponStatus), "nameText");

        private readonly char[] buf = new char[32];

        private RectTransform canvasRoot;
        private RectTransform root;
        private TMP_FontAsset font;
        private Material fontMaterial;

        private Box spd, alt;
        private Box hdg;
        private TargetCardWidgets card;

        private bool builtActive;

        private struct Box
        {
            public RectTransform Root;
            public Image Backing;
            public TextMeshProUGUI Value;
            public TextMeshProUGUI SubLine;
            public Image Bar;
            public float LastValue;
            public float LastSub;
            public bool Initialized;
        }

        private struct TargetCardWidgets
        {
            public RectTransform Root;
            public Image Border;
            public Image Backing;
            public TextMeshProUGUI Header;
            public RawImage Feed;
            public bool Active;
            public string LastHeader;
        }

        public bool Available => root != null;

        public bool Ensure()
        {
            if (root != null && canvasRoot != null) return true;
            root = null;
            canvasRoot = null;

            CombatHUD hud = SceneSingleton<CombatHUD>.i;
            if (hud == null || hud.iconLayer == null || !hud.iconLayer.gameObject.activeInHierarchy) return false;
            Canvas hudCanvas = hud.iconLayer.GetComponentInParent<Canvas>();
            if (hudCanvas == null) return false;

            Build(hud, hudCanvas);
            return root != null;
        }

        private void Build(CombatHUD hud, Canvas hudCanvas)
        {
            canvasRoot = hudCanvas.transform as RectTransform;
            if (canvasRoot == null) canvasRoot = hudCanvas.GetComponent<RectTransform>();

            WeaponStatus weaponStatus = WeaponStatusField?.GetValue(hud) as WeaponStatus;
            TextMeshProUGUI nameLabel = weaponStatus != null ? NameTextField?.GetValue(weaponStatus) as TextMeshProUGUI : null;
            font = nameLabel != null ? nameLabel.font : null;
            fontMaterial = nameLabel != null ? nameLabel.fontSharedMaterial : null;

            var rootObject = new GameObject("BoscaliWingviewCluster", typeof(RectTransform));
            root = (RectTransform)rootObject.transform;
            root.SetParent(canvasRoot, false);
            root.anchorMin = new Vector2(0.5f, 0.5f);
            root.anchorMax = new Vector2(0.5f, 0.5f);
            root.pivot = new Vector2(0.5f, 0.5f);
            root.anchoredPosition = Vector2.zero;
            root.sizeDelta = Vector2.zero;

            spd = BuildBox("SPD");
            alt = BuildBox("ALT");
            hdg = BuildBox("HDG", withBar: false);
            card = BuildTargetCard();

            rootObject.SetActive(false);
            builtActive = false;
        }

        private Box BuildBox(string name, bool withBar = true)
        {
            var boxObject = new GameObject(name, typeof(RectTransform), typeof(Image));
            RectTransform rect = (RectTransform)boxObject.transform;
            rect.SetParent(root, false);
            rect.pivot = new Vector2(0.5f, 0.5f);
            Image backing = boxObject.GetComponent<Image>();
            backing.raycastTarget = false;
            backing.color = new Color(0f, 0f, 0f, 0.35f);

            TextMeshProUGUI value = BuildText(rect, "Value", TextAlignmentOptions.Center, 22f);
            value.rectTransform.anchorMin = new Vector2(0f, 0.4f);
            value.rectTransform.anchorMax = new Vector2(1f, 1f);
            value.rectTransform.offsetMin = Vector2.zero;
            value.rectTransform.offsetMax = Vector2.zero;

            TextMeshProUGUI sub = BuildText(rect, "SubLine", TextAlignmentOptions.Center, 13f);
            sub.rectTransform.anchorMin = new Vector2(0f, 0f);
            sub.rectTransform.anchorMax = new Vector2(1f, 0.4f);
            sub.rectTransform.offsetMin = Vector2.zero;
            sub.rectTransform.offsetMax = Vector2.zero;
            sub.color = new Color(1f, 1f, 1f, 0.7f);

            Image bar = null;
            if (withBar)
            {
            var barObject = new GameObject("Bar", typeof(RectTransform), typeof(Image));
            RectTransform barRect = (RectTransform)barObject.transform;
            barRect.SetParent(root, false);
            barRect.pivot = new Vector2(0.5f, 0f);
            bar = barObject.GetComponent<Image>();
            bar.raycastTarget = false;
            // Filled needs a sprite: without one Unity ignores fillAmount and draws the full rect.
            bar.sprite = AvSprites.White;
            bar.type = Image.Type.Filled;
            bar.fillMethod = Image.FillMethod.Vertical;
            bar.fillOrigin = 0;
            bar.fillAmount = 0f;
            }

            return new Box { Root = rect, Backing = backing, Value = value, SubLine = sub, Bar = bar, LastValue = float.NaN, LastSub = float.NaN };
        }

        private TextMeshProUGUI BuildText(Transform parent, string name, TextAlignmentOptions alignment, float size)
        {
            var textObject = new GameObject(name, typeof(RectTransform));
            textObject.transform.SetParent(parent, false);
            TextMeshProUGUI text = textObject.AddComponent<TextMeshProUGUI>();
            text.font = font;
            text.fontSharedMaterial = fontMaterial;
            text.fontSize = size;
            text.alignment = alignment;
            text.enableWordWrapping = false;
            text.raycastTarget = false;
            return text;
        }

        private TargetCardWidgets BuildTargetCard()
        {
            var cardObject = new GameObject("TargetCard", typeof(RectTransform), typeof(Image));
            RectTransform rect = (RectTransform)cardObject.transform;
            rect.SetParent(root, false);
            rect.pivot = new Vector2(0.5f, 0.5f);
            Image backing = cardObject.GetComponent<Image>();
            backing.raycastTarget = false;
            backing.color = new Color(0f, 0f, 0f, 0.55f);

            var borderObject = new GameObject("Border", typeof(RectTransform), typeof(Image));
            RectTransform borderRect = (RectTransform)borderObject.transform;
            borderRect.SetParent(rect, false);
            // A thin header strip, not a full tinted overlay behind the feed.
            borderRect.anchorMin = new Vector2(0f, 1f);
            borderRect.anchorMax = new Vector2(1f, 1f);
            borderRect.pivot = new Vector2(0.5f, 1f);
            borderRect.anchoredPosition = Vector2.zero;
            borderRect.sizeDelta = new Vector2(0f, ThirdPersonHudLayout.TargetCardHeaderHeight);
            Image border = borderObject.GetComponent<Image>();
            border.raycastTarget = false;
            border.color = new Color(1f, 1f, 1f, 0f); // tinted per theme in Present()

            TextMeshProUGUI header = BuildText(rect, "Header", TextAlignmentOptions.MidlineLeft, 12f);
            header.rectTransform.anchorMin = new Vector2(0f, 1f);
            header.rectTransform.anchorMax = new Vector2(1f, 1f);
            header.rectTransform.pivot = new Vector2(0f, 1f);
            header.rectTransform.anchoredPosition = new Vector2(4f, 0f);
            header.rectTransform.sizeDelta = new Vector2(-8f, ThirdPersonHudLayout.TargetCardHeaderHeight);

            var feedObject = new GameObject("Feed", typeof(RectTransform), typeof(RawImage));
            RectTransform feedRect = (RectTransform)feedObject.transform;
            feedRect.SetParent(rect, false);
            feedRect.anchorMin = new Vector2(0f, 0f);
            feedRect.anchorMax = new Vector2(1f, 1f);
            feedRect.offsetMin = new Vector2(1f, 1f);
            feedRect.offsetMax = new Vector2(-1f, -ThirdPersonHudLayout.TargetCardHeaderHeight);
            RawImage feed = feedObject.GetComponent<RawImage>();
            feed.raycastTarget = false;

            cardObject.SetActive(false);
            return new TargetCardWidgets { Root = rect, Backing = backing, Border = border, Header = header, Feed = feed, Active = false };
        }

        /// <summary>Refresh from this tick's flight data. <paramref name="active"/> is the
        /// caller's third-person-own-aircraft gate; false hides the whole cluster (native-child
        /// style: no per-widget SetActive churn beyond the one root toggle). Called at the same
        /// cadence as <see cref="StatusPanel.Present"/> (throttled by the caller).</summary>
        public void Present(bool active, AvUnits units,
            float speedMps, float altitudeM, float climbMps, float headingDeg, float mach, float gForce,
            float fuelFraction, float throttleFraction,
            bool cameraVisible, Texture cameraTexture, string cameraCode, string cameraRange)
        {
            if (root == null) return;
            if (active != builtActive)
            {
                root.gameObject.SetActive(active);
                builtActive = active;
            }
            if (!active) return;

            // Reference frame is the HUD canvas's own local rect, not raw Screen.width/height:
            // anchoredPosition under this canvas is in the canvas's own (possibly scaled) units.
            float screenW = canvasRoot.rect.width;
            float screenH = canvasRoot.rect.height;
            if (screenW <= 0f || screenH <= 0f) return;

            ColorTheme theme = ThemeManager.Active.ColorTheme;
            Color allClear = theme.AllClear;

            PositionBox(spd, ThirdPersonHudLayout.SpeedBox(screenW, screenH));
            PositionBox(alt, ThirdPersonHudLayout.AltitudeBox(screenW, screenH));
            PositionBox(hdg, ThirdPersonHudLayout.HeadingBox(screenW, screenH));

            RectF spdBar = ThirdPersonHudLayout.SideBar(ThirdPersonHudLayout.SpeedBox(screenW, screenH), outwardIsLeft: true);
            RectF altBar = ThirdPersonHudLayout.SideBar(ThirdPersonHudLayout.AltitudeBox(screenW, screenH), outwardIsLeft: false);
            PositionRect((RectTransform)spd.Bar.transform, spdBar);
            PositionRect((RectTransform)alt.Bar.transform, altBar);
            if (spd.Bar.fillAmount != Mathf.Clamp01(throttleFraction)) spd.Bar.fillAmount = Mathf.Clamp01(throttleFraction);
            spd.Bar.color = allClear;
            if (alt.Bar.fillAmount != Mathf.Clamp01(fuelFraction)) alt.Bar.fillAmount = Mathf.Clamp01(fuelFraction);
            alt.Bar.color = fuelFraction < 0.2f ? theme.Warning : allClear;

            spd.Value.color = alt.Value.color = hdg.Value.color = allClear;
            spd.SubLine.color = alt.SubLine.color = hdg.SubLine.color = new Color(allClear.r, allClear.g, allClear.b, 0.9f);
            spd.Backing.color = alt.Backing.color = hdg.Backing.color = new Color(0.01f, 0.03f, 0.05f, 0.55f);

            WriteSpeed(speedMps, units);
            WriteAltitude(altitudeM, climbMps, units);
            WriteHeading(headingDeg);
            WriteMachG(mach, gForce);
            WriteClimb(climbMps, units);

            PresentTargetCard(cameraVisible, screenW, screenH, allClear, cameraTexture, cameraCode, cameraRange);
        }

        private void PositionBox(Box box, RectF rect) => PositionRect(box.Root, rect);

        private static void PositionRect(RectTransform rt, RectF rect)
        {
            Vector2 pos = new Vector2(rect.CenterX, rect.CenterY);
            if (rt.anchoredPosition != pos) rt.anchoredPosition = pos;
            Vector2 size = new Vector2(rect.Width, rect.Height);
            if (rt.sizeDelta != size) rt.sizeDelta = size;
        }

        private void WriteSpeed(float speedMps, AvUnits units)
        {
            if (!float.IsFinite(speedMps)) return;
            float rounded = Mathf.Round(AvUnitTable.Speed(speedMps, units));
            if (spd.Initialized && spd.LastValue == rounded) return;
            int len = AvUnitTable.SpeedReading(buf, 0, speedMps, units);
            spd.Value.text = new string(buf, 0, len);
            spd.LastValue = rounded;
            spd.Initialized = true;
        }

        private void WriteAltitude(float altitudeM, float climbMps, AvUnits units)
        {
            if (!float.IsFinite(altitudeM)) return;
            float rounded = Mathf.Round(AvUnitTable.Altitude(altitudeM, units));
            if (alt.Initialized && alt.LastValue == rounded) return;
            int len = AvUnitTable.AltitudeReading(buf, 0, altitudeM, units);
            alt.Value.text = new string(buf, 0, len);
            alt.LastValue = rounded;
            alt.Initialized = true;
        }

        private void WriteHeading(float headingDeg)
        {
            float normalized = ((headingDeg % 360f) + 360f) % 360f;
            float rounded = Mathf.Round(normalized);
            if (hdg.Initialized && hdg.LastValue == rounded) return;
            int len = AvNumFormat.Write(buf, 0, rounded, 0);
            len = AvNumFormat.Append(buf, len, "°");
            hdg.Value.text = new string(buf, 0, len);
            hdg.LastValue = rounded;
            hdg.Initialized = true;
        }

        private void WriteMachG(float mach, float gForce)
        {
            if (!float.IsFinite(mach) || !float.IsFinite(gForce)) return;
            // Coarse change detection: round to the display's own precision so sub-pixel float
            // jitter every frame does not force a string allocation every frame.
            float key = Mathf.Round(mach * 100f) + Mathf.Round(gForce * 10f) * 10000f;
            if (spd.Initialized && spd.LastSub == key) return;

            int len = AvNumFormat.Append(buf, 0, "M ");
            len = AvNumFormat.Write(buf, len, mach, 2);
            len = AvNumFormat.Append(buf, len, "  G ");
            len = AvNumFormat.Write(buf, len, gForce, 1);
            spd.SubLine.text = new string(buf, 0, len);
            spd.LastSub = key;
        }

        private void WriteClimb(float climbMps, AvUnits units)
        {
            if (!float.IsFinite(climbMps)) return;
            float key = Mathf.Round(AvUnitTable.Climb(climbMps, units));
            if (alt.Initialized && alt.LastSub == key) return;

            int len = AvUnitTable.ClimbRateReading(buf, 0, climbMps, units);
            alt.SubLine.text = new string(buf, 0, len);
            alt.LastSub = key;
        }

        private void PresentTargetCard(bool visible, float screenW, float screenH, Color allClear,
            Texture cameraTexture, string code, string range)
        {
            bool show = visible && cameraTexture != null;
            if (card.Active != show)
            {
                card.Root.gameObject.SetActive(show);
                card.Active = show;
            }
            if (!show) return;

            RectF rect = ThirdPersonHudLayout.TargetCard(screenW, screenH, true);
            PositionRect(card.Root, rect);
            card.Border.color = new Color(allClear.r, allClear.g, allClear.b, 0.18f);
            card.Backing.color = new Color(0f, 0f, 0f, 0.55f);
            if (!ReferenceEquals(card.Feed.texture, cameraTexture)) card.Feed.texture = cameraTexture;

            string header = "TGT · " + (code ?? "--") + " · " + (range ?? "--");
            if (card.LastHeader != header)
            {
                card.Header.text = header;
                card.Header.color = allClear;
                card.LastHeader = header;
            }
        }

        /// <summary>Take the cluster out of view without destroying it. Idempotent.</summary>
        public void Hide()
        {
            if (root != null && root.gameObject.activeSelf) root.gameObject.SetActive(false);
            builtActive = false;
            if (card.Feed != null) card.Feed.texture = null;
        }

        /// <summary>Drop every object this cluster created. Safe when the native canvas already
        /// took the tree with it (root reads as Unity's fake-null and the destroy is a no-op).</summary>
        public void Destroy()
        {
            if (root != null) Object.Destroy(root.gameObject);
            root = null;
            canvasRoot = null;
            spd = default;
            alt = default;
            hdg = default;
            card = default;
            builtActive = false;
        }
    }
}
