using NOAvionics;
using System.Collections.Generic;
using BoscaliSummer.Modules.Weather.Domain;
using BoscaliSummer.Modules.Weather.Runtime;
using Rewired;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace BoscaliSummer.Modules.Weather.Presentation
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
        private const float Height = 624f;   // full set-piece status and forecast fit without scrolling
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
            "SQUALL LINE: a storm wall on the upwind horizon with a tiered shelf cloud on its leading edge. Click again to return it to AUTO; the weather may still generate it.",
            "SUPERCELL: a leaning cumulonimbus with a downwind anvil and mammatus pouches under it. Click again to return it to AUTO; the weather may still generate it.",
            "DISTANT CB: a lone cumulonimbus far out on the horizon. Click again to return it to AUTO; the weather may still generate it.",
            "STORM EYE: a hurricane eyewall with a clear upper eye and spiral cloud bands outside. Low cloud can remain under the eye. Click again to remove it.",
            "LENTICULARS: smooth stacked lens clouds in a mountain-wave train, riding the wind. Click again to remove them.",
            "SEA FOG: a fog bank over the sea and valleys up to about 300 m. Click again to clear it.",
        };
        private static readonly WeatherScenario[] Scenarios =
        {
            WeatherScenario.HurricaneEye, WeatherScenario.SquallAssault,
            WeatherScenario.MountainWave, WeatherScenario.SeaFog,
            WeatherScenario.FrontalPassage, WeatherScenario.ResetAll,
        };
        private static readonly string[] ScenarioNames = { "HURRICANE EYE", "SQUALL ASSAULT", "MOUNTAIN WAVE", "SEA FOG", "FRONTAL PASSAGE", "RESET ALL" };
        private static readonly string[] ScenarioHelp =
        {
            "Storm, with you in the eye of a hurricane: walls all around, clear sky above.",
            "Rain squall with a stationary squall line and supercell on the horizon.",
            "Fair skies with lenticular clouds standing 25 km ahead of you.",
            "Clear sky over a fog bank in the sea and valleys.",
            "Overcast: a front across the map, deck behind it, open sky ahead.",
            "Back to changing weather: clear forced rain, set-pieces, placement and front turn.",
        };
        private static readonly float?[] RainValues = { null, 0f, 0.4f, 1f };
        private static readonly string[] RainNames = { "AUTO", "DRY", "LIGHT", "HEAVY" };
        private static readonly string[] RainHelp =
        {
            "AUTO: the weather state decides the rain, as it does when nobody is steering.",
            "DRY: no rain whatever the weather state says. The cloud stays.",
            "LIGHT: force a light rain (40%) under any sky.",
            "HEAVY: force the heaviest rain (100%) under any sky.",
        };

        private WeatherManager weather;
        private AvWindow window;
        private AvReadout stateReadout;
        private EnvStateRow stateRow;
        private EnvOutlookStrip outlook;
        private AvRow setsRow;
        private AvRow frontRow;
        private AvControl[] stateButtons;
        private AvControl[] setButtons;
        private AvControl[] scenarioButtons;
        private AvControl[] rainButtons;
        private AvControl placeButton, hereButton, unplaceButton, turnButton, flipButton, changingButton, rerollButton;
        private readonly List<AvHit> hitList = new List<AvHit>(32);
        private readonly List<AvHelpTip> tipList = new List<AvHelpTip>(16);
        private AvHit hovered;
        private AvHelpTip tipHovered;
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
            if (tipHovered != null) { tipHovered.OnPointerExit(Pointer()); tipHovered = null; }
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
            SetTip(mouse);
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

        // Read-only parts (rows, the outlook strip) carry help tips but no AvHit; the disabled Rewired mouse never hovers them.
        private void SetTip(Vector2 mouse)
        {
            AvHelpTip under = null;
            for (int i = 0; i < tipList.Count; i++)
            {
                AvHelpTip t = tipList[i];
                if (t != null && t.isActiveAndEnabled && t.GetComponent<AvHit>() == null &&
                    RectTransformUtility.RectangleContainsScreenPoint((RectTransform)t.transform, mouse, null))
                { under = t; break; }
            }
            if (under == tipHovered) return;
            if (tipHovered != null) tipHovered.OnPointerExit(Pointer());
            tipHovered = under;
            if (tipHovered != null) tipHovered.OnPointerEnter(Pointer());
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
            // This standalone console fits at 1:1 on 720p; the shared window's 1080p
            // reference otherwise shrinks its 11 px micro text below a readable size.
            CanvasScaler scaler = window.Root.parent.GetComponent<CanvasScaler>();
            if (scaler != null && window.Root.parent.parent == uiRoot)
            {
                scaler.referenceResolution = new Vector2(1280f, 720f);
                scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
            }
            window.Closed += OnWindowClosed;
            window.CloseControl.Help = "Close weather console (Esc or Ctrl+O).";

            AvFlow body = window.Body;
            body.ViewportHeight = Height - 30f - AvGridTokens.Footer;   // title bar and footer are fixed chrome

            // NOW: the state name and its mode beside the lit icons; set-pieces and front status share the next line.
            stateReadout = new AvReadout(body.Content);
            stateRow = new EnvStateRow(body.Content);
            stateRow.SetHelp("NOW: SUN, CLOUD, RAIN, STORM and MIST light up for the state on screen. Amber or red means bad flying weather.");
            body.Row(stateReadout, stateRow);
            setsRow = new AvRow(body.Content);
            frontRow = new AvRow(body.Content);
            setsRow.Help = "SET-PIECES: the storm scenery active right now, each with its distance from the centre of the map and its strength.";
            frontRow.Help = "FRONT: whether a frontal boundary splits the sky, how far you have turned it, and whether the sea fog bank is on.";
            body.Row(setsRow, frontRow);

            body.Section(AvIcon.PlayerPlay, "SCENARIOS", "");
            var scenarioSpecs = new AvControl.Spec[Scenarios.Length];
            for (int i = 0; i < Scenarios.Length; i++)
            {
                WeatherScenario scenario = Scenarios[i];
                scenarioSpecs[i] = new AvControl.Spec(ScenarioNames[i], () => weather.ApplyScenario(scenario));
            }
            scenarioButtons = body.Add(new AvButtons(body.Content, scenarioSpecs)).Controls;
            for (int i = 0; i < scenarioButtons.Length; i++) scenarioButtons[i].Help = ScenarioHelp[i];

            body.Section(AvIcon.Cloud, "STATE", "HOLD ONE, OR LET IT CHANGE");
            // Seven states and AUTO on one line: AUTO (resume changing weather) is the last cell.
            var stateSpecs = new AvControl.Spec[States.Length + 1];
            for (int i = 0; i < States.Length; i++)
            {
                WeatherRegimeType state = States[i];
                stateSpecs[i] = new AvControl.Spec(StateNames[i], () => weather.HoldState(state));
            }
            stateSpecs[States.Length] = new AvControl.Spec("AUTO", () => weather.ResumeChanging());
            AvControl[] stateRowControls = body.Add(new AvButtons(body.Content, stateSpecs)).Controls;
            stateButtons = new AvControl[States.Length];
            System.Array.Copy(stateRowControls, stateButtons, States.Length);
            changingButton = stateRowControls[States.Length];
            for (int i = 0; i < stateButtons.Length; i++)
                stateButtons[i].Help = "HOLD " + RegimeSnapshot.FromType(States[i]).Name.ToUpperInvariant() +
                    ": the sky fades to this state and stays there until you pick another or press AUTO.";
            changingButton.Help = "AUTO: resume changing weather. The sky steps one state every few minutes, starting from the state on screen.";

            body.Section(AvIcon.Bolt, "SET-PIECES & FRONT", "LIT = FORCED ON");
            var setSpecs = new AvControl.Spec[SetBits.Length];
            for (int i = 0; i < SetBits.Length; i++)
            {
                byte bit = SetBits[i];
                setSpecs[i] = new AvControl.Spec(SetNames[i], () => weather.SetForcedSets((byte)(weather.ForcedSets ^ bit)));
            }
            setButtons = body.Add(new AvButtons(body.Content, setSpecs)).Controls;
            for (int i = 0; i < setButtons.Length; i++) setButtons[i].Help = SetHelp[i];
            // Placement (4) and front (2) share one line of six.
            AvControl[] placement = body.Add(new AvButtons(body.Content, new[]
            {
                new AvControl.Spec("PLACE 30 KM AHEAD", () => weather.PlaceAhead(30000f)),
                new AvControl.Spec("PLACE HERE", () => weather.PlaceAhead(0f)),
                new AvControl.Spec("DEFAULT PLACE", () => weather.ClearPlacement()),
                new AvControl.Spec("RE-ROLL LAYOUT", () => weather.RerollLayout()),
                new AvControl.Spec("ROTATE 45°", () => weather.TurnFront(1)),
                new AvControl.Spec("FLIP", () => weather.TurnFront(4)),
            })).Controls;
            placeButton = placement[0]; hereButton = placement[1]; unplaceButton = placement[2]; rerollButton = placement[3];
            turnButton = placement[4]; flipButton = placement[5];
            placement[0].Help = "PLACE 30 KM AHEAD: stand the storm eye and lenticulars 30 km ahead of you (lenticulars sit beside the eye).";
            placement[1].Help = "PLACE HERE: centre the storm eye on you, so you start inside the eye.";
            placement[2].Help = "DEFAULT PLACE: put console set-pieces back at their default spots.";
            placement[3].Help = "RE-ROLL LAYOUT: new cloud, cell and default set-piece sites. The state and explicit placement stay.";
            turnButton.Help = "ROTATE 45: turn the front's line by 45 degrees, so the deck arrives from another side.";
            flipButton.Help = "FLIP: swap which side of the map is under the deck and which is open sky.";

            body.Section(AvIcon.CloudRain, "RAIN", "FORCE");
            var rainSpecs = new AvControl.Spec[RainValues.Length];
            for (int i = 0; i < RainValues.Length; i++)
            {
                float? rain = RainValues[i];
                rainSpecs[i] = new AvControl.Spec(RainNames[i], () => weather.SetForcedRain(rain));
            }
            rainButtons = body.Add(new AvButtons(body.Content, rainSpecs)).Controls;
            for (int i = 0; i < rainButtons.Length; i++) rainButtons[i].Help = RainHelp[i];

            body.Section(AvIcon.Clock, "NEXT 60 MIN", "");
            outlook = body.Add(new EnvOutlookStrip(body.Content, WeatherForecast.DefaultOffsetsMinutes.Length));
            outlook.SetHelp("NEXT 60 MIN: the weather state now and at +5, +10, +15, +30 and +60 minutes. Each bar shows rain intensity at your current location, or the forced rain level.");

            hitList.AddRange(window.Root.GetComponentsInChildren<AvHit>(true));
            tipList.AddRange(window.Root.GetComponentsInChildren<AvHelpTip>(true));
        }

        // ---- Live state ----------------------------------------------------------------------

        private void Refresh()
        {
            if (weather == null) return;
            bool host = weather.CanCommand;
            bool held = weather.IsManualOverride;
            WeatherRegimeType shown = weather.ShownState;
            float next = weather.NextChangeIn();
            string mode = held ? "HELD"
                : next < 0f ? "HELD // MISSION"
                : "CHANGING // NEXT " + AvNum.Clock(next);
            stateReadout.Set(RegimeSnapshot.FromType(shown).Name, "STATE", mode);
            int lights = EnvStateRow.LitFor(shown);
            if (weather.ForcedRainIntensity.HasValue)
                lights = weather.ForcedRainIntensity.Value > 0f ? lights | EnvStateRow.Rain : lights & ~EnvStateRow.Rain;
            if ((weather.ForcedSets & Superstructures.FogBankSet) != 0) lights |= EnvStateRow.Mist;
            stateRow.Set(lights, WeatherEnvView.RegimeState(shown));

            window.Footer.Set(host ? "HOST // ALL PLAYERS" : "READ ONLY", AvState.Info);

            WeatherField field = weather.Field;
            string sets = "";
            for (int i = 0; field != null && i < field.SuperstructureCount; i++)
            {
                Superstructure s = field.SuperstructureAt(i);
                int slot = System.Array.IndexOf(SetBits, s.Set);
                string name = slot >= 0 ? SetNames[slot] : "STORM";
                Vector2 delta = new Vector2(s.X, s.Z) - weather.FieldPosition;
                float km = delta.magnitude / 1000f;
                sets += (sets.Length > 0 ? "  ·  " : "") + name + " " + AvNum.Fixed(km, 0) + " KM " + AvNum.Percent(s.Strength);
            }
            setsRow.Set(sets.Length > 0 ? sets : "NO SET-PIECES", "", "", AvState.Info);

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
            hereButton.Interactable = host;
            unplaceButton.Interactable = host && weather.HasAnchor;
            turnButton.Interactable = host;
            flipButton.Interactable = host;
            int turn = weather.FrontTurn;
            bool splitSky = field != null && field.Split.Amount > 0f;
            int frontHeading = field != null ? ((Mathf.RoundToInt(field.Split.Heading) % 360) + 360) % 360 : 0;
            string frontStatus = (splitSky ? "FRONT · HDG " + frontHeading.ToString("000") + "°" : "NO FRONT") +
                (turn != 0 ? "  ·  TURN " + AvNum.Fixed(turn * 45, 0) + "°" : "") +
                ((weather.ForcedSets & Superstructures.FogBankSet) != 0 ? "  ·  FOG" : "");
            frontRow.Set(frontStatus, "", "", AvState.Info);
            float? rainNow = weather.ForcedRainIntensity;
            for (int i = 0; i < rainButtons.Length; i++)
            {
                rainButtons[i].Interactable = host;
                rainButtons[i].Latched = RainValues[i].HasValue == rainNow.HasValue &&
                    (!rainNow.HasValue || Mathf.Abs(RainValues[i].Value - rainNow.Value) < 0.05f);
            }

            ForecastStep[] steps = weather.GetForecastTimeline();
            if (steps != null)
            {
                int[] offsets = WeatherForecast.DefaultOffsetsMinutes;
                for (int i = 0; i < steps.Length && i < offsets.Length; i++)
                {
                    WeatherRegimeType type = steps[i].Regime.Type;
                    AvState st = type == WeatherRegimeType.Storm ? AvState.Danger
                        : steps[i].RainProbability > 0.2f ? AvState.Caution : WeatherEnvView.RegimeState(type);
                    outlook.Set(i, i == 0 ? "NOW" : "+" + offsets[i], i == 0, type, steps[i].Regime.Code, steps[i].RainProbability, st);
                }
            }
        }
    }
}
