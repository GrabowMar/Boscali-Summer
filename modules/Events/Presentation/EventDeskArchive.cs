using System;
using System.Collections.Generic;
using BoscaliSummer.Features.Events.Domain;
using NOAvionics;
using NOAvionics.Ui;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Features.Events.Presentation
{
    /// <summary>
    /// A local reading room, floating over the map on kit v2's <see cref="AvWindow"/> chrome. Four
    /// sections (aircraft / events / world / manual) share one stepper and one detail part; native
    /// encyclopedia data is read only while open.
    ///
    /// <para>Kit gap: v2 has no clickable virtualised list (<c>AvList</c>'s pooled rows take no click
    /// handler), so browsing a section's records uses an <c>AvStepper</c> (record N of M) instead of
    /// v1's paged, click-to-select index column. Kit gap: <c>AvWindow</c> has no auto-fit-to-viewport
    /// scaling, so this window uses a fixed size instead of v1's shrink-to-fit.</para>
    /// </summary>
    internal sealed class EventDeskArchive : MonoBehaviour
    {
        private const float Width = 860f;
        private const float Height = 700f;

        private AvWindow window;
        private readonly List<AircraftDefinition> aircraft = new List<AircraftDefinition>(128);
        private AvControl[] tabs;
        private AvSection indexSection;
        private AvStepper stepper;
        private EventDetailPart detail;
        private EventAircraftPreview preview;
        private int section, selected;
        private bool open, keyboardTouched, keyboardWas, pauseWas;

        internal bool IsOpen => open;

        internal static EventDeskArchive Create()
        {
            var go = new GameObject("BoscaliFieldArchive", typeof(RectTransform));
            var archive = go.AddComponent<EventDeskArchive>();
            archive.Build();
            return archive;
        }

        internal void Show(int initialSection)
        {
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
            window.Show();
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
            window?.Hide();
            Destroy(gameObject);
        }

        private void OnDestroy() => Close();

        private void Update()
        {
            if (!open) return;
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
            Canvas canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 30003;
            CanvasScaler scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
            gameObject.AddComponent<GraphicRaycaster>();

            window = AvWindow.Build(transform, "field-archive", "DIRECTORATE / FIELD ARCHIVE", Width, Height, 200);
            window.Closed += Close;
            AvFlow p = window.Body;

            string[] names = { "AIRCRAFT", "EVENTS", "WORLD", "MANUAL" };
            var specs = new AvControl.Spec[names.Length];
            for (int i = 0; i < names.Length; i++)
            {
                int target = i;
                specs[i] = new AvControl.Spec(names[i], () => SelectSection(target));
            }
            tabs = p.Buttons(specs).Controls;

            indexSection = p.Section(AvIcon.ListDetails, "INDEX", "0 RECORDS");
            stepper = p.Add(new AvStepper(p.Content, "RECORD", RecordLabel, () => Step(-1), () => Step(1)));

            p.Section(AvIcon.Database, "DETAIL");
            detail = p.Add(new EventDetailPart(p.Content));
            detail.Rotate = degrees => preview?.Rotate(degrees);

            window.Footer.Set("Local reading room · no signal leaves this cockpit.");
        }

        private string RecordLabel() => Count == 0 ? "0 / 0" : AvNum.Fixed(selected + 1, 0) + " / " + AvNum.Fixed(Count, 0);

        private void Step(int delta)
        {
            if (Count == 0) return;
            selected = ((selected + delta) % Count + Count) % Count;
            Populate();
        }

        private int Count => section == 0 ? aircraft.Count : section == 1 ? EventCatalog.All.Length :
            section == 2 ? EventDocs.World.Length : EventDocs.Guide.Length;

        private void SelectSection(int target)
        {
            section = Mathf.Clamp(target, 0, 3);
            for (int i = 0; i < tabs.Length; i++) tabs[i].Latched = i == section;
            selected = 0;
            Populate();
        }

        private void Populate()
        {
            preview?.Dispose();
            preview = null;
            indexSection.SetCaption(AvNum.Fixed(Count, 0) + " RECORDS");
            stepper.Refresh();
            if (Count == 0)
            {
                detail.ShowEmpty(section == 0 ? "AIRCRAFT INDEX UNAVAILABLE" : "NO RECORDS",
                    "The native encyclopedia has not loaded in this scene.");
                window.Body.RequestRelayout();
                return;
            }
            selected = Mathf.Clamp(selected, 0, Count - 1);
            if (section == 0) AircraftDetail(aircraft[selected]);
            else if (section == 1) EventDetail(EventCatalog.All[selected]);
            else DocDetail(section == 2 ? EventDocs.World[selected] : EventDocs.Guide[selected]);
            window.Body.RequestRelayout();
        }

        private void AircraftDetail(AircraftDefinition definition)
        {
            var info = definition.aircraftInfo;
            string figures = info != null
                ? "MAX SPEED " + AvNum.Fixed(info.maxSpeed, 0) + " m/s · STALL " + AvNum.Fixed(info.stallSpeed, 0) +
                  " m/s · EMPTY " + AvNum.Fixed(info.emptyWeight, 0) + " kg"
                : "PERFORMANCE FILE UNAVAILABLE";
            RawImage output = detail.ShowAircraft("NATIVE AIRCRAFT / " + definition.code,
                definition.unitName.ToUpperInvariant(), figures,
                definition.description ?? "No briefing is recorded for this aircraft.");
            bool hasModel = false;
            try { preview = new EventAircraftPreview(output); hasModel = preview.Load(definition); }
            catch (Exception) { preview?.Dispose(); preview = null; }
            detail.SetPreviewAvailable(hasModel);
        }

        private void EventDetail(EventDefinition definition)
        {
            string figures = "PRICE ×" + AvNum.Fixed(definition.SupportCostMultiplier, 2) +
                " · RESET ×" + AvNum.Fixed(definition.SupportCooldownMultiplier, 2) +
                " · WINDOW " + AvNum.Fixed(definition.DurationMinSeconds / 60, 0) + "–" +
                AvNum.Fixed(definition.DurationMaxSeconds / 60, 0) + " MIN";
            Sprite art = EventArtCache.Get(definition.IconKey, definition.IsSuper ? "tier_super" : "tier_medium");
            detail.ShowEvent(EventCatalog.TierLabel(definition.Tier).ToUpperInvariant() + " / " +
                EventCatalog.CategoryLabel(definition.Category).ToUpperInvariant() + " · " +
                EventCatalog.TargetLabel(definition.Target).ToUpperInvariant(),
                definition.Title.ToUpperInvariant(), art, figures, definition.FlavorText);
        }

        private void DocDetail(EventDocEntry entry) =>
            detail.ShowDoc(entry.Code, entry.Title.ToUpperInvariant(), entry.Body);
    }

    /// <summary>The archive's single detail pane: kicker, title, poster or model preview, figures, body.</summary>
    internal sealed class EventDetailPart : AvPart
    {
        private const float MediaHeight = 220f;
        private readonly TMP_Text kicker, title, figures, body;
        private readonly Image poster;
        private readonly RawImage modelView;
        private readonly AvControl rotateLeft, rotateRight;
        private readonly TMP_Text previewNote;
        private bool showModel, showPoster, previewAvailable = true;

        public Action<float> Rotate;

        public EventDetailPart(RectTransform parent)
        {
            Rect = AvLay.Child(parent, "Detail");
            kicker = AvText.Make(Rect, "Kicker", AvTextRole.Micro, "", TextAlignmentOptions.TopLeft);
            title = AvText.Make(Rect, "Title", AvTextRole.Title, "", TextAlignmentOptions.TopLeft, true);

            var posterGo = new GameObject("Poster", typeof(RectTransform), typeof(CanvasRenderer));
            posterGo.transform.SetParent(Rect, false);
            poster = posterGo.AddComponent<Image>();
            poster.preserveAspect = false;
            poster.raycastTarget = false;

            var modelGo = new GameObject("Model", typeof(RectTransform), typeof(CanvasRenderer));
            modelGo.transform.SetParent(Rect, false);
            modelView = modelGo.AddComponent<RawImage>();
            modelView.raycastTarget = false;

            rotateLeft = AvControl.Make(Rect, new AvControl.Spec("LEFT 30", () => Rotate?.Invoke(-30f), AvButtonStyle.Quiet, AvIcon.ChevronLeft));
            rotateRight = AvControl.Make(Rect, new AvControl.Spec("RIGHT 30", () => Rotate?.Invoke(30f), AvButtonStyle.Quiet, AvIcon.ChevronRight));
            previewNote = AvText.Make(Rect, "PreviewNote", AvTextRole.Micro, "", TextAlignmentOptions.TopLeft, true);
            figures = AvText.Make(Rect, "Figures", AvTextRole.DataSmall, "", TextAlignmentOptions.TopLeft, true);
            body = AvText.Make(Rect, "Body", AvTextRole.Prose, "", TextAlignmentOptions.TopLeft, true);
            Restyle();
        }

        public void ShowEmpty(string headline, string note)
        {
            kicker.text = "";
            title.text = headline ?? "";
            figures.text = "";
            body.text = note ?? "";
            showModel = false;
            showPoster = false;
            Restyle();
        }

        public void ShowDoc(string code, string headline, string text)
        {
            kicker.text = code ?? "";
            title.text = headline ?? "";
            figures.text = "";
            body.text = text ?? "";
            showModel = false;
            showPoster = false;
            Restyle();
        }

        public void ShowEvent(string kick, string headline, Sprite art, string figuresText, string flavorText)
        {
            kicker.text = kick ?? "";
            title.text = headline ?? "";
            poster.sprite = art;
            figures.text = figuresText ?? "";
            body.text = flavorText ?? "";
            showModel = false;
            showPoster = true;
            Restyle();
        }

        public RawImage ShowAircraft(string kick, string headline, string figuresText, string description)
        {
            kicker.text = kick ?? "";
            title.text = headline ?? "";
            figures.text = figuresText ?? "";
            body.text = description ?? "";
            showModel = true;
            showPoster = false;
            previewAvailable = true;
            Restyle();
            return modelView;
        }

        public void SetPreviewAvailable(bool available)
        {
            previewAvailable = available;
            Restyle();
        }

        public override float Measure(float width)
        {
            float w = width;
            float h = AvText.Height(kicker, w) + 4f + AvText.Height(title, w) + 8f;
            if (showModel) h += MediaHeight + 6f + 26f + 6f;
            else if (showPoster) h += MediaHeight + 8f;
            if (figures.text.Length > 0) h += AvText.Height(figures, w) + 6f;
            h += AvText.Height(body, w);
            return h;
        }

        public override void Place(AvSlot s)
        {
            base.Place(s);
            float w = s.W, y = 0f;
            float kh = AvText.Height(kicker, w);
            AvLay.Place(kicker.rectTransform, 0f, y, w, kh);
            y += kh + 4f;
            float th = AvText.Height(title, w);
            AvLay.Place(title.rectTransform, 0f, y, w, th);
            y += th + 8f;

            poster.gameObject.SetActive(showPoster);
            modelView.gameObject.SetActive(showModel);
            rotateLeft.gameObject.SetActive(showModel);
            rotateRight.gameObject.SetActive(showModel);
            previewNote.gameObject.SetActive(showModel && !previewAvailable);

            if (showModel)
            {
                AvLay.Place(modelView.rectTransform, 0f, y, w, MediaHeight);
                if (!previewAvailable) AvLay.Place(previewNote.rectTransform, 8f, y + MediaHeight * 0.5f - 10f, w - 16f, 20f);
                y += MediaHeight + 6f;
                AvLay.Place(rotateLeft.Rect, 0f, y, (w - 8f) * 0.5f, 26f);
                AvLay.Place(rotateRight.Rect, (w + 8f) * 0.5f, y, (w - 8f) * 0.5f, 26f);
                y += 26f + 6f;
            }
            else if (showPoster)
            {
                AvLay.Place(poster.rectTransform, 0f, y, w, MediaHeight);
                y += MediaHeight + 8f;
            }

            figures.gameObject.SetActive(figures.text.Length > 0);
            if (figures.text.Length > 0)
            {
                float fh = AvText.Height(figures, w);
                AvLay.Place(figures.rectTransform, 0f, y, w, fh);
                y += fh + 6f;
            }
            AvLay.Place(body.rectTransform, 0f, y, w, AvText.Height(body, w));
        }

        public override void Restyle()
        {
            kicker.color = AvStyleHost.FuiColor("key", AvTheme.RailInfo);
            title.color = AvStyleHost.FuiColor("ink", AvTheme.TextPrimary);
            figures.color = AvStyleHost.FuiColor("key", AvTheme.RailInfo);
            body.color = AvStyleHost.FuiColor("ink", AvTheme.TextPrimary);
            previewNote.text = "MODEL PREVIEW UNAVAILABLE";
            previewNote.color = AvStyleHost.FuiColor("ink-dim", AvTheme.Dim);
            rotateLeft.Restyle();
            rotateRight.Restyle();
        }
    }
}
