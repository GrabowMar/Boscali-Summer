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
    /// The weather console (Ctrl+O), concept B "MET OFFICE": a synoptic chart on the left, instruments and set-pieces in the middle, story, time, scenarios and log on the right,
    /// a command line of eight keys under them. The host steers; a client sees the same window read-only with every control off and the reason on the footer.
    ///
    /// <para>The window reads the world only through <see cref="IWeatherChartSource"/> and writes only through <see cref="IMetOfficeCommands"/>. While open it owns the pointer and the
    /// keyboard exactly as the old console did (<see cref="MetOfficeInput"/>): Rewired mouse and keyboard off, own hit test, everything restored as found.</para>
    /// </summary>
    internal sealed partial class MetOfficeWindow : MonoBehaviour
    {
        private const int SortOrder = 30004;
        private static readonly int ControlCount = Enum.GetValues(typeof(MetControl)).Length;
        private static readonly float[] ChipX = { 330f, 406f, 496f }, ChipW = { 70f, 84f, 60f };

        private IWeatherChartSource source;
        private IMetOfficeCommands commands;
        private readonly MetOfficeModel model = new MetOfficeModel();
        private readonly MetSkyPicker picker = new MetSkyPicker();
        private readonly MetOfficeInput input = new MetOfficeInput();
        private readonly MetRepaint repaint = new MetRepaint();
        private AvWindow window;
        private readonly AvControl[] control = new AvControl[ControlCount];
        private AvChip roleChip;
        private TMP_Text dtg;
        private Image backing;
        private MetTagLegend tagLegend;
        private readonly List<AvHit> hitList = new List<AvHit>(96);
        private readonly List<AvHelpTip> tipList = new List<AvHelpTip>(24);
        private AvHit hovered;
        private AvHelpTip tipHovered;
        private float nextRefresh, refusedUntil;

        internal bool IsOpen => window != null && window.Visible;
        internal RectTransform Root => window.Root;
        internal AvWindow Window => window;
        internal void RefreshNow() => Refresh();
        internal MetOfficeModel Model => model;

        internal static MetOfficeWindow Create(Transform parent, IWeatherChartSource source, IMetOfficeCommands commands)
        {
            var go = new GameObject("BoscaliMetOffice", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var view = go.AddComponent<MetOfficeWindow>();
            view.source = source; view.commands = commands;
            view.Build(go.transform);
            return view;
        }

        /// <summary>Opens the window, takes the input and only then refreshes, so an exception in Fill or a column refresh cannot leave the window open with the mouse and keyboard free (or captured by nothing).</summary>
        internal void Show()
        {
            bool wasOpen = IsOpen;
            window.Show();
            if (!wasOpen) input.Take();
            nextRefresh = 0f;
            Refresh();
        }

        /// <summary>Shows the window without touching input (the offline harness has no cursor manager, Rewired or pause menu).</summary>
        internal void ShowFrame()
        {
            window.Show();
            nextRefresh = 0f;
            Refresh();
        }

        /// <summary>The harness: a left click on a registered control through its AvHit, the call Update makes with the real mouse. False when the id has no control (PinClass has the class chips instead).</summary>
        internal bool Click(MetControl id) => ClickControl(control[(int)id]);

        /// <summary>The control a catalog id was registered as (null when none): lets the check prove no control was wired to the wrong id.</summary>
        internal AvControl ControlOf(MetControl id) => control[(int)id];

        // Harness probes the column partials register as they are built (Tasks 7 to 9): counts and clicks the check needs without the shell knowing the columns' fields,
        // so the check compiles at every stage. Probe is -1 and ClickPart false until the column that registers the name exists.
        private readonly Dictionary<string, Func<int>> probes = new Dictionary<string, Func<int>>();
        private readonly Dictionary<string, Func<int, bool>> clicks = new Dictionary<string, Func<int, bool>>();
        internal int Probe(string name) => probes.TryGetValue(name, out Func<int> probe) ? probe() : -1;
        internal bool ClickPart(string name, int index) => clicks.TryGetValue(name, out Func<int, bool> click) && click(index);

        private static bool ClickControl(AvControl c)
        {
            AvHit hit = c != null ? c.GetComponentInChildren<AvHit>(true) : null;
            if (hit == null) return false;
            ((IPointerClickHandler)hit).OnPointerClick(WindowPointer.Data());
            return true;
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
            // The tag legend shares the footer with the hover sentence: it steps aside while a sentence (or a refusal) is on.
            tagLegend.Rect.gameObject.SetActive(hovered == null && tipHovered == null && refusedUntil <= 0f);
            if (Time.unscaledTime >= nextRefresh) { nextRefresh = Time.unscaledTime + 0.25f; Refresh(); }
        }

        // Read-only parts (cards, the chart) carry help tips but no AvHit; the disabled Rewired mouse never hovers them.
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

        // ---- Actions: every press, key or automation hook goes through here --------------------------

        /// <summary>One request per press. A disabled control is refused with its reason on the footer.</summary>
        internal bool Press(MetControl id)
        {
            source.Fill(model);
            MetState state = MetOfficeRules.State(id, model);
            if (!state.Enabled)
            {
                window.Footer.SetHint(MetOfficeRules.Tip(id, model));
                refusedUntil = Time.unscaledTime + 2.5f;
                return false;
            }
            bool ok = Do(id);
            nextRefresh = 0f;
            return ok;
        }

        private bool Do(MetControl id)
        {
            switch (id)
            {
                case MetControl.Hold: return commands.SetTapeMoves(!model.Running);
                case MetControl.PaceDown: return commands.SetPace(TapeSwitch.Step(model.Pace, -1));
                case MetControl.PaceUp: return commands.SetPace(TapeSwitch.Step(model.Pace, 1));
                case MetControl.PinAuto: return commands.PinSky(-1);
                case MetControl.PinPrev: return commands.PinSky(picker.Step(-1));
                case MetControl.PinNext: return commands.PinSky(picker.Step(1));
                case MetControl.PinSelected: return commands.PinSky(picker.Selected);
                case MetControl.Reroll: return commands.Reroll();
                case MetControl.AnchorHere: return commands.PlaceAhead(0f);
                case MetControl.AnchorAhead: return commands.PlaceAhead(30000f);
                case MetControl.AnchorDefault: return commands.ClearPlacement();
                // Seam stubs: MetOfficeRules keeps the front-in controls disabled until S5 flips their MetAvail, and the adapter refuses them until it implements them.
                case MetControl.StoryAuto: return commands.SetStory(-1);
                case MetControl.StoryPrev: return commands.SetStory(StoryStep(-1));
                case MetControl.StoryNext: return commands.SetStory(StoryStep(1));
                case MetControl.Front2: return commands.FrontIn(2);
                case MetControl.Front5: return commands.FrontIn(5);
                case MetControl.Front10: return commands.FrontIn(10);
                case MetControl.Front30: return commands.FrontIn(30);
                default:
                    if (id >= MetControl.SetSquall && id <= MetControl.SetSeaFog) return commands.SetSets((byte)(ForcedMask() ^ MetCatalog.SetBits[MetCatalog.SetIndex(id)]));
                    if (id >= MetControl.RainAuto && id <= MetControl.RainHeavy) return commands.SetRain(MetCatalog.RainValues[id - MetControl.RainAuto]);
                    if (id >= MetControl.ScenHurricane && id <= MetControl.ScenReset) return commands.Scenario(MetCatalog.Scenarios[MetCatalog.ScenarioIndex(id)]);
                    return false;
            }
        }

        /// <summary>The story the stepper moves to, wrapping inside StoryCount (0 while no story is authored).</summary>
        private int StoryStep(int direction) => MetOfficeRules.StoryStep(model.StoryPick, model.StoryCount, direction);

        private byte ForcedMask()
        {
            byte mask = 0;
            for (int i = 0; i < MetOfficeModel.SetCount; i++) if (model.Sets[i].Forced) mask |= MetCatalog.SetBits[i];
            return mask;
        }

        private void CycleRain()
        {
            source.Fill(model);   // a press never steps from a stale refresh
            int now = 0;
            for (int i = 0; i < MetCatalog.RainValues.Length; i++) if (RainMatches(i)) now = i;
            Press(MetCatalog.From(MetControl.RainAuto, (now + 1) % MetCatalog.RainValues.Length));
        }

        private bool RainMatches(int i) =>
            MetCatalog.RainValues[i].HasValue == model.ForcedRain.HasValue &&
            (!model.ForcedRain.HasValue || Mathf.Abs(MetCatalog.RainValues[i].Value - model.ForcedRain.Value) < 0.05f);

        // ---- Build ---------------------------------------------------------------------------------

        private static void Tip(GameObject target, string sentence) => AvHelpTip.Attach(target, sentence);

        private AvControl.Spec Spec(MetControl id, string label, AvIcon icon = AvIcon.None, AvButtonStyle style = AvButtonStyle.Default) =>
            new AvControl.Spec(label, () => Press(id), style, icon);

        private AvControl Reg(MetControl id, AvControl c)
        {
            control[(int)id] = c;
            repaint.Track(MetRail.Attach(c.Rect, MetCatalog.Info(id).Effect));
            return c;
        }

        private AvFlow Column(string name, float x, float width)
        {
            RectTransform col = AvLay.Child(window.Root, name);
            AvLay.Place(col, x - AvGridTokens.Pad, MetOfficeLayout.BodyTop - AvGridTokens.Pad, width + 2f * AvGridTokens.Pad, MetOfficeLayout.BodyHeight + 2f * AvGridTokens.Pad);
            return new AvFlow(col, window.Ticker, width + 2f * AvGridTokens.Pad, 0f);
        }

        private MetCard Card(AvFlow flow, string title)
        {
            MetCard card = flow.Add(new MetCard(flow.Content, window.Ticker, title));
            return card;
        }

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
            window.CloseControl.Help = "Close the Met Office (Esc or Ctrl+O).";
            window.Ticker.Register(repaint);
            window.Root.Find("Body").gameObject.SetActive(false);   // the kit body (scroll view) stays empty: the columns are placed on the root
            // The kit frame is 0.82 alpha when glassy and only the cards have their own fill, so the free-standing parts (section headers, the anchor and rain rows) would show the cockpit through them.
            backing = AvLay.Solid(window.Root, "Backing", Color.clear);
            AvLay.Place(backing.rectTransform, 4f, MetOfficeLayout.Title + 2f, MetOfficeLayout.Width - 8f, MetOfficeLayout.Height - MetOfficeLayout.Footer - MetOfficeLayout.Title - 4f);
            backing.color = MetTitleTint.BackingColor();
            BuildTitleBar();
            BuildHero();
            BuildLeft();
            BuildCentre();
            BuildRight();
            BuildTape();
            window.Footer.Place(new AvSlot(0f, MetOfficeLayout.Height - MetOfficeLayout.Footer, MetOfficeLayout.Width, MetOfficeLayout.Footer));
            tagLegend = new MetTagLegend(window.Root);
            tagLegend.Place(new AvSlot(MetOfficeLayout.Width - MetTagLegend.Width - 10f, MetOfficeLayout.Height - MetOfficeLayout.Footer + 8f, MetTagLegend.Width, 14f));
            window.Ticker.Register(tagLegend);

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
            string[] words = { "SMOOTH", "FADE 20 S", "CUT" };
            AvState[] states = { AvState.Info, AvState.Ready, AvState.Caution };
            for (int i = 0; i < words.Length; i++)
            {
                var chip = new AvChip(r);
                chip.Set(words[i], states[i]);
                chip.Place(new AvSlot(ChipX[i], 6f, ChipW[i], 18f));
                window.Ticker.Register(chip);
                Tip(chip.Rect.gameObject, MetCatalog.ChipHelp[i]);
            }
            dtg = AvText.Make(r, "Dtg", AvTextRole.DataSmall, "", TextAlignmentOptions.MidlineRight);
            AvLay.Place(dtg, right - 492f, 0f, 272f, 30f);
            dtg.color = EnvInk.Role("ink-muted");
            Tip(dtg.gameObject, MetCatalog.DtgHelp);
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
            bool ready = model.Host && model.Captured;
            window.Footer.Set(MetOfficeRules.Status(model), ready ? AvState.Info : AvState.Caution);
            if (refusedUntil > 0f && Time.unscaledTime > refusedUntil && hovered == null && tipHovered == null) { window.Footer.SetHint(null); refusedUntil = 0f; }
            roleChip.Set(model.Host ? "HOST // ALL PLAYERS" : "READ ONLY", model.Host ? AvState.Ready : AvState.Caution);
            Tip(roleChip.Rect.gameObject, model.Host ? MetCatalog.RoleHostHelp : MetCatalog.RoleClientHelp);
            AvText.Set(dtg, MetOfficeText.Dtg(DateTime.UtcNow) + " · " + MetOfficeText.MissionClock(model.MissionSeconds) + " · SN " + (model.SerialHex.Length > 0 ? model.SerialHex : "----"));
            RefreshDesk();

            // Controls: enabled state and the hover line (the reason while disabled), then each column's latches and labels.
            for (int i = 0; i < ControlCount; i++)
            {
                if (control[i] == null) continue;
                var id = (MetControl)i;
                control[i].Interactable = MetOfficeRules.State(id, model).Enabled;
                control[i].Help = MetOfficeRules.Tip(id, model);
            }
        }

        /// <summary>TMP re-lays a label on every assignment, so a refresh only writes the ones that changed.</summary>
        private static void Label(AvControl c, string text) { if (c.Label != text) c.Label = text; }

        /// <summary>Sims and the offline harness: the state a check needs, without a mouse.</summary>
        internal Dictionary<string, object> Readout()
        {
            source.Fill(model);
            int enabled = 0;
            for (int i = 0; i < ControlCount; i++) if (MetOfficeRules.State((MetControl)i, model).Enabled) enabled++;
            // Numbers only: the sim checks compare numbers (result_above / result_below).
            return WithInput(new Dictionary<string, object>
            {
                { "open", IsOpen ? 1 : 0 }, { "host", model.Host ? 1 : 0 }, { "captured", model.Captured ? 1 : 0 }, { "running", model.Running ? 1 : 0 }, { "pace", model.Pace },
                { "speed", model.SpeedMps }, { "pinnedSky", model.PinnedSky + 1 }, { "enabledControls", enabled }, { "controls", ControlCount },
                { "fronts", model.FrontCount }, { "cells", model.CellCount }, { "sets", (int)ForcedMask() }, { "anchor", model.HasAnchor ? 1 : 0 },
                { "story", model.StoryPick + 1 }, { "salt", model.LayoutSalt },
                { "rain", model.ForcedRain.HasValue ? (int)Mathf.Round(model.ForcedRain.Value * 100f) : -1 },
                { "inputHeld", input.MouseIsOurs ? 1 : 0 },
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

    /// <summary>Tints the title bar's own pieces (the ID plate and the clock text) when the theme changes.</summary>
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
