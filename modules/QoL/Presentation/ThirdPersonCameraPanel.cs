using BoscaliSummer.Features.QoL.Runtime;
using BoscaliSummer.Framework.Contracts;
using NOAvionics;
using NOAvionics.Ui;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Features.QoL.Presentation
{
    /// <summary>One passive view of the native camera texture. Never owns or renders the feed.</summary>
    internal sealed class ThirdPersonCameraPanel
    {
        private const float PanelWidth = 408f;
        private const float PanelPad = AvTokens.Space3;
        private const float HeaderHeight = AvTokens.TitleBarHeight;
        private const float FooterHeight = 64f;
        private const float SafeMargin = AvTokens.Space6;

        public static bool Available => NativeCamera.Available;

        private GameObject root;
        private Canvas canvas;
        private RectTransform panel;
        private Image feedGround;
        private Image[] feedFrame;
        private Image signalRail;
        private RawImage image;
        private AvStyled.DataBar dataBar;
        private TMP_Text selection;
        private TMP_Text unavailable;
        private TMP_Text contactAge, markStatus;
        private float nextReadout;
        private TargetCam source;
        private Camera camera;
        private int layoutScreenWidth = -1, layoutScreenHeight = -1;
        private int layoutTextureWidth = -1, layoutTextureHeight = -1;
        private float layoutScale = -1f;
        private Rect layoutSafeArea;

        public void Refresh(Aircraft aircraft, Transform owner)
        {
            var targets = aircraft != null && aircraft.weaponManager != null ? aircraft.weaponManager.GetTargetList() : null;
            if (!Available || aircraft == null || aircraft.targetCam == null || targets == null || targets.Count == 0)
            {
                Hide();
                return;
            }

            int selectedCount = 0;
            // UI work is bounded even if another mod expands the game's target list.
            for (int i = 0; i < Mathf.Min(targets.Count, 64); i++)
                if (targets[i] != null && targets[i] != aircraft && !targets[i].disabled) selectedCount++;
            if (selectedCount == 0) { Hide(); return; }

            TargetCam next = aircraft.targetCam;
            if (source != next || camera == null)
            {
                source = next;
                camera = NativeCamera.ReadCamera(source);
                nextReadout = 0f;
            }

            RenderTexture sourceTexture = camera != null ? camera.targetTexture : null;

            if (root == null) Create(owner);
            if (!root.activeSelf) root.SetActive(true);
            Layout(sourceTexture);

            // Disabled cameras retain their last texture. Do not present that stale image as live.
            var mode = NativeCamera.ReadMode(source);
            bool targetMode = mode != TargetCam.CamMode.landingMode;
            RenderTexture texture = source.isActiveAndEnabled && targetMode && camera != null && camera.isActiveAndEnabled
                ? sourceTexture : null;
            bool live = texture != null && texture.IsCreated();
            image.texture = live ? texture : null;
            image.enabled = live;
            string caption = live ? "LIVE" : "NO SIGNAL";
            dataBar.SetChip(0, caption, live ? "live" : "warn");
            signalRail.color = live ? AvTheme.RailReady : AvTheme.RailCaution;
            Color frameColor = live ? AvTheme.Hairline : AvTheme.RailCaution.WithAlpha(0.55f);
            for (int i = 0; i < feedFrame.Length; i++) feedFrame[i].color = frameColor;
            string selected = selectedCount + (selectedCount == 1 ? " TARGET SELECTED" : " TARGETS SELECTED");
            if (selection.text != selected) selection.text = selected;
            if (Time.unscaledTime >= nextReadout)
            {
                nextReadout = Time.unscaledTime + 0.25f;
                contactAge.text = NativeCamera.ContactFreshness(aircraft);
                ObservationManager observations = ObservationManager.Instance;
                if (observations != null && observations.TryGet(out ObservationPoint point))
                    markStatus.text = $"MARK X {point.X:0} / Z {point.Z:0} · {Time.unscaledTime - point.RecordedAt:0}s OLD";
                else
                    markStatus.text = observations != null ? observations.ShortcutHint + " · " + observations.Status : "";
            }
            unavailable.enabled = !live;
            unavailable.text = targetMode ? "TARGET CAMERA UNAVAILABLE" : "LANDING CAMERA ACTIVE";
        }

        private void Create(Transform owner)
        {
            root = new GameObject("Boscali Third Person Camera", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            root.transform.SetParent(owner, false);
            canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 5;
            CanvasScaler scaler = root.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;

            var panelObject = new GameObject("Camera Panel", typeof(RectTransform), typeof(Image));
            panel = panelObject.GetComponent<RectTransform>();
            panel.SetParent(root.transform, false);
            panel.anchorMin = panel.anchorMax = new Vector2(1f, 0f);
            panel.pivot = new Vector2(1f, 0f);
            panel.anchoredPosition = new Vector2(-SafeMargin, SafeMargin);
            panel.sizeDelta = new Vector2(PanelWidth, 1f);
            Image background = panelObject.GetComponent<Image>();
            background.sprite = AvSprites.Panel;
            background.type = Image.Type.Sliced;
            background.color = Color.white;
            background.raycastTarget = false;

            float contentWidth = PanelWidth - PanelPad * 2f;
            dataBar = AvStyled.TopBar(
                panel, new Rect(PanelPad, -PanelPad, contentWidth, HeaderHeight), "CAM", 1);
            dataBar.State.text = "TARGET VIEW";
            dataBar.SetChip(0, "NO SIGNAL", "warn");

            feedGround = AvKit.Panel(panel, new Rect(0f, 0f, 1f, 1f), AvTheme.SurfaceInert);

            var imageObject = new GameObject("Camera Feed", typeof(RectTransform), typeof(RawImage));
            image = imageObject.GetComponent<RawImage>();
            image.raycastTarget = false;
            RectTransform rect = image.rectTransform;
            rect.SetParent(panel, false);
            feedFrame = AvKit.Outline(panel, new Rect(0f, 0f, 1f, 1f), AvTheme.Hairline);

            unavailable = AvStyled.Label(
                panel, new Rect(0f, 0f, 1f, 1f), "", "row-main warn", align: TextAlignmentOptions.Center);
            selection = AvStyled.Label(panel, new Rect(0f, 0f, 1f, 1f), "", "row-name");
            contactAge = AvStyled.Label(panel, new Rect(0f, 0f, 1f, 1f), "", "row-sub");
            markStatus = AvStyled.Label(panel, new Rect(0f, 0f, 1f, 1f), "", "row-sub");
            signalRail = AvKit.Rule(panel, new Rect(0f, 0f, 3f, 1f), AvTheme.RailCaution);
        }

        private void Layout(RenderTexture texture)
        {
            if (panel == null || canvas == null) return;

            Rect safe = Screen.safeArea;
            if (safe.width <= 1f || safe.height <= 1f)
                safe = new Rect(0f, 0f, Screen.width, Screen.height);

            int textureWidth = texture != null && texture.width > 0 ? texture.width : 4;
            int textureHeight = texture != null && texture.height > 0 ? texture.height : 3;
            float canvasScale = Mathf.Max(0.01f, canvas.scaleFactor);

            if (layoutScreenWidth == Screen.width && layoutScreenHeight == Screen.height &&
                layoutTextureWidth == textureWidth && layoutTextureHeight == textureHeight &&
                Mathf.Approximately(layoutScale, canvasScale) && SameRect(layoutSafeArea, safe))
                return;

            layoutScreenWidth = Screen.width;
            layoutScreenHeight = Screen.height;
            layoutTextureWidth = textureWidth;
            layoutTextureHeight = textureHeight;
            layoutScale = canvasScale;
            layoutSafeArea = safe;

            float contentWidth = PanelWidth - PanelPad * 2f;
            float fixedHeight = PanelPad * 2f + HeaderHeight + AvTokens.Space2 * 2f + FooterHeight;
            float safeHeight = safe.height / canvasScale;
            float maxFeedHeight = Mathf.Max(96f, safeHeight - SafeMargin * 2f - fixedHeight);

            float feedWidth = contentWidth;
            float feedHeight = feedWidth * textureHeight / textureWidth;
            if (feedHeight > maxFeedHeight)
            {
                feedHeight = maxFeedHeight;
                feedWidth = feedHeight * textureWidth / textureHeight;
            }

            float panelHeight = fixedHeight + feedHeight;
            panel.sizeDelta = new Vector2(PanelWidth, panelHeight);

            float rightInset = Mathf.Max(0f, Screen.width - safe.xMax) / canvasScale;
            float bottomInset = Mathf.Max(0f, safe.yMin) / canvasScale;
            panel.anchoredPosition = new Vector2(-(SafeMargin + rightInset), SafeMargin + bottomInset);

            float feedX = PanelPad + (contentWidth - feedWidth) * 0.5f;
            float feedY = -(PanelPad + HeaderHeight + AvTokens.Space2);
            var feedRect = new Rect(feedX, feedY, feedWidth, feedHeight);
            AvKit.Place(feedGround.rectTransform, feedRect);
            AvKit.Place(image.rectTransform, feedRect);
            PlaceOutline(feedFrame, feedRect);
            AvKit.Place((RectTransform)unavailable.transform, feedRect);

            float footerY = feedY - feedHeight - AvTokens.Space2;
            AvKit.Place(signalRail.rectTransform, new Rect(PanelPad, footerY, 3f, FooterHeight));
            float copyX = PanelPad + AvTokens.Space3;
            float copyWidth = contentWidth - AvTokens.Space3;
            AvKit.Place((RectTransform)selection.transform, new Rect(copyX, footerY, copyWidth, 22f));
            AvKit.Place((RectTransform)contactAge.transform, new Rect(copyX, footerY - 21f, copyWidth, 19f));
            AvKit.Place((RectTransform)markStatus.transform, new Rect(copyX, footerY - 40f, copyWidth, 24f));
        }

        private static void PlaceOutline(Image[] outline, Rect area)
        {
            if (outline == null || outline.Length < 4) return;
            const float edge = 1f;
            AvKit.Place(outline[0].rectTransform, new Rect(area.x, area.y, area.width, edge));
            AvKit.Place(outline[1].rectTransform, new Rect(area.x, area.y - area.height + edge, area.width, edge));
            AvKit.Place(outline[2].rectTransform, new Rect(area.x, area.y, edge, area.height));
            AvKit.Place(outline[3].rectTransform, new Rect(area.x + area.width - edge, area.y, edge, area.height));
        }

        private static bool SameRect(Rect left, Rect right)
        {
            return Mathf.Approximately(left.x, right.x) && Mathf.Approximately(left.y, right.y) &&
                   Mathf.Approximately(left.width, right.width) && Mathf.Approximately(left.height, right.height);
        }

        private void Hide()
        {
            if (image != null) image.texture = null;
            if (root != null && root.activeSelf) root.SetActive(false);
            source = null;
            camera = null;
        }

        public void Destroy()
        {
            Hide();
            if (root != null) Object.Destroy(root);
            root = null;
            canvas = null;
            panel = null;
            feedGround = null;
            feedFrame = null;
            signalRail = null;
            image = null;
            dataBar = null;
            selection = null;
            unavailable = null;
            contactAge = markStatus = null;
            nextReadout = 0f;
            layoutScreenWidth = layoutScreenHeight = -1;
            layoutTextureWidth = layoutTextureHeight = -1;
            layoutScale = -1f;
            layoutSafeArea = default;
        }
    }
}
