using System.Collections.Generic;
using BoscaliSummer.Features.Weather.Domain;
using BoscaliSummer.Features.Weather.Runtime;
using NOAvionics;
using NOAvionics.Ui;
using Rewired;
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
    /// Rewired mouse, the panel hit-tests its own kit v2 controls by their <see cref="AvHit"/>
    /// component. Everything is restored exactly as found, and the mouse only once its buttons
    /// are up.</para>
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
        private AvWindow window;
        private AvReadout stateReadout;
        private AvRow setsRow;
        private AvRow frontRow;
        private AvControl[] stateButtons;
        private AvControl[] setButtons;
        private AvControl[] scenarioButtons;
        private AvControl[] rainButtons;
        private AvControl placeButton, unplaceButton, turnButton, flipButton, changingButton, rerollButton;
        private readonly List<AvHit> hitList = new List<AvHit>(32);
        private AvHit hovered;
        private float nextRefresh;
        private bool ownsCursorFlag, mouseTouched, mouseWas, keyboardTouched, keyboardWas, pauseWas, releasePending;

        internal bool IsOpen => window != null && window.Visible;

        internal static WeatherConsoleWindow Create(WeatherManager owner, Transform parent)
        {
            var go = new GameObject("BoscaliWeatherConsole", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var view = go.AddComponent<WeatherConsoleWindow>();
            view.weather = owner;
            view.Build(go.transform);
            return view;
        }

        internal void Show()
        {
            bool wasOpen = IsOpen;
            window.Show();
            if (!wasOpen) TakeInput();
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
            SetHovered(null);
            GiveBackInput();
        }

        private void OnDestroy()
        {
            if (window != null && window.Visible) window.Hide();
            // A mouse still held at teardown is handed back immediately.
            if (releasePending) RestoreMouse();
        }

        private void Update()
        {
            if (releasePending && !Input.GetMouseButton(0) && !Input.GetMouseButton(1)) RestoreMouse();
            if (!IsOpen) return;
            if (Input.GetKeyDown(KeyCode.Escape)) { Close(); return; }

            // The game's UI module reads the Rewired mouse we disabled: hit-test our own controls.
            // Without Rewired the event system delivers clicks itself.
            if (!mouseTouched) { if (Time.unscaledTime >= nextRefresh) { nextRefresh = Time.unscaledTime + 0.25f; Refresh(); } return; }
            AvHit under = null;
            Vector2 mouse = Input.mousePosition;
            for (int i = 0; i < hitList.Count; i++)
            {
                AvHit h = hitList[i];
                if (h != null && h.isActiveAndEnabled &&
                    RectTransformUtility.RectangleContainsScreenPoint((RectTransform)h.transform, mouse, null))
                { under = h; break; }
            }
            SetHovered(under);
            if (under != null && Input.GetMouseButtonDown(0))
                ((IPointerClickHandler)under).OnPointerClick(Pointer());

            if (Time.unscaledTime >= nextRefresh) { nextRefresh = Time.unscaledTime + 0.25f; Refresh(); }
        }

        private void SetHovered(AvHit next)
        {
            if (hovered == next) return;
            if (hovered != null)
            {
                ((IPointerExitHandler)hovered).OnPointerExit(Pointer());
                hovered.GetComponent<AvHelpTip>()?.OnPointerExit(Pointer());
            }
            hovered = next;
            if (hovered != null)
            {
                ((IPointerEnterHandler)hovered).OnPointerEnter(Pointer());
                // The Rewired mouse is off, so the event system never delivers hover: forward it to the help tip too.
                hovered.GetComponent<AvHelpTip>()?.OnPointerEnter(Pointer());
            }
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

        private void Build(Transform uiRoot)
        {
            window = AvWindow.Build(uiRoot, "WeatherConsole", "WEATHER CONSOLE", Width, Height, SortOrder);
            // AvWindow scales itself when its root is not under a UI canvas (WeatherManager.transform is not).
            window.Closed += OnWindowClosed;

            AvFlow body = window.Body;

            body.Section(AvIcon.Cloud, "NOW", "");
            stateReadout = body.Add(new AvReadout(body.Content));
            setsRow = body.Add(new AvRow(body.Content));

            body.Section(AvIcon.PlayerPlay, "SCENARIOS", "One click: a state with its set-pieces, sent to every player.");
            var scenarioSpecs = new AvControl.Spec[Scenarios.Length];
            for (int i = 0; i < Scenarios.Length; i++)
            {
                WeatherManager.Scenario scenario = Scenarios[i];
                scenarioSpecs[i] = new AvControl.Spec(ScenarioNames[i], () => weather.ApplyScenario(scenario));
            }
            scenarioButtons = body.Add(new AvButtons(body.Content, scenarioSpecs)).Controls;
            for (int i = 0; i < scenarioButtons.Length; i++) scenarioButtons[i].Help = ScenarioHelp[i];

            body.Section(AvIcon.Cloud, "STATE", "Hold a state. Clouds fade into it; fog, light and wind follow.");
            var stateSpecs = new AvControl.Spec[States.Length];
            for (int i = 0; i < States.Length; i++)
            {
                WeatherRegimeType state = States[i];
                stateSpecs[i] = new AvControl.Spec(StateNames[i], () => weather.HoldState(state));
            }
            stateButtons = body.Add(new AvButtons(body.Content, stateSpecs)).Controls;
            for (int i = 0; i < stateButtons.Length; i++)
                stateButtons[i].Help = "Hold " + RegimeSnapshot.FromType(States[i]).Name.ToLowerInvariant() + ".";
            changingButton = body.Add(new AvButtons(body.Content, new[]
            {
                new AvControl.Spec("CHANGING WEATHER", () => weather.ResumeChanging()),
            })).Controls[0];
            changingButton.Help = "Resume the weather stepping one state every few minutes, from the state on screen.";

            body.Section(AvIcon.Bolt, "SET-PIECES", "Force formations on in any sky; they build over ~20 s.");
            var setSpecs = new AvControl.Spec[SetBits.Length];
            for (int i = 0; i < SetBits.Length; i++)
            {
                byte bit = SetBits[i];
                setSpecs[i] = new AvControl.Spec(SetNames[i], () => weather.SetForcedSets((byte)(weather.ForcedSets ^ bit)));
            }
            setButtons = body.Add(new AvButtons(body.Content, setSpecs)).Controls;
            for (int i = 0; i < setButtons.Length; i++) setButtons[i].Help = SetHelp[i];
            AvControl[] placement = body.Add(new AvButtons(body.Content, new[]
            {
                new AvControl.Spec("PLACE 30 KM AHEAD", () => weather.PlaceAhead(30000f)),
                new AvControl.Spec("PLACE HERE", () => weather.PlaceAhead(0f)),
                new AvControl.Spec("DEFAULT PLACE", () => weather.ClearPlacement()),
                new AvControl.Spec("RE-ROLL LAYOUT", () => weather.RerollLayout()),
            })).Controls;
            placeButton = placement[0]; unplaceButton = placement[2]; rerollButton = placement[3];
            placement[0].Help = "Stand the storm eye and lenticulars 30 km ahead of you (lenticulars sit beside the eye).";
            placement[1].Help = "Centre the storm eye on you, so you are in the eye.";
            placement[2].Help = "Put console set-pieces back at their default spots.";
            placement[3].Help = "New sites for every cloud group, cell and set-piece. The weather state stays.";

            body.Section(AvIcon.Refresh, "FRONT", "Turn the frontal boundary in BROKEN and worse skies.");
            AvControl[] frontControls = body.Add(new AvButtons(body.Content, new[]
            {
                new AvControl.Spec("ROTATE 45°", () => weather.TurnFront(1)),
                new AvControl.Spec("FLIP", () => weather.TurnFront(4)),
            })).Controls;
            turnButton = frontControls[0]; flipButton = frontControls[1];
            turnButton.Help = "Turn the front's line by 45 degrees.";
            flipButton.Help = "Swap which side of the map is under the deck.";
            frontRow = body.Add(new AvRow(body.Content));

            body.Section(AvIcon.CloudRain, "RAIN", "Force precipitation, or leave it to the weather.");
            var rainSpecs = new AvControl.Spec[RainValues.Length];
            for (int i = 0; i < RainValues.Length; i++)
            {
                float? rain = RainValues[i];
                rainSpecs[i] = new AvControl.Spec(RainNames[i], () => weather.SetForcedRain(rain));
            }
            rainButtons = body.Add(new AvButtons(body.Content, rainSpecs)).Controls;
            for (int i = 0; i < rainButtons.Length; i++)
                rainButtons[i].Help = "Rain: " + RainNames[i].ToLowerInvariant() + ".";

            hitList.AddRange(window.Root.GetComponentsInChildren<AvHit>(true));
        }

        // ---- Live state ----------------------------------------------------------------------

        private void Refresh()
        {
            if (weather == null) return;
            bool host = weather.CanCommand;
            bool held = weather.IsManualOverride;
            WeatherRegimeType shown = weather.ShownState;
            float next = weather.NextChangeIn();
            string mode = held ? "HELD BY THE CONSOLE"
                : next < 0f ? "HELD WEATHER"
                : "CHANGING · NEXT STEP IN " + AvNum.Clock(next);
            stateReadout.Set(RegimeSnapshot.FromType(shown).Name, "STATE", mode);

            window.Footer.Set(host
                ? "Host controls — every player sees the same sky."
                : "Read only — only the host can change the weather.", AvState.Info);

            WeatherField field = weather.Field;
            string sets = "";
            for (int i = 0; field != null && i < field.SuperstructureCount; i++)
            {
                Superstructure s = field.SuperstructureAt(i);
                int slot = System.Array.IndexOf(SetBits, s.Set);
                string name = slot >= 0 ? SetNames[slot] : "STORM";
                float km = Mathf.Sqrt(s.X * s.X + s.Z * s.Z) / 1000f;
                sets += (sets.Length > 0 ? "  ·  " : "") + name + " " + AvNum.Fixed(km, 0) + " KM " + AvNum.Percent(s.Strength);
            }
            setsRow.Set("SET-PIECES", "", sets.Length > 0 ? sets : "No storm set-pieces in this sky.", AvState.Info);

            for (int i = 0; i < stateButtons.Length; i++)
            {
                stateButtons[i].Interactable = host;
                stateButtons[i].Latched = held && States[i] == shown;
            }
            changingButton.Interactable = host;
            changingButton.Latched = !held && next >= 0f;
            byte forced = weather.ForcedSets;
            for (int i = 0; i < setButtons.Length; i++)
            {
                setButtons[i].Interactable = host;
                setButtons[i].Latched = (forced & SetBits[i]) != 0;
            }
            rerollButton.Interactable = host;
            for (int i = 0; i < scenarioButtons.Length; i++) scenarioButtons[i].Interactable = host;
            placeButton.Interactable = host;
            unplaceButton.Interactable = host && weather.HasAnchor;
            turnButton.Interactable = host;
            flipButton.Interactable = host;
            int turn = weather.FrontTurn;
            bool splitSky = field != null && field.Split.Amount > 0f;
            string frontStatus = (splitSky ? "Front across the map" : "No front in this sky (broken and worse)") +
                (turn != 0 ? "  ·  turned " + AvNum.Fixed(turn * 45, 0) + "°" : "") +
                ((weather.ForcedSets & Superstructures.FogBankSet) != 0 ? "  ·  fog bank on" : "");
            frontRow.Set("STATUS", frontStatus, "", AvState.Info);
            float? rainNow = weather.ForcedRainIntensity;
            for (int i = 0; i < rainButtons.Length; i++)
            {
                rainButtons[i].Interactable = host;
                rainButtons[i].Latched = RainValues[i].HasValue == rainNow.HasValue &&
                    (!rainNow.HasValue || Mathf.Abs(RainValues[i].Value - rainNow.Value) < 0.05f);
            }
        }
    }
}
