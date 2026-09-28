using System.Collections.Generic;
using BoscaliSummer.Features.Weather.Domain;
using BoscaliSummer.Features.Weather.Runtime;
using NOAvionics;
using NOAvionics.Ui;
using Rewired;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace BoscaliSummer.Features.Weather.Presentation
{
    /// <summary>
    /// The weather console (Ctrl+O). The host holds or releases weather states, forces storm
    /// set-pieces and re-rolls the cloud layout; clients see the same panel read-only.
    ///
    /// <para>While open it takes the mouse and keyboard away from flying the way the
    /// interaction menu does: the game's cursor flag frees the cursor, and the Rewired mouse
    /// is disabled so a click cannot fire guns. Because the game's UI module reads that same
    /// Rewired mouse, the panel hit-tests its own buttons. Everything is restored exactly
    /// as found, and the mouse only once its buttons are up.</para>
    /// </summary>
    internal sealed class WeatherConsoleWindow : MonoBehaviour
    {
        private const float Width = 920f;
        private const float Height = 740f;
        private const int SortOrder = 30004;

        private static readonly WeatherRegimeType[] States =
        {
            WeatherRegimeType.Clear, WeatherRegimeType.Fair, WeatherRegimeType.Scattered,
            WeatherRegimeType.Broken, WeatherRegimeType.Overcast, WeatherRegimeType.RainSquall,
            WeatherRegimeType.Storm,
        };
        private static readonly string[] StateNames = { "CLEAR", "FAIR", "SCATTERED", "BROKEN", "OVERCAST", "RAIN", "STORM" };
        private static readonly byte[] SetBits =
        {
            Superstructures.SquallLineSet, Superstructures.SupercellSet, Superstructures.DistantCellSet,
            Superstructures.StormEyeSet, Superstructures.LenticularSet, Superstructures.FogBankSet,
        };
        private static readonly string[] SetNames = { "SQUALL LINE", "SUPERCELL", "DISTANT CB", "STORM EYE", "LENTICULARS", "SEA FOG" };
        private static readonly string[] SetHelp =
        {
            "A storm wall on the upwind horizon with a tiered shelf cloud on its leading edge.",
            "A leaning cumulonimbus with a downwind anvil and mammatus pouches under it.",
            "A lone cumulonimbus far out on the horizon.",
            "A hurricane eyewall: a clear eye in a stadium of cloud, spiral rain bands outside. Fly into it.",
            "Smooth stacked lens clouds in a mountain-wave train, riding the wind.",
            "A fog bank over the sea and valleys up to about 300 m.",
        };
        private static readonly WeatherManager.Scenario[] Scenarios =
        {
            WeatherManager.Scenario.HurricaneEye, WeatherManager.Scenario.SquallAssault,
            WeatherManager.Scenario.MountainWave, WeatherManager.Scenario.SeaFog,
            WeatherManager.Scenario.FrontalPassage, WeatherManager.Scenario.ResetAll,
        };
        private static readonly string[] ScenarioNames = { "HURRICANE EYE", "SQUALL ASSAULT", "MOUNTAIN WAVE", "SEA FOG", "FRONTAL PASSAGE", "RESET ALL" };
        private static readonly string[] ScenarioHelp =
        {
            "Storm, with you in the eye of a hurricane: walls all around, clear sky above.",
            "Rain squall with a squall line and a supercell bearing down.",
            "Fair skies with lenticular clouds standing 25 km ahead of you.",
            "Clear sky over a fog bank in the sea and valleys.",
            "Overcast: a front across the map, deck behind it, open sky ahead.",
            "Back to changing weather: no forced set-pieces, placement or front turn.",
        };
        private static readonly float?[] RainValues = { null, 0f, 0.4f, 1f };
        private static readonly string[] RainNames = { "AUTO", "DRY", "LIGHT", "HEAVY" };

        private WeatherManager weather;
        private Canvas canvas;
        private GameObject surface;
        private RectTransform frame;
        private TMP_Text stateText, modeText, setsText, accessText;
        private readonly AvButton[] stateButtons = new AvButton[7];
        private readonly AvButton[] setButtons = new AvButton[6];
        private readonly AvButton[] scenarioButtons = new AvButton[6];
        private AvButton placeButton, unplaceButton, turnButton, flipButton;
        private TMP_Text frontText;
        private readonly AvButton[] rainButtons = new AvButton[4];
        private AvButton changingButton, rerollButton;
        private readonly List<AvButton> hitList = new List<AvButton>(24);
        private AvButton hovered;
        private float nextRefresh;
        private bool ownsCursorFlag, mouseTouched, mouseWas, keyboardTouched, keyboardWas, pauseWas, releasePending;

        internal bool IsOpen { get; private set; }

        internal static WeatherConsoleWindow Create(WeatherManager owner, Transform parent)
        {
            var go = new GameObject("BoscaliWeatherConsole", typeof(RectTransform), typeof(Canvas),
                typeof(CanvasScaler), typeof(GraphicRaycaster));
            go.transform.SetParent(parent, false);
            var view = go.AddComponent<WeatherConsoleWindow>();
            view.weather = owner;
            view.canvas = go.GetComponent<Canvas>();
            view.canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            view.canvas.sortingOrder = SortOrder;
            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
            view.Build();
            view.canvas.enabled = false;
            view.surface.SetActive(false);
            return view;
        }

        internal void Show()
        {
            canvas.enabled = true;
            surface.SetActive(true);
            if (!IsOpen) TakeInput();
            IsOpen = true;
            nextRefresh = 0f;
            Refresh();
        }

        internal void Close()
        {
            if (!IsOpen) return;
            IsOpen = false;
            canvas.enabled = false;
            surface.SetActive(false);
            SetHovered(null);
            AvButton.ClearTooltip();
            GiveBackInput();
        }

        private void OnDestroy()
        {
            Close();
            // A mouse still held at teardown is handed back immediately.
            if (releasePending) RestoreMouse();
        }

        private void Update()
        {
            if (releasePending && !Input.GetMouseButton(0) && !Input.GetMouseButton(1)) RestoreMouse();
            if (!IsOpen) return;
            if (Input.GetKeyDown(KeyCode.Escape)) { Close(); return; }

            // The game's UI module reads the Rewired mouse we disabled: hit-test our own buttons.
            // Without Rewired the event system delivers clicks itself.
            if (!mouseTouched) { if (Time.unscaledTime >= nextRefresh) { nextRefresh = Time.unscaledTime + 0.25f; Refresh(); } return; }
            AvButton under = null;
            Vector2 mouse = Input.mousePosition;
            for (int i = 0; i < hitList.Count; i++)
            {
                AvButton b = hitList[i];
                if (b != null && b.isActiveAndEnabled &&
                    RectTransformUtility.RectangleContainsScreenPoint((RectTransform)b.transform, mouse, null))
                { under = b; break; }
            }
            SetHovered(under);
            if (under != null && Input.GetMouseButtonDown(0))
                ((IPointerClickHandler)under).OnPointerClick(Pointer());

            if (Time.unscaledTime >= nextRefresh) { nextRefresh = Time.unscaledTime + 0.25f; Refresh(); }
        }

        private void SetHovered(AvButton next)
        {
            if (hovered == next) return;
            if (hovered != null) ((IPointerExitHandler)hovered).OnPointerExit(Pointer());
            hovered = next;
            if (hovered != null) ((IPointerEnterHandler)hovered).OnPointerEnter(Pointer());
        }

        private static PointerEventData Pointer() =>
            new PointerEventData(EventSystem.current) { button = PointerEventData.InputButton.Left, position = Input.mousePosition };

        // ---- Input isolation ----------------------------------------------------------------

        private void TakeInput()
        {
            ownsCursorFlag = !CursorManager.GetFlag(CursorFlags.SelectionMenu);
            if (ownsCursorFlag) CursorManager.SetFlag(CursorFlags.SelectionMenu, true);
            pauseWas = GameplayUI.AllowPauseKeybind;
            GameplayUI.AllowPauseKeybind = false;
            if (!ReInput.isReady || ReInput.controllers == null) return;
            if (!releasePending && ReInput.controllers.Mouse != null)
            {
                mouseTouched = true;
                mouseWas = ReInput.controllers.Mouse.enabled;
                ReInput.controllers.Mouse.enabled = false;
            }
            releasePending = false;
            if (ReInput.controllers.Keyboard != null)
            {
                keyboardTouched = true;
                keyboardWas = ReInput.controllers.Keyboard.enabled;
                ReInput.controllers.Keyboard.enabled = false;
            }
        }

        private void GiveBackInput()
        {
            if (ownsCursorFlag) CursorManager.SetFlag(CursorFlags.SelectionMenu, false);
            ownsCursorFlag = false;
            GameplayUI.AllowPauseKeybind = pauseWas;
            if (keyboardTouched && ReInput.isReady && ReInput.controllers?.Keyboard != null)
                ReInput.controllers.Keyboard.enabled = keyboardWas;
            keyboardTouched = false;
            // Hand the mouse back only once its buttons are up, or the closing click fires.
            if (mouseTouched) releasePending = true;
            if (releasePending && !Input.GetMouseButton(0) && !Input.GetMouseButton(1)) RestoreMouse();
        }

        private void RestoreMouse()
        {
            releasePending = false;
            if (mouseTouched && ReInput.isReady && ReInput.controllers?.Mouse != null)
                ReInput.controllers.Mouse.enabled = mouseWas;
            mouseTouched = false;
        }

        // ---- Layout --------------------------------------------------------------------------

        private void Build()
        {
            var root = (RectTransform)transform;
            surface = new GameObject("WeatherConsoleSurface", typeof(RectTransform));
            var full = (RectTransform)surface.transform;
            full.SetParent(root, false);
            AvKit.Stretch(full);
            AvRoomFrame.CreateBackdrop(full, .55f);
            frame = AvRoomFrame.CreateFrame(full, "WeatherConsoleFrame", out CanvasGroup group);
            group.alpha = 1f;
            frame.sizeDelta = new Vector2(Width, Height);
            frame.anchorMin = frame.anchorMax = new Vector2(.5f, .5f);
            frame.pivot = new Vector2(.5f, .5f);
            frame.anchoredPosition = Vector2.zero;

            AvKit.Panel(frame, new Rect(0f, 0f, Width, Height), AvTheme.Ground, AvSprites.Panel).raycastTarget = true;
            AvRoomFrame.CreateEdge(frame, new Rect(0f, 0f, Width, 1f), AvTheme.Frame);
            AvRoomFrame.CreateEdge(frame, new Rect(0f, -Height + 1f, Width, 1f), AvTheme.Frame);
            AvRoomFrame.CreateEdge(frame, new Rect(0f, 0f, 1f, Height), AvTheme.Frame);
            AvRoomFrame.CreateEdge(frame, new Rect(Width - 1f, 0f, 1f, Height), AvTheme.Frame);

            Label("WEATHER CONSOLE", 28f, -20f, 560f, 30f, 23f, AvTheme.TextPrimary, true);
            accessText = Label("HOST CONTROLS", 30f, -53f, 560f, 18f, 12f, AvTheme.Dim);
            AvButton close = AvKit.Button(frame, "× CLOSE", new Rect(Width - 150f, -22f, 124f, 34f), Close);
            close.WithTooltip("Close the weather console (Ctrl+O or Esc).");
            hitList.Add(close);
            AvKit.Rule(frame, new Rect(28f, -82f, Width - 56f, 2f), AvTheme.RailInfo);

            // Now: the state on screen, whether it changes, which set-pieces are up.
            AvKit.Panel(frame, new Rect(28f, -100f, Width - 56f, 92f), AvTheme.Surface);
            AvKit.Rule(frame, new Rect(28f, -100f, 3f, 92f), AvTheme.RailInfo);
            Label("NOW", 44f, -110f, 200f, 16f, 11f, AvTheme.Dim, true);
            stateText = Label("—", 44f, -130f, 420f, 34f, 26f, AvTheme.TextPrimary, true);
            modeText = Label("—", 470f, -114f, 400f, 22f, 14f, AvTheme.RailInfo, true);
            setsText = Label("—", 470f, -142f, 400f, 36f, 12f, AvTheme.Dim);
            setsText.enableWordWrapping = true;

            Section("SCENARIOS", "One click: a state with its set-pieces, sent to every player.", -206f);
            for (int i = 0; i < Scenarios.Length; i++)
            {
                WeatherManager.Scenario scenario = Scenarios[i];
                scenarioButtons[i] = Button(ScenarioNames[i], new Rect(28f + 144f * i, -234f, 138f, 40f),
                    () => weather.ApplyScenario(scenario), ScenarioHelp[i]);
            }

            Section("STATE", "Hold a state. Clouds fade into it; fog, light and wind follow.", -292f);
            for (int i = 0; i < States.Length; i++)
            {
                WeatherRegimeType state = States[i];
                stateButtons[i] = Button(StateNames[i], new Rect(28f + 124f * i, -320f, 116f, 40f),
                    () => weather.HoldState(state),
                    "Hold " + RegimeSnapshot.FromType(state).Name.ToLowerInvariant() + ".");
            }
            changingButton = Button("CHANGING WEATHER", new Rect(28f, -366f, 240f, 34f), () => weather.ResumeChanging(),
                "Resume the weather stepping one state every few minutes, from the state on screen.");

            Section("SET-PIECES", "Force formations on in any sky; they build over ~20 s.", -418f);
            for (int i = 0; i < SetBits.Length; i++)
            {
                byte bit = SetBits[i];
                setButtons[i] = Button(SetNames[i], new Rect(28f + 144f * i, -446f, 138f, 40f),
                    () => weather.SetForcedSets((byte)(weather.ForcedSets ^ bit)), SetHelp[i]);
            }
            placeButton = Button("PLACE 30 KM AHEAD", new Rect(28f, -492f, 200f, 34f), () => weather.PlaceAhead(30000f),
                "Stand the storm eye and lenticulars 30 km ahead of you (lenticulars sit beside the eye).");
            Button("PLACE HERE", new Rect(236f, -492f, 150f, 34f), () => weather.PlaceAhead(0f),
                "Centre the storm eye on you, so you are in the eye.");
            unplaceButton = Button("DEFAULT PLACE", new Rect(394f, -492f, 170f, 34f), () => weather.ClearPlacement(),
                "Put console set-pieces back at their default spots.");
            rerollButton = Button("RE-ROLL LAYOUT", new Rect(Width - 28f - 200f, -492f, 200f, 34f), () => weather.RerollLayout(),
                "New sites for every cloud group, cell and set-piece. The weather state stays.");

            Section("FRONT", "Turn the frontal boundary in BROKEN and worse skies.", -544f);
            turnButton = Button("ROTATE 45°", new Rect(28f, -572f, 150f, 36f), () => weather.TurnFront(1),
                "Turn the front's line by 45 degrees.");
            flipButton = Button("FLIP", new Rect(186f, -572f, 110f, 36f), () => weather.TurnFront(4),
                "Swap which side of the map is under the deck.");
            frontText = Label("—", 312f, -576f, 560f, 28f, 12f, AvTheme.Dim);

            Section("RAIN", "Force precipitation, or leave it to the weather.", -626f);
            for (int i = 0; i < RainValues.Length; i++)
            {
                float? rain = RainValues[i];
                rainButtons[i] = Button(RainNames[i], new Rect(28f + 124f * i, -654f, 116f, 40f),
                    () => weather.SetForcedRain(rain), "Rain: " + RainNames[i].ToLowerInvariant() + ".");
            }
        }

        private void Section(string title, string help, float y)
        {
            Label(title, 28f, y, 300f, 20f, 13f, AvTheme.RailInfo, true);
            Label(help, 300f, y, Width - 330f, 20f, 11f, AvTheme.Dim);
        }

        private AvButton Button(string text, Rect area, System.Action action, string tooltip)
        {
            AvButton button = AvKit.Button(frame, text, area, action, AvTokens.FontBody);
            button.WithTooltip(tooltip);
            hitList.Add(button);
            return button;
        }

        private TMP_Text Label(string value, float x, float y, float w, float h, float size, Color color, bool bold = false)
        {
            TMP_Text text = AvKit.Label(frame, value, new Rect(x, y, w, h), color, size,
                bold ? FontStyles.Bold : FontStyles.Normal, TextAlignmentOptions.MidlineLeft);
            text.richText = false;
            return text;
        }

        // ---- Live state ----------------------------------------------------------------------

        private void Refresh()
        {
            if (weather == null) return;
            bool host = weather.CanCommand;
            bool held = weather.IsManualOverride;
            WeatherRegimeType shown = weather.ShownState;
            stateText.text = RegimeSnapshot.FromType(shown).Name.ToUpperInvariant();
            float next = weather.NextChangeIn();
            modeText.text = held ? "HELD BY THE CONSOLE"
                : next < 0f ? "HELD WEATHER"
                : $"CHANGING  ·  NEXT STEP IN {Mathf.FloorToInt(next / 60f)}:{Mathf.FloorToInt(next % 60f):00}";
            accessText.text = host ? "HOST CONTROLS  ·  EVERY PLAYER SEES THE SAME SKY" : "READ ONLY  ·  ONLY THE HOST CAN CHANGE THE WEATHER";

            WeatherField field = weather.Field;
            string sets = "";
            for (int i = 0; field != null && i < field.SuperstructureCount; i++)
            {
                Superstructure s = field.SuperstructureAt(i);
                int slot = System.Array.IndexOf(SetBits, s.Set);
                string name = slot >= 0 ? SetNames[slot] : "STORM";
                float km = Mathf.Sqrt(s.X * s.X + s.Z * s.Z) / 1000f;
                sets += (sets.Length > 0 ? "  ·  " : "") + $"{name} {km:0} KM {Mathf.RoundToInt(s.Strength * 100f)}%";
            }
            setsText.text = sets.Length > 0 ? "SET-PIECES  " + sets : "NO STORM SET-PIECES IN THIS SKY";

            for (int i = 0; i < stateButtons.Length; i++)
            {
                stateButtons[i].SetEnabled(host);
                stateButtons[i].SetLatched(held && States[i] == shown);
            }
            changingButton.SetEnabled(host);
            changingButton.SetLatched(!held && next >= 0f);
            byte forced = weather.ForcedSets;
            for (int i = 0; i < setButtons.Length; i++)
            {
                setButtons[i].SetEnabled(host);
                setButtons[i].SetLatched((forced & SetBits[i]) != 0);
            }
            rerollButton.SetEnabled(host);
            for (int i = 0; i < scenarioButtons.Length; i++) scenarioButtons[i].SetEnabled(host);
            placeButton.SetEnabled(host);
            unplaceButton.SetEnabled(host && weather.HasAnchor);
            turnButton.SetEnabled(host);
            flipButton.SetEnabled(host);
            int turn = weather.FrontTurn;
            bool splitSky = field != null && field.Split.Amount > 0f;
            frontText.text = (splitSky ? "FRONT ACROSS THE MAP" : "NO FRONT IN THIS SKY (BROKEN AND WORSE)") +
                (turn != 0 ? $"  ·  TURNED {turn * 45}°" : "") +
                ((weather.ForcedSets & Superstructures.FogBankSet) != 0 ? "  ·  FOG BANK ON" : "");
            float? rainNow = weather.ForcedRainIntensity;
            for (int i = 0; i < rainButtons.Length; i++)
            {
                rainButtons[i].SetEnabled(host);
                rainButtons[i].SetLatched(RainValues[i].HasValue == rainNow.HasValue &&
                    (!rainNow.HasValue || Mathf.Abs(RainValues[i].Value - rainNow.Value) < 0.05f));
            }
        }
    }
}
