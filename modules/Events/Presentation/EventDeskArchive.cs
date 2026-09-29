using System;
using System.Collections.Generic;
using BoscaliSummer.Features.Events.Domain;
using NOAvionics;
using NOAvionics.Ui;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Features.Events.Presentation
{
    /// <summary>
    /// A local reading room, floating over the map on kit v2's <see cref="AvWindow"/> chrome. The window is
    /// two panes under one tab bar: a paged index on the left (about 40%) and the detail of the selected
    /// record on the right. Four sections (aircraft / events / world / manual) share both panes; native
    /// encyclopedia data is read only while open. The window is sized to the screen and each pane scrolls
    /// on its own, so nothing is ever taller than the window.
    /// </summary>
    internal sealed class EventDeskArchive : MonoBehaviour
    {
        private const float MaxWidth = 1040f;
        private const float MaxHeight = 960f;
        private const float ScreenMargin = 40f;
        private const int IndexRows = 14;
        private const int SortingOrder = 30003;

        // AvWindow chrome the pane height is carved out of.
        private const float TitleHeight = 30f;

        private AvWindow window;
        private readonly List<AircraftDefinition> aircraft = new List<AircraftDefinition>(128);
        private AvControl[] tabs;
        private ArchivePanesPart panes;
        private AvSection indexSection;
        private AvList index;
        private EventDetailPart detail;
        private EventAircraftPreview preview;
        private int section, selected;
        private bool open, keyboardTouched, keyboardWas, pauseWas;

        internal bool IsOpen => open;
        internal AvWindow Window => window;

        /// <summary>The canvas-unit size of the screen the window is scaled to (1920x1080 reference, Expand).</summary>
        internal static Vector2 ScreenUnits()
        {
            float scale = Mathf.Min(Screen.width / 1920f, Screen.height / 1080f);
            if (scale <= 0.001f) return new Vector2(1920f, 1080f);
            return new Vector2(Screen.width / scale, Screen.height / scale);
        }

        internal static EventDeskArchive Create() => Create(null, ScreenUnits());

        /// <summary>
        /// Creates the archive under its own scaled overlay canvas, or, for the offline harness, under
        /// <paramref name="parent"/> (an existing canvas) at the given screen size.
        /// </summary>
        internal static EventDeskArchive Create(Transform parent, Vector2 screenUnits)
        {
            var go = new GameObject("BoscaliFieldArchive", typeof(RectTransform));
            if (parent == null)
            {
                Canvas canvas = go.AddComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.sortingOrder = SortingOrder;
                CanvasScaler scaler = go.AddComponent<CanvasScaler>();
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1920f, 1080f);
                scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
                go.AddComponent<GraphicRaycaster>();
            }
            else
            {
                go.transform.SetParent(parent, false);
                AvLay.Fill((RectTransform)go.transform);
            }
            var archive = go.AddComponent<EventDeskArchive>();
            archive.Build(screenUnits);
            return archive;
        }

        internal void Show(int initialSection, int record = 0)
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
            SelectSection(initialSection, record);
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
            else if (Input.GetKeyDown(KeyCode.DownArrow)) Step(1);
            else if (Input.GetKeyDown(KeyCode.UpArrow)) Step(-1);
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

        private void Build(Vector2 screenUnits)
        {
            float width = Mathf.Clamp(screenUnits.x - 2f * ScreenMargin, 760f, MaxWidth);
            float height = Mathf.Clamp(screenUnits.y - 2f * ScreenMargin, 480f, MaxHeight);
            // The host is a proper canvas, so the window builds under it instead of becoming a root canvas.
            window = AvWindow.Build(transform, "field-archive", "DIRECTORATE / FIELD ARCHIVE", width, height, SortingOrder);
            window.Closed += Close;
            AvFlow p = window.Body;

            string[] names = { "AIRCRAFT", "EVENTS", "WORLD", "MANUAL" };
            AvIcon[] icons = { AvIcon.Plane, AvIcon.AlertTriangle, AvIcon.Map2, AvIcon.Bookmark };
            var specs = new AvControl.Spec[names.Length];
            for (int i = 0; i < names.Length; i++)
            {
                int target = i;
                specs[i] = new AvControl.Spec(names[i], () => SelectSection(target), AvButtonStyle.Default, icons[i]);
            }
            tabs = p.Buttons(specs).Controls;
            for (int i = 0; i < tabs.Length; i++) tabs[i].SingleLine();

            // Pane height: the body minus the tab row, the flow's top/bottom pad and the gaps.
            float bodyHeight = height - TitleHeight - AvGridTokens.Footer;
            float paneHeight = Mathf.Max(200f, bodyHeight - 2f * AvGridTokens.Pad - AvGridTokens.Tab - AvGridTokens.Gap);
            panes = p.Add(new ArchivePanesPart(p.Content, window.Ticker, p.Inner, paneHeight));

            indexSection = panes.Left.Flow.Section(AvIcon.ListDetails, "INDEX", "0 RECORDS");
            index = panes.Left.Flow.Add(new AvList(panes.Left.Content, window.Ticker, IndexRows, BindIndexRow));
            index.RowClicked = Select;

            detail = panes.Right.Flow.Add(new EventDetailPart(panes.Right.Content));
            detail.Rotate = degrees => preview?.Rotate(degrees);

            window.Footer.Set("Local reading room · no signal leaves this cockpit · Esc closes, Up and Down step the index.");
        }

        private void Step(int delta)
        {
            if (Count == 0) return;
            Select(((selected + delta) % Count + Count) % Count);
        }

        private void Select(int item)
        {
            if (item < 0 || item >= Count) return;
            selected = item;
            Populate();
        }

        private string RowTitle(int item) => section == 0 ? aircraft[item].unitName :
            section == 1 ? EventCatalog.All[item].Title :
            section == 2 ? EventDocs.World[item].Title : EventDocs.Guide[item].Title;

        private void BindIndexRow(int item, AvRow row)
        {
            bool chosen = item == selected;
            string value = section == 0 ? aircraft[item].code ?? "" :
                section == 1 ? EventCatalog.TierLabel(EventCatalog.All[item].Tier) :
                AvNum.Fixed(item + 1, 0).PadLeft(2, '0');
            AvState state = section == 1 ? EventsMfdPanel.TierState(EventCatalog.TierLabel(EventCatalog.All[item].Tier))
                : AvState.Info;
            row.Set(RowTitle(item).ToUpperInvariant(), "", value, state);
            row.Armed = chosen;
        }

        private int Count => section == 0 ? aircraft.Count : section == 1 ? EventCatalog.All.Length :
            section == 2 ? EventDocs.World.Length : EventDocs.Guide.Length;

        private void SelectSection(int target, int record = 0)
        {
            section = Mathf.Clamp(target, 0, 3);
            for (int i = 0; i < tabs.Length; i++) tabs[i].Latched = i == section;
            selected = Mathf.Clamp(record, 0, Mathf.Max(0, Count - 1));
            Populate();
        }

        private void Populate()
        {
            preview?.Dispose();
            preview = null;
            indexSection.SetCaption(AvNum.Fixed(Count, 0) + " RECORDS");
            index.SetCount(Count);
            if (Count == 0)
            {
                detail.ShowEmpty(section == 0 ? "AIRCRAFT INDEX UNAVAILABLE" : "NO RECORDS",
                    "The native encyclopedia has not loaded in this scene.");
                panes.Right.ScrollToTop();
                return;
            }
            selected = Mathf.Clamp(selected, 0, Count - 1);
            index.Reveal(selected);
            if (section == 0) AircraftDetail(aircraft[selected]);
            else if (section == 1) EventDetail(EventCatalog.All[selected]);
            else DocDetail(section == 2 ? EventDocs.World[selected] : EventDocs.Guide[selected]);
            panes.Right.ScrollToTop();
        }

        private void AircraftDetail(AircraftDefinition definition)
        {
            var info = definition.aircraftInfo;
            var figures = new List<DetailFigure>(3);
            if (info != null)
            {
                figures.Add(new DetailFigure("MAX SPEED", AvNum.Fixed(info.maxSpeed, 0) + " M/S"));
                figures.Add(new DetailFigure("STALL SPEED", AvNum.Fixed(info.stallSpeed, 0) + " M/S"));
                figures.Add(new DetailFigure("EMPTY WEIGHT", AvNum.Thousands(info.emptyWeight) + " KG"));
            }
            RawImage output = detail.ShowAircraft("NATIVE AIRCRAFT / " + (definition.code ?? ""),
                definition.unitName.ToUpperInvariant(), figures,
                definition.description ?? "No briefing is recorded for this aircraft.",
                info == null ? "Performance file unavailable." : null);
            bool hasModel = false;
            try { preview = new EventAircraftPreview(output); hasModel = preview.Load(definition); }
            catch (Exception) { preview?.Dispose(); preview = null; }
            detail.SetPreviewAvailable(hasModel);
        }

        private void EventDetail(EventDefinition definition)
        {
            var figures = new List<DetailFigure>(6)
            {
                new DetailFigure("SUPPORT PRICE", "x" + AvNum.Fixed(definition.SupportCostMultiplier, 2),
                    definition.SupportCostMultiplier < 0.999f ? "COST DOWN" : definition.SupportCostMultiplier > 1.001f ? "COST UP" : "UNCHANGED",
                    definition.SupportCostMultiplier < 0.999f ? AvState.Ready : definition.SupportCostMultiplier > 1.001f ? AvState.Caution : AvState.Inert),
                new DetailFigure("RESET CLOCK", "x" + AvNum.Fixed(definition.SupportCooldownMultiplier, 2),
                    definition.SupportCooldownMultiplier < 0.999f ? "FASTER" : definition.SupportCooldownMultiplier > 1.001f ? "SLOWER" : "UNCHANGED",
                    definition.SupportCooldownMultiplier < 0.999f ? AvState.Ready : definition.SupportCooldownMultiplier > 1.001f ? AvState.Caution : AvState.Inert),
                new DetailFigure("WINDOW", AvNum.Fixed(definition.DurationMinSeconds / 60, 0) + "–" +
                    AvNum.Fixed(definition.DurationMaxSeconds / 60, 0) + " MIN"),
                new DetailFigure("TIER", EventCatalog.TierLabel(definition.Tier),
                    null, EventsMfdPanel.TierState(EventCatalog.TierLabel(definition.Tier))),
                new DetailFigure("TARGET", EventCatalog.TargetLabel(definition.Target)),
                new DetailFigure("STORY", EventCatalog.CategoryLabel(definition.Category)),
            };
            string orders = null;
            if (definition.Script.Length > 0)
            {
                var lines = new string[definition.Script.Length];
                for (int i = 0; i < lines.Length; i++)
                    lines[i] = "T+" + AvNum.Clock(definition.Script[i].AtSeconds) + "  " + definition.Script[i].Label;
                orders = string.Join("\n", lines);
            }
            Sprite art = EventArtCache.Get(definition.IconKey, definition.IsSuper ? "tier_super" : "tier_medium");
            detail.ShowEvent(EventCatalog.TierLabel(definition.Tier) + " / " +
                EventCatalog.CategoryLabel(definition.Category) + " · " + EventCatalog.TargetLabel(definition.Target),
                definition.Title.ToUpperInvariant(), art, EventsMfdPanel.CategoryIcon(EventCatalog.CategoryLabel(definition.Category)),
                figures, definition.FlavorText, orders);
        }

        private void DocDetail(EventDocEntry entry) =>
            detail.ShowDoc(entry.Code, entry.Title.ToUpperInvariant(), entry.Body);
    }
}
