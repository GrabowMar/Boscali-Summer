using NOAvionics;
using System;
using BoscaliSummer.Modules.Events.Domain;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Modules.Events.Presentation
{
    /// <summary>
    /// The field archive: a local recognition and reference library, floating over the map on kit v2's
    /// <see cref="AvWindow"/> chrome. Under the library's header band and its shelf tabs the window is two
    /// panes: the open drawer's index on the left, the selected record on the right (a unit's recognition
    /// card, an event dossier, or a volume typeset as a field-manual page). The game's encyclopedia is read
    /// when the window opens and dropped when it closes; the model viewer exists only while a unit card is up.
    /// The window is sized to the screen and each pane scrolls on its own, so nothing is taller than the window.
    /// </summary>
    internal sealed class EventDeskArchive : MonoBehaviour
    {
        private const float MaxWidth = 1360f;
        private const float MaxHeight = 1000f;
        private const float ScreenMargin = 40f;
        private const int SortingOrder = 30003;

        // AvWindow chrome the pane height is carved out of.
        private const float TitleHeight = 30f;

        private readonly ArchiveLibrary library = new ArchiveLibrary();
        private AvWindow window;
        private LibraryHeaderPart header;
        private ShelfTabsPart tabs;
        private ArchivePanesPart panes;
        private IndexHeadPart indexHead;
        private AvPagedStack<ArchiveIndexRow> index;
        private IndexFootPart indexFoot;
        private float indexRoom;
        private bool twoLine;
        private UnitCardPart card;
        private EventDossierPart dossier;
        private VolumePart volume;
        private EventAircraftPreview preview;
        private int shelf, selected;
        private bool open, keyboardTouched, keyboardWas, pauseWas;
        private bool pictures;

        internal bool IsOpen => open;

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

        /// <summary>Opens the library at a shelf (<see cref="ArchiveShelf"/>) and a record on it.</summary>
        internal void Show(int initialShelf, int record = 0)
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
            library.Load();
            int holdings = 0;
            for (int i = 0; i < ArchiveShelf.Count; i++)
            {
                int held = library.Count(i);
                holdings += held;
                tabs.SetCount(i, held);
            }
            header.SetHoldings(holdings, ArchiveShelf.Count);
            window.Show();
            SelectShelf(initialShelf, record);
        }

        internal void Close()
        {
            if (!open) return;
            open = false;
            DisposePreview();
            library.Clear();
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
            if (pictures != EventArtCache.PicturesEnabled)
            {
                pictures = EventArtCache.PicturesEnabled;
                if (shelf == ArchiveShelf.Events && Count > 0)
                    dossier.Bind(EventCatalog.All[selected], selected);
            }
            if (Input.GetKeyDown(KeyCode.Escape)) Close();
            else if (Input.GetKeyDown(KeyCode.DownArrow)) Step(1);
            else if (Input.GetKeyDown(KeyCode.UpArrow)) Step(-1);
            else if (Input.GetKeyDown(KeyCode.RightArrow)) StepShelf(1);
            else if (Input.GetKeyDown(KeyCode.LeftArrow)) StepShelf(-1);
        }

        private void Build(Vector2 screenUnits)
        {
            float width = Mathf.Clamp(screenUnits.x - 2f * ScreenMargin, 760f, MaxWidth);
            float height = Mathf.Clamp(screenUnits.y - 2f * ScreenMargin, 480f, MaxHeight);
            bool compact = height < 620f;
            // The host is a proper canvas, so the window builds under it instead of becoming a root canvas.
            window = AvWindow.Build(transform, "field-archive", "DIRECTORATE / FIELD ARCHIVE", width, height, SortingOrder);
            window.Closed += Close;
            window.CloseControl.Help = "Close the field archive (Esc).";
            AvFlow p = window.Body;

            header = p.Add(new LibraryHeaderPart(p.Content, compact));
            tabs = p.Add(new ShelfTabsPart(p.Content, compact));
            tabs.Picked = target => SelectShelf(target);

            // Pane height: the body minus the header band, the tab row, the flow's top/bottom pad and the gaps.
            float bodyHeight = height - TitleHeight - AvGridTokens.Footer;
            float paneHeight = Mathf.Max(200f, bodyHeight - 2f * AvGridTokens.Pad - header.Measure(p.Inner) -
                tabs.Measure(p.Inner) - 2f * AvGridTokens.Gap);
            panes = p.Add(new ArchivePanesPart(p.Content, window.Ticker, p.Inner, paneHeight));

            // The index fills its pane: heading, as many 30 px lines as fit beside the pager line, and the drawer's
            // foot (its leaders and its label), which a compact window has no room for.
            indexHead = panes.Left.Flow.Add(new IndexHeadPart(panes.Left.Content));
            indexRoom = paneHeight - 2f * AvGridTokens.Pad - indexHead.Measure(0f) - 3f * AvGridTokens.Gap;
            int lines = Mathf.Clamp(Mathf.FloorToInt((indexRoom - (AvGridTokens.Row + 6f) - (compact ? 0f : 72f)) / 32f), 4, 40);
            index = panes.Left.Flow.Add(new AvPagedStack<ArchiveIndexRow>(panes.Left.Content, window.Ticker, lines, 2f,
                (parent, slot) => new ArchiveIndexRow(parent, Select), BindIndexRow, "Index", 40, pagerLead: 2f,
                startHidden: false, trailingGap: true));
            indexFoot = panes.Left.Flow.Add(new IndexFootPart(panes.Left.Content), 1f);
            indexFoot.Picked = Select;
            indexFoot.SetShown(!compact);

            // One record part per kind of record; only the one in use is shown, and it takes the pane's spare height.
            card = panes.Right.Flow.Add(new UnitCardPart(panes.Right.Content) { ViewHeight = paneHeight - 2f * AvGridTokens.Pad }, 1f);
            dossier = panes.Right.Flow.Add(new EventDossierPart(panes.Right.Content), 1f);
            volume = panes.Right.Flow.Add(new VolumePart(panes.Right.Content), 1f);
            card.OpenRecord = Select;
            dossier.OpenRecord = Select;
            card.SetShown(false);
            dossier.SetShown(false);

            window.Footer.Set("LOCAL READING ROOM  ·  ESC CLOSES");
        }

        private int Count => library.Count(shelf);

        private void Step(int delta)
        {
            if (Count == 0) return;
            Select(((selected + delta) % Count + Count) % Count);
        }

        private void StepShelf(int delta) =>
            SelectShelf(((shelf + delta) % ArchiveShelf.Count + ArchiveShelf.Count) % ArchiveShelf.Count);

        private void Select(int item)
        {
            if (item < 0 || item >= Count) return;
            selected = item;
            Populate();
        }

        private void SelectShelf(int target, int record = 0)
        {
            shelf = Mathf.Clamp(target, 0, ArchiveShelf.Count - 1);
            tabs.Select(shelf);
            selected = Mathf.Clamp(record, 0, Mathf.Max(0, Count - 1));
            Populate();
        }

        private EventDocEntry[] Docs => shelf == ArchiveShelf.World ? EventDocs.World : EventDocs.Guide;

        private static string TierTag(EventDefinition definition) =>
            definition.Tier == EventTier.Super ? "SUPER" : EventCatalog.TierLabel(definition.Tier);

        private void BindIndexRow(int item, ArchiveIndexRow row)
        {
            if (shelf < ArchiveShelf.Units)
            {
                ArchiveRecord record = library.Card(shelf, item);
                row.Set(item, record.Code, record.Name.ToUpperInvariant(), twoLine ? record.Brief : "",
                    ArchiveLibrary.RoleTag(record.Role), AvState.Info, item == selected);
            }
            else if (shelf == ArchiveShelf.Events)
            {
                EventDefinition definition = EventCatalog.All[item];
                row.Set(item, "EV-" + ArchiveUi.Two(item + 1), definition.Title.ToUpperInvariant(),
                    twoLine ? EventCatalog.CategoryLabel(definition.Category) + "  ·  " + EventCatalog.TargetLabel(definition.Target) : "",
                    TierTag(definition), EventsMfdPanel.TierState(EventCatalog.TierLabel(definition.Tier)), item == selected);
            }
            else
                row.Set(item, (shelf == ArchiveShelf.World ? "WF-" : "FM-") + ArchiveUi.Two(item + 1),
                    Docs[item].Title.ToUpperInvariant(), twoLine ? Docs[item].Code : "", "PAGE " + AvNum.Fixed(item + 1, 0),
                    AvState.Info, item == selected);
        }

        private void Populate()
        {
            int count = Count;
            string name = ArchiveShelf.Names[shelf], drawer = "DRAWER " + ArchiveUi.Two(shelf + 1);
            bool units = shelf < ArchiveShelf.Units;
            indexHead.Set(drawer + "  ·  " + name,
                count > 0 ? AvNum.Fixed(count, 0) + (units ? " CARDS" : shelf == ArchiveShelf.Events ? " DOSSIERS" : " PAGES") : "—",
                units ? "DESIG" : "FILE", units && shelf != ArchiveShelf.Aircraft ? "NAME, BY CLASS" : units ? "NAME" : "TITLE",
                units ? "ROLE" : shelf == ArchiveShelf.Events ? "TIER" : "REF");
            BindFoot(count);
            index.SetCount(count);

            bool showCard = units && count > 0, showDossier = shelf == ArchiveShelf.Events && count > 0;
            card.SetShown(showCard);
            dossier.SetShown(showDossier);
            volume.SetShown(!showCard && !showDossier);
            string record;
            if (count == 0)
            {
                DropPreview();
                volume.BindNotice(drawer + " / " + name, "SHELF EMPTY",
                    "No records are held in this drawer. The library reads the game's own encyclopedia when the reading room opens, " +
                    "and it found nothing there that it could file on this shelf.");
                record = "NO RECORDS HELD";
            }
            else
            {
                selected = Mathf.Clamp(selected, 0, count - 1);
                index.Reveal(selected);
                if (showCard)
                {
                    // The model first: binding the card then frames it once, at the plate's new shape.
                    ArchiveRecord unit = library.Card(shelf, selected);
                    bool hasModel = false;
                    try
                    {
                        if (preview == null) preview = new EventAircraftPreview(card.ModelView);
                        hasModel = preview.Load(unit.Prefab);
                    }
                    catch (Exception) { DisposePreview(); }
                    card.SetPreview(preview, hasModel);
                    card.Bind(library, shelf, selected);
                    record = unit.Name.ToUpperInvariant();
                }
                else if (showDossier)
                {
                    DropPreview();
                    pictures = EventArtCache.PicturesEnabled;
                    dossier.Bind(EventCatalog.All[selected], selected);
                    record = EventCatalog.All[selected].Title.ToUpperInvariant();
                }
                else
                {
                    DropPreview();
                    volume.Bind(name, Docs, selected);
                    record = Docs[selected].Title.ToUpperInvariant();
                }
            }

            // Lay the panes out now, so a volume can be scrolled to the entry that was picked.
            panes.Relayout();
            if (!showCard && !showDossier && count > 0) panes.Right.ScrollTo(volume.SectionTop(selected));
            else panes.Right.ScrollToTop();
            window.Footer.Set(drawer + " " + name + "  ·  " +
                (count > 0 ? ArchiveUi.Two(selected + 1) + " OF " + ArchiveUi.Two(count) + "  ·  " : "") + record +
                "\nESC CLOSES  ·  UP DOWN STEP THE INDEX  ·  LEFT RIGHT CHANGE DRAWER");
        }

        /// <summary>
        /// The foot of the index. A unit drawer short enough to leave the room, and filed under codes that each
        /// name one card, lists the card that leads the shelf in each figure; any drawer short enough for it
        /// gives each index line a second line of filing detail.
        /// </summary>
        private void BindFoot(int count)
        {
            float width = panes.Left.Flow.Inner;
            bool units = shelf < ArchiveShelf.Units && count > 0 && library.DistinctCodes(shelf);
            for (int pass = units ? 0 : 1; pass < 2; pass++)
            {
                indexFoot.Begin("SHELF LEADERS  ·  LARGEST ON THIS SHELF", ArchiveShelf.Help[shelf]);
                if (pass == 0)
                    for (int figure = 0; figure < library.Figures(shelf); figure++)
                    {
                        int leader = library.Leader(shelf, figure);
                        if (leader < 0) continue;
                        ArchiveRecord record = library.Card(shelf, leader);
                        ArchiveFigure f = record.Figures[figure];
                        indexFoot.Add(f.Label, record.Code, f.Unit.Length > 0 ? f.Text + " " + f.Unit : f.Text, leader,
                            "Open the " + record.Name.ToLowerInvariant() + " record: it leads this shelf in " + f.Label.ToLowerInvariant() + ".");
                    }
                indexFoot.End();
                // The leaders stay only if the whole drawer and its foot fit the pane without scrolling.
                if (pass == 1 || count * 32f + indexFoot.Measure(width) <= indexRoom) break;
            }
            twoLine = indexFoot.Shown && count * 48f + indexFoot.Measure(width) <= indexRoom;
        }

        /// <summary>Releases the model viewer and tells the card its plate is empty.</summary>
        private void DropPreview()
        {
            DisposePreview();
            card.SetPreview(null, false);
        }

        /// <summary>Releases the viewer's camera, light and render target (also on close, when the card may be gone).</summary>
        private void DisposePreview()
        {
            if (preview == null) return;
            preview.Dispose();
            preview = null;
        }
    }
}
