using System.Reflection;
using BoscaliSummer.Modules.Hud.Domain;
using BoscaliSummer.Core.Contracts;
using HarmonyLib;
using NOAvionics;
using NuclearOption.UIStyleSystem;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Modules.Hud.Presentation
{
    /// <summary>
    /// The one status item this module draws: a compact card of plain UGUI objects parented
    /// directly under the native weapons panel (<c>HUDCanvas/HMDCenter/TopRightPanel</c>,
    /// resolved through <see cref="CombatHUD"/> the same way the reference KaceyTronic-RWR mod
    /// finds its own HUD canvas). No owned canvas, no CanvasGroup on a native object, no
    /// SetActive on anything we did not create ourselves: this panel is native's own child, so
    /// it shows and hides exactly when native HUD objects do -- cockpit and chase views always;
    /// orbit only while <c>Runtime.ExternalHudEnabler</c> is holding the native canvas open for
    /// the pilot's own aircraft; hidden in the map and while paused. Lost on scene reload;
    /// <see cref="Ensure"/> rebuilds lazily. Its position is unchanged by the 2026-09-28
    /// wingview-third-person restyle (a themed 1px border and a small "STATUS" header on a
    /// darker plate); the target-camera feed moved out to
    /// <see cref="ThirdPersonHudCluster"/>'s bottom-right card, third-person only.
    /// </summary>
    internal sealed class StatusPanel
    {
        private const float RowHeight = 22f;
        private const float DetailHeight = 18f;
        private const float RowFontSize = 13f;
        private const float DetailFontSize = 12f;
        private const float HeaderFontSize = 11f;
        private const float RowPad = 3f;
        private const float GlyphSize = 6f;
        private const float DockGap = 4f;
        private const float HeaderHeight = 16f;

        private static readonly FieldInfo TopRightPanelField = AccessTools.Field(typeof(CombatHUD), "topRightPanel");
        private static readonly FieldInfo WeaponStatusField = AccessTools.Field(typeof(CombatHUD), "weaponStatus");
        private static readonly FieldInfo NameTextField = AccessTools.Field(typeof(WeaponStatus), "nameText");

        private RectTransform dock;
        private RectTransform root;
        private CanvasGroup group;
        private Image backing;
        private Image border;
        private TextMeshProUGUI header;
        private Color nativeBackingColor = new Color(0f, 0f, 0f, 1f);
        private TMP_FontAsset font;
        private Material fontMaterial;

        private readonly Row[] rows = new Row[HudLayout.MaxRows];
        private float lastGroupAlpha = -1f;

        private struct Row
        {
            public RectTransform Root;
            public Image Glyph;
            public TextMeshProUGUI Text;
            public TextMeshProUGUI Detail;
            public Image Bar;
            public bool Active;
            public bool Initialized;
            public HudTone LastTone;
            public string LastText;
            public string LastDetail;
            public bool LastDetailsOn;
            public float LastBar;
            public float LastFontSize;
        }

        public bool Available => root != null;

        /// <summary>
        /// Find the native dock and, on first success or after it is lost, (re)build our own
        /// tree under it. Cheap when already built (two null checks). Never touches, replays or
        /// forces anything native -- only reads <see cref="CombatHUD.iconLayer"/> the way the
        /// reference mod does, to know the native HUD canvas actually exists this frame.
        /// </summary>
        public bool Ensure()
        {
            if (root != null && dock != null) return true;
            root = null;
            dock = null;

            CombatHUD hud = SceneSingleton<CombatHUD>.i;
            if (hud == null || hud.iconLayer == null || !hud.iconLayer.gameObject.activeInHierarchy) return false;
            Canvas hudCanvas = hud.iconLayer.GetComponentInParent<Canvas>();
            if (hudCanvas == null) return false;

            GameObject panelObject = TopRightPanelField?.GetValue(hud) as GameObject;
            RectTransform panelRect = panelObject != null ? panelObject.transform as RectTransform : null;
            if (panelRect == null || panelRect.GetComponentInParent<Canvas>() != hudCanvas) return false;

            Build(hud, panelRect);
            return root != null;
        }

        private void Build(CombatHUD hud, RectTransform panelRect)
        {
            dock = panelRect;

            WeaponStatus weaponStatus = WeaponStatusField?.GetValue(hud) as WeaponStatus;
            TextMeshProUGUI nameLabel = weaponStatus != null ? NameTextField?.GetValue(weaponStatus) as TextMeshProUGUI : null;
            font = nameLabel != null ? nameLabel.font : null;
            fontMaterial = nameLabel != null ? nameLabel.fontSharedMaterial : null;

            var rootObject = new GameObject("BoscaliStatusPanel", typeof(RectTransform));
            root = (RectTransform)rootObject.transform;
            root.SetParent(dock, false);
            root.anchorMin = new Vector2(0f, 0f);
            root.anchorMax = new Vector2(1f, 0f);
            root.pivot = new Vector2(0.5f, 1f);
            root.anchoredPosition = new Vector2(0f, -DockGap);
            root.sizeDelta = new Vector2(0f, RowHeight);

            group = rootObject.AddComponent<CanvasGroup>();
            group.interactable = false;
            group.blocksRaycasts = false;
            lastGroupAlpha = -1f;

            Image nativeImage = dock.GetComponent<Image>();
            if (nativeImage == null) nativeImage = dock.GetComponentInChildren<Image>(true);
            backing = rootObject.AddComponent<Image>();
            backing.raycastTarget = false;
            if (nativeImage != null)
            {
                backing.sprite = nativeImage.sprite;
                backing.type = nativeImage.type;
                backing.pixelsPerUnitMultiplier = nativeImage.pixelsPerUnitMultiplier;
                nativeBackingColor = nativeImage.color;
            }

            var borderObject = new GameObject("Border", typeof(RectTransform), typeof(Image));
            RectTransform borderRect = (RectTransform)borderObject.transform;
            borderRect.SetParent(root, false);
            // One hairline under the header, not a tinted copy of the whole panel.
            borderRect.anchorMin = new Vector2(0f, 1f);
            borderRect.anchorMax = new Vector2(1f, 1f);
            borderRect.pivot = new Vector2(0.5f, 1f);
            borderRect.anchoredPosition = new Vector2(0f, -HeaderHeight);
            borderRect.sizeDelta = new Vector2(-RowPad * 2f, 1f);
            border = borderObject.GetComponent<Image>();
            border.raycastTarget = false;
            border.color = new Color(0f, 0f, 0f, 0f); // filled with the tone colour per Present()

            header = BuildLabel("Header", TextAlignmentOptions.MidlineLeft);
            header.rectTransform.anchorMin = new Vector2(0f, 1f);
            header.rectTransform.anchorMax = new Vector2(1f, 1f);
            header.rectTransform.pivot = new Vector2(0f, 1f);
            header.rectTransform.anchoredPosition = new Vector2(RowPad, 0f);
            header.rectTransform.sizeDelta = new Vector2(-RowPad * 2f, HeaderHeight);
            header.text = "STATUS";
            // Fixed sizes in this canvas's units: the native label's own size (the HUD text
            // setting, ~40) ellipsised every row away in a short row.
            header.fontSize = HeaderFontSize;

            for (int i = 0; i < rows.Length; i++) rows[i] = BuildRow(i);
        }

        private TextMeshProUGUI BuildLabel(string name, TextAlignmentOptions alignment)
        {
            var labelObject = new GameObject(name, typeof(RectTransform));
            labelObject.transform.SetParent(root, false);
            TextMeshProUGUI label = labelObject.AddComponent<TextMeshProUGUI>();
            label.font = font;
            label.fontSharedMaterial = fontMaterial;
            label.alignment = alignment;
            label.enableWordWrapping = false;
            label.raycastTarget = false;
            return label;
        }

        private Row BuildRow(int index)
        {
            var rowObject = new GameObject("Row" + index, typeof(RectTransform));
            RectTransform rowRect = (RectTransform)rowObject.transform;
            rowRect.SetParent(root, false);
            rowRect.anchorMin = new Vector2(0f, 1f);
            rowRect.anchorMax = new Vector2(1f, 1f);
            rowRect.pivot = new Vector2(0f, 1f);
            rowRect.anchoredPosition = new Vector2(0f, -index * RowHeight);
            rowRect.sizeDelta = new Vector2(0f, RowHeight);

            var glyphObject = new GameObject("Glyph", typeof(RectTransform), typeof(Image));
            RectTransform glyphRect = (RectTransform)glyphObject.transform;
            glyphRect.SetParent(rowRect, false);
            glyphRect.anchorMin = new Vector2(0f, 0.5f);
            glyphRect.anchorMax = new Vector2(0f, 0.5f);
            glyphRect.pivot = new Vector2(0f, 0.5f);
            glyphRect.anchoredPosition = new Vector2(RowPad, 0f);
            glyphRect.sizeDelta = new Vector2(GlyphSize, GlyphSize);
            Image glyph = glyphObject.GetComponent<Image>();
            glyph.raycastTarget = false;

            var textObject = new GameObject("Text", typeof(RectTransform));
            RectTransform textRect = (RectTransform)textObject.transform;
            textRect.SetParent(rowRect, false);
            textRect.anchorMin = new Vector2(0f, 0f);
            textRect.anchorMax = new Vector2(1f, 1f);
            textRect.offsetMin = new Vector2(RowPad * 2f + GlyphSize, 1f);
            textRect.offsetMax = new Vector2(-RowPad, -1f);
            TextMeshProUGUI text = textObject.AddComponent<TextMeshProUGUI>();
            text.font = font;
            text.fontSharedMaterial = fontMaterial;
            text.fontSize = RowFontSize;
            text.alignment = TextAlignmentOptions.MidlineLeft;
            text.enableWordWrapping = false;
            text.overflowMode = TextOverflowModes.Ellipsis;
            text.raycastTarget = false;

            var detailObject = new GameObject("Detail", typeof(RectTransform));
            detailObject.transform.SetParent(rowRect, false);
            TextMeshProUGUI detail = detailObject.AddComponent<TextMeshProUGUI>();
            detail.font = font;
            detail.fontSharedMaterial = fontMaterial;
            detail.fontSize = DetailFontSize;
            detail.alignment = TextAlignmentOptions.MidlineLeft;
            detail.enableWordWrapping = false;
            detail.overflowMode = TextOverflowModes.Ellipsis;
            detail.raycastTarget = false;
            detail.gameObject.SetActive(false);

            var barObject = new GameObject("Bar", typeof(RectTransform), typeof(Image));
            RectTransform barRect = (RectTransform)barObject.transform;
            barRect.SetParent(rowRect, false);
            barRect.anchorMin = new Vector2(0f, 0f);
            barRect.anchorMax = new Vector2(1f, 0f);
            barRect.pivot = new Vector2(0f, 0f);
            barRect.offsetMin = new Vector2(RowPad * 2f + GlyphSize, 0f);
            barRect.offsetMax = new Vector2(-RowPad, 2f);
            Image bar = barObject.GetComponent<Image>();
            bar.raycastTarget = false;
            bar.sprite = AvSprites.White;
            bar.type = Image.Type.Filled;
            bar.fillMethod = Image.FillMethod.Horizontal;
            bar.fillOrigin = 0;
            bar.fillAmount = 0f;

            rowObject.SetActive(false);
            return new Row
            {
                Root = rowRect, Glyph = glyph, Text = text, Detail = detail, Bar = bar,
                Active = false, Initialized = false, LastBar = -1f, LastFontSize = -1f
            };
        }

        /// <summary>
        /// Refresh the panel from this tick's data. Called at most 10 Hz by the board; text,
        /// colour and fill writes only touch a component when its cached value actually
        /// changed. The target-camera feed is presented separately by
        /// <see cref="ThirdPersonHudCluster"/> since the 2026-09-28 restyle.
        /// </summary>
        public void Present(HudMessage[] snapshot, int count, bool showDetails, int contrast, int opacityStep,
            float scale, int offsetX, int offsetY)
        {
            if (root == null) return;

            float alpha = HudLayout.Opacity(opacityStep);
            if (count <= 0)
            {
                if (root.gameObject.activeSelf) root.gameObject.SetActive(false);
                return;
            }
            if (!root.gameObject.activeSelf) root.gameObject.SetActive(true);
            if (lastGroupAlpha != alpha) { group.alpha = alpha; lastGroupAlpha = alpha; }

            float contrastAlpha = contrast == 0 ? 0.12f : contrast == 2 ? 0.85f : 0.4f;
            Color backingColor = new Color(nativeBackingColor.r, nativeBackingColor.g, nativeBackingColor.b, nativeBackingColor.a * contrastAlpha);
            if (backing.color != backingColor) backing.color = backingColor;

            Color themeAllClear = ThemeManager.Active.ColorTheme.AllClear;
            Color borderColor = new Color(themeAllClear.r, themeAllClear.g, themeAllClear.b, 0.4f);
            if (border.color != borderColor) border.color = borderColor;
            if (header.color != themeAllClear) header.color = themeAllClear;

            float fontSize = RowFontSize * scale;
            float mainHeight = RowHeight * scale;
            const float headerHeight = HeaderHeight;
            float nextY = headerHeight;
            for (int i = 0; i < rows.Length; i++)
            {
                bool active = i < count;
                if (rows[i].Active != active)
                {
                    rows[i].Root.gameObject.SetActive(active);
                    rows[i].Active = active;
                }
                if (!active) continue;

                HudMessage message = snapshot[i];
                bool hasDetail = showDetails && !string.IsNullOrEmpty(message.Detail);
                float rowHeight = mainHeight + (hasDetail ? DetailHeight * scale : 0f);
                RectTransform rowRect = rows[i].Root;
                Vector2 wantPosition = new Vector2(0f, -nextY);
                nextY += rowHeight;
                if (rowRect.anchoredPosition != wantPosition) rowRect.anchoredPosition = wantPosition;
                Vector2 wantSize = new Vector2(0f, rowHeight);
                if (rowRect.sizeDelta != wantSize) rowRect.sizeDelta = wantSize;

                PlaceLine(rows[i].Text.rectTransform, 0f, mainHeight, scale);
                PlaceLine(rows[i].Detail.rectTransform, mainHeight, DetailHeight * scale, scale);
                rows[i].Glyph.rectTransform.anchorMin = rows[i].Glyph.rectTransform.anchorMax = new Vector2(0f, 1f);
                rows[i].Glyph.rectTransform.anchoredPosition = new Vector2(RowPad, -mainHeight * .5f);
                if (rows[i].Detail.gameObject.activeSelf != hasDetail) rows[i].Detail.gameObject.SetActive(hasDetail);
                Color toneColor = ToneColor(message.Tone);
                bool changed = !rows[i].Initialized || rows[i].LastTone != message.Tone ||
                    rows[i].LastText != message.Text || rows[i].LastDetail != message.Detail ||
                    rows[i].LastDetailsOn != showDetails;
                if (changed)
                {
                    rows[i].Glyph.color = toneColor;
                    rows[i].Text.color = toneColor;
                    rows[i].Detail.color = new Color(toneColor.r, toneColor.g, toneColor.b, .7f);
                    rows[i].Text.text = message.Text;
                    rows[i].Detail.text = hasDetail ? message.Detail : "";
                    rows[i].LastTone = message.Tone;
                    rows[i].LastText = message.Text;
                    rows[i].LastDetail = message.Detail;
                    rows[i].LastDetailsOn = showDetails;
                    rows[i].Initialized = true;
                }
                if (rows[i].LastFontSize != fontSize)
                {
                    rows[i].Text.fontSize = fontSize;
                    rows[i].Detail.fontSize = DetailFontSize * scale;
                    rows[i].LastFontSize = fontSize;
                }
                float barValue = showDetails ? message.Bar : 0f;
                if (rows[i].LastBar != barValue)
                {
                    rows[i].Bar.fillAmount = barValue;
                    rows[i].Bar.color = new Color(toneColor.r, toneColor.g, toneColor.b, 0.6f);
                    rows[i].LastBar = barValue;
                }
            }

            float totalHeight = nextY;
            Vector2 rootSize = new Vector2(0f, totalHeight);
            if (root.sizeDelta != rootSize) root.sizeDelta = rootSize;
            Vector2 rootPosition = new Vector2(offsetX, -DockGap + offsetY);
            if (root.anchoredPosition != rootPosition) root.anchoredPosition = rootPosition;
        }

        private static void PlaceLine(RectTransform rect, float top, float height, float scale)
        {
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(RowPad * 2f + GlyphSize, -top - scale);
            rect.sizeDelta = new Vector2(-RowPad * 3f - GlyphSize, height - 2f * scale);
        }

        private static Color ToneColor(HudTone tone)
        {
            ColorTheme theme = ThemeManager.Active.ColorTheme;
            switch (tone)
            {
                case HudTone.Caution: return theme.Warning;
                case HudTone.Warning: return theme.Alert;
                default: return theme.AllClear;
            }
        }

        /// <summary>Take our panel out of view without destroying it. Idempotent.</summary>
        public void Hide()
        {
            if (root != null && root.gameObject.activeSelf) root.gameObject.SetActive(false);
        }

        /// <summary>
        /// Drop every object this panel created. Called on scene reset/teardown; also safe to
        /// call when the native dock has already destroyed the tree for us (root/dock read as
        /// Unity's fake-null and the GameObject destroy is skipped).
        /// </summary>
        public void Destroy()
        {
            if (root != null) Object.Destroy(root.gameObject);
            root = null;
            dock = null;
            group = null;
            backing = null;
            border = null;
            header = null;
            for (int i = 0; i < rows.Length; i++) rows[i] = default;
        }
    }
}
