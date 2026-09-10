using BoscaliSummer.Features.QoL.Runtime;
using BoscaliSummer.Framework.Contracts;
using NOAvionics.Ui;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Features.QoL.Presentation
{
    /// <summary>One passive view of the native camera texture. Never owns or renders the feed.</summary>
    internal sealed class ThirdPersonCameraPanel
    {
        public static bool Available => NativeCamera.Available;

        private GameObject root;
        private RawImage image;
        private TMP_Text status;
        private TMP_Text selection;
        private TMP_Text unavailable;
        private TMP_Text contactAge, markStatus;
        private float nextReadout;
        private TargetCam source;
        private Camera camera;

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

            if (root == null) Create(owner);
            if (!root.activeSelf) root.SetActive(true);

            // Disabled cameras retain their last texture. Do not present that stale image as live.
            var mode = NativeCamera.ReadMode(source);
            bool targetMode = mode != TargetCam.CamMode.landingMode;
            RenderTexture texture = source.isActiveAndEnabled && targetMode && camera != null && camera.isActiveAndEnabled
                ? camera.targetTexture : null;
            bool live = texture != null && texture.IsCreated();
            image.texture = live ? texture : null;
            image.enabled = live;
            string caption = live ? "LIVE" : "NO SIGNAL";
            if (status.text != caption) status.text = caption;
            status.color = live ? AvTheme.Accent : new Color(1f, 0.72f, 0.3f);
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
            if (live)
            {
                // Preserve the native symbology and full image, including non-4:3 feeds.
                float scale = Mathf.Min(384f / texture.width, 288f / texture.height);
                image.rectTransform.sizeDelta = new Vector2(texture.width * scale, texture.height * scale);
            }
        }

        private void Create(Transform owner)
        {
            root = new GameObject("Boscali Third Person Camera", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            root.transform.SetParent(owner, false);
            Canvas canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 5;
            CanvasScaler scaler = root.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;

            var panelObject = new GameObject("Camera Panel", typeof(RectTransform));
            RectTransform panel = panelObject.GetComponent<RectTransform>();
            panel.SetParent(root.transform, false);
            panel.anchorMin = panel.anchorMax = new Vector2(1f, 0f);
            panel.pivot = new Vector2(1f, 0f);
            panel.anchoredPosition = new Vector2(-24f, 24f);
            panel.sizeDelta = new Vector2(408f, 402f);
            AvKit.Panel(panel, new Rect(0f, 0f, 408f, 402f), new Color(0.025f, 0.055f, 0.065f, 0.96f));
            AvKit.Panel(panel, new Rect(12f, -10f, 3f, 18f), AvTheme.Accent);
            AvKit.Label(panel, "TARGET VIEW", new Rect(23f, -6f, 240f, 26f), AvTheme.Accent, 16f);
            status = AvKit.Label(panel, "LIVE", new Rect(266f, -6f, 130f, 26f), AvTheme.Accent, 13f,
                FontStyles.Normal, TextAlignmentOptions.Right);
            AvKit.Panel(panel, new Rect(11f, -35f, 386f, 290f), new Color(0.2f, 0.45f, 0.38f, 1f));
            AvKit.Panel(panel, new Rect(12f, -36f, 384f, 288f), Color.black);
            selection = AvKit.Label(panel, "", new Rect(12f, -326f, 384f, 24f),
                new Color(0.68f, 0.8f, 0.75f), 13f);
            contactAge = AvKit.Label(panel, "", new Rect(12f, -348f, 384f, 20f), AvTheme.Dim, 12f);
            markStatus = AvKit.Label(panel, "", new Rect(12f, -369f, 384f, 28f), AvTheme.Dim, 11f);

            var imageObject = new GameObject("Camera Feed", typeof(RectTransform), typeof(RawImage));
            image = imageObject.GetComponent<RawImage>();
            image.raycastTarget = false;
            RectTransform rect = image.rectTransform;
            rect.SetParent(panel, false);
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 1f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = new Vector2(0f, -180f);
            unavailable = AvKit.Label(panel, "", new Rect(20f, -158f, 368f, 44f),
                new Color(0.68f, 0.8f, 0.75f), 15f, FontStyles.Normal, TextAlignmentOptions.Center);
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
            image = null;
            status = null;
            selection = null;
            unavailable = null;
            contactAge = markStatus = null;
            nextReadout = 0f;
        }
    }
}
