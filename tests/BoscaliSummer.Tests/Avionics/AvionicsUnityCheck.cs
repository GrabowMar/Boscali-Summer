#if UNITY_EDITOR
using System;
using System.IO;
using NOAvionics;
using NOAvionics.Ui;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Object = UnityEngine.Object;

/// <summary>Geometry and interaction checks against the real shared UI, not a mockup.</summary>
public static class AvionicsUnityCheck
{
    public static void Check()
    {
        var canvas = new GameObject("Shared UI check", typeof(Canvas)).GetComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        var root = (RectTransform)canvas.transform;
        root.sizeDelta = new Vector2(480f, 596f);
        AvKit.Panel(root, new Rect(0f, 0f, 480f, 596f), AvTheme.Ground);
        var content = new GameObject("Content", typeof(RectTransform)).GetComponent<RectTransform>();
        content.SetParent(root, false);
        AvKit.Place(content, new Rect(0f, 0f, 480f, 596f));
        var shell = AvScreen.Build(content, "OPS", new[] { "SPACE", "CYBER", "SPEC OPS", "INTEL" },
            new[] { new[] { "ALLOCATION", "POINTS" }, new[] { "ORBIT", "MODULES" },
                new[] { "CYBER", "ON NETWORK" } }, 3, 480f, 596f, null);
        shell.DataBar.State.text = "OPERATIONS / STATION CONTROL";
        shell.DataBar.SetChip(0, "NETWORK READY", true);
        shell.DataBar.SetChip(1, "LINK ESTABLISHED", "info");
        shell.DataBar.SetChip(2, "MAP IDLE", false);
        shell.Metrics[0].Set("19,429", "AVAILABLE", .75f, AvTheme.Accent);
        shell.Metrics[1].Set("12 / 15", "STATION ACTIVE", .8f, AvTheme.Accent);
        shell.Metrics[2].Set("5 / 6", "INFOCON 3", .83f, AvTheme.Warning);
        var page = (RectTransform)shell.CreatePage(0, "Pattern page").transform;
        shell.SetPage(0);
        Rect body = shell.Body;
        Rect inner = AvStyled.Section(page, new Rect(body.x, body.y, body.width, 106f),
            "ACTION HIERARCHY", "native theme");
        Require(inner.height > 60f, "Section must return its usable content height, not zero");
        float buttonWidth = (inner.width - 16f) / 3f;
        AvButton action = AvKit.Button(page, "OPEN PLANNER", new Rect(inner.x, inner.y, buttonWidth, 30f), null);
        AvKit.Button(page, "LAUNCH CORE", new Rect(inner.x + buttonWidth + 8f, inner.y, buttonWidth, 30f), null,
            11f, AvButtonStyle.Primary);
        AvButton selected = AvKit.Button(page, "LAYER ON", new Rect(inner.x + 2f * (buttonWidth + 8f), inner.y, buttonWidth, 30f), null,
            11f, AvButtonStyle.Toggle);
        selected.SetLatched(true);
        AvButton disabled = AvKit.Button(page, "UNAVAILABLE", new Rect(inner.x, inner.y - 38f, buttonWidth, 30f),
            () => throw new Exception("Disabled action fired"));
        disabled.SetEnabled(false);
        disabled.OnPointerClick(new PointerEventData(EventSystem.current) { button = PointerEventData.InputButton.Left });

        var gauges = new Image[3];
        for (int i = 0; i < gauges.Length; i++)
            gauges[i] = AvKit.ProgressBar(page,
                new Rect(inner.x + i * (buttonWidth + 8f), body.y - 116f, buttonWidth, 6f),
                i * .5f, AvTheme.Accent);
        Rect scrollArea = new Rect(body.x, body.y - 138f, body.width, body.height - 138f);
        RectTransform scrolling = AvScreen.Scroll(page, scrollArea, 600f, out Rect local);
        for (int i = 0; i < 12; i++)
        {
            float y = local.y - i * 48f;
            AvKit.Rule(scrolling, new Rect(local.x + 12f, y - 46f, local.width - 28f, 1f), AvTheme.Hairline);
            AvStyled.Label(scrolling, new Rect(12f, y - 4f, 280f, 18f), "READABLE ROW " + (i + 1), "row-name");
            AvStyled.Label(scrolling, new Rect(12f, y - 23f, 360f, 18f), "Full-width state and clear action labels.", "row-sub");
        }
        var lateCard = new GameObject("LateNativeCard", typeof(RectTransform));
        lateCard.transform.SetParent(content, false);
        shell.WriteStatus(null, null, "Hover for help. Selected controls retain their underline and state text.");
        Transform glass = content.Find("DisplayGlass");
        Require(glass != null && glass.GetSiblingIndex() == content.childCount - 1,
            "Display glass must stay above chrome and later-created page content");
        Image glassImage = glass.GetComponent<Image>();
        Require(glassImage != null && !glassImage.raycastTarget,
            "Display glass must never intercept MFD input");
        Require(page.parent.GetSiblingIndex() < glass.GetSiblingIndex(),
            "Pages built after the shell must remain behind display glass");
        Canvas.ForceUpdateCanvases();
        foreach (TMP_Text label in canvas.GetComponentsInChildren<TMP_Text>()) label.ForceMeshUpdate();
        for (int i = 0; i < gauges.Length; i++)
        {
            Require(gauges[i].sprite != null, "Filled progress images require a sprite");
            Mesh mesh = gauges[i].canvasRenderer.GetMesh();
            Require(i != 0 || mesh == null || mesh.vertexCount == 0, "Zero progress must draw no fill geometry");
            if (i > 0)
                Require(mesh != null && Mathf.Abs(mesh.bounds.size.x - gauges[i].rectTransform.rect.width * i * .5f) < .5f,
                    "Progress geometry must reflect 50/100 percent rather than a full quad");
        }
        Require(!shell.DataBar.State.isTextOverflowing, "Full screen identity must fit above telemetry");
        foreach (TMP_Text chip in shell.DataBar.Chips)
        {
            Require(!chip.isTextOverflowing, "Header telemetry must not truncate");
            Require(chip.fontSize >= 10f, "Header telemetry must retain the minimum font size");
        }
        foreach (AvStyled.Metric metric in shell.Metrics)
        {
            Require(!metric.Value.isTextOverflowing && !metric.Unit.isTextOverflowing,
                "Metric value and unit must fit independently");
            Require(metric.Value.rectTransform.anchoredPosition.y - metric.Value.rectTransform.sizeDelta.y
                >= metric.Unit.rectTransform.anchoredPosition.y, "Metric units must not overlap values");
            Require(metric.Fill.rectTransform.anchoredPosition.y < metric.Caption.rectTransform.anchoredPosition.y,
                "Metric progress track must stay below its caption");
        }
        ScrollRect scroll = canvas.GetComponentInChildren<ScrollRect>();
        Require(scroll != null && scroll.verticalScrollbar != null, "Overflow must have a visible scroll affordance");
        Require(scroll.verticalScrollbar.navigation.mode == Navigation.Mode.None,
            "Scroll controls must not capture flight navigation");
        Require(scroll.verticalScrollbar.size > 0f && scroll.verticalScrollbar.size < 1f,
            "Scrollbar thumb must communicate the visible fraction");
        Require(scroll.verticalNormalizedPosition > .99f, "A new page must open at its first row");
        Require(scroll.verticalScrollbar.handleRect.rect.height <= scroll.viewport.rect.height,
            "Scrollbar thumb must not extend outside its viewport");
        Require(local.width <= scrollArea.width - 8f, "Scrollable rows must reserve a separate thumb gutter");

        // Resize after construction: label and frame must follow the same bounds.
        ((RectTransform)action.transform).sizeDelta = new Vector2(buttonWidth + 10f, 32f);
        Canvas.ForceUpdateCanvases();
        Require(Mathf.Abs(action.GetComponentInChildren<TMP_Text>().rectTransform.rect.width - buttonWidth - 10f) < .1f,
            "Resized action must retain its label");
        ((RectTransform)action.transform).sizeDelta = new Vector2(buttonWidth, 30f);
        Canvas.ForceUpdateCanvases();
        Render(canvas);
        Object.DestroyImmediate(canvas.gameObject);
        AvButton.ClearTooltip();
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void Render(Canvas canvas)
    {
        Camera camera = new GameObject("Shared UI camera", typeof(Camera)).GetComponent<Camera>();
        camera.orthographic = true;
        camera.orthographicSize = 298f;
        camera.transform.position = new Vector3(0f, 0f, -10f);
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = AvTheme.Ground;
        var target = new RenderTexture(480, 596, 24);
        camera.targetTexture = target;
        camera.Render();
        RenderTexture.active = target;
        var image = new Texture2D(480, 596, TextureFormat.RGB24, false);
        image.ReadPixels(new Rect(0, 0, 480, 596), 0, 0);
        image.Apply();
        File.WriteAllBytes("SHARED-596.png", image.EncodeToPNG());
        RenderTexture.active = null;
        camera.targetTexture = null;
        Object.DestroyImmediate(image);
        Object.DestroyImmediate(target);
        Object.DestroyImmediate(camera.gameObject);
    }
}
#endif
