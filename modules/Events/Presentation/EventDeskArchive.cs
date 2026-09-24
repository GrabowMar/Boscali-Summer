using System;
using System.Collections.Generic;
using BoscaliSummer.Features.Events.Domain;
using NOAvionics.Ui;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Features.Events.Presentation
{
    /// <summary>A local reading room. Native encyclopedia data is read only when opened.</summary>
    internal sealed class EventDeskArchive : MonoBehaviour
    {
        private const float Width = 1160f;
        private const float Height = 820f;
        private const int Rows = 8;
        private Canvas canvas;
        private RectTransform frame, list, detail;
        private GameObject surface;
        private readonly List<AircraftDefinition> aircraft = new List<AircraftDefinition>();
        private readonly AvButton[] tabs = new AvButton[4];
        private EventAircraftPreview preview;
        private Vector2 fitted;
        private int section, page, selected;
        private bool open, keyboardTouched, keyboardWas, pauseWas;

        internal bool IsOpen => open;

        internal static EventDeskArchive Create()
        {
            var go = new GameObject("BoscaliFieldArchive", typeof(RectTransform), typeof(Canvas),
                typeof(CanvasScaler), typeof(GraphicRaycaster));
            var archive = go.AddComponent<EventDeskArchive>();
            archive.canvas = go.GetComponent<Canvas>();
            archive.canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            archive.canvas.sortingOrder = 30003;
            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
            archive.Build();
            archive.canvas.enabled = false;
            archive.surface.SetActive(false);
            return archive;
        }

        internal void Show(int initialSection)
        {
            Fit();
            canvas.enabled = true;
            surface.SetActive(true);
            if (!open)
            {
                pauseWas = GameplayUI.AllowPauseKeybind;
                GameplayUI.AllowPauseKeybind = false;
                keyboardTouched = Rewired.ReInput.isReady && Rewired.ReInput.controllers != null &&
                    Rewired.ReInput.controllers.Keyboard != null;
                if (keyboardTouched)
                {
                    keyboardWas = Rewired.ReInput.controllers.Keyboard.enabled;
                    Rewired.ReInput.controllers.Keyboard.enabled = false;
                }
                open = true;
            }
            LoadAircraft();
            SelectSection(initialSection);
        }

        internal void Close()
        {
            if (!open) return;
            open = false;
            preview?.Dispose();
            preview = null;
            GameplayUI.AllowPauseKeybind = pauseWas;
            if (keyboardTouched && Rewired.ReInput.isReady && Rewired.ReInput.controllers != null &&
                Rewired.ReInput.controllers.Keyboard != null)
                Rewired.ReInput.controllers.Keyboard.enabled = keyboardWas;
            keyboardTouched = false;
            AvButton.ClearTooltip();
            Destroy(gameObject);
        }

        private void OnDestroy() => Close();

        private void Update()
        {
            if (!open) return;
            Fit();
            if (Input.GetKeyDown(KeyCode.Escape)) Close();
        }

        private void LoadAircraft()
        {
            aircraft.Clear();
            if (Encyclopedia.i == null || Encyclopedia.i.aircraft == null) return;
            foreach (AircraftDefinition definition in Encyclopedia.i.aircraft)
                if (definition != null && definition.unitPrefab != null && definition.IsAllowed(false) &&
                    aircraft.Count < 128) aircraft.Add(definition);
            aircraft.Sort((a, b) => string.Compare(a.unitName, b.unitName, StringComparison.OrdinalIgnoreCase));
        }

        private void Build()
        {
            var root = (RectTransform)transform;
            surface = new GameObject("FieldArchiveSurface", typeof(RectTransform));
            var full = (RectTransform)surface.transform;
            full.SetParent(root, false);
            AvKit.Stretch(full);
            Image backdrop = AvRoomFrame.CreateBackdrop(full, .78f);
            backdrop.gameObject.AddComponent<Button>().onClick.AddListener(Close);
            CanvasGroup group;
            frame = AvRoomFrame.CreateFrame(full, "FieldArchiveFrame", out group);
            frame.sizeDelta = new Vector2(Width, Height);
            AvKit.Panel(frame, new Rect(0, 0, Width, Height), AvTheme.Ground, AvSprites.Panel).raycastTarget = true;
            Edge(new Rect(0, 0, Width, 1));
            Edge(new Rect(0, -Height + 1, Width, 1));
            Edge(new Rect(0, 0, 1, Height));
            Edge(new Rect(Width - 1, 0, 1, Height));
            Label(frame, "DIRECTORATE / FIELD ARCHIVE", new Rect(28, -24, 700, 28), 24, AvTheme.TextPrimary);
            Label(frame, "LOCAL READING ROOM  •  NO SIGNAL LEAVES THIS COCKPIT",
                new Rect(30, -57, 720, 20), 12, AvTheme.Dim);
            AvKit.Button(frame, "× CLOSE", new Rect(Width - 152, -25, 122, 34), Close);
            AvKit.Rule(frame, new Rect(26, -91, Width - 52, 2), AvTheme.RailInfo);
            string[] names = { "AIRCRAFT", "EVENTS", "WORLD", "MANUAL" };
            for (int i = 0; i < names.Length; i++)
            {
                int target = i;
                tabs[i] = AvKit.Button(frame, names[i], new Rect(26 + i * 278, -108, 269, 36),
                    () => SelectSection(target));
            }
            list = Panel("Index", new Rect(26, -157, 295, 627));
            detail = Panel("Detail", new Rect(333, -157, 801, 627));
            Fit();
        }

        private void Edge(Rect rect) => AvRoomFrame.CreateEdge(frame, rect, AvTheme.Frame);

        private RectTransform Panel(string name, Rect rect)
        {
            var image = AvKit.Panel(frame, rect, AvTheme.SurfaceInert);
            image.gameObject.name = name;
            return image.rectTransform;
        }

        private static TMP_Text Label(RectTransform parent, string text, Rect rect, float size,
            Color color, bool wrap = false) => AvKit.Label(parent, text, rect, color, size,
                FontStyles.Normal, TextAlignmentOptions.TopLeft, wrap);

        private void Fit()
        {
            if (frame == null) return;
            Rect canvasRect = ((RectTransform)transform).rect;
            float w = canvasRect.width > 0 ? canvasRect.width : 1920;
            float h = canvasRect.height > 0 ? canvasRect.height : 1080;
            if (Mathf.Abs(fitted.x - w) < .5f && Mathf.Abs(fitted.y - h) < .5f) return;
            fitted = new Vector2(w, h);
            Rect fit = AvRoomFrame.WindowRect(w, h);
            float scale = Mathf.Min(1f, Mathf.Min(fit.width / Width, fit.height / Height));
            frame.localScale = new Vector3(scale, scale, 1);
            frame.anchoredPosition = new Vector2(w * .5f, -h * .5f);
        }

        private void SelectSection(int target)
        {
            section = Mathf.Clamp(target, 0, 3);
            for (int i = 0; i < tabs.Length; i++) tabs[i].SetLatched(i == section);
            page = selected = 0;
            Populate();
        }

        private int Count => section == 0 ? aircraft.Count : section == 1 ? EventCatalog.All.Length :
            section == 2 ? EventDocs.World.Length : EventDocs.Guide.Length;

        private string RowTitle(int index) => section == 0 ? aircraft[index].unitName :
            section == 1 ? EventCatalog.All[index].Title :
            section == 2 ? EventDocs.World[index].Title : EventDocs.Guide[index].Title;

        private void Populate()
        {
            preview?.Dispose();
            preview = null;
            for (int i = list.childCount - 1; i >= 0; i--)
            {
                GameObject old = list.GetChild(i).gameObject;
                old.SetActive(false);
                Destroy(old);
            }
            for (int i = detail.childCount - 1; i >= 0; i--)
            {
                GameObject old = detail.GetChild(i).gameObject;
                old.SetActive(false);
                Destroy(old);
            }
            string[] sections = { "AIRFRAME REGISTRY", "EVENT DOSSIERS", "WORLD FILES", "FIELD MANUAL" };
            Label(list, sections[section], new Rect(16, -16, 260, 26), 16, AvTheme.TextPrimary);
            Label(list, Count + " RECORDS  /  SELECT ONE", new Rect(16, -45, 260, 20), 11, AvTheme.Dim);
            AvKit.Rule(list, new Rect(16, -70, 263, 1), AvTheme.Frame);
            for (int row = 0; row < Rows; row++)
            {
                int index = page * Rows + row;
                if (index >= Count) break;
                float y = -82 - row * 60;
                AvKit.Panel(list, new Rect(14, y, 267, 53), index == selected ? AvTheme.Surface : AvTheme.Ground);
                AvKit.Panel(list, new Rect(14, y, 3, 53), index == selected ? AvTheme.RailReady : AvTheme.RailInert);
                Label(list, (index + 1).ToString("00") + "  " + RowTitle(index).ToUpperInvariant(),
                    new Rect(25, y - 9, 244, 34), 12, index == selected ? AvTheme.TextPrimary : AvTheme.Dim, true);
                int choice = index;
                AvKit.HitButton(list, new Rect(14, y, 267, 53), () => { selected = choice; Populate(); });
            }
            AvKit.Button(list, "‹", new Rect(15, -566, 70, 32), () => ChangePage(-1));
            Label(list, (page + 1) + " / " + Mathf.Max(1, Mathf.CeilToInt(Count / (float)Rows)),
                new Rect(94, -571, 105, 25), 13, AvTheme.Dim);
            AvKit.Button(list, "›", new Rect(208, -566, 70, 32), () => ChangePage(1));
            if (Count == 0)
            {
                Label(detail, section == 0 ? "AIRCRAFT INDEX UNAVAILABLE" : "NO RECORDS",
                    new Rect(28, -35, 700, 30), 20, AvTheme.TextPrimary);
                Label(detail, "The native encyclopedia has not loaded in this scene.",
                    new Rect(28, -85, 700, 50), 14, AvTheme.Dim, true);
                return;
            }
            selected = Mathf.Clamp(selected, 0, Count - 1);
            if (section == 0) AircraftDetail(aircraft[selected]);
            else if (section == 1) EventDetail(EventCatalog.All[selected]);
            else DocDetail(section == 2 ? EventDocs.World[selected] : EventDocs.Guide[selected]);
        }

        private void ChangePage(int step)
        {
            page = Mathf.Clamp(page + step, 0, Mathf.Max(0, Mathf.CeilToInt(Count / (float)Rows) - 1));
            selected = Mathf.Min(Count - 1, page * Rows);
            Populate();
        }

        private void AircraftDetail(AircraftDefinition aircraftDefinition)
        {
            Label(detail, "NATIVE AIRCRAFT / " + aircraftDefinition.code, new Rect(26, -19, 740, 20), 12, AvTheme.RailInfo);
            Label(detail, aircraftDefinition.unitName.ToUpperInvariant(), new Rect(26, -46, 740, 34), 24, AvTheme.TextPrimary);
            var view = new GameObject("AirframeModel", typeof(RectTransform), typeof(RawImage));
            var rect = (RectTransform)view.transform;
            rect.SetParent(detail, false);
            AvKit.Place(rect, new Rect(26, -92, 749, 282));
            RawImage output = view.GetComponent<RawImage>();
            output.color = Color.white;
            output.raycastTarget = false;
            bool hasModel = false;
            try { preview = new EventAircraftPreview(output); hasModel = preview.Load(aircraftDefinition); }
            catch (Exception) { preview?.Dispose(); preview = null; }
            if (!hasModel) Label(detail, "MODEL PREVIEW UNAVAILABLE", new Rect(40, -210, 700, 32), 16, AvTheme.Dim);
            AvKit.Button(detail, "LEFT 30", new Rect(26, -386, 90, 30), () => preview?.Rotate(-30));
            AvKit.Button(detail, "RIGHT 30", new Rect(124, -386, 90, 30), () => preview?.Rotate(30));
            var info = aircraftDefinition.aircraftInfo;
            string figures = info != null ? "MAX SPEED  " + info.maxSpeed.ToString("0") + " m/s    •    STALL  " +
                info.stallSpeed.ToString("0") + " m/s    •    EMPTY  " + info.emptyWeight.ToString("0") + " kg" :
                "PERFORMANCE FILE UNAVAILABLE";
            Label(detail, figures, new Rect(26, -431, 750, 22), 13, AvTheme.RailInfo);
            Label(detail, aircraftDefinition.description ?? "No briefing is recorded for this aircraft.",
                new Rect(26, -470, 748, 135), 14, AvTheme.TextPrimary, true);
        }

        private void EventDetail(EventDefinition definition)
        {
            Label(detail, EventCatalog.TierLabel(definition.Tier).ToUpperInvariant() + " / " +
                EventCatalog.CategoryLabel(definition.Category).ToUpperInvariant(),
                new Rect(26, -18, 740, 20), 12, AvTheme.RailInfo);
            Label(detail, definition.Title.ToUpperInvariant(), new Rect(26, -47, 740, 38), 25, AvTheme.TextPrimary);
            Image art = AvKit.Panel(detail, new Rect(26, -100, 749, 255), Color.white);
            art.sprite = EventArtCache.Get(definition.IconKey,
                definition.IsSuper ? "tier_super" : "tier_medium");
            art.type = Image.Type.Simple;
            art.preserveAspect = false;
            Label(detail, "THEATER SCENARIO  /  " + EventCatalog.TargetLabel(definition.Target).ToUpperInvariant(),
                new Rect(26, -377, 740, 22), 12, AvTheme.RailInfo);
            Label(detail, definition.FlavorText, new Rect(26, -416, 742, 160), 15, AvTheme.TextPrimary, true);
            Label(detail, "PRICE ×" + definition.SupportCostMultiplier.ToString("0.00") +
                "   •   RESET ×" + definition.SupportCooldownMultiplier.ToString("0.00") +
                "   •   WINDOW " + definition.DurationMinSeconds / 60 + "–" +
                definition.DurationMaxSeconds / 60 + " MIN",
                new Rect(26, -574, 742, 22), 12, AvTheme.RailInfo);
        }

        private void DocDetail(EventDocEntry entry)
        {
            Label(detail, entry.Code, new Rect(28, -24, 740, 20), 12, AvTheme.RailInfo);
            Label(detail, entry.Title.ToUpperInvariant(), new Rect(28, -62, 740, 40), 26, AvTheme.TextPrimary);
            AvKit.Rule(detail, new Rect(28, -123, 742, 2), AvTheme.RailInfo);
            Label(detail, entry.Body, new Rect(28, -153, 740, 350), 18, AvTheme.TextPrimary, true);
            Label(detail, "DIRECTORATE ARCHIVE  /  LOCAL COPY", new Rect(28, -566, 740, 20), 11, AvTheme.Dim);
        }
    }
}
