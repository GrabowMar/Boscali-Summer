using System;
using System.Collections.Generic;
using BoscaliSummer.Core.Ui;
using BoscaliSummer.Modules.Weather.Domain;
using NOAvionics;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace BoscaliSummer.Modules.Weather.Presentation
{
    /// <summary>
    /// The weather console (Ctrl+O): the sky picker on the left (class tabs, the skies of the class as tiles, AUTO), the sky overhead and the wind and fronts on the right,
    /// the weather along the wind across the bottom. A tile click sets that sky for every player; AUTO hands the weather back to the mission. The host steers; a client sees
    /// the same window read-only with every control off and the reason on the footer.
    ///
    /// <para>The window reads the world only through <see cref="IWeatherChartSource"/> and writes only through <see cref="IMetOfficeCommands"/>. While open it owns the pointer and the
    /// keyboard (<see cref="MetOfficeInput"/>): Rewired mouse and keyboard off, own hit test, everything restored as found.</para>
    /// </summary>
    internal sealed partial class MetOfficeWindow : MonoBehaviour
    {
        private const int SortOrder = 30004;
        private static readonly int ControlCount = Enum.GetValues(typeof(MetControl)).Length;

        private IWeatherChartSource source;
        private IMetOfficeCommands commands;
        private readonly MetOfficeModel model = new MetOfficeModel();
        private readonly MetSkyPicker picker = new MetSkyPicker();
        private readonly MetOfficeInput input = new MetOfficeInput();
        private AvWindow window;
        private readonly AvControl[] control = new AvControl[ControlCount];   // Pin, SkyPrev and SkyNext have no single control: the tiles and the arrow keys are theirs
        private AvChip roleChip;
        private TMP_Text dtg;
        private Image backing;
        private readonly List<AvHit> hitList = new List<AvHit>(32);
        private readonly List<AvHelpTip> tipList = new List<AvHelpTip>(8);
        private AvHit hovered;
        private AvHelpTip tipHovered;
        private float nextRefresh, refusedUntil;

        internal bool IsOpen => window != null && window.Visible;

        internal static MetOfficeWindow Create(Transform parent, IWeatherChartSource source, IMetOfficeCommands commands)
        {
            var go = new GameObject("BoscaliMetOffice", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var view = go.AddComponent<MetOfficeWindow>();
            view.source = source; view.commands = commands;
            view.Build(go.transform);
            return view;
        }

        /// <summary>Opens the window, takes the input and only then refreshes, so an exception in Fill cannot leave the window open with the mouse and keyboard free (or captured by nothing).</summary>
        internal void Show()
        {
            bool wasOpen = IsOpen;
            window.Show();
            if (!wasOpen) input.Take();
            nextRefresh = 0f;
            Refresh();
        }

        internal void Close()
        {
            if (!IsOpen) return;
            window.Hide(); // raises Closed -> OnWindowClosed restores input
        }

        private void OnWindowClosed()
        {
            WindowPointer.Hover(ref hovered, null);
            if (tipHovered != null) { tipHovered.OnPointerExit(WindowPointer.Data()); tipHovered = null; }
            input.Give();
        }

        private void OnDestroy()
        {
            if (window != null && window.Visible) window.Hide();
            input.Teardown();
        }

        // ---- Frame loop ----------------------------------------------------------------------

        private void Update()
        {
            input.Tick();
            if (!IsOpen) return;
            if (Input.GetKeyDown(KeyCode.Escape)) { Close(); return; }
            Keys();
            // The game's UI module reads the Rewired mouse we disabled: hit-test our own controls. Without Rewired the event system delivers clicks itself.
            if (input.MouseIsOurs)
            {
                Vector2 mouse = Input.mousePosition;
                AvHit under = null;
                for (int i = 0; i < hitList.Count; i++)
                {
                    AvHit h = hitList[i];
                    if (h != null && h.isActiveAndEnabled && RectTransformUtility.RectangleContainsScreenPoint((RectTransform)h.transform, mouse, null)) { under = h; break; }
                }
                WindowPointer.Hover(ref hovered, under);
                SetTip(mouse);
                if (under != null && Input.GetMouseButtonDown(0)) ((IPointerClickHandler)under).OnPointerClick(WindowPointer.Data());
            }
            if (Time.unscaledTime >= nextRefresh) { nextRefresh = Time.unscaledTime + 0.25f; Refresh(); }
        }

        // Read-only parts carry help tips but no AvHit; the disabled Rewired mouse never hovers them.
        private void SetTip(Vector2 mouse)
        {
            AvHelpTip under = null;
            for (int i = 0; i < tipList.Count; i++)
            {
                AvHelpTip t = tipList[i];
                if (t != null && t.isActiveAndEnabled && RectTransformUtility.RectangleContainsScreenPoint((RectTransform)t.transform, mouse, null)) { under = t; break; }
            }
            if (under == tipHovered) return;
            if (tipHovered != null) tipHovered.OnPointerExit(WindowPointer.Data());
            tipHovered = under;
            if (tipHovered != null) tipHovered.OnPointerEnter(WindowPointer.Data());
        }

        private void Keys()
        {
            if (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl) || Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt)) return;
            if (Input.GetKeyDown(KeyCode.Backspace)) Press(MetControl.Auto);
            if (Input.GetKeyDown(KeyCode.R)) Press(MetControl.Reroll);
            if (Input.GetKeyDown(KeyCode.LeftArrow)) Press(MetControl.SkyPrev);
            if (Input.GetKeyDown(KeyCode.RightArrow)) Press(MetControl.SkyNext);
        }

        // ---- Actions: every click, key or automation hook goes through here --------------------------

        /// <summary>One request per press (<paramref name="sky"/> is the tile's sky for <see cref="MetControl.Pin"/>). A disabled control is refused with its reason on the footer.</summary>
        internal bool Press(MetControl id, int sky = -1)
        {
            source.Fill(model);
            MetState state = MetOfficeRules.State(id, model);
            if (!state.Enabled)
            {
                window.Footer.SetHint(MetOfficeRules.Tip(id, model));
                refusedUntil = Time.unscaledTime + 2.5f;
                return false;
            }
            bool ok = id switch
            {
                MetControl.Pin => sky >= 0 && (sky == model.PinnedSky || commands.PinSky(sky)),   // the sky already set: nothing to send
                MetControl.SkyPrev => commands.PinSky(picker.Step(model.PinnedSky, -1)),
                MetControl.SkyNext => commands.PinSky(picker.Step(model.PinnedSky, 1)),
                MetControl.Auto => commands.PinSky(-1),
                _ => commands.Reroll(),
            };
            nextRefresh = 0f;
            return ok;
        }

        // ---- Build ---------------------------------------------------------------------------------

        private AvControl.Spec Spec(MetControl id, string label, AvIcon icon = AvIcon.None) => new AvControl.Spec(label, () => Press(id), AvButtonStyle.Default, icon);

        private AvControl Reg(MetControl id, AvControl c) { control[(int)id] = c; return c; }

        private AvFlow Column(string name, float x, float width)
        {
            RectTransform col = AvLay.Child(window.Root, name);
            AvLay.Place(col, x - AvGridTokens.Pad, MetOfficeLayout.BodyTop - AvGridTokens.Pad, width + 2f * AvGridTokens.Pad, MetOfficeLayout.BodyHeight + 2f * AvGridTokens.Pad);
            return new AvFlow(col, window.Ticker, width + 2f * AvGridTokens.Pad, 0f);
        }

        private MetCard Card(AvFlow flow, string title) => flow.Add(new MetCard(flow.Content, window.Ticker, title));

        private void Build(Transform uiRoot)
        {
            window = AvWindow.Build(uiRoot, "MetOffice", "WEATHER CONSOLE", MetOfficeLayout.Width, MetOfficeLayout.Height, SortOrder);
            // AvWindow scales itself when its root is not under a UI canvas (WeatherManager.transform is not). The console is laid out for 1080p and shrinks to fit a smaller screen.
            CanvasScaler scaler = window.Root.parent.GetComponent<CanvasScaler>();
            if (scaler != null && window.Root.parent.parent == uiRoot)
            {
                scaler.referenceResolution = new Vector2(1920f, 1080f);
                scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
            }
            window.Closed += OnWindowClosed;
            window.CloseControl.Help = "Close the weather console (Esc or Ctrl+O).";
            window.Root.Find("Body").gameObject.SetActive(false);   // the kit body (scroll view) stays empty: the columns are placed on the root
            // The kit frame is 0.82 alpha when glassy and only the cards have their own fill, so the free-standing class tabs would show the cockpit through them.
            backing = AvLay.Solid(window.Root, "Backing", Color.clear);
            AvLay.Place(backing.rectTransform, 4f, MetOfficeLayout.Title + 2f, MetOfficeLayout.Width - 8f, MetOfficeLayout.Height - MetOfficeLayout.Footer - MetOfficeLayout.Title - 4f);
            backing.color = MetTitleTint.BackingColor();
            BuildTitleBar();
            BuildPicker();
            BuildRight();
            BuildStrip();
            window.Footer.Place(new AvSlot(0f, MetOfficeLayout.Height - MetOfficeLayout.Footer, MetOfficeLayout.Width, MetOfficeLayout.Footer));

            hitList.AddRange(window.Root.GetComponentsInChildren<AvHit>(true));
            tipList.AddRange(window.Root.GetComponentsInChildren<AvHelpTip>(true));
            // Tips on interactive controls are hovered through AvHit; the per-frame tip scan only needs the rest.
            for (int i = tipList.Count - 1; i >= 0; i--)
                if (tipList[i].GetComponent<AvHit>() != null) tipList.RemoveAt(i);
        }

        private void BuildTitleBar()
        {
            RectTransform r = window.Root;
            var title = r.Find("Title").GetComponent<TMP_Text>();
            AvLay.Place(title, 68f, 0f, 250f, 30f);
            float right = MetOfficeLayout.Width;
            AvFrame plate = AvFrame.Add(r, "IdPlate", AvChamfer.Diagonal(6f));
            AvLay.Place(plate.rectTransform, 8f, 5f, 52f, 20f);
            plate.Stroke = 0f;
            plate.Paint(EnvInk.Role("key"), Color.clear);
            TMP_Text plateText = AvText.Make(r, "IdPlateText", AvTextRole.DataStrong, "ENV", TextAlignmentOptions.Center);
            AvLay.Place(plateText, 8f, 5f, 52f, 20f);
            plateText.color = EnvInk.Role("ground");
            dtg = AvText.Make(r, "Dtg", AvTextRole.DataSmall, "", TextAlignmentOptions.MidlineRight);
            AvLay.Place(dtg, right - 432f, 0f, 212f, 30f);
            dtg.color = EnvInk.Role("ink-muted");
            AvHelpTip.Attach(dtg.gameObject, MetCatalog.DtgHelp);
            roleChip = new AvChip(r);
            roleChip.Place(new AvSlot(right - 214f, 6f, 150f, 18f));
            window.Ticker.Register(roleChip);
            window.Ticker.Register(new MetTitleTint(plate, plateText, dtg, backing));
        }

        // ---- Live state ----------------------------------------------------------------------------

        private void Refresh()
        {
            if (source == null || window == null) return;
            source.Fill(model);
            picker.Sync(model);
            window.Footer.Set(MetOfficeRules.Status(model), model.Host && model.Captured ? AvState.Info : AvState.Caution);
            if (refusedUntil > 0f && Time.unscaledTime > refusedUntil && hovered == null && tipHovered == null) { window.Footer.SetHint(null); refusedUntil = 0f; }
            roleChip.Set(model.Host ? "HOST // ALL PLAYERS" : "READ ONLY", model.Host ? AvState.Ready : AvState.Caution);
            AvHelpTip.Attach(roleChip.Rect.gameObject, model.Host ? MetCatalog.RoleHostHelp : MetCatalog.RoleClientHelp);
            AvText.Set(dtg, MetOfficeText.Dtg(DateTime.UtcNow) + " · " + MetOfficeText.MissionClock(model.MissionSeconds));
            RefreshDesk();
            for (int i = 0; i < ControlCount; i++)
            {
                if (control[i] == null) continue;
                var id = (MetControl)i;
                control[i].Interactable = MetOfficeRules.State(id, model).Enabled;
                control[i].Help = MetOfficeRules.Tip(id, model);
            }
        }

        /// <summary>Sims and the offline harness: the state a check needs, without a mouse.</summary>
        internal Dictionary<string, object> Readout()
        {
            source.Fill(model);
            int enabled = 0;
            for (int i = 0; i < ControlCount; i++) if (MetOfficeRules.State((MetControl)i, model).Enabled) enabled++;
            // Numbers only: the sim checks compare numbers (result_above / result_below).
            return WithInput(new Dictionary<string, object>
            {
                { "open", IsOpen ? 1 : 0 }, { "host", model.Host ? 1 : 0 }, { "captured", model.Captured ? 1 : 0 }, { "pinnedSky", model.PinnedSky + 1 },
                { "forced", model.Forced ? 1 : 0 }, { "browsedClass", (int)picker.Class }, { "enabledControls", enabled }, { "controls", ControlCount },
                { "fronts", model.FrontCount }, { "salt", model.LayoutSalt }, { "inputHeld", input.MouseIsOurs ? 1 : 0 },
            });
        }

        /// <summary>The input contract, read back (no window needed, so a sim can read it before opening): open = mouse 0, keyboard 0, pause 0, cursor flag 1; closed = the values read before opening (-1 = no Rewired).</summary>
        internal static Dictionary<string, object> InputState() => WithInput(new Dictionary<string, object>());

        private static Dictionary<string, object> WithInput(Dictionary<string, object> d)
        {
            d["mouseEnabled"] = Enabled(true); d["keyboardEnabled"] = Enabled(false);
            d["pauseAllowed"] = GameplayUI.AllowPauseKeybind ? 1 : 0; d["cursorFlag"] = CursorManager.GetFlag(CursorFlags.SelectionMenu) ? 1 : 0;
            return d;
        }

        private static int Enabled(bool mouse)
        {
            if (!Rewired.ReInput.isReady || Rewired.ReInput.controllers == null) return -1;
            Rewired.Controller c = mouse ? (Rewired.Controller)Rewired.ReInput.controllers.Mouse : Rewired.ReInput.controllers.Keyboard;
            return c == null ? -1 : c.enabled ? 1 : 0;
        }
    }

    /// <summary>Tints the title bar's own pieces (the ID plate and the clock text) and the backing when the theme changes.</summary>
    internal sealed class MetTitleTint : AvPart
    {
        private readonly AvFrame plate;
        private readonly TMP_Text plateText, dtg;
        private readonly Image backing;
        public MetTitleTint(AvFrame plate, TMP_Text plateText, TMP_Text dtg, Image backing) { this.plate = plate; this.plateText = plateText; this.dtg = dtg; this.backing = backing; }
        public static Color BackingColor() => EnvInk.Alpha(EnvInk.Role("ground"), 0.92f);
        public override void Place(AvSlot slot) { }
        public override void Restyle()
        {
            plate.Paint(EnvInk.Role("key"), Color.clear);
            plateText.color = EnvInk.Role("ground");
            dtg.color = EnvInk.Role("ink-muted");
            backing.color = BackingColor();
        }
    }
}
